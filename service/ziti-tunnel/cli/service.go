package cli

import "strings"

//GetIdentities is to fetch identities through cmdline
func GetIdentities(args []string, flags map[string]bool) {
	GetDataFromIpcPipe(&GET_STATUS, GetIdentitiesFromRTS, args, flags)
}

//GetServices is to fetch services through cmdline
func GetServices(args []string, flags map[string]bool) {
	GetDataFromIpcPipe(&GET_STATUS, GetServicesFromRTS, args, flags)
}

//GetFeedback is to fetch logs and sysntem info through cmdline
func OnOffIdentity(args []string, flags map[string]bool) {
	identityPayload := make(map[string]interface{})
	identityPayload["OnOff"] = strings.EqualFold(args[1], "on")
	identityPayload["Fingerprint"] = args[0]
	ONOFF_IDENTITY.Payload = identityPayload
	log.Infof("OnOffIdentity Payload %v", ONOFF_IDENTITY)
	GetDataFromIpcPipe(&ONOFF_IDENTITY, GetServicesFromRTS, args, flags)
}
