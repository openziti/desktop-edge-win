# Making a Release

## Prerequisites

1. Visual Studio (currently 2022) / dotnet
1. Developer PowerShell for VS 2022
1. the latest [Advanced Installer](https://www.advancedinstaller.com/download.html)
1. [optional, necessary to test automatic upgrade] two signing certificates:
    1. the OpenZiti signing cert/key/passphrase
    1. a legitimage 3rd party CA signer
1. (add any that are missed if there are any)

## General Overview

Release notes are maintained in `upcoming-release-notes.md` as a working file. As changes are made between releases,
update this file with the relevant details. When a release is cut, `prepare-beta.ps1` adds the version header
and dependency info, and the publish action uses it for the GitHub release body. Release notes through 2.10.1.0
are archived in `release-notes-archive/`. Notes for later versions can be found on the
[Releases](https://github.com/openziti/desktop-edge-win/releases) page.

The ["Create Release"](https://github.com/openziti/desktop-edge-win/actions/workflows/publish.yml) action runs
`scripts/publish-release.sh` which validates `upcoming-release-notes.md`, creates a GitHub release, uploads artifacts,
and publishes the `win32crypto` build to JFrog.

After creating the release, verify the changelog looks correct in GitHub.

## Publishing a Release

Once satisfied with local testing (see below), to make a new release here are the rough steps to follow:

* ensure `upcoming-release-notes.md` is up to date with the changes for this release
* run `prepare-beta.ps1` with the version and optionally a new ZET version. If no ZET version is provided, the current version from `Installer/build.ps1` is used
  ```
  .\scripts\prepare-beta.ps1 -DesktopEdgeVersion <version> -ZetVersion <zet-version>
  .\scripts\prepare-beta.ps1 -DesktopEdgeVersion <version>
  .\scripts\prepare-beta.ps1 -DesktopEdgeVersion <version> -DryRun
  ```
* review and merge the PR to main. **DO NOT include any updates to the `release-streams` at this time**
* on merge to main the ["Build Installer"](https://github.com/openziti/desktop-edge-win/actions/workflows/installer.build.yml) action will fire. Find the action, download/test the build artifacts
* take note of the action's run ID. From the action page it should be shown as the top/last action run (e.g. the numeric ID at the end of the action URL)
* go to the ["Create Release" action](https://github.com/openziti/desktop-edge-win/actions/workflows/publish.yml), click "Run workflow" and enter the version and action run ID
* after the release is published, update the beta release streams with `scripts/promote.ps1`. Commit and push the changes in a follow-up PR
  ```
  .\scripts\promote.ps1 -Version <version> -To beta
  ```
* when the beta is validated and ready for general availability:
  ```
  .\scripts\promote.ps1 -Version <version> -To latest, stable
  ```


## Making a Release for Local Testing

First, you should probably bump the file that drives the [version](../version). The project does not follow the
[semver](https://semver.org/) versioning scheme perfectly but it follows it in spirit. Do not use these versions for
decisions related to the API/domain socket protocols used. Use your best judgement when bumping the version.

Creating a release for local testing is probably best accomplished by running the `scripts/build-test-release.ps1` script. This
script will automate much of the tedium associated with locally testing releases and allows for easy overriding of value.
This script will update the `release-streams/beta*.json` files as well, making it easier to publish a new release. You
will require the appropriate secrets if you want to locally test the automatic upgrade procedure as the OpenZiti signing
cert is mandatory to sign the executable for the upgrade process to start. The follow secrets will be necessary:

* $env:AWS_REGION="_region_"
* $env:AWS_KEY_ID="arn:aws:kms:_region__:_id__:key/_key_id__"
* $env:AWS_ACCESS_KEY_ID="_access_key_id_"
* $env:AWS_SECRET_ACCESS_KEY="_aws_secret_key_"
* $env:OPENZITI_P12_PASS_2024="_password_for_p12_" (and the .p12)

This example builds both the openssl and win32crypto versions:

```
$ver="<version>"
.\scripts\build-test-release.ps1 -url https://netfoundry.jfrog.io/artifactory/downloads/desktop-edge-win-win32crypto -version $ver -Win32Crypto:$true
.\scripts\build-test-release.ps1 -url https://github.com/openziti/desktop-edge-win/releases/download -version $ver -Win32Crypto:$false
```

After the installers finish they will output `deps-info.txt` files with dependency versions.


## Automatic Installation

### Testing

For years, the ZDEW has had automatic upgrade capabilities built into it. Testing the automatic upgrade __must__ always
be done before marking/deploying a release. Starting with the 2.2.1.x, the url used to discover updates has been exposed
to users, allowing for easier testing of the automatic upgrade process.

For the automatic upgrade to succeed, the executable __must__ meet the following criteria:
* the executable must be signed by the expected signing certificate
* the executable must have a sha256 which matches the executable produced
* the upgrade url must return a block of json. the json must be in this format, shown is the 2.1.16 release example:
 
      {
        "name": "2.1.16",
        "tag_name": "2.1.16",
        "published_at": "2023-03-14T20:41:27Z",
        "installation_critical": false,
        "assets": [
          {
            "name": "Ziti.Desktop.Edge.Client-2.1.16.exe",
            "browser_download_url": "https://github.com/openziti/desktop-edge-win/releases/download/2.1.16/Ziti.Desktop.Edge.Client-2.1.16.exe"
          }
        ]
      }

If you do not have the OpenZiti signer `OPENZITI_P12_PASS` variable set. When you build the installer you'll see
something like the following:

    Not calling signtool - env:OPENZITI_P12_PASS is not set

This is an indication that the exe was not signed by the `build.ps1` process, and this build will never work in
the automatic upgrade scenario. For the automatic upgrade to succeed, you'll need to make sure the expected 
signer (the one that signs the exe) signed the executable, see [SignedFileValidator.cs](../ZitiUpdateService/checkers/PeFile/SignedFileValidator.cs).

Once the build is created, you can change to this project and run a simple server such as:

    python -m http.server 8000

Then, update your locally running ZDEW and point it to something like: http://localhost:8000/release-streams/dev.json
