using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Add Identity, With JWT, opens the 3rd Party Certificate dialog for a CA or ottca JWT. The dialog sends the cert and
/// key as the paths picked in its file dialogs.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ThirdPartyCaTests
{
    private static readonly By CaDialogTitle = By.XPath("//Text[@Name='3rd Party Certificate']");
    // The certificate's Browse button has no x:Name.
    private static readonly By BrowseCertButton = By.XPath("//Button[@Name='Browse' and not(@AutomationId='BrowseKeyBtn')]");
    private static readonly TimeSpan FileDialogTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FileDialogOpenProbe = TimeSpan.FromSeconds(15);

    private readonly IntegrationFixture _fixture;

    public ThirdPartyCaTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 90000)]
    public async Task AutocaEnrollCreatesIdentity()
    {
        string name = nameof(AutocaEnrollCreatesIdentity);
        const string caName = "test_tpca";
        const string commonName = "test_tpca_user1";
        string caId = _fixture.Quickstart.CreateThirdPartyCa(caName, Array.Empty<string>());
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, commonName, Array.Empty<string>());
        string caJwt = await _fixture.Quickstart.GetCaJwtAsync(caId);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        OpenCaDialog(s, caJwt);
        await VerifyStep(Capture(s), name, "01-ca-dialog");
        ChooseCertFiles(s, cert);
        await VerifyStep(Capture(s), name, "02-files-chosen");
        JObject reply = Join(s, cert);
        Assert.Equal(0, (int?)reply["Code"]);
        WaitForEnrolledRow(s, AutocaIdentityName(caName, commonName));
        await VerifyStep(Capture(s), name, "03-enrolled");
        AssertIdentityFileKeepsCertPaths(cert);
    }

    [Fact(Timeout = 90000)]
    public async Task OttcaEnrollsPreCreatedIdentity()
    {
        string name = nameof(OttcaEnrollsPreCreatedIdentity);
        const string caName = "test_tpca_ottca";
        // Created by the fixture import, so the controller already holds it under this name.
        const string identityName = "test_tpca_ottca_user1";
        _fixture.Quickstart.CreateThirdPartyCa(caName, Array.Empty<string>());
        string ottcaJwt = _fixture.Quickstart.CreateOttCaEnrollment(identityName, caName);
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, identityName, Array.Empty<string>());

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        OpenCaDialog(s, ottcaJwt);
        await VerifyStep(Capture(s), name, "01-ca-dialog");
        ChooseCertFiles(s, cert);
        JObject reply = Join(s, cert);
        Assert.Equal(0, (int?)reply["Code"]);
        WaitForEnrolledRow(s, identityName);
        await VerifyStep(Capture(s), name, "02-enrolled");
        AssertIdentityFileKeepsCertPaths(cert);
    }

    private void AssertIdentityFileKeepsCertPaths(Quickstart.ClientCert cert)
    {
        string identityFile = AddedIdentityFile(_fixture);
        AssertJwtEnrolledIdentityFile(identityFile);
        // The SDK keeps the strings it enrolled with, so the identity stays tied to the user's files.
        JObject file = ReadIdentityFile(identityFile);
        Assert.Equal(cert.CertPath, (string?)file.SelectToken("id.cert"));
        Assert.Equal(cert.KeyPath, (string?)file.SelectToken("id.key"));
    }

    [Fact(Timeout = 120000)]
    public async Task RejectsAddingSameIdentityTwice()
    {
        string name = nameof(RejectsAddingSameIdentityTwice);
        const string caName = "test_tpca_dup";
        const string commonName = "test_tpca_dup_user1";
        string caId = _fixture.Quickstart.CreateThirdPartyCa(caName, Array.Empty<string>());
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, commonName, Array.Empty<string>());
        string caJwt = await _fixture.Quickstart.GetCaJwtAsync(caId);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        Enroll(s, caJwt, cert, AutocaIdentityName(caName, commonName));
        SaveStep(s, name, "01-enrolled");

        // The app names the identity after the JWT file, so the same JWT again always reuses the first one's name.
        OpenCaDialog(s, caJwt);
        ChooseCertFiles(s, cert);
        await AssertJoinRejected(s, name, "02-add-failure-blurb", cert, "identity exists with the same name");
        Assert.Equal(1, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task RejectsReenrollingSameCert()
    {
        string name = nameof(RejectsReenrollingSameCert);
        const string caName = "test_tpca_reuse";
        const string commonName = "test_tpca_reuse_user1";
        string caId = _fixture.Quickstart.CreateThirdPartyCa(caName, Array.Empty<string>());
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, commonName, Array.Empty<string>());
        string caJwt = await _fixture.Quickstart.GetCaJwtAsync(caId);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        Enroll(s, caJwt, cert, AutocaIdentityName(caName, commonName));
        SaveStep(s, name, "01-enrolled");

        // Forgetting frees the name in ZET, while the controller keeps the identity that holds the cert.
        OpenIdentityDetails(s, AutocaIdentityName(caName, commonName));
        ClickAt(s, WaitFor(s, ById("ForgetIdentityButton")));
        ClickAt(s, WaitFor(s, ById("ConfirmButton")));
        WaitUntil(s, "the forgotten identity leaves the landing list", ControllerTimeout, () => IdentityRowCount(s) == 0);

        OpenCaDialog(s, caJwt);
        ChooseCertFiles(s, cert);
        await AssertJoinRejected(s, name, "02-add-failure-blurb", cert, "certificate already in use");
        AssertNoNewIdentity(s);
    }

    [Fact(Timeout = 90000)]
    public async Task RejectsCertFromUnregisteredCa()
    {
        const string unregisteredCa = "test_tpca_unregistered";
        string caId = _fixture.Quickstart.CreateThirdPartyCa("test_tpca_neg", Array.Empty<string>());
        _fixture.Quickstart.CreateLocalPkiCa(unregisteredCa);
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(unregisteredCa, "test_tpca_unregistered_user1",
            Array.Empty<string>());
        await AssertFirstJoinRejected(nameof(RejectsCertFromUnregisteredCa), caId, cert, "key/cert are invalid");
    }

    [Fact(Timeout = 90000)]
    public async Task RejectsCertMissingExternalId()
    {
        const string caName = "test_tpca_extid";
        // The CA reads the externalId from a SAN URI, which `ziti pki create client` certs never carry.
        string caId = _fixture.Quickstart.CreateThirdPartyCa(caName,
            new[] { "--location", "SAN_URI", "--matcher", "ALL", "--parser", "NONE" });
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, "test_tpca_extid_user1",
            Array.Empty<string>());
        await AssertFirstJoinRejected(nameof(RejectsCertMissingExternalId), caId, cert, "expected to contain an externalId");
    }

    [Fact(Timeout = 90000)]
    public async Task RejectsExpiredCert()
    {
        const string caName = "test_tpca_expired";
        string caId = _fixture.Quickstart.CreateThirdPartyCa(caName, Array.Empty<string>());
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, "test_tpca_expired_user1",
            new[] { "--expire-limit=-1" });
        await AssertFirstJoinRejected(nameof(RejectsExpiredCert), caId, cert, "key/cert are invalid");
    }

    /// <summary>Join with cert as the session's first identity and assert ZET rejects it with zetError.</summary>
    private async Task AssertFirstJoinRejected(string name, string caId, Quickstart.ClientCert cert, string zetError)
    {
        string caJwt = await _fixture.Quickstart.GetCaJwtAsync(caId);
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        OpenCaDialog(s, caJwt);
        ChooseCertFiles(s, cert);
        await AssertJoinRejected(s, name, "01-add-failure-blurb", cert, zetError);
        AssertNoNewIdentity(s);
    }

    private static async Task AssertJoinRejected(AppiumSession s, string name, string step, Quickstart.ClientCert cert,
        string zetError)
    {
        JObject reply = Join(s, cert);
        await VerifyStep(CaptureBlurbOnReply(s, AddIdentityLine), name, step);
        // The blurb shows no detail from ZET, so only the reply proves why the add failed.
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains(zetError, (string?)reply["Error"]);
    }

    private void AssertNoNewIdentity(AppiumSession s)
    {
        Assert.Equal(0, IdentityRowCount(s));
        string identityFile = AddedIdentityFile(_fixture);
        Assert.False(File.Exists(identityFile), $"ZET wrote an identity file for a rejected add: {identityFile}");
    }

    private static void Enroll(AppiumSession s, string caJwt, Quickstart.ClientCert cert, string identityName)
    {
        OpenCaDialog(s, caJwt);
        ChooseCertFiles(s, cert);
        Assert.Equal(0, (int?)Join(s, cert)["Code"]);
        WaitForEnrolledRow(s, identityName);
    }

    private static string AutocaIdentityName(string caName, string commonName) => $"{caName}-{commonName}";

    private static void WaitForEnrolledRow(AppiumSession s, string identityName)
    {
        AssertEnrollmentAdded(s, AddIdentityLine);
        WaitForZetEventAfter(s, AddIdentityLine, ControllerConnectedEvent);
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), "the CA identity shows on the landing list");
    }

    private static void OpenCaDialog(AppiumSession s, string caJwt)
    {
        WriteTestJwt(caJwt);
        ClickAddIdentityWithJwt(s);
        WaitFor(s, CaDialogTitle);
    }

    private static void ChooseCertFiles(AppiumSession s, Quickstart.ClientCert cert)
    {
        ChooseFile(s, BrowseCertButton, "Select Certificate File", cert.CertPath);
        WaitUntil(s, "the certificate box shows the chosen file", TimeSpan.FromSeconds(5),
            () => TextById(s, "CertificateFile") == cert.CertPath);
        ChooseFile(s, ById("BrowseKeyBtn"), "Select Key File", cert.KeyPath);
        WaitUntil(s, "the key box shows the chosen file", TimeSpan.FromSeconds(5),
            () => TextById(s, "KeyFile") == cert.KeyPath);
    }

    /// <summary>Click Join Network, assert the sent AddIdentity carries cert's paths, and return ZET's reply.</summary>
    private static JObject Join(AppiumSession s, Quickstart.ClientCert cert)
    {
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => WaitForId(s, "JoinNetworkBtn").Click());
        JObject sent = UiCommand(s.Relay!, AddIdentityLine);
        Assert.Equal(cert.CertPath, (string?)sent["Data"]!["Certificate"]);
        Assert.Equal(cert.KeyPath, (string?)sent["Data"]!["Key"]);
        return reply;
    }

    /// <summary>Click browse until the app's Open dialog titled title shows, then choose path in it.</summary>
    private static void ChooseFile(AppiumSession s, By browse, string title, string path)
    {
        IntPtr? dialog = null;
        // Clicked again only when no dialog opened, since WinAppDriver can drop a click. A first Open dialog can outlast
        // the probe, and the docked window hides its Browse button before the dialog shows, so a retry clicks only a
        // displayed button and otherwise keeps polling for the dialog.
        WaitUntil(s, $"the '{title}' dialog opens", FileDialogOpenProbe * 2 + TimeSpan.FromSeconds(10), () =>
        {
            dialog = FileOpenDialog.Find(s.ProcessId, title);
            if (dialog != null)
                return true;
            IWebElement? button = s.Driver.FindElements(browse).FirstOrDefault(e => e.Displayed);
            if (button == null)
                return false;
            ClickAt(s, button);
            DateTime probe = DateTime.UtcNow + FileDialogOpenProbe;
            while (dialog == null && DateTime.UtcNow < probe)
            {
                Thread.Sleep(100);
                dialog = FileOpenDialog.Find(s.ProcessId, title);
            }
            return dialog != null;
        });
        FileOpenDialog.Choose(dialog!.Value, path, FileDialogTimeout);
    }
}
