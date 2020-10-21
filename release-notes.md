# Release 1.2.12

* What's New

  * [#147](https://github.com/openziti/desktop-edge-win/issues/147) - Added "collect-logs.ps1" to the installer. This script
    can be run to collect the logs files from the service. Must be run as administrator.

* Bug Fixes

  * 

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
