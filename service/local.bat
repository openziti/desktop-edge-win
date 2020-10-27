REM call build.bat quick
net stop ziti
copy /y c:\git\github\openziti\desktop-edge-win\service\ziti-tunnel.exe "C:\Program Files (x86)\NetFoundry, Inc\Ziti Desktop Edge\ziti-tunnel.exe"
net start ziti
