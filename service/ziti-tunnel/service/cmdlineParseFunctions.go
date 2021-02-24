package service

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

func convertToIdentityCli(id *dto.Identity) IdentityCli {
	return IdentityCli{
		Name:        id.Name,
		FingerPrint: id.FingerPrint,
		Active:      id.Active,
		Config:      id.Config.ZtAPI,
		Status:      id.Status,
	}
}

func convertToServiceCli(svc dto.Service) ServiceCli {
	return ServiceCli{
		Name:          svc.Name,
		AssignedIP:    svc.AssignedIP,
		Id:            svc.Id,
		InterceptHost: svc.InterceptHost,
		InterceptPort: svc.InterceptPort,
		AssignedHost:  svc.AssignedHost,
		OwnsIntercept: svc.OwnsIntercept,
	}
}

// GetIdentitiesFromRTS is to get identities from the RTS
func GetIdentitiesFromRTS(args []string, status *dto.TunnelStatus, flag string) dto.Response {
	message := fmt.Sprintf("Listing Identities - %s", args)

	var filteredIdentities []IdentityCli

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
		errMsg := fmt.Sprintf("Could not find Identities matching %s", args)
		return dto.Response{Message: message, Code: ERROR, Error: errMsg, Payload: nil}
	}
	identitiesBytes, err := json.Marshal(filteredIdentities)
	if err != nil {
		log.Error(err)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not fetch Identities from Runtime", Payload: nil}
	}

	identitiesStr := string(identitiesBytes)

	return dto.Response{Message: message, Code: SUCCESS, Error: "", Payload: identitiesStr}
}

func GetServicesFromRTS(args []string, status *dto.TunnelStatus, flag string) dto.Response {
	message := fmt.Sprintf("Listing Services - %s", args)

	var filteredServices []ServiceCli

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
		return dto.Response{Message: message, Code: ERROR, Error: errMsg, Payload: nil}
	}
	servicesBytes, err := json.Marshal(filteredServices)
	if err != nil {
		log.Error(err)
		return dto.Response{Message: message, Code: ERROR, Error: "Could not fetch services from Runtime", Payload: nil}
	}

	servicesStr := string(servicesBytes)

	return dto.Response{Message: message, Code: SUCCESS, Error: "", Payload: servicesStr}
}
