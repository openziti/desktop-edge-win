// +build windows
package main

import (
	"fmt"
	"os"
	"strings"

	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/debug"
	"golang.org/x/sys/windows/svc/eventlog"

	"wintun-testing/ziti-tunnel/ipc"
	pkglog "wintun-testing/ziti-tunnel/log"
	"wintun-testing/ziti-tunnel/service"
)

var log = pkglog.Logger
var elog = pkglog.Elog

func main() {
	pkglog.InitLogger("debug")

	isIntSess, err := svc.IsAnInteractiveSession()
	if err != nil {
		log.Fatalf("failed to determine if we are running in an interactive session: %v", err)
	}

	pkglog.InitEventLog(isIntSess)
	defer pkglog.Elog.Close()

	// if not interactive that means this is running as a service via services
	if !isIntSess {
		err = os.Setenv("ZITI_LOG", "5")
		if err != nil {
			log.Warnf("error setting env: %v", err)
		}else {
			log.Warn("ziti_log set to 5")
			log.Warn("ziti_log set to 5")
			log.Warn("ziti_log set to 5")
			log.Warn("ziti_log set to 5")
		}
		log.Info("service is starting")
		service.RunService(false)
		log.Info("service has stopped")
		return
	}

	// all this code below is to either support installing/removing the service or testing it
	if len(os.Args) < 2 {
		usage("no command specified")
	}

	if !isIntSess {
		elog = debug.New(ipc.SvcName)
	} else {
		elog, err = eventlog.Open(ipc.SvcName)
		if err != nil {
			return
		}
	}
	cmd := strings.ToLower(os.Args[1])
	switch cmd {
	case "debug":
		service.RunService(true)
		return
	case "install":
		elog.Info(service.InstallEvent, "installing service: "+ipc.SvcName)
		err = service.InstallService()
	case "remove":
		elog.Info(service.InstallEvent, "removing service: "+ipc.SvcName)
		err = service.RemoveService()
	case "start":
		err = service.StartService()
	case "stop":
		err = service.ControlService(svc.Stop, svc.Stopped)
	case "pause":
		err = service.ControlService(svc.Pause, svc.Paused)
	case "continue":
		err = service.ControlService(svc.Continue, svc.Running)
	default:
		usage(fmt.Sprintf("invalid command %s", cmd))
	}
	if err != nil {
		elog.Error(10, fmt.Sprintf("failed to %s %s: %v", cmd, ipc.SvcName, err))
		log.Fatalf("failed to %s %s: %v", cmd, ipc.SvcName, err)
	}
	return
}

func usage(errmsg string) {
	fmt.Fprintf(os.Stderr,
		"%s\n\n"+
			"usage: %s <command>\n"+
			"       where <command> is one of\n"+
			"       install, remove, debug, start, stop, pause or continue.\n",
		errmsg, os.Args[0])
	os.Exit(2)
}
