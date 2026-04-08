# Manual Testing

These are the tests you need to make sure to perform per release.

## Test Cases

- Identity with dial access to a single service
- Identity with bind access to a single service
- Identity with dial access to a service with TOTP posture check
- Identity with dial access to a service with TOTP + time-based posture check
- Identity with dial access to a service with TOTP + on-wake locking posture check
- Identity with dial access to a service with process-based posture check
- Identity marked as disabled, is disabled on restart of zet
- Identity with overlapping service definitions in two different networks
- Multiple ziti edge tunnels running at one time, intercepts are removed on clean shutdown
- Move identity files from `%SystemRoot%\System32\config\systemprofile\AppData\Roaming\NetFoundry` to a temp folder, restart the tunnel service, and verify those identities no longer appear in the UI

### Identity List Sorting
- Sort by Name ascending and descending, verify alphabetical order
- Sort by Status, verify enabled/disabled identities group correctly
- Sort by Services ascending: identities needing attention (timed out, MFA needed, ext auth needed) should appear first, then by service count
- Sort by Services descending: normal identities with the most services should appear first
- Change sort option, restart the UI, verify the sort preference is persisted

### Adding Identities
- by url - success
- by url - success with three or more ext-jwt-signers
- by url - url times out -- https://62a2d8fa-6ed4-4ca9-a939-db058b6696c3.production.netfoundry.io:8441/
- by url - url is totally invalid: "this is not valid whatsoever"
- by url - url is kinda correct: "https://google.com"
- by jwt

### ext-jwt-signers
- ext-jwt-signer is entirely correct
- ext-jwt-signer incorrect, specifically the external-auth url is invalid: "this is invalid"
- ext-jwt-signer incorrect, specifically the external-auth url never returns: https://62a2d8fa-6ed4-4ca9-a939-db058b6696c3.production.netfoundry.io:8441/
- ext-jwt-signer incorrect, url is not the root of a .well-known/openid-configuration endpoint
- single ext-jwt-signer: clicking authenticate auto-launches the browser without opening identity details
- add identity by url, turn off UI, ensure the identity needs ext auth
- add identity by url, restart zet, ensure the identity requires ext auth
- add identity by url, authenticate, turn off UI, ensure identity does not indicate it needs auth
- add identity by url, authenticate, restart zet, ensure the identity requires ext auth when zet starts

### Toast Notifications
- ext auth success while UI is minimized: toast appears with identity name
- ext auth success while UI is active: no toast (user can see the state change)
- ext auth failure while UI is minimized: toast appears with identity name
- ext auth failure while UI is active: in-app blurb appears
- start ext auth, abandon it, wait for tunnel to resend needs_ext_login: authenticate toast re-queued
- complete ext auth, restart ZET with UI minimized: authenticate toast should appear for the identity needing re-auth
- MFA needed while UI is minimized: toast appears with authenticate button, clicking it opens MFA screen
- multiple identities needing authorization at startup: single summary toast with correct count instead of one per identity
- rapidly disable/enable multiple MFA and ext auth identities with the UI minimized: after 5 seconds a single summary toast should appear with the correct count of identities requiring authorization

## Methodology

See [`BUILDING.md`](./BUILDING.md) for prerequisites and how to start a local quickstart network.

Create a `scripts/.env.ps1` file with your controller credentials:

```powershell
$ZITI_USER="usernamehere"
$ZITI_PASS="passwordhere"
```

Then run the setup script from the project root. This provisions identities, auth policies, external JWT signers, and services on the controller.

- `ClearIdentitiesOk` is mandatory. The script cleans up the specified ZitiHome directory as well as the controller (resets identities, auth policies, external jwt signers, etc.)
- `Url` is the address of the controller with or without https
- `RouterName` is the name of the router colocated with the controller. Services use this router to offload connections back to the controller management API
- `ZitiHome` is the directory where JWTs, certs, and keys are written
- `ExternalId` is the email address of the user you want to use with the IdP

Example using a local quickstart:

```powershell
.\scripts\setup-ids-for-test.ps1 `
    -ClearIdentitiesOk `
    -Url "https://localhost:1280" `
    -RouterName "router-quickstart" `
    -ZitiHome "C:\temp\ziti-quickstart" `
    -ExternalId "your.email@example.com"
```

Example using a remote controller:

```powershell
.\scripts\setup-ids-for-test.ps1 `
    -ClearIdentitiesOk `
    -Url "ctrl.cdaws.clint.demo.openziti.org:8441" `
    -RouterName "ip-172-31-47-200-edge-router" `
    -ZitiHome "C:\temp\zdew-testing\" `
    -ExternalId "your.email@example.com"
```

## Verify MFA

Minimally, enroll the following identities:
- mfa-not-needed.jwt
- mfa-0.jwt
- mfa-normal.jwt
- mfa-to.jwt
- mfa-unlock.jwt
- mfa-wake.jwt
- normal-user-01.jwt

## 3rd Party CA
- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user1.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user1.key with alias: `autoid01`
- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user2.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user2.key with alias: `autoid02`
- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user3.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user3.key with alias: `autoid03`

## Enable / Disable

- enable an ext-jwt-signer based identity then disable it
- authenticate to an ext-jwt-signer then disable, enable, reauth
- enable a cert-based auth identity then disable it, verify it auths, then disable it
- Identity marked as enabled is enabled on restart
- Identity marked as disabled is disabled on restart
- get a little crazy, disable/enable a bunch of identities - spaz out ... :)

## Random

- Delete the config file - verify things start up properly
