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

package service

import (
	"bufio"
	"encoding/json"
	"fmt"
	"github.com/openziti/desktop-edge-win/service/cziti"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/config"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/constants"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/idutil"
	"golang.org/x/sys/windows/registry"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
	"net"
	"os"
	"strconv"
	"strings"
	"time"
)

type RuntimeState struct {
	state   *dto.TunnelStatus
	tun     *tun.Device
	tunName string
}

func (t *RuntimeState) RemoveByFingerprint(fingerprint string) {
	log.Debugf("removing fingerprint: %s", fingerprint)
	if index, removed := t.Find(fingerprint); index < len(t.state.Identities) {
		removed.ZitiContext.Shutdown()
		t.state.Identities = append(t.state.Identities[:index], t.state.Identities[index+1:]...)
	}
}

func (t *RuntimeState) Find(fingerprint string) (int, *dto.Identity) {
	for i, n := range t.state.Identities {
		if n.FingerPrint == fingerprint {
			return i, n
		}
	}
	return len(t.state.Identities), nil
}

func (t *RuntimeState) SaveState() {
	// overwrite file if it exists
	_ = os.MkdirAll(config.Path(), 0644)

	cfg, err := os.OpenFile(config.File(), os.O_RDWR|os.O_CREATE|os.O_TRUNC, 0644)
	if err != nil {
		log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "opening the config file", err)
	}
	w := bufio.NewWriter(bufio.NewWriter(cfg))
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	_ = enc.Encode(t.ToStatus())
	_ = w.Flush()

	err = cfg.Close()
	if err != nil {
		log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "closing the config file", err)
	}
	log.Debug("state saved")
}

func (t *RuntimeState) ToStatus() dto.TunnelStatus {
	var uptime int64

	now := time.Now()
	tunStart := now.Sub(TunStarted)
	uptime = tunStart.Milliseconds()

	var idCount int
	if t.state != nil && t.state.Identities != nil {
		idCount = len(t.state.Identities)
	}

	clean := dto.TunnelStatus{
		Active:         t.state.Active,
		Duration:       uptime,
		Identities:     make([]*dto.Identity, idCount),
		IpInfo:         t.state.IpInfo,
		LogLevel:       t.state.LogLevel,
		ServiceVersion: Version,
		TunIpv4:        t.state.TunIpv4,
		TunIpv4Mask:    t.state.TunIpv4Mask,
	}

	for i, id := range t.state.Identities {
		cid := idutil.Clean(*id)
		clean.Identities[i] = &cid
	}

 	return clean
}

func (t *RuntimeState) CreateTun(ipv4 string, ipv4mask int) (net.IP, error) {
	if noZiti() {
		log.Warnf("NOZITI set to true. this should be only used for debugging")
		return nil, nil
	}

	log.Infof("creating TUN device: %s", TunName)
	tunDevice, err := tun.CreateTUN(TunName, 64*1024 - 1)
	if err == nil {
		t.tun = &tunDevice
		tunName, err2 := tunDevice.Name()
		if err2 == nil {
			t.tunName = tunName
		}
	} else {
		return nil, fmt.Errorf("error creating TUN device: (%v)", err)
	}

	if name, err := tunDevice.Name(); err == nil {
		log.Debugf("created TUN device [%s]", name)
	} else {
		return nil, fmt.Errorf("error getting TUN name: (%v)", err)
	}

	nativeTunDevice := tunDevice.(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())

	if strings.TrimSpace(ipv4) == "" {
		log.Infof("ip not provided using default: %v", ipv4)
		ipv4 = constants.Ipv4ip
	}
	if ipv4mask < constants.Ipv4MaxMask {
		log.Warnf("provided mask is very large: %d.")
	}
	ip, ipnet, err := net.ParseCIDR(fmt.Sprintf("%s/%d", ipv4, ipv4mask))
	if err != nil {
		return nil, fmt.Errorf("error parsing CIDR block: (%v)", err)
	}

	log.Infof("setting TUN interface address to [%s]", ip)
	err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
        //err = luid.SetIPAddresses([]net.IPNet{{IP:ip, Mask: []byte{255,255,255,0}}})
	//err = luid.SetIPAddresses([]net.IPNet{{IP:ip, Mask: []byte{255,255,255,255}}})	
	if err != nil {
		return nil, fmt.Errorf("failed to set IP address to %v: (%v)", ip, err)
	}

	//dnsip := net.ParseIP("100.64.0.1")
	//dnsip := ip //net.ParseIP("127.21.71.53")
	//ipnet2 := net.IPNet{IP: dnsip, Mask: []byte{255,255,255,255}}
	//log.Infof("meh: %v", ipnet2)
	//err = luid.SetIPAddresses([]net.IPNet{ *ipnet })
	dnsServers := []net.IP{ ip }

	log.Infof("adding DNS servers to TUN: %s", dnsServers)
	err = luid.AddDNS(dnsServers)
	if err != nil {
		return nil, fmt.Errorf("failed to add DNS addresses: (%v)", err)
	}
	log.Infof("checking TUN dns servers")
	dns, err := luid.DNS()
	if err != nil {
		return nil, fmt.Errorf("failed to fetch DNS address: (%v)", err)
	}
	log.Infof("TUN dns servers set to: %s", dns)

	log.Infof("routing destination [%s] through [%s]", *ipnet, ipnet.IP)
	err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
	if err != nil {
		return nil, fmt.Errorf("failed to SetRoutes: (%v)", err)
	}
	log.Info("routing applied")

	cziti.DnsInit(&rts, ipv4, ipv4mask)
	cziti.Start()
	err = cziti.HookupTun(tunDevice)
	if err != nil {
		log.Panicf("An unrecoverable error has occurred! %v", err)
	}

	return ip, nil
}

