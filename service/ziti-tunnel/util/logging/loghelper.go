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

package logging

import (
	"fmt"
	rotatelogs "github.com/lestrrat-go/file-rotatelogs"
	"github.com/mgutz/ansi"
	"github.com/sirupsen/logrus"
	"golang.org/x/sys/windows/svc/debug"
	"io"
	"os"
	"strings"
	"syscall"
	"time"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/config"
)

var Elog debug.Log
var logger = logrus.New()
var loggerInitialized = false
var tf = "2006-01-02T15:04:05.000"
var tfl = len(tf)


func init() {
	/*
	 * https://github.com/sirupsen/logrus/issues/496
	 */
	handle := syscall.Handle(os.Stdout.Fd())
	kernel32DLL := syscall.NewLazyDLL("kernel32.dll")
	setConsoleModeProc := kernel32DLL.NewProc("SetConsoleMode")
	setConsoleModeProc.Call(uintptr(handle), 0x0001|0x0002|0x0004)

	f := &dateFormatter{
		timeFormat: tf,
	}
	logger.SetFormatter(f)
}

func Logger() *logrus.Logger {
	return logger
}

func InitLogger(level string) {
	l, _ := ParseLevel(level)
	logger.SetLevel(l)

	rl, _ := rotatelogs.New(config.LogFile() + ".%Y%m%d%H%M.log",
		rotatelogs.WithRotationTime(24 * time.Hour),
		rotatelogs.WithRotationCount(7),
		rotatelogs.WithLinkName(config.LogFile()))

	multiWriter := io.MultiWriter(rl, os.Stdout)

	logger.SetOutput(multiWriter)
	if !loggerInitialized {
		logger.Infof("============================================================================")
		logger.Infof("Logger initialization")
		logger.Infof("	- initialized at   : %v", time.Now())
		logger.Infof("	- log file location: %s", config.LogFile())
		logger.Infof("============================================================================")
		loggerInitialized = true
	}
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

type dateFormatter struct {
	timeFormat string
}

func (f *dateFormatter) Format(entry *logrus.Entry) ([]byte, error) {
	var level string
	switch entry.Level {
	case logrus.PanicLevel:
		level = panicColor
	case logrus.FatalLevel:
		level = fatalColor
	case logrus.ErrorLevel:
		level = errorColor
	case logrus.WarnLevel:
		level = warnColor
	case logrus.InfoLevel:
		level = infoColor
	case logrus.DebugLevel:
		level = debugColor
	case logrus.TraceLevel:
		level = traceColor
	}

	return []byte(fmt.Sprintf("[%sZ] %s\t%s\n",
			time.Now().UTC().Format(f.timeFormat),
			level,
			entry.Message),
		),
		nil
}

var panicColor = ansi.Red + "PANIC" + ansi.DefaultFG
var fatalColor = ansi.Red + "FATAL" + ansi.DefaultFG
var errorColor = ansi.Red + "ERROR" + ansi.DefaultFG
var warnColor = ansi.Yellow + " WARN" + ansi.DefaultFG
var infoColor = ansi.White + " INFO" + ansi.DefaultFG
var debugColor = ansi.Blue + "DEBUG" + ansi.DefaultFG
var traceColor = ansi.LightBlack + "TRACE" + ansi.DefaultFG