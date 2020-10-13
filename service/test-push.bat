@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0
set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\
set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version
set GO111MODULE=on
cd /d %ZITI_TUNNEL_WIN_ROOT%

@echo converting shallow clone so travis can co: %GIT_BRANCH%
git remote set-branches origin %GIT_BRANCH% 2>&1
git fetch --depth 1 origin %GIT_BRANCH% 2>&1
git checkout %GIT_BRANCH% 2>&1

echo fetching ziti-ci 2>&1
call %SVC_ROOT_DIR%/../get-ziti-ci.bat
echo ziti-ci has been retrieved. running: ziti-ci version 2>&1
ziti-ci version 2>&1
ziti-ci configure-git

@echo trying ssh
ssh -T -i github_deploy_key github.com

@echo generating version info - this will get pushed from publish.bat in CI _if_ publish.bat started build.bat 2>&1
ziti-ci generate-build-info --noAddNoCommit --useVersion=false %SVC_ROOT_DIR%/ziti-tunnel/version.go main --verbose 2>&1
@echo version info generated 2>&1
@echo --------------------------- 2>&1
type version 2>&1
@echo --------------------------- 2>&1

@echo ======================================================== 2>&1
@echo trying git add and commit 2>&1
@echo ======================================================== 2>&1

git add service/ziti-tunnel/version.go 2>&1
REM git commit -m "[skip ci] updating version" 2>&1

@echo ======================================================== 2>&1
@echo trying git push 2>&1
@echo ======================================================== 2>&1
REM git push

@echo ======================================================== 2>&1
@echo all done 2>&1
@echo ======================================================== 2>&1
sleep 5
