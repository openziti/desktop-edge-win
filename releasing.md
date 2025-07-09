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

In July 2025 a GitHub action was created that specifically publishes a release. The action simply runs the
script located in the root of the checkout named `publish-release.sh` using bash instead of Powershell. The
script is relatively straightforward. It requires specifying the branch containing the `publish-release.sh`,
which will likely always be `main`, the action id of the job to publish artifacts from, and the expected
version to be published. The version input is used to verify the expected action contains the expected artifact
version as a small check.

The script also expects the first (topmost) release-note entry to be the same version as input and found above
via the built artifacts. It will extract the changes, use the `gh` CLI, generate a release, and upload artifacts
to the release. It will also publish the `win32crypto` build to the NetFoundry JFrog repository (as of July 2025).

After creating the release, verify the changelog looks correct in GitHub.

## Publishing a Release

Once satisfied with local testing (see below), to make a new release here are the rough steps to follow:

* make a branch for code changes from main
* make code changes, including the version file, perform local testing
* push code changes and merge to main using a pull request. **DO NOT include any updates to the `release-streams` at this time**
* on merge to main the ["Build Installer"](https://github.com/openziti/desktop-edge-win/actions/workflows/installer.build.yml)
  action will fire. Find the action, download/test the build artifacts.
* take note of the action's id when downloading the artifacts. From the action itself it should be shown as the top/last action run.
  for example: https://github.com/openziti/desktop-edge-win/actions/runs/16150600186, that would have 'id': 16150600186.
* go to the ["Create Release" action](https://github.com/openziti/desktop-edge-win/actions/workflows/publish.yml)
* click "Run workflow" and enter inputs
  * branch: main
  * expected version to publish: enter expected version (the version needs to match the versions from the action id)
  * enter GitHub Actions run ID: enter the action id from above


## Making a Release for Local Testing

First, you should probably bump the file that drives the [version](../version). The project does not follow the
[semver](https://semver.org/) versioning scheme perfectly but it follows it in spirit. Do not use these versions for
decisions related to the API/domain socket protocols used. Use your best judgement when bumping the version.

Creating a release for local testing is probably best accomplished by running the `build-test-release.ps1` script. This
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
$ver="2.7.1.5"
.\build-test-release.ps1 -url https://netfoundry.jfrog.io/artifactory/downloads/desktop-edge-win-win32crypto -version $ver -Win32Crypto:$true
.\build-test-release.ps1 -url https://github.com/openziti/desktop-edge-win/releases/download -version $ver -Win32Crypto:$false
```

After the installers finish they will output `deps-info.txt` files. This file is useful to fill out the dependencies for
the README.

Example:
```
cat .\deps-info.txt

Dependencies from ziti-edge-tunnel:
---------------------------------------------
* ziti-tunneler: v1.7.3
* ziti-sdk:      1.7.4
* tlsuv:         v0.36.4[OpenSSL 3.5.0 8 Apr 2025]
```


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
