/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge.Client.IntegrationTests;

[TestClass]
public class ConnectAndStatusTests {

	[TestMethod]
	public async Task Connect_GetStatus_ReturnsTunnelInfo() {
		var client = new DataClient("integration-test");

		try {
			await client.ConnectAsync();
		} catch (ServiceException ex) {
			Assert.Inconclusive("Could not connect to ziti-edge-tunnel pipes; is the service running? " + ex.Message);
		}

		await client.WaitForConnectionAsync();

		ZitiTunnelStatus status = await client.GetStatusAsync();

		Assert.IsNotNull(status, "GetStatusAsync returned null.");
		Assert.AreEqual(0, status.Code, $"GetStatus returned non-zero code. Message='{status.Message}', Error='{status.Error}'.");
	}
}
