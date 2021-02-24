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
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/logging"
	"net"
	"os"
	"os/exec"
	"strings"
)

var log = logging.Logger()

func ResetDNS() {
	/*
	log.Info("resetting DNS server addresses")
	script := `Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR resetting DNS: %v", err)
	}
	*/
	log.Warnf("SKIPPING DNS RESET")
}

func GetConnectionSpecificDomains() []string {
	script := `Get-DnsClient | Select-Object ConnectionSpecificSuffix -Unique | ForEach-Object { $_.ConnectionSpecificSuffix }; (Get-DnsClientGlobalSetting).SuffixSearchList`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	output := new(bytes.Buffer)
	cmd.Stdout = output

	log.Tracef("running powershell command to get ConnectionSpecificSuffixes: %s", script)
	err := cmd.Run()

	if err != nil {
		log.Panicf("An unexpected and unrecoverable error has occurred while running the command: %s %v", script, err)
	}

	var names []string
	for {
		domain, err := output.ReadString('\n')
		if err != nil {
			break
		}
		domain = strings.TrimSpace(domain)
		if "" != domain {
			if !strings.HasSuffix(domain, ".") {
				names = append(names, domain+".")
			}
		}
	}
	return names
}

func GetUpstreamDNS() []string {
	script := `Get-DnsClientServerAddress | ForEach-Object { $_.ServerAddresses } | Sort-Object | Get-Unique`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	output := new(bytes.Buffer)
	cmd.Stdout = output

	err := cmd.Run()

	if err != nil {
		log.Panicf("An unexpected and unrecoverable error has occurred while running the command: %s %v", script, err)
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
	/*
	ipsStrArr := make([]string, len(ips))
	for i, ip := range ips {
		ipsStrArr[i] = fmt.Sprintf("'%s'", ip.String())
	}
	ipsAsString := strings.Join(ipsStrArr, ",")

	log.Infof("injecting DNS servers [%s] onto interfaces", ipsAsString)

	script := fmt.Sprintf(`$dnsinfo=Get-DnsClientServerAddress
$dnsIps=@(%s)

# see https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.addressfamily
$IPv4=2
$IPv6=23

$dnsUpdates = @{}

foreach ($dns in $dnsinfo)
{
    if($dnsUpdates[$dns.InterfaceIndex] -eq $null) { $dnsUpdates[$dns.InterfaceIndex]=[System.Collections.ArrayList]@() }
    if($dns.AddressFamily -eq $IPv6) {
        $dnsServers=$dns.ServerAddresses
        $ArrList=[System.Collections.ArrayList]@($dnsServers)
        $dnsUpdates[$dns.InterfaceIndex].AddRange($ArrList)
    }
    elseif($dns.AddressFamily -eq $IPv4){
        $dnsServers=$dns.ServerAddresses
        $ArrList=[System.Collections.ArrayList]@($dnsServers)
        foreach($d in $dnsIps) {
            if(($dnsServers -ne $null) -and ($dnsServers.Contains($d)) ) {
                # uncomment when debugging echo ($dns.InterfaceAlias + " IPv4 already contains $d")
            } else {
                $ArrList.Insert(0,$d)
            }
        }
        $dnsUpdates[$dns.InterfaceIndex].AddRange($ArrList)
    }
}

foreach ($key in $dnsUpdates.Keys)
{
    Set-DnsClientServerAddress -InterfaceIndex $key -ServerAddresses ($dnsUpdates[$key])
}`, ipsAsString)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stderr
	cmd.Stdout = os.Stdout

	if err := cmd.Run(); err != nil {
		log.Errorf("ERROR resetting DNS (%v)", err)
	}
	*/
	log.Warnf("SKIPPING DNS INJECT")
}

func FlushDNS() {
	log.Info("flushing DNS cache using ipconfig /flushdns")
	script := `ipconfig /flushdns`

	cmd := exec.Command("cmd", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR flushing DNS: %v", err)
	}
}

func AddNrptRule(hostname string, server string) {
	RemoveNrptRule(hostname)
	script := fmt.Sprintf(`Add-DnsClientNrptRule -Namespace "%s" -NameServers "%s" -Comment "Added by ziti-tunnel" -DisplayName "ziti-tunnel:%s"`, hostname, server, hostname)
	log.Debugf("adding nrpt rule with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR adding nrpt rule: %v", err)
	}
}

func RemoveAllNrptRules() {
	script := fmt.Sprintf(`Get-DnsClientNrptRule | Where { $_.Comment.StartsWith("Added by ziti-tunnel") } | Remove-DnsClientNrptRule -ErrorAction SilentlyContinue -Force`)
	log.Debugf("removing all nrpt rules with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing all nrpt rules: %v", err)
	}
}

func RemoveNrptRule(hostname string) {
	script := fmt.Sprintf(`Get-DnsClientNrptRule | Where { $_.Namespace -eq '%s' } | Remove-DnsClientNrptRule -ErrorAction SilentlyContinue`, hostname)
	log.Debugf("removing nrpt rule with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing nrpt rule: %v", err)
	}
}