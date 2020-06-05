@echo off
SET CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
cd %ZITI_TUNNEL_WIN_ROOT%

SET REPO_URL=https://github.com/openziti/ziti-tunneler-sdk-c.git
SET REPO_BRANCH=update-submodule-to-https-vs-git
SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c
set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%\install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%\install\lib
mkdir %TUNNELER_SDK_DIR%

if not exist %SVC_ROOT_DIR%ziti.dll (
    if not exist %TUNNELER_SDK_DIR%\build mkdir %TUNNELER_SDK_DIR%\build
    if not exist %TUNNELER_SDK_DIR%\install mkdir %TUNNELER_SDK_DIR%\install

    cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%\build -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%\install
    cmake --build %TUNNELER_SDK_DIR%\build --target install

    echo ERROR LEVEL: %ERRORLEVEL%
    echo ERROR LEVEL: %ERRORLEVEL%
    echo ERROR LEVEL: %ERRORLEVEL%
    echo ERROR LEVEL: %ERRORLEVEL%
    echo ERROR LEVEL: %ERRORLEVEL%

    cp %TUNNELER_SDK_DIR%\install\lib\ziti.dll %SVC_ROOT_DIR%
    cp %TUNNELER_SDK_DIR%\install\lib\libuv.dll %SVC_ROOT_DIR%

    @echo COPIED dlls to %SVC_ROOT_DIR%
    cd %SVC_ROOT_DIR%
) else (
    @echo ------------------------------------------------------------------------------
    @echo SKIPPED BUILDING ziti.dll because ziti.dll was found at %SVC_ROOT_DIR%ziti.dll
    @echo ------------------------------------------------------------------------------
)

cd /d %SVC_ROOT_DIR%
go build -a ./ziti-tunnel
if %ERRORLEVEL% GEQ 1 (
    cd /d %CURDIR%
    EXIT /B %ERRORLEVEL%
)

:END
cd /d %CURDIR%