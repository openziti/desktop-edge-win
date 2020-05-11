package service

import (
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"wintun-testing/ziti-tunnel/dto"
)

func dbg() {

	r := rts.ToStatus()
	events.broadcast <- dto.TunnelStatusEvent{
		StatusEvent: dto.StatusEvent{Op: "status"},
		Status:      r,
	}


	svcs := make([]*dto.Service, 2)
	svcs[0] = &dto.Service{
		Name:     "FakeService1",
		HostName: "fake-service.com",
		Port:     1234,
	}
	svcs[1] = &dto.Service{
		Name:     "Second Fake Service",
		HostName: "some-other-host.ziti",
		Port:     5555,
	}

	events.broadcast <- dto.IdentityEvent{
		ActionEvent: IDENTITY_ADDED,
		Id:          dto.Identity{
			Name:        "NewIdentity",
			FingerPrint: "new_id_fingerprint",
			Active:      true,
			Config:      idcfg.Config{
				ZtAPI:       "http://new_id.com:2123",
			},
			Status:      STATUS_ENROLLED,
			Services:    svcs,
			Metrics:     nil,
		},
	}

	events.broadcast <- dto.ServiceEvent{
		ActionEvent: SERVICE_ADDED,
		Service:     dto.Service{
			Name:     "New Service",
			HostName: "some new hostname",
			Port:     5000,
		},
		Fingerprint: "new_id_fingerprint",
	}

	events.broadcast <- dto.ServiceEvent{
		ActionEvent: SERVICE_REMOVED,
		Service:     dto.Service{
			Name:     "New Service",
		},
		Fingerprint: "new_id_fingerprint",
	}

	events.broadcast <- dto.IdentityEvent{
		ActionEvent: IDENTITY_REMOVED,
		Id:          dto.Identity{
			Name:        "",
			FingerPrint: "new_id_fingerprint",
			Active:      false,
			Config:      idcfg.Config{},
			Status:      "",
			Services:    nil,
			Metrics:     nil,
			Connected:   false,
			NFContext:   nil,
		},
	}
}
