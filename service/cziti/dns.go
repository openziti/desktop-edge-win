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
	"os"
	"strings"
	"sync"
	"sync/atomic"
)

var dnsip net.IP

type DnsManager interface {
	Resolve(dnsName string) net.IP

	RegisterService(svcId string, dnsNameToReg string, port uint16, ctx *ZIdentity, svcName string) (net.IP, error)
	UnregisterService(host string, port uint16)
	ReturnToDns(hostname string) net.IP
}

var initOnce = sync.Once{}
var dnsMgrPrivate = &dnsImpl{}
var DNSMgr DnsManager = dnsMgrPrivate

type dnsImpl struct {
	cidr    uint32
	ipCount uint32
	serviceMap map[string]ctxService
	hostnameMap map[string]*ctxIp
	tun api.DesktopEdgeIface
}

type intercept struct {
	host string
	port uint16
	isIp bool
}
func (i intercept) String() string {
	if i.isIp {
		//an ip does not need the host normalized
		return fmt.Sprintf("%s:%d", i.host, i.port)
	} else {
		return i.AsHostPort()
	}
}
func (i intercept) AsHostPort() string {
	return fmt.Sprintf("%s:%d", normalizeDnsName(i.host), i.port)
}
func (i intercept) AsDnsName() string {
	return normalizeDnsName(i.host)
}

type ctxIp struct {
	ctx        *ZIdentity
	ip         net.IP
	network    string
	dnsEnabled bool
}

type ctxService struct {
	ctx       *ZIdentity
	name      string
	serviceId string
	count     int
	icept	  intercept
}

func normalizeDnsName(dnsName string) string {
	dnsName = strings.TrimSpace(dnsName)
	if !strings.HasSuffix(dnsName, ".") {
		// append a period to the dnsName - forcing it to be a FQDN
		dnsName += "."
	}

	return strings.ToLower(dnsName) //normalize the casing
}

