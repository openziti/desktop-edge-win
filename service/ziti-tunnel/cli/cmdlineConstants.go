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
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/logging"
)

var GET_STATUS = dto.CommandMsg{
	Function: "Status",
}

var templateIdentity = `{{printf "%40s" "Name"}} | {{printf "%41s" "FingerPrint"}} | {{printf "%6s" "Active"}} | {{printf "%30s" "Config"}} | {{"Status"}}
{{range .}}{{printf "%40s" .Name}} | {{printf "%41s" .FingerPrint}} | {{printf "%6t" .Active}} | {{printf "%30s" .Config}} | {{.Status}}
{{end}}`

var templateService = `{{printf "%40s" "Name"}} | {{printf "%15s" "Id"}} | {{printf "%40s" "InterceptHost"}} | {{printf "%14s" "InterceptPort"}} | {{printf "%15s" "AssignedIP"}} | {{printf "%15s" "AssignedHost"}} | {{"OwnsIntercept"}}
{{range .}}{{printf "%40s" .Name}} | {{printf "%15s" .Id}} | {{printf "%40s" .InterceptHost}} | {{printf "%14d" .InterceptPort}} | {{printf "%15s" .AssignedIP}} | {{printf "%15s" .AssignedHost}} | {{.OwnsIntercept}}
{{end}}`

var log = logging.Logger()
