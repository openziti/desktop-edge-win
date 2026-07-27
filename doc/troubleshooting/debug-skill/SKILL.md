---
name: debug ziti-desktop-edge-win
description: Analyze a feedback.zip from Ziti Desktop Edge for Windows (ZDEW). Handles both single feedback zips and aggregated zips (a zip containing multiple feedback zips). Fetches the bundle from a support ticket when it isn't already local, dates the incident, checks whether the logs even cover it, and produces a diagnostic report.
---

## Invocation

```
/debug ziti-desktop-edge-win [ticket number or zip path]
```

## Before you start

**Everything you conclude is bounded by the log window.** ZDEW prunes its own logs on startup
(`delete_older_logs()`), so a bundle collected days after an incident frequently does not contain the
incident. Step 4 exists to catch that before you write a confident wrong answer. If the reported event
predates the oldest log, say so in the report — "no evidence of X" and "X is outside the log window" are
different findings and only one of them is honest.

**Absence of a log line proves nothing until you check three things:** that the log window covers the
event, that the log level in effect would have emitted that line, and that you grepped every rolled file
(`*.log` *and* `*.log.YYYYMMDD0000.log`).

**Paths, not `cd`.** Pass absolute paths to every command. The Bash tool's working directory persists
across calls, so a bare `cd` moves the operator's terminal out from under them.

**If you extract into a gitignored directory** (a build or scratch dir, `node_modules/`, anything matched by
`.gitignore`), the Grep tool returns **zero matches silently**. Either extract somewhere tracked, or use
`grep` through Bash for the whole analysis. Do not conclude "no hits" from a Grep tool call against an
ignored path.

**The bundle contains sensitive material.** Treat it as confidential customer data:

- `service/*.json` identity files hold **enrollment tokens, private keys, and client certificates** — a
  live credential for that customer's network.
- `service/*.ziti` dumps embed session certificates, session tokens, and API session state.
- `systeminfo.txt`, `tasklist.txt`, `netstat.txt`, `dnsCache.txt` and the UI logs carry usernames,
  hostnames, domain names, internal IPs, installed software, and the customer's internal service names.

Consequences for how you work: never paste raw log excerpts into a public issue tracker without redacting
hostnames, usernames, controller URLs, identity names, and tokens. Never include an identity file's contents
in a report. When quoting a log line as evidence, quote the smallest part that makes the point. Keep the
extracted tree out of any directory that gets committed, and say so when you tell the operator where it
went.

## Steps

### 1. Locate the bundle

In order of preference:

1. A zip path passed in the invocation.
2. A `.zip` in the current working directory. If several, list them and ask which to use.
3. **A ticket number or ticket URL** — fetch the attachment from whatever ticket system is wired up for this
   session (check the available tools for a support-desk integration; otherwise ask the operator to download
   it). List the attachments first and let the operator choose: a ZDEW feedback bundle is a timestamped
   `YYYY-MM-DD_HHMMSS.zip`, typically 100 KB–5 MB. Small `image001.png` / `image00N.jpg` attachments in the
   0.5–20 KB range are email-signature noise from the mail thread — never download those by default, but do
   mention any image over ~15 KB in case it's a screenshot of the customer's settings.

Read the ticket's comments while you're there, including any internal or private ones. Engineers often leave
the hypothesis you're supposed to be testing — and the diagnostic they already asked the customer for — in a
comment the customer never sees. Step 5 depends on knowing what was asked.

If there is no zip and no ticket, say so and stop.

### 2. Decide where things go, and tell the operator

Two destinations, and don't mix them:

- **Extracted logs** — bulky, sensitive, disposable, never committed. Put them in a scratch or build
  directory that is already gitignored, or in the session scratchpad.
- **The report** — a document. Put it in the working directory alongside any other notes for that ticket
  (e.g. `<ticket>-analysis.md`), not buried inside the extracted tree.

### 3. Extract

```bash
unzip -o -q /abs/path/bundle.zip -d /abs/path/scratch/<ticket>/
ls -laR /abs/path/scratch/<ticket>/
```

A **single feedback capture** extracts directly into:

