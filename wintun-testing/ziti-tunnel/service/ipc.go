package service

import (
	"bufio"
	"crypto/sha1"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"net"
	"os"
	"strings"
	"time"
	"wintun-testing/cziti"

	"github.com/Microsoft/go-winio"
	"github.com/netfoundry/ziti-foundation/identity/identity"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/eventlog"

	"wintun-testing/cziti/windns"
	"wintun-testing/ziti-tunnel/config"
	"wintun-testing/ziti-tunnel/dto"
	"wintun-testing/ziti-tunnel/idutil"
	"wintun-testing/ziti-tunnel/runtime"
)

func SubMain(ops <- chan string, changes chan<- svc.Status) error {
	// open and assign the event log for this service
	Elog, err := eventlog.Open(SvcName)
	if err != nil {
	   return err
	}

	_ = Elog.Info(InformationEvent, SvcName + " starting. log file located at " + config.LogFile())

	// create a channel for notifying any connections that they are to be interrupted
	interrupt = make(chan struct{})

	// create the ACE string representing the following groups have access to the pipes created
	grps := []string{InteractivelyLoggedInUser, System, BuiltinAdmins, LocalService}
	auth := "D:" + strings.Join(grps, "")

	// create the pipes
	pc := winio.PipeConfig{
		SecurityDescriptor: auth,
		MessageMode:        false,
		InputBufferSize:    1024,
		OutputBufferSize:   1024,
	}
	logs, err := winio.ListenPipe(logsPipeName, &pc)
	if err != nil {
		return err
	}
	// listen for log requests
	go acceptLogs(logs)
	log.Infof("log listener ready. pipe: %s", logsPipeName)

	pc2 := winio.PipeConfig{
		SecurityDescriptor: auth,
		MessageMode:        false,
		InputBufferSize:    1024,
		OutputBufferSize:   1024,
	}
	ipc, err := winio.ListenPipe(ipcPipeName, &pc2)
	if err != nil {
		return err
	}

	// listen for ipc messages
	go acceptIPC(ipc)
	log.Infof("ipc listener ready pipe: %s", ipcPipeName)

	// wire in a log file for csdk troubleshooting
	logFile, err := os.OpenFile(config.Path() + "cziti.log", os.O_WRONLY | os.O_TRUNC | os.O_APPEND | os.O_CREATE, 0644)
	if err != nil {
		log.Warnf("could not open log for writing. no debug information will be captured.")
	} else {
		cziti.SetLog(logFile)
		cziti.SetLogLevel(4)
		defer logFile.Close()
	}

	// initialize the network interface
	err = initialize()
	if err != nil {
		log.Errorf("unexpected err: %v", err)
		return err
	}

	setTunnelState(true)

	// notify the service is running
	changes <- svc.Status{State: svc.Running, Accepts: cmdsAccepted}
	_ = Elog.Info(InformationEvent, SvcName + " status set to running")
	log.Info(SvcName + " status set to running. starting cancel loop")

	loop:
		for {
			select {
			case c := <-ops:
				log.Infof("request for control received, %v", c)
				if c == "stop" {
					log.Debug("====== stop request received beginning shutdown")
					break loop
				} else {
					log.Debug("unexpected operation: " + c)
				}
			}
		}

	log.Debug("======           shutdownConnections")
	shutdownConnections()

	log.Debug("======           resetting dns")
	windns.ResetDNS()

	log.Debug("======           closing handles")
	state.Close()
	_ = ipc.Close()
	_ = logs.Close()
	log.Info("shutdown complete. exiting process")

	return nil
}

func shutdownConnections() {
	log.Info("waiting for all connections to close...")

	for i := 0; i < connections; i++ {
		log.Debug("cancelling read loop")
		interrupt <- struct{}{}
	}
	wg.Wait()
	log.Info("all connections closed")
}

func acceptIPC(p net.Listener) {
	for {
		c, err := p.Accept()
		if err != nil {
			if err != winio.ErrPipeListenerClosed {
				log.Errorf("unexpected error while accepting a connection. exiting loop. %v", err)
			}
			return
		}
		wg.Add(1)
		connections ++
		log.Debugf("accepting a new client")

		go serveIpc(c)
	}
}

