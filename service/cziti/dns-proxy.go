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

package cziti

import (
	"fmt"
	"github.com/miekg/dns"
	"io"
	"net"
	"os"
	"strings"
	"time"
)

var domains []string // get any connection-specific local domains

var reqch = make(chan dnsreq, 64)
var proxiedRequests = make(chan *proxiedReq, 64)
var respChan = make(chan []byte, 64)

func processDNSquery(packet []byte, p *net.UDPAddr, s *net.UDPConn) {
	q := &dns.Msg{}
	if err := q.Unpack(packet); err != nil {
		log.Errorf("ERROR", err)
		return
	}

	msg := dns.Msg{}
	msg.SetReply(q)
	msg.RecursionAvailable = false
	msg.Authoritative = false
	msg.Rcode = dns.RcodeRefused

	query := q.Question[0]
	log.Tracef("processing a dns query. type:%s, for:%s on %v. id:%v", dns.Type(query.Qtype), query.Name, p, q.Id)

	var ip net.IP
	// fmt.Printf("query: Type(%d) name(%s)\n", query.Qtype, query.Name)
	dnsName := strings.TrimSpace(query.Name)
	ip = DNS.Resolve(dnsName)

	// never proxy hostnames that we know about regardless of type
	if ip == nil {
		// no direct hit. need to now check to see if the dns query used a connection-specific local domain
		for _, d := range domains {
			domain := d
			if strings.HasSuffix(dnsName, domain) {
				dnsNameTrimmed := strings.TrimRight(dnsName, domain)
				// dns request has domain appended - removing and resolving
				ip = DNS.Resolve(dnsNameTrimmed)
				break
			}
		}
	}

	// never proxy hostnames that we know about regardless of type
	if ip != nil {
		log.Debugf("resolved %s as %v", query.Name, ip)

		var answer *dns.A
		if query.Qtype == dns.TypeA && len(ip.To4()) == net.IPv4len {
			answer = &dns.A{
				Hdr: dns.RR_Header{Name: query.Name, Rrtype: dns.TypeA, Class: dns.ClassINET, Ttl: 60},
				A:   ip,
			}
		} else if query.Qtype == dns.TypeAAAA {
			answer = &dns.A{
				Hdr: dns.RR_Header{Name: query.Name, Rrtype: dns.TypeAAAA, Class: dns.ClassINET, Ttl: 60},
				A:   ip.To16(),
			}
		}

		if answer != nil {
			msg.Authoritative = true
			msg.Rcode = dns.RcodeSuccess

			msg.Answer = append(msg.Answer, answer)
		}

		repB, err := msg.Pack()
		if err == nil {
			_, _, err = s.WriteMsgUDP(repB, nil, p)
		}
		if err != nil {
			log.Error("unexpected dns error", err)
		}
	} else {
		// log.Debug("proxying ", dns.Type(query.Qtype), query.Name, q.Id, " for ", p)
		proxyDNS(q, p, s)
	}
}

type dnsreq struct {
	q []byte
	s *net.UDPConn
	p *net.UDPAddr
}

func RunDNSserver(dnsBind []net.IP, ready chan bool) {
	dnsServers := GetUpstreamDNS()
	go runDNSproxy(dnsServers)

	for _, bindAddr := range dnsBind {
		go runListener(bindAddr, 53, reqch)
	}

	ReplaceDNS(dnsBind)

	ready <- true

	for req := range reqch {
		processDNSquery(req.q, req.p, req.s)
	}
}

func runListener(ip net.IP, port int, reqch chan dnsreq) {
	log.Infof("Running DNS listener on %v", ip)
	laddr := &net.UDPAddr{
		IP:   ip,
		Port: port,
		Zone: "",
	}

	network := "udp6"
	if len(ip.To4()) == net.IPv4len {
		network = "udp4"
	}

	server, err := net.ListenUDP(network, laddr)
	if err != nil {
		panic(err)
	}

	for {
		b := make([]byte, 1024)
		nb, _, _, peer, err := server.ReadMsgUDP(b, nil)
		if err != nil {
			if err == io.EOF || err == os.ErrClosed || strings.HasSuffix(err.Error(), "use of closed network connection") {
				log.Warnf("DNS listener closing.")
				server.Close()
				break
			} else {
				server.Close()
				panic(err)
			}
		}

		reqch <- dnsreq{
			q: b[:nb],
			s: server,
			p: peer,
		}
	}
}

/*******************************************************************/
type proxiedReq struct {
	req  *dns.Msg
	peer *net.UDPAddr
	s    *net.UDPConn
	exp  time.Time
}