- `dnsCache.txt`, `externalIP.txt`, `ipconfig.all.txt`, `netstat.txt`, `network-routes.txt`,
  `NrptPolicy.txt`, `NrptRule.txt`, `systeminfo.txt`, `tasklist.txt`
- `service/` — `*.ziti` dump file(s) and `ziti-tunneler.log*`
- `UI/` — `ZitiDesktopEdge.*.log`
- `ZitiMonitorService/` — `ZitiUpdateService.*.log`

An **aggregated bundle** extracts into a folder of timestamped `.zip` files (`2026-04-09_091225.zip`, …) —
the customer sent multiple captures. Extract each into its own sibling folder and run steps 4–12 per
capture:

```bash
unzip -o -q /abs/path/scratch/<ticket>/2026-04-09_091225.zip -d /abs/path/scratch/<ticket>/2026-04-09_091225/
```

An empty `NrptPolicy.txt` is normal (it means no NRPT policy is applied), not a missing file.

### 4. Date the machine, then check log coverage

**This is the step that decides whether the rest of the analysis is meaningful.** Do it before any log
reading.

```bash
grep -iE "^(Host Name|OS Name|OS Version|Original Install Date|System Boot Time|System Model|System Type)" \
  /abs/path/capture/systeminfo.txt
```

- **`Original Install Date` is reset by an in-place Windows upgrade.** A date days or weeks old on a
  machine the user has owned for years *is* the upgrade date. For any "settings lost / identities lost
  after an update" ticket this is the single most useful line in the bundle.
- **`OS Version` build number** identifies the Windows release: `26200` = 11 25H2, `26100` = 11 24H2,
  `22631` = 11 23H2, `22621` = 11 22H2, `19045` = 10 22H2.
- **`System Boot Time`** distinguishes a reboot from a manual service restart in step 6.

Then bound the window:

```bash
ls -1 /abs/path/capture/service /abs/path/capture/UI /abs/path/capture/ZitiMonitorService
grep -hoE "delete_older_logs\(\) Deleting old log file .*" /abs/path/capture/service/*.log | sort -u
```

Write down the oldest surviving timestamp per log directory and compare it against the customer's stated
incident date. **State the gap explicitly in the report.** The pruning lines tell you exactly which days
were destroyed and when.

Note that the three log directories roll independently — `UI/` often reaches further back than `service/`,
and the monitor logs further still. If the tunneler log for the incident day is gone, check whether the
monitor or UI log for that day survived; they carry less detail but they carry timestamps.

### 5. Verify what was already asked for

From the ticket comments (step 1), list every diagnostic action support requested — restart the tunneler,
enable trace logging, reproduce and recapture — and confirm in the logs whether it actually happened.

```bash
# was trace/verbose logging actually on?
grep -hoE "ziti_log_set_level\(\) set log level: .*" /abs/path/capture/service/ziti-tunneler*.log*
# per-level line counts, to see what detail you actually have
grep -hoE "\] +(TRACE|VERBOSE|DEBUG|INFO|WARN|ERROR) " /abs/path/capture/service/ziti-tunneler*.log* \
  | sort | uniq -c
```

Customers routinely send logs without performing the requested step. Reporting "the customer never
restarted it, so the question we asked is still unanswered" is often the most actionable line in the whole
report — and it stops the team from re-analyzing the same bundle.

### 6. Service lifecycle

Banners first:

```bash
grep -h "service begins\|service ends" /abs/path/capture/service/ziti-tunneler*.log*
```

Then process starts independently, because a banner can be missing when a log rolls mid-startup:

```bash
grep -hoE "^\[[^]]+\].*ziti_log_init\(\) Ziti C SDK version .*starting at \([^)]+\)" \
  /abs/path/capture/service/ziti-tunneler*.log*
```

Build a chronological table. Collapse `service ends` immediately followed by `service begins` into one
**Restart** row.

| Log File | Event | Timestamp (UTC) | Notes |
|---|---|---|---|
| ... | Restart / Start / Stop | ... | ... |

Interpretation:

- **Restart (clean)** — usually an update or intentional restart. Not worth reporting individually unless
  there are many in a short period.
