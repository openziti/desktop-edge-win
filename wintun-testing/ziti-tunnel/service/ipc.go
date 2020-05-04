package service

import (
	"bufio"
	"crypto/sha1"
	"encoding/json"
	"fmt"
	"github.com/Microsoft/go-winio"
	"github.com/netfoundry/ziti-foundation/identity/identity"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
	"golang.org/x/sys/windows/svc"
	"io"
	"io/ioutil"
	"net"
	"os"
	"strings"
	"time"
	"wintun-testing/cziti"
	"wintun-testing/ziti-tunnel/globals"

	"wintun-testing/cziti/windns"
	"wintun-testing/ziti-tunnel/config"
	"wintun-testing/ziti-tunnel/dto"
	"wintun-testing/ziti-tunnel/idutil"
)

type Pipes struct {
	ipc    net.Listener
	logs   net.Listener
	events net.Listener
}

func(p *Pipes) Close() {
	p.ipc.Close()
	p.logs.Close()
	p.events.Close()
}

func SubMain(ops <- chan string, changes chan<- svc.Status) error {
	defer close(top.Broadcast)
	log.Info("============================== service begins ==============================")

	_ = globals.Elog.Info(InformationEvent, SvcName + " starting. log file located at " + config.LogFile())

	// create a channel for notifying any connections that they are to be interrupted
	interrupt = make(chan struct{})

	pipes, err := openPipes()
	if err != nil {
		return err
	}
	defer pipes.Close()

	// wire in a log file for csdk troubleshooting
	logFile, err := os.OpenFile(config.Path() + "cziti.log", os.O_WRONLY | os.O_TRUNC | os.O_APPEND | os.O_CREATE, 0644)
	if err != nil {
		log.Warnf("could not open cziti.log for writing. no debug information will be captured.")
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
	_ = globals.Elog.Info(InformationEvent, SvcName + " status set to running")
	log.Info(SvcName + " status set to running. starting cancel loop")

	waitForStopRequest(ops)

	pipes.shutdownConnections()

	windns.ResetDNS()

	rts.Close()

	log.Info("==============================  service ends  ==============================")

	return nil
}
func waitForStopRequest(ops <- chan string) {

loop:
	for {
		c := <-ops
		log.Infof("request for control received, %v", c)
		if c == "stop" {
			break loop
		} else {
			log.Debug("unexpected operation: " + c)
		}
	}
}

func openPipes() (*Pipes, error) {
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
	logs, err := winio.ListenPipe(logsPipeName(), &pc)
	if err != nil {
		return nil, err
	}
	ipc, err := winio.ListenPipe(ipcPipeName(), &pc)
	if err != nil {
		return nil, err
	}
	events, err := winio.ListenPipe(eventsPipeName(), &pc)
	if err != nil {
		return nil, err
	}

	// listen for log requests
	go accept(logs, serveLogs)
	log.Infof("log listener ready. pipe: %s", logsPipeName())

	// listen for ipc messages
	go accept(ipc, serveIpc)
	log.Infof("ipc listener ready pipe: %s", ipcPipeName())

	// listen for events messages
	go accept(events, serveEvents)
	log.Infof("events listener ready pipe: %s", eventsPipeName())

	return &Pipes{
		ipc: ipc,
		logs: logs,
		events: events,
	}, nil
}

func(p *Pipes) shutdownConnections() {
	log.Info("waiting for all connections to close...")

	for i := 0; i < connections; i++ {
		log.Debug("cancelling read loop")
		interrupt <- struct{}{}
	}
	wg.Wait()
	log.Info("all connections closed")
}

func initialize() error {
	rts.LoadConfig()

	err := rts.CreateTun()
	if err != nil {
		return err
	}
	setTunInfo(rts.state)

	s := rts.state
	// decide if the tunnel should be active or not and if so - activate it
	setTunnelState(s.Active)

	// connect any identities that are enabled
	for _, id := range s.Identities {
		connectIdentity(id)
	}

	log.Debugf("initial state loaded from configuration file")
	return nil
}

func setTunInfo(s *dto.TunnelStatus) {
	//set the tun info into the state
	s.IpInfo = &dto.TunIpInfo{
		Ip:     Ipv4ip,
		DNS:    Ipv4dns,
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

func accept(p net.Listener, serveFunction func(net.Conn)) {
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

		go serveFunction(c)
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
			
			top.Broadcast <- dto.ZitiTunnelStatus{
				Status:  rts.ToStatus(),
				Metrics: nil,
			}
		default:
			log.Warnf("Unknown operation: %s. Returning error on pipe", cmd.Function)
			respondWithError(enc, "Something unexpected has happened", UNKNOWN_ERROR, nil)
		}
		_ = rw.Flush()
	}
	log.Info("IPC Loop has exited")
}

func serveLogs(conn net.Conn) {
	log.Debug("accepted a connection, writing logs to pipe")
	w := bufio.NewWriter(conn)

	file, err := os.OpenFile(config.LogFile(), os.O_RDONLY, 0644)
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

func serveEvents(conn net.Conn) {
	log.Debug("accepted a connection, writing events to pipe")

	consumer := make(chan interface{}, 1)
	top.Register(consumer)

	w := bufio.NewWriter(conn)
	o := json.NewEncoder(w)

	for {
		msg := <-consumer
		status, ok := msg.(dto.ZitiTunnelStatus)
		if !ok {
			log.Errorf("message received couldn't be converted to status? %v", status)
			 break
		}
		respond(o, dto.Response{Payload: status})
		_, err := w.WriteString("\n")
		if err != nil {
			if err == io.EOF {
				//fine client disconnected
				log.Debug("exiting from serveEvents - client disconnected")
			} else {
				log.Errorf("exiting from serveEvents - unexpected error %v", err)
			}
			top.Unregister(consumer)
			break
		}
		_ = w.Flush()

		log.Infof("got %v", msg)
	}
	log.Info("exiting serve events")
}

func reportStatus(out *json.Encoder) {
	log.Debugf("request for status")

	respond(out, dto.ZitiTunnelStatus{
		Status:  rts.ToStatus(),
		Metrics: nil,
	})
	log.Debugf("request for status responded to")
}

func tunnelState(onOff bool, out *json.Encoder) {
	log.Debugf("toggle ziti on/off: %t", onOff)
	state := rts.state
	if onOff == state.Active {
		log.Debug("nothing to do. the state of the tunnel already matches the requested state: %t", onOff)
		respond(out, dto.Response{Message: fmt.Sprintf("noop: tunnel state already set to %t", onOff), Code: SUCCESS, Error: "", Payload: nil})
		return
	}
	setTunnelState(onOff)
	state.Active = onOff
	SaveState(&rts)

	respond(out, dto.Response{Message: "tunnel state updated successfully", Code: SUCCESS, Error: "", Payload: nil})
	log.Debugf("toggle ziti on/off: %t responded to", onOff)
}

func setTunnelState(onOff bool) {
	if onOff {
		TunStarted = time.Now()

		state := rts.state
		for _, id := range state.Identities {
			connectIdentity(id)
		}
	} else {
		// state.Close()
	}
}

func toggleIdentity(out *json.Encoder, fingerprint string, onOff bool) {
	log.Debugf("toggle ziti on/off5 for %s: %t", fingerprint, onOff)

	_, id := rts.Find(fingerprint)
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
	SaveState(&rts)
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

	connectIdentity(&newId.Id)

	state := rts.state
	//if successful parse the output and add the config to the identity
	state.Identities = append(state.Identities, &newId.Id)

	//save the state
	SaveState(&rts)

	//return successful message
	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: idutil.Clean(newId.Id)}

	respond(out, resp)
	log.Debugf("new identity for %s responded to", newId.Id.Name)
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
		log.Debugf("loading identity %s with fingerprint %s", id.Name, id.FingerPrint)
		rts.LoadIdentity(id)
	} else {
		log.Debugf("id [%s] is already connected - not reconnecting", id.Name)
	}
	id.Active = true
	log.Infof("identity [%s] connected [%t] and set to active [%t]", id.Name, id.Connected, id.Active)
}

func disconnectIdentity(id *dto.Identity) error {
	//tell the c sdk to disconnect the identity/services etc
	log.Infof("Disconnecting identity: %s", id.Name)

	id.Active = false
	if id.Connected {
		// actually disconnect from the c sdk here
		log.Warn("not implemented yet - disconnected an already connected id doesn't actually work yet...")
		//id.Connected = false
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
	_, id := rts.Find(fingerprint)
	if id == nil {
		respondWithError(out, fmt.Sprintf("Could not find identity by fingerprint: %s", fingerprint), IDENTITY_NOT_FOUND, nil)
		return
	}

	err := disconnectIdentity(id)
	if err != nil {
		respondWithError(out, "Error when disconnecting identity", ERROR_DISCONNECTING_ID, err)
		return
	}
	rts.RemoveByIdentity(*id)
	SaveState(&rts)

	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: nil}
	respond(out, resp)
	log.Infof("request to remove identity by fingerprint: %s responded to", fingerprint)
}

func respond(out *json.Encoder, thing interface{}) {
	//leave for debugging j := json.NewEncoder(os.Stdout)
	//leave for debugging j.Encode(thing)
	_ = out.Encode(thing)
}
