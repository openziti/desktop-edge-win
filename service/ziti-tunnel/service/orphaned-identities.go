package service

import (
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/config"
)

// After System update, the identity files are not getting copied to the config path
// So we are adding a function to scan for identities in the backtup location
func scanForIdentitiesPostWindowsUpdate() error {
	srcBackUpPaths := [2]string{"Windows.~BT\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\NetFoundry",
		"Windows.old\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\NetFoundry"}
	systemDrivePath := os.Getenv("SystemDrive")
	if systemDrivePath == "" {
		return nil
	}
	for _, srcPath := range srcBackUpPaths {
		sourcePath := filepath.Join(systemDrivePath, string(os.PathSeparator), srcPath)
		_, err := os.Stat(sourcePath)
		if err != nil {
			log.Debugf("Folder %s does not exist", sourcePath)
			continue
		}
		err = searchAndCopyFilesFromBackup(sourcePath)
		if err != nil {
			log.Debugf("Copy files from %s failed, %v", sourcePath, err)
		}
	}
	return nil
}

func searchAndCopyFilesFromBackup(srcPath string) error {
	err := filepath.Walk(srcPath, copyFilesFromBackUp)
	if err != nil {
		return err
	}
	return nil
}

func copyFilesFromBackUp(path string, f os.FileInfo, err error) error {
	if !f.IsDir() {
		log.Infof("Found: %s", path)
		destinationFile := filepath.Join(config.Path(), string(os.PathSeparator), f.Name())
		//check if the file is present in the destination folder
		_, err := os.Stat(destinationFile)
		if err == nil && !strings.Contains(f.Name(), ConfigFileName) {
			log.Debugf("File %s is already present in the config path and it is not %s, So not transfering", f.Name(), ConfigFileName)
			deleteFile(path)
			return nil
		}
		_, err = copy(path, destinationFile)
		if err != nil {
			log.Errorf("Error occurred while copying the Windows backup folder %s --> %s %v", f.Name(), destinationFile, err)
			return nil
		} else {
			log.Infof("Found backup file at %s. Restored this file to %s", path, destinationFile)
			deleteFile(path)
		}
	}
	return nil
}
func deleteFile(path string) {
	log.Infof("Removing file %s", path)
	err := os.Remove(path)
	if err != nil {
		log.Errorf("Error occured while removing the file %s, %v", path, err)
	} else {
		log.Infof("Removed file %s", path)
	}
}

func copy(src, dst string) (int64, error) {
	sourceFileStat, err := os.Stat(src)
	if err != nil {
		return 0, err
	}

	if !sourceFileStat.Mode().IsRegular() {
		return 0, errors.New(fmt.Sprintf("%s is not a regular file", src))
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
