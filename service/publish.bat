IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

IF "%TRAVIS_BRANCH%"=="master" GOTO RELEASE
@echo Publishing to snapshot repo
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%ZITT_TUNNEL_WIN_VER%-SNAPSHOT --target=service/ziti-tunnel-win.zip
GOTO END

:RELEASE
@echo Publishing release
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%ZITT_TUNNEL_WIN_VER% --target=service/ziti-tunnel-win.zip
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot publish

:END
@echo publishing complete