- **Start with no preceding stop** — the process died without a clean shutdown. Compare the timestamp
  against `System Boot Time` from step 4 first: if they're within a minute, it's a reboot, and the missing
  `service ends` is expected because the OS killed the service. That is benign and should be labeled as a
  reboot, not as an ungraceful exit.
- **No events at all** — the service has run continuously since before the oldest log. Healthy; say so.
- **More starts than stops** — count the unexplained ones after removing reboots.

Do **not** cross-check the most recent `service begins` against `uptime[Ns]` in the `.ziti` dump and expect
them to match. `uptime` there is **per-identity context uptime**, not process uptime; a context reloads on
re-auth, on config change, and on identity enable/disable. A dump showing 3 days of context uptime under a
service that started 4 days ago is normal. Only `ziti_log_init` and the banners date the process.

Then the monitor service:

```bash
grep -h "OnShutdown was called\|OnStop was called\|OnStart\|aliveness check" \
  /abs/path/capture/ZitiMonitorService/*.log
```

- `ziti-monitor OnShutdown was called` just before a gap — the machine rebooted. Benign.
- `ziti-monitor OnStop was called` — intentional stop, typically an update. Check whether the version
  changes on the next start.
- `aliveness check ... appears blocked and has been for N times. AlivenessChecksBeforeAction:12` — **report
  the peak N reached, and whether it ever hit the threshold.** Partial counts that reset (3 of 12 during a
  post-boot storm) are noise; only reaching 12 means the monitor killed the tunneler. Treating any
  aliveness warning as a stall produces false findings — this fires routinely during startup and
  resume-from-sleep.

### 7. Crash and stall markers

```bash
ls -la /abs/path/capture/*.dmp /abs/path/capture/service/*.dmp 2>/dev/null
```

- `ziti-edge-tunnel.stalled.dmp` in the capture root → **STALLED**
- `service/ziti-edge-tunnel.crash.dmp` → **CRASHED**
- neither → **no crash/stall markers**

For an aggregated bundle, glob `**/*.dmp` once across the whole ticket folder so you can see which
captures have them.

### 8. Config, identities, and the Windows.old restore path

Read this section for any ticket about **settings reverting, identities disappearing, or IP range changing
after a Windows update.** It is a known ZDEW defect path, not a customer error.

```bash
grep -hoE "^\[[^]]+\].*load_tunnel_status_from_file\(\) Loading config file from .*" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -h "Restored old identity from the backup path\|Removing old identity from the backup path\|failed to copy backup identity file" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -hoE "load_identities\(\) loading identity file: .*" /abs/path/capture/service/ziti-tunneler*.log*
```

What the code does (`ziti-tunnel-sdk-c`, `programs/ziti-edge-tunnel/ziti-edge-tunnel.c`,
`move_config_from_previous_windows_backup()`):

- Runs **unconditionally on every startup**, over `%SystemDrive%\Windows.~BT\...` and
  `%SystemDrive%\Windows.old\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry`.
- Copies **every regular file** in that folder despite the "identity" naming — `config.json` included —
  and overwrites the live file without an exclusive flag.
- Runs **after** the config has already been read and after the IP range has been resolved from it. So a
  restored `config.json` is never read on the run that restores it, and the next save writes in-memory
  state back over it. On a fresh post-upgrade profile that state is defaults. Net effect: **identities come
  back, the config does not.**
- Deletes the backup file after a successful copy, but does not check or log whether the delete succeeded.
  `Windows.old` is TrustedInstaller-owned; if the delete fails, the copy repeats on every startup.

So: `Restored old identity...` lines present → the restore ran, name the files. Absent → note it, and
**immediately restate the step-4 coverage caveat**, because `Windows.old` is pruned by Windows roughly 10
days post-upgrade and the startup that mattered is usually in a deleted log. Absent-and-out-of-window is
not evidence against this path.

Worth asking the customer for directly (a directory listing is enough):

```
C:\Windows.old\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry
C:\Windows.~BT\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry
C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\config.json
```

A colleague who upgraded more recently is a better evidence source than a customer whose `Windows.old` has
already been pruned.

