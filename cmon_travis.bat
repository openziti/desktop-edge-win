@echo looking for key using: ssh-keygen -F github.com __1__ 2>&1
ssh-keygen -F github.com 2>&1

@echo issuing ssh-keygen -R 2>&1
ssh-keygen -R github.com 2>&1

@echo mkdir and adding to actual known hosts... 2>&1
@echo mkdir %USERPROFILE%\.ssh 2>&1
mkdir %USERPROFILE%\.ssh 2>&1

@echo adding github key: ssh-keyscan -t rsa github.com 2>&1
ssh-keyscan -t rsa github.com >> %USERPROFILE%\.ssh\known_hosts 2>&1

@echo typing %USERPROFILE%\.ssh\known_hosts 2>&1
type %USERPROFILE%\.ssh\known_hosts 2>&1

@echo looking for key using: ssh-keygen -F github.com - expect to find it this time! 2>&1
ssh-keygen -F github.com 2>&1

@echo test ssh - expect it to work 2>&1
ssh -Tv -i github_deploy_key git@github.com 2>&1

REM @echo test ssh - manual known hosts 2>&1
REM ssh -vT -o "UserKnownHostsFile manual-known-hosts.txt" -i github_deploy_key github.com 2>&1
