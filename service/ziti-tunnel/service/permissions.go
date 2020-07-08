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

import (
	"github.com/openziti/foundation/util/stringz"
	"os/user"
)

func EnsurePermissions(group string) string {
	log.Infof("verifying process has access to the correct group: %s", group)

	//check the current process is in this group
	g, err := user.LookupGroup(group)
	if err != nil || g == nil {
		log.Fatalf("the necessary group [%s] was not found on this machine. ensure the group exists and start the process again.", group)
		return "" //quiets the nil warning below even though this code won't get hit
	}

	sid := g.Gid
	log.Debugf("sid for %s is %s", group, sid)

	u, err := user.Current()
	if err != nil || u == nil {
		log.Fatal("could not acquire current user! user: %v %v", u, err)
		return "" //quiets the nil warning below even though this code won't get hit
	}

	gr, err := u.GroupIds()
	if err != nil {
		log.Fatal("could not acquire groups for current user!", err)
	}

	if stringz.Contains(gr, sid) {
		log.Debugf("user: %s is in the required group: %s", u.Username, NF_GROUP_NAME)
	} else {
		log.Fatalf("Token Membership Error: %s", err)
	}

	return sid
}