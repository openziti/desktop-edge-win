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

	ipAsInt := Ipv4ToInt(ip)
	baseIp := ipAsInt & uint32(baseMask)
	//fmt.Printf("actual ip: %b - %v ", ipAsInt, ip)

	newIpAsInt := ipAsInt + 1

	return IntToIpv4(baseIp + newIpAsInt & uint32(ipMask))
}
func Ipv4ToInt(ip net.IP) uint32 {
	if len(ip) == 16 {
		return binary.BigEndian.Uint32(ip[12:16])
	}
	return binary.BigEndian.Uint32(ip)
}
func IntToIpv4(nn uint32) net.IP {
	ip := make(net.IP, 4)
	binary.BigEndian.PutUint32(ip, nn)
	return ip
}
