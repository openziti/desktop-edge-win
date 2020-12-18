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

package cziti

/*
#cgo windows LDFLAGS: -l libziti.imp -luv -lws2_32 -lpsapi

#include <ziti/ziti.h>
#include "ziti/ziti_tunnel.h"
#include "ziti/ziti_log.h"

#include "sdk.h"
extern void initCB(ziti_context nf, int status, void *ctx);
extern void serviceCB(ziti_context nf, ziti_service*, int status, void *ctx);
extern void shutdown_callback(uv_async_t *handle);
extern void free_async(uv_handle_t* timer);

extern void c_mapiter(model_map *map);

*/
import "C"
import (
	"encoding/json"
	"errors"
	"sync"
	"unsafe"
)

const (
	ADDED = "added"
	REMOVED = "removed"
)

var ServiceChanges = make(chan ServiceChange, 256)

type sdk struct {
	libuvCtx *C.libuv_ctx
}
type ServiceChange struct {
	Operation   string
	Service     *ZService
	ZitiContext *ZIdentity
}

var _impl sdk

func init() {
	_impl.libuvCtx = (*C.libuv_ctx)(C.calloc(1, C.sizeof_libuv_ctx))
	C.libuv_init(_impl.libuvCtx)
}

func SetLogLevel(level int) {
	log.Infof("Setting cziti log level to: %d", level)
	C.set_log_level(C.int(level), _impl.libuvCtx)
}

func Start(loglevel int) {
	v := C.ziti_get_version()
	log.Infof("starting ziti-sdk-c %s(%s)[%s]", C.GoString(v.version), C.GoString(v.revision), C.GoString(v.build_date))

	_impl.run(loglevel)
}

func (inst *sdk) run(loglevel int) {
	SetLogLevel(loglevel)
	C.libuv_run(inst.libuvCtx)
}

type ZService struct {
	Name           string
	Id             string
	InterceptHost  string
	InterceptPort  uint16
	AssignedIP     string
	OwnsIntercept  bool
	OwnerNetwork   string
	OwnerServiceId string
}

type ZIdentity struct {
	Options     *C.ziti_options
	zctx        C.ziti_context
	zid         *C.ziti_identity
	status      int
	statusErr   error
	Loaded      bool
	Name        string
	Version     string
	Services    sync.Map
	Fingerprint string
	Active      bool
}

func NewZid() *ZIdentity {
	zid := &ZIdentity{}
	zid.Services = sync.Map{}
	zid.Options = (*C.ziti_options)(C.calloc(1, C.sizeof_ziti_options))
	return zid
}

func (c *ZIdentity) GetMetrics() (int64, int64, bool) {
	if c == nil {
		return 0, 0, false
	}
	if C.is_null(unsafe.Pointer(&c.zctx)) {
		log.Warnf("ziti context is C.NULL! %s", c.Fingerprint)
		return 0, 0, false
	}
	var up, down C.double
	C.ziti_get_transfer_rates(c.zctx, &up, &down)

	return int64(up), int64(down), true
}

func (c *ZIdentity) UnsafePointer() unsafe.Pointer {
	return unsafe.Pointer(c.zctx)
}
func (c *ZIdentity) AsKey() string {
	return "marker askey"
}

func (c *ZIdentity) Status() (int, error) {
	return c.status, c.statusErr
}

func (c *ZIdentity) setVersionFromId() string {
	if len(c.Version) > 0 {
		return c.Version
	}
	c.Version = "<unknown version>"
	if c != nil {
		if c.zctx != nil {
			v1 := C.ziti_get_controller_version(c.zctx)
			return C.GoString(v1.version)
		}
	}
	return c.Version
}

func (c *ZIdentity) setNameFromId() string {
	if len(c.Name) > 0 {
		return c.Name
	}
	c.Name = "<unknown>"
	if c != nil {
		if c.zid != nil {
			if c.zid.name != nil {
				c.Name = C.GoString(c.zid.name)
			} else {
				log.Debug("in Name - c.zid.name was nil")
			}
		} else {
			log.Debug("in Name - c.zid was nil")
		}
	} else {
		log.Debug("in Name - c was nil")
	}
	return c.Name
}

