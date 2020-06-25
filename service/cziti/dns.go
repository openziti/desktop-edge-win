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

import (
	"encoding/binary"
	"errors"
	"fmt"
	"net"
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
	hostnameMap map[string]net.IP
	// ipv4 -> dnsName
	ipMap map[uint32]string
}

type ctxService struct {
	ctx  *CZitiCtx
	name string
}

func (dns *dnsImpl) RegisterService(dnsName string, port uint16, ctx *CZitiCtx, name string) (net.IP, error) {
	log.Infof("adding DNS entry for service name %s@%s:%d", name, dnsName, port)
	DnsInit(defaultCidr, defaultMaskBits)
	dnsName = dnsName + "."
	key := fmt.Sprint(dnsName, ':', port)
	if cs, found := dns.serviceMap[key]; found {
		if cs.ctx != ctx || cs.name != name {
			return nil, fmt.Errorf(
				"service mapping conflict: %s:%d is already mapped for another context", dnsName, port)
		}
		return dns.hostnameMap[dnsName], nil
	}

	dns.serviceMap[key] = ctxService{
		ctx:  ctx,
		name: name,
	}

	if ip, mapped := dns.hostnameMap[dnsName]; mapped {
		return ip, nil
	}

	nextAddr := dns.cidr | atomic.AddUint32(&dns.counter, 1)
	nextIp := make(net.IP, 4)
	binary.BigEndian.PutUint32(nextIp, nextAddr)
	dns.hostnameMap[dnsName] = nextIp
	dns.ipMap[nextAddr] = dnsName

	return nextIp, nil
}

func (dns *dnsImpl) Resolve(dnsName string) net.IP {
	return dns.hostnameMap[dnsName]
}

func (dns *dnsImpl) DeregisterService(ctx *CZitiCtx, name string) {
	for k, sc := range dns.serviceMap {
		if sc.ctx == ctx && sc.name == name {
			log.Infof("removing %s from DNS mapping", name)
			delete(dns.serviceMap, k)
			return
		}
	}
}

func (this *dnsImpl) GetService(ip net.IP, port uint16) (*CZitiCtx, string, error) {
	ipv4 := binary.BigEndian.Uint32(ip)
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
}

func DnsInit(ip string, maskBits int) {
	initOnce.Do(func() {
		DNS.serviceMap = make(map[string]ctxService)
		DNS.ipMap = make(map[uint32]string)
		DNS.hostnameMap = make(map[string]net.IP)
		i := net.ParseIP(ip).To4()
		mask := net.CIDRMask(maskBits, 32)
		DNS.cidr = binary.BigEndian.Uint32(i) & binary.BigEndian.Uint32(mask)
		DNS.counter = 2
	})
}
