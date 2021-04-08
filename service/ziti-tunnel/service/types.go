package service

import (
	"github.com/openziti/desktop-edge-win/service/cziti"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

type Id struct {
	dto.Identity
	CId *cziti.ZIdentity
}
