@echo off
REM Copyright NetFoundry, Inc.
REM
REM Licensed under the Apache License, Version 2.0 (the "License");
REM you may not use this file except in compliance with the License.
REM You may obtain a copy of the License at
REM
REM https://www.apache.org/licenses/LICENSE-2.0
REM
REM Unless required by applicable law or agreed to in writing, software
REM distributed under the License is distributed on an "AS IS" BASIS,
REM WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
REM See the License for the specific language governing permissions and
REM limitations under the License.
REM
SET REPO_URL=https://github.com/openziti/ziti-tunnel-sdk-c.git
SET ZITI_TUNNEL_REPO_BRANCH=v0.22.11
REM override the c sdk used in the build - leave blank for the same as specified in the tunneler sdk
SET ZITI_SDK_C_BRANCH=
SET ZITI_TUNNEL_REPO_URL=https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/%ZITI_TUNNEL_REPO_BRANCH%/ziti-edge-tunnel-Windows_x86_64.zip

REM the number of TCP connections the tunneler sdk can have at any one time
SET TCP_MAX_CONNECTIONS=256
SET WINTUN_DL_URL=https://www.wintun.net/builds/wintun-0.13.zip

set SVC_ROOT_DIR=%~dp0
set CURDIR=%CD%
set /P VERSION=<%SVC_ROOT_DIR%\..\version

call %SVC_ROOT_DIR%set-env.bat

IF "%ZITI_DEBUG%"=="" (
    REM clear out if debug was run in the past
    SET ZITI_DEBUG_CMAKE=
) else (
    SET ZITI_DEBUG_CMAKE=-DCMAKE_BUILD_TYPE=Debug
    echo ZITI_DEBUG detected. will run cmake with: %ZITI_DEBUG_CMAKE%
)

cd /d %ZITI_TUNNEL_WIN_ROOT%

IF "%1"=="" (
    GOTO NO_ACTION_SUPPLIED
)

IF "%BUILD_VERSION%"=="" (
    GOTO BUILD_VERSION_ERROR
) else (
    echo Version file found. detected version: %BUILD_VERSION%
)

IF "%1"=="clean" (
    echo doing 'clean' build
    GOTO CLEAN
)
IF "%1"=="quick" (
    echo doing 'quick' build - no tunneler sdk/c sdk build
    GOTO QUICK
)
IF "%1"=="CI" (
    echo doing 'CI' build
    GOTO CI
)

GOTO UNKNOWN_OPTION

echo You should not be here. This is a bug. Please report
cd /d %CURDIR%
exit /b 1

:CI
echo fetching ziti-ci 2>&1
call %SVC_ROOT_DIR%/../get-ziti-ci.bat 2>&1
echo ziti-ci has been retrieved. running: ziti-ci version 2>&1
ziti-ci version 2>&1
echo ""

echo generating version info - this will get pushed from publish.bat in CI _if_ publish.bat started build.bat 2>&1
ziti-ci generate-build-info --noAddNoCommit --useVersion=false %SVC_ROOT_DIR%/version.go main --verbose 2>&1

echo calling powershell script to update versions in UI and Installer 2>&1
powershell -file %ZITI_TUNNEL_WIN_ROOT%update-versions.ps1 2>&1
echo version info generated 2>&1
goto QUICK

:CLEAN
echo REMOVING old build folder if it exists at %TUNNELER_SDK_DIR%build
rmdir /s /q %TUNNELER_SDK_DIR%build
echo REMOVING wintun.dll at %SVC_ROOT_DIR%wintun.dll
del /q %SVC_ROOT_DIR%wintun.dll

:QUICK

