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

//
/*
#cgo LDFLAGS: -l ziti-tunnel-sdk-c -l lwipcore -l lwipwin32arch -l ziti-tunnel-sdk-c -l ziti-tunnel-cbs-c

#include <ziti/netif_driver.h>
#include <ziti/ziti_tunnel.h>
#include <ziti/ziti_tunnel_cbs.h>
#include <ziti/ziti_log.h>

#include <uv.h>

extern int netifClose(netif_handle dev);
extern ssize_t netifRead(netif_handle dev, void *buf, size_t buf_len);
extern ssize_t netifWrite(netif_handle dev, void *buf, size_t len);
extern int netifSetup(netif_handle dev, uv_loop_t *loop, packet_cb packetCb, void* ctx);

typedef struct netif_handle_s {
   const char* id;
   packet_cb on_packet_cb;
   void *packet_cb_ctx;
} netif_handle_t;

extern void readAsync(struct uv_async_s *a);
extern void readIdle(uv_prepare_t *idler);

extern void call_on_packet(void *packet, ssize_t len, packet_cb cb, void *ctx);
extern void remove_intercepts(uv_async_t *handle);
extern void free_async(uv_handle_t* timer);
extern void ZLOG(int level, char* msg);

*/
import "C"
import (
	"golang.zx2c4.com/wireguard/tun"
	"io"
	"os"
	"sync"
	"unsafe"
)

type Tunnel interface {
	AddIntercept(svcId string, service string, hostname string, port int, ctx unsafe.Pointer)
}

type tunnel struct {
	dev    tun.Device
	driver C.netif_driver
	writeQ chan []byte
	readQ  chan []byte

	idleR       *C.uv_prepare_t
	read        *C.uv_async_t
	loop        *C.uv_loop_t
	onPacket    C.packet_cb
	onPacketCtx unsafe.Pointer

	tunCtx C.tunneler_context
}

//var devMap = make(map[string]*tunnel)
var theTun *tunnel

func HookupTun(dev tun.Device/*, dns []net.IP*/) error {
	log.Debug("in HookupTun ")
	defer log.Debug("exiting HookupTun")
	name, err := dev.Name()
	if err != nil {
		log.Error(err)
		return err
	}
	drv := makeDriver(name)

	log.Debug("in HookupTun2")

	t := &tunnel{
		dev:    dev,
		driver: drv,
		writeQ: make(chan []byte, 64),
		readQ:  make(chan []byte, 64),
	}
	//devMap[name] = t
	theTun = t

	opts := (*C.tunneler_sdk_options)(C.calloc(1, C.sizeof_tunneler_sdk_options))
	opts.netif_driver = drv
	opts.ziti_dial = C.ziti_sdk_dial_cb(C.ziti_sdk_c_dial)
	opts.ziti_close = C.ziti_sdk_close_cb(C.ziti_sdk_c_close)
	opts.ziti_write = C.ziti_sdk_write_cb(C.ziti_sdk_c_write)
	opts.ziti_host_v1 = C.ziti_sdk_host_v1_cb(C.ziti_sdk_c_host_v1)

	t.tunCtx = C.ziti_tunneler_init(opts, _impl.libuvCtx.l)
	return nil
}

func makeDriver(name string) C.netif_driver {
	driver := &C.netif_driver_t{}
	driver.handle = &C.netif_handle_t{}
	driver.handle.id = C.CString(name)
	driver.close = C.netif_close_cb(C.netifClose)
	driver.setup = C.setup_packet_cb(C.netifSetup)
	driver.write = C.netif_write_cb(C.netifWrite)
	return driver
}

//export netifWrite
func netifWrite(h C.netif_handle, buf unsafe.Pointer, length C.size_t) C.ssize_t {
	/*t, found := devMap[C.GoString(h.id)]
	if !found {
		log.Panicf("An unexpected and unrecoverable error has occurred while calling netifWrite")
	}*/

	b := C.GoBytes(buf, C.int(length))

	theTun.writeQ <- b

	return C.ssize_t(len(b))
}

//export netifClose
func netifClose(h C.netif_handle) C.int {
	log.Debug("in netifClose")
	return C.int(0)
}

//export netifSetup
func netifSetup(h C.netif_handle, l *C.uv_loop_t, packetCb C.packet_cb, ctx unsafe.Pointer) C.int {
	/*t, found := devMap[C.GoString(h.id)]
	if !found {
		log.Error("should not be here")
		return -1
	}*/

	theTun.read = (*C.uv_async_t)(C.calloc(1, C.sizeof_uv_async_t))
	C.uv_async_init(l, theTun.read, C.uv_async_cb(C.readAsync))
	theTun.read.data = unsafe.Pointer(h)
	log.Debugf("in netifSetup netif[%s] handle[%p] before", C.GoString(h.id), h)

	theTun.idleR = (*C.uv_prepare_t)(C.calloc(1, C.sizeof_uv_prepare_t))
	C.uv_prepare_init(l, theTun.idleR)
	theTun.idleR.data = unsafe.Pointer(h)
	C.uv_prepare_start(theTun.idleR, C.uv_prepare_cb(C.readIdle))
	log.Debugf("in netifSetup netif[%s] handle[%p] after", C.GoString(h.id), h)

	theTun.onPacket = packetCb
	theTun.onPacketCtx = ctx
	theTun.loop = l

	go theTun.runWriteLoop()
	go theTun.runReadLoop()

	return C.int(0)
}

