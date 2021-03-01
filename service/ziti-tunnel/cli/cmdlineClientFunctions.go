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
	"io"
	"net"
	"strings"
	"time"

	"github.com/Microsoft/go-winio"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
)

type fetchFromRTS func([]string, *dto.TunnelStatus, map[string]bool) dto.Response

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

func readMessageFromPipe(ipcPipeConn net.Conn, readDone chan struct{}, fn fetchFromRTS, args []string, flags map[string]bool) {

	reader := bufio.NewReader(ipcPipeConn)
	msg, err := reader.ReadString('\n')

	if err != nil {
		log.Error(err)
		return
	}

	if len(args) == 0 {
		args = append(args, "all")
	}

	dec := json.NewDecoder(strings.NewReader(msg))
	var tunnelStatus dto.ZitiTunnelStatus
	if err := dec.Decode(&tunnelStatus); err == io.EOF {
		return
	} else if err != nil {
		log.Fatal(err)
	}

	if tunnelStatus.Status != nil {
		responseMsg := fn(args, tunnelStatus.Status, flags)
		if responseMsg.Code == service.SUCCESS {
			log.Info("\n" + responseMsg.Payload.(string) + "\n" + responseMsg.Message)
		} else {
			log.Info(responseMsg.Error)
		}
	} else {
		log.Errorf("Ziti tunnel retuned nil status")
	}

	readDone <- struct{}{}
	return
}

//GetIdentities is to fetch identities through cmdline
func GetIdentities(args []string, flags map[string]bool) {
	getDataFromIpcPipe(&GET_STATUS, GetIdentitiesFromRTS, args, flags)
}

//GetServices is to fetch services through cmdline
func GetServices(args []string, flags map[string]bool) {
	getDataFromIpcPipe(&GET_STATUS, GetServicesFromRTS, args, flags)
}

func getDataFromIpcPipe(commandMsg *dto.CommandMsg, fn fetchFromRTS, args []string, flags map[string]bool) {
	log.Infof("fetching identities through cmdline...%s", args)

	log.Debug("Connecting to pipe")
	timeout := 2000 * time.Millisecond
	ipcPipeConn, err := winio.DialPipe(service.IpcPipeName(), &timeout)
	defer closeConn(ipcPipeConn)

	if err != nil {
		log.Errorf("Connection to ipc pipe is not established, %v", err)
		return
	}
	readDone := make(chan struct{})
	defer close(readDone) // ensure that goroutine exits

	go readMessageFromPipe(ipcPipeConn, readDone, fn, args, flags)

	err = sendMessagetoPipe(ipcPipeConn, commandMsg, args)
	if err != nil {
		log.Errorf("Message is not sent to ipc pipe, %v", err)
		return
	}

	log.Debugf("Connection to ipc pipe is established - %s and remote address %s", ipcPipeConn.LocalAddr().String(), ipcPipeConn.RemoteAddr().String())

	<-readDone
	log.Debug("read finished normally")
}

func closeConn(conn net.Conn) {
	err := conn.Close()
	if err != nil {
		log.Warnf("abnormal error while closing connection. %v", err)
	}
}
