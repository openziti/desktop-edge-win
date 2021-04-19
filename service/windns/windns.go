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

const (
	MAX_POWERSHELL_SCRIPT_LEN = 7500 //represents how long the powershell script can be. as of apr 2021 the limit was 8k (8192). leaves a little room for the rest of the script
)

var log = logging.Logger()
var exeName = "ziti-tunnel"

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

func RemoveAllNrptRules() {
	script := fmt.Sprintf(`Get-DnsClientNrptRule | Where { $_.Comment.StartsWith("Added by %s") } | Remove-DnsClientNrptRule -ErrorAction SilentlyContinue -Force`, exeName)
	log.Tracef("removing all nrpt rules with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing all nrpt rules: %v", err)
	}
}

var namespaceTemplate = `%s@{n="%s";}`
var namespaceTemplatePadding = len(namespaceTemplate)

func AddNrptRules(domainsToMap map[string]bool, dnsServer string) {
	if len(domainsToMap) == 0 {
		log.Debug("no domains to map specified to AddNrptRules. exiting early")
		return
	}

	maxBucketSize := 500
	currentSize := 0
	hostnames := make([]string, maxBucketSize)
	ruleSize := 0
	for hostname := range domainsToMap {
		ruleSize = ruleSize + len(hostname) + namespaceTemplatePadding
		if ruleSize > MAX_POWERSHELL_SCRIPT_LEN || currentSize >= maxBucketSize {
			log.Debugf("sending chunk of domains to be added to NRPT")
			chunkedAddNrptRules(hostnames[:currentSize], dnsServer)
			hostnames = make([]string, maxBucketSize)
			currentSize = 0
			ruleSize = len(hostname) + namespaceTemplatePadding
		}
		hostnames[currentSize] = hostname
		currentSize++
	}
	if currentSize > 0 {
		//means there's a chunk still to add....
		chunkedAddNrptRules(hostnames[:currentSize], dnsServer)
	}
}

func chunkedAddNrptRules(domainsToAdd []string, dnsServer string) {
	sb := strings.Builder{}
	sb.WriteString(`$Namespaces = @(
`)

	for _, hostname := range domainsToAdd {
		sb.WriteString(fmt.Sprintf(namespaceTemplate, "\n", hostname))
	}

	sb.WriteString(fmt.Sprintf(`)

ForEach ($Namespace in $Namespaces) {
    $ns=$Namespace["n"]
    $Rule = @{Namespace="${ns}"; NameServers=@("%s"); Comment="Added by %s"; DisplayName="%s:${ns}"; }
    Add-DnsClientNrptRule @Rule
}`, dnsServer, exeName, exeName))

	script := sb.String()
	log.Tracef("Executing    ADD NRPT script containing %d domains. total script size: %d\n%s", len(domainsToAdd), len(script), script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR adding nrpt rules: %v", err)
	}
}

func RemoveNrptRules(domainsToRemove map[string]bool) {
	if len(domainsToRemove) == 0 {
		log.Debug("no domains to map specified to RemoveNrptRules. exiting early")
		return
	}

	maxBucketSize := 500
	currentSize := 0
	hostnames := make([]string, maxBucketSize)
	ruleSize := 0
	for hostname := range domainsToRemove {
		ruleSize = ruleSize + len(hostname) + namespaceTemplatePadding
		if ruleSize > MAX_POWERSHELL_SCRIPT_LEN || currentSize >= maxBucketSize {
			log.Debugf("sending chunk of domains to be added to NRPT")
			chunkedRemoveNrptRules(hostnames[:currentSize])
			hostnames = make([]string, maxBucketSize)
			currentSize = 0
			ruleSize = len(hostname) + namespaceTemplatePadding
		}
		hostnames[currentSize] = hostname
		currentSize++
	}

	if currentSize > 0 {
		//means there's a chunk still to add....
		chunkedRemoveNrptRules(hostnames[:currentSize])
	}
}

func chunkedRemoveNrptRules(domainsToRemove []string) {
	sb := strings.Builder{}
	sb.WriteString(`$toRemove = @(`)

	for _, hostname := range domainsToRemove {
		sb.WriteString(fmt.Sprintf(namespaceTemplate, "\n", hostname))
	}

	sb.WriteString(fmt.Sprintf(`)

ForEach ($ns in $toRemove){
  Get-DnsClientNrptRule | where Namespace -eq $ns["n"] | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue
}
`))

	script := sb.String()
	log.Tracef("Executing REMOVE NRPT script containing %d domains. total script size: %d\n%s", len(domainsToRemove), len(script), script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing nrpt rules: %v", err)
	}
}

func IsNrptPoliciesEffective() bool {
	script := fmt.Sprintf(`Add-DnsClientNrptRule -Namespace ".ziti.test" -NameServers "100.64.0.1" -Comment "Added by ziti-tunnel" -DisplayName "ziti-tunnel:.ziti.test"
	Get-DnsClientNrptPolicy -Effective | Select-Object Namespace -Unique | Where-Object Namespace -Eq ".ziti.test"`)
	log.Debugf("checking the nrpt policies with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	output := new(bytes.Buffer)
	cmd.Stdout = output

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR adding the test nrpt rules: %v", err)
		return false
	}

	policyFound := false
	for {
		l, err := output.ReadString('\n')
		policyOut := strings.Trim(l, "\r\n")
		if err != nil {
			break
		}

		if strings.Compare(policyOut, ".ziti.test") == 0 {
			policyFound = true
			log.Debug("The nrpt policies are effective in this client")
			break
		}
	}
	removeSingleNrtpRule(".ziti.test")

	return policyFound
}

func removeSingleNrtpRule(nrptRule string) {
	script := fmt.Sprintf(`Get-DnsClientNrptRule | where Namespace -eq "%s" | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue`, nrptRule)
	log.Debugf("Removing the nrpt rule with: %s", script)

	cmd := exec.Command("powershell", "-Command", script)
	cmd.Stderr = os.Stdout
	output := new(bytes.Buffer)
	cmd.Stdout = output

	err := cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing the nrpt rules: %v", err)
	}
}
