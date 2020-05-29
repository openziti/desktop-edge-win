@echo off
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version
cd %ZITI_TUNNEL_WIN_ROOT%

IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

@echo fetching ziti-ci
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
ziti-ci version

@echo generating version info - this will not be pushed
ziti-ci generate-build-info --useVersion=false %SVC_ROOT_DIR%/ziti-tunnel/version.go main

@echo configuring git
ziti-ci configure-git
pwd
cd service

SET REPO_URL=https://github.com/openziti/ziti-tunneler-sdk-c.git
SET REPO_BRANCH=update-submodule-to-https-vs-git
SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c
set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%\install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%\install\lib
mkdir %TUNNELER_SDK_DIR%

if not exist %SVC_ROOT_DIR%ziti.dll (
    @echo cloning %REPO_URL%
    git clone %REPO_URL% %TUNNELER_SDK_DIR%
    IF %ERRORLEVEL% NEQ 0 @echo Could not clone git repo:%REPO_URL%

    cd %TUNNELER_SDK_DIR%
    git checkout %REPO_BRANCH%
    IF %ERRORLEVEL% NEQ 0 @echo Could not checkout branch :%REPO_BRANCH%

    @echo Updating submodules...
    git submodule update --init --recursive
    IF %ERRORLEVEL% NEQ 0 @echo Could not update submodules

    if not exist %TUNNELER_SDK_DIR%\build mkdir %TUNNELER_SDK_DIR%\build
    if not exist %TUNNELER_SDK_DIR%\install mkdir %TUNNELER_SDK_DIR%\install

    cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%\build -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%\install
    cmake --build %TUNNELER_SDK_DIR%\build --target install
    if %ERRORLEVEL% GEQ 1 EXIT /B %ERRORLEVEL%

    cp %TUNNELER_SDK_DIR%\install\lib\ziti.dll %SVC_ROOT_DIR%
    cp %TUNNELER_SDK_DIR%\install\lib\libuv.dll %SVC_ROOT_DIR%

    @echo COPIED dlls to %SVC_ROOT_DIR%
    cd %SVC_ROOT_DIR%
) else (
    @echo ------------------------------------------------------------------------------
    @echo SKIPPED BUILDING ziti.dll because ziti.dll was found at %SVC_ROOT_DIR%ziti.dll
    @echo ------------------------------------------------------------------------------
)

go build -a ./ziti-tunnel
if %ERRORLEVEL% GEQ 1 EXIT /B %ERRORLEVEL%

@echo creating the distribution zip file
zip ziti-tunnel-win.zip *.dll ziti*exe

dir
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot proceed
exit /b 1

:END
