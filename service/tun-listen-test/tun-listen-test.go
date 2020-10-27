package main

import (
	"github.com/michaelquigley/pfxlog"
	"github.com/openziti/desktop-edge-win/service/cziti"
	"github.com/sirupsen/logrus"
	"net"
)

var log = pfxlog.Logger()

func main() {

	log.Level = logrus.TraceLevel

	ip := net.ParseIP("100.64.0.1")

	dns := []net.IP{ ip }

	ready := make(chan bool)
	cziti.DnsInit(nil, "123.4.3.5", 24)
	go cziti.RunDNSserver(dns, ready)
	cziti.DNS.RegisterService("some identity", "dns-lookup.name", 5000, &cziti.CZitiCtx{}, "some-service-name" )

	<-ready
	log.Infof("DNS server - ready")
	<-ready
	log.Infof("DNS server - ready")
}