if not exist %SVC_ROOT_DIR%wintun.dll (
    echo ------------------------------------------------------------------------------
    echo DOWNLOADING wintun.dll using powershell
    echo       from: %WINTUN_DL_URL%
    echo ------------------------------------------------------------------------------
    powershell "Invoke-WebRequest %WINTUN_DL_URL% -OutFile %SVC_ROOT_DIR%wintun.zip"
    powershell "Expand-Archive -Path %SVC_ROOT_DIR%wintun.zip -Force -DestinationPath %SVC_ROOT_DIR%wintun-extracted"
    echo    copying: %SVC_ROOT_DIR%wintun-extracted\wintun\bin\amd64\wintun.dll %SVC_ROOT_DIR%wintun.dll
    copy %SVC_ROOT_DIR%wintun-extracted\wintun\bin\amd64\wintun.dll %SVC_ROOT_DIR%wintun.dll
    echo   removing: %SVC_ROOT_DIR%wintun-extracted
    del /s /q %SVC_ROOT_DIR%wintun-extracted\
    rmdir /s /q %SVC_ROOT_DIR%wintun-extracted\
    echo   removing: %SVC_ROOT_DIR%wintun.zip
    del /s %SVC_ROOT_DIR%wintun.zip
)

echo changing to service folder: %SVC_ROOT_DIR%
cd %SVC_ROOT_DIR%

if DEFINED ZITI_TUNNEL_REPO_URL (
    echo ------------------------------------------------------------------------------
    echo DOWNLOADING ziti-edge-tunnel-Windows_x86_64.zip using powershell
    echo       from: %ZITI_TUNNEL_REPO_URL%
    echo ------------------------------------------------------------------------------
	echo REMOVING ziti-edge-tunnel-Windows_x86_64.zip at %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64.zip
	del /q %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64.zip
    powershell "Invoke-WebRequest %ZITI_TUNNEL_REPO_URL% -OutFile %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64.zip"
    powershell "Expand-Archive -Path %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64.zip -Force -DestinationPath %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64"
	echo copying: %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64\ziti-edge-tunnel.exe %SVC_ROOT_DIR%ziti-edge-tunnel.exe
    copy %SVC_ROOT_DIR%ziti-edge-tunnel-Windows_x86_64\ziti-edge-tunnel.exe %SVC_ROOT_DIR%ziti-edge-tunnel.exe
	GOTO COMPRESS_FILES
)

echo ------------------------------------------------------------------------------
echo BUILDING ziti-edge-tunnel begins
echo ------------------------------------------------------------------------------

if exist %TUNNELER_SDK_DIR% (
    echo %TUNNELER_SDK_DIR% exists
    cd %TUNNELER_SDK_DIR%
    echo ------------------------------------------------------------------------------
    echo issuing git pull to pick up any changes
    echo ------------------------------------------------------------------------------
    git pull
    git submodule update --init --recursive
    SET ACTUAL_ERR=%ERRORLEVEL%
    echo ------------------------------------------------------------------------------
) else (
    echo ------------------------------------------------------------------------------
    echo %TUNNELER_SDK_DIR% not found
    echo     - cloning %REPO_URL%
    echo ------------------------------------------------------------------------------
    echo issuing mkdir %TUNNELER_SDK_DIR%
    mkdir %TUNNELER_SDK_DIR%

    echo changing to %TUNNELER_SDK_DIR%
    cd %TUNNELER_SDK_DIR%

    echo current directory is %CD% - should be %TUNNELER_SDK_DIR%
    echo.
    echo git clone %REPO_URL% %TUNNELER_SDK_DIR% --recurse-submodules
    echo.

    git clone %REPO_URL% %TUNNELER_SDK_DIR% --recurse-submodules
    SET ACTUAL_ERR=%ERRORLEVEL%
)
IF %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Could not pull or clone git repo:%REPO_URL%
    echo.
    goto FAIL
)

echo in %cd% - checking out %ZITI_TUNNEL_REPO_BRANCH% branch: %ZITI_TUNNEL_REPO_BRANCH%
git checkout %ZITI_TUNNEL_REPO_BRANCH%
IF %ERRORLEVEL% NEQ 0 (
    SET ACTUAL_ERR=%ERRORLEVEL%
    echo.
    echo Could not checkout branch: %ZITI_TUNNEL_REPO_BRANCH%
    echo.
    goto FAIL
)

echo clone or checkout complete.

echo Updating submodules... if needed...
REM git config --get remote.origin.url
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

