# Ziti Desktop Edge for Windows

(the uwp project which used to be here has moved to [uwp-vpnplugin-archive](.uwp-vpnplugin-archive) and is likely abandoned)

The Ziti Desktop Edge for Windows is an application that is necessary to integrate applications which cannot embed a Ziti SDK
directly into the application. This is colloquially known as a "brown field" Ziti-enabled application because the app
itself has no understanding that it has been Ziti-enabled.

In order for an application that has no knowledge of being Ziti-enabled to work the connections established by the app
must be intercepted before leaving the computer and routed through the Ziti network. This is accomplished by three main
components:

* [wintun](https://www.wintun.net) - provides a Layer 3 TUN for Windows
* A Windows service which runs as the local system account which creates the TUN as well as manages the Ziti connections
* A Windows UWP UI application that allows the interactively logged on user to interact with the Windows service

Build steps

* Ensure gcc is set to MingW - this project has been tested with MingW 8 and 10 as of Aug 2021
* Set PROJECT_ROOT=<project_path> and SVC_ROOT_DIR=%PROJECT_ROOT%\desktop-edge-win\service\ path
* Go to service directory: "cd %SVC_ROOT_DIR%"
* Run "build.bat clean|quick" or quick.bat or clean.bat (Use quick, if you have already done a clean build, and you have the latest tsdk library in the _deps folder)
* To publish you will need to obtain the WinSign.p12 file and copy it to the "%PROJECT_ROOT%\desktop-edge-win\Installer" path. This is the signing certificate and that it's necessary to build the installer with this. If you don't have it - you need to talk to someone about obtaining it.
* Build Installer: cd to the "%PROJECT_ROOT%\desktop-edge-win" path and execute "powershell -file Installer\build.ps1"
* If build is successful, the installer and sha file will be generated in "%PROJECT_ROOT%\desktop-edge-win\Installer\Output"
