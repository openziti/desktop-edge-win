// +build windows
package service

import (
	"fmt"
	"os"
	"path/filepath"

	"golang.org/x/sys/windows/svc/eventlog"
	"golang.org/x/sys/windows/svc/mgr"
)

func InstallService() error {
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()

	s, err := m.OpenService(SvcName)
	if err == nil {
		s.Close()
		return fmt.Errorf("service %s already exists", SvcName)
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
	s, err = m.CreateService(SvcName, fullPath, mgr.Config{
		StartType:        mgr.StartAutomatic,
		DisplayName:      SvcName,
		Description:      SvcNameLong,
	})
	if err != nil {
		return err
	}
	defer s.Close()

	err = eventlog.InstallAsEventCreate(SvcName, eventlog.Error|eventlog.Warning|eventlog.Info)
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
	s, err := m.OpenService(SvcName)
	if err != nil {
		return fmt.Errorf("service %s is not installed", SvcName)
	}
	defer s.Close()
	err = s.Delete()
	if err != nil {
		return err
	}
	err = eventlog.Remove(SvcName)
	if err != nil {
		return fmt.Errorf("RemoveEventLogSource() failed: %s", err)
	}
	return nil
}