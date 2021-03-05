package cli

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

import (
	"bytes"
	"encoding/json"
	"fmt"
	"strings"
	"text/template"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
)

func convertToIdentityCli(id *dto.Identity) dto.IdentityCli {
	return dto.IdentityCli{
		Name:        id.Name,
		FingerPrint: id.FingerPrint,
		Active:      id.Active,
		Config:      id.Config.ZtAPI,
		Status:      id.Status,
	}
}

func convertToServiceCli(svc dto.Service) dto.ServiceCli {
	return dto.ServiceCli{
		Name:          svc.Name,
		Id:            svc.Id,
		InterceptHost: svc.InterceptHost,
		InterceptPort: svc.InterceptPort,
		AssignedIP:    svc.AssignedIP,
		AssignedHost:  svc.AssignedHost,
		OwnsIntercept: svc.OwnsIntercept,
	}
}

// GetIdentitiesFromRTS is to get identities from the RTS
func GetIdentitiesFromRTS(args []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {

	var filteredIdentities []dto.IdentityCli

	if flags["services"] {
		return filterServicesByIdentity(args, status, flags)
	}

	for _, val := range args {
		if val == "all" {
			for _, id := range status.Identities {
				filteredIdentities = append(filteredIdentities, convertToIdentityCli(id))
			}
			break
		} else {
			for _, id := range status.Identities {
				if strings.Compare(id.Name, val) == 0 {
					filteredIdentities = append(filteredIdentities, convertToIdentityCli(id))
				}
			}
		}
	}

	if len(filteredIdentities) == 0 {
		errMsg := fmt.Sprintf("Could not find identities matching %s", args)
		return dto.Response{Message: "", Code: service.ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Got %d identities - %s", len(filteredIdentities), args)
	return generateResponse("identities", message, filteredIdentities, flags, templateIdentity)
}

func filterServicesByIdentity(identity []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {
	var filteredServices []dto.ServiceCli

	for _, id := range status.Identities {
		for _, filterID := range identity {
			if (filterID == "all" || id.Name == filterID) && len(id.Services) > 0 {
				for _, svc := range id.Services {
					filteredServices = append(filteredServices, convertToServiceCli(*svc))
				}
				// if the filterId array has all or matching string, then fetch all the services and break from the filter loop
				break
			}
		}

	}

	if len(filteredServices) == 0 {
		errMsg := fmt.Sprintf("Could not find services for identity %s", identity)
		return dto.Response{Message: "", Code: service.ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Got %d services for identity - %s", len(filteredServices), identity)
	return generateResponse("services", message, filteredServices, flags, templateService)

}

func GetServicesFromRTS(args []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {

	var filteredServices []dto.ServiceCli

	for _, val := range args {
		if val == "all" {
			for _, id := range status.Identities {
				if len(id.Services) > 0 {
					for _, svc := range id.Services {
						filteredServices = append(filteredServices, convertToServiceCli(*svc))
					}
				}
			}
			break
		} else {
			for _, id := range status.Identities {
				if len(id.Services) > 0 {
					for _, svc := range id.Services {
						if strings.Compare(val, svc.Name) == 0 {
							filteredServices = append(filteredServices, convertToServiceCli(*svc))
						}
					}
				}
			}
		}
	}

	if len(filteredServices) == 0 {
		errMsg := fmt.Sprintf("Could not find services matching %s", args)
		return dto.Response{Message: "", Code: service.ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Got %d services - %s", len(filteredServices), args)

	return generateResponse("services", message, filteredServices, flags, templateService)

}

func generateResponse(dataType string, message string, filteredData interface{}, flags map[string]bool, templateStr string) dto.Response {

	var bytesData []byte
	var err error
	var responseBuffer bytes.Buffer
	var responseStr string

	if flags["prettyJSON"] == true {
		bytesData, err = json.MarshalIndent(filteredData, "", "	")

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: service.ERROR, Error: "Could not fetch " + dataType + " from Runtime", Payload: nil}
		}
		responseStr = string(bytesData)

	} else {
		it, err := template.New("filteredData").Parse(templateStr)

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: service.ERROR, Error: "Could not parse " + dataType + " from Runtime", Payload: nil}
		}

		err = it.Execute(&responseBuffer, filteredData)

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: service.ERROR, Error: "Could not print " + dataType + " from Runtime", Payload: nil}
		}
		responseStr = responseBuffer.String()
	}

	return dto.Response{Message: message, Code: service.SUCCESS, Error: "", Payload: responseStr}
}

func GetLogLevelFromRTS(args []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {

	if flags["query"] == true {
		message := fmt.Sprintf("Loglevel is currently set to %s", status.LogLevel)
		return dto.Response{Message: message, Code: service.SUCCESS, Error: "", Payload: ""}
	}
	errMsg := fmt.Sprintf("Unknown error: args %s flag %v", args, flags)
	return dto.Response{Message: "", Code: service.ERROR, Error: errMsg, Payload: ""}

}

// GetIdentityResponseObjectFromRTS is to get identity info from the RTS
func GetIdentityResponseObjectFromRTS(args []string, status dto.Response, flags map[string]bool) dto.Response {
	log.Debugf("Message from ziti-tunnel : %v", status.Message)
	if status.Error == "" && status.Payload != nil {
		log.Debugf("Payload from RTS %v", status.Payload)
		payloadData := status.Payload.(map[string]interface{})
		identityStatus := make(map[string]interface{})
		identityStatus["FingerPrint"] = payloadData["FingerPrint"]
		identityStatus["Active"] = payloadData["Active"]
		identityStatus["Name"] = payloadData["Name"]
		return dto.Response{Message: status.Message, Code: service.SUCCESS, Error: "", Payload: identityStatus}
	} else {
		return status
	}
}

// GetResponseObjectFromRTS is to get response object info from the RTS
func GetResponseObjectFromRTS(args []string, status dto.Response, flags map[string]bool) dto.Response {
	return status
}
