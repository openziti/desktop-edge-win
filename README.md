# Ziti Desktop Edge for Windows

(the uwp project which used to be here has moved to [uwp-vpnplugin-archive](.uwp-vpnplugin-archive) and is likely abandoned)

The Ziti Desktop Edge for Windows is an application that is necessary to integrate applications which cannot embed a Ziti SDK
directly into the application. This is colloquially known as a "brown field" Ziti-enabled application because the app
itself has no understanding that it has been Ziti-enabled.

In order for an application that has no knowledge of being Ziti-enabled to work the connections established by the app
must be intercepted before leaving the computer and routed through the Ziti network. This is accomplished by three main
components:

* [wintun](https://www.wintun.net) - provides a Layer 3 TUN for Windows
* A Windows service which runs as the local system account which creates the TUN as well as manages the Ziti connections
* A Windows UWP UI application that allows the interactively logged on user to interact with the Windows service

## Building A Release

see [releasing](./releasing.md)

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

Build the application using the same step "Building a release" above, it will generate a msixuplod file in the Build_MSIX_APPXSetupFiles folder inside the Installer directory. Then login to microsoft partner portal and follow the below steps to submit the application to microsoft store.

1. Go to Windows & xbox and create an application with the name Ziti Desktop Edge. Once this application name is reserved for you, you can create the submission. (One time step)
2. Create a package flight and upload the msixupload file. When you click on save, it will validate the package. It will verify whether the applcation Id and name are matching to what is configured in the partner portal. If there are validation errors, you need to fix the errors first and upload the package again. You dont need to digitally sign the exe when you create the package, the partner portal will sign it for you.
3. Once the package is validated and saved successfully, create a submission with this packge. Ziti application requires restricted capabilities like runFullTrust, localSystemServices and packagedServices. These capabilities are configured in the new Package aip file. So this submission has to be approved by the partner portal, when you submit it for the first time. You need to provide explanation stating why we need whose features and submit to the store for approval.