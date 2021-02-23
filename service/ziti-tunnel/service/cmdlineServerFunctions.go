package service

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

// GetIdentityFromRTS is to get identities from the RTS
func GetIdentityFromRTS(args []string) dto.Response {
	message := fmt.Sprintf("Listing Identities - %s", args)

	filteredIdentities := []*dto.Identity{}

	for _, val := range args {
		if val == "all" {
			filteredIdentities = rts.state.Identities
			break
		} else {
			for _, id := range rts.state.Identities {
				if strings.Compare(id.Name, val) == 0 {
					filteredIdentities = append(filteredIdentities, id)
				}
			}
		}
	}
	if len(filteredIdentities) == 0 {
		errMsg := fmt.Sprintf("Could not find Identities matching %s", args)
		return dto.Response{Message: message, Code: ERROR, Error: errMsg, Payload: nil}
	}
	identities, err := json.Marshal(filteredIdentities)
	if err != nil {
		log.Error(err)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not fetch Identities from Runtime", Payload: nil}
	}
	var identitiesMapList []map[string]interface{}
	json.Unmarshal(identities, &identitiesMapList)

	for _, identitiesMap := range identitiesMapList {
		for field, _ := range identitiesMap {
			if field != "Name" && field != "FingerPrint" && field != "Active" && field != "ControllerVersion" && field != "Status" && field != "Config" {
				delete(identitiesMap, field)
			}
		}
	}

	identitiesBytes, mapErr := json.Marshal(identitiesMapList)
	if mapErr != nil {
		log.Error(mapErr)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not transform Identities to json", Payload: nil}
	}

	identitiesStr := string(identitiesBytes)
	log.Infof("RTS %s", identitiesStr)

	return dto.Response{Message: message, Code: SUCCESS, Error: "", Payload: identitiesStr}
}