func initialize() error {
	log.Debugf("reading config file located at: %s", config.File())
	file, err := os.OpenFile(config.File(), os.O_RDONLY, 0640)
	if err != nil {
		// file does not exist or process has no rights to read the file - return leaving configuration empty
		// this is expected when first starting
		return nil
	}

	r := bufio.NewReader(file)
	dec := json.NewDecoder(r)

	_ = dec.Decode(&state)

	err = state.CreateTun()
	if err != nil {
		return err
	}
	setTunInfo()

	// decide if the tunnel should be active or not and if so - activate it
	setTunnelState(state.Active)

	// connect any identities that are enabled
	for _, id := range state.Identities {
		connectIdentity(id)
	}

	err = file.Close()
	if err != nil {
		return fmt.Errorf("could not close configuration file. this is not normal! %v", err)
	}
	log.Debugf("initial state loaded from configuration file")
	return nil
}

func setTunInfo() {
	//set the tun info into the state
	state.IpInfo = &runtime.TunIpInfo{
		Ip:     runtime.Ipv4ip,
		DNS:    runtime.Ipv4dns,
		MTU:    1400,
		Subnet: "255.255.255.0",
	}
}

func closeConn(conn net.Conn) {
	err := conn.Close()
	if err != nil {
		log.Warnf("abnormal error while closing connection. %v", err)
	}
}

func serveIpc(conn net.Conn) {
	log.Debug("beginning ipc receive loop")
	defer closeConn(conn) //close the connection after this function invoked as go routine exits

	done := make(chan struct{})
	defer close(done) // ensure that goroutine exits
	defer wg.Done() // count down whenever the function exits

	go func() {
		select {
		case <-interrupt:
			log.Info("request to interrupt read loop received")
			conn.Close()
			log.Warnf("read loop interrupted")
		case <-done:
			log.Debug("loop finished normally")
		}
	}()

	writer := bufio.NewWriter(conn)
	reader := bufio.NewReader(conn)
	rw := bufio.NewReadWriter(reader, writer)
	enc := json.NewEncoder(writer)

	for {
		log.Debug("beginning read")
		msg, err := reader.ReadString('\n')
		if err != nil {
			if err != winio.ErrFileClosed {
				log.Errorf("unexpected error while reading line. %v", err)

				//try to respond...
				respondWithError(enc, "could not read line properly! exiting loop!", UNKNOWN_ERROR, err)
			}
			return
		}

		log.Debugf("msg received: %s", msg)

		if strings.TrimSpace(msg) == "" {
			// empty message. ignore it and read again
			log.Debug("empty line received. ignoring")
			continue
		}

		dec := json.NewDecoder(strings.NewReader(msg))
		var cmd dto.CommandMsg
		if err := dec.Decode(&cmd); err == io.EOF {
			break
		} else if err != nil {
			log.Fatal(err)
		}

		switch cmd.Function {
		case "AddIdentity":
			addIdMsg, err := reader.ReadString('\n')
			if err != nil {
				respondWithError(enc, "could not read string properly", UNKNOWN_ERROR, err)
				return
			}
			log.Debugf("msg received: %s", addIdMsg)
			addIdDec := json.NewDecoder(strings.NewReader(addIdMsg))

			var newId dto.AddIdentity
			if err := addIdDec.Decode(&newId); err == io.EOF {
				break
			} else if err != nil {
				log.Fatal(err)
			}
			newIdentity(newId, enc)
		case "RemoveIdentity":
			log.Debugf("Request received to remove an identity")
			removeIdentity(enc, cmd.Payload["Fingerprint"].(string))
		case "Status":
			reportStatus(enc)
		case "TunnelState":
			onOff := cmd.Payload["OnOff"].(bool)
			tunnelState(onOff, enc)
		case "IdentityOnOff":
			onOff := cmd.Payload["OnOff"].(bool)
			fingerprint := cmd.Payload["Fingerprint"].(string)
			toggleIdentity(enc, fingerprint, onOff)
		default:
			log.Warnf("Unknown operation: %s. Returning error on pipe", cmd.Function)
			respondWithError(enc, "Something unexpected has happened", UNKNOWN_ERROR, nil)
		}
		_ = rw.Flush()
	}
	log.Info("IPC Loop has exited")
}

func acceptLogs(p net.Listener) {
	log.Debug("beginning logs receive loop")
	for {
		lcon, err := p.Accept()
		if err != nil {
			log.Errorf("unexpected error during accept. exiting accept loop! %v", err)
			return
		}

		go serveLogs(lcon) //serveLogs will close the connection for us
	}
}

