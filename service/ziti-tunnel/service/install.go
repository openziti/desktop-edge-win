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

package service

import (
	"fmt"
	"golang.org/x/sys/windows/svc/eventlog"
	"golang.org/x/sys/windows/svc/mgr"
	"os"
	"path/filepath"
)

func InstallService() error {
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()

	s, err := m.OpenService(SvcStartName)
	if err == nil {
		s.Close()
		return fmt.Errorf("service %s already exists", SvcStartName)
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
	s, err = m.CreateService(SvcStartName, fullPath, mgr.Config{
		StartType:        mgr.StartAutomatic,
		DisplayName:      SvcName,
		Description:      SvcNameLong,
	})
	if err != nil {
		return err
	}
	defer s.Close()

	err = eventlog.InstallAsEventCreate(SvcStartName, eventlog.Error|eventlog.Warning|eventlog.Info)
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
	s, err := m.OpenService(SvcStartName)
	if err != nil {
		return fmt.Errorf("service %s is not installed", SvcStartName)
	}
	defer s.Close()
	err = s.Delete()
	if err != nil {
		return err
	}
	err = eventlog.Remove(SvcStartName)
	if err != nil {
		return fmt.Errorf("RemoveEventLogSource() failed: %s", err)
	}
	return nil
}