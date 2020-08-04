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
	"github.com/openziti/desktop-edge-win/service/cziti/windns"
)

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

	ip = DNS.Resolve(query.Name)

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
			log.Debug("dns error", err)
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

func runDNSserver(dnsBind []net.IP) {
	dnsServers := windns.GetUpstreamDNS()
	go runDNSproxy(dnsServers)

	reqch := make(chan dnsreq, 16) //allow dns requests to be buffered

	for _, bindAddr := range dnsBind {
		go runListener(bindAddr, 53, reqch)
	}

	windns.ReplaceDNS(dnsBind)

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

	// oob := make([]byte, 1024)
	for {
		b := make([]byte, 1024)
		nb, _, _, peer, err := server.ReadMsgUDP(b, nil)
		if err != nil {
			panic(err)
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

var proxiedRequests chan *proxiedReq

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
	go runDNSproxy(windns.GetUpstreamDNS())
}

func runDNSproxy(dnsServers []string) {
	defer func() {
		if err := recover(); err != nil {
			log.Errorf("Recovered in f. %v", err)
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
				log.Debugf("skipping upstream: %s, %v", s, err.Error())
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

	proxiedRequests = make(chan *proxiedReq, 16)
	respChan := make(chan []byte, 16)

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
				}
			}

		case rep := <-respChan:
			reply := dns.Msg{}
			if err := reply.Unpack(rep); err == nil {
				id := (uint32(reply.Id) << 16) | uint32(reply.Question[0].Qtype)
				req, found := reqs[id]
				if found {
					delete(reqs, id)
					// fmt.Printf("proxy resolved %+v for %v\n\n", reply, req.peer)
					req.s.WriteMsgUDP(rep, nil, req.peer)
				} else {
					log.Trace("matching request was not found for ",
						dns.Type(reply.Question[0].Qtype), reply.Question[0].Name)
				}
			}
		case <-time.After(time.Minute):
			// cleanup requests we didn't get answers for
			now := time.Now()
			for k, r := range reqs {
				if now.After(r.exp) {
					log.Debugf("req expired %s %s", dns.Type(r.req.Question[0].Qtype), r.req.Question[0].Name)
					delete(reqs, k)
				}
			}
		}
	}
}
