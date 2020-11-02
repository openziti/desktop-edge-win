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

package iputil

import (
	"encoding/binary"
	"net"
)

var allOnes = 0xFFFFFFFF

func Ipv4Inc(ip net.IP, maskBits int) net.IP {
	baseIpMask := 32 - maskBits
	baseMask := (allOnes >> baseIpMask) << baseIpMask
	ipMask := allOnes >> maskBits

	ipAsInt := Ipv4ToUint32(ip)
	baseIp := ipAsInt & uint32(baseMask)
	//fmt.Printf("actual ip: %b - %v ", ipAsInt, ip)

	newIpAsInt := ipAsInt + 1

	return Uint32ToIpv4(baseIp + newIpAsInt & uint32(ipMask))
}
func Ipv4ToUint32(ip net.IP) uint32 {
	if len(ip) == 16 {
		return binary.BigEndian.Uint32(ip[12:16])
	}
	return binary.BigEndian.Uint32(ip)
}
func Uint32ToIpv4(nn uint32) net.IP {
	ip := make(net.IP, 4)
	binary.BigEndian.PutUint32(ip, nn)
	return ip
}
