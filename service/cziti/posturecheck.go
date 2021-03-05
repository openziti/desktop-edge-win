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

void return_domain_info_c(ziti_context ztx, char* id, ziti_pr_domain_cb response_cb, char* domain);
void return_mac_info_c(ziti_context ztx, char* id, ziti_pr_mac_cb response_cb, char** mac_addresses, int num_mac);
void return_os_info_c(ziti_context ztx, char* id, ziti_pr_os_cb response_cb, char* os_type, char* os_version, char* os_build);
void return_proc_info_c(ziti_context ztx, char* id, char* path, ziti_pr_process_cb response_cb, bool is_running, char* sha, char** signers, int num_signers);
char** makeCharArray(int size);
void setArrayString(char **a, char *s, int n);
void freeCharArray(char **a, int size);
*/
import "C"
import (
	"github.com/openziti/sdk-golang/ziti/edge/posture"
	"unsafe"
)

//export ziti_pq_domain_go
func ziti_pq_domain_go(ctx C.ziti_context, id *C.char, response_cb C.ziti_pr_domain_cb) {
	svcId := C.GoString(id)
	log.Debugf("domain posture check request [%s]", svcId)

	domain := C.CString(posture.Domain())
	defer C.free(unsafe.Pointer(domain))

	log.Debugf("domain posture check response [%s]. domain: %v", svcId, posture.Domain())
	C.return_domain_info_c(ctx, id, response_cb, domain)
}

//export ziti_pq_process_go
func ziti_pq_process_go(ztx C.ziti_context, id *C.char, path *C.char, response_cb C.ziti_pr_process_cb) {
	svcId := C.GoString(id)
	gopath := C.GoString(path)
	log.Debugf("proc posture check request [%s, %s]", svcId, gopath)

	pi := posture.Process(gopath)
	sha := C.CString(pi.Hash)
	defer C.free(unsafe.Pointer(sha))

	signers := pi.SignerFingerprints
	numSigners := C.int(len(signers))
	csigners := C.makeCharArray(numSigners)
	defer C.freeCharArray(csigners, numSigners)
	for i, s := range signers {
		C.setArrayString(csigners, C.CString(s), C.int(i))
	}

	log.Debugf("proc posture check response [%s, %s]. running:%t, hash:%s, signers:%v", svcId, gopath, pi.IsRunning, pi.Hash, signers)
	C.return_proc_info_c(ztx, id, path, response_cb, C.bool(pi.IsRunning), sha, csigners, numSigners)
}

//export ziti_pq_os_go
func ziti_pq_os_go(ztx C.ziti_context, id *C.char, response_cb C.ziti_pr_os_cb) {
	svcId := C.GoString(id)
	log.Debugf("os posture check request [%s]", svcId)

	oi := posture.Os()
	ostype := C.CString(oi.Type)
	defer C.free(unsafe.Pointer(ostype))
	osvers := C.CString(oi.Version)
	defer C.free(unsafe.Pointer(osvers))
	gosbuild := "unused"
	osbuild := C.CString(gosbuild)
	defer C.free(unsafe.Pointer(osbuild))

	log.Debugf("os posture check response [%s]. ostype:%s, osvers:%s, osbuild:%s", svcId, oi.Type, oi.Version, gosbuild)
	C.return_os_info_c(ztx, id, response_cb, ostype, osvers, osbuild)
}

//export ziti_pq_mac_go
func ziti_pq_mac_go(ztx C.ziti_context, id *C.char, response_cb C.ziti_pr_mac_cb) {
	svcId := C.GoString(id)
	log.Debugf("mac posture check request [%s]", svcId)

	macs := posture.MacAddresses()
	nummacs := C.int(len(macs))

	cmacs := C.makeCharArray(C.int(nummacs))
	defer C.freeCharArray(cmacs, nummacs)
	for i, s := range macs {
		C.setArrayString(cmacs, C.CString(s), C.int(i))
	}

	log.Debugf("mac posture check response [%s]. macs: %v", svcId, macs)
	C.return_mac_info_c(ztx, id, response_cb, cmacs, nummacs)
}
