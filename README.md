# Ziti Desktop Edge for Windows

The Ziti Desktop Edge for Windows is an application that is necessary to integrate applications which cannot embed an 
OpenZiti SDK directly into the application. This is colloquially known as a "brown field" application because the app
itself has no understanding that it has been Ziti-enabled.

In order for an application that has no knowledge of being Ziti-enabled to work the connections established by the app
must be intercepted before leaving the computer and routed through the Ziti network. This is accomplished by three main
components:

* [wintun](https://www.wintun.net) - provides a Layer 3 TUN for Windows
* A Windows service which runs as the local system account which creates the TUN as well as manages the Ziti connections
* A Windows UWP UI application that allows the interactively logged on user to interact with the Windows service

## Building A Release

see [releasing](./releasing.md)

## Offline Installation

The Ziti Desktop Edge for Windows requires .NET Framework 4.8 or later to be installed in order to work properly. This
dependency will be installed automatically if there is internet connectivity available at install time but when running
in an offline envirnonment it will need to be pre-installed before installing Ziti Desktop Edge for Windows.

You can obtain the .NET 4.8 offline installer from Microsoft. As of this writing, the url is:
https://support.microsoft.com/en-us/topic/microsoft-net-framework-4-8-offline-installer-for-windows-9d23f658-3b97-68ab-d013-aa3c3e7495e0

You can check what version of .NET is installed by running the following powershell command:
```
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP' -Recurse |
    Get-ItemProperty -Name version -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -Match '^(?!S)\p{L}' } |
    Select-Object PSChildName, version
```

This should output something like the following:
```
PSChildName Version
----------- -------
Client      4.8.09032
Full        4.8.09032
Client      4.0.0.0
```

## Automatic Upgrades

The Ziti Desktop Edge for Windows will automatically keep itself up to date. When it is installed, a default url will be used for
detecting updates. By default, this url will point to the 'stable' stream of updates: https://get.openziti.io/zdew/stable.json

There are currently two other streams available to subscribe to latest and beta:
* https://get.openziti.io/zdew/latest.json
* https://get.openziti.io/zdew/beta.json

The 'latest' stream should be updated along with a GitHub release marked as latest. The 'beta' stream is updated often and is
representative of the absolutely newest release available. These releases are marked in GitHub as "Pre-release".

The 'stable' stream does not follow a rigorous pattern. When it's decided the latest release has been stable for "long enough", 
the latest stream will be promoted to stable.

You may opt to disable automatic updates of the Ziti Desktop Edge for Windows by going to the Main Menu -> Advanced Settings ->
Configure Automatic Upgrades screen and choosing to enable or disable the functionality.

### Configuring a System for Offline Installation

Public Key Infrastructure is complex and verifying trust is vital. Part of this trust verification is around certificate revocation
lists - CRLs. The installer executables produced by the OpenZiti project are signed by two different entities. One entity is an
indepependant third party and the other is the OpenZiti code signing CA. Also, as part of the signing process a timestamp is 
produced at the time of signing using yet another CA. If you are going to use the automatic upgrade functionality, please see 
[offline-installation/README.md]() for more information as to how to enable this functionality in an entirely offline environment.

### Customizing the Automatic Update URL

Users may change the url used for automatic updates by going to Main Menu -> Advanced Settings -> Configure Automatic Upgrades
and changing the URL. There is no schema supplied for this document at this time but it very roughly matches the GitHub api
for releases as this API was used before allowing the URL to be changed. The format of the document can be seen by inspecting
the files in [./release-streams]() and is subject (although unlikey) to change.

There is currently no mechanism to update the URL at install time but the URL can be changed by changing the file located at
`C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\ZitiUpdateService\settings.json`. The file is very simple,
containing two entries:
```
{
  "AutomaticUpdatesDisabled": false,
  "AutomaticUpdateURL": "https://get.openziti.io/zdew/stable.json"
}
```

Updating the file will require administrator rights as the file is in a protected folder. After updating, the `ziti-monitor`
service will need to be restarted for the setting to take effect. A restart of the machine is the easiest way to ensure the
serivce is restarted. This process could be scripted easily enough, no script exists in the OpenZiti project at this time to
manage this file in any automatic fashion, doing so is currently out of scope of the project.

### Acceptable Files For Automatic Installation

The Ziti Desktop Edge for Windows will not allow automatic installation of any binary. The file referenced by the automatic
upgrade URL must be specifically signed by the OpenZiti project's CA. Only files signed by an OpenZiti signing certificate
will be considered eligible for automatic installation. You will not be able to build your own version of the software and
expect it to be automatically updated. The expected root CA is compiled directly into the executable. You will need to 
change and deploy your own version with your own CA in order to have the automatic upgrade capability work and your version
will not be usable by other installations of the Ziti Desktop Edge for Windows.


## Microsoft Defender SmartScreen Issues

After the new signing process was adopted where we sign with a legitimate, purchased signing certificate from a trusted CA as well
as signing using our own signing certificates, Microsoft Defender SmartScreen began flagging the installer as potentially malicious.
Thankfully we were able to submit a ticket and get an understanding of the issue and how to resolve this going forward. This is a
short synopses of what transpired:

* Contact support through our "Pay-for" avenues
* Submit the exe along with a small description of the issue
* Talk to a representative about the issue over MS Teams - provide additional information
* Microsoft Defender SmartScreen team analyzed the exe, which confirmed the .exe was clean
* Establish "reputation" in the Microsoft Defender SmartScreen system by submitting the exe

Here is a brief transcript of the emails returned from support along with the steps taken:


### Response after submitting the exe for review

We have reviewed the reported application "ziti.desktop.edge.client-x.y.z.exe" and confirm that it is clean. 
Accordingly we have adjusted the SmartScreen ratings to address this issue. 
Now we can confirm that there will be no SmartScreen warning on the file. 

In future if you wish to report false warning you should perform the following steps. 
This ensures that the issue is put into our system so our team members can investigate it as soon as possible. 
Site Owner reports are prioritized.

•	Attempt downloading the file using Microsoft Edge  
•	When informed the download was blocked by SmartScreen Filter  
•	Click the blocked download and select Report this file as safe | I am the owner or representative of this website and I want to report an incorrect warning about it  
•	Fill out the form completely and submit. You will receive an email confirming receipt of your report.  

### Response after another brief chat via MS Teams

The warning you reported indicates that the application being downloaded and run does not yet have known reputation in our system. 
We can confirm that the submitted application has since established reputation and attempting to download or run the application 
should no longer show any warnings.

The signing certificate (thumbprint: “51d52749da021f095b021df8c09bd62c55c36f1f”) is still in the process of establishing reputation.
 
When a certificate is renewed, or if a new certificate is used to sign files, fresh reputation needs to be established.
The reputation of the previous certificate is one of the important elements in attaching reputation to the newer certificate.
Typically, a renewed certificate will establish reputation more quickly than a completely new certificate such as one from a
different CA or one which uses different organization details (company name, etc.).
 
Once your signing certificate has gained reputation in our system, all applications or releases signed with your certificate 
should have warn-free experience (assuming nothing happens to denigrate the reputation of the certificate, such as being
used to sign malware).
  
For more information about Windows Defender SmartScreen®, refer to the following resources: 
https://docs.microsoft.com/en-us/windows/security/threat-protection/microsoft-defender-smartscreen/microsoft-defender-smartscreen-overview 
https://feedback.smartscreen.microsoft.com/smartscreenfaq.aspx 
  
For more information about code signing requirements, refer to:             
http://msdn.microsoft.com/en-us/windows/hardware/gg487309.aspx 
 

## Submit the Ziti Desktop Edge application to Microsoft store

Build the application using the same step "Building a release" above, it will generate a msixuplod file in the Build_MSIX_APPXSetupFiles folder 
inside the Installer directory. Then login to microsoft partner portal and follow the below steps to submit the application to microsoft store.

1. Go to Windows & xbox and create an application with the name Ziti Desktop Edge. Once this application name is reserved for you, you can create 
   the submission. (One time step)
2. Create a package flight and upload the msixupload file. When you click on save, it will validate the package. It will verify whether the 
   applcation Id and name are matching to what is configured in the partner portal. If there are validation errors, you need to fix the errors 
   first and upload the package again. You dont need to digitally sign the exe when you create the package, the partner portal will sign it for you.
3. Once the package is validated and saved successfully, create a submission with this packge. Ziti application requires restricted capabilities like runFullTrust, localSystemServices and packagedServices. These capabilities are configured in the new Package aip file. So this submission has to be approved by the partner portal, when you submit it for the first time. You need to provide explanation stating why we need whose features and submit to the store for approval.
 
## Testing Automatic Upgrades

When updating the ZitiUpdateService (aka ZitiMonitorService), it's important to ensure the upgrade solution continues to work.
For information about this testing, see [releasing](./releasing.md).