### 9. Tun adapter, DNS range, and routes

```bash
grep -hoE "seed_dns\(\) DNS configured with range .*" /abs/path/capture/service/ziti-tunneler*.log* | sort -u
grep -hoE "set_dns\(\) executing .*" /abs/path/capture/service/ziti-tunneler*.log* | sort -u
awk '/ziti-tun0/,/^$/' /abs/path/capture/ipconfig.all.txt
```

- `seed_dns() DNS configured with range A - B (N ips)` is the **authoritative range for that startup**.
- **The ZDEW default is `100.64.0.1/10`.** Anything else came from `config.json`. This is how you tell "the
  setting was lost and fell back to default" from "the setting is present but not what the customer
  remembers." Both are real outcomes with different fixes, and the customer cannot tell them apart.
- A `/16` where the customer expected `/24` is a different failure from a full reset to `100.64.0.1/10` —
  quote the actual mask from `ipconfig.all.txt` rather than paraphrasing the customer.

**Attribute every route to its interface before blaming ZDE.** This is the most common analytical error in
these tickets:

```bash
grep -E "^(Ethernet|Wireless|Unknown|PPP|Tunnel) adapter|^   (Description|IPv4 Address|Subnet Mask)" \
  /abs/path/capture/ipconfig.all.txt
grep -nE "(^| )100\.(6[4-9]|[7-9][0-9]|1[0-1][0-9]|12[0-7])\.|(^| )100\.100\." \
  /abs/path/capture/network-routes.txt
```

`100.64.0.0/10` is CGNAT space and ZDE is not its only occupant. Zscaler Client Connector, Netskope,
GlobalProtect, Tailscale, and corporate dock/USB NICs all appear there. In `network-routes.txt`, columns
are `destination / mask / gateway / interface / metric` — a route whose **interface** is not the ziti
adapter's IP is not ZDE's route, whatever its destination looks like. Match the interface column back to an
adapter in `ipconfig.all.txt` before attributing anything.

### 10. Errors AND warnings

Scan every rolled log, both levels. **Do not scan `ERROR` alone** — the highest-volume real defects in ZDEW
surface at `WARN`, and an ERROR-only pass will miss them entirely.

Counts per file first, to spot spikes:

```bash
grep -c "ERROR" /abs/path/capture/service/ziti-tunneler*.log*
grep -c "WARN"  /abs/path/capture/service/ziti-tunneler*.log*
```

Then bucket the messages by shape — collapse bracketed values and digits so variants group together:

```bash
grep -h "ERROR" /abs/path/capture/service/ziti-tunneler*.log* \
  | sed -E 's/^\[[^]]+\][[:space:]]+ERROR //' \
  | sed -E 's/\[[^]]*\]/[]/g' \
  | sed -E 's/[0-9]+/N/g' \
  | sort | uniq -c | sort -rn | head -25
```

Repeat with `WARN` (and `s/[[:space:]]+WARN //`). For any high-count warning, check whether it's periodic —
a fixed interval means a retry loop, not a burst:

```bash
grep -h "<the warning text>" /abs/path/capture/service/ziti-tunneler*.log* | head -20
```

#### Category 1: network / control plane

The root layer. If these are elevated, downstream service failures are *expected* and not independently
significant.

| Pattern | Meaning |
|---|---|
| `failed to connect to controller due to not authorized` | OIDC token expired; client re-authenticating |
| `failed to connect to controller due to failed to authenticate` | Harder auth failure after token expiry |
| `failed to get identity_data: no api session token set` | No valid session during the re-auth window |
| `failed to get current edge routers: ... UNAUTHORIZED` | Can't fetch routers — no valid session |
| `ch[N] disconnected from edge router[...]` | Router channel dropped |
| `failed to get identity_data: unknown node or service` | **DNS resolution failed for the controller hostname** (libuv `-3008`) |
| `Unknown system error -10013` | Winsock `WSAEACCES` — a local policy, firewall, or ZTNA client blocked the socket |
| `latency_timeout() ... closing channel` | No traffic before the latency probe; channel torn down |

