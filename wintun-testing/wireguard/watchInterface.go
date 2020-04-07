package wireguard

import (
	"bytes"
	"golang.org/x/sys/windows"
	"golang.zx2c4.com/wireguard/device"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/conf"
	"golang.zx2c4.com/wireguard/windows/services"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
	"log"
	"net"
	"sort"
	"sync"
	"time"

)


type InterfaceWatcherError struct {
	ServiceError services.Error
	Err          error
}
type InterfaceWatcherEvent struct {
	luid   winipcfg.LUID
	family winipcfg.AddressFamily
}
type InterfaceWatcher struct {
	Errors chan InterfaceWatcherError

	device *device.Device
	conf   *conf.Config
	tun    *tun.NativeTun

	setupMutex              sync.Mutex
	interfaceChangeCallback winipcfg.ChangeCallback
	changeCallbacks4        []winipcfg.ChangeCallback
	changeCallbacks6        []winipcfg.ChangeCallback
	storedEvents            []InterfaceWatcherEvent
}

func WatchInterface() (*InterfaceWatcher, error) {
	iw := &InterfaceWatcher{
		Errors: make(chan InterfaceWatcherError, 2),
	}
	var err error
	iw.interfaceChangeCallback, err = winipcfg.RegisterInterfaceChangeCallback(func(notificationType winipcfg.MibNotificationType, iface *winipcfg.MibIPInterfaceRow) {
		iw.setupMutex.Lock()
		defer iw.setupMutex.Unlock()

		if notificationType != winipcfg.MibAddInstance {
			return
		}
		if iw.tun == nil {
			iw.storedEvents = append(iw.storedEvents, InterfaceWatcherEvent{iface.InterfaceLUID, iface.Family})
			return
		}
		if iface.InterfaceLUID != winipcfg.LUID(iw.tun.LUID()) {
			return
		}
		iw.setup(iface.Family)
	})
	if err != nil {
		return nil, err
	}
	return iw, nil
}

func (iw *InterfaceWatcher) setup(family winipcfg.AddressFamily) {
	var changeCallbacks *[]winipcfg.ChangeCallback
	var ipversion string
	if family == windows.AF_INET {
		changeCallbacks = &iw.changeCallbacks4
		ipversion = "v4"
	} else if family == windows.AF_INET6 {
		changeCallbacks = &iw.changeCallbacks6
		ipversion = "v6"
	} else {
		return
	}
	if len(*changeCallbacks) != 0 {
		for _, cb := range *changeCallbacks {
			cb.Unregister()
		}
		*changeCallbacks = nil
	}
	var err error

	log.Printf("Monitoring default %s routes", ipversion)
	*changeCallbacks, err = monitorDefaultRoutes(family, iw.device, iw.conf.Interface.MTU == 0, hasDefaultRoute(family, iw.conf.Peers), iw.tun)
	if err != nil {
		iw.Errors <- InterfaceWatcherError{services.ErrorBindSocketsToDefaultRoutes, err}
		return
	}

	log.Printf("Setting device %s addresses", ipversion)
	err = configureInterface(family, iw.conf, iw.tun)
	if err != nil {
		iw.Errors <- InterfaceWatcherError{services.ErrorSetNetConfig, err}
		return
	}
}

