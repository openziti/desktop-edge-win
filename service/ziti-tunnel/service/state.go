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
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-tunnel-win/service/cziti"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/config"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/dto"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/idutil"
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
	if index, _ := t.Find(fingerprint); index < len(t.state.Identities) {
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

func (t *RuntimeState) RemoveByIdentity(id dto.Identity) {
	t.RemoveByFingerprint(id.FingerPrint)
}

func (t *RuntimeState) FindByIdentity(id dto.Identity) (int, *dto.Identity) {
	return t.Find(id.FingerPrint)
}

func SaveState(t *RuntimeState) {
	// overwrite file if it exists
	_ = os.MkdirAll(config.Path(), 0644)

	cfg, err := os.OpenFile(config.File(), os.O_RDWR|os.O_CREATE|os.O_TRUNC, 0644)
	if err != nil {
		panic(err)
	}
	w := bufio.NewWriter(bufio.NewWriter(cfg))
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	_ = enc.Encode(t.state)
	_ = w.Flush()

	err = cfg.Close()
	if err != nil {
		panic(err)
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
		Active:     t.state.Active,
		Duration:   uptime,
		Identities: make([]*dto.Identity, idCount),
		IpInfo:     t.state.IpInfo,
	}

	for i, id := range t.state.Identities {
		cid := idutil.Clean(*id)
		clean.Identities[i] = &cid
	}

 	return clean
}

func (t *RuntimeState) CreateTun(ipv4 string, ipv4mask int) error {
	if noZiti() {
		log.Warnf("NOZITI set to true. this should be only used for debugging")
		return nil
	}

	log.Infof("creating TUN device: %s", TunName)
	tunDevice, err := tun.CreateTUN(TunName, 64*1024)
	if err == nil {
		t.tun = &tunDevice
		tunName, err2 := tunDevice.Name()
		if err2 == nil {
			t.tunName = tunName
		}
	} else {
		return fmt.Errorf("error creating TUN device: (%v)", err)
	}

	if name, err := tunDevice.Name(); err == nil {
		log.Debugf("created TUN device [%s]", name)
	}

	nativeTunDevice := tunDevice.(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())

	if strings.TrimSpace(ipv4) == "" {
		log.Infof("ip not provided using default: %d", ipv4)
		ipv4 = Ipv4ip
	}
	if ipv4mask < 16 || ipv4mask > 24 {
		log.Warnf("provided mask is invalid: %d. using default value: %d", ipv4mask, Ipv4mask)
		ipv4mask = Ipv4mask
	}
	ip, ipnet, err := net.ParseCIDR(fmt.Sprintf("%s/%d", ipv4, ipv4mask))
	if err != nil {
		return fmt.Errorf("error parsing CIDR block: (%v)", err)
	}

	log.Infof("setting TUN interface address to [%s]", ip)
	err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
	if err != nil {
		return fmt.Errorf("failed to set IP address: (%v)", err)
	}

	dnsServers := []net.IP{
		net.ParseIP(Ipv4dns).To4(),
		net.ParseIP(Ipv6dns),
	}
	err = luid.AddDNS(dnsServers)
	if err != nil {
		return fmt.Errorf("failed to add DNS address: (%v)", err)
	}
	dns, err := luid.DNS()
	if err != nil {
		return fmt.Errorf("failed to fetch DNS address: (%v)", err)
	}
	log.Debugf("dns servers set to = %s", dns)

	log.Infof("routing destination [%s] through [%s]", *ipnet, ipnet.IP)
	err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
	if err != nil {
		return err
	}
	log.Info("routing applied")

	cziti.DnsInit(ipv4, ipv4mask)
	cziti.Start()
	_, err = cziti.HookupTun(tunDevice, dns)
	if err != nil {
		panic(err)
	}
	return nil
}

func (t *RuntimeState) LoadIdentity(id *dto.Identity) {
	if !noZiti() {
		if id.Connected {
			log.Warnf("id [%s] already connected", id.FingerPrint)
			return
		}
		log.Infof("loading identity %s with fingerprint %s", id.Name, id.FingerPrint)
		ctx := cziti.LoadZiti(id.Path())
		id.NFContext = ctx

		id.Connected = true
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
		}

		id.Services = make([]*dto.Service, 0)
	} else {
		log.Warnf("NOZITI set to true. this should be only used for debugging")
	}

	events.broadcast <- dto.IdentityEvent{
		ActionEvent: IDENTITY_ADDED,
		Id: dto.Identity{
			Name:        id.Name,
			FingerPrint: id.FingerPrint,
			Active:      id.Active,
			Config:      idcfg.Config{},
			Status:      "enrolled",
			Services:    id.Services,
		},
	}
}

func noZiti() bool {
	v, _ := strconv.ParseBool(os.Getenv("NOZITI"))
	return v
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
		log.Warnf("unexpected error reading config file. %v", err)
		t.state = &dto.TunnelStatus{}
		return
	}

	err = file.Close()
	if err != nil {
		log.Errorf("could not close configuration file. this is not normal! %v", err)
	}
}