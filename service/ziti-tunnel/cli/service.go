package cli

import "strings"

//GetIdentities is to fetch identities through cmdline
func GetIdentities(args []string, flags map[string]bool) {
	GetDataFromIpcPipe(&GET_STATUS, GetIdentitiesFromRTS, nil, args, flags)
}

//GetServices is to fetch services through cmdline
func GetServices(args []string, flags map[string]bool) {
	GetDataFromIpcPipe(&GET_STATUS, GetServicesFromRTS, nil, args, flags)
}

//OnOffIdentity is to enable or disable the identity through cmdline
func OnOffIdentity(args []string, flags map[string]bool) {
	identityPayload := make(map[string]interface{})
	identityPayload["OnOff"] = strings.EqualFold(args[1], "on")
	identityPayload["Fingerprint"] = args[0]
	ONOFF_IDENTITY.Payload = identityPayload
	log.Debugf("OnOffIdentity Payload %v", ONOFF_IDENTITY)
	GetDataFromIpcPipe(&ONOFF_IDENTITY, nil, GetResponseObjectFromRTS, args, flags)
}

//SetLogLevel is to change the loglevel through cmdline
func SetLogLevel(args []string, flags map[string]bool) {
	if flags["query"] == true {
		GetDataFromIpcPipe(&GET_STATUS, GetLogLevelFromRTS, nil, args, flags)
	} else {
		loglevelPayload := make(map[string]interface{})
		loglevelPayload["Level"] = args[0]
		SET_LOGLEVEL.Payload = loglevelPayload
		log.Debugf("LogLevel Payload %v", SET_LOGLEVEL)
		GetDataFromIpcPipe(&SET_LOGLEVEL, nil, GetResponseObjectFromRTS, args, flags)
	}
}
