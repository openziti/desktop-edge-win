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

package globals

import (
	rotatelogs "github.com/lestrrat-go/file-rotatelogs"
	"github.com/michaelquigley/pfxlog"
	"github.com/openziti/desktop-edge-win/service/cziti"
	"github.com/sirupsen/logrus"
	"golang.org/x/sys/windows/svc/debug"
	"io"
	"os"
	"strings"
	"time"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/config"
)

var Elog debug.Log
var logger *logrus.Entry

func Logger() *logrus.Entry {
	if logger == nil {
		logger = pfxlog.Logger()
	}
	return logger
}

var loggerInitialized = false

func InitLogger(level string) {
	l, _ := ParseLevel(level)
	logrus.SetLevel(l)

	rl, _ := rotatelogs.New(config.LogFile() + ".%Y%m%d%H%M.log",
		rotatelogs.WithRotationTime(24 * time.Hour),
		rotatelogs.WithRotationCount(7),
		rotatelogs.WithLinkName(config.LogFile()))

	multiWriter := io.MultiWriter(rl, os.Stdout)

	logrus.SetOutput(multiWriter)
	logrus.SetFormatter(pfxlog.NewFormatter())
	if !loggerInitialized {
		logger.Infof("============================================================================")
		logger.Infof("Logger initialization")
		logger.Infof("	- initialized at   : %v", time.Now())
		logger.Infof("	- log file location: %s", config.LogFile())
		logger.Infof("============================================================================")
		loggerInitialized = true
	}
}

func SetLogLevel(goLevel logrus.Level, cLevel int) {
	logrus.Infof("Setting logger levels to %s", goLevel)
	logrus.SetLevel(goLevel)
	cziti.SetLogLevel(cLevel)
}

func ParseLevel(lvl string) (logrus.Level, int) {
	switch strings.ToLower(lvl) {
	case "panic":
		return logrus.PanicLevel, 0
	case "fatal":
		return logrus.FatalLevel, 0
	case "error":
		return logrus.ErrorLevel, 1
	case "warn", "warning":
		return logrus.WarnLevel, 2
	case "info":
		return logrus.InfoLevel, 3
	case "debug":
		return logrus.DebugLevel, 4
	case "trace":
		return logrus.TraceLevel, 5
	case "verbose":
		return logrus.TraceLevel, 6
	default:
		logrus.Warnf("level not recognized: [%s]. Using Info", lvl)
		return logrus.InfoLevel, 3
	}
}