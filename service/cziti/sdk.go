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

#include <stdlib.h>

#include <ziti/ziti.h>
#include <ziti/ziti_events.h>
#include <ziti/ziti_tunnel.h>
#include <ziti/ziti_tunnel_cbs.h>
#include "ziti/ziti_log.h"
#include "sdk.h"

void doZitiShutdown(uv_async_t *handle);
void zitiContextEvent(ziti_context nf, int status, void *ctx);
void eventCB(ziti_context ztx, ziti_event_t *event);

void shutdown_callback(uv_async_t *handle);
void free_async(uv_handle_t* timer);

void c_mapiter(model_map *map);
void ziti_dump_to_file(void *ctx, char* outputPath);
int ziti_dump_to_log(void *ctx, void* stringsBuilder);
void* stailq_first_forgo(void* entries);

*/
import "C"
import (
	"encoding/json"
	"errors"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/api"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/util/logging"
	"net"
	"os"
	"strings"
	"sync"
	"time"
	"unsafe"
)

var log = logging.Logger()
var noFileLog = logging.NoFilenameLogger()
var Version dto.ServiceVersion
var BulkServiceChanges = make(chan BulkServiceChange, 32)

var cCfgZitiTunnelerClientV1 = C.CString("ziti-tunneler-client.v1")
var cCfgInterceptV1 = C.CString("intercept.v1")

type sdk struct {
	libuvCtx *C.libuv_ctx
}
type ServiceChange struct {
	Operation   string
	Service     *ZService
	ZitiContext *ZIdentity
}
type BulkServiceChange struct {
	Fingerprint       string
	HostnamesToAdd    map[string]bool
	HostnamesToRemove map[string]bool
	ServicesToRemove  []*dto.Service
	ServicesToAdd     []*dto.Service
	MinTimeout		  int
	MaxTimeout		  int
	LastUpdatedTime	  time.Time
}

type ToastNotification struct {
	IdentityName		string
	Fingerprint			string
	Message				string
	MinimumTimeOut		int
	AllServicesTimeout	int
	NotificationTime	time.Time
	Severity			string
}

