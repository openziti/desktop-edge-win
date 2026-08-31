---
name: debug ziti-desktop-edge-win
description: Analyze a feedback.zip from Ziti Desktop Edge for Windows (ZDEW). Handles single feedback zips, aggregated zips (a zip containing multiple feedback zips), and fleet tickets (many separate bundles, one per machine). Fetches the bundle from a support ticket when it isn't already local, dates the incident, checks whether the logs even cover it, triages any crash dumps, and produces a diagnostic report.
---

## Invocation

```
/debug ziti-desktop-edge-win [ticket number or zip path]
```

## Before you start

**Everything you conclude is bounded by the log window.** ZDEW prunes its own logs on startup
(`delete_older_logs()`), so a bundle collected days after an incident frequently does not contain the
incident. Step 4 exists to catch that before you write a confident wrong answer. If the reported event
predates the oldest log, say so in the report -- "no evidence of X" and "X is outside the log window" are
different findings and only one of them is honest.

**Absence of a log line proves nothing until you check three things:** that the log window covers the
event, that the log level in effect would have emitted that line, and that you grepped every rolled file
(`*.log` *and* `*.log.YYYYMMDD0000.log`).

**Paths, not `cd`.** Pass absolute paths to every command. The Bash tool's working directory persists
across calls, so a bare `cd` moves the operator's terminal out from under them.

**Use `grep` through Bash for the whole analysis, never the Grep tool.** The logs live outside any repo (step
2), and the Grep tool returns **zero matches silently** for paths outside the workspace or matched by a
`.gitignore`. Do not conclude "no hits" from a Grep tool call against the extracted tree.

**The bundle contains sensitive material.** Treat it as confidential customer data:

- `service/*.json` identity files hold **enrollment tokens, private keys, and client certificates** -- a
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
3. **A ticket number or ticket URL** -- fetch the attachment from whatever ticket system is wired up for this
   session (check the available tools for a support-desk integration; otherwise ask the operator to download
   it). List the attachments first and let the operator choose: a ZDEW feedback bundle is a timestamped
   `YYYY-MM-DD_HHMMSS.zip`, typically 100 KB–5 MB. Small `image001.png` / `image00N.jpg` attachments in the
   0.5–20 KB range are email-signature noise from the mail thread -- never download those by default, but do
   mention any image over ~15 KB in case it's a screenshot of the customer's settings.

Read the ticket's comments while you're there, including any internal or private ones. Engineers often leave
the hypothesis you're supposed to be testing -- and the diagnostic they already asked the customer for -- in a
comment the customer never sees. Step 5 depends on knowing what was asked.

If there is no zip and no ticket, say so and stop.

**Fleet tickets.** A third shape exists beyond single and aggregated: many separate bundles on one ticket, one
per machine, named after the host or site rather than a timestamp
(`<hostid>-<site>.<customer-domain>.zip`). Download all of them. The cross-machine comparison
in step 9b is usually worth more than any single capture, and a machine the customer did *not* report is often
the most valuable one in the set because it acts as a control.

Do not offer the operator a "representative subset" of a fleet ticket. Picking three of nine looks efficient
and destroys the comparison; the outlier you skip is the one that tells you whether the cause is local or
infrastructural.

### 2. Decide where things go, and tell the operator

Two destinations, and don't mix them:

