These release notes will catalog the changes in the Windows service. 
For UI changes or installer changes see [release-notes.md]()

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

  * [#102](https://github.com/openziti/desktop-edge-win/pull/102) - DNS requests with "connection-specific local domain" would not resolve

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
