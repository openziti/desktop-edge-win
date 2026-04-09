---
name: debug ziti-desktop-edge-win
description: Analyze a feedback.zip from Ziti Desktop Edge for Windows (ZDEW). Handles both single feedback zips and aggregated zips (a zip containing multiple feedback zips). Extracts everything under a ticket number folder and prepares the contents for analysis.
---

## Invocation

```
/debug ziti-desktop-edge-win
```

## Steps

### 1. Find the zip file

Look in the current working directory for a `.zip` file. If there are multiple, list them and ask the user which
one to use. If there are none, tell the user and stop.

### 2. Ask for the ticket number

Ask the user:

> What is the ticket number for this zip?

Wait for their response. Use that value as the destination folder name.

### 3. Unzip the outer zip

Unzip the zip file into a subfolder named after the ticket number:

```bash
unzip <zipfile> -d <ticket_number>/
```

### 4. Detect: single feedback zip or aggregation?

A **normal feedback zip** extracts directly into these files/folders:
- `dnsCache.txt`, `externalIP.txt`, `ipconfig.all.txt`, `netstat.txt`, `network-routes.txt`,
  `NrptPolicy.txt`, `NrptRule.txt`, `systeminfo.txt`, `tasklist.txt`
- `service/` — contains a `.ziti` identity file and `ziti-tunneler.log*` files
- `UI/` — contains `ZitiDesktopEdge.*.log` files
- `ZitiMonitorService/` — contains `ZitiUpdateService.*.log` files

An **aggregated zip** extracts into a folder containing one or more timestamped `.zip` files
(e.g. `2026-04-09_091225.zip`, `2026-04-09_091615.zip`). This means the customer submitted multiple
feedback captures bundled together.

Check the extracted contents using a glob search for `*.zip` inside the ticket folder.

### 5. If aggregated: unzip each inner zip

For each `.zip` found inside the extracted folder, unzip it into its own subfolder (named after the zip,
without the `.zip` extension):

```bash
unzip <inner_zip> -d <inner_zip_dir>/
```

### 6. Scan for crash and stall markers

For each feedback capture folder, check for the following files:

- **Stall:** `ziti-edge-tunnel.stalled.dmp` in the root of the capture folder
- **Crash:** `service/ziti-edge-tunnel.crash.dmp` in the `service/` subfolder

Use a glob search across the ticket folder:

```
**/*.dmp
```

Note the result for each capture:
- If a stall dump is found → flag as **STALLED**
- If a crash dump is found → flag as **CRASHED**
- If neither is found → flag as **no crash/stall markers**

### 7. Analyze the .ziti identity file

Each capture's `service/` folder contains a `.ziti` dump file. Read it and check the following sections:

#### Controllers

Look for the `Controller[HA]:` block. Each listed controller has an `online[Y/N]` flag:

- All controllers should show `online[Y]`
- Flag any controller with `online[N]` as **OFFLINE — investigate**

#### Channels (routers)

Look for the `Channels:` block. Each channel has a `connected[Y/N]` flag and a `latency[Nms]` value:

- All channels should show `connected[Y]` — flag any `connected[N]` as **DISCONNECTED**
- Latency guidelines:
  - Under 100ms — normal
  - 100–250ms — elevated, worth noting
  - Over 250ms — high, flag as **HIGH LATENCY**
- Each channel also shows `connected[Ns]` — how long the channel has been up in seconds. Compare this against
  the other captures (or against the dump timestamp / uptime). If channels show a much shorter uptime than
  expected, this indicates a **recent reconnect**. Cross-reference with any elevated latency on the same
  channels — a recent reconnect plus high latency on the same router is a meaningful signal that the router
  connection was unstable.

#### Connections

Look for the `Connections:` block. Each connection shows `state`, `service`, `channel`, and traffic counters
(`sent`, `recv`, `recv_buff`):

- All connections should be in `state[Connected]`
- `recv_buff` should be 0 or near 0 — a large non-zero value suggests a backpressure/stall condition
- Note which services are in use and whether traffic volumes seem active or idle

### 8. Analyze service start/stop history in tunneler logs

For each capture, read all `service/ziti-tunneler*.log*` files and grep for the start and stop banners:

- **Service begins:** `============================ service begins ================================`
- **Service ends:** `============================ service ends ==================================`

Build a chronological table of all events across all log files. Collapse a `service ends` immediately followed
by a `service begins` into a single **Restart** row — they are one logical event:

| Log File | Event | Timestamp (UTC) | Notes |
|---|---|---|---|
| ... | Restart / Start / Stop | ... | ... |

#### What to look for

- **Restart (clean):** a `service ends` immediately followed by a `service begins` — typically an update or
  intentional restart. These are **not worth reporting individually** unless there are many of them in a short
  period, which would indicate the user is experiencing repeated disruptions.
