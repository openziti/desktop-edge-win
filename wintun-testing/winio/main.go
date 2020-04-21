package main

import (
	"bufio"
	"crypto/sha1"
	"encoding/json"
	"fmt"
	"github.com/Microsoft/go-winio"
	"github.com/michaelquigley/pfxlog"
	"github.com/netfoundry/ziti-foundation/identity/identity"
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
	"io"
	"io/ioutil"
	"net"
	"os"

	"wintun-testing/winio/config"
	"wintun-testing/winio/dto"
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

var debug = true

func main() {
	config.InitLogger("debug")

	ipc, err := winio.ListenPipe(ipcPipeName, nil)
	if err != nil {
		log.Panic(err)
	}
	go acceptIPC(ipc)
	log.Info("IPC listener ready")

	logs, err := winio.ListenPipe(logsPipeName, nil)
	if err != nil {
		log.Panic(err)
	}
	go acceptLogs(logs)
	log.Info("log listener ready")

	initialize()
	<-finished
	<-finished
	_ = ipc.Close()
	_ = logs.Close()
}

func acceptIPC(p net.Listener) {
	for {
		c, err := p.Accept()
		log.Debugf("accepting a new client")
		if err != nil {
			panic(err)
		}

		go serveIpc(c)
	}
}

func initialize() {
	log.Debugf("reading config file located at s%:", config.File())
	file, err := os.OpenFile(config.File(), os.O_RDONLY, 0640)
	if err != nil {
		// file does not exist or process has no rights to read the file - return leaving configuration empty
		// this is expected when first starting
		return
	}

	r := bufio.NewReader(file)
	dec := json.NewDecoder(r)

	_ = dec.Decode(&state)

	err = file.Close()
	if err != nil {
		log.Panic("could not close configuration file. this is not normal.")
	}
	log.Debugf("initial state loaded")
}

func closeConn(conn net.Conn) {
	err := conn.Close()
	if err != nil {
		log.Warn("abnormal error while closing connection. ", err.Error())
	}
}

func serveIpc(conn net.Conn) {
	log.Debug("beginning receive loop")
	defer closeConn(conn) //close the connection after this function invoked as go routine exits

	writer := bufio.NewWriter(conn)
	rw := bufio.NewReadWriter(bufio.NewReader(conn), writer)
	dec := json.NewDecoder(rw)
	enc := json.NewEncoder(rw)

	for {
		var cmd dto.CommandMsg
		if err := dec.Decode(&cmd); err == io.EOF {
			break
		} else if err != nil {
			log.Fatal(err)
		}

		switch cmd.Function {
		case "AddIdentity":
			var newId dto.AddIdentity
			if err := dec.Decode(&newId); err == io.EOF {
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
		_ = writer.WriteByte('\n') //just in case the client tries to read a line
		_ = rw.Flush()
	}
}

func acceptLogs(p net.Listener) {
	for {
		conn, err := p.Accept()
		if err != nil {
			panic(err)
		}

		go serveLogs(conn)
	}
}

func serveLogs(conn net.Conn) {
	w := bufio.NewWriter(conn)

	file, err := os.OpenFile(config.LogFile(), os.O_RDONLY, 0640)
	if err != nil {
		log.Errorf("could not open log file at %s", config.LogFile())
		return
	}

	r := bufio.NewReader(file)
	wrote, err := io.Copy(w, r)
	if err != nil{
		log.Errorf("problem responding with log data")
	}
	log.Debugf("wrote %d bytes to client from logs", wrote)
	w.Write([]byte("end of logs\n"))
	w.Flush()
	_ = conn.Close() //close the connection

	err = file.Close()
	if err != nil {
		log.Error("error closing log file", err)
	}
}

func reportStatus(out *json.Encoder) {
	log.Debugf("request for status")
	rtn := state

	//remove the config from the status
	ids := make([]dto.Identity, len(rtn.Identities))
	for i, id := range rtn.Identities {
		ids[i] = cleanId(id)
	}
	rtn.Identities = ids
	_ = out.Encode(rtn)
}

func tunnelState(onOff bool, out *json.Encoder) {
	log.Debugf("toggle ziti on/off: %t", onOff)
	state.TunnelActive = onOff
	runtime.SaveState(&state)
	//TODO: actually turn the tunnel on and off as well as handle errors
	_ = out.Encode(dto.Response{Message: "tunnel state updated successfully", Code: SUCCESS, Error: "", Payload: nil})
}

func toggleIdentity(out *json.Encoder, fingerprint string, onOff bool) {
	log.Debugf("toggle ziti on/off for %s: %t", fingerprint, onOff)

	_, id := state.Find(fingerprint)
	*id.Active = onOff

	_ = out.Encode(dto.Response{Message: "identity toggled", Code: SUCCESS, Error: "", Payload: nil})
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
	newPath := config.Path() + newId.Id.FingerPrint + ".json"

	//move the temp file to its final home after enrollment
	err = os.Rename(enrolled.Name(), newPath)
	if err != nil {
		log.Errorf("unexpected issue renaming the enrollment! attempting to remove the temporary file at: %s", enrolled.Name())
		removeTempFile(*enrolled)
		respondWithError(out, "a problem occurred while writing the identity file.", COULD_NOT_ENROLL, err)
	}

	//newId.Id.Active = false //set to false by default - enable the id after persisting
	log.Infof("enrolled successfully. identity file written to: %s", newPath)

	if *newId.Id.Active == true {
		connectIdentity(&newId.Id)
	}

	//if successful parse the output and add the config to the identity
	state.Identities = append(state.Identities, newId.Id)

	//save the state
	runtime.SaveState(&state)

	//return successful message
	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: cleanId(newId.Id)}

	respond(out, resp)
}

func respondWithError(out *json.Encoder, msg string, code int, err error) {
	if err != nil {
		_ = out.Encode(dto.Response{Message: msg, Code: code, Error: err.Error()})
	} else {
		_ = out.Encode(dto.Response{Message: msg, Code: code, Error: ""})
	}
}

func connectIdentity(id *dto.Identity) {
	//tell the c sdk to use the file from the id and connect
	log.Infof("Connecting identity: %s", id.Name)
	loadServices(id)
}

func loadServices(id *dto.Identity) {
	id.Services = append(id.Services,
		dto.Service{Name: "ServiceOne", HostName: "MyServiceName", Port: 1111},
		dto.Service{Name: "SecondOne", HostName: "SecondService", Port: 2222},
		dto.Service{Name: "LastDummy Service With Spaces and is very very long", HostName: "10.10.10.10", Port: 3333},
	)
}

func disconnectIdentity(id dto.Identity) error {
	//tell the c sdk to disconnect the identity/services etc
	log.Infof("Disconnecting identity: %s", id.Name)

	//remove the file from the filesystem - first verify it's the proper file
	err := os.Remove(config.Path() + id.FingerPrint + ".json")
	if err != nil {
		log.Warn("could not remove file: %s", config.Path()+id.FingerPrint+".json")
	}
	return nil
}

func removeIdentity(out *json.Encoder, fingerprint string) {
	_, id := state.Find(fingerprint)
	if id == nil {
		respondWithError(out, fmt.Sprintf("Could not find identity by fingerprint: %s", fingerprint), IDENTITY_NOT_FOUND, nil)
		return
	}

	err := disconnectIdentity(*id)
	if err != nil {
		respondWithError(out, "Error when disconnecting identity", ERROR_DISCONNECTING_ID, err)
		return
	}
	state.RemoveByIdentity(*id)
	runtime.SaveState(&state)

	resp := dto.Response{Message: "success", Code: SUCCESS, Error: "", Payload: nil}
	_ = out.Encode(resp)
}

func respond(out *json.Encoder, thing interface{}) {
	//debug json to stdout if needed _ = json.NewEncoder(os.Stdout).Encode(thing)
	_ = out.Encode(thing)
}

//Removes the Config from the provided identity and returns a 'cleaned' id
func cleanId(id dto.Identity) dto.Identity {
	nid := id
	nid.Config = idcfg.Config{}
	nid.Config.ZtAPI = id.Config.ZtAPI
	return nid
}
