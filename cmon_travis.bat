@echo off
@echo "about to try to echo a key onto known hosts..." 2>&1
@echo "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8" >> %USERPROFILE%\.ssh\known_hosts
type %USERPROFILE%\.ssh\known_hosts 2>&1
@echo "getting ziti-ci" 2>&1
call get-ziti-ci.bat 2>&1
@echo "ziti-ci has been retrieved. running\:ziti-ci version" 2>&1
ziti-ci version 2>&1
ziti-ci configure-git 2>&1
"C:\Program Files\OpenSSH-Win64\ssh.exe" -vT -i github_deploy_key github.com 2>&1
ssh -vT -i github_deploy_key github.com 2>&1
