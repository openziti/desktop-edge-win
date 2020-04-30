// +build windows
package main

import (
	"fmt"
	"os"
	"strings"
	"wintun-testing/ziti-tunnel/globals"

	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/debug"
	"golang.org/x/sys/windows/svc/eventlog"

	"wintun-testing/ziti-tunnel/service"
)

var log = globals.Logger()

func main() {
	globals.InitLogger("debug")

	isIntSess, err := svc.IsAnInteractiveSession()
	if err != nil {
		log.Fatalf("failed to determine if we are running in an interactive session: %v", err)
	}

	// if not interactive that means this is probably running as a service via services
	hasArgs := len(os.Args) > 1
	if !isIntSess && !hasArgs {
		service.RunService(false)
		log.Info("service has completed")
		return
	}

	// all this code below is to either support installing/removing the service or testing it
	if len(os.Args) < 2 {
		usage("no command specified")
	}

	if !isIntSess {
		globals.Elog = debug.New(service.SvcName)
	} else {
		globals.Elog, err = eventlog.Open(service.SvcName)
		if err != nil {
			return
		}
	}
	defer globals.Elog.Close()

	elog := globals.Elog

	cmd := strings.ToLower(os.Args[1])
	switch cmd {
	case "debug":
		service.RunService(true)
		return
	case "install":
		elog.Info(service.InstallEvent, "installing service: "+service.SvcName)
		err = service.InstallService()
	case "remove":
		elog.Info(service.InstallEvent, "removing service: "+service.SvcName)
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
		elog.Error(10, fmt.Sprintf("failed to %s %s: %v", cmd, service.SvcName, err))
		log.Fatalf("failed to %s %s: %v", cmd, service.SvcName, err)
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
