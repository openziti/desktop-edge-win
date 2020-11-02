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
const (
	MaxDnsRequests = 64
	DnsMsgBufferSize = 1024
)
var reqch = make(chan dnsreq, MaxDnsRequests)
var proxiedRequests = make(chan *proxiedReq, MaxDnsRequests)
var respChan = make(chan []byte, MaxDnsRequests)

func processDNSquery(packet []byte, p *net.UDPAddr, s *net.UDPConn, ipVer int) {
	q := &dns.Msg{}
	if err := q.Unpack(packet); err != nil {
		log.Errorf("unexpected error in processDNSquery. [len(packet):%d] [ipVer:%v] [error: %v]", len(packet), ipVer, err)
		return
	}

	msg := dns.Msg{}
	msg.SetReply(q)
	msg.RecursionAvailable = false
	msg.Authoritative = false
	msg.Rcode = dns.RcodeRefused

	var query dns.Question
	if len(q.Question) > 0 {
		query = q.Question[0]
		log.Tracef("processing a dns query. type:%s, for:%s on %v. id:%v", dns.Type(query.Qtype), query.Name, p, q.Id)
	} else {
		log.Warnf("DNS MSG had no question! %v", msg.String())
		return
	}

	var ip net.IP
	dnsName := strings.TrimSpace(query.Name)
	ip = dnsi.Resolve(dnsName)

	// never proxy hostnames that we know about regardless of type
	if ip == nil {
		// no direct hit. need to now check to see if the dns query used a connection-specific local domain
		for _, d := range domains {
			domain := d
			if strings.HasSuffix(dnsName, domain) {
				dnsNameTrimmed := strings.TrimRight(dnsName, domain)
				// dns request has domain appended - removing and resolving
				ip = dnsi.Resolve(dnsNameTrimmed)
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
		proxyDNS(q, p, s, ipVer)
	}
}

type dnsreq struct {
	q    []byte
	s    *net.UDPConn
	p    *net.UDPAddr
	ifId int
}

func RunDNSserver(dnsBind []net.IP, ready chan bool) {
	go runDNSproxy(GetUpstreamDNS(), dnsBind)

	for _, bindAddr := range dnsBind {
		go runListener(&bindAddr, 53, reqch)
	}

	ReplaceDNS(dnsBind)

	ready <- true

	for req := range reqch {
		processDNSquery(req.q, req.p, req.s, req.ifId)
	}
}

func runListener(ip *net.IP, port int, reqch chan dnsreq) {
	laddr := &net.UDPAddr{
		IP:   *ip,
		Port: port,
		Zone: "",
	}

	id := 6
	network := "udp6"
	if len(ip.To4()) == net.IPv4len {
		id = 4
		network = "udp4"
	}

	attempts := 0
	var server *net.UDPConn
	var err error
	maxAttempts := 20
	for attempts < maxAttempts {
		attempts++
		server, err = net.ListenUDP(network, laddr)
		if err == nil {
			break
		} else if attempts > maxAttempts {
			log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "udp listening on network", err)
		} else if attempts < (3 * maxAttempts / 4) {
			// only log at debug until we hit 3/4 the max attempts to remove unnecessary warns from the log
			log.Debugf("System not ready to listen on %v yet. Retrying after 500ms. This has happened %d times.", laddr, attempts)
		} else {
			log.Warnf("System not ready to listen on %v yet. Retrying after 500ms. This has happened %d times.", laddr, attempts)
		}
		time.Sleep(500 * time.Millisecond)
	}

	log.Infof("DNS listening at: %v", laddr)

	for {
		b := *(nextBuffer())
		nb, _, _, peer, err := server.ReadMsgUDP(b, nil)
		if err != nil {
			if err == io.EOF || err == os.ErrClosed || strings.HasSuffix(err.Error(), "use of closed network connection") {
				log.Warnf("DNS listener closing.")
				_ = server.Close()
				break
			} else {
				_ = server.Close()
				log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "reading a udp message", err)
			}
		}
		if len(reqch) == cap(reqch) {
			log.Warn("DNS req will be blocked. If this warning is continuously displayed please report")
		}
		reqch <- dnsreq{
			q:    b[:nb],
			s:    server,
			p:    peer,
			ifId: id,
		}
	}
}

/*******************************************************************/
type proxiedReq struct {
	req   *dns.Msg
	peer  *net.UDPAddr
	s     *net.UDPConn
	exp   time.Time
	ipVer int
}

