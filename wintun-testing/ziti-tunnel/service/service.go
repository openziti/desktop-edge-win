// +build windows

package service

import (
	"fmt"

	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/debug"
)


type zitiService struct{}

func (m *zitiService) Execute(args []string, r <-chan svc.ChangeRequest, changes chan<- svc.Status) (ssec bool, errno uint32) {
	changes <- svc.Status{State: svc.StartPending}

	control := make(chan string)
	go func() {
		err := SubMain(control, changes)
		if err != nil {
			log.Errorf("the main loop exited with an unexpected error: %v", err)
		}
	}()
loop:
	for {
		select {
		case c := <-r:
			switch c.Cmd {
			case svc.Interrogate:
				changes <- c.CurrentStatus
				_ = Elog.Info(InterrogateEvent, "interrogate request received")
				changes <- c.CurrentStatus
			case svc.Stop:
				_ = Elog.Info(StopEvent, "issuing stop")
				control <- "stop"
				_ = Elog.Info(StopEvent, "stop issued")
				break loop
			case svc.Shutdown:
				_ = Elog.Info(ShutdownEvent, "issuing shutdown")
				control <- "stop"
				_ = Elog.Info(ShutdownEvent, "shutdown issued")
				break loop
			case svc.Pause:
				changes <- svc.Status{State: svc.Paused, Accepts: cmdsAccepted}
				_ = Elog.Info(PauseEvent, "request to pause service received. todo - unimplemented")
			case svc.Continue:
				changes <- svc.Status{State: svc.Running, Accepts: cmdsAccepted}
				_ = Elog.Info(ContinueEvent, "request to continue service received. todo - unimplemented")
			default:
				_ = Elog.Error(1, fmt.Sprintf("unexpected control request #%d", c))
			}
		}
	}

	log.Info("main loop exiting")
	changes <- svc.Status{State: svc.StopPending}
	return
}

func RunService(isDebug bool) {
	_ = Elog.Info(StartEvent, fmt.Sprintf("starting %s service", SvcStartName))
	run := svc.Run
	if isDebug {
		run = debug.Run
	}
	err := run(SvcStartName, &zitiService{})

	if err != nil {
		_ = Elog.Error(ErrorEvent, fmt.Sprintf("%s service failed: %v", SvcStartName, err))
		return
	}
	_ = Elog.Info(StopEvent, fmt.Sprintf("%s service stopped", SvcStartName))
}