# Ziti Tunnel for Windows

(the uwp project which used to be here has moved to [uwp-vpnplugin-archive](.uwp-vpnplugin-archive) and is likely abandoned)

The Ziti Tunnel for Windows is an application that is necessary to integrate applications which cannot embed a Ziti SDK
directly into the application. This is colloquially known as a "brown field" Ziti-enabled application because the app
itself has no understanding that it has been Ziti-enabled.

In order for an application that has no knowledge of being Ziti-enabled to work the connections established by the app
must be intercepted before leaving the computer and routed through the Ziti network. This is accomplished by three main
components:

* [wintun](https://www.wintun.net) - provides a Layer 3 TUN for Windows
* A Windows service which runs as the local system account which creates the TUN as well as manages the Ziti connections
* A Windows UWP UI application that allows the interactively logged on user to interact with the Windows service

