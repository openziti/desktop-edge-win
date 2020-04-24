package main

import (
	"fmt"
	"golang.org/x/sys/windows"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
	"net"
	"os"
	"os/signal"
	user2 "os/user"
	"path/filepath"
	"syscall"
	"wintun-testing/cziti"
	"wintun-testing/cziti/windns"
)

const bufferSize = 64 * 1024

// TUN IPs
const ipv4ip = "169.254.1.1"
const ipv4mask = 24
const ipv4dns = "127.0.0.1" // use lo -- don't pass DNS queries through tunneler SDK

// IPv6 CIDR fe80:6e66:7a69:7469::/64
//   <link-local>: nf : zi : ti ::
const ipv6pfx = "fe80:6e66:7a69:7469"
const ipv6ip = "1"
const ipv6mask = 64
const ipv6dns = "::1" // must be in "ipv6ip/ipv6mask" CIDR block

func main() {
	user, err := user2.Current()

	fmt.Printf("user [%v]\n", user)

	if len(os.Args) < 2 {
		fmt.Printf("usage: %s <interfaceName>\n", filepath.Base(os.Args[0]))
		os.Exit(1)
	}
	interfaceName := os.Args[1]

	fmt.Println("creating TUN device")
	tunDevice, err := tun.CreateTUN(interfaceName, 64*1024)
	if err == nil {
		realInterfaceName, err2 := tunDevice.Name()
		if err2 == nil {
			interfaceName = realInterfaceName
		}

	} else {
		fmt.Printf("error creating TUN device: (%v)\n", err)
		os.Exit(1)
	}

	if name, err := tunDevice.Name(); err == nil {
		fmt.Printf("created TUN device [%s]\n", name)
	}

	nativeTunDevice := tunDevice.(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())
	ip, ipnet, err := net.ParseCIDR(fmt.Sprintf("%s/%d", ipv4ip, ipv4mask))
	if err != nil {
		fatal(err)
	}
	fmt.Printf("setting TUN interface address to [%s]\n", ip)
	err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
	if err != nil {
		fatal(err)
	}

	dnsServers := []net.IP{
		net.ParseIP(ipv4dns).To4(),
		net.ParseIP(ipv6dns),
	}
	err = luid.AddDNS(dnsServers)
	if err != nil {
		fatal(err)
	}
	dns, err := luid.DNS()
	if err != nil {
		fatal(err)
	}
	fmt.Println("dns servers = ", dns)

	fmt.Printf("routing destination [%s] through [%s]\n", *ipnet, ipnet.IP)
	err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
	if err != nil {
		fatal(err)
	}
	fmt.Println("routing applied")

	errs := make(chan error)
	term := make(chan os.Signal, 1)

	fmt.Println("running")
	cziti.DnsInit(ipv4ip, 24)

	cziti.Start()
	_, err = cziti.HookupTun(tunDevice, dns)
	if err != nil {
		panic(err)
	}

	if ctx, err := cziti.LoadZiti(os.Args[2]); err != nil {
		panic(err)
	} else {
		fmt.Printf("successfully loaded %s@%s\n", ctx.Name(), ctx.Controller())
	}

	signal.Notify(term, os.Interrupt)
	signal.Notify(term, os.Kill)
	signal.Notify(term, syscall.SIGTERM)

	select {
	case <-term:
	case <-errs:
	}

	windns.ResetDNS()

	fmt.Println("shutting down")
}

func fatal(v ...interface{}) {
	panic(windows.StringToUTF16Ptr(fmt.Sprint(v...)))
}