func serveLogs(conn net.Conn) {
	log.Debug("accepted a connection, writing logs to pipe")
	w := bufio.NewWriter(conn)

	file, err := os.OpenFile(config.LogFile(), os.O_RDONLY, 0640)
	if err != nil {
		log.Errorf("could not open log file at %s", config.LogFile())
		_, _ = w.WriteString("an unexpected error occurred while retrieving logs. look at the actual log file.")
		return
	}

	r := bufio.NewReader(file)
	wrote, err := io.Copy(w, r)
	if err != nil {
		log.Errorf("problem responding with log data")
	}
	_, err = w.Write([]byte("end of logs\n"))
	if err != nil {
		log.Errorf("unexpected error writing log response: %v", err)
	}

	err = w.Flush()
	if err != nil {
		log.Errorf("unexpected error flushing log response: %v", err)
	}
	log.Debugf("wrote %d bytes to client from logs", wrote)

	err = file.Close()
	if err != nil {
		log.Error("error closing log file", err)
	}

	err = conn.Close()
	if err != nil {
		log.Error("error closing connection", err)
	}
}

func reportStatus(out *json.Encoder) {
	log.Debugf("request for status")
	respond(out, state.Clean())
	log.Debugf("request for status responded to")
}

func tunnelState(onOff bool, out *json.Encoder) {
	log.Debugf("toggle ziti on/off: %t", onOff)
	if onOff == state.Active {
		log.Debug("nothing to do. the state of the tunnel already matches the requested state: %t", onOff)
		respond(out, dto.Response{Message: fmt.Sprintf("noop: tunnel state already set to %t", onOff), Code: SUCCESS, Error: "", Payload: nil})
		return
	}
	setTunnelState(onOff)
	state.Active = onOff
	runtime.SaveState(&state)

	respond(out, dto.Response{Message: "tunnel state updated successfully", Code: SUCCESS, Error: "", Payload: nil})
	log.Debugf("toggle ziti on/off: %t responded to", onOff)
}

func setTunnelState(onOff bool) {
	if onOff {
		runtime.TunStarted = time.Now()

		for _, id := range state.Identities {
			connectIdentity(id)
		}
	} else {
		// state.Close()
	}
}

func toggleIdentity(out *json.Encoder, fingerprint string, onOff bool) {
	log.Debugf("toggle ziti on/off5 for %s: %t", fingerprint, onOff)

	_, id := state.Find(fingerprint)
	if id.Active == onOff {
		log.Debugf("nothing to do - the provided identity %s is already set to active=%t", id.Name, id.Active)
		//nothing to do...
		respond(out, dto.Response{
			Code:    SUCCESS,
			Message: fmt.Sprintf("no update performed. identity is already set to active=%t", onOff),
			Error:   "",
			Payload: nil,
		})
		return
	}

	if onOff {
		connectIdentity(id)
	} else {
		disconnectIdentity(id)
	}

	respond(out, dto.Response{Message: "identity toggled", Code: SUCCESS, Error: "", Payload: idutil.Clean(*id)})
	log.Debugf("toggle ziti on/off for %s: %t responded to", fingerprint, onOff)
	runtime.SaveState(&state)
}

func removeTempFile(file os.File) {
	err := os.Remove(file.Name()) // clean up
	if err != nil {
		log.Warnf("could not remove temp file: %s", file.Name())
	}
	err = file.Close()
	if err != nil {
		log.Warnf("could not close the temp file: %s", file.Name())
	}
}

