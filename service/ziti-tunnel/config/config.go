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

package config

import (
	"log"
	"os"
	"path/filepath"
)

func ExecutablePath() string {
	fi, err := os.Executable()
	if err != nil {
		log.Panicf("COULD NOT STAT os.executable! %v", err)
	}
	dir := filepath.Dir(fi)
	return dir
}
func File() string {
	return Path() + "config.json"
}
func Path() string {
	path, _ := os.UserConfigDir()
	return path + string(os.PathSeparator) + "NetFoundry" + string(os.PathSeparator)
}
func LogFile() string {
	return filepath.Join(LogsPath(), "ziti-tunneler.log")
}
func LogsPath() string {
	return filepath.Join(ExecutablePath(), "logs", "service")
}
func BackupFile() string {
	return File() + ".backup"
}
func EnsureConfigFolder() error {
	return ensureFolder(Path())
}
func EnsureLogsFolder() error {
	return ensureFolder(LogsPath())
}
func ensureFolder(path string) error {
	if _, err := os.Stat(path); os.IsNotExist(err) {
		mkdirerr := os.Mkdir(path, 0644)
		if mkdirerr != nil {
			return mkdirerr
		}
	}
	return nil
}
