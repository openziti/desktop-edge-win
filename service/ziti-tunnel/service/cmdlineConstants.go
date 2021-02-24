package service

import "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"

var GET_STATUS = dto.CommandMsg{
	Function: "Status",
}

var identityFields = []string{"Name", "FingerPrint", "Active", "ControllerVersion", "Status", "Config"}
