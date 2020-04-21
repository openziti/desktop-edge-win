package main

import (
	"fmt"
	"golang.org/x/sys/windows"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
	"net"
	"os"
	"os/exec"
	"os/signal"
	user2 "os/user"
	"path/filepath"
	"syscall"
	"wintun-testing/cziti"
)

const bufferSize = 64 * 1024

func main() {
	user, err := user2.Current()

	fmt.Printf("user [%v]\n", user)

	if len(os.Args) < 2 {
		fmt.Printf("usage: %s <interfaceName>\n", filepath.Base(os.Args[0]))
		os.Exit(1)
	}
	interfaceName := os.Args[1]

	fmt.Println("creating TUN device")
	tunDevice, err := tun.CreateTUN(interfaceName, 0)
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
	ip, ipnet, err := net.ParseCIDR("169.254.1.1/24")
	if err != nil {
		fatal(err)
	}
	fmt.Printf("setting TUN interface address to [%s]\n", ip)
	err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
	if err != nil {
		fatal(err)
	}
	fmt.Printf("TUN interface address set to [%s]\n", ip)

	fmt.Printf("routing destination [%s] through [%s]\n", *ipnet, ipnet.IP)
	err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
	if err != nil {
		fatal(err)
	}
	fmt.Println("routing applied")

	errs := make(chan error)
	term := make(chan os.Signal, 1)

	fmt.Println("running")
	cziti.DnsInit("169.254.1.1", 24)

	cziti.Start()
	_, err = cziti.HookupTun(tunDevice)
	if err != nil {
		panic(err)
	}

	cmd := exec.Command("netsh", "interface", "ipv4", "set",
		"dnsservers", "name="+interfaceName,
		"source=static", "address=169.254.1.253",
		"register=primary", "validate=no")
	cmd.Stderr = os.Stderr
	cmd.Stdout = os.Stdout
	err = cmd.Run()
	if err != nil {
		fmt.Print(err)
	}

	if ctx, err := cziti.LoadZiti(os.Args[2]); err != nil {
		panic(err)
	} else {
		fmt.Printf("successfully loaded %s@%s\n", ctx.Name(), ctx.Controller())
	}
	//tunnel.AddIntercept("awesome sauce service", "169.254.1.42", 8080)
	/*
		for {
			buffer := make([]byte, bufferSize)
			n, err := tunDevice.Read(buffer, 0)
			if err != nil {
				fatal(err)
			}
			printPacket(buffer[:n])

			//fmt.Printf("read [%d] bytes [", n)
			//for i := 0; i < n; i++ {
			//	fmt.Printf("%x ", buffer[i])
			//}
			//fmt.Println("]")
		}
	*/

	signal.Notify(term, os.Interrupt)
	signal.Notify(term, os.Kill)
	signal.Notify(term, syscall.SIGTERM)

	select {
	case <-term:
	case <-errs:
	}

	fmt.Println("shutting down")
}

func fatal(v ...interface{}) {
	panic(windows.StringToUTF16Ptr(fmt.Sprint(v...)))
}
