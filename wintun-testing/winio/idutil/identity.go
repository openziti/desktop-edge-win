package idutil

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"wintun-testing/winio/dto"
)

//Removes the Config from the provided identity and returns a 'cleaned' id
func Clean(id dto.Identity) *dto.Identity {
	nid := id
	nid.Config = idcfg.Config{}
	nid.Config.ZtAPI = id.Config.ZtAPI
	nid.Services = make([]*dto.Service, len(id.Services))
	for i, svc := range id.Services{
		nid.Services[i] = svc
	}
	return &nid
}
