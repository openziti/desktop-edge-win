@echo off
@echo "about to try to echo a key onto known hosts..." 2>&1
@echo "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8" >> github_known_hosts 2>&1

@echo type'ing github_known_hosts 2>&1
type github_known_hosts 2>&1

@echo using ssh -o "StrictHostKeyChecking no" -o "UserKnownHostsFile github_known_hosts" github.com to generate github_known_hosts2
ssh -o "StrictHostKeyChecking no" -o "UserKnownHostsFile github_known_hosts2" github.com

@echo type'ing github_known_hosts 2>&1
type github_known_hosts 2>&1

@echo ssh cmd1-a
"C:\Program Files\OpenSSH-Win64\ssh.exe" -vT -o "UserKnownHostsFile github_known_hosts" -i github_deploy_key github.com 2>&1
@echo ssh cmd1-b
ssh -vT -o "UserKnownHostsFile github_known_hosts" -i github_deploy_key github.com 2>&1

@echo mkdir and adding to actual known hosts... 2>&1
@echo mkdir %USERPROFILE%\.ssh 2>&1
mkdir %USERPROFILE%\.ssh 2>&1

@echo echoing: echo "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8" onto %USERPROFILE%\.ssh\known_hosts 2>&1
echo "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8" >> %USERPROFILE%\.ssh\known_hosts 2>&1

@echo typing %USERPROFILE%\.ssh\known_hosts
type %USERPROFILE%\.ssh\known_hosts

@echo ssh cmd2-a
"C:\Program Files\OpenSSH-Win64\ssh.exe" -vT -i github_deploy_key github.com 2>&1
@echo ssh cmd2-b
ssh -vT -i github_deploy_key github.com 2>&1


@echo "getting ziti-ci" 2>&1
call get-ziti-ci.bat 2>&1
@echo "ziti-ci has been retrieved. running\:ziti-ci version" 2>&1
ziti-ci version 2>&1
ziti-ci configure-git 2>&1