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
#include <ziti/ziti_events.h>
#include "ziti/ziti_tunnel.h"
#include "ziti/ziti_log.h"

#include "sdk.h"
extern void zitiContextEvent(ziti_context nf, int status, void *ctx);
extern void eventCB(ziti_context ztx, ziti_event_t *event);

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
	Options       *C.ziti_options
	czctx         C.ziti_context
	czid          *C.ziti_identity
	status        int
	statusErr     error
	Loaded        bool
	Name          string
	Version       string
	Services      sync.Map
	Fingerprint   string
	StatusChanges func(int)
}

func NewZid(statusChange func(int)) *ZIdentity {
	zid := &ZIdentity{}
	zid.Services = sync.Map{}
	zid.Options = (*C.ziti_options)(C.calloc(1, C.sizeof_ziti_options))
	zid.StatusChanges = statusChange
	return zid
}

func (c *ZIdentity) GetMetrics() (int64, int64, bool) {
	if c == nil {
		return 0, 0, false
	}
	if C.is_null(unsafe.Pointer(c.czctx)) {
		log.Debugf("ziti context is still null. the identity is probably not initialized yet: %s", c.Fingerprint)
		return 0, 0, false
	}
	var up, down C.double
	C.ziti_get_transfer_rates(c.czctx, &up, &down)

	return int64(up), int64(down), true
}

