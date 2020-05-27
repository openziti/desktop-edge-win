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

package idutil

import (
	"github.com/michaelquigley/pfxlog"
	idcfg "github.com/netfoundry/ziti-sdk-golang/ziti/config"
	"github.com/netfoundry/ziti-tunnel-win/service/cziti"
	"github.com/netfoundry/ziti-tunnel-win/service/ziti-tunnel/dto"
)

var log = pfxlog.Logger()

//Removes the Config from the provided identity and returns a 'cleaned' id
func Clean(id dto.Identity) dto.Identity {
	log.Tracef("cleaning identity: %s", id.Name)
	nid := id
	nid.Config = idcfg.Config{}
	nid.Config.ZtAPI = id.Config.ZtAPI
	nid.Services = make([]*dto.Service, len(id.Services))
	for i, svc := range id.Services{
		nid.Services[i] = svc
	}
	AddMetrics(&nid)
	log.Tracef("Up: %v Down %v", nid.Metrics.Up, nid.Metrics.Down)
	return nid
}

func AddMetrics(id *dto.Identity) {
	up, down, _ := cziti.GetTransferRates(id.NFContext)

	id.Metrics = &dto.Metrics{
		Up:   up,
		Down: down,
	}
}
