package dto

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
)

type AddIdentity struct {
	EnrollmentFlags enroll.EnrollmentFlags `json:"Flags"`
	Id              Identity               `json:"Id"`
}

type Service struct {
	Name     string
	HostName string
	Port     uint16
}

type Identity struct {
	Name        string
	FingerPrint string
	Active      bool
	Config      idcfg.Config
	Status      string
	Services    []*Service
	Metrics     Metrics
}
type Metrics struct {
	TotalBytes int64
}
type CommandMsg struct {
	Function string
	Payload  map[string]interface{}
}
type Response struct {
	Code    int
	Message string
	Error   string
	Payload interface{} `json:"Payload"`
}
type ZitiTunnelStatus struct {
	Code    int
	Message string
	Error   string
	Payload Identity
}