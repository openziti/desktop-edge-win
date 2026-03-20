using System.Net;
using System.Security.Cryptography;
using ZitiUpdateService.Checkers.PeFile;

namespace ZitiDesktopEdgeTests {
	[TestClass]
	public class SignedFilesTest {
		static SignedFilesTest() {
			TestUtils.ConfigureNLog();
		}
#pragma warning disable CS8601 // Possible null reference assignment.
		private string exeloc = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#pragma warning restore CS8601 // Possible null reference assignment.

		[TestMethod]
		public void TestOlderFiles() {
			var zdew1_2_13 = Path.Combine(exeloc, "TestFiles", "1.2.13.exe");
			TestUtils.DownloadFileAsync("https://github.com/openziti/desktop-edge-win/releases/download/1.2.13/Ziti.Desktop.Edge.Client-1.2.13.exe", zdew1_2_13).Wait();
			try {
				new SignedFileValidator(zdew1_2_13).Verify();
			} catch (CryptographicException expected) {
				StringAssert.Contains(expected.Message, "Executable not signed by an appropriate certificate");
			} catch (Exception e) {
				Assert.Fail("Unexpected exception thrown", e);
			}
		}

		[TestMethod]
		public void TestOpenZitiSigner2022() {
			var zdew2_1_16 = Path.Combine(exeloc, "TestFiles", "2.1.16.exe");
			TestUtils.DownloadFileAsync("https://github.com/openziti/desktop-edge-win/releases/download/2.1.16/Ziti.Desktop.Edge.Client-2.1.16.exe", zdew2_1_16).Wait();

			// this should NOT throw an exception...
			new SignedFileValidator(zdew2_1_16).Verify();
		}

		[TestMethod]
		public void TestOpenZitiSigner2024() {
			var new_zdew = Path.Combine(@"D:\git\github\openziti\desktop-edge-win\Installer\Output\Ziti Desktop Edge Client-2.2.2.exe");
			
			// this should NOT throw an exception...
			new SignedFileValidator(new_zdew).Verify();
		}
	}
}