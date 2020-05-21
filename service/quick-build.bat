SET TUNNELER_SDK_DIR=c:\git\github\ziti-tunneler-sdk-c
set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%\install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%\install\lib

cls 
go build -a ./ziti-tunnel

copy *.dll c:\temp\ziti-windows-tunneler\
copy ziti-tunnel.exe c:\temp\ziti-windows-tunneler\