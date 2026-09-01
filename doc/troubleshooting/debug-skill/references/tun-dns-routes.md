# Tun adapter, DNS range, and routes

Read this for any ticket about DNS resolution, an unexpected IP range, unreachable services, or a suspected
conflict with another VPN or ZTNA client.

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