func (c *ZIdentity) Tags() []string {
	if c.zctx != nil && c.zid != nil {
		C.c_mapiter(&c.zid.tags)
		/*
		it := C.model_map_iterator(&c.zid.tags)
		for {
			if it != nil {
				k := C.model_map_it_value(it)
				v := C.model_map_it_value(it)
				log.Infof("key: %s. value: %s", k, v)
			} else {
				break
			}
			it = C.model_map_it_next(it) //get the next entry
		}
		return nil*/
	}
	return nil
}

func (c *ZIdentity) Controller() string {
	if c.zctx != nil {
		return C.GoString(C.ziti_get_controller(c.zctx))
	}
	return C.GoString(c.Options.controller)
}

var cTunClientCfgName = C.CString("ziti-tunneler-client.v1")
var cTunServerCfgName = C.CString("ziti-tunneler-server.v1")

//export serviceCB
func serviceCB(_ C.ziti_context, service *C.ziti_service, status C.int, tnlr_ctx unsafe.Pointer) {
	isCnull := tnlr_ctx == C.NULL
	isNil := tnlr_ctx == nil
	if isCnull || isNil {
		log.Errorf("in serviceCB with null tnlr_ctx??? ")
		return
	}
	zid := (*ZIdentity)(tnlr_ctx)

	name := C.GoString(service.name)
	svcId := C.GoString(service.id)
	log.Debugf("============ INSIDE serviceCB - status: %s:%s - %v, %v ============", name, svcId, status, service.perm_flags)
	if status == C.ZITI_SERVICE_UNAVAILABLE {
		serviceUnavailable(zid, svcId, name)
	} else if status == C.ZITI_OK {
		//first thing's first - determine if the service is already in this runtime
		//if it is that means this is 'probably' a config change. to make it easy
		//just dereg/disconnect the service and then let the rest of this code execute
		found, ok := zid.Services.Load(svcId)
		if ok && found != nil {
			log.Infof("service with id: %s, name: %s exists. updating service.", svcId, name)
			fs := found.(ZService)
			ok := DNSMgr.UnregisterService(fs.InterceptHost, fs.InterceptPort)
			if ok {
				zid.Services.Delete(svcId)
			} else {
				log.Warn("unregister service from serviceCB was not ok?")
			}
		} else {
			log.Debugf("new service with id: %s, name: %s in context %d", svcId, name, &zid)
		}

		if C.ZITI_CAN_BIND == ( service.perm_flags & C.ZITI_CAN_BIND ) {
			var v1Config C.ziti_server_cfg_v1
			r := C.ziti_service_get_config(service, cTunServerCfgName, unsafe.Pointer(&v1Config), (*[0]byte)(C.parse_ziti_server_cfg_v1))
			if r == 0 {
				C.ziti_tunneler_host_v1(C.tunneler_context(theTun.tunCtx), unsafe.Pointer(zid.zctx), service.name, v1Config.protocol, v1Config.hostname, v1Config.port)
				C.free_ziti_server_cfg_v1(&v1Config)
			} else {
				log.Infof("service is bindable but doesn't have config? %s. flags: %v.", name, service.perm_flags)
			}
		}

		cfg := C.ziti_service_get_raw_config(service, cTunClientCfgName)

		host := ""
		port := -1
		if cfg != nil {
			var c map[string]interface{}

			if err := json.Unmarshal([]byte(C.GoString(cfg)), &c); err == nil {
				host = c["hostname"].(string)
				port = int(c["port"].(float64))
			}
		}

		if host != "" && port != -1 {
			ip, ownsIntercept, err := DNSMgr.RegisterService(svcId, host, uint16(port), zid, name)
			if err != nil {
				log.Warn(err)
				log.Infof("service intercept beginning for service: %s@%s:%d on ip %s", name, host, port, ip.String())
				AddIntercept(svcId, name, ip.String(), port, unsafe.Pointer(zid.zctx))
			} else {
				log.Infof("service intercept beginning for service: %s@%s:%d on ip %s", name, host, port, ip.String())
				AddIntercept(svcId, name, ip.String(), port, unsafe.Pointer(zid.zctx))
			}
			added := ZService{
				Name:          name,
				Id:            svcId,
				InterceptHost: host,
				InterceptPort: uint16(port),
				AssignedIP:    ip.String(),
				OwnsIntercept: ownsIntercept,
			}
			zid.Services.Store(svcId, added)
			ServiceChanges <- ServiceChange{
				Operation:   ADDED,
				Service:     &added,
				ZitiContext: zid,
			}
		} else {
			log.Debugf("service named %s is not enabled for 'tunneling'. host:%s port:%d", name, host, port)
		}
	}
}

