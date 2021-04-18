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
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"net"
	"strconv"
	"strings"
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

	updateTunIpv4Payload := make(map[string]interface{})
	if CIDRstring != "" {
		ip, ipnet, err := net.ParseCIDR(CIDRstring.(string))
		if err != nil {
			log.Error("Incorrect cidr %s", CIDRstring.(string))
		}
		ipMask, _ := ipnet.Mask.Size()

		updateTunIpv4Payload["TunIPv4"] = ip
		updateTunIpv4Payload["TunIPv4Mask"] = ipMask
	}
	if AddDns != "" {
		addDnsBool, err := strconv.ParseBool(AddDns.(string))

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
		log.Infof("Config can be updated only if the tunnel is started")
	}

}