// RegisterService will return the next ip address in the configured range. If the ip address is not
// assigned to a hostname an error will also be returned indicating why.
func (dns *dnsImpl) RegisterService(svcId string, dnsNameToReg string, port uint16, zid *ZIdentity, svcName string) (net.IP, error) {
	//check to see if host is an ip address - if so we want to intercept the ip. otherwise treat host as a host
	//name and register it in dns, obtain an ip and all that...
	ip := net.ParseIP(dnsNameToReg)

	icept := intercept{isIp: false, host:dnsNameToReg, port: port}
	if ip != nil {
		icept.host = ip.String()
		icept.isIp = true
	}
	log.Infof("adding DNS for %s. service name %s@%s. is ip: %t", dnsNameToReg, svcName, icept.String(), icept.isIp)

	currentNetwork := "<unknown-network>"
	if zid != nil {
		currentNetwork = zid.Controller()
	}

	key := icept.AsHostPort()
	// check to see if the hostname is mapped...
	if foundIp, found := dns.hostnameMap[icept.AsDnsName()]; found {
		foundIp.dnsEnabled = true
		ip = foundIp.ip
		// now check to see if the host *and* port are mapped...
		if foundContext, found := dns.serviceMap[key]; found {
			//no matter what happens this service needs a new IP address...
			nextAddr := dns.cidr | atomic.AddUint32(&dns.ipCount, 1)
			ip = make(net.IP, 4)
			binary.BigEndian.PutUint32(ip, nextAddr)

			if foundIp.network != currentNetwork {
				// means the host:port are mapped to some other *identity* already. that's an invalid state
				return ip, fmt.Errorf("service mapping conflict for service name %s. %s:%d in %s is already mapped by another identity in %s", svcName, dnsNameToReg, port, currentNetwork, foundIp.network)
			}
			if foundContext.serviceId != svcId {
				// means the host:port are mapped to some other service already. that's an invalid state
				return ip, fmt.Errorf("service mapping conflict for service name %s. %s:%d is already mapped by service id %s", svcName, dnsNameToReg, port, foundContext.serviceId)
			}
			// while the host *AND* port are not used - the hostname is.
			// need to increment the refcounter of how many service use this hostname
			foundContext.count ++
			log.Debugf("DNS mapping used by another service. total services using %s = %d", dnsNameToReg, foundContext.count)
		} else {
			// good - means the service can be mapped
		}
	} else {
		log.Debugf("first time seeing host: %s", icept.host)
		// if not used at all - map it
		if icept.isIp {
			log.Infof("adding route for ip:%s", icept.host)

			err := dns.tun.AddRoute(
				net.IPNet{IP: ip, Mask: net.IPMask{255, 255, 255, 255}},
				dnsip,
				1)
			if err != nil {
				if err == os.ErrExist {
					log.Debugf("route to %s already exists.", icept.host)
				} else {
					log.Errorf("Unexpected error adding a route to %s: %v", icept.host, err)
				}
			}
		} else {
			nextAddr := dns.cidr | atomic.AddUint32(&dns.ipCount, 1)
			ip = make(net.IP, 4)
			binary.BigEndian.PutUint32(ip, nextAddr)

			log.Infof("mapping hostname %s to ip %s as dns %s", dnsNameToReg, ip.String(), icept.AsDnsName())
			dns.hostnameMap[icept.AsDnsName()] = &ctxIp {
				ip:         ip,
				ctx:        zid,
				network:    currentNetwork,
				dnsEnabled: zid.Active,
			}
		}
	}

	dns.serviceMap[key] = ctxService{
		ctx:       zid,
		name:      svcName,
		serviceId: svcId,
		count:     1,
		icept:     icept,
	}

	return ip, nil
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

func (dns *dnsImpl) UnregisterService(host string, port uint16) {
	key := fmt.Sprintf("%s:%d", normalizeDnsName(host), port)
	log.Debugf("dns asked to unregister %s", key)

	//find the dns entry...
	if sc, found := dns.serviceMap[key]; found {
		sc.count--
		if sc.count < 1 {
			icept := sc.icept
			log.Infof("removing service named %s from DNS mapping known as %s", host, icept)
			if icept.isIp {
				err := dns.tun.RemoveRoute(net.IPNet{IP: net.ParseIP(icept.host)}, dnsip)
				if err != nil {
					log.Warnf("Unexpected error removing route for %s. %v", icept.host, err)
				}
			} else {
				found := dns.hostnameMap[normalizeDnsName(icept.host)]
				if found != nil {
					found.dnsEnabled = false
				} else {
					log.Warnf("could not disable hostname %s as it was not in the map", icept.host)
				}
			}
		} else {
			// another service is using the mapping - can't remove it yet so decrement
			log.Debugf("cannot remove dns mapping for %s yet - %d other services still use this hostname", host, sc.count)
		}
	} else {
		log.Warnf("key not found. could not remove dns entry for %s", key)
	}
}

func (dns *dnsImpl) GetService(ip net.IP, port uint16) (*ZIdentity, string, error) {
	return nil, "", nil //not used yet
}

func (dns *dnsImpl) ReturnToDns(hostname string) net.IP {
	dnsEntry := dns.hostnameMap[normalizeDnsName(hostname)]
	if dnsEntry != nil {
		dnsEntry.dnsEnabled = true
		return dnsEntry.ip
	} else {
		// must be an ip - pass as ip and try to return that
		ip := net.ParseIP(hostname)
		return ip
	}
}

func DnsInit(tun api.DesktopEdgeIface, ip string, maskBits int) {
	initOnce.Do(func() {
		dnsMgrPrivate.serviceMap = make(map[string]ctxService)
		//DNS.ipMap = make(map[uint32]string)
		dnsMgrPrivate.hostnameMap = make(map[string]*ctxIp)
		dnsip = net.ParseIP(ip).To4()
		mask := net.CIDRMask(maskBits, 32)
		dnsMgrPrivate.cidr = binary.BigEndian.Uint32(dnsip) & binary.BigEndian.Uint32(mask)
		dnsMgrPrivate.ipCount = 2
		dnsMgrPrivate.tun = tun
	})
}
