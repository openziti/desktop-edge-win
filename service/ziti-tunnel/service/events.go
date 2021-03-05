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
package service

import "github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"

const (
	ADDED       = "added"
	REMOVED     = "removed"
	SERVICE_OP  = "service"
	IDENTITY_OP = "identity"
	LOGLEVEL_OP = "logLevel"
	CHANGED     = "changed"
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
var LOGLEVEL_CHANGED = dto.ActionEvent{
	StatusEvent: dto.StatusEvent{Op: LOGLEVEL_OP},
	Action:      CHANGED,
}
