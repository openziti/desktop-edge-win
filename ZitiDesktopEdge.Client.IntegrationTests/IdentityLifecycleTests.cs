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

[Collection("Quickstart")]
public class IdentityLifecycleTests {
	private readonly QuickstartFixture _quickstartFixture;

	public IdentityLifecycleTests(QuickstartFixture quickstartFixture) {
		_quickstartFixture = quickstartFixture;
	}

	[Fact]
	public async Task AddIdentity_WithJwt_Succeeds() {
		DataClient client = await ConnectClient();
		await AddIdentityFromJwt(client, "normal-user-01");
		await WaitForEnrollment(client, "normal-user-01");
	}

	[Fact]
	public async Task IdentityOnOff_Disable_SetsActiveFalse() {
		DataClient client = await ConnectClient();
		await AddIdentityFromJwt(client, "normal-user-02");

		Identity enrolled = await WaitForEnrollment(client, "normal-user-02");
		await client.IdentityOnOffAsync(enrolled.Identifier, false);
		Identity off = await WaitForActiveState(client, "normal-user-02", false);
		Assert.False(off.Active, "identity should be inactive after disable");
	}

	[Fact]
	public async Task IdentityOnOff_ReenableAfterDisable_SetsActiveTrue() {
		DataClient client = await ConnectClient();
		await AddIdentityFromJwt(client, "normal-user-03");

		Identity enrolled = await WaitForEnrollment(client, "normal-user-03");
		await client.IdentityOnOffAsync(enrolled.Identifier, false);
		await WaitForActiveState(client, "normal-user-03", false);
		await client.IdentityOnOffAsync(enrolled.Identifier, true);
		Identity on = await WaitForActiveState(client, "normal-user-03", true);
		Assert.True(on.Active, "identity should be active after enable");
	}

	[Fact]
	public async Task RemoveIdentity_RemovesFromStatus() {
		DataClient client = await ConnectClient();
		await AddIdentityFromJwt(client, "normal-user-06");

		Identity enrolled = await WaitForEnrollment(client, "normal-user-06");
		await client.RemoveIdentityAsync(enrolled.Identifier);
		await WaitForIdentityAbsent(client, "normal-user-06");

		ZitiTunnelStatus status = await client.GetStatusAsync();
		Assert.DoesNotContain(status.Data.Identities, i => i.Name == "normal-user-06");
	}

	[Fact]
	public async Task ServiceRestart_PreservesIdentityStates() {
		DataClient client = await ConnectClient();
		await AddIdentityFromJwt(client, "normal-user-04");
		await AddIdentityFromJwt(client, "normal-user-05");

		Identity toDisable = await WaitForEnrollment(client, "normal-user-04");
		await WaitForEnrollment(client, "normal-user-05");
		await client.IdentityOnOffAsync(toDisable.Identifier, false);
		await WaitForActiveState(client, "normal-user-04", false);

		await RestartZitiService();

		DataClient reconnected = await ConnectClient();
		Identity disabled = await WaitForActiveState(reconnected, "normal-user-04", false);
		Identity enabled = await WaitForActiveState(reconnected, "normal-user-05", true);
		Assert.False(disabled.Active, "disabled identity should persist disabled after restart");
		Assert.True(enabled.Active, "enabled identity should persist enabled after restart");
	}

	private static async Task RestartZitiService() {
		var monitor = new MonitorClient("integration-test-monitor");
		await monitor.ConnectAsync();
		await monitor.WaitForConnectionAsync();
		await monitor.StopServiceAsync();
		await monitor.StartServiceAsync(TimeSpan.FromSeconds(60));
	}

	private static async Task<DataClient> ConnectClient() {
		var client = new DataClient("integration-test");
		await client.ConnectAsync();
		await client.WaitForConnectionAsync();
		return client;
	}

	private async Task AddIdentityFromJwt(DataClient client, string name) {
		string jwtPath = Path.Combine(_quickstartFixture.IdentityDir, name + ".jwt");
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
	private static async Task<Identity> WaitForEnrollment(DataClient client, string name) {
		while (true) {
			ZitiTunnelStatus status = await client.GetStatusAsync();
			Identity? identity = status?.Data?.Identities?.FirstOrDefault(i => i.Name == name);
			if (identity is not null && !string.IsNullOrEmpty(identity.Identifier) && !string.IsNullOrEmpty(identity.ControllerVersion)) {
				return identity;
			}
			await Task.Delay(100);
		}
	}

	private static async Task WaitForIdentityAbsent(DataClient client, string name) {
		while (true) {
			ZitiTunnelStatus status = await client.GetStatusAsync();
			bool present = status?.Data?.Identities?.Any(i => i.Name == name) ?? false;
			if (!present) {
				return;
			}
			await Task.Delay(100);
		}
	}

	// IdentityOnOff's IPC response echoes the request in Data, not an Identity, so
	// IdentityResponse.Data deserializes to defaults (Active=false). The real state
	// change is visible on the next GetStatusAsync after ZET applies the toggle.
	private static async Task<Identity> WaitForActiveState(DataClient client, string name, bool expected) {
		while (true) {
			ZitiTunnelStatus status = await client.GetStatusAsync();
			Identity? identity = status?.Data?.Identities?.FirstOrDefault(i => i.Name == name);
			if (identity is not null && identity.Active == expected) {
				return identity;
			}
			await Task.Delay(100);
		}
	}
}
