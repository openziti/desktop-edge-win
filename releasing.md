# Making a Release

## Prerequisites

1. Visual Studio (currently 2022) / dotnet
1. Powershell
1. the latest [Advanced Installer](https://www.advancedinstaller.com/download.html)
1. [optional for automatic upgrade] two signing certificates:
    1. the OpenZiti signing cert/key/passphrase
    1. a legitimage 3rd party CA signer
1. (add any that are missed if there are any)

## Making a Release for Local Testing

First, you should probably bump the file that drives the [version](../version). The project does not follow the
[semver](https://semver.org/) versioning scheme exclusively but it follows it in spirit. Do not use these versions for
decisions related to the API/domain socket protocols used. Use your best judgement when bumping the version.

Creating a release for local testing is accomplished by running the [`build.ps1`](../Installer/build.ps1) Powershell script.
It should "just run" assuming you have the prerequisties. You'll need to set the environment variable: `OPENZITI_P12_PASS`
in order for the process to sign the built executable a second time. Set it using: `$env:OPENZITI_P12_PASS="__passphrase_here__"`

After the `build.ps1` script finishes, an executable will be produced at `Installer\Output`. You'll see output similar to:
```
Done Adding Additional Store
Successfully signed: C:\work\git\github\openziti\desktop-edge-win\Installer\Output\Ziti Desktop Edge Client-2.2.1.6.exe
========================== build.ps1 completed ==========================
=========== emitting a json file that represents this build ============
published_at resolved to: 2023-11-21T10:10:41Z
```

This installer can be executed manually/directly to test the installer and to test the deployed components.

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

### Making the Official Release

Once you've tested the build and feel confident it's ready to be released you're ready to make an actual release. To do this, do the following:
* make a new 'release' on github
* put up a pull request against the repo and change the associated stream/s: latest, stable, etc.
* test, this change by using the corresponding `release-next` raw url. For example if you are updating stable, use:

      https://raw.githubusercontent.com/openziti/desktop-edge-win/release-next/release-streams/stable.json

* Once tested, merge the pull request to main. Once merged the release will show in the stream


## Checklist

1. Verify the file at the root of the checkout named `version` is updated with the version number that is being released
1. Verify all pull requests are merged into `main` that should be released
1. Verify all changes have been accounted for in `release-notes.md` and verify that the file is accurate for the version being released. For example if producing release 10.10.10 the release notes should refer to the changes in 10.10.10. *[This step is often missed]*

## Performing the Release

1. Navigate to [Releases](https://github.com/openziti/desktop-edge-win/releases)
1. A github action should have already created a draft release with some changes listed within.
1. Verify these changes are correct.
1. In another window/tab navigate to [Actions](https://github.com/openziti/desktop-edge-win/actions)
1. On the left - click "Build Installer" under "Workflows" to filter down to only the Installer CI actions.
1. Find the installer built from the `main` branch and open it
1. Locate the execuable installer added to the job and download it. It'll have a name such as: ZitiDesktopEdgeClient-x.y.z and will be a zip file
1. After downloading the zip file, unzip it somewhere and verify there are two different files: the executable and a hash
1. Back on the [Releases](https://github.com/openziti/desktop-edge-win/releases) page - click the button to 'edit' the latest release (which should be marked 'Draft')
1. Veify the Tag Version and Release title match the version specified in the `version` file
1. Drag the unzipped files (two of them) onto the section of the form labeled: "Attach binaries by dropping them here or selecting them."
1. Copy the release notes markdown from the release-notes.md into the release.
1. When ready click "Publish Release"