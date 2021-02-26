package cli

import (
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/logging"
)

var GET_STATUS = dto.CommandMsg{
	Function: "Status",
}

var templateIdentity = "{{printf \"%40s\" \"Name\"}} | {{printf \"%41s\" \"FingerPrint\"}} | {{printf \"%6s\" \"Active\"}} | {{printf \"%30s\" \"Config\"}} | {{\"Status\"}}\n" +
	"{{range .}}{{printf \"%40s\" .Name}} | {{printf \"%41s\" .FingerPrint}} | {{printf \"%6t\" .Active}} | {{printf \"%30s\" .Config}} | {{.Status}}\n{{end}}"

var templateService = "{{printf \"%40s\" \"Name\"}} | {{printf \"%15s\" \"Id\"}} | {{printf \"%40s\" \"InterceptHost\"}} | {{printf \"%14s\" \"InterceptPort\"}} | {{printf \"%15s\" \"AssignedIP\"}} | {{printf \"%15s\" \"AssignedHost\"}} | {{\"OwnsIntercept\"}}\n" +
	"{{range .}}{{printf \"%40s\" .Name}} | {{printf \"%15s\" .Id}} | {{printf \"%40s\" .InterceptHost}} | {{printf \"%14d\" .InterceptPort}} | {{printf \"%15s\" .AssignedIP}} | {{printf \"%15s\" .AssignedHost}} | {{.OwnsIntercept}}\n{{end}}"

var log = logging.Logger()
