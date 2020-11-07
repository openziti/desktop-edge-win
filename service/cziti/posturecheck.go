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
func ziti_pq_domain_go(ctx C.ziti_context, id *C.char, response_cb C.ziti_pr_domain_cb ) {
	domain := C.CString(posture.Domain())
	defer C.free(unsafe.Pointer(domain))

	log.Debugf("submitting domain posture check info. domain: %v", posture.Domain())
	C.return_domain_info_c(ctx, id, response_cb, domain)
}

//export ziti_pq_process_go
func ziti_pq_process_go(ztx C.ziti_context, id *C.char, path *C.char, response_cb C.ziti_pr_process_cb) {
	gopath := C.GoString(path)
	pi := posture.Process(gopath)

	sha := C.CString(pi.Hash)
	defer C.free(unsafe.Pointer(sha))

	signers := pi.SignerFingerprints
	numSigners := C.int(len(signers))
	cargs := C.makeCharArray(numSigners)
	defer C.freeCharArray(cargs, numSigners)
	for i, s := range signers {
		C.setArrayString(cargs, C.CString(s), C.int(i))
	}

	log.Debugf("submitting proc posture check info for %s. running:%t, hash:%s, signers:%v", gopath, pi.IsRunning, pi.Hash, signers)
	C.return_proc_info_c(ztx, id, path, response_cb, C.bool(pi.IsRunning), sha, cargs, numSigners)
}
//export ziti_pq_os_go
func ziti_pq_os_go(ztx C.ziti_context, id *C.char, response_cb C.ziti_pr_os_cb) {
	oi := posture.Os()
	ostype := C.CString(oi.Type)
	defer C.free(unsafe.Pointer(ostype))
	osvers := C.CString(oi.Version)
	defer C.free(unsafe.Pointer(osvers))
	osbuild := C.CString(oi.Build)
	defer C.free(unsafe.Pointer(osbuild))

	log.Debugf("submitting os posture check info: ostype:%s, osvers:%s, osbuild:%s", oi.Type, oi.Version, oi.Build)
	C.return_os_info_c(ztx, id, response_cb, ostype, osvers, osbuild)
}
//export ziti_pq_mac_go
func ziti_pq_mac_go(ztx C.ziti_context, id *C.char, response_cb C.ziti_pr_mac_cb) {
	macs := posture.MacAddresses()
	nummacs := C.int(len(macs))

	cargs := C.makeCharArray(C.int(nummacs))
	defer C.freeCharArray(cargs, nummacs)

	log.Debugf("submitting mac posture check info. macs: %v", macs)
	C.return_mac_info_c(ztx, id, response_cb, cargs, nummacs)
}
