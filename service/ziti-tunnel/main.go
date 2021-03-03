// +build windows

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
package main

import (
	"fmt"
	"os"
	"strings"

	"github.com/openziti/desktop-edge-win/service/cziti"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/logging"
	"github.com/sirupsen/logrus"
	"golang.org/x/sys/windows/svc/debug"
	"golang.org/x/sys/windows/svc/eventlog"

	commandline "github.com/openziti/desktop-edge-win/service/ziti-tunnel/cmd"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
	"golang.org/x/sys/windows/svc"
)

var log = logging.Logger()

func main() {
	service.Version = dto.ServiceVersion{
		Version:   Version,
		Revision:  Revision,
		BuildDate: BuildDate,
	}
	cziti.Version = service.Version

	// passing no arguments is an indicator that this is expecting to be run 'as a service'.
	// using arg count instead of svc.IsAnInteractiveSession() as svc.IsAnInteractiveSession()
	// seems to return false even when run in an interactive shell as via `psexec -i -s cmd.exe`
	hasArgs := len(os.Args) > 1

	var err error
	if hasArgs {
		logging.Elog = debug.New(service.SvcName)
	} else {
		logging.InitLogger(logrus.InfoLevel)
		logging.Elog, err = eventlog.Open(service.SvcName)
		if err != nil {
			return
		}
		log.Infof("running as service. version: %s", version())
		service.RunService(false)
		log.Info("service has completed")
		return
	}

	// all this code below is to either support installing/removing the service or testing it
	if len(os.Args) < 2 {
		usage("no command specified")
	}

	defer logging.Elog.Close()

	elog := logging.Elog

	cmd := strings.ToLower(os.Args[1])

	switch cmd {
	case "debug":
		logging.InitLogger(logrus.InfoLevel)
		log.Infof("running interactively: %s", version())
		service.Debug = true
		service.RunService(true)
		return
	case "install":
		elog.Info(service.InstallEvent, "installing service: "+service.SvcName)
		err = service.InstallService()
	case "remove":
		elog.Info(service.InstallEvent, "removing service: "+service.SvcName)
		err = service.RemoveService()
	case "start":
		log.Infof("starting as service: %s", version())
		err = service.StartService()
	case "stop":
		log.Infof("stopping service: %s", version())
		err = service.ControlService(svc.Stop, svc.Stopped)
	case "pause":
		log.Infof("pausing service: %s", version())
		err = service.ControlService(svc.Pause, svc.Paused)
	case "continue":
		log.Infof("continuing service: %s", version())
		err = service.ControlService(svc.Continue, svc.Running)
	case "version":
		fmt.Println(version())
	case "list":
		commandline.Execute()
	case "identity":
		commandline.Execute()
	case "loglevel":
		commandline.Execute()
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
			"       install, remove, debug, start, stop, pause, continue, list, identity, loglevel or version.\n",
		errmsg, os.Args[0])
	os.Exit(2)
}

func version() string {
	return fmt.Sprintf("%v version: %v, revision: %v, branch: %v, build-by: %v, built-on: %v",
		os.Args[0], Version, Revision, Branch, BuildUser, BuildDate)
}
