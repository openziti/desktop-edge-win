@echo off
set SVC_ROOT_DIR=%~dp0

set /p BUILD_VERSION=<%SVC_ROOT_DIR%..\version
IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

call %SVC_ROOT_DIR%\build.bat

IF "%TRAVIS_BRANCH%"=="master" GOTO RELEASE
@echo Publishing to snapshot repo
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION%-SNAPSHOT --target=service/ziti-tunnel-win.zip --classifier=%TRAVIS_BRANCH%
GOTO END

:RELEASE
@echo Publishing release
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION% --target=service/ziti-tunnel-win.zip
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot publish
exit /b 1

:END
@echo publishing complete
