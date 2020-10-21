package api

import "net"

type DesktopEdgeIface interface {
	AddRoute(destination net.IPNet, nextHop net.IP, metric uint32) error
	RemoveRoute(destination net.IPNet, nextHop net.IP) error

	InterceptDNS()
	ReleaseDNS()

	InterceptIP()
	ReleaseIP()

	Close()
}

type DesktopEdgeManager interface {
	AddIdentity()
	RemoveIdentity()
	Status()
	TunnelState()
	IdentityToggle()
	SetLogLevel()
	Debug()
	SaveState()
	CreateTun()
	FindIdentity()
}