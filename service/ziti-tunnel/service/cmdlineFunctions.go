package service

import (
	"bufio"
	"encoding/json"
	"io"
	"net"
	"strings"
	"time"

	"github.com/Microsoft/go-winio"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

func connectToIPCPipe() (net.Conn, error) {
	log.Info("Connecting to pipe")
	timeout := 2000 * time.Millisecond
	ipcPipeConn, err := winio.DialPipe(ipcPipeName(), &timeout)
	defer log.Info("Closing ipc pipe connection")
	defer closeConn(ipcPipeConn)
	if err == nil {
		log.Info("Connected to ipc pipe")
		return ipcPipeConn, nil
	}
	return nil, err

}

func sendMessagetoPipe(ipcPipeConn net.Conn) error {
	writer := bufio.NewWriter(ipcPipeConn)
	enc := json.NewEncoder(writer)

	var payload = map[string]interface{}{
		"args": "all",
	}
	LIST_IDENTITIES.Payload = payload
	err := enc.Encode(LIST_IDENTITIES)
	if err != nil {
		log.Error("could not encode or writer list identities message, %v", err)
		return err
	}

	log.Debugf("Message sent to ipc pipe")

	writer.Flush()

	return nil
}

func readMessageFromPipe(ipcPipeConn net.Conn, readDone chan struct{}) {
	for {

		reader := bufio.NewReader(ipcPipeConn)
		msg, err := reader.ReadString('\n')

		if err != nil {
			log.Error(err)
			return
		}

		dec := json.NewDecoder(strings.NewReader(msg))
		var responseMsg dto.Response
		if err := dec.Decode(&responseMsg); err == io.EOF {
			break
		} else if err != nil {
			log.Fatal(err)
		}

		if responseMsg.Code == SUCCESS {
			log.Infof("Response message from Command line function : %s", responseMsg.Message)
			log.Info(responseMsg.Payload)
		} else {
			log.Errorf("%s - %s", responseMsg.Message, responseMsg.Error)
		}

		readDone <- struct{}{}
		break

	}
	return
}

//GetIdentities is to fetch identities through cmdline
func GetIdentities() {
	log.Info("fetching identities through cmdline...")

	log.Info("Connecting to pipe")
	timeout := 2000 * time.Millisecond
	ipcPipeConn, err := winio.DialPipe(ipcPipeName(), &timeout)
	defer log.Info("Closing ipc pipe connection")
	defer closeConn(ipcPipeConn)

	if err != nil {
		log.Errorf("Connection to ipc pipe is not established, %v", err)
		return
	}
	readDone := make(chan struct{})
	defer close(readDone) // ensure that goroutine exits

	go readMessageFromPipe(ipcPipeConn, readDone)

	err = sendMessagetoPipe(ipcPipeConn)
	if err != nil {
		log.Errorf("Message is not sent to ipc pipe, %v", err)
		return
	}

	log.Infof("Connection to ipc pipe is established - %s and remote address %s", ipcPipeConn.LocalAddr().String(), ipcPipeConn.RemoteAddr().String())

	<-readDone
	log.Debug("read finished normally")
}
