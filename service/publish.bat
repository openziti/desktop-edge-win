@echo off
set CURDIR=%CD%
set SVC_ROOT_DIR=%~dp0

set /p BUILD_VERSION=<%SVC_ROOT_DIR%..\version
IF "%BUILD_VERSION%"=="" GOTO BUILD_VERSION_ERROR

call %SVC_ROOT_DIR%\build.bat
SET ACTUAL_ERR=%ERRORLEVEL%
if %ACTUAL_ERR% NEQ 0 (
    @echo.
    @echo call to build.bat failed with %ACTUAL_ERR%
    @echo.
    exit /b 1
) else (
    @echo.
    @echo result of ninja build: %ACTUAL_ERR%
)

IF "%GIT_BRANCH%"=="master" GOTO RELEASE
@echo Publishing to snapshot repo
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION%-SNAPSHOT --target=service/ziti-tunnel-win.zip 2>&1
REM ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION%-SNAPSHOT --target=service/ziti-tunnel-win.zip --classifier=%GIT_BRANCH% 2>&1
GOTO END

:RELEASE
@echo Publishing release
ziti-ci publish artifactory --groupId=ziti-tunnel-win.amd64.windows --artifactId=ziti-tunnel-win --version=%BUILD_VERSION% --target=service/ziti-tunnel-win.zip 2>&1
GOTO END

:BUILD_VERSION_ERROR
@echo The build version environment variable was not set - cannot publish
exit /b 1

:FAIL
IF %~1 NEQ 0 (
    @echo ================================================================
    @echo.
    @echo FAILURE:
    @echo     %~2
    @echo.
    @echo ================================================================
    exit /b %~1
)
exit /b 0

:END
@echo configuring git - relies on build.bat successfully grabbing ziti-ci and build.bat updating service/ziti-tunnel/version.go
ziti-ci configure-git 2>&1

@echo publishing complete - committing version.go as ci

git checkout %GIT_BRANCH% 2>&1
CALL :FAIL %ERRORLEVEL% "checkout failed"

@echo converting shallow clone so travis can co: %GIT_BRANCH%
git remote set-branches origin %GIT_BRANCH% 2>&1
git fetch --depth 1 origin %GIT_BRANCH% 2>&1
git checkout %GIT_BRANCH% 2>&1
@echo git checkout %GIT_BRANCH% complete: %ERRORLEVEL%

git add service/ziti-tunnel/version.go 2>&1
CALL :FAIL %ERRORLEVEL% "git add failed"
@echo git add service/ziti-tunnel/version.go complete: %ERRORLEVEL%

@echo issuing status, diff, commit
@echo ========================================================
git status 2>&1
git diff 2>&1
git commit -m "[ci skip] committing updated version information" 2>&1
CALL :FAIL %ERRORLEVEL% "git commit failed"
@echo git commit -m "[ci skip] committing updated version information" complete: %ERRORLEVEL%

@echo issuing git status and push now
@echo ========================================================
git status 2>&1

cd %CURDIR%
@echo back at: %CURDIR%

git push 2>&1
CALL :FAIL %ERRORLEVEL% "git push failed"
@echo git push complete: %ERRORLEVEL%
@echo publish script has completed
