# Release 2.1.16

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* ziti-tunnel-sdk-c updated to v0.20.23/c sdk v0.31.4
  - fixes packet buffer leaks when ziti_write fails
  - fixes several memory leaks

# Release 2.1.15

## What's New
* none

## Other changes:
* TCP retransmissions from intercepted clients are now much less likely, thanks to TSDK changes that limit the number of
  pending written bytes (to the ziti connection) to 128k. TCP clients now experience back-pressure through the TCP receive
  window for proper flow control.

## Bugs fixed:
* [TSDK bug 611](https://github.com/openziti/ziti-tunnel-sdk-c/issues/611) - Release packet buffers for unparsable dns queries. This bug would eventually result in "pbuf_alloc" failures, which prevented the tunneler from intercepting packets.
* [CSDK PR 491](https://github.com/openziti/ziti-sdk-c/pull/491) - Avoid crash when writing to closed ziti connections.

## Dependency Updates
* Advanced Installer updated to 20.4.1
* ziti-tunnel-sdk-c updated to v0.20.22/c sdk v0.31.2

# Release 2.1.14

## What's New
* Ziti Desktop Edge for Windows can now be installed in an air-gapped (offline) environment

## Other changes:
* adds DNS flushing to tunneler

## Bugs fixed:
* none

## Dependency Updates
* Advanced Installer updated to 20.3.1
* ziti-tunnel-sdk-c updated to v0.20.20/c sdk v0.31.0

# Release 2.1.13

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [TSDK bug 585](https://github.com/openziti/ziti-tunnel-sdk-c/issues/585) - fix dns queries that contain '_', e.g. SRV lookups
* [CSDK bug 478](https://github.com/openziti/ziti-sdk-c/issues/478) - avoid disconnecting active channel due to latency timeout

## Dependency Updates
* TSDK updated to 0.20.18 / CSDK 0.30.9
* uv-mbed updated to 0.14.12

# Release 2.1.12

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [TSDK bug 585](https://github.com/openziti/ziti-tunnel-sdk-c/issues/585) - fix dns queries that contain '_', e.g. SRV lookups

## Dependency Updates
* TSDK updated to 0.20.16 / CSDK 0.30.8

# Release 2.1.11

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [TSDK bug 578](https://github.com/openziti/ziti-tunnel-sdk-c/issues/578) - interception for services with wildcard domain addresses could be connected to the wrong ziti service.

## Dependency Updates
* TSDK updated to 0.20.14 / CSDK 0.30.8