**Baseline:** the `not authorized` cluster bursts around midnight UTC on the nightly OIDC refresh — ~48–52
per daily log is normal. Elevated *throughout* the day means the controller was unreachable or rejecting
sessions for an extended period.

**Resume-from-sleep is its own baseline.** A cluster of `unknown node or service` / `-10013` /
`not authorized` within a minute or two of `Received power resume event` is the NIC and DNS not being ready
while the tunneler retries. On a machine also running a ZTNA client (Zscaler et al.), name resolution can
fail until that client's own tunnel is up. Not a ZDE defect — check for the resume event before escalating:

```bash
grep -hoE "^\[[^]]+\].*endpoint_status_change\(\) Received power (resume|suspend) event" \
  /abs/path/capture/service/ziti-tunneler*.log*
```

#### Category 2: service / dial

Application-level failures. **Always check category 1 first.** If network errors are normal but dial
failures are elevated, the problem is service-side — posture policies, terminator health, service config.

| Pattern | Meaning |
|---|---|
| `ziti context is not authenticated, cannot connect to service[...]` | Dial during an auth gap — expected if category 1 is elevated |
| `ziti dial failed: invalid state` / `connection is closed` | Paired with the above |
| `ziti_write() failed: invalid state` | Write on a torn-down connection — expected during auth refresh |
| `on_tcp_client_err() ... err=-14, terminating connection` | Client-side TCP reset — routine |
| `exceeded maximum retries creating circuit ... timeout waiting for message reply` | Circuit creation failing at the router — real if category 1 is quiet |

#### Category 3: routing and NRPT (WARN level — easy to miss)

| Pattern | Meaning |
|---|---|
| `refresh_routes() failed to create exclusion route[IP]: 1168(...)` | Could not install the bypass route for a controller/router public IP. Periodic repetition means it never succeeded. On a machine with another VPN/ZTNA client owning the default route, missing exclusion routes are a plausible cause of "overlapping ranges" complaints. |
| `is_nrpt_policies_effective() NRPT policies are ineffective in this system` | ZDEW falls back to interface DNS. Expected on some managed builds; note it when DNS resolution is the complaint. |
| `failed to create route` / `failed to remove route` | Route table contention — check for other tunnel adapters |

#### What to flag

- Network errors elevated all day (not just midnight) → controller/auth disruption; check whether the same
  spike appears in other captures on the same date
- The same spike across multiple captures on one date → infrastructure, not this machine
- Dial failures elevated without matching network errors → service-side
- Any 2x+ day-over-day spike in either category → name the date, and say whether it's isolated or shared
- Any warning repeating at a fixed interval for days → a retry loop that never succeeds; report it with a
  total count even when nothing else is wrong

### 11. Known misleading strings

Do not take these at face value:

- **`1168(The operation completed successfully.`** — ZDEW formats the message text of error 0 next to error
  code `1168`. `1168` is `ERROR_NOT_FOUND`. The line is a **failure**; the trailing text is a formatting
  bug, and the unbalanced parenthesis is part of it.
- **`Unknown system error -10013`** — not unknown: Winsock `WSAEACCES`, permission denied on a socket.
- **`The config file %s cannot be opened due to %s`** — the format arguments are swapped in some versions,
  so the error string and the path appear in the wrong order.
- **`uptime[Ns]`** in a `.ziti` dump — per-identity context, not the process. See step 6.
- **`Unknown adapter ziti-tun0`** in `ipconfig.all.txt` — "Unknown" is just Windows' adapter-type label for
  the Wintun device. Not an error.
- **A missing log line** — see "Before you start". Check coverage and log level before calling it evidence.

### 12. The `.ziti` dump

`service/` holds zero or more `*.ziti` dumps — **one per identity context**, and a machine with a dozen
enrolled identities may still have only one dump. Name which identity each dump covers; do not generalize
one identity's health to the machine.

Header block (first ~14 lines) gives the version inventory — app version, C SDK, tlsuv/OpenSSL, sodium,
libuv, OS build, hostname, and dump time. Put these in the report; it's the first thing engineering asks
for.

