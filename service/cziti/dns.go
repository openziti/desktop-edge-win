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

const defaultCidr = "169.254.0.0"
const defaultMaskBits = 16

var initOnce = sync.Once{}
var DNS = &dnsImpl{}

type dnsImpl struct {
	cidr    uint32
	counter uint32

	serviceMap map[string]ctxService

	// dnsName -> ip address
	hostnameMap map[string]ctxIp
	// ipv4 -> dnsName
	//ipMap map[uint32]string
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
}

func normalizeDnsName(dnsName string) string {
	if strings.HasSuffix(dnsName, ".") {
		return dnsName
	} else {
		// append a period to the dnsName - forcing it to be a FQDN
		return dnsName + "."
	}
}

// RegisterService will return the next ip address in the configured range. If the ip address is not
// assigned to a hostname an error will also be returned indicating why.
func (dns *dnsImpl) RegisterService(svcId string, dnsNameToReg string, port uint16, ctx *CZitiCtx, name string) (net.IP, error) {
	log.Infof("adding DNS entry for service name %s@%s:%d", name, dnsNameToReg, port)
	DnsInit(defaultCidr, defaultMaskBits)
	dnsName := normalizeDnsName(dnsNameToReg)
	key := fmt.Sprint(dnsName, ':', port)

	var ip net.IP

	currentNetwork := C.GoString(ctx.options.controller)

	// check to see if the hostname is mapped...
	if foundIp, found := dns.hostnameMap[dnsName]; found {
		ip = foundIp.ip
		// now check to see if the host *and* port are mapped...
		if foundContext, found := dns.serviceMap[key]; found {
			if foundIp.network != currentNetwork {
				// means the host:port are mapped to some other *identity* already. that's an invalid state
				return ip, fmt.Errorf("service mapping conflict: %s:%d in %s is already mapped for another identity in %s", dnsNameToReg, port, currentNetwork, foundIp.network)
			}
			if foundContext.serviceId != svcId {
				// means the host:port are mapped to some other service already. that's an invalid state
				return ip, fmt.Errorf("service mapping conflict: %s:%d is already mapped for another service", dnsNameToReg, port)
			}
			// while the host *AND* port are not used - the hostname is.
			// need to increment the refcounter of how many service use this hostname
			foundContext.count ++
			log.Debugf("DNS mapping for %s used by another service. total services using %s = %d", dnsNameToReg, dnsNameToReg, foundContext.count)
		} else {
			// good - means the service can be mapped
			dns.serviceMap[key] = ctxService{
				ctx:  ctx,
				name: name,
				serviceId: svcId,
				count: 1,
			}
		}
	} else {
		// if not used at all - map it
		nextAddr := dns.cidr | atomic.AddUint32(&dns.counter, 1)
		ip = make(net.IP, 4)
		binary.BigEndian.PutUint32(ip, nextAddr)

		log.Infof("mapping hostname %s to ip %s", dnsNameToReg, ip.String())
		dns.hostnameMap[dnsName] = ctxIp {
			ip: ip,
			ctx: ctx,
			network: currentNetwork,
		}
		dns.serviceMap[key] = ctxService{
			ctx:  ctx,
			name: name,
			serviceId: svcId,
			count: 1,
		}
	}

	return ip, nil
}

func (dns *dnsImpl) Resolve(toResolve string) net.IP {
	dnsName := normalizeDnsName(toResolve)
	return dns.hostnameMap[dnsName].ip
}

func (dns *dnsImpl) DeregisterService(ctx *CZitiCtx, name string) {
	for k, sc := range dns.serviceMap {
		if sc.ctx == ctx && sc.name == name {
			if sc.count < 1 {
				log.Infof("removing %s from DNS mapping", name)
				delete(dns.serviceMap, k)
			} else {
				// another service is using the mapping - can't remove it yet so decrement
				sc.count --
				log.Debugf("cannot remove dns mapping for %s yet - %d other services still use this hostname", name, sc.count)
			}
			return
		}
	}
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

func DnsInit(ip string, maskBits int) {
	initOnce.Do(func() {
		DNS.serviceMap = make(map[string]ctxService)
		//DNS.ipMap = make(map[uint32]string)
		DNS.hostnameMap = make(map[string]ctxIp)
		i := net.ParseIP(ip).To4()
		mask := net.CIDRMask(maskBits, 32)
		DNS.cidr = binary.BigEndian.Uint32(i) & binary.BigEndian.Uint32(mask)
		DNS.counter = 2
	})
}