echo Generating project files for tunneler sdk
if "%ZITI_SDK_C_BRANCH%"=="" (
    echo ZITI_SDK_C_BRANCH is not set - ZITI_SDK_C_BRANCH_CMD will be empty
    SET ZITI_SDK_C_BRANCH_CMD=%ZITI_SPACES:~2,1%
) else (
    echo SETTING ZITI_SDK_C_BRANCH_CMD to: -DZITI_SDK_C_BRANCH^=%ZITI_SDK_C_BRANCH%
    SET ZITI_SDK_C_BRANCH_CMD=-DZITI_SDK_C_BRANCH=%ZITI_SDK_C_BRANCH%
)

cmake -G Ninja -S %TUNNELER_SDK_DIR% -B %TUNNELER_SDK_DIR%build -DGIT_VERSION=%VERSION% -DCMAKE_INSTALL_PREFIX=%TUNNELER_SDK_DIR%install -DCMAKE_TOOLCHAIN_FILE=%TUNNELER_SDK_DIR%\toolchains\default.cmake %ZITI_SDK_C_BRANCH_CMD% %ZITI_DEBUG_CMAKE%

SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Compilation of %TUNNELER_SDK_DIR% failed
    echo.
    goto FAIL
) else (
    echo.
    echo result of ninja build: %ACTUAL_ERR%
)

cmake --build %TUNNELER_SDK_DIR%build --target bundle --verbose 

SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Bundle command of %TUNNELER_SDK_DIR%build failed
    echo.
    goto FAIL
) else (
    echo.
    echo result of cmake bundle: %ACTUAL_ERR%
)

echo checking the CSDK
pushd %TUNNELER_SDK_DIR%build\_deps\ziti-sdk-c-src
git rev-parse --short HEAD > hash.txt
git rev-parse --abbrev-ref HEAD > branch.txt
set /p CSDK_BRANCH=<branch.txt
set /p CSDK_HASH=<hash.txt
del /q branch.txt
del /q hash.txt

cd %SVC_ROOT_DIR%

echo    copying ziti-edge-tunnel.exe to service root (for zip creation)
echo copy /y %TUNNELER_SDK_DIR%build\programs\ziti-edge-tunnel\ziti-edge-tunnel.exe %SVC_ROOT_DIR%
copy /y %TUNNELER_SDK_DIR%build\programs\ziti-edge-tunnel\ziti-edge-tunnel.exe %SVC_ROOT_DIR%

SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo Could not copy %TUNNELER_SDK_DIR%build\programs\ziti-edge-tunnel\ziti-edge-tunnel.exe
    echo.
    goto FAIL
) else (
    echo.
    echo result of copy build\programs\ziti-edge-tunnel\ziti-edge-tunnel.exe: %ACTUAL_ERR%
)

:COMPRESS_FILES

echo building the windows ziti-edge-tunnel distribution zip file
powershell "Compress-Archive -Force -path *.dll, *.exe -DestinationPath .\ziti-edge-tunnel-win.zip"

GOTO END

:BUILD_VERSION_ERROR
echo The build version environment variable was not set - cannot proceed
cd %CURDIR%
exit /b 1

:UNKNOWN_OPTION
echo.
echo ERROR: The action supplied is not known: %1

:NO_ACTION_SUPPLIED
echo.
echo        Supply a build action: CI^|clean^|quick
echo.
cd /d %CURDIR%
exit /b 1

:FAIL
echo.
echo ACTUAL_ERR: %ACTUAL_ERR%
cd /d %CURDIR%
EXIT /B %ACTUAL_ERR%

:END
cd /d %CURDIR%

echo.
echo.
echo =====================================================
echo -- BUILD COMPLETE  : %date% %time%
echo --     tunneler sdk: %ZITI_TUNNEL_REPO_BRANCH%
echo --     c sdk       : %CSDK_BRANCH%
echo --                 : %CSDK_HASH%
echo --                 : %ZITI_SDK_C_BRANCH%
echo --                 : %ZITI_SDK_C_BRANCH_CMD%
echo =====================================================