func monitorDefaultRoutes(family winipcfg.AddressFamily, device *device.Device, autoMTU bool, blackholeWhenLoop bool, tun *tun.NativeTun) ([]winipcfg.ChangeCallback, error) {
	var minMTU uint32
	if family == windows.AF_INET {
		minMTU = 576
	} else if family == windows.AF_INET6 {
		minMTU = 1280
	}
	ourLUID := winipcfg.LUID(tun.LUID())
	lastLUID := winipcfg.LUID(0)
	lastIndex := ^uint32(0)
	lastMTU := uint32(0)
	doIt := func() error {
		err := bindSocketRoute(family, device, ourLUID, &lastLUID, &lastIndex, blackholeWhenLoop)
		if err != nil {
			return err
		}
		if !autoMTU {
			return nil
		}
		mtu := uint32(0)
		if lastLUID != 0 {
			iface, err := lastLUID.Interface()
			if err != nil {
				return err
			}
			if iface.MTU > 0 {
				mtu = iface.MTU
			}
		}
		if mtu > 0 && lastMTU != mtu {
			iface, err := ourLUID.IPInterface(family)
			if err != nil {
				return err
			}
			iface.NLMTU = mtu - 80
			if iface.NLMTU < minMTU {
				iface.NLMTU = minMTU
			}
			err = iface.Set()
			if err != nil {
				return err
			}
			tun.ForceMTU(int(iface.NLMTU)) // TODO: having one MTU for both v4 and v6 kind of breaks the windows model, so right now this just gets the second one which is... bad.
			lastMTU = mtu
		}
		return nil
	}
	err := doIt()
	if err != nil {
		return nil, err
	}

	firstBurst := time.Time{}
	burstMutex := sync.Mutex{}
	burstTimer := time.AfterFunc(time.Hour*200, func() {
		burstMutex.Lock()
		firstBurst = time.Time{}
		doIt()
		burstMutex.Unlock()
	})
	burstTimer.Stop()
	bump := func() {
		burstMutex.Lock()
		burstTimer.Reset(time.Millisecond * 150)
		if firstBurst.IsZero() {
			firstBurst = time.Now()
		} else if time.Since(firstBurst) > time.Second*2 {
			firstBurst = time.Time{}
			burstTimer.Stop()
			doIt()
		}
		burstMutex.Unlock()
	}

	cbr, err := winipcfg.RegisterRouteChangeCallback(func(notificationType winipcfg.MibNotificationType, route *winipcfg.MibIPforwardRow2) {
		if route != nil && route.DestinationPrefix.PrefixLength == 0 {
			bump()
		}
	})
	if err != nil {
		return nil, err
	}
	cbi, err := winipcfg.RegisterInterfaceChangeCallback(func(notificationType winipcfg.MibNotificationType, iface *winipcfg.MibIPInterfaceRow) {
		if notificationType == winipcfg.MibParameterNotification {
			bump()
		}
	})
	if err != nil {
		cbr.Unregister()
		return nil, err
	}
	return []winipcfg.ChangeCallback{cbr, cbi}, nil
}

func bindSocketRoute(family winipcfg.AddressFamily, device *device.Device, ourLUID winipcfg.LUID, lastLUID *winipcfg.LUID, lastIndex *uint32, blackholeWhenLoop bool) error {
	r, err := winipcfg.GetIPForwardTable2(family)
	if err != nil {
		return err
	}
	lowestMetric := ^uint32(0)
	index := uint32(0)       // Zero is "unspecified", which for IP_UNICAST_IF resets the value, which is what we want.
	luid := winipcfg.LUID(0) // Hopefully luid zero is unspecified, but hard to find docs saying so.
	for i := range r {
		if r[i].DestinationPrefix.PrefixLength != 0 || r[i].InterfaceLUID == ourLUID {
			continue
		}
		ifrow, err := r[i].InterfaceLUID.Interface()
		if err != nil || ifrow.OperStatus != winipcfg.IfOperStatusUp {
			continue
		}
		if r[i].Metric < lowestMetric {
			lowestMetric = r[i].Metric
			index = r[i].InterfaceIndex
			luid = r[i].InterfaceLUID
		}
	}
	if luid == *lastLUID && index == *lastIndex {
		return nil
	}
	*lastLUID = luid
	*lastIndex = index
	blackhole := blackholeWhenLoop && index == 0
	if family == windows.AF_INET {
		log.Printf("Binding v4 socket to interface %d (blackhole=%v)", index, blackhole)
		return device.BindSocketToInterface4(index, blackhole)
	} else if family == windows.AF_INET6 {
		log.Printf("Binding v6 socket to interface %d (blackhole=%v)", index, blackhole)
		return device.BindSocketToInterface6(index, blackhole)
	}
	return nil
}