func proxyDNS(req *dns.Msg, peer *net.UDPAddr, serv *net.UDPConn) {
	proxiedRequests <- &proxiedReq{
		req:  req,
		peer: peer,
		s:    serv,
		exp:  time.Now().Add(30 * time.Second),
	}
}

func dnsPanicRecover() {
	// get dns again and reconfigure
	go runDNSproxy(GetUpstreamDNS())
}

func runDNSproxy(dnsServers []string) {
	domains = GetConnectionSpecificDomains()
	log.Infof("ConnectionSpecificDomains: %v", domains)
	log.Infof("dnsServers: %v", dnsServers)
	defer func() {
		if err := recover(); err != nil {
			log.Errorf("Recovering from panic due to DNS-related issue. %v", err)
			dnsPanicRecover()
		}
	}()

	var dnsUpstreams []*net.UDPConn
	for _, s := range dnsServers {
		sAddr, err := net.ResolveUDPAddr("udp", fmt.Sprintf("%s:53", s))
		if err != nil {
			// fec0:0:0:ffff:: is 'legacy' from windows apparently...
			// see: https://en.wikipedia.org/wiki/IPv6_address#Deprecated_and_obsolete_addresses_2
			if ! strings.HasPrefix(s, "fec0:0:0:ffff::") {
				log.Errorf("skipping upstream due to error: %s, %v", s, err.Error())
			} else {
				// just ignore for now - don't even log it...
			}
		} else {
			log.Debugf("adding upstream dns server: %s", s)
			conn, err := net.DialUDP("udp", nil, sAddr)
			if err != nil {
				log.Debugf("skipping upstream due to dial udp issue to %s. error: %v", s, err.Error())
			} else {
				dnsUpstreams = append(dnsUpstreams, conn)
			}
		}
	}

	for _, proxy := range dnsUpstreams {
		go func() {
			resp := make([]byte, 1024) //make a buffer which is reused
			for {
				n, err := proxy.Read(resp)
				if err != nil {
					// something is wrong with the DNS connection panic and let DNS recovery kick in
					if err == io.EOF || err == os.ErrClosed || strings.HasSuffix(err.Error(), "use of closed network connection") {
						log.Errorf("error receiving from ip: %v. error %v", proxy.RemoteAddr(), err)
						return
					} else {
						log.Warnf("odd error: %v", err)
					}
				} else {
					respChan <- resp[:n]
				}
			}
		}()
	}

	reqs := make(map[uint32]*proxiedReq)

	for {
		select {
		case pr := <-proxiedRequests:
			id := (uint32(pr.req.Id) << 16) | uint32(pr.req.Question[0].Qtype)
			reqs[id] = pr
			b, _ := pr.req.Pack()
			for _, proxy := range dnsUpstreams {
				if _, err := proxy.Write(b); err != nil {
					//TODO: if this happens -does this mean the next dns server is not available?

					proxy.Close() //first thing - close the proxy connection

					// when this hits - it never seems to recover. throw a panic which will get recovered
					// via the defer specified above and try to recreate the connections. this is a heavy
					// handed way of reconnecting to the DNS server but it should be infrequent
					log.Panicf("failed to proxy DNS to %s %s %v. %v",
						dns.Type(pr.req.Question[0].Qtype),
						pr.req.Question[0].Name,
						proxy.RemoteAddr(),
						err)
				} else {
					log.Tracef("Proxied request sent to %v from %v", proxy.RemoteAddr(), proxy.LocalAddr())
				}
			}

		case rep := <-respChan:
			reply := dns.Msg{}
			if err := reply.Unpack(rep); err == nil {
				id := (uint32(reply.Id) << 16) | uint32(reply.Question[0].Qtype)
				req, found := reqs[id]
				if found {
					delete(reqs, id)
					log.Tracef("proxy resolved %v from %v", reply.Question[0].Name, req.s.RemoteAddr())
					req.s.WriteMsgUDP(rep, nil, req.peer)
				} else {
					log.Tracef("matching request was not found for %s %s",
						dns.Type(reply.Question[0].Qtype), reply.Question[0].Name)
				}
			}
		case <-time.After(time.Minute):
			// cleanup requests we didn't get answers for
			now := time.Now()
			for k, r := range reqs {
				if now.After(r.exp) {
					log.Warn("a DNS request has expired - enable trace logging and reproduce this issue for more information")
					log.Tracef("         expired DNS req: %s %s", dns.Type(r.req.Question[0].Qtype), r.req.Question[0].Name)

					delete(reqs, k)
				}
			}
		}
	}
}
