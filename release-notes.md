# Release 1.6.25

## What's New
* Filtering is now available on the detail page of identities

## Other changes:
* none

## Bugs fixed:
* [#287](https://github.com/openziti/desktop-edge-win/issues/287) - access fileshare via UNC path in Windows explorer very slow

## Dependency Updates
* updated TSDK/CSDK to v0.7.26.1
* updated .net logging to NLog 4.7.6

# Release 1.6.24

## What's New
* fixes to c sdk to better handle when the controller is unavailable at startup

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* updated TSDK/CSDK to v0.7.26/0.18.7

# Release 1.6.23

## What's New
* fixes to c sdk to better handle when the controller is unavailable at startup

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* updated TSDK/CSDK to v0.7.26/0.18.7

# Release 1.6.22

## What's New
* fixes [#274](https://github.com/openziti/desktop-edge-win/issues/274) - Added logging to all SC calls into the monitor service
* feedback now collects systeminfo and dnscache info
* added a "please wait" to the feedback option

## Other changes:
* none

## Bugs fixed:
* stability fixes when the monitor service is down the UI should not crash when trying to access the monitor service
* stability fixes from tsdk/csdk

## Dependency Updates
* updated TSDK/CSDK to v0.7.25/0.18.6

# Release 1.6.20

## What's New
* fixes [#274](https://github.com/openziti/desktop-edge-win/issues/274) - Added logging to all SC calls into the monitor service

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.6.19

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* fixes [#268](https://github.com/openziti/desktop-edge-win/issues/268) - Fixed UI crash when using Feedback button to collect logs and .eml file type not mapped
* fixes [#271](https://github.com/openziti/desktop-edge-win/issues/271) - Fixed UI crash when Monitor service was not running
* Fixed bug when "Service Logs" would also open the "Application Logs"

## Dependency Updates
* none

# Release 1.6.18

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* fixes [#266](https://github.com/openziti/desktop-edge-win/issues/266) - Fixes a crash on Windows Server 2016

## Dependency Updates
* Tunneler SDK C: v0.7.24

# Release 1.6.3-1.6.17

## What's New
* This is a maintenance release. Generally the only changes are around stability changes to the automatic update functionality

## Other changes:
* for developers a 'beta release' channel has been established allowing pre-releases to enter the release stream

## Bugs fixed:
* no specific bugs - some automatic updates would fail to shutdown the data service properly

## Dependency Updates
* none

# Release 1.6.2

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* stability updates via updated Tunneler SDK C

## Dependency Updates
* Tunneler SDK C: v0.7.20

# Release 1.6.1

## What's New
* 

## Other changes:
* none

## Bugs fixed:
* first time install bug - the NetFoundry folder would not exist after the logs were moved

## Dependency Updates
* none

# Release 1.6.0

## What's New
* upped version to 1.6.0 to represent log changes from 1.5.12

## Other changes:
* none

## Bugs fixed:
* walked back some changes trying to fix stability that seemed to decrease stability

## Dependency Updates
* C SDK-> 0.18.0
* Tunneler SDK C -> v0.7.18


# Release 1.5.12

## What's New
* logs condensed into a single log file - only ziti-tunneler.log files remain (cziti.logs are removed)
* clicking "Service Logs" will open the latest service log file. if ".log" is not mapped to a program the `${installFolder}\logs\service` folder will be opened
* clicking "Application Logs" will open the latest UI log file. if ".log" is not mapped to a program the `${installFolder}\logs\UI` folder will be opened
* closes [#254](https://github.com/openziti/desktop-edge-win/issues/254) - logs relocated to easier accessed location: "%ProgramFiles(x86)%\NetFoundry, Inc\Ziti Desktop Edge\logs"

## Other changes:
* collect-logs.ps1 has been removed in favor of logs being at a more accessible location and the 'feedback' button collecting logs anyway

## Bugs fixed:
* all logs now have valid timestamps
* fixes [#251](https://github.com/openziti/desktop-edge-win/issues/251) - timestamp in UI and service logs has incorrect format

## Dependency Updates
* C SDK updated to pick up log callback. unifies logs into one, fixes timestamp issue

# Release 1.5.11

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* fixes [#250](https://github.com/openziti/desktop-edge-win/issues/250) - setting the log level for the data service would not work

## Dependency Updates

* none

# Release 1.5.10

## What's New
* fixes [#201](https://github.com/openziti/desktop-edge-win/issues/201) - Feedback menu item will collect all logs

## Other changes:
* none

## Bugs fixed:
* fixes [#245](https://github.com/openziti/desktop-edge-win/issues/245) - every identity misidentified as orphaned on startup

## Dependency Updates

* none

## Known Issues

The automatic update functionality works - however the termination of the UI is not functioning properly. Each update restart the UI to get the latest UI code

# Release 1.5.9

## What's New
* closed [#242](https://github.com/openziti/desktop-edge-win/issues/242) - orphaned identities returned to service/ui on startup

## Other changes:
* none

## Bugs fixed:
* fixes [#243](https://github.com/openziti/desktop-edge-win/issues/243) - problem during initial install might cause the whole network to be blocked

## Dependency Updates

* update to ziti-tunnel-sdk-c v0.7.18 / ziti-sdk-c 0.17.20

# Release 1.5.8

## What's New
* closed [#234](https://github.com/openziti/desktop-edge-win/issues/234) - logs all now produced in UTC and formatted as time not delta from process start

## Other changes:
* none

## Bugs fixed:
* fixes [#222](https://github.com/openziti/desktop-edge-win/issues/222) - strange ipv6 response using nslookup
* fixes [#239](https://github.com/openziti/desktop-edge-win/issues/239) - services marked duplicate erroneously

## Dependency Updates

* update to ziti-tunnel-sdk-c v0.7.18 / ziti-sdk-c 0.17.20

# Release 1.5.7

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* fixes [#231](https://github.com/openziti/desktop-edge-win/issues/231) - overlapping hostnames do not receive a new ip
* fixes [#219](https://github.com/openziti/desktop-edge-win/issues/219) - obtain more DNS information to use when resolving DNS requests that do not terminate with a period

# Release 1.5.6

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* Another issue with auto-update resolved. The same version was set to update - same version should not update...

# Release 1.5.5

## What's New
* nothing - this build exists just to verify the auto update functionality works again. it is exactly the same as version 1.5.4

# Release 1.5.4

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* Another issue with auto-update resolved. 

# Release 1.5.3

## What's New
* identities disabled are now remembered when starting/stopping the service. the client can still see identities if id is disabled 

## Other changes:
* none

## Bugs fixed:
* NRE in ziti-monitor if no subscriptions exist

# Release 1.5.2

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* bug found when comparing versions. e.g.: 1.5.0 was considered newer than 1.5.0.0

# Release 1.5.1

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* fixes [#226](https://github.com/openziti/desktop-edge-win/issues/226) - update check fails on second run due to NRE

## Dependency Updates
* none

# Release 1.5.0

## What's New
* closes [#216](https://github.com/openziti/desktop-edge-win/issues/216) - The big change is that the big button now will send a message to the monitor service which will have the proper rights to stop and start the data service (`ziti`).

## Other changes:
* Changed the default mask to /10 as to not be different
* Changed the minimum allowable mask to be /16
* Migrate any masks > /16 to /16
* fixes [#220](https://github.com/openziti/desktop-edge-win/issues/220) - Alphabetize the service list

## Bugs fixed:
* fixes [#221](https://github.com/openziti/desktop-edge-win/issues/221) - Cleanup previous update files
* fixes [#218](https://github.com/openziti/desktop-edge-win/issues/218) - 0 length config cause panic
* fixes [#211](https://github.com/openziti/desktop-edge-win/issues/211) - segv on hosted service

## Dependency Updates
* update to github.com/openziti/sdk-golang v0.14.12

# Release 1.4.4
(skipped 1.4.3 by mistake)

## What's New

* none

## Bug Fixes

* Crash related to [ziti-sdk-c #171](https://github.com/openziti/ziti-sdk-c/issues/171)

## Dependency Updates

* update to ziti-tunnel-sdk-c v0.7.12

# Release 1.4.2

## What's New

* Changed the default mask to /16 from /24 to allow 65k services by default

## Bug Fixes

* Windows server reporting the wrong version for posture checks

## Dependency Updates

* update to github.com/openziti/sdk-golang v0.14.12

# Release 1.4.1

## What's New

* nothing

## Bug Fixes

* fixes a crash when services became unavailable

## Dependency Updates

* update ziti-tunneler-sdk-c to 0.7.10

# Release 1.4.0

## What's New

* Version bump to 1.4.0 to signify the inclusion of policy checks

## Bug Fixes

* none

## Dependency Updates

* update ziti-tunneler-sdk-c to 0.7.8

# Release 1.3.12

## What's New

(1.3.11 was released only as a dev/test release)

* Posture checks have been added
* config file is now backed before each save

## Bug Fixes

* cgo string leaks patched

## Dependency Updates

* Tunneler SDK updated to v0.7.8

# Release 1.3.10

## What's New

* Better logging in ziti-monitor. More logs at debug, a couple important ones at info
* clean up a warning or two

## Bug Fixes

* none

## Dependency Updates

* none

# Release 1.3.9

## What's New

* none

## Bug Fixes

* Disabling an identity with an IP intercept and re-enabling the identity would cause a crash
* Adding an identity when the controller was offline resulted in a UI error preventing the app from closing/continuing

## Dependency Updates

* none

# Release 1.3.8

## What's New

* When/if the service stops - all identities are removed from view and returned after the UI reconnects to the service

## Bug Fixes

* [#159](https://github.com/openziti/desktop-edge-win/issues/159) - Update UI if the detail page is open

## Dependency Updates

* update ziti-tunneler-sdk-c to 0.7.4

# Release 1.3.7

# What's New

* none

# Bug Fixes

* [#191](https://github.com/openziti/desktop-edge-win/issues/191) - Fix crash when controller is unavailable

# Dependency Updates

* none

# Release 1.3.6

# What's New

(Note 1.3.5 was not released due to 1.3.6 coming so quickly on the heels of 1.3.5)

* Automatic updates moved to 10 minutes by default (1.3.5)

# Bug Fixes

* Toggling an identity on/off would crash the service (1.3.5)
* Toggling an identity on/off/on/off after fixing the issue above would intercept and point to the wrong ip

# Dependency Updates

* none

# Release 1.3.5

skipped - 1.3.6 superseded this release

# Release 1.3.4

# What's New

* UI: When identity detail card is open you can now drag the window similar to the main window
* `ziti-monitor` log level changed to info by default

# Bug Fixes

* A bug with DNS resolution is fixed (no issue filed)

# Dependency Updates

* none

# Release 1.3.3

## What's New

    * nothing

## Bug Fixes

    * [#186](https://github.com/openziti/desktop-edge-win/issues/186) - All intercepts marked as already mapped

# Release 1.3.2

## What's New

    * [#184](https://github.com/openziti/desktop-edge-win/issues/184) - Better logging. ziti-monitor logging can now be configured via file.

## Bug Fixes

    * [#184](https://github.com/openziti/desktop-edge-win/issues/184) - Auto update no longer tries to update when the versions are the same

# Release 1.3.1

* What's New

    * This release exists only for testing the auto-upgrade capability

# Release 1.3.0

* What's New

  * Ziti Desktop Edge for Windows will now montior and install updates

* Bug Fixes

  * none

* Dependency Updates

  * None

# Release 1.2.13

* What's New

  * [#138](https://github.com/openziti/desktop-edge-win/issues/138) - DNS now listens on the same IP as the configured TUN
  * [#165](https://github.com/openziti/desktop-edge-win/issues/165) - The UI has been widened and the services now are stacked making it easier to read
  * [#167](https://github.com/openziti/desktop-edge-win/issues/167) - Change the logs folder to be available to all users not just administrators

* Bug Fixes

  * [#158](https://github.com/openziti/desktop-edge-win/issues/158) - Toggling an identity off that was not found crashes the service
  * [#96](https://github.com/openziti/desktop-edge-win/issues/96) - Toggling all identities off with multiple identities in the same network causes crash

* Dependency Updates

  * Updated to v0.7.2 of ziti-tunneller-sdk-c

# Release 1.2.12

* What's New

  * [#147](https://github.com/openziti/desktop-edge-win/issues/147) - Added "collect-logs.ps1" to the installer. This script
    can be run to collect the logs files from the service. Must be run as administrator.

* Bug Fixes

  * [#155](https://github.com/openziti/desktop-edge-win/issues/155) - When the registry key is set incorrectly (over 255) the 
    detection would fail. Changed the logic to accomodate incorrectly set values
  * [#145](https://github.com/openziti/desktop-edge-win/issues/145) - DNS Mask was erroneously overidden to 24 if anything other 
    than 24 was supplied

* Dependency Updates

  * Updated to v0.6.11
  
# Release 1.2.11

* What's New

  * [#135](https://github.com/openziti/desktop-edge-win/issues/135) - Support added for IPv4 intercepting. 
    Services can now be created for any IPv4 address.
  * [#123](https://github.com/openziti/desktop-edge-win/issues/123) - Windows can add ConnectionSpecificDomains 
    to DNS requests where no period (.) is in the DNS request such as "web-page" or "my-service". 
    These requests would not resolve properly because they would be received as "web-page.myConnectionSpecificDomain". 
    This now works correctly.
  * [#131](https://github.com/openziti/desktop-edge-win/issues/131) - Installer added to GitHub Actions build.

* Bug Fixes

  * [#130](https://github.com/openziti/desktop-edge-win/issues/130) - Detection of new release was inconsistent.
  * [#129](https://github.com/openziti/desktop-edge-win/issues/129) - DNS entry was not removed when service was deleted.
  
# Release 1.2.9

* What's New

  * adds support for Ziti-provided end to end encryption
  * upgrades the Ziti Desktop Edge Service to version 0.0.30
  * the log level can now be changed from the UI by going to Advanced Settings -> Set Logging Level
  * the config file has been slimmed
  * the name used to start/stop the Ziti Desktop Edge Service has been made much more convenient. 
    Now you can start and stop the service with: `net start|stop ziti` (this is the change that
    necessitates removing and reinstalling the application during upgrade from previous versions)

# Release 1.2.6

* What's New

  * 'About' updated to reflect actual service version
  * Added ability to set the log level of the service dynamically through Advanced Settings

* Bug fixes

  * [#102](https://github.com/openziti/desktop-edge-win/issues/102) - DNS requests with "connection-specific local domain" would not resolve

# Release 1.0.4

* What's New

  * [#94](https://github.com/openziti/desktop-edge-win/issues/94) - Added support for 'hosted' services

* Bug fixes

  * [#102](https://github.com/openziti/desktop-edge-win/issues/102) - DNS requests with "connection-specific local domain" would not resolve

# Release 0.0.30

* What's New

  * Continually improved logging
  * changed ip from 169.254.0.0/16 to 100.64.0.0/10
  * [#120](https://github.com/openziti/desktop-edge-win/issues/120) - Allow UI/client to get and set log level dynamically via ipc

* Bug fixes

  * [#116](https://github.com/openziti/desktop-edge-win/issues/116) - Removes information from the config that wasn't needed in config.json
  
# Release 0.0.29

* What's New
    
  * Continually improved logging
  * Add support for 'verbose' logging along with error, warn, info, debug, trace

* Bug fixes

  * [#123](https://github.com/openziti/desktop-edge-win/issues/123) - ConnectionSpecificDomains cause DNS lookup failures
  * [#117](https://github.com/openziti/desktop-edge-win/issues/117) - TLD were not resolving properly - fixed in 0.0.28 but marking resolved in 0.0.29

# Release 0.0.28

* What's New
    
  * Continually improved logging
   * Better DNS removal when services are no longer available or when an identity is removed

* Bug fixes

  * [#106](https://github.com/openziti/desktop-edge-win/issues/106) - DNS stops responding when changing wireless networks
  * [#121](https://github.com/openziti/desktop-edge-win/issues/121) - DNS queries take a long time after a computer wakes from sleep

# Release 0.0.27

* Bug fixes

  * [#119](https://github.com/openziti/desktop-edge-win/issues/119) - Service would not start when IPv6 was disabled via the Windows registry

# Release 0.0.24

* What's New

  * [#94](https://github.com/openziti/desktop-edge-win/issues/94) - Added support for 'hosted' services

* Bug fixes

  * [#102](https://github.com/openziti/desktop-edge-win/issues/102) - DNS requests with "connection-specific local domain" would not resolve

# Release 0.0.21-23 - nothing captured

# Release 0.0.20

* What's New

  * Nothing yet

* Bug fixes

  * [#85](https://github.com/openziti/desktop-edge-win/issues/85) - buffer DNS messages and panic/recover properly when network changes happen

# Release 0.0.19

* What's New

  * Nothing yet

* Bug fixes

  * [#82](https://github.com/openziti/desktop-edge-win/issues/82) - MTU was no longer sent to UI correctly
  * [#86](https://github.com/openziti/desktop-edge-win/issues/86) - Inconsistent treatment of DNS requests - all requests will be treated as absolute going forward
  * [#90](https://github.com/openziti/desktop-edge-win/issues/90) - UI will not reconnect to service if started before service

# Release 0.0.18

* What's New

  * [#70](https://github.com/openziti/desktop-edge-win/issues/70) - Version added to api to report when model changes occur
  
* Bug fixes

  * [#69](https://github.com/openziti/desktop-edge-win/issues/69) - reference counting for identities with access to the same service 

# Release 0.0.17

* What's New

  * [#70](https://github.com/openziti/desktop-edge-win/issues/70) - added api ApiVersion to TunnelStatusEvent 
  * [#71](https://github.com/openziti/desktop-edge-win/issues/71) - add .log to rolled over log files
  
* Bug fixes

  * [#59](https://github.com/openziti/desktop-edge-win/issues/59) - too many services blocked service from accepting connections from the UI
  * [#61](https://github.com/openziti/desktop-edge-win/issues/61) - identity shutdown needs to be on the uv loop (issue with forgetting identities)
  * [#63](https://github.com/openziti/desktop-edge-win/issues/63) - when service restarts and UI reconnects clear identities and let the service repopulate the UI
  * [#67](https://github.com/openziti/desktop-edge-win/issues/67) - set the MTU based on the value reported from the interface

# Release 0.0.16

- tracking lost

# Release 0.0.15

* What's New

* Bug fixes
  * [#51](https://github.com/openziti/desktop-edge-win/issues/51) - cziti log would never roll over. 
            now the cziti log rolls daily with a maximum of seven (7) log files

# All versions prior to 0.0.15

Changelog tracking began with 0.0.15 - all previous changes were not tracked. If interested please
review the commit history.