func configureInterface(family winipcfg.AddressFamily, conf *conf.Config, tun *tun.NativeTun) error {
	luid := winipcfg.LUID(tun.LUID())

	estimatedRouteCount := 0
	for _, peer := range conf.Peers {
		estimatedRouteCount += len(peer.AllowedIPs)
	}
	routes := make([]winipcfg.RouteData, 0, estimatedRouteCount)
	addresses := make([]net.IPNet, len(conf.Interface.Addresses))
	var haveV4Address, haveV6Address bool
	for i, addr := range conf.Interface.Addresses {
		addresses[i] = addr.IPNet()
		if addr.Bits() == 32 {
			haveV4Address = true
		} else if addr.Bits() == 128 {
			haveV6Address = true
		}
	}

	foundDefault4 := false
	foundDefault6 := false
	for _, peer := range conf.Peers {
		for _, allowedip := range peer.AllowedIPs {
			if (allowedip.Bits() == 32 && !haveV4Address) || (allowedip.Bits() == 128 && !haveV6Address) {
				continue
			}
			route := winipcfg.RouteData{
				Destination: allowedip.IPNet(),
				Metric:      0,
			}
			if allowedip.Bits() == 32 {
				if allowedip.Cidr == 0 {
					foundDefault4 = true
				}
				route.NextHop = net.IPv4zero
			} else if allowedip.Bits() == 128 {
				if allowedip.Cidr == 0 {
					foundDefault6 = true
				}
				route.NextHop = net.IPv6zero
			}
			routes = append(routes, route)
		}
	}

	err := luid.SetIPAddressesForFamily(family, addresses)
	if err == windows.ERROR_OBJECT_ALREADY_EXISTS {
		cleanupAddressesOnDisconnectedInterfaces(family, addresses)
		err = luid.SetIPAddressesForFamily(family, addresses)
	}
	if err != nil {
		return err
	}

	deduplicatedRoutes := make([]*winipcfg.RouteData, 0, len(routes))
	sort.Slice(routes, func(i, j int) bool {
		return routes[i].Metric < routes[j].Metric ||
			bytes.Compare(routes[i].NextHop, routes[j].NextHop) == -1 ||
			bytes.Compare(routes[i].Destination.IP, routes[j].Destination.IP) == -1 ||
			bytes.Compare(routes[i].Destination.Mask, routes[j].Destination.Mask) == -1
	})
	for i := 0; i < len(routes); i++ {
		if i > 0 && routes[i].Metric == routes[i-1].Metric &&
			bytes.Equal(routes[i].NextHop, routes[i-1].NextHop) &&
			bytes.Equal(routes[i].Destination.IP, routes[i-1].Destination.IP) &&
			bytes.Equal(routes[i].Destination.Mask, routes[i-1].Destination.Mask) {
			continue
		}
		deduplicatedRoutes = append(deduplicatedRoutes, &routes[i])
	}

	err = luid.SetRoutesForFamily(family, deduplicatedRoutes)
	if err != nil {
		return nil
	}

	ipif, err := luid.IPInterface(family)
	if err != nil {
		return err
	}
	if conf.Interface.MTU > 0 {
		ipif.NLMTU = uint32(conf.Interface.MTU)
		tun.ForceMTU(int(ipif.NLMTU))
	}
	if family == windows.AF_INET {
		if foundDefault4 {
			ipif.UseAutomaticMetric = false
			ipif.Metric = 0
		}
	} else if family == windows.AF_INET6 {
		if foundDefault6 {
			ipif.UseAutomaticMetric = false
			ipif.Metric = 0
		}
		ipif.DadTransmits = 0
		ipif.RouterDiscoveryBehavior = winipcfg.RouterDiscoveryDisabled
	}
	err = ipif.Set()
	if err != nil {
		return err
	}

	err = luid.SetDNSForFamily(family, conf.Interface.DNS)
	if err != nil {
		return err
	}

	return nil
}

