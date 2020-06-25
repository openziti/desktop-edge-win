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
#cgo LDFLAGS: -l ziti_tunneler -l lwipcore -l lwipwin32arch -l ziti_tunneler -l ziti_tunneler_cbs

#include <ziti/netif_driver.h>
#include <ziti/ziti_tunneler.h>
#include <ziti/ziti_tunneler_cbs.h>

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

*/
import "C"
import (
	"errors"
	"golang.zx2c4.com/wireguard/tun"
	"io"
	"net"
	"os"
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

var devMap = make(map[string]*tunnel)

func HookupTun(dev tun.Device, dns []net.IP) (Tunnel, error) {
	log.Debug("in HookupTun ")
	defer log.Debug("exiting HookupTun")
	name, err := dev.Name()
	if err != nil {
		log.Error(err)
		return nil, err
	}
	drv := makeDriver(name)

	log.Debug("in HookupTun2")

	t := &tunnel{
		dev:    dev,
		driver: drv,
		writeQ: make(chan []byte, 64),
		readQ:  make(chan []byte, 64),
	}
	devMap[name] = t

	opts := (*C.tunneler_sdk_options)(C.calloc(1, C.sizeof_tunneler_sdk_options))
	opts.netif_driver = drv
	opts.ziti_dial = C.ziti_sdk_dial_cb(C.ziti_sdk_c_dial)
	opts.ziti_close = C.ziti_sdk_close_cb(C.ziti_sdk_c_close)
	opts.ziti_write = C.ziti_sdk_write_cb(C.ziti_sdk_c_write)

	t.tunCtx = C.ziti_tunneler_init(opts, _impl.libuvCtx.l)

	go runDNSserver(dns)

	return t, nil
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
	t, found := devMap[C.GoString(h.id)]
	if !found {
		panic("should not be here")
		return -1
	}

	b := C.GoBytes(buf, C.int(length))

	t.writeQ <- b

	return C.ssize_t(len(b))
}

//export netifClose
func netifClose(h C.netif_handle) C.int {
	log.Debug("in netifClose")
	return C.int(0)
}

//export netifSetup
func netifSetup(h C.netif_handle, l *C.uv_loop_t, packetCb C.packet_cb, ctx unsafe.Pointer) C.int {
	t, found := devMap[C.GoString(h.id)]
	if !found {
		log.Error("should not be here")
		return -1
	}

	t.read = (*C.uv_async_t)(C.calloc(1, C.sizeof_uv_async_t))
	C.uv_async_init(l, t.read, C.uv_async_cb(C.readAsync))
	t.read.data = unsafe.Pointer(h)
	log.Debugf("in netifSetup netif[%s] handle[%p]", C.GoString(h.id), h)

	t.idleR = (*C.uv_prepare_t)(C.calloc(1, C.sizeof_uv_prepare_t))
	C.uv_prepare_init(l, t.idleR)
	t.idleR.data = unsafe.Pointer(h)
	C.uv_prepare_start(t.idleR, C.uv_prepare_cb(C.readIdle))
	log.Debugf("in netifSetup netif[%s] handle[%p]", C.GoString(h.id), h)

	t.onPacket = packetCb
	t.onPacketCtx = ctx
	t.loop = l

	go t.runWriteLoop()
	go t.runReadLoop()

	return C.int(0)
}

func (t *tunnel) runReadLoop() {
	mtu, err := t.dev.MTU()
	if err != nil {
		panic(err)
	}
	log.Debug("starting tun read loop mtu=%d", mtu)
	defer log.Debug("tun read loop is done")
	mtuBuf := make([]byte, mtu)
	for {
		nr, err := t.dev.Read(mtuBuf, 0)
		if err != nil {
			if err == io.EOF || err == os.ErrClosed {
				//that's fine...
				return
			}
			panic(err)
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
	dev := (*C.netif_handle_t)(idler.data)

	id := C.GoString(dev.id)
	t, found := devMap[id]
	if !found {
		log.Debug("should not be here id = [%s]", id)
		panic(errors.New("where is my tunnel?"))
	}

	np := len(t.readQ)
	for i := np; i > 0; i-- {
		b := <-t.readQ
		buf := C.CBytes(b)

		C.call_on_packet(buf, C.ssize_t(len(b)), t.onPacket, t.onPacketCtx)
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
				panic(err)
			}

			if n < len(p) {
				log.Debug("Error short write")
			}
		}
	}
}

func (t *tunnel) AddIntercept(svcId string, service string, host string, port int, ctx unsafe.Pointer) {
	log.Debugf("about to add intercept for: %s, %s, %d", service, host, port)
	res := C.ziti_tunneler_intercept_v1(t.tunCtx, ctx, C.CString(svcId), C.CString(service), C.CString(host), C.int(port))
	log.Debugf("intercept added: %v", res)
}

func RemoveIntercept(svcvId string) {
	for _, t := range devMap {
		log.Infof("issuing stop intercepting for service id: %s", svcvId)
		C.ziti_tunneler_stop_intercepting(t.tunCtx, C.CString(svcvId))
	}
}

func AddIntercept(svcId string, service string, host string, port uint16, ctx *CZitiCtx) {
	for _, t := range devMap {
		log.Debug("adding intercept for: %s, %s, %d", service, host, port)
		res := C.ziti_tunneler_intercept_v1(t.tunCtx, unsafe.Pointer(ctx.zctx), C.CString(svcId), C.CString(service), C.CString(host), C.int(port))
		log.Debugf("intercept added: %v", res)
	}
}