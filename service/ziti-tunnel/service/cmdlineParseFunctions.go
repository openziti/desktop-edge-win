package service

import (
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"text/template"

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

	var filteredIdentities []IdentityCli

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
		return dto.Response{Message: "", Code: ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Listing %d identities - %s", len(filteredIdentities), args)
	return generateResponse("identities", message, filteredIdentities, flags, templateIdentity)
}

func filterServicesByIdentity(identity []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {
	var filteredServices []ServiceCli

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
		return dto.Response{Message: "", Code: ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Listing %d services for identity - %s", len(filteredServices), identity)
	return generateResponse("services", message, filteredServices, flags, templateService)

}

func GetServicesFromRTS(args []string, status *dto.TunnelStatus, flags map[string]bool) dto.Response {

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
		return dto.Response{Message: "", Code: ERROR, Error: errMsg, Payload: nil}
	}
	message := fmt.Sprintf("Listing %d services - %s", len(filteredServices), args)

	return generateResponse("services", message, filteredServices, flags, templateService)

}

func generateResponse(dataType string, message string, filteredData interface{}, flags map[string]bool, templateStr string) dto.Response {

	var bytesData []byte
	var err error

	if flags["prettyJSON"] == true {
		bytesData, err = json.MarshalIndent(filteredData, "", "	")

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: ERROR, Error: "Could not fetch " + dataType + " from Runtime", Payload: nil}
		}

	} else {
		it, err := template.New("filteredData").Parse(templateStr)

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: ERROR, Error: "Could not parse " + dataType + " from Runtime", Payload: nil}
		}

		err = it.Execute(os.Stdout, filteredData)

		if err != nil {
			log.Error(err)
			return dto.Response{Message: message, Code: ERROR, Error: "Could not print " + dataType + " from Runtime", Payload: nil}
		}
	}

	responseStr := string(bytesData)

	return dto.Response{Message: message, Code: SUCCESS, Error: "", Payload: responseStr}
}
