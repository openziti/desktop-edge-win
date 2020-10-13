@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version
set GO111MODULE=on
cd /d %ZITI_TUNNEL_WIN_ROOT%

echo fetching ziti-ci 2>&1
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
echo ziti-ci has been retrieved. running: ziti-ci version 2>&1
ziti-ci version 2>&1

@echo generating version info - this will get pushed from publish.bat in CI _if_ publish.bat started build.bat 2>&1
ziti-ci generate-build-info --noAddNoCommit --useVersion=false %SVC_ROOT_DIR%/ziti-tunnel/version.go main --verbose 2>&1
@echo version info generated 2>&1
@echo --------------------------- 2>&1
type version
@echo --------------------------- 2>&1

@echo ======================================================== 2>&1
@echo trying git add and commit 2>&1
@echo ======================================================== 2>&1
git diff 2>&1
git add service/ziti-tunnel/version.go 2>&1


@echo issuing git diff 2>&1
git diff 2>&1

@echo issuing git commit 2>&1
git commit -m "[ci skip] updating version" 2>&1

@echo issuing git diff 2>&1
git diff 2>&1

@echo issuing git status 2>&1
git status 2>&1

@echo issuing git push origin HEAD:%GIT_BRANCH% 2>&1


echo "THIS IS THE END"

sleep 5