package config

import (
	"io"
	"os"
	"strings"

	"github.com/michaelquigley/pfxlog"
	"github.com/sirupsen/logrus"
	"gopkg.in/natefinch/lumberjack.v2"
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

func ParseLevel(lvl string) logrus.Level {
	switch strings.ToLower(lvl) {
	case "panic":
		return logrus.PanicLevel
	case "fatal":
		return logrus.FatalLevel
	case "error":
		return logrus.ErrorLevel
	case "warn", "warning":
		return logrus.WarnLevel
	case "info":
		return logrus.InfoLevel
	case "debug":
		return logrus.DebugLevel
	case "trace":
		return logrus.TraceLevel
	default:
		logrus.Warnf("level not recognized: %s. Using Info", lvl)
		return logrus.InfoLevel
	}
}

func InitLogger(level string) {
	logLevel := ParseLevel(level)
	logrus.SetLevel(logLevel)

	_ = os.Remove(LogFile()) //reset the log on startup
	multiWriter := io.MultiWriter(&lumberjack.Logger{
		Filename:   LogFile(),
		MaxSize:    1, // megabytes
		MaxBackups: 2,
		MaxAge:     30,   //days
		Compress:   false, // disabled by default
	}, os.Stdout)

	logrus.SetOutput(multiWriter)
	logrus.SetFormatter(pfxlog.NewFormatter())
	pfxlog.Logger().Infof("Logger initialized. Log file located at: %s", LogFile())
}
