package cziti
/*
#cgo windows LDFLAGS: -l libziti.imp -luv -lws2_32 -lpsapi

#include "sdk.h"
extern void initCB(nf_context nf, int status, void *ctx);
extern void serviceCB(nf_context nf, ziti_service*, int status, void *ctx);
 */
import "C"
import (
	"encoding/json"
	"errors"
	"fmt"
	"unsafe"
)

type sdk struct {
	libuvCtx *C.libuv_ctx
}

var _impl sdk

func init() {
	_impl.libuvCtx = (*C.libuv_ctx)(C.calloc(1, C.sizeof_libuv_ctx))
	C.libuv_init(_impl.libuvCtx)
}

func Start() {
	v := C.NF_get_version()
	fmt.Printf("starting ziti-sdk-c %s(%s)[%s]\n",
		C.GoString(v.version), C.GoString(v.revision), C.GoString(v.build_date))

	_impl.run()
}

func (inst *sdk) run() {
	C.libuv_run(inst.libuvCtx)
}

func Stop() {
	C.libuv_stop(_impl.libuvCtx)
}

type Service struct {
	Name string
	Id   string
	InterceptHost string
	InterceptPort int
}

type CZitiCtx struct {
	options C.nf_options
	nf C.nf_context

	Services *map[string]Service
}

func (c *CZitiCtx) Name() string {
	return C.GoString(C.NF_get_identity(c.nf).name)
}

func (c *CZitiCtx) Controller() string {
	return C.GoString(C.NF_get_controller(c.nf))
}

var tunCfgName = C.CString("ziti-tunneler-client.v1")

//export serviceCB
func serviceCB(nf C.nf_context, service *C.ziti_service, status C.int, data unsafe.Pointer) {
	ctx := (*CZitiCtx)(data)

	if ctx.Services == nil {
		m := make(map[string]Service)
		ctx.Services = &m
	}

	name := C.GoString(service.name)
	if status == C.ZITI_SERVICE_UNAVAILABLE {
		DNS.DeregisterService(ctx, name)
		delete(*ctx.Services, name)
	} else if status == C.ZITI_OK {
		cfg := C.ziti_service_get_raw_config(service, tunCfgName)

		host := ""
		port := -1
		if cfg != nil {
			var c map[string]interface{}

			if err := json.Unmarshal([]byte(C.GoString(cfg)), &c); err == nil {
				host = c["hostname"].(string)
				port = int(c["port"].(float64))
			}
		}
		(*ctx.Services)[name] = Service{
			Name: name,
			Id: C.GoString(service.id),
			InterceptHost: host,
			InterceptPort: port,
		}
		if host != "" && port != -1 {
			ip, err := DNS.RegisterService(host, uint16(port), ctx, name)
			if err != nil {
				fmt.Println(err)
			} else {
				fmt.Printf("service[%s] is mapped to <%s:%d>\n", name, ip.String(), port)
				for _, t := range devMap {
					t.AddIntercept(name, ip.String(), port, unsafe.Pointer(ctx.nf))
				}
			}
		}
	}
}

//export initCB
func initCB(nf C.nf_context, status C.int, data unsafe.Pointer) {
	ctx := (*CZitiCtx)(data)
	fmt.Printf("status %d: ctx = %+v, nf = %+v\n", status, ctx, nf)
	ctx.nf = nf
	ctx.options.ctx = data
	cfg := C.GoString(ctx.options.config)
	if ch, ok := initMap[cfg]; ok {
		ch <- ctx
	} else {
		fmt.Printf("respose channel not found\n")
	}
}

var initMap = make(map[string]chan interface{})

func zitiError(code C.int) error {
	return errors.New(C.GoString(C.ziti_errorstr(code)))
}

func LoadZiti(cfg string) (*CZitiCtx, error)  {
	ctx := CZitiCtx{}
	ctx.options.config = C.CString(cfg)
	ctx.options.init_cb = C.nf_init_cb(C.initCB)
	ctx.options.service_cb = C.nf_service_cb(C.serviceCB)
	ctx.options.refresh_interval = C.int(10)
	ctx.options.config_types = C.all_configs
	//ctx.options.ctx = unsafe.Pointer(&ctx)

	ch := make(chan interface{})
	initMap[cfg] = ch
	rc := C.NF_init_opts(&ctx.options, _impl.libuvCtx.l, unsafe.Pointer(&ctx))
	if rc != C.ZITI_OK {
		return nil, zitiError(rc)
	}

	res := <- ch

	if c, ok := res.(*CZitiCtx); ok {
		return c, nil
	}
	return nil, res.(error)
}
