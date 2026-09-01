# Fleet comparison

Read this once every capture has been analyzed individually, whenever the ticket carries more than one. For an
aggregated or fleet ticket this is where the answer usually is: one machine tells you what broke, the set tells
you *why*.

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

