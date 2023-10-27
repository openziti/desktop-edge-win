this folder is used to test the automatic upgrade.
make/modify one of the json files here
start a server using python:
  * python -m http.server 8000
point your UI at the resultant .json:
  * http://localhost:8000/2.1.20.json
download a build from GitHub (or build one locally). unzip it into a folder in _this_ folder. update the json file returned during update checking
test the update functionality