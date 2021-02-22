package service

import "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"

const (
	IDENTITIES = "ListIdentities"
)

var LIST_IDENTITIES = dto.CommandMsg{
	Function: IDENTITIES,
	Payload: map["args"]"all",
}
