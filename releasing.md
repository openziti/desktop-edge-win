# Making a Release

Making a point release is a manual process at this time. It's kept as a manual process but is quick to perform. There are a few things which must be done before a release can be considered ready.

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