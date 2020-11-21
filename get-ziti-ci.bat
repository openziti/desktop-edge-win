@echo off

IF "x%ZITI_CI_VERSION%"=="x" GOTO DEFAULT
SET GO111MODULE=on

echo Fetching ziti-ci@%ZITI_CI_VERSION%
go get github.com/netfoundry/ziti-ci@%ZITI_CI_VERSION% 2> NUL
GOTO END

:DEFAULT
echo Fetching default ziti-ci
go get github.com/netfoundry/ziti-ci 2> NUL
GOTO END

:END
echo go get of ziti-ci complete
