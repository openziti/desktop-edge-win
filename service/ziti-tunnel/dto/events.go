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
package dto

const (
	ADDED   = "added"
	REMOVED = "removed"
	ERROR   = "error"

	SERVICE_OP  = "service"
	IDENTITY_OP = "identity"
	MFA_OP      = "mfa"

	MFA_ENROLL_CHALLENGE_ACTION = "enrollment_challenge"
	MFA_AUTH_CHALLENGE_ACTION   = "auth_challenge"
)

var SERVICE_ADDED = ActionEvent{
	StatusEvent: StatusEvent{Op: SERVICE_OP},
	Action:      ADDED,
}
var SERVICE_REMOVED = ActionEvent{
	StatusEvent: StatusEvent{Op: SERVICE_OP},
	Action:      REMOVED,
}

var IDENTITY_ADDED = ActionEvent{
	StatusEvent: StatusEvent{Op: IDENTITY_OP},
	Action:      ADDED,
}
var IDENTITY_REMOVED = ActionEvent{
	StatusEvent: StatusEvent{Op: IDENTITY_OP},
	Action:      REMOVED,
}

var MFA_ENROLLMENT_CHALLENGE = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFA_ENROLL_CHALLENGE_ACTION,
}
var MFA_ERROR = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      ERROR,
}

var MFA_CHALLENGE = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFA_AUTH_CHALLENGE_ACTION,
}
