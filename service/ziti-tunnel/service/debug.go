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
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/dto"
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
			ZitiContext:   nil,
		},
	}
}
