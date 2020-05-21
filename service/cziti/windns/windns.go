/*
 * Copyright NetFoundry, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

package windns

import (
	"bytes"
	"fmt"
	"net"
	"os"
	"os/exec"
	"strings"

	"github.com/michaelquigley/pfxlog"
)

var log = pfxlog.Logger()

func ResetDNS() {
	log.Info("restoring dns to original-ish state")

	script := `Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR resetting DNS", err)
	}
}

func GetUpstreamDNS() []string {
	ResetDNS()

	script := `Get-DnsClientServerAddress | ForEach-Object { $_.ServerAddresses } | Sort-Object | Get-Unique`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	output := new(bytes.Buffer)
	cmd.Stdout = output

	err := cmd.Run()

	if err != nil {
		panic(err)
	}

	var names []string
	for {
		l, err := output.ReadString('\n')
		if err != nil {
			break
		}
		addr := net.ParseIP(strings.TrimSpace(l))
		if !addr.IsLoopback() {
			names = append(names, addr.String())
		}
	}
	return names
}

func ReplaceDNS(ips []net.IP) {
	var names []string
	for _, i := range ips {
		names = append(names, i.String())
	}
	addresses := strings.Join(names, ",")
	script := fmt.Sprintf(
		`Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses %s }`,
		addresses)
	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stderr
	cmd.Stdout = os.Stdout

	if err := cmd.Run(); err != nil {
		log.Errorf("ERROR resetting DNS (%v)", err)
	}
}