func serviceUnavailable(ctx *ZIdentity, svcId string, name string) {
	found, ok := ctx.Services.Load(svcId)
	if ok {
		fs := found.(ZService)
		ok := DNSMgr.UnregisterService(fs.InterceptHost, fs.InterceptPort)
		if !ok {
			log.Warn("unregister service from serviceUnavailable was not ok?")
		}
		ctx.Services.Delete(svcId)
		ServiceChanges <- ServiceChange{
			Operation: REMOVED,
			Service:   &fs,
			ZitiContext: ctx,
		}
	} else {
		log.Warnf("could not remove service? service not found with id: %s, name: %s in context %d", svcId, name, &ctx)
	}
}

//export initCB
func initCB(nf C.ziti_context, status C.int, data unsafe.Pointer) {
	ctx := (*ZIdentity)(data)

	ctx.zctx = nf
	if nf != nil {
		ctx.zid = C.ziti_get_identity(nf)
	}
	ctx.Options.ctx = data
	ctx.status = int(status)
	ctx.statusErr = zitiError(status)

	ctx.Name = ctx.setNameFromId()
	ctx.Version = ctx.setVersionFromId()

	log.Infof("connected to controller %s running %v", ctx.Name, ctx.Version)
	cfg := C.GoString(ctx.Options.config)
	if ch, ok := initMap[cfg]; ok {
		ch <- ctx
	} else {
		log.Warn("response channel not found")
	}
}

var initMap = make(map[string]chan *ZIdentity)

func zitiError(code C.int) error {
	if int(code) != 0 {
		return errors.New(C.GoString(C.ziti_errorstr(code)))
	}
	return nil
}

func LoadZiti(cfg string, isActive bool) *ZIdentity {
	ctx := NewZid()// &ZIdentity{}

	ctx.Active = isActive

	ctx.Options.config = C.CString(cfg)
	ctx.Options.init_cb = C.ziti_init_cb(C.initCB)
	ctx.Options.service_cb = C.ziti_service_cb(C.serviceCB)
	ctx.Options.refresh_interval = C.long(15)
	ctx.Options.metrics_type = C.INSTANT
	ctx.Options.config_types = C.all_configs
	ctx.Options.pq_domain_cb = C.ziti_pq_domain_cb(C.ziti_pq_domain_go)
	ctx.Options.pq_mac_cb = C.ziti_pq_mac_cb(C.ziti_pq_mac_go)
	ctx.Options.pq_os_cb = C.ziti_pq_os_cb(C.ziti_pq_os_go)
	ctx.Options.pq_process_cb = C.ziti_pq_process_cb(C.ziti_pq_process_go)

	ch := make(chan *ZIdentity)
	initMap[cfg] = ch
	rc := C.ziti_init_opts(ctx.Options, _impl.libuvCtx.l, unsafe.Pointer(ctx))
	if rc != C.ZITI_OK {
		ctx.status, ctx.statusErr = int(rc), zitiError(rc)
		go func() {
			ch <- ctx
		}()
	}

	res := <-ch
	delete(initMap, cfg)

	return res
}

//export free_async
func free_async(handle *C.uv_handle_t){
	C.free(unsafe.Pointer(handle))
}

//export log_writer_cb
func log_writer_cb(level C.int, loc C.string, msg C.string, msglen C.int) {
	gomsg := C.GoStringN(msg, msglen)
	goline := C.GoString(loc)
	lvl := level
	switch lvl {
	case 0:
		noFileLog.Warnf("level 0 should not be logged, please report: %s", gomsg)
		break
	case 1:
		noFileLog.Errorf("SDK: %s\t%s", goline, gomsg)
		break
	case 2:
		noFileLog.Warnf("SDK: %s\t%s", goline, gomsg)
		break
	case 3:
		noFileLog.Infof("SDK: %s\t%s", goline, gomsg)
		break
	case 4:
		noFileLog.Debugf("SDK: %s\t%s", goline, gomsg)
		break
	case 5:
	case 6:
		//VERBOSE:5
		//TRACE:6
		noFileLog.Tracef("SDK: %s\t%s", goline, gomsg)
		break
	default:
		noFileLog.Warnf("level [%d] NOT recognized: %s", level, gomsg)
		break
	}
}