func cleanupAddressesOnDisconnectedInterfaces(family winipcfg.AddressFamily, addresses []net.IPNet) {
	if len(addresses) == 0 {
		return
	}
	includedInAddresses := func(a net.IPNet) bool {
		// TODO: this makes the whole algorithm O(n^2). But we can't stick net.IPNet in a Go hashmap. Bummer!
		for _, addr := range addresses {
			ip := addr.IP
			if ip4 := ip.To4(); ip4 != nil {
				ip = ip4
			}
			mA, _ := addr.Mask.Size()
			mB, _ := a.Mask.Size()
			if bytes.Equal(ip, a.IP) && mA == mB {
				return true
			}
		}
		return false
	}
	interfaces, err := winipcfg.GetAdaptersAddresses(family, winipcfg.GAAFlagDefault)
	if err != nil {
		return
	}
	for _, iface := range interfaces {
		if iface.OperStatus == winipcfg.IfOperStatusUp {
			continue
		}
		for address := iface.FirstUnicastAddress; address != nil; address = address.Next {
			ip := address.Address.IP()
			ipnet := net.IPNet{IP: ip, Mask: net.CIDRMask(int(address.OnLinkPrefixLength), 8*len(ip))}
			if includedInAddresses(ipnet) {
				log.Printf("Cleaning up stale address %s from interface ‘%s’", ipnet.String(), iface.FriendlyName())
				iface.LUID.DeleteIPAddress(ipnet)
			}
		}
	}
}


func hasDefaultRoute(family winipcfg.AddressFamily, peers []conf.Peer) bool {
	var (
		foundV401    bool
		foundV41281  bool
		foundV600001 bool
		foundV680001 bool
		foundV400    bool
		foundV600    bool
		v40          = [4]byte{}
		v60          = [16]byte{}
		v48          = [4]byte{0x80}
		v68          = [16]byte{0x80}
	)
	for _, peer := range peers {
		for _, allowedip := range peer.AllowedIPs {
			if allowedip.Cidr == 1 && len(allowedip.IP) == 16 && allowedip.IP.Equal(v60[:]) {
				foundV600001 = true
			} else if allowedip.Cidr == 1 && len(allowedip.IP) == 16 && allowedip.IP.Equal(v68[:]) {
				foundV680001 = true
			} else if allowedip.Cidr == 1 && len(allowedip.IP) == 4 && allowedip.IP.Equal(v40[:]) {
				foundV401 = true
			} else if allowedip.Cidr == 1 && len(allowedip.IP) == 4 && allowedip.IP.Equal(v48[:]) {
				foundV41281 = true
			} else if allowedip.Cidr == 0 && len(allowedip.IP) == 16 && allowedip.IP.Equal(v60[:]) {
				foundV600 = true
			} else if allowedip.Cidr == 0 && len(allowedip.IP) == 4 && allowedip.IP.Equal(v40[:]) {
				foundV400 = true
			}
		}
	}
	if family == windows.AF_INET {
		return foundV400 || (foundV401 && foundV41281)
	} else if family == windows.AF_INET6 {
		return foundV600 || (foundV600001 && foundV680001)
	}
	return false
}


func (iw *InterfaceWatcher) Configure(device *device.Device, conf *conf.Config, tun *tun.NativeTun) {
	iw.setupMutex.Lock()
	defer iw.setupMutex.Unlock()

	iw.device, iw.conf, iw.tun = device, conf, tun
	for _, event := range iw.storedEvents {
		if event.luid == winipcfg.LUID(iw.tun.LUID()) {
			iw.setup(event.family)
		}
	}
	iw.storedEvents = nil
}