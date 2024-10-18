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