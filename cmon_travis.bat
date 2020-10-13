echo "about to try to echo a key onto known hosts..."
echo "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8" >> %USERPROFILE%/.ssh/known_hosts
type %USERPROFILE%/.ssh/known_hosts
echo "getting ziti-ci"
call get-ziti-ci.bat
echo "ziti-ci has been retrieved. running\:ziti-ci version"
ziti-ci version
ziti-ci configure-git
C:\Program Files\OpenSSH-Win64\ssh.exe -vT -i github_deploy_key github.com
ssh -vT -i github_deploy_key github.com