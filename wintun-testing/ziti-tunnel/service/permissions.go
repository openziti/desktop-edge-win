package service

import (
	"github.com/netfoundry/ziti-foundation/util/stringz"
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