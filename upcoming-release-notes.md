# Next Release
## What's New
* [Issue 818](https://github.com/openziti/desktop-edge-win/issues/818) - Added update progress UI
  * Update service sends progress and failure status to the UI during updates
  * Upgrade sentinel shows a dismissable progress dialog on user-triggered updates
  * Update failures are shown to the user instead of silently hanging
  * Automatic updates now relaunch the UI after the install completes if the UI was running prior to the update

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.14.6
* ziti-sdk:      1.14.3
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]
