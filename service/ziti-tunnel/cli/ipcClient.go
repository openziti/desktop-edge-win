package cli

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

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"strings"
	"time"

	"github.com/Microsoft/go-winio"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
)

type fetchStatusFromRTS func([]string, *dto.TunnelStatus, map[string]bool) dto.Response
type fetchResponseFromRTS func([]string, dto.Response, map[string]bool) dto.Response

func sendMessagetoPipe(ipcPipeConn net.Conn, commandMsg *dto.CommandMsg, args []string) error {
	writer := bufio.NewWriter(ipcPipeConn)
	enc := json.NewEncoder(writer)

	err := enc.Encode(commandMsg)
	if err != nil {
		log.Error("could not encode or writer list identities message, %v", err)
		return err
	}

	log.Debug("Message sent to ipc pipe")

	writer.Flush()

	return nil
}

func readMessageFromPipe(ipcPipeConn net.Conn, readDone chan bool, fn fetchStatusFromRTS, responseFn fetchResponseFromRTS, args []string, flags map[string]bool) {

	reader := bufio.NewReader(ipcPipeConn)
	msg, err := reader.ReadString('\n')

	if err != nil {
		log.Error(err)
		readDone <- false
		return
	}

	if len(args) == 0 {
		args = append(args, "all")
	}

	dec := json.NewDecoder(strings.NewReader(msg))

	if fn != nil {
		var tunnelStatus dto.ZitiTunnelStatus
		if err := dec.Decode(&tunnelStatus); err == io.EOF {
			readDone <- false
			return
		} else if err != nil {
			log.Fatal(err)
			readDone <- false
			return
		}

		if tunnelStatus.Status != nil {
			responseMsg := fn(args, tunnelStatus.Status, flags)
			if responseMsg.Code == service.SUCCESS {
				fmt.Println(responseMsg.Payload.(string))
				fmt.Println(responseMsg.Message)
				readDone <- true
				return
			} else {
				if responseMsg.Error != "" {
					log.Error(responseMsg.Error)
				} else {
					log.Info(responseMsg.Message)
				}
			}
		} else {
			log.Errorf("Ziti tunnel retuned status, %v", tunnelStatus)
		}
	}

	if responseFn != nil {
		var response dto.Response
		if err := dec.Decode(&response); err == io.EOF {
			readDone <- false
			return
		} else if err != nil {
			log.Fatal(err)
			readDone <- false
			return
		}

		if response.Message != "" {
			responseMsg := responseFn(args, response, flags)
			if responseMsg.Code == service.SUCCESS {
				if responseMsg.Payload != nil {
					log.Infof("Payload : %v", responseMsg.Payload)
				}
				log.Infof("Message : %s", responseMsg.Message)
				readDone <- true
				return
			} else {
				if responseMsg.Error != "" {
					log.Error(responseMsg.Error)
				} else {
					log.Info(responseMsg.Message)
				}
			}
		} else {
			log.Errorf("Ziti tunnel retuned nil response message, %v", response)
		}
	}

	readDone <- false
	return
}

func GetDataFromIpcPipe(commandMsg *dto.CommandMsg, fn fetchStatusFromRTS, responseFn fetchResponseFromRTS, args []string, flags map[string]bool) bool {
	log.Infof("Command %s with args %s", commandMsg.Function, args)

	log.Debug("Connecting to pipe")
	timeout := 2000 * time.Millisecond
	ipcPipeConn, err := winio.DialPipe(service.IpcPipeName(), &timeout)
	defer closeConn(ipcPipeConn)

	if err != nil {
		log.Errorf("Connection to ipc pipe is not established, %v", err)
		log.Fatal("Ziti Desktop Edge app may not be running")
		return false
	}
	readDone := make(chan bool)
	defer close(readDone) // ensure that goroutine exits

	go readMessageFromPipe(ipcPipeConn, readDone, fn, responseFn, args, flags)

	err = sendMessagetoPipe(ipcPipeConn, commandMsg, args)
	if err != nil {
		log.Errorf("Message is not sent to ipc pipe, %v", err)
		return false
	}

	log.Debugf("Connection to ipc pipe is established - %s and remote address %s", ipcPipeConn.LocalAddr().String(), ipcPipeConn.RemoteAddr().String())

	status := <-readDone
	log.Debug("read finished normally")
	return status
}

// monitor ipc messages

func GetDataFromMonitorIpcPipe(actionMessage *dto.ActionEvent, args []string, flags map[string]bool) bool {
	log.Infof("Command %s with args %s", actionMessage.StatusEvent.Op, args)

	log.Debug("Connecting to pipe")
	timeout := 2000 * time.Millisecond
	ipcPipeConn, err := winio.DialPipe(monitorIpcPipe, &timeout)
	defer closeConn(ipcPipeConn)

	if err != nil {
		log.Errorf("Connection to monitor ipc pipe is not established, %v", err)
		log.Errorf("Ziti Update service may not be running")
		return false
	}
	readDone := make(chan bool)
	defer close(readDone) // ensure that goroutine exits

	go readMessageFromMonitorPipe(ipcPipeConn, readDone, args, flags)

	err = sendMessageToMonitorPipe(ipcPipeConn, actionMessage, args)
	if err != nil {
		log.Errorf("Message is not sent to monitor ipc pipe, %v", err)
		return false
	}

	log.Debugf("Connection to monitor ipc pipe is established - %s and remote address %s", ipcPipeConn.LocalAddr().String(), ipcPipeConn.RemoteAddr().String())

	status := <-readDone
	log.Debug("read finished normally")
	return status
}

func sendMessageToMonitorPipe(ipcPipeConn net.Conn, actionMessage *dto.ActionEvent, args []string) error {
	writer := bufio.NewWriter(ipcPipeConn)
	enc := json.NewEncoder(writer)

	err := enc.Encode(actionMessage)
	if err != nil {
		log.Error("could not encode or write response message, %v", err)
		return err
	}

	log.Debug("Message sent to monitor ipc pipe")

	writer.Flush()

	return nil
}

func readMessageFromMonitorPipe(ipcPipeConn net.Conn, readDone chan bool, args []string, flags map[string]bool) {

	reader := bufio.NewReader(ipcPipeConn)
	msg, err := reader.ReadString('\n')

	if err != nil {
		log.Error(err)
		readDone <- false
		return
	}

	dec := json.NewDecoder(strings.NewReader(msg))

	var monitorServiceResponse dto.MonitorServiceResponse
	if err := dec.Decode(&monitorServiceResponse); err == io.EOF {
		readDone <- false
		return
	} else if err != nil {
		log.Fatal(err)
		readDone <- false
		return
	}

	if monitorServiceResponse.Code == service.SUCCESS {
		log.Infof("Feedback file %s is created", monitorServiceResponse.Message)
		readDone <- true
		return
	} else {
		log.Error(monitorServiceResponse.Error)
	}

	readDone <- false
	return
}

func closeConn(conn net.Conn) {
	if conn != nil {
		err := conn.Close()
		if err != nil {
			log.Warnf("abnormal error while closing connection. %v", err)
		}
	}
}
