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

package cziti

import "C"
import (
	"encoding/binary"
	"fmt"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/api"
	"net"
	"strings"
	"sync"
)

var dnsip net.IP

type DnsManager interface {
	Resolve(dnsName string) net.IP
	ApplyDNS(dnsNameToReg string, ip string)
}

var initOnce = sync.Once{}
var dnsMgrPrivate = &dnsImpl{}
var DNSMgr DnsManager = dnsMgrPrivate

type dnsImpl struct {
	cidr    uint32
	ipCount uint32
	serviceMap map[string]*ctxService
	hostnameMap map[string]*ctxIp
	tun api.DesktopEdgeIface
}

func (dns *dnsImpl) ApplyDNS(dnsNameToReg string, ip string) {
	dnsName := normalizeDnsName(dnsNameToReg)
	log.Warnf("APPLY DNS: %s=%s", dnsName, ip)
	ipnet := net.ParseIP(ip)
	ctxIp := &ctxIp{
		ip:         ipnet,
		ctx:        nil,
		network:    "nolongerused",
		dnsEnabled: true,
		refCount:   1,
	}
	dns.hostnameMap[dnsName] = ctxIp
	log.Warnf("ADDED %s to resolver from source: %s", dnsName, dnsNameToReg)
}

type intercept struct {
	host string
	port uint16
	isIp bool
}
func (i intercept) String() string {
	return i.AsHostPort()
}
func (i intercept) AsHostPort() string {
	return fmt.Sprintf("%s:%d", i.AsDnsName(), i.port)
}
func (i intercept) AsDnsName() string {
	if i.isIp {
		return i.host
	} else {
		return normalizeDnsName(i.host)
	}
}

type ctxIp struct {
	ctx        *ZIdentity
	ip         net.IP
	network    string
	dnsEnabled bool
	refCount  int
}

type ctxService struct {
	ctx       *ZIdentity
	name      string
	serviceId string
	ctxIp     *ctxIp
	icept     intercept
}

func normalizeDnsName(dnsName string) string {

	dnsName = strings.TrimSpace(dnsName)
	if !strings.HasSuffix(dnsName, ".") {
		// append a period to the dnsName - forcing it to be a FQDN
		dnsName += "."
	}

	return strings.ToLower(dnsName) //normalize the casing
}

func (dns *dnsImpl) Resolve(toResolve string) net.IP {
	dnsName := normalizeDnsName(toResolve)
	found := dns.hostnameMap[dnsName]
	if found != nil {
		if found.dnsEnabled {
			return found.ip
		} else {
			log.Debugf("resolved %s as %v but service is not active", toResolve, found.ip)
		}
	}
	return nil
}

func DnsInit(tun api.DesktopEdgeIface, ip string, maskBits int) {
	initOnce.Do(func() {
		dnsMgrPrivate.serviceMap = make(map[string]*ctxService)
		//DNS.ipMap = make(map[uint32]string)
		dnsMgrPrivate.hostnameMap = make(map[string]*ctxIp)
		//register the test dns entry:
		dnsMgrPrivate.hostnameMap[normalizeDnsName("dew-dns-probe.openziti.org")] = &ctxIp{
			//ctx:        nil,
			ip:         net.ParseIP("127.0.0.1"),
			//network:    "",
			dnsEnabled: true,
			refCount:   0,
		}

		dnsip = net.ParseIP(ip).To4()
		mask := net.CIDRMask(maskBits, 32)
		dnsMgrPrivate.cidr = binary.BigEndian.Uint32(dnsip) & binary.BigEndian.Uint32(mask)
		dnsMgrPrivate.ipCount = 2
		dnsMgrPrivate.tun = tun
	})
}
