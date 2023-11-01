# Upgrade Testing

this folder is used to test the automatic upgrade. Here's the basic process, hopefully the readme is accurate when you're
reading it...

## Prerequisites

1. visual studio (currently 2022)
1. powershell
1. the latest advanced installer
1. (add any that are missed if there are any)
 
## Test Steps

1. set these variables in your shell. For example:
    - `$env:ZITI_DESKTOP_EDGE_VERSION="2.1.18"`
    - `$env:ZITI_DESKTOP_EDGE_DOWNLOAD_URL="http://localhost:8000"`
1. start a simple http server in this directory (using WSL or whatever)
    - `python -m http.server 8000`
1. build an installer:
    - `${REPOSITORY_ROOT}\Installer\build.ps1`
1. copy the json file to this folder: `cp .\${env:ZITI_DESKTOP_EDGE_VERSION}.json .\ZitiUpdateService\upgradeTesting\version-check.json`
1. find the exe and copy it to this folder in a versioned way like:
    - `mkdir .\ZitiUpdateService\upgradeTesting\${env:ZITI_DESKTOP_EDGE_VERSION} -Force | Out-Null`
1. sdf
1. asdf
1. sdf
1. 

make/modify one of the json files here
start a server using python:
  * python -m http.server 8000
point your UI at the resultant .json:
  * http://localhost:8000/2.1.20.json
download a build from GitHub (or build one locally). unzip it into a folder in _this_ folder. update the json file returned during update checking
test the update functionality