func proxyDNS(req *dns.Msg, peer *net.UDPAddr, serv *net.UDPConn, ipVer int) {
	if len(proxiedRequests) == cap(proxiedRequests) {
		log.Warn("proxiedRequests will be blocked. If this warning is continuously displayed please report")
	}
	proxiedRequests <- &proxiedReq{
		req:   req,
		peer:  peer,
		s:     serv,
		exp:   time.Now().Add(30 * time.Second),
		ipVer: ipVer,
	}
}

func dnsPanicRecover(localDnsServers []net.IP) {
	//reset all network interfaces...
	ResetDNS()

	// get dns again and reconfigure
	go runDNSproxy(GetUpstreamDNS(), localDnsServers)

	// reconfigure DNS
	ReplaceDNS(localDnsServers)
}

func runDNSproxy(upstreamDnsServers []string, localDnsServers []net.IP) {
	log.Infof("starting DNS proxy upstream: %v, local: %v", upstreamDnsServers, localDnsServers)
	domains = GetConnectionSpecificDomains()
	log.Infof("ConnectionSpecificDomains: %v", domains)
	defer func() {
		if err := recover(); err != nil {
			log.Errorf("Recovering from panic due to DNS-related issue. %v", err)
			dnsPanicRecover(localDnsServers)
		}
	}()

	var dnsUpstreams []*net.UDPConn

	outer:
	for _, s := range upstreamDnsServers {
		for _, ldns := range localDnsServers {
			if s == ldns.String() {
				//skipping upstream that's the same as a local
				continue outer
			}
		}
		sAddr, err := net.ResolveUDPAddr("udp", fmt.Sprintf("%s:53", s))
		if err != nil {
			// fec0:0:0:ffff:: is 'legacy' from windows apparently...
			// see: https://en.wikipedia.org/wiki/IPv6_address#Deprecated_and_obsolete_addresses_2
			if !strings.HasPrefix(s, "fec0:0:0:ffff::") {
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
		go func(p *net.UDPConn) {
			resp := make([]byte, 1024) //make a buffer which is reused
			for {
				n, err := p.Read(resp)
				if err != nil {
					// something is wrong with the DNS connection panic and let DNS recovery kick in
					if err == io.EOF || err == os.ErrClosed || strings.HasSuffix(err.Error(), "use of closed network connection") {
						log.Errorf("error receiving from ip: %v. error %v", p.RemoteAddr(), err)
						return
					} else {
						log.Warnf("odd error: %v", err)
					}
				} else {
					if len(respChan) == cap(respChan) {
						log.Warn("respChan will be blocked. If this warning is continuously displayed please report")
					}
					respChan <- resp[:n]
				}
			}
		}(proxy)
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

					_ = proxy.Close() //first thing - close the proxy connection

					// when this hits - it never seems to recover. throw a panic which will get recovered
					// via the defer specified above and try to recreate the connections. this is a heavy
					// handed way of reconnecting to the DNS server but it should be infrequent
					log.Panicf("failed to proxy DNS to %s %s %v. %v",
						dns.Type(pr.req.Question[0].Qtype),
						pr.req.Question[0].Name,
						proxy.RemoteAddr(),
						err)
				} else {
					log.Tracef("Proxied request sent to %v from ipv%d listener", proxy.RemoteAddr(), pr.ipVer)
				}
			}

		case rep := <-respChan:
			reply := dns.Msg{}
			if err := reply.Unpack(rep); err == nil {
				id := (uint32(reply.Id) << 16) | uint32(reply.Question[0].Qtype)
				req, found := reqs[id]
				if found {
					delete(reqs, id)
					log.Tracef("proxy resolved request for %v id:%d", reply.Question[0].Name, reply.Id)
					n, oobn, err := req.s.WriteMsgUDP(rep, nil, req.peer)
					if err != nil {
						log.Errorf("an error has occurred while trying to write a udp message. n:%d, oobn:%d, err:%v", n, oobn, err)
					}
				} else {
					// keep this log but leave commented out. When two listeners are enabled (ipv4/v6) this msg will
					// just mean some other request was processed successfully and removed the entry from the map
					// log.Tracef("matching request was not found for id:%d. %s %s", reply.Id, dns.Type(reply.Question[0].Qtype), reply.Question[0].Name)
				}
			}
		case <-time.After(time.Minute):
			// cleanup requests we didn't get answers for
			now := time.Now()
			for k, r := range reqs {
				if now.After(r.exp) {
					log.Debugf("a DNS request has expired - enable trace logging and reproduce this issue for more information")
					log.Tracef("         expired DNS req: %s %s", dns.Type(r.req.Question[0].Qtype), r.req.Question[0].Name)

					delete(reqs, k)
				}
			}
		}
	}
}
