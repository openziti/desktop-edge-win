package globals

import (
	rotatelogs "github.com/lestrrat-go/file-rotatelogs"
	"github.com/michaelquigley/pfxlog"
	"github.com/sirupsen/logrus"
	"golang.org/x/sys/windows/svc/debug"
	"golang.org/x/sys/windows/svc/eventlog"
	"io"
	"os"
	"strings"
	"time"

	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/config"
)

var Elog debug.Log
var logger *logrus.Entry

func Logger() *logrus.Entry {
	if logger == nil {
		logger = pfxlog.Logger()
	}
	return logger
}

func InitLogger(logLevel logrus.Level) {
	logrus.SetLevel(logLevel)

	rl, _ := rotatelogs.New(config.LogFile() + ".%Y%m%d%H%M",
		rotatelogs.WithRotationTime(24 * time.Hour),
		rotatelogs.WithRotationCount(7),
		rotatelogs.WithLinkName(config.LogFile()))

	multiWriter := io.MultiWriter(rl, os.Stdout)

	logrus.SetOutput(multiWriter)
	logrus.SetFormatter(pfxlog.NewFormatter())
	logger.Infof("Logger initialized. Log file located at: %s", config.LogFile())
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

func InitEventLog(svcName string, interactive bool) {
	var err error
	if !interactive {
		Elog = debug.New(svcName)
	} else {
		Elog, err = eventlog.Open(svcName)
		if err != nil {
			return
		}
	}
}
