// +build windows

package service

import (
	"fmt"

	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/debug"

	"wintun-testing/ziti-tunnel/config"
	"wintun-testing/ziti-tunnel/ipc"
	log2 "wintun-testing/ziti-tunnel/log"
)


type zitiService struct{}
const cmdsAccepted = svc.AcceptStop | svc.AcceptShutdown | svc.AcceptPauseAndContinue

func (m *zitiService) Execute(args []string, r <-chan svc.ChangeRequest, changes chan<- svc.Status) (ssec bool, errno uint32) {
	changes <- svc.Status{State: svc.StartPending}
	config.InitLogger("debug")

	control := make(chan string)
	go func() {
		err := ipc.SubMain(control, changes)
		if err != nil {
			log.Errorf("unexpected error received from ipc subroutine: %v", err)
		}
	}()
loop:
	for {
		select {
		case c := <-r:
			switch c.Cmd {
			case svc.Interrogate:
				changes <- c.CurrentStatus
				_ = log2.Elog.Info(ipc.InterrogateEvent, "interrogate request received")
				changes <- c.CurrentStatus
			case svc.Stop:
				_ = log2.Elog.Info(ipc.StopEvent, "issuing stop")
				control <- "stop"
				_ = log2.Elog.Info(ipc.StopEvent, "stop issued")
				break loop
			case svc.Shutdown:
				_ = log2.Elog.Info(ipc.ShutdownEvent, "issuing shutdown")
				control <- "stop"
				_ = log2.Elog.Info(ipc.ShutdownEvent, "shutdown issued")
				break loop
			case svc.Pause:
				changes <- svc.Status{State: svc.Paused, Accepts: cmdsAccepted}
				_ = log2.Elog.Info(ipc.PauseEvent, "request to pause service received. todo - unimplemented")
			case svc.Continue:
				changes <- svc.Status{State: svc.Running, Accepts: cmdsAccepted}
				_ = log2.Elog.Info(ipc.ContinueEvent, "request to continue service received. todo - unimplemented")
			default:
				_ = log2.Elog.Error(1, fmt.Sprintf("unexpected control request #%d", c))
			}
		}
	}

	log.Info("main loop exiting")
	changes <- svc.Status{State: svc.StopPending}
	return
}

func RunService(isDebug bool) {
	_ = log2.Elog.Info(StartEvent, fmt.Sprintf("starting %s service", SvcName))
	run := svc.Run
	if isDebug {
		run = debug.Run
	}
	err := run(SvcName, &zitiService{})
	if err != nil {
		_ = log2.Elog.Error(ErrorEvent, fmt.Sprintf("%s service failed: %v", SvcName, err))
		return
	}
	_ = log2.Elog.Info(StopEvent, fmt.Sprintf("%s service stopped", SvcName))
}