- **Bundles and extracted logs** -- bulky, sensitive, never committed. They need a durable per-ticket archive
  the operator can go back to in a later session, so pick the destination in this order and use one
  `<archive>/<ticket#>/` folder for the whole ticket:

  1. `C:\temp\support\nfsupport\` if it already exists. It holds one folder per ticket; match what is there.
  2. Otherwise `C:\temp\support\`, creating it if needed.

  Keep each zip alongside the folder it extracts into, named the same (`foo.zip` next to `foo/`).
  **Do not use the session scratchpad.** It is disposable and invisible to the operator, so bundles left
  there are lost. **Say the full path you chose in your first reply**, before any analysis, so the operator
  knows where the data landed.
- **The report** -- a document. Put it in the working directory alongside any other notes for that ticket
  (e.g. `<ticket>-analysis.md`), not buried inside the extracted tree and not in the bundle archive.

### 3. Extract

The commands below assume the preferred archive from step 2. If you fell back to `C:\temp\support\`, drop the
`nfsupport` element from every path.

```bash
unzip -o -q /c/temp/support/nfsupport/<ticket>/bundle.zip -d /c/temp/support/nfsupport/<ticket>/bundle/
ls -laR /c/temp/support/nfsupport/<ticket>/
```

A **single feedback capture** extracts directly into:

- `dnsCache.txt`, `externalIP.txt`, `ipconfig.all.txt`, `netstat.txt`, `network-routes.txt`,
  `NrptPolicy.txt`, `NrptRule.txt`, `systeminfo.txt`, `tasklist.txt`
- `service/` -- `*.ziti` dump file(s) and `ziti-tunneler.log*`
- `UI/` -- `ZitiDesktopEdge.*.log`
- `ZitiMonitorService/` -- `ZitiUpdateService.*.log`

An **aggregated bundle** extracts into a folder of timestamped `.zip` files (`2026-04-09_091225.zip`, …) --
the customer sent multiple captures. Extract each into its own sibling folder and run steps 4–12 per
capture:

```bash
for z in /c/temp/support/nfsupport/<ticket>/bundle/*.zip; do unzip -o -q "$z" -d "${z%.zip}"; done
ls -d /c/temp/support/nfsupport/<ticket>/bundle/*/
```

A **fleet ticket** is the ticket folder itself, holding one zip per machine. Same loop, one level up, then run
steps 4–12 per machine and step 9b across all of them:

```bash
for z in /c/temp/support/nfsupport/<ticket>/*.zip; do unzip -o -q "$z" -d "${z%.zip}"; done
ls -d /c/temp/support/nfsupport/<ticket>/*/
```

An empty `NrptPolicy.txt` is normal (it means no NRPT policy is applied), not a missing file.

**`ziti-tunneler.log` duplicates the current day's rolled file.** The live log and
`ziti-tunneler.log.<today>0000.log` hold the same lines. Any `cat`/`grep` across `ziti-tunneler.log*` therefore
double-counts every event from the collection day: lifecycle tables come out with phantom duplicate restarts,
and per-day error counts for the collection day come out at roughly twice their true value.

**So there are two globs, and which one you use depends on what you are asking.** Check first which files
exist:

```bash
ls -1 /abs/path/capture/service/ziti-tunneler*
```

- **Counting, bucketing, or building a chronology** -- use the rolled files only,
  `ziti-tunneler.log.*`. They cover the whole window including the collection day, with no duplication.
- **Reading the newest lines**, or if no rolled file exists for the collection day (the bundle was taken
  before the first roll) -- use `ziti-tunneler.log` as well, and dedupe with `sort -u` before you count.

Steps 6, 9b and 10 are all counting steps, so their commands use `ziti-tunneler.log.*`.

**Some rolled logs contain binary garbage** where a write was torn by an abrupt process death. `grep` then
reports `Binary file ... matches` and silently gives you nothing. Use `grep -a` for the entire analysis, not
just when you hit it.

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
- **`System Boot Time` is unreliable on its own.** With Fast Startup enabled the value drifts, because the tick
  count it is derived from excludes time spent hibernated. Observed in the field: two machines whose reported
  boot time landed ~25 seconds *after* a monitor `OnShutdown` event, which is impossible. Use it only to
  corroborate. To separate a reboot from a crash in step 6, use the monitor's `OnShutdown was called` instead.
- **Timestamps are UTC in the logs, local in the filesystem.** Log lines are UTC. Zip entry mtimes (so any
  `ls -l` on an extracted file, including a `.dmp`) carry the *machine's local* time with no zone recorded.
  Get the offset from a tunneler startup banner, which prints both:
  `initialized at : Mon Aug 24 2026, 13:44:12 PM (local time), 2026-08-24T11:44:12 (UTC)`. Convert before you
  correlate a file mtime against a log line, or you will chase a two-hour phantom gap.

Then bound the window:

```bash
ls -1 /abs/path/capture/service /abs/path/capture/UI /abs/path/capture/ZitiMonitorService
grep -ahoE "delete_older_logs\(\) Deleting old log file .*" /abs/path/capture/service/*.log | sort -u
```

Write down the oldest surviving timestamp per log directory and compare it against the customer's stated
incident date. **State the gap explicitly in the report.** The pruning lines tell you exactly which days
were destroyed and when.

Note that the three log directories roll independently -- `UI/` often reaches further back than `service/`,
and the monitor logs further still. If the tunneler log for the incident day is gone, check whether the
monitor or UI log for that day survived; they carry less detail but they carry timestamps.

### 5. Verify what was already asked for

From the ticket comments (step 1), list every diagnostic action support requested -- restart the tunneler,
enable trace logging, reproduce and recapture -- and confirm in the logs whether it actually happened.

```bash
# was trace/verbose logging actually on?
grep -ahoE "ziti_log_set_level\(\) set log level: .*" /abs/path/capture/service/ziti-tunneler*.log*
# per-level line counts, to see what detail you actually have
grep -ahoE "\] +(TRACE|VERBOSE|DEBUG|INFO|WARN|ERROR) " /abs/path/capture/service/ziti-tunneler.log.* \
  | sort | uniq -c
```

Customers routinely send logs without performing the requested step. Reporting "the customer never
restarted it, so the question we asked is still unanswered" is often the most actionable line in the whole
report -- and it stops the team from re-analyzing the same bundle.

### 6. Service lifecycle

Banners first:

```bash
grep -ah "service begins\|service ends" /abs/path/capture/service/ziti-tunneler.log.*
```

Then process starts independently, because a banner can be missing when a log rolls mid-startup:

```bash
grep -ahoE "^\[[^]]+\].*ziti_log_init\(\) Ziti C SDK version .*starting at \([^)]+\)" \
  /abs/path/capture/service/ziti-tunneler.log.*
```

Rolled files only, per step 3 -- the live `ziti-tunneler.log` repeats the collection day and will invent a
duplicate restart in the table below.

Build a chronological table. Collapse `service ends` immediately followed by `service begins` into one
**Restart** row.

| Log File | Event | Timestamp (UTC) | Notes |
|---|---|---|---|
| ... | Restart / Start / Stop | ... | ... |

Interpretation:

- **Restart (clean)** -- usually an update or intentional restart. Not worth reporting individually unless
  there are many in a short period.
- **Start with no preceding stop** -- the process died without a clean shutdown. **This is the crash marker,
  and it is the most reliable one in the bundle.** Before calling it a crash, subtract reboots: look for a
  monitor `ziti-monitor OnShutdown was called` within ~3 minutes *before* the orphan `service begins`. If one
  is there, the OS stopped the service and the missing `service ends` is expected -- label it a reboot. Prefer
  this over `System Boot Time`, which is unreliable (step 4). What remains after subtracting reboots is an
  ungraceful exit.

  Corroborate each survivor against the monitor log, which says so outright:

  ```bash
  grep -ah "did not exit cleanly" /abs/path/capture/ZitiMonitorService/*.log
  ```

  `SERVICE IS DOWN and did not exit cleanly.` is definitive. Its absence is not, because the monitor logs roll
  independently and prune sooner than you expect -- a crash whose tunneler log survived may have lost its
  monitor evidence, and vice versa. On one machine the only surviving proof of a crash was the monitor line
  plus a `.dmp` mtime; the tunneler log for that day was already gone.

- **Both services restarting together, repeatedly, with no `OnShutdown` at all** -- the machine is losing
  power, not crashing. `ziti-edge-tunnel` and `ziti-monitor` are independent processes and do not die in
  lockstep. Seen six times on one site over four days, alongside a four-day DNS outage: the site's router and
  the machine were both power-cycling. Report it as a site infrastructure problem, not a ZDEW one.
- **No events at all** -- the service has run continuously since before the oldest log. Healthy; say so.
- **More starts than stops** -- count the unexplained ones after removing reboots.

Do **not** cross-check the most recent `service begins` against `uptime[Ns]` in the `.ziti` dump and expect
them to match. `uptime` there is **per-identity context uptime**, not process uptime; a context reloads on
re-auth, on config change, and on identity enable/disable. A dump showing 3 days of context uptime under a
service that started 4 days ago is normal. Only `ziti_log_init` and the banners date the process.

Then the monitor service:

```bash
grep -ah "OnShutdown was called\|OnStop was called\|OnStart\|aliveness check" \
  /abs/path/capture/ZitiMonitorService/*.log
```

- `ziti-monitor OnShutdown was called` just before a gap -- the machine rebooted. Benign.
- `ziti-monitor OnStop was called` -- intentional stop, typically an update. Check whether the version
  changes on the next start.
- `aliveness check ... appears blocked and has been for N times. AlivenessChecksBeforeAction:12` -- **report
  the peak N reached, and whether it ever hit the threshold.** Read the threshold off the same line rather
  than assuming 12 -- 12 is only the default, and `AlivenessChecksBeforeAction` is settable by policy or
  `settings.json` anywhere in 1–720. Partial counts that reset (3 of 12 during a post-boot storm) are noise;
  only reaching the printed threshold means the monitor killed the tunneler. Treating any
  aliveness warning as a stall produces false findings -- this fires routinely during startup and
  resume-from-sleep.

### 7. Crash and stall markers

```bash
ls -la /abs/path/capture/*.dmp /abs/path/capture/service/*.dmp 2>/dev/null
```

- `ziti-edge-tunnel.stalled.dmp` in the capture root -> **STALLED**
- `service/ziti-edge-tunnel.crash.dmp` -> **CRASHED**
- neither -> **no crash/stall markers**

Glob `**/*.dmp` once across the whole ticket folder, with sizes and dates, so you can see which captures have
them:

```bash
ls -la --time-style=long-iso /abs/path/<ticket>/*/*.dmp /abs/path/<ticket>/*/service/*.dmp
```

**Two different writers, and the distinction decides which repo the bug belongs to.**

- `stalled.dmp` is written by **ZDEW's monitor service** (`ZitiUpdateService/utils/MiniDump.cs`, called from
  `UpdateService.cs`) just before it kills a tunneler it believes is blocked. Bugs here are `desktop-edge-win`.
- `crash.dmp` is written by **ZET itself**, from its own `CrashFilter`
  (`programs/ziti-edge-tunnel/windows/minidump.c`). Bugs here are `ziti-tunnel-sdk-c`.

**A 0-byte `.dmp` means `MiniDumpWriteDump` failed outright.** Do not report it as a stall or a crash; report
it as a lost diagnostic. Common on the `stalled.dmp` path, where the write races the `Kill()` that follows it.

**Date every dump before trusting it.** These are fixed filenames, overwritten by each new event, so a dump
tells you about the machine's *most recent* crash and nothing else. Convert the mtime from local to UTC
(step 4) before comparing it to anything. A dump months older than the reported incident is not noise -- it is
the previous occurrence, and its date is evidence.

**Correlate the dump mtime against the identity certificate's `Not Before`.** This is the highest-yield check
in the whole step and it is easy to miss:

```bash
grep -aA6 "Identity Cert:" /abs/path/capture/service/*.ziti | grep -aiE "Not (Before|After)"
```

A dump written within a minute of the certificate's `Not Before` means the crash happened during certificate
renewal. Six of six dumps matched this way on one fleet ticket, which turned a vague "it crashes sometimes"
into a named trigger. Renewal fires roughly 23–30 days before expiry on a one-year certificate, so it is a
rare event -- if the timestamps line up, that is not coincidence.

The same query gives you `Not After`, which lets you predict the *next* renewal per machine and say which
machines are still exposed. A machine with no crash dump whose certificate expires soon has simply not renewed
yet.

**Getting a stack out of a dump.** The dumps carry no PE headers (no `MiniDumpWithModuleHeaders`), so they
cannot be unwound without the exact matching `ziti-edge-tunnel.exe`. Download that exe from the ZET release
matching the version in the `.ziti` header (step 12 tells you whether it is the regular or `-win32crypto`
build), then in WinDbg: `.exepath+ <dir holding the exe>`, `.reload /f`, `.ecxr`, `kb`. A recent WinDbg reads
the MinGW DWARF directly and gives named frames; an older one needs `addr2line` against the same exe.

**You can triage a dump without a debugger,** which is worth doing before asking the operator to open WinDbg.
Every dump embeds a `MINIDUMP_EXCEPTION` record: locate the little-endian `ExceptionCode` bytes `050000c0`
(`0xc0000005`, access violation), then read `ExceptionInformation[1]` at +40 for the faulting address, and the
thread `CONTEXT` via the RVA at +156 (`Rax` at +0x78, `Rcx` +0x80, `Rdx` +0x88, `Rsp` +0x98, `Rip` +0xF8).

Two things that fall out of the registers alone:

- A faulting address of `0x0` is a NULL dereference. `0xFFFFFFFFFFFFFFFF` means the OS could not report the
  address, which usually means the bad pointer was not merely NULL but garbage -- check whether the argument
  register is *non-canonical* (bits 63–48 neither all-zero nor all-one), because such a value can never be a
  valid user-mode address and points at a use-after-free rather than a missing NULL check.
- Registers often hold string fragments. Decode them as little-endian ASCII -- `0x3534333231636261` reads
  `abc12345`. On one crash this recovered the surviving argument to a failed `strcmp`, and its shape (a ziti
  id) identified which of the two struct fields was NULL.

**Do not generalize one dump to the rest.** Compare `Rip` and the faulting address across every dump on the
ticket before concluding they share a cause. On one fleet ticket five dumps faulted at one `ucrtbase` offset
with address `-1` while the sixth faulted at a different offset with address `0` -- two distinct defects that
looked identical until the exception records were read side by side.

### 8. Config, identities, and the Windows.old restore path

Read this section for any ticket about **settings reverting, identities disappearing, or IP range changing
after a Windows update.** It is a known ZDEW defect path, not a customer error. The behavior is
**version-dependent** (fixed in ZET v1.18.6), so get the ZET version first: the `.ziti` dump header (step
12) or the startup banner (step 6).

```bash
grep -ahoE "^\[[^]]+\].*load_tunnel_status_from_file\(\) Loading config file from .*" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -ah "Restored old identity from the backup path\|Removing old identity from the backup path\|failed to copy backup identity file\|keeping existing file\|failed to remove backup file" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -ahoE "load_identities\(\) loading identity file: .*" /abs/path/capture/service/ziti-tunneler*.log*
```

What the code does (`ziti-tunnel-sdk-c`, `programs/ziti-edge-tunnel/ziti-edge-tunnel.c`,
`move_config_from_previous_windows_backup()`), on every version:

- Runs **unconditionally on every startup**, over `%SystemDrive%\Windows.~BT\...` and
  `%SystemDrive%\Windows.old\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry`.
- Copies **every regular file** in that folder despite the "identity" naming -- `config.json` included.

On **ZET before v1.18.6** (the defect path):

- Overwrites the live file without an exclusive flag.
- Runs **after** the config has already been read and after the IP range has been resolved from it. So a
  restored `config.json` is never read on the run that restores it, and the next save writes in-memory
  state back over it. On a fresh post-upgrade profile that state is defaults. Net effect: **identities come
  back, the config does not.**
- Deletes the backup file after a successful copy, but does not check or log whether the delete succeeded.
  `Windows.old` is TrustedInstaller-owned; if the delete fails, the copy repeats on every startup.

On **ZET v1.18.6 and later**:

- Runs **before** the config load, so a restored `config.json` is read on the same startup and the
  "identities come back, the config does not" outcome no longer applies.
- Never overwrites an existing live file: `keeping existing file X, backup file left at Y` (WARN) marks
  each skipped copy.
- Logs delete failures: `failed to remove backup file[...]` (WARN). Repeated on every startup, it means the
  backup copy of that file is stuck in `Windows.old` (TrustedInstaller-owned) but is now harmless.

So: `Restored old identity...` lines present -> the restore ran, name the files. Absent -> note it, and
**immediately restate the step-4 coverage caveat**, because `Windows.old` is pruned by Windows roughly 10
days post-upgrade and the startup that mattered is usually in a deleted log. Absent-and-out-of-window is
not evidence against this path.

**The monitor service restores its own settings the same way** (ZDEW 2.11.3.0 and later): on startup,
before its settings load and defaults are written, it copies missing files into
`...\systemprofile\AppData\Roaming\NetFoundry\ZitiUpdateService` from `Windows.old` then `$Windows.~BT`,
never overwriting an existing file and skipping empty backups. Check this for tickets about automatic
update or maintenance-window settings reverting after a Windows upgrade -- the evidence is in
`ZitiMonitorService/`, not the tunneler logs:

```bash
grep -ah "Windows upgrade backup\|keeping existing file\|ignoring empty backup file\|failed to copy backup file\|failed to remove backup file\|failed restoring backup files" \
  /abs/path/capture/ZitiMonitorService/*.log
```

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
grep -ahoE "seed_dns\(\) DNS configured with range .*" /abs/path/capture/service/ziti-tunneler*.log* | sort -u
grep -ahoE "set_dns\(\) executing .*" /abs/path/capture/service/ziti-tunneler*.log* | sort -u
awk '/ziti-tun0/,/^$/' /abs/path/capture/ipconfig.all.txt
```

- `seed_dns() DNS configured with range A - B (N ips)` is the **authoritative range for that startup**.
- **The ZDEW default is `100.64.0.1/10`.** Anything else came from `config.json`. This is how you tell "the
  setting was lost and fell back to default" from "the setting is present but not what the customer
  remembers." Both are real outcomes with different fixes, and the customer cannot tell them apart.
- A `/16` where the customer expected `/24` is a different failure from a full reset to `100.64.0.1/10` --
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
are `destination / mask / gateway / interface / metric` -- a route whose **interface** is not the ziti
adapter's IP is not ZDE's route, whatever its destination looks like. Match the interface column back to an
adapter in `ipconfig.all.txt` before attributing anything.

### 9b. Fleet comparison (multiple captures only)

Skip for a single capture. For an aggregated or fleet ticket, this is where the answer usually is: one machine
tells you what broke, the set tells you *why*.

**Find the control.** Build a one-line-per-machine table of total ERROR+WARN counts and the counts of the two
or three signature messages. A machine that is quiet while the others are loud is the most informative capture
in the set. Then diff it against a loud one -- OS build, installed security software (`tasklist.txt`), upstream
DNS (`ipconfig.all.txt`), external IP (`externalIP.txt`), ZET version. Whatever differs is a hypothesis with a
cheap test attached.

On one ticket the single quiet machine of nine was the only one not running the endpoint-protection scanning
engine the other eight had, and had zero of every error class the others had thousands of. One control is not
proof, but it converted a shapeless "it happens everywhere" into a one-week A/B the customer could run.

**Attribute by date, not by machine.** Bucket the signature errors per machine per day:

```bash
grep -ah "<signature>" /abs/path/capture/service/ziti-tunneler.log.* | grep -aoE "^\[[0-9-]{10}" \
  | sort | uniq -c
```

A spike on the same date across many machines is infrastructure. A spike on one machine on its own dates is
local. This one query separates "your controller had a bad day" from "that site has a bad router", and it is
the difference between a useful reply and a wrong one.

**Measure inbound reachability directly when the machine hosts a service.** If the identity binds a service
(`hosted_service[...]` lines), each inbound dial is logged, and the count over the log window is a hard
availability number:

```bash
grep -ahc "on_hosted_client_connect" /abs/path/capture/service/ziti-tunneler.log.*
```

Divide by the expected poll count (window length ÷ the caller's polling interval, which you can read off the
gaps between those same lines) to get a percentage per machine. This is far better than error counts for
answering "how bad is it really" and it is stated in terms the customer already understands, because it matches
what their own monitoring shows. On one fleet it ranged from 26% to 94% across nine machines -- a number the
customer had been describing as "intermittent".

Also worth a per-machine count: DNS re-lookups of the service the local app talks to
(`format_resp() found record[...] for query[...]`). A machine re-resolving hundreds of times has a connection
that keeps dying; a machine resolving five times in a week is holding one healthy long-lived connection.

### 10. Errors AND warnings

Scan every rolled log, both levels. **Do not scan `ERROR` alone** -- the highest-volume real defects in ZDEW
surface at `WARN`, and an ERROR-only pass will miss them entirely.

Counts per file first, to spot spikes:

```bash
grep -ac "ERROR" /abs/path/capture/service/ziti-tunneler.log.*
grep -ac "WARN"  /abs/path/capture/service/ziti-tunneler.log.*
```

Then bucket the messages by shape -- collapse bracketed values and digits so variants group together:

```bash
grep -ah "ERROR" /abs/path/capture/service/ziti-tunneler.log.* \
  | sed -E 's/^\[[^]]+\][[:space:]]+ERROR //' \
  | sed -E 's/\[[^]]*\]/[]/g' \
  | sed -E 's/[0-9]+/N/g' \
  | sort | uniq -c | sort -rn | head -25
```

Repeat with `WARN` (and `s/[[:space:]]+WARN //`). For any high-count warning, check whether it's periodic --
a fixed interval means a retry loop, not a burst:

```bash
grep -ah "<the warning text>" /abs/path/capture/service/ziti-tunneler.log.* | head -20
```

#### Category 1: network / control plane

The root layer. If these are elevated, downstream service failures are *expected* and not independently
significant.

| Pattern | Meaning |
|---|---|
| `failed to connect to controller due to not authorized` | OIDC token expired; client re-authenticating |
| `failed to connect to controller due to failed to authenticate` | Harder auth failure after token expiry |
| `failed to get identity_data: no api session token set` | No valid session during the re-auth window |
| `failed to get current edge routers: ... UNAUTHORIZED` | Can't fetch routers -- no valid session |
| `ch[N] disconnected from edge router[...]` | Router channel dropped |
| `failed to get identity_data: unknown node or service` | **DNS resolution failed for the controller hostname** (libuv `-3008`) |
| `Unknown system error -10013` | Winsock `WSAEACCES` -- a local policy, firewall, or ZTNA client blocked the socket |
| `latency_timeout() ... closing channel` | No traffic before the latency probe; channel torn down |

**Baseline:** the `not authorized` cluster bursts around midnight UTC on the nightly OIDC refresh -- ~48–52
per daily log is normal. Elevated *throughout* the day means the controller was unreachable or rejecting
sessions for an extended period.

**Resume-from-sleep is its own baseline.** A cluster of `unknown node or service` / `-10013` /
`not authorized` within a minute or two of `Received power resume event` is the NIC and DNS not being ready
while the tunneler retries. On a machine also running a ZTNA client (Zscaler et al.), name resolution can
fail until that client's own tunnel is up. Not a ZDE defect -- check for the resume event before escalating:

```bash
grep -ahoE "^\[[^]]+\].*endpoint_status_change\(\) Received power (resume|suspend) event" \
  /abs/path/capture/service/ziti-tunneler.log.*
```

#### Category 2: service / dial

Application-level failures. **Always check category 1 first.** If network errors are normal but dial
failures are elevated, the problem is service-side -- posture policies, terminator health, service config.

| Pattern | Meaning |
|---|---|
| `ziti context is not authenticated, cannot connect to service[...]` | Dial during an auth gap -- expected if category 1 is elevated |
| `ziti dial failed: invalid state` / `connection is closed` | Paired with the above |
| `ziti_write() failed: invalid state` | Write on a torn-down connection -- expected during auth refresh |
| `on_tcp_client_err() ... err=-13/-14, terminating connection` | Local teardown of an intercepted connection -- routine. See the lwIP note below. |
| `exceeded maximum retries creating circuit ... timeout waiting for message reply` | Circuit creation failing at the router -- real if category 1 is quiet |

#### Category 3: routing and NRPT (WARN level -- easy to miss)

| Pattern | Meaning |
|---|---|
| `refresh_routes() failed to create exclusion route[IP]: 1168(...)` | Could not install the bypass route for a controller/router public IP. Periodic repetition means it never succeeded. On a machine with another VPN/ZTNA client owning the default route, missing exclusion routes are a plausible cause of "overlapping ranges" complaints. |
| `is_nrpt_policies_effective() NRPT policies are ineffective in this system` | ZDEW falls back to interface DNS. Expected on some managed builds; note it when DNS resolution is the complaint. |
| `failed to create route` / `failed to remove route` | Route table contention -- check for other tunnel adapters |

#### What to flag

- Network errors elevated all day (not just midnight) -> controller/auth disruption; check whether the same
  spike appears in other captures on the same date
- The same spike across multiple captures on one date -> infrastructure, not this machine
- Dial failures elevated without matching network errors -> service-side
- Any 2x+ day-over-day spike in either category -> name the date, and say whether it's isolated or shared
- Any warning repeating at a fixed interval for days -> a retry loop that never succeeds; report it with a
  total count even when nothing else is wrong

### 11. Known misleading strings

Do not take these at face value:

- **`1168(The operation completed successfully.`** -- ZDEW formats the message text of error 0 next to error
  code `1168`. `1168` is `ERROR_NOT_FOUND`. The line is a **failure**; the trailing text is a formatting
  bug, and the unbalanced parenthesis is part of it.
- **`Unknown system error -10013`** -- not unknown: Winsock `WSAEACCES`, permission denied on a socket.
- **`on_tcp_client_err() ... err=-N` is an lwIP `err_t`, not a libuv code.** `tunnel_tcp.c` is the lwIP glue in
  `ziti-tunnel-sdk-c`, so read `-13` as `ERR_ABRT` and `-14` as `ERR_RST` (lwIP `err.h`: `-3` `ERR_TIMEOUT`,
  `-11` `ERR_CONN`, `-13` `ERR_ABRT`, `-14` `ERR_RST`, `-15` `ERR_CLSD`). Looking these up as libuv errno gives
  a completely wrong reading.

  **`err=-13` is logged at ERROR and is almost always noise.** The source address is the tun adapter's own
  gateway (`100.64.0.1`), so it is the *local* application's intercepted connection being torn down, not
  anything on the overlay. On one fleet it ran at a flat ~80/hour, 24/7, on eight of nine machines regardless
  of health, and was the highest-count error in every bundle. Ranking machines by `err=-13` ranks them by how
  chatty the local app is. Do not lead a report with it, and do not use it as a severity signal.
- **`The config file %s cannot be opened due to %s`** -- the format arguments are swapped in some versions,
  so the error string and the path appear in the wrong order.
- **`uptime[Ns]`** in a `.ziti` dump -- per-identity context, not the process. See step 6.
- **`Unknown adapter ziti-tun0`** in `ipconfig.all.txt` -- "Unknown" is just Windows' adapter-type label for
  the Wintun device. Not an error.
- **A missing log line** -- see "Before you start". Check coverage and log level before calling it evidence.

### 12. The `.ziti` dump

`service/` holds zero or more `*.ziti` dumps -- **one per identity context**, and a machine with a dozen
enrolled identities may still have only one dump. Name which identity each dump covers; do not generalize
one identity's health to the machine.

Header block (first ~14 lines) gives the version inventory -- app version, C SDK, tlsuv/OpenSSL, sodium,
libuv, OS build, hostname, and dump time. Put these in the report; it's the first thing engineering asks
for. The `ziti-sdk` line carries the commit (`1.11.8(gb3e8377)`), which is what lets you read the exact source
that was running: `gh api repos/openziti/ziti-sdk-c/contents/library/<file>?ref=<commit>`.

The `tlsuv:` line also tells you which Windows build variant is installed. An OpenSSL version means the regular
build; its absence means the `-win32crypto` twin. You need this to download the right binary in step 7.

**Identity Cert block** -- `Not Before` / `Not After` on the identity certificate. Pull it always, not just for
crash tickets:

```bash
grep -aA6 "Identity Cert:" /abs/path/capture/service/*.ziti | grep -aiE "Not (Before|After)"
```

`Not Before` dates the last certificate renewal, which is a rare event (annual, firing 23–30 days before
expiry) and therefore a precise clock for correlating anything else that happened at the same moment -- see
step 7. `Not After` tells you when the next renewal is due, so you can say which machines are still exposed to
whatever the last one triggered. A machine that spends its renewal window unauthenticated cannot renew at all,
and its certificate then expires outright, requiring re-enrolment; if you see a long unauthenticated stretch,
check it against `Not After`.

**Stale dumps.** `service/` may hold a `.ziti` from a long-dead identity -- a different controller URL, a much
older app version in its header, `enabled[false]`, `No Session found`. Read the header date before analyzing
any dump. These are inert leftovers worth a hygiene note, not a finding.

**Controllers** -- `Controller:` or `Controller[HA]:`. Each entry carries `online[Y/N]`; flag any `online[N]`
as **OFFLINE -- investigate**. Note the controller version, and whether the deployment is HA (multiple
entries) or single.

**Channels (routers)** -- `Channels:` block, each with `connected[Y/N]`, `latency[Nms]`, `connected[Ns]`:

- any `connected[N]` -> **DISCONNECTED**
- latency under 100 ms normal; 100–250 ms elevated; over 250 ms -> **HIGH LATENCY**
- `connected[Ns]` is channel uptime. Short uptime = recent reconnect. **Before flagging instability, check
  for a power resume or an auth burst at that same moment** (step 10) -- all channels reconnecting
  simultaneously points at the local machine waking up, not at the routers. Channels reconnecting at
  *different* times, or one channel that never comes back, is the real signal.
- recent reconnect *plus* elevated latency on the same router is a meaningful instability signal

**Connections** -- `Connections:` block with `state`, `service`, `channel`, `sent`, `recv`, `recv_buff`:

- all should be `state[Connected]`
- `recv_buff` should be 0 or near 0; a large value means backpressure or a stalled reader
- note which services are in use and whether the byte counters show real traffic -- active connections with
  real volume are strong evidence the data plane works, which narrows a vague "nothing works" complaint
- `idle_time` far larger than `connect_time` across every connection means the user wasn't actually using
  it during the window

**API session** -- note `auth_method[...]` (`Legacy` vs OIDC) and `api_session_state[N]`. A `Legacy` auth
method on a controller that also offers OIDC is worth mentioning; the token lifetimes differ and so do the
midnight-refresh baselines in step 10.

### 13. Write the report

Write to the document location chosen in step 2 (the working directory, e.g. `<ticket>-analysis.md`) -- not
inside the extracted log tree.

```markdown
# ZDEW Debug Report -- Ticket <ticket_number>

**Source zip:** `<filename>` (<size>)
**Captures:** N (single / aggregated)
**Generated:** <date>

---

## <capture_name> -- <hostname>

| Item | Value |
|---|---|
| ZDE app | |
| Tunneler SDK | |
| C SDK | (with commit) |
| tlsuv / OpenSSL | (absence = the -win32crypto build) |
| OS | (name, build, release name) |
| Original install date | (= in-place upgrade date, if recent) |
| System boot | (corroborating only -- unreliable under Fast Startup) |
| Identity cert | `Not Before` / `Not After` |
| Dump taken | |
| Controller | (version, HA or single) |

### Log Window Coverage
Oldest log per directory, the incident date, and whether the window covers it. Name any days destroyed by
`delete_older_logs()`.

### Prior Asks
What support requested, and whether the logs show it was done.

### Crash / Stall Markers
Which writer produced each dump, its mtime converted to UTC, whether it lines up with the certificate
`Not Before`, and the faulting address and `Rip` per dump. Say explicitly when dumps do *not* share a signature.
0-byte dumps are a lost diagnostic, not an event.

### Service History
Chronological table. Reboots labeled as reboots, crashes labeled as crashes, with the evidence for each
(orphan `service begins`, monitor `did not exit cleanly`, or `OnShutdown`).

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
- **Say plainly when the symptom does not reproduce in the bundle** -- and then say what would be needed to
  catch it, rather than padding with healthy-system findings.
- **Every "no evidence of X" carries its coverage caveat** or it will be misread as "X did not happen."
- Keep a healthy-system section short. Three lines confirming channels, connections, and lifecycle are fine
  are worth more than three paragraphs.
- **Assume the report gets forwarded.** Quote the minimum log text needed, and never include identity file
  contents, session tokens, certificates, or full controller URLs. If a finding requires a customer
  hostname or identity name to make sense, use it -- but don't paste surrounding lines that add nothing but
  exposure.

After writing, tell the operator the path and the one finding that matters most.
