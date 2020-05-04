package service

import (
	"github.com/tv42/topic"
	"golang.org/x/sys/windows/svc"
	"sync"
)

var pipeBase = `\\.\pipe\NetFoundry\tunneler\`
//var tunStatus = RuntimeState{}

//var state = dto.TunnelStatusa{}
var rts = RuntimeState{}
var interrupt chan struct{}

var wg sync.WaitGroup
var connections int
var Debug bool

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
)

func pipeName(path string) string {
	if !Debug {
		return pipeBase + path
	} else {
		return pipeBase + `debug\` + path
	}
}

var top = topic.New()

func ipcPipeName() string {
	return pipeName("ipc")
}
func logsPipeName() string {
	return pipeName("logs")
}
func eventsPipeName() string {
	return pipeName("events")
}
