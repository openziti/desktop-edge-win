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
public class IdentityLifecycleTests {

	[ClassInitialize]
	public static void Init(TestContext _) {
		QuickstartFixture.StartQuickstart();
		QuickstartFixture.CreateTestIdentities();
	}

	[TestMethod]
	public async Task AddIdentity_Succeeds() {
		DataClient client = await ConnectClient();
		await AddJwt(client, "normal-user-01");
		// AddIdentityAsync throws ServiceException when Code != 0. Reaching this line is the assertion.
	}

	[TestMethod]
	public async Task DisableIdentity_SetsActiveFalse() {
		DataClient client = await ConnectClient();
		await AddJwt(client, "normal-user-02");

		Identity enrolled = await WaitForIdentity(client, "normal-user-02");
		await client.IdentityOnOffAsync(enrolled.Identifier, false);
		Identity off = await WaitForActive(client, "normal-user-02", false);
		Assert.IsFalse(off.Active, "identity should be inactive after disable");
	}

	[TestMethod]
	public async Task EnableIdentity_SetsActiveTrue() {
		DataClient client = await ConnectClient();
		await AddJwt(client, "normal-user-03");

		Identity enrolled = await WaitForIdentity(client, "normal-user-03");
		await client.IdentityOnOffAsync(enrolled.Identifier, false);
		await WaitForActive(client, "normal-user-03", false);
		await client.IdentityOnOffAsync(enrolled.Identifier, true);
		Identity on = await WaitForActive(client, "normal-user-03", true);
		Assert.IsTrue(on.Active, "identity should be active after enable");
	}

	private static async Task<DataClient> ConnectClient() {
		var client = new DataClient("integration-test");
		try {
			await client.ConnectAsync();
		} catch (ServiceException ex) {
			Assert.Inconclusive("Could not connect to ziti-edge-tunnel pipes; is the service running? " + ex.Message);
		}
		await client.WaitForConnectionAsync();
		return client;
	}

	private static async Task AddJwt(DataClient client, string name) {
		string jwtPath = Path.Combine(QuickstartFixture.IdentityDir!, name + ".jwt");
		string jwtContent = File.ReadAllText(jwtPath).Trim();
		await client.AddIdentityAsync(new EnrollIdentifierPayload {
			UseKeychain = false,
			IdentityFilename = name,
			JwtContent = jwtContent,
		});
	}

	// ZET's response to AddIdentity is an async ack with no Identity body. The identity
	// becomes toggle-ready only after the controller handshake completes; ZET populates
	// ControllerVersion on the status record at that point (same as Loaded=true on the wire).
	// Poll until we see that. The UI observes the same delay via OnIdentityEvent.
	private static async Task<Identity> WaitForIdentity(DataClient client, string name) {
		while (true) {
			ZitiTunnelStatus status = await client.GetStatusAsync();
			Identity? match = status?.Data?.Identities?.FirstOrDefault(i => i.Name == name);
			if (match is not null && !string.IsNullOrEmpty(match.Identifier) && !string.IsNullOrEmpty(match.ControllerVersion)) {
				return match;
			}
			await Task.Delay(100);
		}
	}

	// IdentityOnOff's IPC response echoes the request in Data, not an Identity, so
	// IdentityResponse.Data deserializes to defaults (Active=false). The real state
	// change is visible on the next GetStatusAsync after ZET applies the toggle.
	private static async Task<Identity> WaitForActive(DataClient client, string name, bool desired) {
		while (true) {
			ZitiTunnelStatus status = await client.GetStatusAsync();
			Identity? match = status?.Data?.Identities?.FirstOrDefault(i => i.Name == name);
			if (match is not null && match.Active == desired) {
				return match;
			}
			await Task.Delay(100);
		}
	}
}
