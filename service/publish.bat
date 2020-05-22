SET ZITT_TUNNEL_WIN_VER=0.0.4
IF "%TRAVIS_BRANCH%"=="master" GOTO RELEASE
@echo SNAP
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%ZITT_TUNNEL_WIN_VER%-SNAPSHOT --target=service/ziti-tunnel-win.zip
GOTO END

:RELEASE
@echo REL
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%ZITT_TUNNEL_WIN_VER% --target=service/ziti-tunnel-win.zip
GOTO END

:END
@echo publishing complete