# Manual Testing

These are the tests you need to make sure to perform per release.

## Test Cases

- Identity with dial access to a single service
- Identity with bind access to a single service
- Identity with dial access to a service with by TOTP posture check
- Identity with dial access to a service with by TOTP + time-based posture check
- Identity with dial access to a service with by TOTP + on-wake locking posture check

- Identity with dial access to a service with process-based posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check
- Identity with dial access to a service with by posture check


- Identity marked as disabled, is disabled on restart of zet
- Identity marked as enabled is enabled on restart
- Identity with overlapping service definitions in two different networks
- 
- Multiple ziti edge tunnels running at one time, intercepts are removed on clean shutdown


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

### Test cases
- add identity by url, turn off UI, ensure the identity needs ext auth
- add identity by url, restart zet, ensure the identity requires ext auth
- add identity by url, authenticate, turn off UI ensure identity does not indicate it needs auth
- add identity by url, authenticate, restart zet, ensure the identity requires ext auth when zet start


## Methodology

- establish an `.env.ps1` file located adjacent to `setup-ids-for-test.ps1` containing:

      $ZITI_USER="usernamehere"
      $ZITI_PASS="passwordhere"
- execute the `.\setup-ids-for-test.ps1`:
-- ClearIdentitiesOk is a mandatory flag that must be passed. the script cleans up the specified ZitiHome directory
   as well as cleans up the controller and resets identities, auth policies, external jwt signers etc.
-- Url is the url to the controller with or without the https
-- RouterName is the name of the router colocated with the controller. For the services to work, this router will be used
   to offload connections back to the controller management API for verifying the service works properly.
-- ZitiHome is the location of the jwts that will be produced as well as the location for certs/keys (pki-root)
-- ExternalId is the email address of whatever user you want to use with the IdP. Likely this will be your email address

      .\setup-ids-for-test.ps1 `
        -ClearIdentitiesOk `
        -Url "$zitiControllerAddress:$zitiControllerPort" `
        -RouterName "$zitiRouterName" `
        -ZitiHome "$pathToPlaceFiles" `
        -ExternalId "some.email@address.com"

-- Example execution:

      .\setup-ids-for-test.ps1 `
        -ClearIdentitiesOk `
        -Url "ctrl.cdaws.clint.demo.openziti.org:8441" `
        -RouterName "ip-172-31-47-200-edge-router" `
        -ZitiHome "C:\temp\zdew-testing\"" `
        -ExternalId "some.email@address.com"

## Verify MFA

- Minimally, enroll the identites: 
-- mfa-not-needed.jwt
-- mfa-0.jwt
-- mfa-normal.jwt
-- mfa-to.jwt
-- mfa-unlock.jwt
-- mfa-wake.jwt
-- normal-user-01.jwt

## 3rd Party CA
-- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user1.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user1.key
   with alias: `autoid01`
-- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user2.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user2.key
   with alias: `autoid02`
-- tpca-test-autoId.jwt using pki\tpca-test-autoId\certs\tpca-test-autoId-user3.cert and pki\tpca-test-autoId\keys\tpca-test-autoId-user3.key
   with alias: `autoid03`