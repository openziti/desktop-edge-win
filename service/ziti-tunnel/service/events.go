package service

import "service/ziti-tunnel/dto"

const (
	ADDED = "added"
	REMOVED = "removed"
	SERVICE_OP = "service"
	IDENTITY_OP = "identity"
)

var SERVICE_ADDED = dto.ActionEvent{
	StatusEvent: dto.StatusEvent{Op: SERVICE_OP},
	Action:      ADDED,
}
var SERVICE_REMOVED = dto.ActionEvent{
	StatusEvent: dto.StatusEvent{Op: SERVICE_OP},
	Action:      REMOVED,
}

var IDENTITY_ADDED = dto.ActionEvent{
	StatusEvent: dto.StatusEvent{Op: IDENTITY_OP},
	Action:      ADDED,
}
var IDENTITY_REMOVED = dto.ActionEvent{
	StatusEvent: dto.StatusEvent{Op: IDENTITY_OP},
	Action:      REMOVED,
}