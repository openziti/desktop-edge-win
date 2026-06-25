# Release 1.10.10

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* TSDK updated to 0.15.21

# Release 1.10.9

## What's New
* none

## Other changes:
* Whitelisting.md is added. The executables for ziti, have to be whitelisted in McAfee, so it will not mark the software as a thread. The steps are mentioned in the whitelisting.md file.

## Bugs fixed:
* [#480](https://github.com/openziti/desktop-edge-win/issues/480) Clean up old adapters

## Dependency Updates
* none

# Release 1.10.8

## What's New
* none

## Other changes:
* UI layout fix : set max size and fix margins for identity list on menu

## Bugs fixed:
* [#476](https://github.com/openziti/desktop-edge-win/issues/476) WDE creates the tun adapter and fails, when the old one is in hung state. It should clean up the adaptors that failed to assign ip

## Dependency Updates
* update t-sdk v0.15.20 and c-sdk 0.26.10

# Release 1.10.7

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* go mod tidy run - many dependency updates
* wintun updated from 0.14 to 0.14.1
* update t-sdk v0.15.19 and c-sdk 0.26.9

# Release 1.10.6

## What's New
* none

## Other changes:
* Notification Title correction

## Bugs fixed:
* [#458](https://github.com/openziti/desktop-edge-win/issues/458) When user start tunnel and then immediately stop it, WDE crashes because of null value in the context
* [#461](https://github.com/openziti/desktop-edge-win/issues/461) ZDEW crashes when controller/router certificate expires
* [#464](https://github.com/openziti/desktop-edge-win/issues/464) nil reference issue in WDE when c-sdk sends the service events with nil data

## Dependency Updates
* update t-sdk v0.15.18 and c-sdk 0.26.9

# Release 1.10.5

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#452](https://github.com/openziti/desktop-edge-win/issues/452) Send identity updated events after mfa verify, mfa auth and service events

## Dependency Updates
* update t-sdk v0.15.17 and c-sdk 0.26.8

# Release 1.10.4

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#451](https://github.com/openziti/desktop-edge-win/issues/451) The Auth events were missing for the mfa enabled identity, when user put the laptop to sleep for more than 30 minutes. It is failing only for latest Network controllers.

## Dependency Updates
* update t-sdk v0.15.16 and c-sdk 0.26.6

# Release 1.10.3

## What's New
Services with multi factor authentication posture checks will give interface queues and windows notifications when the services are timing out for an identity. A timer icon will appear and a message when the services will be timing out under 20 minutes. Once a service times out, the value on the identity list will display the amount of services which are not available and the timer will turn to an actionable lock on the details page to signify that it is not available. A windows notification that can be clicked to re-authenticate, will let the user know when all of the services time out.

## Other changes:
* [#440](https://github.com/openziti/desktop-edge-win/issues/440) Send status after the wake or unlock
* [#443](https://github.com/openziti/desktop-edge-win/issues/443) WDE should send 2 new controller events to UI to capture the controller state - connected and disconnected 
* [#446](https://github.com/openziti/desktop-edge-win/issues/446) Send MFA auth_challenge event when controller is waiting for MFA code. UI should handle the new event and show the MFA lock icon

## Bugs fixed:
* none

## Dependency Updates
* update wintun to 0.13, update t-sdk v0.15.15 and c-sdk 0.26.5

# Release 1.10.2

## What's New
* none

## Other changes:
* [#430](https://github.com/openziti/desktop-edge-win/issues/430) Send notification if WDE receives the service updates with timeout that is less than 5 minutes

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.10.1

## What's New
* none

## Other changes:
* [#421](https://github.com/openziti/desktop-edge-win/issues/421) Calculate timeoutRemaining based on the service updates time or mfa auth time

## Bugs fixed:
* none

## Dependency Updates
* updated c-sdk to v0.26.3, updated t-sdk to v0.15.14

# Release 1.10.0

## What's New
* mfa timeout process. ZDE will prompt the user to enter MFA token, when the timeout set for the services is about to expire. Sends notification periodically until MFA token is entered
* [#418](https://github.com/openziti/desktop-edge-win/issues/418) notification has to be sent based on timeout remaining field. When User enters auth Mfa, the timer will reset to original timeout field. timeout remaining field will also be reset to the value in timeout field
* [#278](https://github.com/openziti/desktop-edge-win/issues/278) inform the user an update is available before automatically updating. So user can manually install the latest version anytime within 2 hours after the release is published. If a major/minor version has changed, then the auto installation will start immediately. If the user does not initiate manual installation within the given time, a warning will be displayed and after 2 hours the ZDE will auto update.

## Other changes:
* [#381](https://github.com/openziti/desktop-edge-win/issues/381) Open Ziti UI on startup.
* [#415](https://github.com/openziti/desktop-edge-win/issues/415) The notification frequency should be between 5 and 20 minutes. ZDE accepts the requests to modify it (UI is not ready yet). 20 minutes before the timeout, it should start sending the notification to UI

## Bugs fixed:
* none

## Dependency Updates
* updated c-sdk to v0.25.5, updated t-sdk to v0.15.10

# Release 1.9.11

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* tsdk is updated to v0.15.13, c-sdk 0.24.3

# Release 1.9.10

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#408](https://github.com/openziti/desktop-edge-win/issues/408) Toggling an identity ON (true) responds with Active:false
* [#403](https://github.com/openziti/desktop-edge-win/issues/403) 0 DNS questions in a Response causes crash

## Dependency Updates
* wintun updated to 0.12

# Release 1.9.9

## What's New
* none

## Other changes:
* [#387](https://github.com/openziti/desktop-edge-win/issues/387) DNS clean up and dns responses are delayed
* old signing certificate is removed, here forward 'old' clients cannot auto-update. they must uninstall/reinstall

## Bugs fixed:
* none

## Dependency Updates
* tsdk is updated to v0.15.8, c-sdk 0.24.3

# Release 1.9.8

## What's New
* none

## Other changes:
* Signing process updates

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.9.7

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* tsdk is updated to v0.15.7, c-sdk 0.23.3

# Release 1.9.6

## What's New
* [#378](https://github.com/openziti/desktop-edge-win/issues/378) Cleanup old ziti network adapter profiles

## Other changes:
* updated auto-installation config
* renewed signing cert
* added dns cache output back to feedback.zip
* set lower metric, if dns server property is set
* Detect power events

## Bugs fixed:
* none

## Dependency Updates
* tsdk is updated to v0.15.6, c-sdk 0.22.5
* use wintun[0.11](https://www.wintun.net/builds/wintun-0.11.zip)

# Release 1.9.5

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#360](https://github.com/openziti/desktop-edge-win/issues/360) Display issue when no cidr is used
* [#366](https://github.com/openziti/desktop-edge-win/issues/366) ZDE app does not prompt for the mfa token after starting the laptop from sleep
* [#367](https://github.com/openziti/desktop-edge-win/issues/367) Refresh tun config when user updates them

## Dependency Updates
* use wintun[0.10.3](https://www.wintun.net/builds/wintun-0.10.3.zip)
* tsdk is updated to v0.15.4, c-sdk 0.22.5

# Release 1.9.4

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#362](https://github.com/openziti/desktop-edge-win/issues/362) Update tun ip, mask and addDns flag
* [#364](https://github.com/openziti/desktop-edge-win/issues/364) Add the test nrpt policy function in the startup method

## Dependency Updates
* none

# Release 1.9.3

## What's New
* Page Service List
* Sort Service on Identity Details Page
* Consistent UX for MFA Screens

## Other changes:
* Service to UI interaction is now with one bulk update of services instead of one event per service update
* Added a configuration option to control if DNS is applied to the TUN. Some users are having issues with NRPT-only working. For now we'll add a boolean that allows the user to control if DNS should be added to the TUN

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.9.2

## What's New
* [#322](https://github.com/openziti/desktop-edge-win/issues/322) Ability to toggle identity, set loglevel and generate feedback zip file from cmd line

## Other changes:
* none

## Bugs fixed:
* [#346](https://github.com/openziti/desktop-edge-win/issues/346) Fixed the UI filtering of services on the Identity detail screen
* [#348](https://github.com/openziti/desktop-edge-win/issues/348) IP addresses do not need to be added to the NRPT
* [#349](https://github.com/openziti/desktop-edge-win/issues/349) Too many services can cause the NRPT update to fail

## Dependency Updates
* none

# Release 1.9.1

## What's New
* NRPT rules will be created matching all "Connection Specific Domains" discovered. This should allow for unqualified
  names to be properly resolved
* TUN will no longer have an assigned DNS server. This will prevent a large number of DNS requests from being sent to
  the ZitiTUN to be proxied. Now only DNS requests matching an NRPT rule will land at the ZitiTUN DNS Server

## Other changes:
* After TUN creation the interface metric will be set to 255 to persuade Windows to send
  all DNS requests to an interface that is not the TUN first
* Removed dnscache.txt from feedback zip

## Bugs fixed:
* [#332](https://github.com/openziti/desktop-edge-win/issues/332) Logs from csdk/tunneler are missing
* [#340](https://github.com/openziti/desktop-edge-win/issues/340) auth mfa verify icon is missing at ZDE startup
* Fixed a bug where process posture checks were case sensitive

## Dependency Updates
* Tunneler SDK to v0.15.1(CSDK 0.22.0)
* All go dependencies updated - numerous changes see commit log for details

# Release 1.9.0

## What's New
* MFA functionality has been implemented and works with later versions of the Ziti Controller (18.5+). [A brief overview is here](doc/mfa/mfa.md)

## Other changes:
* UI: changed the icon to show the "white" icon when off, green when on.

## Bugs fixed:
* fixed a bug with the monitor service indicating it was using zulu time - when it was not

## Dependency Updates
* Tunneler SDK to v0.14.0/CSDK 0.22.0

# Release 1.8.4

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* Fixed a bug from the CSDK handling hosted services
* [#330](https://github.com/openziti/desktop-edge-win/issues/330) Fixed issue intercepting connections when the configured IP is not exactly in 100.64.0.0/10
* [#328](https://github.com/openziti/desktop-edge-win/issues/328) Print a warning when the configured IP is not in the carrier grade NAT range 100.64.0.0/10

## Dependency Updates
* Tunneler SDK to v0.11.10/CSDK 0.20.22

# Release 1.8.3

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* Fixed a bug from the CSDK handling posture checks

## Dependency Updates
* Tunneler SDK to v0.11.9/CSDK 0.20.21

# Release 1.8.2

## What's New
* [#317](https://github.com/openziti/desktop-edge-win/issues/317) command line list function to fetch identities and services

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.8.0

## What's New
* DNS resolution has *CHANGED*. Users have had issues with the proxied DNS requests at times leading to an
  experience that was frustrating at time. Restarting the client fixed the problem but is also not what we want.
  Now the Ziti Desktop Edge for Windows will add NRPT rules and only send intercepted services to the resolver.
  The resolver will still proxy requests it does not know but fewer requests should need to be made to the internal
  DNS resolver.
* The internal DNS resolver no longer needs to be the primary DNS resolver on all interfaces due to the change
  mentioned above  

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.7.10

## What's New
* none

## Other changes:
* none

## Bugs fixed:
* [#313](https://github.com/openziti/desktop-edge-win/pull/313) Add identity button missing

## Dependency Updates
* none

# Release 1.7.9

## What's New
* Stopping the data service (`ziti`) using the big button no longer shows the old warning asking to start the service or exit the UI.
  Now the expected behavior is to see the button "off" which is used to turn the tunnel back "on"

## Other changes:
* none

## Bugs fixed:
* [#310](https://github.com/openziti/desktop-edge-win/pull/310) Restore identities moved by Windows after Windows system update

## Dependency Updates
* Tunneler SDK to v0.8.21/CSDK 0.20.13

# Release 1.7.8

## What's New
* a new DNS probe record was added to the DNS server to allow DNS-related testing
* ziti-monitor service now probes the DNS server for diagnostic reasons
* added code to check upgrade status - only useful when the ziti-monitor service is not running

## Other changes:
* minor logging updates

## Bugs fixed:
* fixed an issue with hosting connections after channel failure [CSDK #233](https://github.com/openziti/ziti-sdk-c/pull/233)
* fixed a UI issue when no identites existed

## Dependency Updates
* Tunneler SDK to v0.8.17/CSDK 0.20.12

# Release 1.7.7

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* fixed crash on udp message before dial completes

## Dependency Updates
* Tunneler SDK to v0.8.15

# Release 1.7.6

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* fixed crash on failed session

## Dependency Updates
* Tunneler SDK to v0.8.12

# Release 1.7.5

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* fixed crash on write when dial failed

## Dependency Updates
* Tunneler SDK to v0.8.10

# Release 1.7.4

## What's New
* nothing

## Other changes:
* none

## Bugs fixed:
* double free in TSDK caused a crash

## Dependency Updates
* Tunneler SDK to v0.8.10

# Release 1.7.3

## What's New
* Additional card Main Menu -> Identities was added for situations when the UI scrolls off the top of the screen
* Feedback button continues to collect additional diagnostic data. Also invokes ziti_dump now and puts output into the logs folder

## Other changes:
* none

## Bugs fixed:
* Some users were stuck with a TUN already created - rearranged the logic to try to always cleanup the TUN if needed

## Dependency Updates
* Tunneler SDK to v0.8.9
* CSDK updated to 0.20.7

# Release 1.7.2

This is a substantial update. Some important stability fixes have been applied from the CSDK and Tunneler SDK. Wintun was upgraded to 0.10 removing the need for the OpenZitiWintunInstaller

## What's New
* [#276](https://github.com/openziti/desktop-edge-win/issues/276) Updates for new CDSK eventing api
* [#279](https://github.com/openziti/desktop-edge-win/issues/279) DNS is now flushed on starting the `ziti` service to ensure dns cache is not a problem
* [#264](https://github.com/openziti/desktop-edge-win/issues/264) `ziti` data service no longer blocks waiting for identities to load
* app now uses the ziti_set_app_info function to report app information to controller

## Other changes:
* none

## Bugs fixed:
* DNS proxying would sometimes break depending on when and how a network outage occurred

## Dependency Updates
* Wintun updated to 0.10.0
* Tunneler SDK to v0.8.3
* CSDK updated to 0.20.3

# Release 1.7.0

This is a substantial update. Some important stability fixes have been applied from the CSDK and Tunneler SDK. Wintun was upgraded to 0.10 removing the need for the OpenZitiWintunInstaller

## What's New
* [#276](https://github.com/openziti/desktop-edge-win/issues/276) Updates for new CDSK eventing api
* [#279](https://github.com/openziti/desktop-edge-win/issues/279) DNS is now flushed on starting the `ziti` service to ensure dns cache is not a problem
* [#264](https://github.com/openziti/desktop-edge-win/issues/264) `ziti` data service no longer blocks waiting for identities to load
* app now uses the ziti_set_app_info function to report app information to controller

## Other changes:
* none

## Bugs fixed:
* DNS proxying would sometimes break depending on when and how a network outage occurred

## Dependency Updates
* Wintun updated to 0.10.0
* Tunneler SDK to v0.8.3
* CSDK updated to 0.20.3

# Release 1.6.28

## What's New
* ziti-monitor service set to "Automatic (Delayed Start)". Some users have noticed the monitor service does not start on boot. This is unexpected. To try to combat this problem the monitor service is going to be set to delayed start.
* [#291](https://github.com/openziti/desktop-edge-win/issues/291) ziti-monitor now attempts to collect the external ip address when submitting troubleshooting information

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* none

# Release 1.6.27 (also: 1.6.25, 1.6.26)

## What's New
* Filtering is now available on the detail page of identities

## Other changes:
* none

## Bugs fixed:
* [#287](https://github.com/openziti/desktop-edge-win/issues/287) - access fileshare via UNC path in Windows explorer very slow

## Dependency Updates
* updated TSDK/CSDK to v0.7.26.2
* updated .net logging to NLog 4.7.6
