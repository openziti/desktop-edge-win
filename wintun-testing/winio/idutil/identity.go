package idutil

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"wintun-testing/winio/dto"
)

//Removes the Config from the provided identity and returns a 'cleaned' id
func Clean(id dto.Identity) dto.Identity {
	nid := id
	nid.Config = idcfg.Config{}
	nid.Config.ZtAPI = id.Config.ZtAPI
	return nid
}
