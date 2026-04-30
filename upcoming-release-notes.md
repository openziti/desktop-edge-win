# Release 2.10.6.0
## What's New
* [Issue 818](https://github.com/openziti/desktop-edge-win/issues/818) - Added update progress UI
  * Update service sends progress and failure status to the UI during updates
  * Upgrade sentinel shows a dismissable progress dialog on user-triggered updates
  * Update failures are shown to the user instead of silently hanging
  * Automatic updates now relaunch the UI after the install completes if the UI was running prior to the update
* updated to [ziti-edge-tunnel v1.16.0](https://github.com/openziti/ziti-tunnel-sdk-c/releases/tag/v1.16.0)

## Bugs fixed
* [Issue 823](https://github.com/openziti/desktop-edge-win/issues/823) - Fixed a crash when rapidly scrolling the main identity list

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.16.0
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]