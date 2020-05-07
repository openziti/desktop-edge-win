package idutil

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"wintun-testing/cziti"
	"wintun-testing/ziti-tunnel/dto"
)

//Removes the Config from the provided identity and returns a 'cleaned' id
func Clean(id dto.Identity) dto.Identity {
	nid := id
	nid.Config = idcfg.Config{}
	nid.Config.ZtAPI = id.Config.ZtAPI
	nid.Services = make([]*dto.Service, len(id.Services))
	for i, svc := range id.Services{
		nid.Services[i] = svc
	}
	GetMetrics(nid)
	return nid
}

func GetMetrics(id dto.Identity) /*dto.Metrics*/ {
	up, down := cziti.GetTransferRates(id.NFContext)
	id.Metrics.Up = up
	id.Metrics.Down = down
	/*
	return dto.Metrics{
		Up:   up,
		Down: down,
	}
	*/
}
