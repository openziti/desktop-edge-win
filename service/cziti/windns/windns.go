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
	"github.com/michaelquigley/pfxlog"
	"net"
	"os"
	"os/exec"
	"strings"
)

var log = pfxlog.Logger()

func ResetDNS() {
	log.Infof("skipping ResetDNS for now")

	log.Infof("resetting dns...")
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

	/*
	ifaces, err := net.Interfaces()
	if err != nil {
		fmt.Print(fmt.Errorf("localAddresses: %+v\n", err.Error()))
		return
	}
	for _, i := range ifaces {
		addrs, err := i.Addrs()
		if err != nil {
			fmt.Print(fmt.Errorf("localAddresses: %+v\n", err.Error()))
			continue
		}
		for _, a := range addrs {
			switch v := a.(type) {
			case *net.IPAddr:
				fmt.Printf("%v : %s (%s)\n", i.Name, v, v.IP.DefaultMask())

			case *net.IPNet:
				fmt.Printf("%v : %s [%v/%v]\n", i.Name, v, v.IP, v.Mask)
			}

		}
	}

	var names []string
	for _, i := range ips {
		names = append(names, i.String())
	}
	addresses := strings.Join(names, ",")

	*/
	/*
	script := fmt.Sprintf(
		`Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses %s }`,
		addresses)
	*/



	script := `$dnsinfo=Get-DnsClientServerAddress

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
        if(($dnsServers -ne $null) -and ($dnsServers.Contains("::1")) ) {
            echo ($dns.InterfaceAlias + " IPv6 already contains ::1")
        } else {
            $ArrList.Insert(0,"::1")
        }
        $dnsUpdates[$dns.InterfaceIndex].AddRange($ArrList)
    }
    elseif($dns.AddressFamily -eq $IPv4){
        $dnsServers=$dns.ServerAddresses
        $ArrList=[System.Collections.ArrayList]@($dnsServers)
        if(($dnsServers -ne $null) -and ($dnsServers.Contains("127.0.0.1")) ) {
            echo ($dns.InterfaceAlias + " IPv4 already contains 127.0.0.1")
        } else {
            $ArrList.Insert(0,"127.0.0.1")
        }
        $dnsUpdates[$dns.InterfaceIndex].AddRange($ArrList)
    }
}

foreach ($key in $dnsUpdates.Keys)
{
    $dnsServers=$dnsUpdates[$key]
    Set-DnsClientServerAddress -InterfaceIndex $key -ServerAddresses ($dnsServers)
}`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stderr
	cmd.Stdout = os.Stdout

	if err := cmd.Run(); err != nil {
		log.Errorf("ERROR resetting DNS (%v)", err)
	}
}
