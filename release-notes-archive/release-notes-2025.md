# Release 2.9.0.0
## What's New
* Services now show whether they are Dial, Bind, or Both

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.3
* ziti-sdk:      1.10.4
* tlsuv:         v0.40.3[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.7.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.3
* ziti-sdk:      1.10.4
* tlsuv:         v0.40.3[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.6.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
* The ziti-sdk update fixes an issue that prevented ZDEW from authenticating with older controllers

## Dependencies
* ziti-tunneler: v1.10.1
* ziti-sdk:      1.10.1
* tlsuv:         v0.40.1[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.5.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.0
* ziti-sdk:      1.10.0
* tlsuv:         v0.40.1[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.4.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
* fixed an issue that caused service terminators to accumulate when hosted service configurations changed
* fixed a potential when hosted services become unavailable
* fixes API session refresh failure when connected with edge router versions 1.6-1.8

## Dependencies
* ziti-tunneler: v1.9.9
* ziti-sdk:      1.9.21
* tlsuv:         v0.40.1[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.3.0
## What's New
* Remove stats from main screen

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.8
* ziti-sdk:      1.9.20
* tlsuv:         v0.40.1[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.2.0
## What's New
* updated dependencies

## Bugs fixed:
* terminators are not removed when hosted service configurations are changed [openziti/ziti-tunnel-sdk-c#1024](https://github.com/openziti/ziti-tunnel-sdk-c/issues/1024)

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.8
* ziti-sdk:      1.9.20
* tlsuv:         v0.40.1[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.1.0
## What's New
* updated dependencies

## Bugs fixed:
* n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.6
* ziti-sdk:      1.9.17
* tlsuv:         v0.39.7[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.39.7[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.8.0.0
## What's New
* updated dependencies

## Bugs fixed:
* n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.5
* ziti-sdk:      1.9.16
* tlsuv:         v0.39.6[OpenSSL 3.6.0 1 Oct 2025]

# Release 2.7.6.0
## What's New
* updated dependencies

## Bugs fixed:
* n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.2
* ziti-sdk:      1.9.14
* tlsuv:         v0.39.6[OpenSSL 3.6.0 1 Oct 2025]

# Release 2.7.5.0
## What's New
* updated dependencies

## Bugs fixed:
* n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.0
* ziti-sdk:      1.9.12
* tlsuv:         v0.39.6[OpenSSL 3.6.0 1 Oct 2025]

# Release 2.7.4.0
## What's New
* [issue 875](https://github.com/openziti/desktop-edge-win/issues/875) - Allow removing identities whether disabled or MFA-enabled

## Bugs fixed:
* [issue 877](https://github.com/openziti/desktop-edge-win/issues/877) - New Identity with TOTP required cannot enable MFA

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.7.13
* ziti-sdk:      1.9.2
* tlsuv:         v0.38.1[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.3.1
## What's New
* n/a

## Bugs fixed:
This release updates the CSDK via the ziti-edge-tunnel to attempt to resolve a problem with OIDC-based auth
[issue 871](https://github.com/openziti/desktop-edge-win/issues/871) - Better errors with By URL identity adding

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.7.13
* ziti-sdk:      1.9.2
* tlsuv:         v0.38.1[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.3.0
## What's New
* Added "Add DNS" option back for legacy support. [See issue 865](https://github.com/openziti/desktop-edge-win/issues/865)

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.7.11
* ziti-sdk:      1.8.3
* tlsuv:         v0.37.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.2.1
## What's New
* Bugfix

## Other changes
* None

## Bugs fixed:
* [Issue 859](https://github.com/openziti/desktop-edge-win/issues/859) - Bad url in beta.json file caused no update without logging anything useful

## Dependencies
* ziti-tunneler: v1.7.11
* ziti-sdk:      1.8.3
* tlsuv:         v0.37.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.2.0
## What's New
* Bugfix

## Other changes
* None

## Bugs fixed:
* [Issue 825](https://github.com/openziti/desktop-edge-win/issues/825) - MFA needed via posture check prevents service listing

## Dependencies
* ziti-tunneler: v1.7.11
* ziti-sdk:      1.8.3
* tlsuv:         v0.37.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.8
## What's New
* Nothing

## Other changes
* None

## Bugs fixed:
* None

## Dependencies
* ziti-tunneler: v1.7.7
* ziti-sdk:      1.7.11
* tlsuv:         v0.37.3[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.7
## What's New
* New/added identities are no longer stored in the Windows keychain.

## Other changes
* None

## Bugs fixed:
* [Issue 852](https://github.com/openziti/desktop-edge-win/issues/852) - Adding identities fails on some hosts

## Dependencies
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.6
## What's New
* Nothing

## Other changes
* None

## Bugs fixed:
* [Issue 846](https://github.com/openziti/desktop-edge-win/issues/846) - UI crashes unexpectedly when enrolling a used JWT

## Dependencies
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.5
## What's New
* Nothing

## Other changes
* testing publish/updates procedure

## Bugs fixed:
* None

## Dependencies
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.4
## What's New
* Nothing

## Other changes
* testing publish/updates procedure

## Bugs fixed:
* None

## Dependencies
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.3
## What's New
* Nothing

## Other changes
* adjusting publish procedures

## Bugs fixed:
* None

## Dependencies
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.1.2
## What's New
* Nothing

## Other changes
* adjusted Copyright on multiple files

## Bugs fixed:
* [issue 813](https://github.com/openziti/desktop-edge-win/issues/813) alphabetizes the external jwt signer lists
* Discovered a bug when testing where auth would fail if the default provider was saved but no longer in the list

## Dependencies
* ziti-tunneler: v1.6.1.4
* ziti-sdk:      1.7.2
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]

# Release 2.7.0.2
## What's New
* Nothing

## Other changes
* fixed incorrect beta url

## Bugs fixed:
* none

## Dependencies
* ziti-tunneler: v1.6.1
* ziti-sdk:      1.6.6.1
* tlsuv:         v0.35.0.24[OpenSSL 3.4.0 22 Oct 2024]

# Release 2.7.0.1
## What's New
* removed any openssl FIPS-related material, fips.dll, removed screen from installer
* dependency updates

## Other changes
* removed the "Add DNS" checkbox. Users with it enabled will see it but on disable there will be no way to enable again

## Bugs fixed:
* *Dependency stability and bug fix updates
* [Issue 828](https://github.com/openziti/desktop-edge-win/issues/828) - unexpected UI crash when add identity fails

## Dependencies
* ziti-tunneler: v1.6.1
* ziti-sdk:      1.6.6.1
* tlsuv:         v0.35.0.24[OpenSSL 3.4.0 22 Oct 2024]

# Release 2.7.0
## What's New
* FIPS compliant cryptography - Ziti Desktop Edge for Windows now ships an optional fips.dll built
  by strictly adhering to the build steps outlined by the OpenSSH project. See the 
  [OpenSSL Source guide](https://openssl-library.org/source/). 
* dependency updates

## Other changes
N/A

## Bugs fixed:
Dependency stability and bug fix updates

## Dependencies
* ziti-tunneler: v1.6.0
* ziti-sdk:      1.6.5
* tlsuv:         v0.35.0[OpenSSL 3.4.0 22 Oct 2024]

# Release 2.6.5.0
## What's New
dependency updates

## Other changes
N/A

## Bugs fixed:
Stability and bug fixes

## Dependencies
* ziti-tunneler: v1.5.10
* ziti-sdk:      1.5.10
* tlsuv:         v0.33.9[OpenSSL 3.4.0 22 Oct 2024]

# Release 2.6.4.0
## What's New
dependency updates

## Other changes
N/A

## Bugs fixed:
Stability and other bug fixes

## Dependencies
* ziti-tunneler: v1.5.8
* ziti-sdk:      1.5.9
* tlsuv:         v0.33.9[OpenSSL 3.4.0 22 Oct 2024]

# Release 2.6.3.1
## What's New
dependency update

## Other changes
N/A

## Bugs fixed:
WSL->ZDEW scp was failing

## Dependencies
* ziti-tunneler: v1.5.4.2
* ziti-sdk:      1.5.4.1
* tlsuv:         v0.33.8[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.6.3.0
## What's New
dependency update

## Other changes
N/A

## Bugs fixed:
WSL->ZDEW scp was failing

## Dependencies

* ziti-tunneler: v1.5.4
* ziti-sdk:      1.5.4
* tlsuv:         v0.33.8[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.6.2.0

## What's New
OIDC-related bug fixes

## Other changes
N/A

## Bugs fixed:
* handle IdP using url-encoded chars in returned code
 
## Dependencies

* ziti-tunneler: v1.5.3
* ziti-sdk:      1.5.3
* tlsuv:         v0.33.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.6.1.0

## What's New
Bug fixes

## Other changes
N/A

## Bugs fixed:
* handle leak during process-based posture checks
 
## Dependencies

* ziti-tunneler: v1.5.2
* ziti-sdk:      1.5.1
* tlsuv:         v0.33.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.6.0.0

## What's New
Bug fixes

## Other changes
* Merged changes from 2.5.2.x stream for stall detector changes

## Bugs fixed:
* ext-jwt-signer with incorrect jwks url would silently fail auth. Now an error will be shown to the user
* join network by url no long hangs when the URL doesn't RST/fail/ack immediately
* jwt with invalid content no longer crashes the UI

## Dependencies
* ziti-tunneler: v1.5.1
* ziti-sdk:      1.5.0
* tlsuv:         v0.33.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.5.0

## What's New
dependencies updated with bug fixes

## Other changes
* n/a

## Bugs fixed:
* n/a

## Dependencies
* ziti-tunneler: v1.5.0
* ziti-sdk:      1.5.0
* tlsuv:         v0.33.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.4.0

## What's New
dependencies updated with bug fixes

## Other changes
* n/a

## Bugs fixed:
* n/a

## Dependencies

* ziti-tunneler: v1.4.5
* ziti-sdk:      1.4.4
* tlsuv:         v0.33.6[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.3.0

## What's New
dependencies updated to handle different IdP token usages (ID|Access)

## Other changes
* n/a

## Bugs fixed:
* n/a

## Dependencies

* ziti-tunneler: v1.4.2
* ziti-sdk:      1.4.1
* tlsuv:         v0.33.5[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.8

## What's New
dependencies

## Other changes
* a logging change for OIDC

## Bugs fixed:
* n/a

## Dependencies

* ziti-tunneler: v1.3.9
* ziti-sdk:      1.3.7
* tlsuv:         v0.33.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.9

## What's New
bug fix to stall detector

## Other changes
* n/a

## Bugs fixed:
* [issue 800 - stall detector can get stuck](https://github.com/openziti/desktop-edge-win/issues/800)

## Dependencies
* ziti-tunneler: v1.3.8
* ziti-sdk:      1.3.6
* tlsuv:         v0.33.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.8

## What's New
dependencies

## Other changes
* a logging change for OIDC

## Bugs fixed:
* n/a

## Dependencies

* ziti-tunneler: v1.3.9
* ziti-sdk:      1.3.7
* tlsuv:         v0.33.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.7

## What's New
dependencies

## Other changes
* n/a

## Bugs fixed:

* C SDK no longer applies `offline_access` scope
* C SDK no longer fails OIDC auth when external url ends with /

## Dependencies

* ziti-tunneler: v1.3.8
* ziti-sdk:      1.3.6
* tlsuv:         v0.33.4[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.6

## What's New
* properly handle secondary auth by ext jwt 

## Other changes
* n/a

## Bugs fixed:
* n/a

## Dependencies

* ziti-tunneler: v1.3.7
* ziti-sdk:      1.3.5
* tlsuv:         v0.33.2[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.5
## What's New
* nothing - bugfix

## Other changes
* dependency update

## Bugs fixed:
* none

## Dependencies

* ziti-tunneler: v1.3.7
* ziti-sdk:      1.3.5
* tlsuv:         v0.33.2[OpenSSL 3.3.1 4 Jun 2024]

# Release 2.5.2.4
## What's New
* nothing - bugfix

## Other changes
* [issue 779 - changed on hover to on click for initiating external provider auth](https://github.com/openziti/desktop-edge-win/issues/779)

## Bugs fixed:
* none

## Dependencies

* ziti-tunneler: v1.3.5
* ziti-sdk:      1.3.4
* tlsuv:         v0.33.1[OpenSSL 3.3.1 4 Jun 2024]
