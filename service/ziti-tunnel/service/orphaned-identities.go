package service

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/config"
)

func ScanAndCopyFromBackup() error {
	srcBackUpPaths := [2]string{"\\Windows.~BT", "\\Windows.old"}
	sysRoot := os.Getenv("SystemDrive")
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
		destinationFile := filepath.Join(config.Path(), string(os.PathSeparator), f.Name())
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
