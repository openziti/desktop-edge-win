// +build windows
package service

import (
	"fmt"
	"os"
	"path/filepath"
	"wintun-testing/ziti-tunnel/ipc"

	"golang.org/x/sys/windows/svc/eventlog"
	"golang.org/x/sys/windows/svc/mgr"
)

func InstallService() error {
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()

	s, err := m.OpenService(ipc.SvcStartName)
	if err == nil {
		s.Close()
		return fmt.Errorf("service %s already exists", ipc.SvcStartName)
	}

	exePath := os.Args[0]
	fullPath, err := filepath.Abs(exePath)
	if err != nil {
		return err
	}
	_, err = os.Stat(fullPath)
	if err != nil {
		return err
	}

	log.Infof("service installed using path: %s", fullPath)
	s, err = m.CreateService(ipc.SvcStartName, fullPath, mgr.Config{
		StartType:        mgr.StartAutomatic,
		DisplayName:      ipc.SvcName,
		Description:      ipc.SvcNameLong,
	})
	if err != nil {
		return err
	}
	defer s.Close()

	err = eventlog.InstallAsEventCreate(ipc.SvcStartName, eventlog.Error|eventlog.Warning|eventlog.Info)
	if err != nil {
		s.Delete()
		return fmt.Errorf("SetupEventLogSource() failed: %s", err)
	}
	return nil
}

func RemoveService() error {
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()
	s, err := m.OpenService(ipc.SvcStartName)
	if err != nil {
		return fmt.Errorf("service %s is not installed", ipc.SvcStartName)
	}
	defer s.Close()
	err = s.Delete()
	if err != nil {
		return err
	}
	err = eventlog.Remove(ipc.SvcStartName)
	if err != nil {
		return fmt.Errorf("RemoveEventLogSource() failed: %s", err)
	}
	return nil
}