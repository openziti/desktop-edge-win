package config

import (
	"os"
)

func File() string {
	return Path() + "config.json"
}
func Path() string {
	path, _ := os.UserConfigDir()
	return path + string(os.PathSeparator) + "NetFoundry" + string(os.PathSeparator)
}
func LogFile() string {
	return Path() + "ziti-tunneler.log"
}
