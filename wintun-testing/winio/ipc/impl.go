package ipc

import (
	"bufio"
	"crypto/sha1"
	"encoding/json"
	"fmt"
	"github.com/Microsoft/go-winio"
	"github.com/michaelquigley/pfxlog"
	"github.com/netfoundry/ziti-foundation/identity/identity"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
	"io"
	"io/ioutil"
	"net"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"
	"wintun-testing/cziti"
	"wintun-testing/cziti/windns"
	"wintun-testing/winio/config"
	"wintun-testing/winio/dto"
	"wintun-testing/winio/idutil"
	"wintun-testing/winio/runtime"
)

var ipcPipeName = `\\.\pipe\NetFoundry\tunneler\ipc`
var logsPipeName = `\\.\pipe\NetFoundry\tunneler\logs`
var finished chan bool
var log = pfxlog.Logger()
var state = runtime.TunnelerState{}

const (
	SUCCESS              = 0
	COULD_NOT_WRITE_FILE = 1
	COULD_NOT_ENROLL     = 2

	UNKNOWN_ERROR          = 100
	ERROR_DISCONNECTING_ID = 50
	IDENTITY_NOT_FOUND     = 1000
)

//var delim = []byte("====")
const (
	// see: https://docs.microsoft.com/en-us/windows/win32/secauthz/sid-strings
	// breaks down to
	//		"allow" 	  	 - A   (A;;
	// 	 	"full access" 	 - FA  (A;;FA
	//		"well-known sid" - IU  (A;;FA;;;IU)
	InteractivelyLoggedInUser = "(A;;GRGW;;;IU)" //generic read/write. We will want to tune this to a specific group but that is not working with Windows 10 home at the moment
	System                    = "(A;;FA;;;SY)"
	BuiltinAdmins             = "(A;;FA;;;BA)"
	LocalService              = "(A;;FA;;;LS)"
)

func SubMain() {
	errs := make(chan error)
	term := make(chan os.Signal, 1)
	config.InitLogger("debug")

	//ensure the necessary group exists and the process has access to the group
	//	sid := runtime.EnsurePermissions(runtime.NF_GROUP_NAME) //will return the string or Fatal/Panic
	//	onlyNF := "S:(ML;;NW;;;LW)D:(A;;FA;;;" + sid + ")"
	grps := []string{InteractivelyLoggedInUser, System, BuiltinAdmins, LocalService}
	auth := "D:" + strings.Join(grps, "")

	pc := winio.PipeConfig{
		SecurityDescriptor: auth,
		MessageMode:        false,
		InputBufferSize:    1024,
		OutputBufferSize:   1024,
	}
	logs, err := winio.ListenPipe(logsPipeName, &pc)
	if err != nil {
		log.Panic(err)
	}
	go acceptLogs(logs)
	log.Info("log listener ready. pipe: %s", logsPipeName)

	pc2 := winio.PipeConfig{
		SecurityDescriptor: auth,
		MessageMode:        false,
		InputBufferSize:    1024,
		OutputBufferSize:   1024,
	}
	ipc, err := winio.ListenPipe(ipcPipeName, &pc2)
	if err != nil {
		log.Panic(err)
	}
	go acceptIPC(ipc)
	log.Info("IPC listener ready pipe: %s", ipcPipeName)

	initialize()
	establishTun()

	signal.Notify(term, os.Interrupt)
	signal.Notify(term, os.Kill)
	signal.Notify(term, syscall.SIGTERM)

	select {
	case <-term:
	case <-errs:
	}

	windns.ResetDNS()

	state.Close()
	_ = ipc.Close()
	_ = logs.Close()
}

func acceptIPC(p net.Listener) {
	for {
		c, err := p.Accept()
		if err != nil {
			panic(err)
		}
		log.Debugf("accepting a new client")

		go serveIpc(c)
	}
}

func initialize() {
	log.Debugf("reading config file located at : %s", config.File())
	file, err := os.OpenFile(config.File(), os.O_RDONLY, 0640)
	if err != nil {
		// file does not exist or process has no rights to read the file - return leaving configuration empty
		// this is expected when first starting
		return
	}

	r := bufio.NewReader(file)
	dec := json.NewDecoder(r)

	_ = dec.Decode(&state)

	// decide if the tunnel should be active or not and if so - activate it
	setTunnelState(state.Active)

	// connect any identities that are enabled
	for _, id := range state.Identities {
		connectIdentity(id)
	}

	err = file.Close()
	if err != nil {
		log.Panic("could not close configuration file. this is not normal.")
	}
	log.Debugf("initial state loaded from configuration file")
}

