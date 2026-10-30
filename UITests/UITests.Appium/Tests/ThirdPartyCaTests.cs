using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ThirdPartyCaTests
{
    private readonly IntegrationFixture _fixture;

    public ThirdPartyCaTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
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

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        OpenCaDialog(session, caJwt);
        await VerifyStep(session, Capture(session), name, "01-ca-dialog");
        ChooseCertFiles(session, cert);
        await VerifyStep(session, Capture(session), name, "02-files-chosen");
        JObject reply = Join(session, cert);
        Assert.Equal(0, (int?)reply["Code"]);
        WaitForEnrolledRow(session, AutocaIdentityName(caName, commonName));
        await VerifyStep(session, Capture(session), name, "03-enrolled");
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
        string enrollmentId = _fixture.Quickstart.RunChecked("edge", "create", "enrollment", "ottca", identityName, caName)
            .Stdout.Trim();
        Quickstart.CliResult enrollmentList =
            _fixture.Quickstart.RunChecked("edge", "list", "enrollments", $"id=\"{enrollmentId}\"", "-j");
        JArray enrollments = JObject.Parse(enrollmentList.Stdout)["data"] as JArray ?? new JArray();
        if (enrollments.Count != 1)
            throw new InvalidOperationException(
                $"expected exactly one enrollment {enrollmentId}, found {enrollments.Count}. stdout: {enrollmentList.Stdout}");
        string? ottcaJwt = (string?)enrollments[0]["jwt"];
        if (string.IsNullOrEmpty(ottcaJwt))
            throw new InvalidOperationException($"enrollment {enrollmentId} has no jwt. stdout: {enrollmentList.Stdout}");
        Quickstart.ClientCert cert = _fixture.Quickstart.CreateClientCert(caName, identityName, Array.Empty<string>());

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        OpenCaDialog(session, ottcaJwt);
        await VerifyStep(session, Capture(session), name, "01-ca-dialog");
        ChooseCertFiles(session, cert);
        JObject reply = Join(session, cert);
        Assert.Equal(0, (int?)reply["Code"]);
        WaitForEnrolledRow(session, identityName);
        await VerifyStep(session, Capture(session), name, "02-enrolled");
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

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        Enroll(session, caJwt, cert, AutocaIdentityName(caName, commonName));
        SaveStep(session, name, "01-enrolled");

        // The app names the identity after the JWT file, so the same JWT again always reuses the first one's name.
        OpenCaDialog(session, caJwt);
        ChooseCertFiles(session, cert);
        await AssertJoinRejected(session, name, "02-add-failure-blurb", cert, "identity exists with the same name");
        Assert.Equal(1, IdentityRowCount(session));
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

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        Enroll(session, caJwt, cert, AutocaIdentityName(caName, commonName));
        SaveStep(session, name, "01-enrolled");

        // Forgetting frees the name in ZET, while the controller keeps the identity that holds the cert.
        OpenIdentityDetails(session, AutocaIdentityName(caName, commonName));
        ClickAt(session, WaitFor(session, ById("ForgetIdentityButton")));
        ClickAt(session, WaitFor(session, ById("ConfirmButton")));
        WaitUntil(session, "the forgotten identity leaves the landing list", ControllerTimeout, () => IdentityRowCount(session) == 0);

        OpenCaDialog(session, caJwt);
        ChooseCertFiles(session, cert);
        await AssertJoinRejected(session, name, "02-add-failure-blurb", cert, "certificate already in use");
        AssertNoNewIdentity(session);
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

    private async Task AssertFirstJoinRejected(string name, string caId, Quickstart.ClientCert cert, string zetError)
    {
        string caJwt = await _fixture.Quickstart.GetCaJwtAsync(caId);
        await using AppiumSession session = await LaunchAsync(_fixture, name);
        OpenCaDialog(session, caJwt);
        ChooseCertFiles(session, cert);
        await AssertJoinRejected(session, name, "01-add-failure-blurb", cert, zetError);
        AssertNoNewIdentity(session);
    }

    private static async Task AssertJoinRejected(AppiumSession session, string name, string step, Quickstart.ClientCert cert,
        string zetError)
    {
        JObject reply = Join(session, cert);
        await VerifyStep(session, CaptureBlurbOnReply(session, AddIdentityLine), name, step);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains(zetError, (string?)reply["Error"]);
    }

    private void AssertNoNewIdentity(AppiumSession session)
    {
        Assert.Equal(0, IdentityRowCount(session));
        string identityFile = AddedIdentityFile(_fixture);
        Assert.False(File.Exists(identityFile), $"ZET wrote an identity file for a rejected add: {identityFile}");
    }

    private static void Enroll(AppiumSession session, string caJwt, Quickstart.ClientCert cert, string identityName)
    {
        OpenCaDialog(session, caJwt);
        ChooseCertFiles(session, cert);
        Assert.Equal(0, (int?)Join(session, cert)["Code"]);
        WaitForEnrolledRow(session, identityName);
    }

    private static string AutocaIdentityName(string caName, string commonName) => $"{caName}-{commonName}";

    private static void WaitForEnrolledRow(AppiumSession session, string identityName)
    {
        AssertEnrollmentAdded(session, AddIdentityLine);
        WaitForZetEventAfter(session, AddIdentityLine, ControllerConnectedEvent);
        WaitForController(session, By.XPath($"//Text[@Name='{identityName}']"), "the CA identity shows on the landing list");
    }

    private static void OpenCaDialog(AppiumSession session, string caJwt)
    {
        WriteTestJwt(caJwt);
        ClickAddIdentityWithJwt(session);
        WaitFor(session, By.XPath("//Text[@Name='3rd Party Certificate']"));
    }

    private static void ChooseCertFiles(AppiumSession session, Quickstart.ClientCert cert)
    {
        // The certificate's Browse button has no x:Name.
        ClickAt(session, WaitFor(session, By.XPath("//Button[@Name='Browse' and not(@AutomationId='BrowseKeyBtn')]")));
        FileOpenDialog.Choose(session, "Select Certificate File", cert.CertPath);
        WaitForText(session, "CertificateFile", cert.CertPath, AppResponseTimeout);
        ClickAt(session, WaitFor(session, ById("BrowseKeyBtn")));
        FileOpenDialog.Choose(session, "Select Key File", cert.KeyPath);
        WaitForText(session, "KeyFile", cert.KeyPath, AppResponseTimeout);
    }

    private static JObject Join(AppiumSession session, Quickstart.ClientCert cert)
    {
        JObject reply = SendAndWaitForZetReply(session, AddIdentityLine, () => WaitForId(session, "JoinNetworkBtn").Click());
        JObject sent = UiCommand(session.Relay!, AddIdentityLine);
        Assert.Equal(cert.CertPath, (string?)sent["Data"]!["Certificate"]);
        Assert.Equal(cert.KeyPath, (string?)sent["Data"]!["Key"]);
        return reply;
    }
}