type TunnelNotificationEvent struct {
	Op				string
	Notification	[]ToastNotification
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

func Start(a api.DesktopEdgeIface, ip string, maskBits int, loglevel int) {
	goapi = a
	DnsInit(ip, maskBits)
	appInfo := "Ziti Desktop Edge for Windows"
	log.Debugf("informing c sdk of appinfo: %s at %s", appInfo, Version.Version)
	C.ziti_set_app_info(C.CString(appInfo), C.CString(Version.Version))
	v := C.ziti_get_version()
	log.Infof("starting ziti-sdk-c %s(%s)[%s]", C.GoString(v.version), C.GoString(v.revision), C.GoString(v.build_date))

	_impl.run(loglevel)
}

func (inst *sdk) run(loglevel int) {
	SetLogLevel(loglevel)
	C.libuv_run(inst.libuvCtx)
}

type ZService struct {
	Name    string
	Id      string
	Service *dto.Service
	Czctx   C.ziti_context
}

type ZIdentity struct {
	Options       	*C.ziti_options
	czctx         	C.ziti_context
	czid          	*C.ziti_identity
	status        	int
	statusErr     	error
	Loaded        	bool
	Name          	string
	Version       	string
	Services      	sync.Map
	Fingerprint   	string
	Active        	bool
	StatusChanges 	func(int)
	MfaNeeded     	bool
	MfaEnabled    	bool
	MinTimeout	  	int
	MaxTimeout	  	int
	LastUpdatedTime	time.Time
	//mfa           *Mfa
}

/*type Mfa struct {
	mfaContext unsafe.Pointer
	authQuery  *C.ziti_auth_query_mfa
	responseCb C.ziti_ar_mfa_cb
}*/

func NewZid(statusChange func(int)) *ZIdentity {
	zid := &ZIdentity{}
	zid.Services = sync.Map{}
	zid.Options = (*C.ziti_options)(C.calloc(1, C.sizeof_ziti_options))
	zid.StatusChanges = statusChange
	return zid
}

func (zid *ZIdentity) GetMetrics() (int64, int64, bool) {
	if zid == nil {
		return 0, 0, false
	}
	if unsafe.Pointer(zid.czctx) == C.NULL {
		log.Debugf("ziti context is still null. the identity is probably not initialized yet: %s", zid.Fingerprint)
		return 0, 0, false
	}
	var up, down C.double
	C.ziti_get_transfer_rates(zid.czctx, &up, &down)

	return int64(up), int64(down), true
}

func (zid *ZIdentity) UnsafePointer() unsafe.Pointer {
	return unsafe.Pointer(zid.czctx)
}
func (zid *ZIdentity) AsKey() string {
	return "marker askey"
}

func (zid *ZIdentity) Status() (int, error) {
	return zid.status, zid.statusErr
}

func (zid *ZIdentity) setVersionFromId() string {
	if len(zid.Version) > 0 {
		return zid.Version
	}
	zid.Version = "<unknown version>"
	if zid != nil {
		if zid.czctx != nil {
			v1 := C.ziti_get_controller_version(zid.czctx)
			return C.GoString(v1.version)
		}
	}
	return zid.Version
}

func (zid *ZIdentity) setNameFromId() string {
	if len(zid.Name) > 0 {
		return zid.Name
	}
	zid.Name = "<unknown>"
	if zid != nil {
		if zid.czid != nil {
			if zid.czid.name != nil {
				zid.Name = C.GoString(zid.czid.name)
			} else {
				log.Debug("in Name - c.zid.name was nil")
			}
		} else {
			log.Debug("in Name - c.zid was nil")
		}
	} else {
		log.Debug("in Name - c was nil")
	}
	return zid.Name
}

func (zid *ZIdentity) Controller() string {
	if zid.czctx != nil {
		return C.GoString(C.ziti_get_controller(zid.czctx))
	}
	return C.GoString(zid.Options.controller)
}

func (zid *ZIdentity) Shutdown() {

	async := (*C.uv_async_t)(C.malloc(C.sizeof_uv_async_t))
	async.data = unsafe.Pointer(zid.czctx)
	log.Debugf("setting up call to ziti_shutdown for context %p using uv_async_t", async.data)
	C.uv_async_init(_impl.libuvCtx.l, async, C.uv_async_cb(C.doZitiShutdown))
	C.uv_async_send((*C.uv_async_t)(unsafe.Pointer(async)))
}

//export doZitiShutdown
func doZitiShutdown(async *C.uv_async_t) {
	ctx := C.ziti_context(async.data)
	log.Infof("invoking ziti_shutdown for context %p", &ctx)
	C.ziti_shutdown(ctx)
}

type clientV1Cfg struct {
	//{"addresses":["eth0.ziti.ranged","eth0.second","eth0.third"],"portRanges":[{"high":80,"low":80},{"high":443,"low":443}],"protocols":["tcp"]}
	Addresses  []string        `json:"addresses"`
	PortRanges []dto.PortRange `json:"portRanges"`
	Protocols  []string        `json:"protocols"`
}
type v1ClientCfg struct {
	//{"hostname":"192.168.15.15","port":80}
	Hostname string `json:"hostname"`
	Port     int    `json:"port"`
}

func serviceCB(ziti_ctx C.ziti_context, service *C.ziti_service, status C.int, zid *ZIdentity) *dto.Service {
	if zid == nil {
		log.Errorf("in serviceCB with nil zid??? ")
		return nil
	}

	name := C.GoString(service.name)
	svcId := C.GoString(service.id)
	log.Debugf("============ INSIDE serviceCB - status: %s:%s - %v, %v ============", name, svcId, status, service.perm_flags)
	C.ziti_sdk_c_on_service(ziti_ctx, service, status, unsafe.Pointer(theTun.tunCtx))

	var protocols []string
	var portRanges []dto.PortRange
	var addresses []dto.Address

	strClientV1cfg := C.GoString(C.ziti_service_get_raw_config(service, cCfgInterceptV1))
	if strClientV1cfg != "" {
		log.Tracef("intercept.v1: %s", strClientV1cfg)
		var obj clientV1Cfg
		uerr := json.Unmarshal([]byte(strClientV1cfg), &obj)
		if uerr != nil {
			log.Errorf("could not marshall json? %v", uerr)
		}
		protocols = obj.Protocols
		portRanges = obj.PortRanges
		a := make([]dto.Address, len(obj.Addresses))
		for idx, add := range obj.Addresses {
			a[idx] = toAddy(add)
		}

		addresses = append(a)
	} else {
		strZitiTunnelerClientV1 := C.GoString(C.ziti_service_get_raw_config(service, cCfgZitiTunnelerClientV1))
		log.Tracef("ziti-tunneler-client.v1: %s", strZitiTunnelerClientV1)
		var obj v1ClientCfg
		uerr := json.Unmarshal([]byte(strZitiTunnelerClientV1), &obj)
		if uerr != nil {
			log.Errorf("could not marshall json? %v", uerr)
		}
		protocols = []string{"UDP", "TCP"}
		portRanges = []dto.PortRange{{Low: obj.Port, High: obj.Port}}
		addresses = []dto.Address{toAddy(obj.Hostname)}
	}

	log.Infof("service update: %s, id: %s, portocols:%s, addresses:%v, portRanges: %v", name, svcId, protocols, addresses, portRanges)

	var svc *dto.Service

	if status == C.ZITI_SERVICE_UNAVAILABLE {
		log.Debugf("serivce has become unavailable: %s [%s]", name, svcId)
		f, ok := zid.Services.Load(svcId)
		if ok {
			svc = &dto.Service{
				Name:      name,
				Id:        svcId,
				Addresses: addresses,
			}
			found := f.(*ZService)
			if found != nil {
				found.Service = nil
			}
			zid.Services.Delete(svcId)
			return svc
		} else {
			log.Warnf("could not remove service? service not found with id: %s, name: %s in context %d", svcId, name, &zid)
		}
	} else if status == C.ZITI_OK {
		pcIds := make(map[string]bool)
		var postureChecks []dto.PostureCheck

		//if any posture query sets pass - that will grant the user access to that service
		hasAccess := false
		timeout	  := -1

		//find all posture checks sets...
		for setIdx := 0; true; setIdx++ {
			pqs := C.posture_query_set_get(service.posture_query_set, C.int(setIdx))
			if unsafe.Pointer(pqs) == C.NULL {
				break
			}

			if C.GoString(pqs.policy_type) == "Bind" {
				log.Tracef("Posture Query set returned a Bind policy: %s [ignored]", C.GoString(pqs.policy_id))
				// posture check does not consider bind policies
				continue
			} else {
				log.Tracef("Posture Query set returned a %s policy: %s, is_passing %t", C.GoString(pqs.policy_type), C.GoString(pqs.policy_id), pqs.is_passing)
			}

			if bool(pqs.is_passing) {
				hasAccess = true
			}

			//get all posture checks in this set...
			for pqIdx := 0; true; pqIdx++ {
				pq := C.posture_queries_get(pqs.posture_queries, C.int(pqIdx))
				if unsafe.Pointer(pq) == C.NULL {
					break
				}

				var pcId string
				pcId = C.GoString(pq.id)
				log.Infof("Posture query %s, timeout %d", C.GoString(pq.id) , int(pq.timeout))

				_, found := pcIds[pcId]
				if found {
					log.Tracef("posture check with id %s already in failing posture check map", pcId)
					for _, pc := range postureChecks {
						if pc.Id == C.GoString(pq.id) {
							if timeout == -1 || timeout > int(pq.timeout) {
								// pc.Timeout = int(pq.timeout)
								timeout	= int(pq.timeout)
							}
							break
						}
					}
				} else {
					pcIds[C.GoString(pq.id)] = false
					pc := dto.PostureCheck{
						IsPassing: bool(pq.is_passing),
						QueryType: C.GoString(pq.query_type),
						Id:        pcId,
						Timeout:   int(pq.timeout),
					}
					timeout	= pc.Timeout

					postureChecks = append(postureChecks, pc)
				}
			}
		}

		svc = &dto.Service{
			Name:          name,
			Id:            svcId,
			Protocols:     protocols,
			Addresses:     addresses,
			Ports:         portRanges,
			OwnsIntercept: true,
			PostureChecks: postureChecks,
			IsAccessable:  hasAccess,
			Timeout:	   timeout,
		}
		added := ZService{
			Name:    name,
			Id:      svcId,
			Service: svc,
			Czctx:   ziti_ctx,
		}
		zid.Services.Store(svcId, &added)
	}

	return svc
}

func toAddy(hostOrIpOrCidr string) dto.Address {
	addy := dto.Address{
		IsHost:   false,
		HostName: "",
		IP:       "",
		Prefix:   0,
	}

	log.Debugf("parsing %s to address", hostOrIpOrCidr)
	ip, ipnet, err := net.ParseCIDR(hostOrIpOrCidr)
	if err != nil {
		//must not be an CIDR... try IP parse
		ip = net.ParseIP(hostOrIpOrCidr)
		if ip == nil {
			log.Debugf("%s does not appear to be an ip/cidr combination. considering it a hostname", hostOrIpOrCidr)
			addy.HostName = hostOrIpOrCidr
			addy.IsHost = true
		} else {
			log.Debugf("%s determined to be ip", hostOrIpOrCidr)
			addy.IP = ip.String()
		}
	} else {
		log.Debugf("%s appears to be a proper CIDR", hostOrIpOrCidr)
		ones, _ := ipnet.Mask.Size()
		addy.IP = ip.String()
		addy.Prefix = ones
	}
	log.Tracef("parsed address: %v from %s", addy, hostOrIpOrCidr)
	return addy
}

//export eventCB
func eventCB(ztx C.ziti_context, event *C.ziti_event_t) {
	log.Tracef("events received. type: %d for ztx(%p)", event._type, ztx)

	appCtx := C.ziti_app_ctx(ztx)
	isCnull := appCtx == C.NULL
	isNil := appCtx == nil
	if isCnull || isNil {
		log.Errorf("in eventCB with null ziti_app_ctx??? ")
		return
	}

	zid := (*ZIdentity)(appCtx)

	switch event._type {
	case C.ZitiContextEvent:
		ctxEvent := C.ziti_event_context_event(event)
		zitiContextEvent(ztx, ctxEvent.ctrl_status, zid)

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
		hostnamesToAdd := make(map[string]bool)
		hostnamesToRemove := make(map[string]bool)
		servicesToRemove := make([]*dto.Service, 0)
		servicesToAdd := make([]*dto.Service, 0)

		srvEvent := C.ziti_event_service_event(event)

		for i := 0; true; i++ {
			removed := C.ziti_service_array_get(srvEvent.removed, C.int(i))
			if unsafe.Pointer(removed) == C.NULL {
				break
			}

			svcToRemove := serviceCB(ztx, removed, C.ZITI_SERVICE_UNAVAILABLE, zid)
			if svcToRemove != nil {
				remAddys := svcToRemove.Addresses
				for _, toRemove := range remAddys {
					if toRemove.IsHost && hostnameRemoved(toRemove.HostName) == 0 {
						hostnamesToRemove[toRemove.HostName] = true
					}
				}
				servicesToRemove = append(servicesToRemove, svcToRemove)
			}
		}
		for i := 0; true; i++ {
			changed := C.ziti_service_array_get(srvEvent.changed, C.int(i))
			if unsafe.Pointer(changed) == C.NULL {
				break
			}

			log.Info("service changed remove the service then add it back immediately", C.GoString(changed.name))
			svcToRemove := serviceCB(ztx, changed, C.ZITI_SERVICE_UNAVAILABLE, zid)
			if svcToRemove != nil {

				remAddys := svcToRemove.Addresses
				for _, toRemove := range remAddys {
					if toRemove.IsHost && hostnameRemoved(toRemove.HostName) == 0 {
						hostnamesToRemove[toRemove.HostName] = true
					}
				}
				servicesToRemove = append(servicesToRemove, svcToRemove)
			}

			svcToAdd := serviceCB(ztx, changed, C.ZITI_OK, zid)
			if svcToAdd != nil {
				addAddys := svcToAdd.Addresses
				for _, toAdd := range addAddys {
					if toAdd.IsHost && hostnameAdded(toAdd.HostName) == 1 {
						hostnamesToAdd[toAdd.HostName] = true
					}
				}
				servicesToAdd = append(servicesToAdd, svcToAdd)
			}
		}
		minimumTimeout := -1
		allServicesTimeout := -1
		for i := 0; true; i++ {
			added := C.ziti_service_array_get(srvEvent.added, C.int(i))
			if unsafe.Pointer(added) == C.NULL {
				break
			}
			svcToAdd := serviceCB(ztx, added, C.ZITI_OK, zid)
			if svcToAdd != nil {
				if svcToAdd.Timeout >= 0 {
					if minimumTimeout == -1 || minimumTimeout > svcToAdd.Timeout {
						minimumTimeout = svcToAdd.Timeout
					}
					if allServicesTimeout == -1 || allServicesTimeout < svcToAdd.Timeout {
						allServicesTimeout = svcToAdd.Timeout
					}
				}
				addAddys := svcToAdd.Addresses
				for _, toAdd := range addAddys {
					if toAdd.IsHost && hostnameAdded(toAdd.HostName) == 1 {
						hostnamesToAdd[toAdd.HostName] = true
					}
				}
				servicesToAdd = append(servicesToAdd, svcToAdd)
			}
		}

		zid.MinTimeout = minimumTimeout
		zid.MaxTimeout = allServicesTimeout
		zid.LastUpdatedTime = time.Now()

		svcChange := BulkServiceChange{
			Fingerprint:       zid.Fingerprint,
			HostnamesToAdd:    hostnamesToAdd,
			HostnamesToRemove: hostnamesToRemove,
			ServicesToAdd:     servicesToAdd,
			ServicesToRemove:  servicesToRemove,
			MinTimeout:		   minimumTimeout,
			MaxTimeout:		   allServicesTimeout,
			LastUpdatedTime:   time.Now(),
		}

		if len(BulkServiceChanges) == cap(BulkServiceChanges) {
			log.Warn("Service changes are not being processed fast enough. This client is out of date from the controller! This is unexpected. If you see this warning please report")
		} else {
			BulkServiceChanges <- svcChange
		}
	case C.ZitiMfaAuthEvent:
		zid := (*ZIdentity)(appCtx)
		log.Debugf("mfa auth event for finger print %s", zid.Fingerprint)
		/*mfa := &Mfa{
			mfaContext: mfa_ctx,
			authQuery:  aq_mfa,
			responseCb: response_cb,
		}
		zid.mfa = mfa*/
		zid.MfaNeeded = true
		zid.MfaEnabled = true

		if zid.Fingerprint != "" {
			var id = dto.Identity{
				Name:              zid.Name,
				FingerPrint:       zid.Fingerprint,
				Active:            zid.Active,
				ControllerVersion: zid.Version,
				Status:            "",
				MfaNeeded:         true,
				MfaEnabled:        true,
				Tags:              nil,
			}

			var m = dto.IdentityEvent{
				ActionEvent: dto.IDENTITY_ADDED,
				Id:          id,
			}
			goapi.BroadcastEvent(m)
		}
		log.Debugf("mfa auth event set enabled/needed to true for ziti context [%p]. Identity name:%s [fingerprint: %s]", zid, zid.Name, zid.Fingerprint)
	default:
		log.Infof("event %d not handled", event._type)
	}
}

func zitiContextEvent(ztx C.ziti_context, status C.int, zid *ZIdentity) {
	zid.status = int(status)
	zid.statusErr = zitiError(status)
	zid.czctx = ztx

	cfg := C.GoString(zid.Options.config)

	if status == C.ZITI_OK {
		if ztx != nil {
			zid.czid = C.ziti_get_identity(ztx)
		}

		zid.Name = zid.setNameFromId()
		zid.Version = zid.setVersionFromId()
		log.Debugf("============ controller connected: %s at %v. MFA: %v", zid.Name, zid.Version, zid.MfaEnabled)
	} else {
		log.Errorf("zitiContextEvent failed to connect[%s] to controller for %s", zid.statusErr, cfg)
	}
	zid.StatusChanges(int(status))
	idMap.Store(ztx, zid)
	log.Debugf("zitiContextEvent triggered and stored in ZIdentity with pointer: %p", unsafe.Pointer(ztx))
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

	zid.Options.events = C.ZitiContextEvent | C.ZitiServiceEvent | C.ZitiRouterEvent | C.ZitiMfaAuthEvent
	zid.Options.event_cb = C.ziti_event_cb(C.eventCB)

	// zid.Options.aq_mfa_cb = C.ziti_aq_mfa_cb(C.ziti_aq_mfa_cb_go)

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
func free_async(handle *C.uv_handle_t) {
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

//export ziti_dump_go_to_file_cb
func ziti_dump_go_to_file_cb(outputPath *C.char, charData *C.char) {
	f, err := os.OpenFile(C.GoString(outputPath),
		os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if err != nil {
		log.Warnf("unexpect error opening file: %v", err)
		_ = f.Close()
		return
	}
	_, _ = f.WriteString(C.GoString(charData))
	_ = f.Close()
}

func ZitiDump(zid *ZIdentity, path string) {
	cpath := C.CString(path)
	defer C.free(unsafe.Pointer(cpath))

	e := os.Remove(path)
	if e != nil {
		//probably did not exist
		log.Debugf("Could not remove file at: %s", path)
	} else {
		log.Debugf("Removed existing ziti_dump file at: %s", path)
	}

	C.ziti_dump_to_file(unsafe.Pointer(zid.czctx), cpath)
	log.Infof("ziti_dump saved to: %s", path)
}

//export ziti_dump_go_to_log_cb
func ziti_dump_go_to_log_cb(stringsBuilder unsafe.Pointer, charData *C.char) {
	sb := (*strings.Builder)(stringsBuilder)
	sb.WriteString(C.GoString(charData))
}
func ZitiDumpOnShutdown(zid *ZIdentity) {
	sb := strings.Builder{}
	C.ziti_dump_to_log(unsafe.Pointer(zid.czctx), unsafe.Pointer(&sb))
	log.Infof("working around the c sdk's limitation of embedding newlines on calling ziti_shutdown\n %s", sb.String())
}

func InitTunnelerDns(ipBase uint32, mask int) {
	C.ziti_tunneler_init_dns(C.uint32_t(ipBase), C.int(mask))
}

var addressCount = make(map[string]int)

func hostnameAdded(host string) int {
	count := 1
	if cur, ok := addressCount[host]; ok {
		count = cur + 1
	}
	addressCount[host] = count
	log.Debugf("hostname added: %s. count now: %d", host, count)
	return count
}
func hostnameRemoved(host string) int {
	count := 0
	if cur, ok := addressCount[host]; ok {
		if cur > 0 {
			count = cur - 1
		}
	}
	addressCount[host] = count
	log.Debugf("hostname removed %s. count now: %d", host, count)
	return count
}