func newIdentity(newId dto.AddIdentity, out *json.Encoder) {
	log.Debugf("new identity for %s: %s", newId.Id.Name, newId.EnrollmentFlags.JwtString)

	tokenStr := newId.EnrollmentFlags.JwtString
	log.Debugf("jwt to parse: %s", tokenStr)
	tkn, _, err := enroll.ParseToken(tokenStr)

	if err != nil {
		respondWithError(out, "failed to parse JWT: %s", COULD_NOT_ENROLL, err)
		return
	}
	var certPath = ""
	var keyPath = ""
	var caOverride = ""

	flags := enroll.EnrollmentFlags{
		CertFile:      certPath,
		KeyFile:       keyPath,
		Token:         tkn,
		IDName:        newId.Id.Name,
		AdditionalCAs: caOverride,
	}

	//enroll identity using the file and go sdk
	conf, err := enroll.Enroll(flags)
	if err != nil {
		respondWithError(out, "failed to enroll", COULD_NOT_ENROLL, err)
		return
	}

	enrolled, err := ioutil.TempFile("" /*temp dir*/, "ziti-enrollment-*")
	if err != nil {
		respondWithError(out, "Could not create temporary file in local storage. This is abnormal. "+
			"Check the process has access to the temporary folder", COULD_NOT_WRITE_FILE, err)
		return
	}

	enc := json.NewEncoder(enrolled)
	enc.SetEscapeHTML(false)
	encErr := enc.Encode(&conf)

	outpath := enrolled.Name()
	if encErr != nil {
		respondWithError(out, fmt.Sprintf("enrollment successful but the identity file was not able to be written to: %s [%s]", outpath, encErr), COULD_NOT_ENROLL, err)
		return
	}

	id, err := identity.LoadIdentity(conf.ID)
	if err != nil {
		respondWithError(out, "unable to load identity which was just created. this is abnormal", COULD_NOT_ENROLL, err)
		return
	}

	//map fields onto new identity
	newId.Id.Config.ZtAPI = conf.ZtAPI
	newId.Id.Config.ID = conf.ID
	newId.Id.FingerPrint = fmt.Sprintf("%x", sha1.Sum(id.Cert().Leaf.Raw)) //generate fingerprint
	if newId.Id.Name == "" {
		newId.Id.Name = newId.Id.FingerPrint
	}
	newId.Id.Status = "Enrolled"

	err = enrolled.Close()
	if err != nil {
		panic(err)
	}
	newPath := newId.Id.Path()

	//move the temp file to its final home after enrollment
	err = os.Rename(enrolled.Name(), newPath)
	if err != nil {
		log.Errorf("unexpected issue renaming the enrollment! attempting to remove the temporary file at: %s", enrolled.Name())
		removeTempFile(*enrolled)
		respondWithError(out, "a problem occurred while writing the identity file.", COULD_NOT_ENROLL, err)
	}

	//newId.Id.Active = false //set to false by default - enable the id after persisting
	log.Infof("enrolled successfully. identity file written to: %s", newPath)

	if newId.Id.Active == true {
		connectIdentity(&newId.Id)
	}

	//if successful parse the output and add the config to the identity
	state.Identities = append(state.Identities, &newId.Id)

	//save the state
	runtime.SaveState(&state)

	//return successful message
	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: idutil.Clean(newId.Id)}

	respond(out, resp)
	log.Debugf("new identity for %s: %s responded to", newId.Id.Name, newId.EnrollmentFlags.JwtString)
}

func respondWithError(out *json.Encoder, msg string, code int, err error) {
	if err != nil {
		respond(out, dto.Response{Message: msg, Code: code, Error: err.Error()})
	} else {
		respond(out, dto.Response{Message: msg, Code: code, Error: ""})
	}
	log.Debugf("responding with error: %s, %d, %v", msg, code, err)
}

func connectIdentity(id *dto.Identity) {
	if !id.Connected {
		//tell the c sdk to use the file from the id and connect
		log.Infof("Connecting identity: %s", id.Name)
		state.LoadIdentity(id)
	} else {
		log.Debugf("id [%s] is already connected - not reconnecting", id.Name)
	}
}

func disconnectIdentity(id *dto.Identity) error {
	//tell the c sdk to disconnect the identity/services etc
	log.Infof("Disconnecting identity: %s", id.Name)

	if id.Connected {
		// actually disconnect from the c sdk here
		log.Warn("not implemented yet - disconnected an already connected id doesn't actually work yet...")
		id.Connected = false
		return nil
	} else {
		log.Debugf("id: %s is already disconnected - not attempting to disconnected again fingerprint:%s", id.Name, id.FingerPrint)
	}

	//remove the file from the filesystem - first verify it's the proper file
	err := os.Remove(id.Path())
	if err != nil {
		log.Warn("could not remove file: %s", config.Path()+id.FingerPrint+".json")
	}
	return nil
}

func removeIdentity(out *json.Encoder, fingerprint string) {
	log.Infof("request to remove identity by fingerprint: %s", fingerprint)
	_, id := state.Find(fingerprint)
	if id == nil {
		respondWithError(out, fmt.Sprintf("Could not find identity by fingerprint: %s", fingerprint), IDENTITY_NOT_FOUND, nil)
		return
	}

	err := disconnectIdentity(id)
	if err != nil {
		respondWithError(out, "Error when disconnecting identity", ERROR_DISCONNECTING_ID, err)
		return
	}
	state.RemoveByIdentity(*id)
	runtime.SaveState(&state)

	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: nil}
	respond(out, resp)
	log.Infof("request to remove identity by fingerprint: %s responded to", fingerprint)
}

func respond(out *json.Encoder, thing interface{}) {
	/*os.Stdout.Write(delim)
	_ = json.NewEncoder(os.Stdout).Encode(thing)
	os.Stdout.Write(delim)
	*/
	_ = out.Encode(thing)
}