func (c *ZIdentity) UnsafePointer() unsafe.Pointer {
	return unsafe.Pointer(c.czctx)
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
		if c.czctx != nil {
			v1 := C.ziti_get_controller_version(c.czctx)
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
		if c.czid != nil {
			if c.czid.name != nil {
				c.Name = C.GoString(c.czid.name)
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
	if c.czctx != nil && c.czid != nil {
		C.c_mapiter(&c.czid.tags)
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
	if c.czctx != nil {
		return C.GoString(C.ziti_get_controller(c.czctx))
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
			zid.Services.Delete(svcId)
			if !ok {
				log.Warnf("unregister service from serviceCB was not ok? %s:%d", fs.InterceptHost, fs.InterceptPort)
			}
		} else {
			log.Debugf("new service with id: %s, name: %s in context %d", svcId, name, &zid)
		}

		if C.ZITI_CAN_BIND == ( service.perm_flags & C.ZITI_CAN_BIND ) {
			var v1Config C.ziti_server_cfg_v1
			r := C.ziti_service_get_config(service, cTunServerCfgName, unsafe.Pointer(&v1Config), (*[0]byte)(C.parse_ziti_server_cfg_v1))
			if r == 0 {
				C.ziti_tunneler_host_v1(C.tunneler_context(theTun.tunCtx), unsafe.Pointer(zid.czctx), service.name, v1Config.protocol, v1Config.hostname, v1Config.port)
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
				AddIntercept(svcId, name, ip.String(), port, unsafe.Pointer(zid.czctx))
			} else {
				log.Infof("service intercept beginning for service: %s@%s:%d on ip %s", name, host, port, ip.String())
				AddIntercept(svcId, name, ip.String(), port, unsafe.Pointer(zid.czctx))
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
			log.Warnf("unregister service from serviceUnavailable was not ok? %s:%d", fs.InterceptHost, fs.InterceptPort)
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

//export eventCB
func eventCB(ztx C.ziti_context, event *C.ziti_event_t) {
	appCtx := C.ziti_app_ctx(ztx)
	log.Tracef("events received. type: %d for ztx(%p)", event._type, ztx)

	switch event._type {
	case C.ZitiContextEvent:
		ctxEvent := C.ziti_event_context_event(event)
		zitiContextEvent(ztx, ctxEvent.ctrl_status, appCtx)

	case C.ZitiRouterEvent:
		rtrEvent := C.ziti_event_router_event(event)
		switch rtrEvent.status {
		case C.EdgeRouterConnected:
			log.Infof("router[%s]: connected to %s", C.GoString(rtrEvent.name), C.GoString(rtrEvent.version))
		case C.EdgeRouterDisconnected:
			log.Infof("router[%s]: Disconnected", C.GoString(rtrEvent.name))
		case C.EdgeRouterRemoved:
			log.Infof("router[%s]: Removed", C.GoString(rtrEvent.name))
		case C.EdgeRouterUnavailable:
			log.Infof("router[%s]: Unavailable", C.GoString(rtrEvent.name))
		}

	case C.ZitiServiceEvent:
		srvEvent := C.ziti_event_service_event(event)
		for i := 0; true ; i++ {
			s := C.ziti_service_array_get(srvEvent.removed, C.int(i))
			if unsafe.Pointer(s) == C.NULL {
				break
			}
			serviceCB(ztx, s, C.ZITI_SERVICE_UNAVAILABLE, appCtx)

			log.Info("service removed ", C.GoString(s.name))
		}
		for i := 0; true ; i++ {
			s := C.ziti_service_array_get(srvEvent.changed, C.int(i))
			if unsafe.Pointer(s) == C.NULL {
				break
			}
			log.Info("service changed ", C.GoString(s.name))
			serviceCB(ztx, s, C.ZITI_OK, appCtx)
		}
		for i := 0; true ; i++ {
			s := C.ziti_service_array_get(srvEvent.added, C.int(i))
			if unsafe.Pointer(s) == C.NULL {
				break
			}
			log.Info("service added ", C.GoString(s.name))
			serviceCB(ztx, s, C.ZITI_OK, appCtx)
		}

	default:
		log.Infof("event %d not handled", event._type)
	}
}

//export zitiContextEvent
func zitiContextEvent(nf C.ziti_context, status C.int, data unsafe.Pointer) {
	zid := (*ZIdentity)(data)

	zid.status = int(status)
	zid.statusErr = zitiError(status)
	zid.czctx = nf

	cfg := C.GoString(zid.Options.config)

	if status == C.int(0) {
		if nf != nil {
			zid.czid = C.ziti_get_identity(nf)
		}

		zid.Name = zid.setNameFromId()
		zid.Version = zid.setVersionFromId()
		log.Infof("============ controller connected: %s at %v", zid.Name, zid.Version)
	} else {
		log.Errorf("zitiContextEvent failed to connect[%s] to controller for %s", zid.statusErr, cfg)
	}
	zid.StatusChanges(int(status))
}

func zitiError(code C.int) error {
	if int(code) != 0 {
		return errors.New(C.GoString(C.ziti_errorstr(code)))
	}
	return nil
}

func LoadZiti(zid *ZIdentity, cfg string, refreshInterval int) {
	zid.Options.config = C.CString(cfg)
	zid.Options.refresh_interval = C.long(refreshInterval)
	zid.Options.metrics_type = C.INSTANT
	zid.Options.config_types = C.all_configs
	zid.Options.pq_domain_cb = C.ziti_pq_domain_cb(C.ziti_pq_domain_go)
	zid.Options.pq_mac_cb = C.ziti_pq_mac_cb(C.ziti_pq_mac_go)
	zid.Options.pq_os_cb = C.ziti_pq_os_cb(C.ziti_pq_os_go)
	zid.Options.pq_process_cb = C.ziti_pq_process_cb(C.ziti_pq_process_go)

	zid.Options.events = C.ZitiContextEvent | C.ZitiServiceEvent | C.ZitiRouterEvent
	zid.Options.event_cb = C.ziti_event_cb(C.eventCB)
	ptr := unsafe.Pointer(zid)
	zid.Options.app_ctx = ptr

	rc := C.ziti_init_opts(zid.Options, _impl.libuvCtx.l)
	if rc != C.ZITI_OK {
		zid.status, zid.statusErr = int(rc), zitiError(rc)
		log.Errorf("FAILED to load identity from config file: %s due to: %s", cfg, zid.statusErr)
	} else {
		log.Debugf("successfully loaded identity from config file: %s", cfg)
	}
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
		noFileLog.Warnf("SDK_: level 0 should not be logged, please report: %s", gomsg)
		break
	case 1:
		noFileLog.Errorf("SDKe: %s\t%s", goline, gomsg)
		break
	case 2:
		noFileLog.Warnf("SDKw: %s\t%s", goline, gomsg)
		break
	case 3:
		noFileLog.Infof("SDKi: %s\t%s", goline, gomsg)
		break
	case 4:
		noFileLog.Debugf("SDKd: %s\t%s", goline, gomsg)
		break
	case 5:
		//VERBOSE:5
		noFileLog.Tracef("SDKv: %s\t%s", goline, gomsg)
		break
	case 6:
		//TRACE:6
		noFileLog.Tracef("SDKt: %s\t%s", goline, gomsg)
		break
	default:
		noFileLog.Warnf("SDK_: level [%d] NOT recognized: %s", level, gomsg)
		break
	}
}