func (t *RuntimeState) LoadIdentity(id *dto.Identity) {
	if !noZiti() {
		if id.Connected {
			log.Warnf("id [%s] already connected", id.FingerPrint)
			return
		}
		log.Infof("loading identity %s with fingerprint %s", id.Name, id.FingerPrint)
		ctx := cziti.LoadZiti(id.Path())
		id.ZitiContext = ctx

		id.Connected = true
		id.Active = true
		if ctx == nil {
			log.Warnf("connecting to identity with fingerprint [%s] did not error but no context was returned", id.FingerPrint)
			return
		}
		log.Infof("successfully loaded %s@%s", ctx.Name(), ctx.Controller())

		// hack for now - if the identity name is '<unknown>' don't set it... :(
		if ctx.Name() == "<unknown>" {
			log.Debugf("name is set to <unknown> which probably indicates the controller is down - not changing the name")
		} else {
			log.Debugf("name changed from %s to %s", id.Name, ctx.Name())
			id.Name = ctx.Name()
			id.Tags = ctx.Tags()
		}

		id.Services = make([]*dto.Service, 0)
	} else {
		log.Warnf("NOZITI set to true. this should be only used for debugging")
	}
}

func noZiti() bool {
	v, _ := strconv.ParseBool(os.Getenv("NOZITI"))
	return v
}

func (t *RuntimeState) LoadConfig() {
	log.Infof("reading config file located at: %s", config.File())
	file, err := os.OpenFile(config.File(), os.O_RDONLY, 0644)
	if err != nil {
		t.state = &dto.TunnelStatus{}
		return
	}

	r := bufio.NewReader(file)
	dec := json.NewDecoder(r)

	err = dec.Decode(&rts.state)
	if err != nil {
		log.Panicf("unexpected error reading config file. %v", err)
	}

	err = file.Close()
	if err != nil {
		log.Errorf("could not close configuration file. this is not normal! %v", err)
	}
}

func (t *RuntimeState) UpdateIpv4Mask(ipv4mask int){
	rts.state.TunIpv4Mask = ipv4mask
	rts.SaveState()
}
func (t *RuntimeState) UpdateIpv4(ipv4 string){
	rts.state.TunIpv4 = ipv4
	rts.SaveState()
}

// uses the registry to determine if IPv6 is enabled or disabled on this machine. If it is disabled an IPv6 DNS entry
// will end up causing a fatal error on startup of the service. For this registry key and values see the MS documentation
// at https://docs.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows
func iPv6Disabled() bool {
	k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`, registry.QUERY_VALUE)
	if err != nil {
		log.Warnf("could not read registry to detect IPv6 - assuming IPv6 enabled. If IPv6 is not enabled the service may fail to start")
		return false
	}
	defer k.Close()

	val, _, err := k.GetIntegerValue("DisabledComponents")
	if err != nil {
		log.Debugf("registry key HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters\\DisabledComponents not present. IPv6 is enabled")
		return false
	}
	actual := val & 255
	log.Debugf("read value from registry: %d. using actual: %d", val, actual)
	if actual == 255 {
		return true
	} else {
		log.Infof("IPv6 has DisabledComponents set to %d. If the service fails to start please report this message", val)
		return false
	}
}

func (r *RuntimeState) AddRoute(destination net.IPNet, nextHop net.IP, metric uint32) error {
	nativeTunDevice := (*r.tun).(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())
	return luid.AddRoute(destination, nextHop, metric)
}

func (r *RuntimeState) RemoveRoute(destination net.IPNet, nextHop net.IP) error {
	nativeTunDevice := (*r.tun).(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())
	return luid.DeleteRoute(destination, nextHop)
}


func (t *RuntimeState) Close() {
	if t.tun != nil {
		tu := *t.tun
		err := tu.Close()
		if err != nil {
			log.Fatalf("problem closing tunnel!")
		}
	} else {
		log.Warn("unexpected situation. the TUN was null? ")
	}
}


func (t *RuntimeState) InterceptDNS() {
	log.Panicf("implement me")
}

func (t *RuntimeState) ReleaseDNS() {
	log.Panicf("implement me")
}

func (t *RuntimeState) InterceptIP() {
	log.Panicf("implement me")
}

func (t *RuntimeState) ReleaseIP() {
	log.Panicf("implement me")
}
