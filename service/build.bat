@echo off
set SVC_ROOT_DIR=%~dp0
set /p BUILD_VERSION=<%SVC_ROOT_DIR%..\version

IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

@echo fetching ziti-ci
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
ziti-ci version

@echo configuring git
ziti-ci configure-git
pwd
cd service

SET REPO_URL=https://github.com/netfoundry/ziti-tunneler-sdk-c.git
SET REPO_BRANCH=update-submodule-to-https-vs-git
SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c
mkdir %TUNNELER_SDK_DIR%

@echo cloning %REPO_URL%
git clone %REPO_URL% %TUNNELER_SDK_DIR%
IF %ERRORLEVEL% NEQ 0 @echo Could not clone git repo:%REPO_URL%

cd ziti-tunneler-sdk-c
git checkout %REPO_BRANCH%
IF %ERRORLEVEL% NEQ 0 @echo Could not checkout branch :%REPO_BRANCH%


@echo Updating submodules...
git submodule update --init --recursive
IF %ERRORLEVEL% NEQ 0 @echo Could not update submodules

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
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot proceed
exit /b 1

:END