func (t *tunnel) runReadLoop() {
	mtu, err := t.dev.MTU()
	if err != nil {
		log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "getting the MTU", err)
	}
	log.Debugf("starting tun read loop mtu=%d", mtu)
	defer log.Debug("tun read loop is done")
	mtuBuf := make([]byte, mtu)
	for {
		nr, err := t.dev.Read(mtuBuf, 0)
		if err != nil {
			if err == io.EOF || err == os.ErrClosed {
				//that's fine...
				return
			}
			log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "reading from the tun device", err)
		}

		if len(t.readQ) == cap(t.readQ) {
			log.Debug("read loop is about to block")
		}

		buf := make([]byte, nr)
		copy(buf, mtuBuf[:nr])
		t.readQ <- buf
		C.uv_async_send((*C.uv_async_t)(unsafe.Pointer(t.read)))
	}
}

//export readIdle
func readIdle(idler *C.uv_prepare_t) {
	/*dev := (*C.netif_handle_t)(idler.data)

	id := C.GoString(dev.id)
	t, found := devMap[id]
	if !found {
		log.Panicf("An unexpected and unrecoverable error has occurred while looking for tun in devMap")
	}*/

	np := len(theTun.readQ)
	for i := np; i > 0; i-- {
		b := <-theTun.readQ
		buf := C.CBytes(b)

		C.call_on_packet(buf, C.ssize_t(len(b)), theTun.onPacket, theTun.onPacketCtx)
		C.free(buf)
	}

}

//export readAsync
func readAsync(a *C.uv_async_t) {
	// nothing to do: only needed to trigger loop into action
}

func (t *tunnel) runWriteLoop() {
	log.Debug("starting Write Loop")
	defer log.Debug("write loop finished")
	for {
		select {
		case p := <-t.writeQ:
			if p == nil {
				return
			}

			n, err := t.dev.Write(p, 0)
			if err != nil {
				log.Panicf("An unexpected and unrecoverable error has occurred while %s: %v", "writing to tun device", err)
			}

			if n < len(p) {
				log.Debug("Error short write")
			}
		}
	}
}
/*
func (t *tunnel) AddIntercept(svcId string, service string, host string, port int, ctx unsafe.Pointer) {
	log.Debugf("about to add intercept for: %s, %s, %d", service, host, port)
	res := C.ziti_tunneler_intercept_v1(t.tunCtx, ctx, C.CString(svcId), C.CString(service), C.CString(host), C.int(port))
	log.Debugf("intercept added: %v", res)
}*/

func AddIntercept(svcId string, service string, host string, port int, ctx unsafe.Pointer) {
	/*for _, t := range devMap {
		log.Debug("adding intercept for: %s, %s, %d", service, host, port)
		t.AddIntercept(svcId, service, host, int(port), unsafe.Pointer(ctx))
	}
	*/
	log.Debugf("about to add intercept for: %s[%s] at %s:%d", service, svcId, host, port)
	_ = C.ziti_tunneler_intercept_v1(theTun.tunCtx, ctx, C.CString(svcId), C.CString(service), C.CString(host), C.int(port))
}

type RemoveWG struct {
	Wg    *sync.WaitGroup
	SvcId string
}

func RemoveIntercept(rwg *RemoveWG) {
	async := (*C.uv_async_t)(C.malloc(C.sizeof_uv_async_t))
	async.data = unsafe.Pointer(rwg)
	C.uv_async_init(_impl.libuvCtx.l, async, C.uv_async_cb(C.remove_intercepts))
	C.uv_async_send((*C.uv_async_t)(unsafe.Pointer(async)))
}

//export remove_intercepts
func remove_intercepts(async *C.uv_async_t) {
	//C.ZLOG(C.INFO, C.CString(fmt.Sprint("aaaa on uv thread - inside remove_intercepts: %d", &async.loop)))
	//c := (*C.char)(async.data)
	rwg := (*RemoveWG)(async.data)

	/*for _, t := range devMap {
		//C.ZLOG(C.INFO, C.CString("bbb on uv thread - inside remove_intercepts"))
		C.ziti_tunneler_stop_intercepting(t.tunCtx, C.CString(rwg.SvcId))
	}*/
	C.ziti_tunneler_stop_intercepting(theTun.tunCtx, C.CString(rwg.SvcId))
	//C.ZLOG(C.INFO, C.CString("ccc on uv thread - inside remove_intercepts"))
	C.uv_close((*C.uv_handle_t)(unsafe.Pointer(async)), C.uv_close_cb(C.free_async))
	C.ZLOG(C.INFO, C.CString("on uv thread - completed remove_intercepts"))
	rwg.Wg.Done()
}
