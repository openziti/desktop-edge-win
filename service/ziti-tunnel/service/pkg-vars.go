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

package service

import (
	"golang.org/x/sys/windows/svc"
	"sync"
	"time"
	"github.com/openziti/ziti-tunnel-win/service/ziti-tunnel/dto"
	"github.com/openziti/ziti-tunnel-win/service/ziti-tunnel/globals"
)

var pipeBase = `\\.\pipe\NetFoundry\tunneler\`

var activeIds = make(map[string]*dto.Identity)
var rts = RuntimeState{}
var interrupt chan struct{}

var ipcWg sync.WaitGroup
var eventsWg sync.WaitGroup
var ipcConnections int
var eventsConnections int

var Debug bool

var TunStarted time.Time
var log = globals.Logger()

var	events = newTopic()

const (
	SUCCESS              = 0
	COULD_NOT_WRITE_FILE = 1
	COULD_NOT_ENROLL     = 2

	UNKNOWN_ERROR          = 100
	ERROR_DISCONNECTING_ID = 50
	IDENTITY_NOT_FOUND     = 1000

	cmdsAccepted = svc.AcceptStop | svc.AcceptShutdown | svc.AcceptPauseAndContinue

	InformationEvent = 1
	ContinueEvent = 2
	PauseEvent = 3
	InstallEvent = 4
	InterrogateEvent = 5
	StopEvent = 6
	ShutdownEvent = 7
	ErrorEvent = 1000

	// This is the name you will use for the NET START command
	SvcStartName = "ziti"

	// This is the name that will appear in the Services control panel
	SvcName = "Ziti Tunneler"

	// This is the longer description that will be shown in Services
	SvcNameLong = "Provides a client for accessing Ziti networks"

	// see: https://docs.microsoft.com/en-us/windows/win32/secauthz/sid-strings
	// breaks down to
	//		"allow" 	  	 - A   (A;;
	// 	 	"full access" 	 - FA  (A;;FA
	//		"well-known sid" - IU  (A;;FA;;;IU)
	InteractivelyLoggedInUser = "(A;;GRGW;;;IU)" //generic read/write. We will want to tune this to a specific group but that is not working with Windows 10 home at the moment
	System                    = "(A;;FA;;;SY)"
	BuiltinAdmins             = "(A;;FA;;;BA)"
	LocalService              = "(A;;FA;;;LS)"

	NF_GROUP_NAME = "NetFoundry Tunneler Users"
	TunName = "ZitiTUN"

	Ipv4ip = "169.254.1.1"
	Ipv4mask = 24
	Ipv4dns = "127.0.0.1" // use lo -- don't pass DNS queries through tunneler SDK

	// IPv6 CIDR fe80:6e66:7a69:7469::/64
	//   <link-local>: nf : zi : ti ::
	ipv6pfx = "fe80:6e66:7a69:7469"
	ipv6ip = "1"
	ipv6mask = 64
	Ipv6dns = "::1" // must be in "ipv6ip/ipv6mask" CIDR block

	STATUS_ENROLLED = "enrolled"
)
