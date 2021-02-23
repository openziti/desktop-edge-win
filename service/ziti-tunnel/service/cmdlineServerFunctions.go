package service

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

// GetIdentitiesFromRTS is to get identities from the RTS
func GetIdentitiesFromRTS(args []string) dto.Response {
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

func GetServicesFromRTS(args []string) dto.Response {
	message := fmt.Sprintf("Listing Services - %s", args)

	var filteredServices []*dto.Service

	for _, val := range args {
		if val == "all" {
			filteredServices = []*dto.Service{}

			for _, id := range rts.state.Identities {
				if len(id.Services) > 0 {
					filteredServices = append(filteredServices, id.Services[0:len(id.Services)]...)
				}
			}
			break
		} else {
			for _, id := range rts.state.Identities {
				if len(id.Services) > 0 {
					for index, svc := range id.Services {
						if strings.Compare(val, svc.Name) == 0 {
							filteredServices = append(filteredServices, id.Services[index])
						}
					}
				}
			}
		}
	}
	if len(filteredServices) == 0 {
		errMsg := fmt.Sprintf("Could not find services matching %s", args)
		return dto.Response{Message: message, Code: ERROR, Error: errMsg, Payload: nil}
	}
	services, err := json.Marshal(filteredServices)
	if err != nil {
		log.Error(err)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not fetch services from Runtime", Payload: nil}
	}
	var servicesMapList []map[string]interface{}
	json.Unmarshal(services, &servicesMapList)

	/*for _, servicesMap := range servicesMapList {
		for field, _ := range servicesMap {
			if field != "Name" && field != "FingerPrint" && field != "Active" && field != "ControllerVersion" && field != "Status" && field != "Config" {
				delete(servicesMap, field)
			}
		}
	} */

	servicesBytes, mapErr := json.Marshal(servicesMapList)
	if mapErr != nil {
		log.Error(mapErr)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not transform services to json", Payload: nil}
	}

	servicesStr := string(servicesBytes)
	log.Infof("RTS %s", servicesStr)

	return dto.Response{Message: message, Code: SUCCESS, Error: "", Payload: servicesStr}
}
