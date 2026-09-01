# The `.ziti` dump

Read this whenever the capture has a `*.ziti` file. It is the version inventory, the certificate dates, and the
only view of controller, router, and connection health at the moment of capture.

`service/` holds zero or more `*.ziti` dumps -- **one per identity context**, and a machine with a dozen
enrolled identities may still have only one dump. Name which identity each dump covers; do not generalize
one identity's health to the machine.

Header block (first ~14 lines) gives the version inventory -- app version, C SDK, tlsuv/OpenSSL, sodium,
libuv, OS build, hostname, and dump time. Put these in the report; it's the first thing engineering asks
for. The `ziti-sdk` line carries the commit (`1.11.8(gb3e8377)`), which is what lets you read the exact source
that was running: `gh api repos/openziti/ziti-sdk-c/contents/library/<file>?ref=<commit>`.

The `tlsuv:` line also tells you which Windows build variant is installed. An OpenSSL version means the regular
build; its absence means the `-win32crypto` twin. You need this to download the right binary for
`references/crash-dumps.md`.

**Identity Cert block** -- `Not Before` / `Not After` on the identity certificate. Pull it always, not just for
crash tickets:

```bash
grep -aA6 "Identity Cert:" /abs/path/capture/service/*.ziti | grep -aiE "Not (Before|After)"
```

`Not Before` dates the last certificate renewal, which is a rare event (annual, firing 23–30 days before
expiry) and therefore a precise clock for correlating anything else that happened at the same moment -- see
`references/crash-dumps.md`. `Not After` tells you when the next renewal is due, so you can say which machines
are still exposed to whatever the last one triggered. A machine that spends its renewal window unauthenticated cannot renew at all,
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
  for a power resume or an auth burst at that same moment** (`references/errors-and-warnings.md`) -- all
  channels reconnecting simultaneously points at the local machine waking up, not at the routers.
  Channels reconnecting at
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
midnight-refresh baselines in `references/errors-and-warnings.md`.

