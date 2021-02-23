package service

import "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"

var LIST_IDENTITIES = dto.CommandMsg{
	Function: "ListIdentities",
}

var LIST_SERVICES = dto.CommandMsg{
	Function: "ListServices",
}
