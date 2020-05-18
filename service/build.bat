echo 1 > version
ziti-ci configure-git
pwd
cd service
pwd
dir

@echo build.bat starts
set SVC_ROOT_DIR=%~dp0

mkdir deps
cd %SVC_ROOT_DIR%deps
@echo deps created and cd'ed to

git clone https://github.com/netfoundry/ziti-tunneler-sdk-c.git
cd ziti-tunneler-sdk-c
git checkout update-submodule-to-https-vs-git

@echo Updating submodules...
git submodule update --init --recursive

SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c
set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%\install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%\install\lib

if not exist %TUNNELER_SDK_DIR%\build mkdir %TUNNELER_SDK_DIR%\build
if not exist %TUNNELER_SDK_DIR%\install mkdir %TUNNELER_SDK_DIR%\install

cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%\build -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%\install
cmake --build %TUNNELER_SDK_DIR%\build --target install

cp %TUNNELER_SDK_DIR%\install\lib\ziti.dll %SVC_ROOT_DIR%
cp %TUNNELER_SDK_DIR%\install\lib\libuv.dll %SVC_ROOT_DIR%

@echo COPIED dlls to %SVC_ROOT_DIR%
cd %SVC_ROOT_DIR%

REM go build -a ./ziti-wintun
go build -a ./ziti-tunnel

dir