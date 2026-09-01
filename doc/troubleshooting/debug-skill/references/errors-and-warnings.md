# Errors, warnings, and known misleading strings

Read this whenever you are about to characterize what the logs are complaining about. It carries the baselines
that stop normal churn being escalated, and the message catalog whose plain reading is wrong.

## Errors AND warnings

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

### Category 1: network / control plane

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

### Category 2: service / dial

Application-level failures. **Always check category 1 first.** If network errors are normal but dial
failures are elevated, the problem is service-side -- posture policies, terminator health, service config.

| Pattern | Meaning |
|---|---|
| `ziti context is not authenticated, cannot connect to service[...]` | Dial during an auth gap -- expected if category 1 is elevated |
| `ziti dial failed: invalid state` / `connection is closed` | Paired with the above |
| `ziti_write() failed: invalid state` | Write on a torn-down connection -- expected during auth refresh |
| `on_tcp_client_err() ... err=-13/-14, terminating connection` | Local teardown of an intercepted connection -- routine. See the lwIP note below. |
| `exceeded maximum retries creating circuit ... timeout waiting for message reply` | Circuit creation failing at the router -- real if category 1 is quiet |

### Category 3: routing and NRPT (WARN level -- easy to miss)

| Pattern | Meaning |
|---|---|
| `refresh_routes() failed to create exclusion route[IP]: 1168(...)` | Could not install the bypass route for a controller/router public IP. Periodic repetition means it never succeeded. On a machine with another VPN/ZTNA client owning the default route, missing exclusion routes are a plausible cause of "overlapping ranges" complaints. |
| `is_nrpt_policies_effective() NRPT policies are ineffective in this system` | ZDEW falls back to interface DNS. Expected on some managed builds; note it when DNS resolution is the complaint. |
| `failed to create route` / `failed to remove route` | Route table contention -- check for other tunnel adapters |

### What to flag

- Network errors elevated all day (not just midnight) -> controller/auth disruption; check whether the same
  spike appears in other captures on the same date
- The same spike across multiple captures on one date -> infrastructure, not this machine
- Dial failures elevated without matching network errors -> service-side
- Any 2x+ day-over-day spike in either category -> name the date, and say whether it's isolated or shared
- Any warning repeating at a fixed interval for days -> a retry loop that never succeeds; report it with a
  total count even when nothing else is wrong

## Known misleading strings

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
- **`uptime[Ns]`** in a `.ziti` dump -- per-identity context, not the process. See the main skill, step 6.
- **`Unknown adapter ziti-tun0`** in `ipconfig.all.txt` -- "Unknown" is just Windows' adapter-type label for
  the Wintun device. Not an error.
- **A missing log line** -- see "Before you start". Check coverage and log level before calling it evidence.

