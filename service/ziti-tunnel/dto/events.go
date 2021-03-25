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
	UPDATED = "updated"

	SERVICE_OP  = "service"
	IDENTITY_OP = "identity"
	MFA_OP      = "mfa"

	MFAEnrollmentChallengAtion		= "enrollment_challenge"
	MFAEnrollmentVerificationAction	= "enrollment_verification"
	MFAEnrollmentRemovedAction		= "enrollment_remove"

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
var IdentityUpdateComplete = ActionEvent{
	StatusEvent: StatusEvent{Op: IDENTITY_OP},
	Action:      UPDATED,
}

var MFAEnrollmentChallengeEvent = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFAEnrollmentChallengAtion,
}
var MFAEnrollmentVerificationEvent = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFAEnrollmentVerificationAction,
}
var MFAEnrollmentRemovedEvent = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFAEnrollmentRemovedAction,
}
var MFAErrorEvent = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      ERROR,
}

var MFAAuthChallengeEvent = ActionEvent{
	StatusEvent: StatusEvent{Op: MFA_OP},
	Action:      MFA_AUTH_CHALLENGE_ACTION,
}
