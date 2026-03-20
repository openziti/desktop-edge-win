# Release Streams

This folder contains the release-streams for Ziti Desktop Edge for Windows. There are currently two streams:
* beta
* latest

The beta stream is updated with candidate builds, the latest stream is updated when we feel good that the beta
stream is stable enough to be promoted.

## Testing Automatic Upgrades

## Prerequisites

1. visual studio (currently 2022)
1. powershell
1. the latest advanced installer
1. (add any that are missed if there are any)

## Testing an Upgrade

This folder is also used when doing local development/upgrade testing. The basic flow is to:

* create an installer
* move the installer to release-streams/local/${version}
* update/create a release-streams/local.json file pointing to this release
* use python or some other simple webserver to serve up the release-streams/local folder as content

All these steps are automated for you if you have the prerequisites installed. Simply run 
`.\build-test-release.ps1` and provide a version to produce. For example, if 2.1.1 is deployed and
you want to test 2.2.1, you would run:
* `.\build-test-release.ps1 -version 2.2.1`
* cd `${project_root}/release-streams/local`
* `python -m http.server 8000`
* open the ZDEW, change the update url to http://localhost:8000/local.json and check for updates

For automatic upgrade testing, you will need to have the signing keys available and set the
$env:OPENZITI_P12_PASS. If you see errors indicating something like this:
```
ZitiUpdateService.Checkers.PeFile.SignedFileValidator   Could not verify certificate. ......
```
Then the binary is not signed properly. 