func establishTun() {
	//do something to make the tun

	//set the tun info into the state
	state.IpInfo = &runtime.TunIpInfo{
		Ip:     "1.1.1.1",
		DNS:    "5.5.5.5",
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

	writer := bufio.NewWriter(conn)
	reader := bufio.NewReader(conn)
	rw := bufio.NewReadWriter(reader, writer)
	enc := json.NewEncoder(writer)

	for {
		log.Debug("beginning read")
		msg, err := reader.ReadString('\n')
		if err != nil {
			respondWithError(enc, "could not read string properly", UNKNOWN_ERROR, err)
			return
		}
		/*
			os.Stdout.Write(delim)
			os.Stdout.Write([]byte(msg))
			os.Stdout.Write(delim)
		*/
		log.Debugf("msg received: %s", msg)
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
			/*
				os.Stdout.Write(delim)
				os.Stdout.Write([]byte(addIdMsg))
				os.Stdout.Write(delim)
			*/
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
			log.Debugf("Unknown operation: %s. Returning error on pipe", cmd.Function)
			respondWithError(enc, "Something unexpected has happened", UNKNOWN_ERROR, nil)
		}
		if cmd.Function != "" {
			//_ = writer.WriteByte('\n') //just in case the client tries to read a line
		} else {
			log.Warn("Empty input received?")
		}
		_ = rw.Flush()
	}
}

func acceptLogs(p net.Listener) {
	log.Debug("beginning logs receive loop")
	for {
		l, err := p.Accept()
		if err != nil {
			panic(err)
		}
		log.Debug("accepted a connection, returning logs")

		go serveLogs(l)
	}
}

func serveLogs(conn net.Conn) {
	log.Debug("writing logs to pipe")
	w := bufio.NewWriter(conn)

	file, err := os.OpenFile(config.LogFile(), os.O_RDONLY, 0640)
	if err != nil {
		log.Errorf("could not open log file at %s", config.LogFile())
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
		return
	}
	setTunnelState(onOff)
	state.Active = onOff
	runtime.SaveState(&state)

	respond(out, dto.Response{Message: "tunnel state updated successfully", Code: SUCCESS, Error: "", Payload: nil})
	log.Debugf("toggle ziti on/off: %t responded to", onOff)
}

func setTunnelState(onOff bool) {
	if state.Active == onOff {
		log.Debugf("noop. tunnel state is already active: %t", onOff)
		return
	}

	if onOff {
		state.CreateTun()
		runtime.TunStarted = time.Now()

		for _, id := range state.Identities {
			connectIdentity(id)
		}
	} else {
		state.Close()
	}
}

func toggleIdentity(out *json.Encoder, fingerprint string, onOff bool) {
	log.Debugf("toggle ziti on/off for %s: %t", fingerprint, onOff)

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

	id.Active = onOff
	connectIdentity(id)

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
	if !id.Active && !state.Active {
		//clear out the services before returning
		id.Services = nil
		log.Infof("not connecting identity: %s as it is not active", id.Name)
		return
	}

	if id.Connected {
		log.Debugf("id: %s is already connected - not attempting to connect again fingerprint:%s", id.Name, id.FingerPrint)
		return
	}

	//tell the c sdk to use the file from the id and connect
	log.Infof("Connecting identity: %s", id.Name)

	if ctx, err := cziti.LoadZiti(id.Path()); err != nil {
		log.Panic(err)
	} else {
		log.Infof("successfully loaded %s@%s\n", ctx.Name(), ctx.Controller())
	}

	id.Connected = true

	loadServices(id)
	log.Infof("Connecting identity: %s responded to", id.Name)
}

func loadServices(id *dto.Identity) {
	id.Services = make([]*dto.Service, 0)
	id.Services = append(id.Services,
		&dto.Service{Name: "ServiceOne", HostName: "MyServiceName", Port: 1111},
		&dto.Service{Name: "SecondOne", HostName: "SecondService", Port: 2222},
		&dto.Service{Name: "LastDummy Service With Spaces and is very very long", HostName: "10.10.10.10", Port: 3333},
	)
}

func disconnectIdentity(id *dto.Identity) error {
	//tell the c sdk to disconnect the identity/services etc
	log.Infof("Disconnecting identity: %s", id.Name)

	if id.Connected {
		// actually disconnect from the c sdk here
		log.Warn("not implemented yet - disconnected an already connected id doesn't actually work yet...")

		return nil
		id.Connected = false
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
