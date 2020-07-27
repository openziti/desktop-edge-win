@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0

set /p BUILD_VERSION=<%SVC_ROOT_DIR%..\version
IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

call %SVC_ROOT_DIR%\build.bat
SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    echo.
    echo call to build.bat failed with %ACTUAL_ERR%
    echo.
    exit /b 1
) else (
    echo.
    echo result of ninja build: %ACTUAL_ERR%
)

IF "%GIT_BRANCH%"=="master" GOTO RELEASE
@echo Publishing to snapshot repo
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION%-SNAPSHOT --target=service/ziti-tunnel-win.zip
REM ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION%-SNAPSHOT --target=service/ziti-tunnel-win.zip --classifier=%GIT_BRANCH%
GOTO END

:RELEASE
@echo Publishing release
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION% --target=service/ziti-tunnel-win.zip
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot publish
exit /b 1

:FAIL
IF %~1 NEQ 0 (
    echo ================================================================
    echo.
    echo FAILURE:
    echo     %~2
    echo.
    echo ================================================================
    exit /b %~1
)
exit /b 0

:END
@echo publishing complete - committing version.go as ci

@echo configuring git - relies on build.bat successfully grabbing ziti-ci
ziti-ci configure-git 2>1

git add service/ziti-tunnel/version.go 2>1
CALL :FAIL %ERRORLEVEL% "git add failed"
@echo git add service/ziti-tunnel/version.go complete: %ERRORLEVEL%

git commit -m "[ci skip] committing updated version information" 2>1
CALL :FAIL %ERRORLEVEL% "git commit failed"
@echo git commit -m "[ci skip] committing updated version information" complete: %ERRORLEVEL%

git push 2>1
CALL :FAIL %ERRORLEVEL% "git push failed"
@echo git push complete: %ERRORLEVEL%

cd %CURDIR%
@echo publish script has completed
