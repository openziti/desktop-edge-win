@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version
set GO111MODULE=on
cd /d %ZITI_TUNNEL_WIN_ROOT%

echo fetching ziti-ci
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
echo ziti-ci has been retrieved. running: ziti-ci version
ziti-ci version

@echo generating version info - this will get pushed from publish.bat in CI _if_ publish.bat started build.bat
ziti-ci generate-build-info --noAddNoCommit --useVersion=false %SVC_ROOT_DIR%/ziti-tunnel/version.go main --verbose 2>&1
@echo version info generated
@echo ---------------------------
type version
@echo ---------------------------

@echo ========================================================
@echo trying git add and commit
@echo ========================================================
git diff
git add service/ziti-tunnel/version.go
@echo ---------------------------
type service/ziti-tunnel/version.go
@echo ---------------------------
@echo ---------------------------
type %SVC_ROOT_DIR%/ziti-tunnel/version.go
@echo ---------------------------
git diff
git commit -m "updating version"
git diff

@echo ========================================================
@echo trying git push
@echo ========================================================
git push
git diff


@echo ========================================================
@echo all done
@echo ========================================================