SET TUNNELER_SDK_DIR=c:\git\github\ziti-tunneler-sdk-c

if not exist %TUNNELER_SDK_DIR%\build mkdir %TUNNELER_SDK_DIR%\build
if not exist %TUNNELER_SDK_DIR%\install mkdir %TUNNELER_SDK_DIR%\install

cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%\build -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%\install
cmake --build %TUNNELER_SDK_DIR%\build --target install

cp %TUNNELER_SDK_DIR%\install\lib\ziti.dll .
cp %TUNNELER_SDK_DIR%\install\lib\libuv.dll .

echo COPIED dlls to .

set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%\install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%\install\lib
REM go build -a ./ziti-wintun
go build -a ./ziti-tunnel

copy *.dll c:\temp\ziti-windows-tunneler\
copy ziti-tunnel.exe c:\temp\ziti-windows-tunneler\