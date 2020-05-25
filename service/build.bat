set SVC_ROOT_DIR=%~dp0

@echo fetching ziti-ci
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
ziti-ci version

@echo configuring git
@echo 1 > version
ziti-ci configure-git
pwd
cd service

SET REPO_URL=https://github.com/netfoundry/ziti-tunneler-sdk-c.git
SET REPO_BRANCH=update-submodule-to-https-vs-git

@echo cloning %REPO_URL%
git clone %REPO_URL%
IF %ERRORLEVEL% NEQ 0 @echo Could not clone git repo:%REPO_URL%

cd ziti-tunneler-sdk-c
git checkout %REPO_BRANCH%
IF %ERRORLEVEL% NEQ 0 @echo Could not checkout branch :%REPO_BRANCH%


@echo Updating submodules...
git submodule update --init --recursive
IF %ERRORLEVEL% NEQ 0 @echo Could not update submodules

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

@echo emitting version information
ziti-ci generate-build-info ziti-tunnel/version.go main

REM go build -a ./ziti-wintun
go build -a ./ziti-tunnel

@echo creating the distribution zip file
zip ziti-tunnel-win.zip *.dll ziti*exe

dir