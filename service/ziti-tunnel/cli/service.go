package cli

import (
	"strings"
    "strconv"
    "golang.org/x/sys/windows/svc"
    "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
    "github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
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

func UpdateConfigIPSubnet(args []string, flags map[string]interface{}) {
	CIDRstring := flags["CIDR"]
	AddDns := flags["AddDns"]
	log.Info("Updating the config file")
	var cidr []string
	var ipMask int
	var err error

	updateTunIpv4Payload := make(map[string]interface{})
	if CIDRstring != "" {
		cidr = strings.Split(CIDRstring.(string), "/")
		if len(cidr) != 2 {
			log.Error("Incorrect cidr")
		}
		ipMask, err = strconv.Atoi(cidr[1])
		if err != nil {
			log.Errorf("Incorrect ipv4 mask, %s", cidr[1])
			return
		}
		updateTunIpv4Payload["TunIPv4"] = cidr[0]
		updateTunIpv4Payload["TunIPv4Mask"] = ipMask
	}
	if AddDns != "" {
		var addDnsBool bool
		addDnsBool, err = strconv.ParseBool(AddDns.(string))

		if err != nil {
			log.Errorf("Incorrect addDns %v", err)
			return
		}
		updateTunIpv4Payload["AddDns"] = addDnsBool
	}

	UPDATE_TUN_IPV4.Payload = updateTunIpv4Payload
	log.Debugf("updateTunIpv4 Payload %v", UPDATE_TUN_IPV4)
	
	status := GetDataFromIpcPipe(&UPDATE_TUN_IPV4, nil, GetResponseObjectFromRTS, args, nil)

	if !status {
		log.Infof("Updating ip and mask in the config file. Manual restart is required")
		var tunIpv4 string
		if len(cidr) == 2 {
			tunIpv4 = cidr[0]
		}
		err = service.UpdateRuntimeStateIpv4(true, tunIpv4, ipMask, AddDns.(string))
		if err != nil {
			log.Errorf("Unable to set Tun ip and mask, %v", err)
			return
		}
		log.Infof("ip and mask are set")
	}

	if status {
		log.Infof("Attempting to restart ziti ")

		err = service.ControlService(svc.Stop, svc.Stopped)
		if (err != nil) {
			log.Errorf("Unable to stop the service, %v", err)
			return	
		}
		err = service.StartService()
		if (err != nil) {
			log.Errorf("Unable to start the service, %v", err)
		}
	}

}

