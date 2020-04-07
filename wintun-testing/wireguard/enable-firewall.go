package wireguard

import (
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/conf"
	"golang.zx2c4.com/wireguard/windows/tunnel/firewall"
	"log"
)

func EnableFirewall(conf *conf.Config, tun *tun.NativeTun) error {
	restrictAll := false
	if len(conf.Peers) == 1 {
	nextallowedip:
		for _, allowedip := range conf.Peers[0].AllowedIPs {
			if allowedip.Cidr == 0 {
				for _, b := range allowedip.IP {
					if b != 0 {
						continue nextallowedip
					}
				}
				restrictAll = true
				break
			}
		}
	}
	if restrictAll && len(conf.Interface.DNS) == 0 {
		log.Println("Warning: no DNS server specified, despite having an allowed IPs of 0.0.0.0/0 or ::/0. There may be connectivity issues.")
	}
	return firewall.EnableFirewall(tun.LUID(), conf.Interface.DNS, restrictAll)
}
