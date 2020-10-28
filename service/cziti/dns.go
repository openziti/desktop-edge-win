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
	"sync/atomic"
)

type DnsManager interface {
	Resolve(dnsName string) net.IP

	RegisterService(dnsName string, port uint16, ctx *CZitiCtx, name string) (net.IP, error)
	DeregisterService(ctx *CZitiCtx, name string)
	GetService(ip net.IP, port uint16) (*CZitiCtx, string, error)
}

var initOnce = sync.Once{}
var DNS = &dnsImpl{}

type dnsImpl struct {
	cidr    uint32
	ipCount uint32

	serviceMap map[string]ctxService

	// dnsName -> ip address
	hostnameMap map[string]ctxIp
	// ipv4 -> dnsName
	//ipMap map[uint32]string
	tun api.DesktopEdgeIface
}

type intercept struct {
	host string
	port uint16
	isIp bool
}
func (i intercept) String() string {
	return fmt.Sprintf("%s:%d", i.host, i.port)
}

type ctxIp struct {
	ctx     *CZitiCtx
	ip      net.IP
	network string
}

type ctxService struct {
	ctx       *CZitiCtx
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
func (dns *dnsImpl) RegisterService(svcId string, dnsNameToReg string, port uint16, ctx *CZitiCtx, svcName string) (net.IP, error) {
	//check to see if host is an ip address - if so we want to intercept the ip. otherwise treat host as a host
	//name and register it in dns, obtain an ip and all that...
	ip := net.ParseIP(dnsNameToReg)

	icept := intercept{isIp: false, port: port}
	if ip == nil {
		icept.host = normalizeDnsName(dnsNameToReg)
	} else {
		icept.host = ip.String()
		icept.isIp = true
	}
	key := icept.String()
	log.Infof("adding DNS for %s. service name %s@%s. is ip: %t", dnsNameToReg, svcName, key, icept.isIp)

	currentNetwork := "<unknown-network>"
	if ctx != nil {
		if ctx.Options != nil {
			if ctx.Options.controller != nil {
				currentNetwork = C.GoString(ctx.Options.controller)
			}
		}
	}

	// check to see if the hostname is mapped...
	if foundIp, found := dns.hostnameMap[icept.host]; found {
		ip = foundIp.ip
		// now check to see if the host *and* port are mapped...
		if foundContext, found := dns.serviceMap[key]; found {
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
		// if not used at all - map it
		if icept.isIp {
			err := dns.tun.AddRoute(
				net.IPNet{IP: ip, Mask: net.IPMask{255, 255, 255, 255}},
				net.IP{0,0,0,0},
				1)
			if err != nil {
				log.Errorf("Unexpected error adding a route to %s: %v", icept.host, err)
			} else {
				log.Infof("adding route for ip:%s", icept.host)
			}
		} else {
			nextAddr := dns.cidr | atomic.AddUint32(&dns.ipCount, 1)
			ip = make(net.IP, 4)
			binary.BigEndian.PutUint32(ip, nextAddr)

			log.Infof("mapping hostname %s to ip %s", dnsNameToReg, ip.String())
			dns.hostnameMap[icept.host] = ctxIp {
				ip: ip,
				ctx: ctx,
				network: currentNetwork,
			}
		}
	}

	dns.serviceMap[key] = ctxService{
		ctx:       ctx,
		name:      svcName,
		serviceId: svcId,
		count:     1,
		icept:	   icept,
	}

	return ip, nil
}

func (dns *dnsImpl) Resolve(toResolve string) net.IP {
	dnsName := normalizeDnsName(toResolve)
	return dns.hostnameMap[dnsName].ip
}

func (dns *dnsImpl) UnregisterService(ctx api.Connection, name string) {
	log.Debugf("UnregisterService named %s called for controller %s and identity: %s.", name, ctx.Controller(), ctx.Name())
	for key, sc := range dns.serviceMap {
		if sc.ctx == ctx && sc.name == name {
			sc.count --
			if sc.count < 1 {
				icept := sc.icept
				log.Infof("removing service named %s from DNS mapping known as %s", name, icept)
				if icept.isIp {
					err := dns.tun.RemoveRoute(net.IPNet{IP: net.ParseIP(icept.host)}, net.IP{0, 0, 0, 0})
					if err != nil {
						log.Warnf("Unexpected error removing route for %s", icept.host)
					}
				} else {
					delete(dns.hostnameMap, icept.host)
				}
				delete(dns.serviceMap, key)
			} else {
				// another service is using the mapping - can't remove it yet so decrement
				log.Debugf("cannot remove dns mapping for %s yet - %d other services still use this hostname", name, sc.count)
			}
			return //short return this is the context and the service name no need to continue
		} else {
			log.Debugf("service context %p does not match event context address %p", sc.ctx, ctx)
		}
	}
	log.Warnf("UnregisterService completed. Match was not found for context %p and name %s", ctx, name)
}

func (this *dnsImpl) GetService(ip net.IP, port uint16) (*CZitiCtx, string, error) {
	return nil, "", nil //not used yet
	/*ipv4 := binary.BigEndian.Uint32(ip)
	dns, found := this.ipMap[ipv4]
	if !found {
		return nil, "", errors.New("service not available")
	}

	key := fmt.Sprint(dns, ':', port)
	sc, found := this.serviceMap[key]
	if !found {
		return nil, "", errors.New("service not available")
	}
	return sc.ctx, sc.name, nil
	*/
}

func DnsInit(tun api.DesktopEdgeIface, ip string, maskBits int) {
	initOnce.Do(func() {
		DNS.serviceMap = make(map[string]ctxService)
		//DNS.ipMap = make(map[uint32]string)
		DNS.hostnameMap = make(map[string]ctxIp)
		i := net.ParseIP(ip).To4()
		mask := net.CIDRMask(maskBits, 32)
		DNS.cidr = binary.BigEndian.Uint32(i) & binary.BigEndian.Uint32(mask)
		DNS.ipCount = 2
		DNS.tun = tun
	})
}
