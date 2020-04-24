package windns

import (
	"bytes"
	"fmt"
	"net"
	"os"
	"os/exec"
	"strings"
)

func ResetDNS() {
	fmt.Println("Resetting DNS to original-ish state")

	script := `Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }`

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		fmt.Println("ERROR resetting DNS", err)
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
		fmt.Println("ERROR", err)
	}
}
