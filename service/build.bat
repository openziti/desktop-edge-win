@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version
set GO111MODULE=on
cd /d %ZITI_TUNNEL_WIN_ROOT%

IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR
IF "%1"=="clean" GOTO CLEAN
IF "%1"=="quick" GOTO QUICK

echo fetching ziti-ci
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
ziti-ci version

echo generating version info - this will pushed from publish.bat in CI
ziti-ci generate-build-info --useVersion=false %SVC_ROOT_DIR%/ziti-tunnel/version.go main
echo version info generated
goto QUICK

:CLEAN
rmdir /s /q deps
del /q %SVC_ROOT_DIR%ziti.dll

:QUICK
cd %SVC_ROOT_DIR%

SET REPO_URL=https://github.com/openziti/ziti-tunneler-sdk-c.git
SET ZITI_TUNNEL_REPO_BRANCH=1a55fc75a918bd27f410ea01c7dfd64099e15cc2
SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c\
set CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%install\include
set CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%install\lib

if exist %SVC_ROOT_DIR%ziti.dll (
    echo ------------------------------------------------------------------------------
    echo SKIPPED BUILDING ziti.dll because ziti.dll was found at %SVC_ROOT_DIR%ziti.dll
    echo ------------------------------------------------------------------------------
    GOTO GOBUILD
)

echo ------------------------------------------------------------------------------
echo BUILDING ziti.dll begins
echo ------------------------------------------------------------------------------

set BEFORE_GIT=%cd%

if exist %TUNNELER_SDK_DIR% (
    echo ------------------------------------------------------------------------------
    echo issuing git pull to pick up any changes
    echo ------------------------------------------------------------------------------
    git pull
    git submodule update --init --recursive
    SET ACTUAL_ERR=%ERRORLEVEL%
    echo ------------------------------------------------------------------------------
) else (
    echo ------------------------------------------------------------------------------
    echo cloning %REPO_URL%
    echo ------------------------------------------------------------------------------
    echo issuing mkdir %TUNNELER_SDK_DIR%
    mkdir %TUNNELER_SDK_DIR%

    echo changing to %TUNNELER_SDK_DIR%
    cd %TUNNELER_SDK_DIR%
    
    echo current directory is %CD% - should be %TUNNELER_SDK_DIR%
    git clone %REPO_URL% %TUNNELER_SDK_DIR% --recurse-submodules
    SET ACTUAL_ERR=%ERRORLEVEL%
)
IF %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Could not pull or clone git repo:%REPO_URL%
    echo.
    goto FAIL
)

echo checking out branch: %ZITI_TUNNEL_REPO_BRANCH%
git checkout %ZITI_TUNNEL_REPO_BRANCH%
IF %ERRORLEVEL% NEQ 0 (
    SET ACTUAL_ERR=%ERRORLEVEL%
    echo.
    echo Could not checkout branch: %ZITI_TUNNEL_REPO_BRANCH%
    echo.
    goto FAIL
)

echo ------------------------------------------------------------------------------
type %TUNNELER_SDK_DIR%.gitmodules
echo.
echo updating any ssh submodules to https using:
echo     powershell "(get-content -path %TUNNELER_SDK_DIR%.gitmodules) -replace 'git@(.*)\:(.*)\.git', 'https://$1/$2.git' | Set-Content -Path %TUNNELER_SDK_DIR%.gitmodules"
echo.
powershell "(get-content -path %TUNNELER_SDK_DIR%.gitmodules) -replace 'git@(.*)\:(.*)\.git', 'https://$1/$2.git' | Set-Content -Path %TUNNELER_SDK_DIR%.gitmodules"
type %TUNNELER_SDK_DIR%.gitmodules
echo ------------------------------------------------------------------------------

echo Updating submodules...
git config --get remote.origin.url
git submodule update --init --recursive
IF %ERRORLEVEL% NEQ 0 (
    SET ACTUAL_ERR=%ERRORLEVEL%
    echo.
    echo Could not update submodules
    echo.
    goto FAIL
)

if not exist %TUNNELER_SDK_DIR%build mkdir %TUNNELER_SDK_DIR%build
if not exist %TUNNELER_SDK_DIR%install mkdir %TUNNELER_SDK_DIR%install

cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%build -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%install
cmake --build %TUNNELER_SDK_DIR%build --target install
SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Build of %TUNNELER_SDK_DIR%build failed
    echo.
    goto FAIL
) else (
    echo.
    echo result of ninja build: %ACTUAL_ERR%
)

cp %TUNNELER_SDK_DIR%install\lib\ziti.dll %SVC_ROOT_DIR%
cp %TUNNELER_SDK_DIR%install\lib\libuv.dll %SVC_ROOT_DIR%

echo COPIED dlls to %SVC_ROOT_DIR%
cd %SVC_ROOT_DIR%

:GOBUILD
echo building the go program
go build -a ./ziti-tunnel
SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Building ziti-tunnel failed
    echo.
    goto FAIL
) else (
    echo go build complete
)

IF "%1"=="quick" GOTO END

echo creating the distribution zip file
zip ziti-tunnel-win.zip *.dll ziti*exe

dir
GOTO END

:BUILD_VERSION_ERROR
echo The build version environment variable was not set - cannot proceed
cd %CURDIR%
exit /b 1

:FAIL
echo.
echo ACTUAL_ERR: %ACTUAL_ERR%
cd %CURDIR%
EXIT /B %ACTUAL_ERR%

:END
cd /d %CURDIR%
