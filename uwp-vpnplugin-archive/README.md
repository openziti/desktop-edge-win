# ziti-client-windows

This is a Windows 10 vpn plugin that "almost" works with windows 10. The VPN API is very fragile 
and replacing the wrong element in the wrong place will likely result in errors.

Some notes:

* to connect to a TCP server the traffic seems to be required to leave the actual machine. you
  cannot use a locally listening socket for the tcpTransport

* using an empty list as opposed to null in StartWithMainTransport might result in unexpected out of
  memory erorrs.
  
* git@github.com:YtFlow/YtFlowTunnel.git at least compiles and deploys. never got it working but 
  might be useful

Testing:

once deployed (ideally using a remote machine so as to not risk your running windows) you should be able to 
get the 'conenct' and 'disconnect' buttons to function. You'll need to update ZitiVPNPlugin.DESIRED_HOST 
as that was using a linux vm.

You can run netcat on that linux vm using: nc -k -l 0.0.0.0 8900
Then you can see what routes are configured using: netstat -nr
If you run dnsmasq on that same linux VM you can set the vpnContext.DnsServer in MainPage.xaml.cs to the
same machine and influence what DNS results you get back and map those to whatever routes are added via the plugin