- **Start (no preceding stop):** a `service begins` with no `service ends` anywhere before it in the log
  window — the service was stopped ungracefully. **Investigate further** (see below).
- **No events at all:** the service has been running continuously since before the oldest log — expected and
  healthy, note it as such.
- **More starts than stops:** at least one ungraceful stop occurred — flag and count how many.
- **Corroborate with uptime:** cross-check the most recent `service begins` timestamp against the `uptime[Ns]`
  value in the `.ziti` file. They should agree within a few seconds.

#### Investigating an ungraceful stop

When a `service begins` has no preceding clean stop, open the `ZitiMonitorService/ZitiUpdateService.*.log` file
covering the same time window and look for:

**Windows shutdown/reboot:**
```
ziti-monitor OnShutdown was called
```
If found just before the ungraceful stop timestamp, the machine rebooted. This is benign — confirm by checking
that the tunneler and monitor both restart shortly after.

**Stall monitor triggering:**
```
ziti-edge-tunnel aliveness check appears blocked and has been for N times. AlivenessChecksBeforeAction:12
```
If found, the monitor detected the tunneler was stalled and killed it. This is a meaningful signal — note how
many times the aliveness check fired before action was taken.

**Controlled service stop (update):**
```
ziti-monitor OnStop was called
```
This is an intentional stop, typically for an update. Check if the version number changes on the subsequent
restart.

### 9. Analyze errors in tunneler logs

For each capture, scan all `service/ziti-tunneler*.log*` files for `ERROR` level entries. Categorize errors
into two buckets — **network errors** and **service/dial errors** — then count per daily log file to assess
frequency and detect spikes.

#### Category 1: Network errors

These indicate the tunneler cannot reach or authenticate with the Ziti control plane (controllers or routers).
They are the root cause layer. If these are elevated, service failures downstream are expected and
*not independently significant*.

| Error pattern | Meaning |
|---|---|
| `failed to connect to controller due to not authorized` | OIDC token expired; client is re-authenticating |
| `failed to connect to controller due to failed to authenticate` | Harder auth failure after token expiry |
| `failed to get identity_data: no api session token set` | No valid session during re-auth window |
| `failed to get current edge routers: ... UNAUTHORIZED` | Can't fetch routers — no valid session |
| `ch[N] disconnected from edge router[...]` | Router channel dropped |

**Baseline:** the `not authorized` cluster fires as a burst around midnight UTC during the nightly OIDC token
refresh — ~48–52 occurrences per daily log file is normal. Counts elevated throughout the day (not just
midnight) indicate the controller was unreachable or rejecting sessions for an extended period.

A router disconnect (`ch[N] disconnected`) paired with immediate reconnect is normal. Multiple routers
dropping at the same time, or a router that does not reconnect, is worth flagging.

#### Category 2: Service / dial errors

These indicate application-level connection failures. **Always check whether elevated network errors explain
them first.** If network errors are normal but dial failures are elevated, the issue is service-side
(permissions, posture, terminator health) rather than network.

| Error pattern | Meaning |
|---|---|
| `ziti context is not authenticated, cannot connect to service[...]` | Dial attempted during auth gap — expected if network errors also elevated |
| `ziti dial failed: invalid state` | Paired with the above |
| `ziti_write() failed: invalid state` | Write on a torn-down connection — expected during auth refresh |
| `on_tcp_client_err() ... err=-14, terminating connection` | Client-side TCP reset — routine |

**Baseline:** a handful of dial failures per day during the midnight auth window is normal. Dial failures
spread across the day, *without* corresponding network errors, means the service itself is the problem —
look at posture policies, terminator health, or service configuration.

#### What to flag

- **Network errors elevated all day (not just midnight):** controller unreachable or auth service disrupted —
  likely an infrastructure event; check if the same spike appears across multiple machines on the same date
- **Same spike across multiple captures on the same date:** points to infrastructure, not the client machine
- **Dial failures elevated without matching network errors:** service-side issue — posture, terminators, or
  config
- **Any 2x+ day-over-day spike in either category:** flag the date and note whether it's isolated to one
  machine or shared

### 10. Write the report to a markdown file

Once all analysis is complete, write the full report to a markdown file inside the ticket folder:

```
<ticket_number>/<ticket_number>-analysis.md
```

The report should contain all findings from steps 6–9, structured as:

```markdown
# ZDEW Debug Report — Ticket <ticket_number>

**Source zip:** `<filename>`
**Captures:** N (single / aggregated)
**Generated:** <date>

---

## <capture_name> — <hostname>

### Crash / Stall Markers
...

### .ziti Analysis
...

### Service History
...

### Error Analysis
...

---

## Summary

One or two sentences calling out the most significant findings across all captures.
Flag anything the support engineer should act on or ask the customer about.
```

After writing the file, tell the user the path and give a one-line summary of the most important finding.
