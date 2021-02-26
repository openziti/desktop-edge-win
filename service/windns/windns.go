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
	"github.com/sirupsen/logrus"
	"io/ioutil"
	"net"
	"os"
	"os/exec"
	"strings"
)

var log = logging.Logger()

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

func AddNrptRules(domainsToMap map[string]struct{}, dnsServer string) {
	tmpFile, err := ioutil.TempFile(os.TempDir(), "ziti-tunnel-nrpt-rules-*.ps1")
	if err != nil {
		log.Errorf("Failed to write to temporary file, %v", err)
	}
	defer os.Remove(tmpFile.Name())

	log.Debugf("Created NRPT File at: " + tmpFile.Name())

	text := []byte(fmt.Sprintf(`$Rules = @(
`))
	if _, err = tmpFile.Write(text); err != nil {
		log.Errorf("Failed to write to temporary file: %v", err)
		return
	}

	for hostname := range domainsToMap {
		text := []byte(fmt.Sprintf(`@{ Namespace ="%s"; NameServers = @("%s"); Comment = "Added by ziti-tunnel"; DisplayName = "ziti-tunnel:%s"; }%s`, hostname, dnsServer, hostname, "\n"))
		if _, err = tmpFile.Write(text); err != nil {
			log.Errorf("Failed to write to temporary file, %v", err)
		}
	}

	text = []byte(fmt.Sprintf(`)

ForEach ($Rule in $Rules) {
	Add-DnsClientNrptRule @Rule
}`))

	if _, err = tmpFile.Write(text); err != nil {
		log.Errorf("Failed to write to temporary file: %v", err)
		return
	}

	// Close the file
	if err := tmpFile.Close(); err != nil {
		log.Errorf("Failed to close the temp file? %v", err)
	}

	log.Debugf("removing nrpt rule with script at: %s", tmpFile.Name())

	cmd := exec.Command("powershell", "-File", tmpFile.Name())
	cmd.Stderr = os.Stdout
	cmd.Stdout = os.Stdout

	err = cmd.Run()
	if err != nil {
		log.Errorf("ERROR removing nrpt rule: %v", err)
	}

	if log.Level >= logrus.DebugLevel {
		scriptBody, terr := ioutil.ReadFile(tmpFile.Name())
		if terr != nil {
			log.Warnf("could not read file at %s: %v", tmpFile.Name(), err)
		}
		log.Debugf("Executing NRPT script:\n%s", scriptBody)
	}
}