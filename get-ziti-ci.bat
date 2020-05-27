@echo off

IF "x%ZITI_CI_VERSION%"=="x" GOTO DEFAULT

echo Fetching ziti-ci@%ZITI_CI_VERSION%
go get github.com/netfoundry/ziti-ci@%ZITI_CI_VERSION%
GOTO END

:DEFAULT
echo Fetching default ziti-ci
go get github.com/netfoundry/ziti-ci
GOTO END

:END
echo go get of ziti-ci complete
