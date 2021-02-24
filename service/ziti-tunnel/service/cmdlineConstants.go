package service

import "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"

var GET_STATUS = dto.CommandMsg{
	Function: "Status",
}

type IdentityCli struct {
	Name              string
	FingerPrint       string
	Active            bool
	Config            string
	ControllerVersion string
	Status            string
}

type ServiceCli struct {
	Name          string
	AssignedIP    string
	InterceptHost string
	InterceptPort uint16
	Id            string
	AssignedHost  string
	OwnsIntercept bool
}
