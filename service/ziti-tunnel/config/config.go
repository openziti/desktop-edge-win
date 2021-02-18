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
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"strings"
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
func ScanAndCopyFromBackup() error {
	srcBackUpPaths := [2]string{"\\Windows.~BT", "\\Windows.old"}
	sysRoot := os.Getenv("SYSTEMROOT")
	if sysRoot == "" {
		return nil
	}
	for _, srcPath := range srcBackUpPaths {
		sourcePath := sysRoot + srcPath
		_, err := os.Stat(sourcePath)
		if err != nil {
			log.Debugf("Folder %s does not exist", sourcePath)
			continue
		}
		err = searchAndCopyFilesFromBackup(sourcePath)
		if err != nil {
			fmt.Printf("Copy files from %s failed, %v", sourcePath, err)
		}
	}
	return nil
}

func searchAndCopyFilesFromBackup(srcPath string) error {
	err := filepath.Walk(srcPath, visit)
	if err != nil {
		return err
	}
	return nil
}

func visit(path string, f os.FileInfo, err error) error {
	if strings.Contains(path, "config\\systemprofile\\AppData\\Roaming\\NetFoundry") && !f.IsDir() {
		log.Infof("\nFound: %s\n", path)
		destinationFile := filepath.Join(Path(), string(os.PathSeparator), f.Name())
		//check if the file is present in the destination folder
		_, err := os.Stat(destinationFile)
		if err == nil && !strings.Contains(f.Name(), "config.json") {
			fmt.Printf("File %s is already present in the config path and it is not config.json, So not transfering", f.Name())
			deleteFile(path)
			return nil
		}
		nBytes, err := copy(path, destinationFile)
		if err != nil {
			log.Errorf("Error occured while Copying the file %s, %v", f.Name(), err)
			return err
		} else {
			log.Infof("Copied the file %s, %d bytes transfered", f.Name(), nBytes)
			deleteFile(path)
		}
	}
	return nil
}
func deleteFile(path string) {
	fmt.Printf("\nRemoving file %s", path)
	err := os.Remove(path)
	if err != nil {
		fmt.Println(err)
	}
}

func copy(src, dst string) (int64, error) {
	sourceFileStat, err := os.Stat(src)
	if err != nil {
		return 0, err
	}

	if !sourceFileStat.Mode().IsRegular() {
		return 0, fmt.Errorf("%s is not a regular file", src)
	}

	source, err := os.Open(src)
	if err != nil {
		return 0, err
	}
	defer source.Close()

	destination, err := os.Create(dst)
	if err != nil {
		return 0, err
	}
	defer destination.Close()
	nBytes, err := io.Copy(destination, source)
	return nBytes, err
}
