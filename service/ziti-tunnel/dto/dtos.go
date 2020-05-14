package dto

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-sdk-golang/ziti/enroll"
	"log"
	"github.com/netfoundry/ziti-tunnel-win/service/cziti"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/config"
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
	Services    []*Service `json:",omitempty"`
	Metrics     *Metrics `json:",omitempty"`

	Connected bool            `json:"-"`
	NFContext *cziti.CZitiCtx `json:"-"`
}
type Metrics struct {
	Up   int64
	Down int64
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

type TunIpInfo struct {
	Ip     string
	Subnet string
	MTU    uint16
	DNS    string
}

func (id *Identity) Path() string {
	if id.FingerPrint == "" {
		log.Fatalf("fingerprint is invalid for id %s", id.Name)
	}
	return config.Path() + id.FingerPrint + ".json"
}

type TunnelStatus struct {
	Active     bool
	Duration   int64
	Identities []*Identity
	IpInfo     *TunIpInfo `json:"IpInfo,omitempty"`
	LogLevel   string
}

type ZitiTunnelStatus struct {
	Status  *TunnelStatus `json:",omitempty"`
	Metrics *Metrics      `json:",omitempty"`
}

type StatusEvent struct {
	Op      string
}

type ActionEvent struct {
	StatusEvent
	Action string
}

type TunnelStatusEvent struct {
	StatusEvent
	Status TunnelStatus
}

type MetricsEvent struct {
	StatusEvent
	Identities []*Identity
}

type ServiceEvent struct {
	ActionEvent
	Fingerprint string
	Service Service
}

type IdentityEvent struct {
	ActionEvent
	Id Identity
}