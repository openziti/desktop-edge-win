@echo off
powershell "Get-Process | where -Property Name -eq "ziti-tunnel" | select -ExpandProperty id" > zid
set /p zid= < zid
del zid
echo %zid%

"c:\Program Files\JetBrains\GoLand 2020.1.1\plugins\go\lib\dlv\windows\dlv.exe" attach %zid% --headless --log --only-same-user=false  --api-version=2 --accept-multiclient --listen=localhost:50000