**Controllers** — `Controller:` or `Controller[HA]:`. Each entry carries `online[Y/N]`; flag any `online[N]`
as **OFFLINE — investigate**. Note the controller version, and whether the deployment is HA (multiple
entries) or single.

**Channels (routers)** — `Channels:` block, each with `connected[Y/N]`, `latency[Nms]`, `connected[Ns]`:

- any `connected[N]` → **DISCONNECTED**
- latency under 100 ms normal; 100–250 ms elevated; over 250 ms → **HIGH LATENCY**
- `connected[Ns]` is channel uptime. Short uptime = recent reconnect. **Before flagging instability, check
  for a power resume or an auth burst at that same moment** (step 10) — all channels reconnecting
  simultaneously points at the local machine waking up, not at the routers. Channels reconnecting at
  *different* times, or one channel that never comes back, is the real signal.
- recent reconnect *plus* elevated latency on the same router is a meaningful instability signal

**Connections** — `Connections:` block with `state`, `service`, `channel`, `sent`, `recv`, `recv_buff`:

- all should be `state[Connected]`
- `recv_buff` should be 0 or near 0; a large value means backpressure or a stalled reader
- note which services are in use and whether the byte counters show real traffic — active connections with
  real volume are strong evidence the data plane works, which narrows a vague "nothing works" complaint
- `idle_time` far larger than `connect_time` across every connection means the user wasn't actually using
  it during the window

**API session** — note `auth_method[...]` (`Legacy` vs OIDC) and `api_session_state[N]`. A `Legacy` auth
method on a controller that also offers OIDC is worth mentioning; the token lifetimes differ and so do the
midnight-refresh baselines in step 10.

### 13. Write the report

Write to the document location chosen in step 2 (the working directory, e.g. `<ticket>-analysis.md`) — not
inside the extracted log tree.

```markdown
# ZDEW Debug Report — Ticket <ticket_number>

**Source zip:** `<filename>` (<size>)
**Captures:** N (single / aggregated)
**Generated:** <date>

---

## <capture_name> — <hostname>

| Item | Value |
|---|---|
| ZDE app | |
| Tunneler SDK | |
| C SDK | |
| tlsuv / OpenSSL | |
| OS | (name, build, release name) |
| Original install date | (= in-place upgrade date, if recent) |
| System boot | |
| Dump taken | |
| Controller | (version, HA or single) |

### Log Window Coverage
Oldest log per directory, the incident date, and whether the window covers it. Name any days destroyed by
`delete_older_logs()`.

### Prior Asks
What support requested, and whether the logs show it was done.

### Crash / Stall Markers

### Service History
Chronological table. Reboots labeled as reboots.

### Config / Identity Handling
Only for settings-loss or identity-loss tickets: what the startup loaded, whether the Windows.old restore
ran, and the coverage caveat if it's silent.

### Tun Adapter / DNS Range / Routes
Configured vs default range, actual adapter IP and mask, and which adapter owns each contested route.

### Error & Warning Analysis
Per-file counts, bucketed messages, categories 1–3, and anything periodic.

### .ziti Analysis
Per identity: controllers, channels, connections, API session.

---

## Summary

The most significant findings across all captures, in one short paragraph.

## What to act on

Numbered, concrete: what to ask the customer, what to file as a bug, what cannot be answered from this
bundle and what capture would answer it.
```

Rules for the report:

- **Separate observation from inference.** Quote the log line, then say what you think it means. A support
  engineer relaying your words to a customer needs to know which is which.
- **Lead with what changed the conclusion**, not with the checklist order.
- **Say plainly when the symptom does not reproduce in the bundle** — and then say what would be needed to
  catch it, rather than padding with healthy-system findings.
- **Every "no evidence of X" carries its coverage caveat** or it will be misread as "X did not happen."
- Keep a healthy-system section short. Three lines confirming channels, connections, and lifecycle are fine
  are worth more than three paragraphs.
- **Assume the report gets forwarded.** Quote the minimum log text needed, and never include identity file
  contents, session tokens, certificates, or full controller URLs. If a finding requires a customer
  hostname or identity name to make sense, use it — but don't paste surrounding lines that add nothing but
  exposure.

After writing, tell the operator the path and the one finding that matters most.
