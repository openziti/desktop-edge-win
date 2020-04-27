package runtime

import (
	"bufio"
	"encoding/json"
	"fmt"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
	"net"
	"os"
	"time"
	"wintun-testing/cziti"
	"wintun-testing/winio/config"
	"wintun-testing/winio/dto"
	"wintun-testing/winio/idutil"
)

type TunnelerState struct {
	Active     bool
	Duration   int64
	Identities []*dto.Identity
	IpInfo     *TunIpInfo `json:"IpInfo,omitempty"`

	tun     tun.Device
	tunName string
}

type TunIpInfo struct {
	Ip     string
	Subnet string
	MTU    uint16
	DNS    string
}

func (t *TunnelerState) RemoveByFingerprint(fingerprint string) {
	log.Debugf("removing fingerprint: %s", fingerprint)
	if index, _ := t.Find(fingerprint); index < len(t.Identities) {
		t.Identities = append(t.Identities[:index], t.Identities[index+1:]...)
	}
}

func (t *TunnelerState) Find(fingerprint string) (int, *dto.Identity) {
	for i, n := range t.Identities {
		if n.FingerPrint == fingerprint {
			return i, n
		}
	}
	return len(t.Identities), nil
}

func (t *TunnelerState) RemoveByIdentity(id dto.Identity) {
	t.RemoveByFingerprint(id.FingerPrint)
}

func (t *TunnelerState) FindByIdentity(id dto.Identity) (int, *dto.Identity) {
	return t.Find(id.FingerPrint)
}

func SaveState(s *TunnelerState) {
	// overwrite file if it exists
	_ = os.MkdirAll(config.Path(), 0640)

	cfg, err := os.OpenFile(config.File(), os.O_RDWR|os.O_CREATE|os.O_TRUNC, 0640)
	if err != nil {
		panic(err)
	}
	w := bufio.NewWriter(bufio.NewWriter(cfg))
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	s.IpInfo = nil
	_ = enc.Encode(s)
	_ = w.Flush()

	err = cfg.Close()
	if err != nil {
		panic(err)
	}
}

func (t TunnelerState) Clean() TunnelerState {
	var d int64
	if t.Active {
		now := time.Now()
		dd := now.Sub(TunStarted)
		d = dd.Milliseconds()
	} else {
		d = 0
	}

	rtn := TunnelerState{
		Active:     t.Active,
		Duration:   d,
		Identities: make([]*dto.Identity, len(t.Identities)),
		IpInfo:     t.IpInfo,
	}
	for i, id := range t.Identities {
		rtn.Identities[i] = idutil.Clean(*id)
	}

	return rtn
}

func (t *TunnelerState) CreateTun() {
	log.Infof("creating TUN device: %s", TunName)
	tunDevice, err := tun.CreateTUN(TunName, 64*1024)
	if err == nil {
		t.tun = tunDevice
		tunName, err2 := tunDevice.Name()
		if err2 == nil {
			t.tunName = tunName
		}
	} else {
		log.Fatalf("error creating TUN device: (%v)", err)
	}

	if name, err := tunDevice.Name(); err == nil {
		fmt.Printf("created TUN device [%s]\n", name)
	}

	nativeTunDevice := tunDevice.(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())
	ip, ipnet, err := net.ParseCIDR(fmt.Sprintf("%s/%d", ipv4ip, ipv4mask))
	if err != nil {
		log.Fatal(err)
	}
	fmt.Printf("setting TUN interface address to [%s]\n", ip)
	err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
	if err != nil {
		log.Fatal(err)
	}

	dnsServers := []net.IP{
		net.ParseIP(ipv4dns).To4(),
		net.ParseIP(ipv6dns),
	}
	err = luid.AddDNS(dnsServers)
	if err != nil {
		log.Fatal(err)
	}
	dns, err := luid.DNS()
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println("dns servers = ", dns)

	fmt.Printf("routing destination [%s] through [%s]\n", *ipnet, ipnet.IP)
	err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println("routing applied")

	fmt.Println("running")
	cziti.DnsInit(ipv4ip, 24)

	cziti.Start()
	_, err = cziti.HookupTun(tunDevice, dns)
	if err != nil {
		panic(err)
	}

}

func (t *TunnelerState) Close() {
	cziti.Stop()
	if t.tun != nil {
		err := t.tun.Close()
		if err != nil {
			log.Fatalf("problem closing tunnel!")
		}
	}
}
