# Release 2.1.10

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [TSDK bug 566](https://github.com/openziti/ziti-tunnel-sdk-c/issues/566) - use case-insensitive comparision when looking up queried hostnames for DNS wildcard domains 

## Dependency Updates
* TSDK updated to 0.20.11 / CSDK 0.30.8

# Release 2.1.9

## What's New
* none

## Other changes:
*

## Bugs fixed:
* 

## Dependency Updates
* TSDK updated to 0.20.9 / CSDK 0.30.8

# Release 2.1.8

## What's New
* none

## Other changes:
*

## Bugs fixed:
*

## Dependency Updates
* TSDK updated to 0.20.6 / CSDK 0.30.2

# Release 2.1.7

## What's New
* none

## Other changes:
*

## Bugs fixed:
* [bug 571](https://github.com/openziti/desktop-edge-win/issues/571) - fix configuring automatic updates when no release is available

## Dependency Updates
* TSDK updated to 0.19.9 / CSDK 0.29.4

# Release 2.1.6

## What's New
* Automatic updates are now able to be disabled entirely. The user will still be notified updates exist

## Other changes:
*

## Bugs fixed:
*

## Dependency Updates
* TSDK updated to 0.19.9 / CSDK 0.29.4

# Release 2.1.5

## What's New
* DNS server IP has changed!!! If you expect the DNS server to be at 100.64.0.3 (or IP + 2) it will now be IP + 1. 
  You can also find the DNS IP by going to Main Menu -> Advanced Settings -> Tunnel Configuration
  
* It is no longer possible to have the DNS server overlap the IP assigned to the interface

## Other changes:
*

## Bugs fixed:
* [bug 560](https://github.com/openziti/desktop-edge-win/issues/560) - fix IP display on tunnel config page.
* [bug 562](https://github.com/openziti/desktop-edge-win/issues/562) - fix mfa incorrectly reported when administratively deleted.

## Dependency Updates
* TSDK updated to 0.19.9 / CSDK 0.29.4

# Release 2.1.4

## What's New
* nothing - bug fix release

## Other changes:
* [issue 545](https://github.com/openziti/desktop-edge-win/issues/545) incorrect reporting of app version
* [issue 298](https://github.com/openziti/desktop-edge-win/issues/298) removed legacy code to remove legacy wintun installer
* [issue 396](https://github.com/openziti/desktop-edge-win/issues/396) feedback.zip no longer tries to email itself

## Bugs fixed:
* [bug 541](https://github.com/openziti/desktop-edge-win/issues/541) and [issue 474](https://github.com/openziti/desktop-edge-win/issues/474) no warning when upgrading

## Dependency Updates
* TSDK updated to 0.19.7 / CSDK 0.29.4

# Release 2.1.3

## What's New
* nothing - bug fix release

## Other changes:
* none

## Bugs fixed:
* [bug 551](https://github.com/openziti/desktop-edge-win/issues/551) address an issue where not every process was allowed to be enumerated

## Dependency Updates
* TSDK updated to 0.19.2 / CSDK 0.29.2

# Release 2.1.2

## What's New
* nothing - bug fix release

## Other changes:
* none

## Bugs fixed:
* fixes a problem where the data service would crash on certain hosted services

## Dependency Updates
* TSDK updated to 0.18.16 / CSDK 0.28.11

# Release 2.0.1/2.1.1

## What's New
* nothing - bug fix release

## Other changes:
* ZDEW no longer captures and tries to use "Primary Dns Suffix", "Primary Dns Suffix" and "Connection-specific DNS Suffix". 
  All intercepts must be fully qualified now (they must contain a period. e.g. "myserver." or "myserver.ziti" not "myserver"

## Bugs fixed:
* Change the way NRPT rules test rules are counted to determine if NRPT is active

## Dependency Updates
* TSDK updated to 0.18.15 / CSDK 0.28.9

# Release 2.0.0

## What's New
* The data service which was go-based: `ziti-tunnel`, has been totally replaced with the C-based `ziti-edge-tunnel`

## Other changes:
* none

## Bugs fixed:
* Fix for the asynchronous calls
* [#515](https://github.com/openziti/desktop-edge-win/issues/515) UI logs are GIGANTIC on 1.12.x branch
* [#516](https://github.com/openziti/desktop-edge-win/issues/516) NUL chars passed to UI via ipc

## Dependency Updates
* TSDK updated to 0.17.24 / CSDK 0.26.27

---

# Release 1.11.5

## What's New
* none - this is a bugfix release

## Other changes:
* none

## Bugs fixed:
* UDP intercepts fix seems to have somehow affected DNS for some. reverting

## Dependency Updates
* TSDK updated to 0.15.25 / CSDK 0.26.26

# Release 1.11.4

## What's New
* none - this is a bugfix release

## Other changes:
* none

## Bugs fixed:
* UDP intercepts would never release the port. This fix adds a 30s timer to UDP traffic. If no traffic arrives at the port after 30s it will be closed. This addresses this error:

     unable to allocate UDP pcb - UDP connection limit is 512

## Dependency Updates
* TSDK updated to 0.15.26 / CSDK 0.26.29

# Release 1.11.3

## What's New
* none - this is a bugfix release

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* TSDK updated to 0.15.25 / CSDK 0.26.26

# Release 1.11.2

## What's New
* none - this is a bugfix release

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* TSDK updated to 0.15.24 / CSDK 0.26.25

# Release 1.11.1

## What's New
* supports reconfiguring an endpoint to point to a new controller address via ziti_api_event

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.10.14

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* TSDK updated to 0.15.23 / CSDK 0.26.22

# Release 1.10.13

## What's New
* The UI can now specify the api page size via the "Tunnel Configuration" page

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.10.11

## What's New
* updated dependencies per below to enable a new setting in the config file: ApiPageSize.
  ApiPageSize is used to determin how many results will be returned in pagination operations.
  Currently this is most useful for users who have many hundreds of services for any given
  identity. Default is set to 250.

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* TSDK updated to 0.15.22 / CSDK 0.26.11
