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
var withFilenameLogger = logrus.New()
var noFilenamelogger = logrus.New()
var loggerInitialized = false
var baseImportPath = "github.com/openziti/desktop-edge-win/service/"
var baseFilePath = config.ExecutablePath()

func init() {
	/*
	 * https://github.com/sirupsen/logrus/issues/496
	 */
	handle := syscall.Handle(os.Stdout.Fd())
	kernel32DLL := syscall.NewLazyDLL("kernel32.dll")
	setConsoleModeProc := kernel32DLL.NewProc("SetConsoleMode")
	setConsoleModeProc.Call(uintptr(handle), 0x0001|0x0002|0x0004)


	with := &dateFormatterNoFilename{
	}
	/*with := &dateFormatterWithFilename{
	}*/
	with.dateFormatter.timeFormat = UTCFormat()
	withFilenameLogger.SetFormatter(with)

	without := &dateFormatterNoFilename{
	}
	without.dateFormatter.timeFormat = UTCFormat()
	noFilenamelogger.SetFormatter(without)
}

func Logger() *logrus.Logger {
	return withFilenameLogger
}

func NoFilenameLogger() *logrus.Logger {
	return noFilenamelogger
}

func SetLoggingLevel(goLevel logrus.Level){
	withFilenameLogger.SetLevel(goLevel)
	noFilenamelogger.SetLevel(goLevel)
}

func InitLogger(level logrus.Level) {
	initLogger(withFilenameLogger, level)
	initLogger(noFilenamelogger, level)
}
func initLogger(logger *logrus.Logger, level logrus.Level) {
	logger.SetLevel(level)

	logger.SetReportCaller(true)

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
	case "verbose":
		return logrus.TraceLevel, 5
	case "trace":
		return logrus.TraceLevel, 6
	default:
		noFilenamelogger.Warnf("level not recognized: [%s]. Using Info", lvl)
		return logrus.InfoLevel, 3
	}
}

type dateFormatter struct {
	timeFormat string
}
type dateFormatterWithFunction struct {
	dateFormatter
}
type dateFormatterWithFilename struct {
	dateFormatter
}
type dateFormatterNoFilename struct {
	dateFormatter
}

func UTCFormat() string {
	return "2006-01-02T15:04:05.000"
}

func toLevel(entry *logrus.Entry) string {
	switch entry.Level {
	case logrus.PanicLevel:
		return panicColor
	case logrus.FatalLevel:
		return fatalColor
	case logrus.ErrorLevel:
		return errorColor
	case logrus.WarnLevel:
		return warnColor
	case logrus.InfoLevel:
		return infoColor
	case logrus.DebugLevel:
		return debugColor
	case logrus.TraceLevel:
		return traceColor
	default:
		return infoColor
	}
}

func (f *dateFormatterWithFunction) Format(entry *logrus.Entry) ([]byte, error) {
	level := toLevel(entry)

	return []byte(fmt.Sprintf("[%sZ] %s\t%s:%d\t%s\n",
			time.Now().UTC().Format(f.timeFormat),
			level,
			strings.ReplaceAll(entry.Caller.Function, baseImportPath, ""),
			entry.Caller.Line,
			entry.Message),
		),
		nil
}
func (f *dateFormatterWithFilename) Format(entry *logrus.Entry) ([]byte, error) {
	level := toLevel(entry)

	return []byte(fmt.Sprintf("[%sZ] %s\t%s:%d\t%s\n",
			time.Now().UTC().Format(f.timeFormat),
			level,
			strings.ReplaceAll(entry.Caller.File, baseFilePath, ""),
			entry.Caller.Line,
			entry.Message),
		),
		nil
}
func (f *dateFormatterNoFilename) Format(entry *logrus.Entry) ([]byte, error) {
	level := toLevel(entry)

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