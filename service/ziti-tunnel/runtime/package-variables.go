package runtime

import (
	"time"
	"wintun-testing/ziti-tunnel/globals"
)

const (
	NF_GROUP_NAME = "NetFoundry Tunneler Users"
	TunName = "ZitiTUN"
)

var TunStarted time.Time
var log = globals.Logger()

const Ipv4ip = "169.254.1.1"
const Ipv4mask = 24
const Ipv4dns = "127.0.0.1" // use lo -- don't pass DNS queries through tunneler SDK

// IPv6 CIDR fe80:6e66:7a69:7469::/64
//   <link-local>: nf : zi : ti ::
const ipv6pfx = "fe80:6e66:7a69:7469"
const ipv6ip = "1"
const ipv6mask = 64
const Ipv6dns = "::1" // must be in "ipv6ip/ipv6mask" CIDR block

