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
in `references/fleet-comparison.md` is usually worth more than any single capture, and a machine the customer
did *not* report is often the most valuable one in the set because it acts as a control.

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
the customer sent multiple captures. Extract each into its own sibling folder and run steps 4–7 per
capture:

```bash
for z in /c/temp/support/nfsupport/<ticket>/bundle/*.zip; do unzip -o -q "$z" -d "${z%.zip}"; done
ls -d /c/temp/support/nfsupport/<ticket>/bundle/*/
```

A **fleet ticket** is the ticket folder itself, holding one zip per machine. Same loop, one level up, then run
steps 4–7 per machine and `references/fleet-comparison.md` across all of them:

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

Step 6, `references/fleet-comparison.md` and `references/errors-and-warnings.md` are all counting steps, so
their commands use `ziti-tunneler.log.*`.

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

### 7. Pick the reference files this ticket needs

Everything above runs on every ticket. What follows does not. The files in `references/` hold the deep
procedure for each area; read a file when its trigger fires, and skip the rest. Reading all of them on every
ticket is the slow, noisy path -- most bundles need two or three.

| Read | When |
|---|---|
| `references/ziti-dump.md` | a `*.ziti` file exists in `service/` -- almost always. Version inventory, certificate dates, controller / router / connection health |
| `references/errors-and-warnings.md` | you are about to characterize what the logs complain about -- almost always. Baselines, the three error categories, and the strings whose plain reading is wrong |
| `references/crash-dumps.md` | a `.dmp` exists, or step 6 found an ungraceful exit, or the ticket reports a crash, a hang, or a dead tray icon |
| `references/config-identity-restore.md` | the ticket reports settings reverting, identities disappearing, or the IP range changing -- especially after a Windows update |
| `references/tun-dns-routes.md` | the ticket involves DNS resolution, an unexpected IP range, unreachable services, or a suspected conflict with another VPN or ZTNA client |
| `references/fleet-comparison.md` | the ticket has more than one capture. Read it after every capture has been analyzed individually |
| `references/report-template.md` | always, at write-up time |

Two rules about skipping:

- **Skipping is not the same as clearing.** A file you did not read produces no finding in either direction.
  Do not write "no crash markers" if you never listed the dumps. Either run the check or leave the section out.
- **The trigger can fire late.** A ticket that looked like a DNS complaint becomes a crash ticket the moment
  step 6 turns up an orphan `service begins`. Come back and read the file then.

### 8. Write the report

Read `references/report-template.md` and follow it.

After writing, tell the operator the path and the one finding that matters most.

