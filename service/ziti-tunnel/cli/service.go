package cli

import (
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

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
	status := GetDataFromIpcPipe(&ONOFF_IDENTITY, nil, GetIdentityResponseObjectFromRTS, args, flags)
	if status {
		NOTIFY_IDENTITY_UI.Payload = identityPayload
		log.Infof("Notifying the Identity Status to UI %v", identityPayload)
		GetDataFromIpcPipe(&NOTIFY_IDENTITY_UI, nil, GetResponseObjectFromRTS, args, flags)
	}
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
		status := GetDataFromIpcPipe(&SET_LOGLEVEL, nil, GetResponseObjectFromRTS, args, flags)
		if status {
			NOTIFY_LOGLEVEL_UI_MONITOR.Payload = loglevelPayload
			log.Infof("Notifying the LogLevel to UI and Ziti monitor service %v", loglevelPayload)
			GetDataFromIpcPipe(&NOTIFY_LOGLEVEL_UI_MONITOR, nil, GetResponseObjectFromRTS, args, flags)
		}
	}
}

//GetFeedback is to create logs zip through cmdline
func GetFeedback(args []string, flags map[string]bool) {
	GetDataFromMonitorIpcPipe(&dto.FEEDBACK_REQUEST, args, flags)
}
