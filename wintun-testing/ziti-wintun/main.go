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
)

const bufferSize = 64 * 1024

func main() {
	user, err := user2.Current()

	fmt.Printf("user [%v]\n", user)

	if len(os.Args) != 2 {
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
	defer func() { _ = tunDevice.Close() }()
	if name, err := tunDevice.Name(); err == nil {
		fmt.Printf("created TUN device [%s]\n", name)
	}

	nativeTunDevice := tunDevice.(*tun.NativeTun)
	luid := winipcfg.LUID(nativeTunDevice.LUID())
	ip, ipnet, err := net.ParseCIDR("169.254.100.200/32")
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

	buffer := make([]byte, bufferSize)
	for {
		n, err := tunDevice.Read(buffer, bufferSize)
		if err != nil {
			fatal(err)
		}
		fmt.Printf("read [%d] bytes from TUN", n)
	}

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
