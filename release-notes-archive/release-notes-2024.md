# Release 2.5.2.3
## What's New
* nothing - bugfix

## Other changes
* none

## Bugs fixed:
* [issue 776 - Asking for feedback log takes too long](https://github.com/openziti/desktop-edge-win/issues/776)

## Dependencies

* ziti-tunneler: v1.3.3
* ziti-sdk:      1.3.2
* tlsuv:         v0.32.9[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.2

## What's New
* Lots of new stuff in this release!
* OIDC Auth Code Flow + PKCE
* Add Identity button now supports adding an identity by JWT or by URl
    * JWT behavior remains the same
    * support has been added for joining a network by 3rd party CA
    * support added for joining an OpenZiti network v1.2+ by URL. Note, the URL must be
      preconfigured with trust from the OS trust store. Unverifiable URLs cannot be used.
* Keychain support is added! The OpenZiti C SDK uses the 
  [tlsuv library](https://github.com/openziti/tlsuv) which as integrated with 
  [Windows "Cryptography API: Next Generation"](https://learn.microsoft.com/en-us/windows/win32/seccng/cng-portal)
  to support storing private key material through OS API calls. While this can be disabled
  if __necessary__, it is enabled by default and should remain enabled unless you are sure
  that it shouldn't be.

## OIDC Auth Code flow + PKCE

If you are using an OpenZiti controller version 1.2 or higher, you are now able to use
an [External JWT Signer](https://openziti.io/docs/learn/core-concepts/security/authentication/external-jwt-signers/) to
authenticate to the overlay. When configured, you can join the network by using either the network 
JWT (downloaded from the ZAC or extracted from the controller's `/network-jwts` endpoint)

If there are more than one ext-jwt-signers configured, new controls on the item details page will let
the user configure a default external auth provider. When a default is configured, simply clicking the
new "authorize IdP" icon.

## Other changes
* removed "add identity" button from the bottom of the screen
* pointers now change to indicate an element is a drag point
* tooltips added to 'Z' icon
* right click on the main screen 'Z' icon to reattach a window
* various UI presentation improvements

## Bugs fixed:
* the UI now knows if it's connected or disconnected and shows the label appropriately
* when disabling the UI the lower portion no longer looks truncated

## Dependencies

* ziti-tunneler: v1.3.2
* ziti-sdk:      1.3.2
* tlsuv:         v0.32.9[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.1.2

## What's New
* nothing - bugfix

## Other changes
* none

## Bugs fixed:
* Rolls back the TLS engine to mbedTLS for now, so identities can write a new CA bundle if needed

## Dependencies

* ziti-tunneler:  v1.1.4.2
* ziti-sdk:       1.0.9

# Release 2.5.1.1

## What's New
* bugfix

## Other changes
* none

## Bugs fixed:
* [issue 760](https://github.com/openziti/desktop-edge-win/issues/760) - stall detector operated too quickly. tamed to 60s from 15s and allowed for configuration

## Dependencies

* ziti-tunneler: v1.3.2
* ziti-sdk:      1.3.2
* tlsuv:         v0.32.9[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.1.0

## What's New
* installer no longer verifies internet connectivity

## Other changes
* none

## Bugs fixed:
* none

## Dependency Updates

n/a

# Release 2.5.0.15

## What's New
* nothing yet

## Other changes
* none

## Bugs fixed:
* none

## Dependency Updates

ziti-tunneler: v1.2.5
ziti-sdk:      1.1.5
tlsuv:         v0.32.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.14

## What's New
* nothing yet

## Other changes
* none

## Bugs fixed:
* none

## Dependency Updates

* ziti-tunneler: v1.2.4
* ziti-sdk:      1.1.4
* tlsuv:         v0.32.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.13

## What's New
* nothing yet

## Other changes
* none

## Bugs fixed:
* none

## Dependency Updates

* ziti-tunneler: v1.2.3
* ziti-sdk:      1.1.3
* tlsuv:         v0.32.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.12

## What's New
* OIDC enabled, implementation (coming soon)
* Keychain integration and TPM enablemed, implementation (coming soon)

## Other changes:
* n/a

## Bugs fixed:
* n/a

## Dependency Updates
ziti-tunneler: v2.0.0-alpha24.11
ziti-sdk:      2.0.0-alpha29
tlsuv:         v0.32.2.1[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.10 & 2.5.0.11

## What's New
* n/a

## Other changes:
* n/a

## Bugs fixed:
* logging was overly verbose due to new healthchecking
* fixed log level setting

## Dependency Updates

ziti-edge-tunnel.exe version -v:
* *ziti-tunneler: v2.0.0-alpha24
* *ziti-sdk:      2.0.0-alpha23
* *tlsuv:         v0.31.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.9

## What's New
* n/a

## Other changes:
* n/a

## Bugs fixed:
* logging was overly verbose due to new healthchecking

## Dependency Updates

ziti-edge-tunnel.exe version -v:
* ziti-tunneler: v2.0.0-alpha22
* ziti-sdk:      2.0.0-alpha23
* tlsuv:         v0.31.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.8

## What's New
* n/a

## Other changes:
* n/a

## Bugs fixed:
* logging was broken

## Dependency Updates

ziti-edge-tunnel.exe version -v:
* ziti-tunneler: v2.0.0-alpha21
* ziti-sdk:      2.0.0-alpha23
* tlsuv:         v0.31.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.7

## What's New
* n/a

## Other changes:
* n/a

## Bugs fixed:
* n/a

## Dependency Updates

ziti-edge-tunnel.exe version -v:
* ziti-tunneler: v2.0.0-alpha20
* ziti-sdk:      2.0.0-alpha22
* tlsuv:         v0.31.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.6

## What's New
* Added stalled ziti-edge-tunnel detection. If the process doesn't respond for 15 seconds the monitor service will
  administratively terminate the process. Example log output shown below:

      [2024-09-17T22:27:20.980Z]  INFO	ZitiUpdateService.UpdateService	ziti-edge-tunnel aliveness check ends successfully	
      [2024-09-17T22:27:35.974Z]  WARN	ZitiUpdateService.UpdateService	ziti-edge-tunnel aliveness check appears blocked and has been for 1 times	
      [2024-09-17T22:27:40.975Z]  WARN	ZitiUpdateService.UpdateService	ziti-edge-tunnel aliveness check appears blocked and has been for 2 times	
      [2024-09-17T22:27:45.975Z]  WARN	ZitiUpdateService.UpdateService	ziti-edge-tunnel aliveness check appears blocked and has been for 3 times	
      [2024-09-17T22:27:45.975Z]  WARN	ZitiUpdateService.UpdateService	forcefully stopping ziti-edge-tunnel as it has been blocked for too long	
      [2024-09-17T22:27:45.975Z]  INFO	ZitiUpdateService.UpdateService	Closing the "data service [ziti]" process	
      [2024-09-17T22:27:45.975Z]  INFO	ZitiUpdateService.UpdateService	Killing: System.Diagnostics.Process (ziti-edge-tunnel)	

## Other changes:
* n/a

## Bugs fixed:
* n/a

## Dependency Updates

ziti-edge-tunnel.exe version -v:
* ziti-tunneler: v2.0.0-alpha19
* *ziti-sdk:      2.0.0-alpha21
* *tlsuv:         v0.31.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.0.3

## What's New
* none

## Other changes:
* added debug option to show when the data channel closes unexpectedly

## Bugs fixed:
* n/a

## Dependency Updates
* ziti-tunnel-sdk-c v2.0.0-alpha11/c sdk 2.0.0-alpha8

# Release 2.5.0.2

## What's New
* updated c-sdk/tunneler to work with HA controllers

## Other changes:
* none

## Bugs fixed:
* n/a

## Dependency Updates
* ziti-tunnel-sdk-c v2.0.0-alpha10/c sdk 2.0.0-alpha8

# Release 2.5.0.1

## What's New
* updated c-sdk/tunneler to work with HA controllers

## Other changes:
* none

## Bugs fixed:
* n/a

## Dependency Updates
* ziti-tunnel-sdk-c v2.0.0-alpha9/c sdk 2.0.0-alpha6

# Release 2.4.0.1

## What's New
* nothing

## Other changes
* none

## Bugs fixed:
* none

## Dependency Updates
* ziti-tunnel-sdk-c updated to v1.1.0/c sdk v1.0.7
  - fixes tight loops that could happen when connectivity to the controller is lost

# Release 2.4.0.0

## What's New
* nothing

## Other changes:
* `ziti-monitor` service will now forcefully terminate `ziti-edge-tunnel` if it doesn't respond within
  the timeout period (60s). If a timeout occurs, the process will be terminated, any `ziti-tun` devices
  will be removed (removing any routes along with it), and the NRPT will be cleaned up. This should 
  fix [issue 674](https://github.com/openziti/desktop-edge-win/issues/674).

## Bugs fixed:
* [issue 674](https://github.com/openziti/desktop-edge-win/issues/674) - `ziti-edge-tunnel` never stops and
  the any attempts to stop the service fail.

## Dependency Updates
* ziti-tunnel-sdk-c updated to v1.0.4/c sdk v1.0.5
  - fixes file:/ handling in identity files

# Release 2.3.1

## What's New

The automatic update process has changed! Prior to version 2.2.x, automatic upgrades were accomplished exclusively
through the `ziti-monitor` service making a REST request to the GitHub API url. With 2.2.x this process will change.
Now, users are able to define the endpoint which they want to pull releases from. One can always download and install
directly from the /releases page, however the release marked "latest" by GitHub will no longer be deployed to ZDEW
endpoints automatically.

Instead, the OpenZiti project will maintain two release streams:
* stable: https://get.openziti.io/zdew/stable.json
* latest: https://get.openziti.io/zdew/latest.json

The latest stream will always be the very latest build which consider a candidate to be moved to the stable branch. 
This branch is not to be considered "experimental", it is simply the latest candidate branch we have available. If 
there are other streams that are needed, we may publish other streams.

After a period of demonstrated stability and no critical bugs, the build will be promoted to the "stable" release 
stream.

A frequent question is around the administration of the URL. At this time, the URL is in control of the end-user 
entirely and not able to be centrally managed by the overlay network itself. It is the user's responsibility to update
the URL accordingly. The URL is controlled by the ZDEW UI, or by updating a file in the SYSTEM profile, by default 
located at: `%SystemRoot%\System32\config\systemprofile\AppData\Roaming\NetFoundry\ZitiUpdateService\settings.json`

Example contents of the file are as follows. Modify this file as needed and restart the `ziti-monitor` service for the 
changes to be effective, or use the UI to modify the file.
```
{
"AutomaticUpdatesDisabled": false,
"AutomaticUpdateURL": "https://get.openziti.io/zdew/latest.json"
}
```

The UI has been updated to contain a text box users can use to change the update url. If needed, users can reset the 
update URL to the default (`https://get.openziti.io/zdew/stable.json`) by clicking the 'reset' button on that form.

Using the UI will cause a check to be performed which will validate the supplied URL. An incorrect URL will result 
in updates not being found/applied. 

If a different URL is supplied, the URL must be available to the client or the save/commit will not succeed
As has always been the case, the executable supplied via the update URL, MUST be a binary signed and produced by 
OpenZiti. Random binaries/executables will are not acceptable. Only binaries signed by the expected OpenZiti signing 
certificate will be considered as genuine, and able to trigger the automatic update. These downloads can be obtained 
from GitHub via the /releases URL produced by the OpenZiti ZDEW build infrastructure

## Other changes:
* none

## Bugs fixed:
* none

## Dependency Updates
* ziti-edge-tunnel updated to v0.22.28/c sdk v0.36.10 / tlsuv v0.28.4
* System.Security.Cryptography.Pkcs from 6.0.1 to 6.0.3
