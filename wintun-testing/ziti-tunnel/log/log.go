package log

import (
	"io"
	"os"
	"strings"

	"github.com/michaelquigley/pfxlog"
	"github.com/sirupsen/logrus"
	"golang.org/x/sys/windows/svc/debug"
	"golang.org/x/sys/windows/svc/eventlog"
	"gopkg.in/natefinch/lumberjack.v2"

	"wintun-testing/ziti-tunnel/config"
	"wintun-testing/ziti-tunnel/ipc"
)

var Logger = *pfxlog.Logger()
var Elog debug.Log

func InitLogger(level string) {
	logLevel := ParseLevel(level)
	logrus.SetLevel(logLevel)

	_ = os.Remove(config.LogFile()) //reset the log on startup
	multiWriter := io.MultiWriter(&lumberjack.Logger{
		Filename:   config.LogFile(),
		MaxSize:    1, // megabytes
		MaxBackups: 2,
		MaxAge:     30,    //days
		Compress:   false, // disabled by default
	}, os.Stdout)

	logrus.SetOutput(multiWriter)
	logrus.SetFormatter(pfxlog.NewFormatter())
	pfxlog.Logger().Infof("Logger initialized. Log file located at: %s", config.LogFile())
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

func InitEventLog(interactive bool) {
	var err error
	if !interactive {
		Elog = debug.New(ipc.SvcName)
	} else {
		Elog, err = eventlog.Open(ipc.SvcName)
		if err != nil {
			return
		}
	}
}
