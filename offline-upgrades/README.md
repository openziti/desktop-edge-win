# Automatic Upgrades in Offline Mode

When attempting an automatic update, the process will still attempt to contact three specific URLs.
These URLs are all related to the certificate revocation lists (CRLs) of the certificates that have 
signed the installer the project produces.

The offline machine *must* be able to make these requests or the validation of the installer will fail.
If you are controlling the offline network, you will need to add DNS entries for these urls and host the
associated files. If you cannot control the DNS or there is no DNS, hosts file entries will need to be
added. The following URLs will be connected to while verifying the installer:

* http://crl.globalsign.com/gsgccr45evcodesignca2020.crl
* http://crl3.digicert.com/DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crl
* https://openziti.github.io/crl/openziti.crl

This folder illustrates how you can use the automatic upgrade behavior while being entirely offline.

Generate a json document and host it with an http server. An easy way to do this is to take an example
from [../release-streams](), such as [..\release-streams\latest.json](). Duplicate the source document,
creating your own release stream. Edit the file and update the versions and URLs referenced according. 
For example, this file could be called "offline.json" and hosted by some server on the "offline" network.

## Example Configuring a Machine Entirely Offline

In this example, [Windows Sandbox](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview)
will be used and the network adapter will be removed after installing Python. Python will be used to run
http servers. All files will be generated locally, on the *host* machine and transferred to the sandbox
in one big copy/paste.

### On the Host Machine

Locally, perform the following tasks. These steps also assume you have cloned this repository, or have
copied the contents locally. All commands will be executed relative to this README.

1. open a powershell prompt and change to this directory
1. make a new folder for the automatic upgrade assets

        mkdir offline
        mkdir offline\crl

1. Download the following certificates and CRLs using `Invoke-WebRequest`
   
        Invoke-WebRequest -Uri "http://secure.globalsign.com/cacert/gsgccr45evcodesignca2020.crt" -OutFile  .\offline\gsgccr45evcodesignca2020.crt
        Invoke-WebRequest -Uri "http://crl.globalsign.com/gsgccr45evcodesignca2020.crl" -OutFile  .\offline\gsgccr45evcodesignca2020.crl
        Invoke-WebRequest -Uri "http://crl.globalsign.com/codesigningrootr45.crl" -OutFile  .\offline\codesigningrootr45.crl
        Invoke-WebRequest -Uri "http://crl3.digicert.com/DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crl" -OutFile  .\offline\DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crl
        Invoke-WebRequest -Uri "http://crl3.digicert.com/DigiCertTrustedRootG4.crl" -OutFile  .\offline\DigiCertTrustedRootG4.crl
        Invoke-WebRequest -Uri "https://openziti.github.io/crl/openziti.crl" -OutFile  .\offline\crl\openziti.crl
        Invoke-WebRequest -Uri "https://cacerts.digicert.com/DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crt" -OutFile  .\offline\DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crt
      
1. copy `https_server.py` and `san.cnf` to the offline folder. The python file is used later when satisfying
   the OpenZiti CRL request and san.cnf is used with openssl below to generate a server cert.

        copy-item -Path .\https_server.py -Destination .\offline\
        copy-item -Path .\san.cnf -Destination .\offline\

1. copy `offline.json` to `c:\offline\` 

        copy-item -Path .\offline.json -Destination .\offline\

1. update `offline.json` accordingly for whatever the latest version will be.
1. Generate a full PKI for `openziti.github.io` to satisfy the CSL request the OpenZiti PKI requires. If openssl is not
   available in the sandbox, run these commands locally and then transfer the files from the host machine to `c:\offline`
   within the sandbox:

        openssl genrsa -out .\offline\openzitiCA.key 2048
        openssl req -x509 -new -nodes -key .\offline\openzitiCA.key -sha256 -days 365 -out .\offline\openzitiCA.pem -subj "/C=US/ST=California/L=San Francisco/O=OpenZiti/OU=IT/CN=openzitiCA"
      
        openssl genrsa -out .\offline\openziti_server.key 2048
        openssl req -new -key .\offline\openziti_server.key -out .\offline\openziti_server.csr -config san.cnf
        openssl x509 -req -in .\offline\openziti_server.csr -CA .\offline\openzitiCA.pem -CAkey .\offline\openzitiCA.key -CAcreateserial -out .\offline\openziti_server.crt -days 365 -sha256 -extfile .\offline\san.cnf -extensions req_ext

1. Transfer the contents of `offline` to the sandbox at `c:\offline`
1. Transfer the "current" (or n-1) installer to the `c:\offline\` folder. Example: 2.5.1.0
1. Transfer the new installer version and corresponding sha256 to the `c:\offline\` folder. Example: 2.5.1.1

### On the Windows Sandbox (or VM)

These commands assume you're using Windows Sandbox. If you're using a VM, there may be differences

1. install the Ziti Desktop Edge for Windows - current version. Example: 2.5.1.0
1. install Python - wait for the install to complete before continuing
1. After installing Python and transferring the `offline` folder, open Device Manager and uninstall the 
   network adapter, likely named "Microsoft Hyper-V Network Adapter", eliminating network access.

1. open a powershell prompt
1. Configure the Windows Sandbox to trust the copied certificates and CRLs

        certutil -addstore CA "c:\offline\gsgccr45evcodesignca2020.crt"
        certutil -addstore CA "c:\offline\gsgccr45evcodesignca2020.crl"
        certutil -addstore CA "c:\offline\codesigningrootr45.crl"
        certutil -addstore CA "c:\offline\DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crt"
        certutil -addstore CA "c:\offline\DigiCertTrustedG4RSA4096SHA256TimeStampingCA.crl"
        certutil -addstore CA "c:\offline\DigiCertTrustedRootG4.crl"
        certutil -addstore CA "c:\offline\crl\openziti.crl"
        certutil -addstore root "C:\offline\openzitiCA.pem"

1. restart Edge if open, restart the ziti-monitor to pick up certificate-related changes:

        net stop ziti-monitor
        net start ziti-monitor

1. start a simple Python server delivering the content from `c:\offline\` by running (assuming `py` is on your path)

        py -m http.server 80 -d C:\offline\

1. start another powershell and Python server for delivering the OpenZiti CRL on port 443

        py C:\offline\https_server.py

1. For "reasons", it seems that the hosts file from within the sandbox doesn't work in my tests. By the time
   you read this, maybe that's changed. So if you find your hosts file entries "not working", make the hosts
   file updates in the *HOST*, hosts file. That seems to work.
1. add a hosts file entry for the necessary hosts:

        127.0.0.1 offline.install.example crl.globalsign.com crl3.digicert.com openziti.github.io

1. verify you can access: http://offline.install.example/offline.json from within the Sandbox
1. verify the browser_download_url looks right and verify you can access that url from within the Sandbox
1. verify you can access: https://openziti.github.io/crl/ from within the Sandbox. If you get a certificate error,
   close Edge and open it back up again and try again. You should also see one listing: `openziti.crl`
1. Do not proceed until the two Edge checks above work properly.
1. change the Ziti Desktop Edge for Windows to use http://offline.install.example/offline.json for the automatic
   upgrade URL.
1. click "Check for Updates Now"
1. If all has gone well you'll see "Update ${next version} is available"
1. Click Perform Update - the software should automatically update
