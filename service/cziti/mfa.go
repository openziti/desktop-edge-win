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

void return_ziti_ar_mfa_cb(ziti_context ztx, void* mfa_ctx, char* code);
*/
import "C"
import (
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"unsafe"
)

//export ziti_aq_mfa_cb_go
func ziti_aq_mfa_cb_go(ztx C.ziti_context, mfa_ctx unsafe.Pointer, aq_mfa *C.ziti_auth_query_mfa, response_cb C.ziti_ar_mfa_cb) {
	mzid, found := idMap.Load(ztx)
	if found {
		zid := mzid.(*ZIdentity)
		var auth = dto.MfaChallenge{
			ActionEvent: dto.MFA_CHALLENGE,
			Fingerprint: zid.Fingerprint,
		}
		goapi.BroadcastEvent(auth)
	} else {
		log.Warnf("ziti_aq_mfa_cb_go called but the context was NOT found in the map. This is unexpected. Please report.")
	}
}

//export ziti_mfa_enroll_cb_go
func ziti_mfa_enroll_cb_go(_ C.ziti_context, status C.int, enrollment *C.ziti_mfa_enrollment, fingerprintP unsafe.Pointer) {
	isVerified := bool(enrollment.is_verified)
	url := C.GoString(enrollment.provisioning_url)
	fp := C.GoString((*C.char)(fingerprintP))
	C.free(fingerprintP) //CString created when executing EnableMFA

	var m = dto.MfaEvent{
		ActionEvent:     dto.MFA_ERROR,
		Fingerprint:     fp,
		IsVerified:      isVerified,
		ProvisioningUrl: url,
		RecoveryCodes:   nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when enrolling mfa: %v", ego)
	} else {
		m.ActionEvent = dto.MFA_ENROLLMENT_CHALLENGE
		i := 0
		for {
			var cstr = C.ziti_char_array_get(enrollment.recovery_codes, C.int(i))
			if cstr == nil {
				break
			}
			m.RecoveryCodes = append(m.RecoveryCodes, C.GoString(cstr))
			i++
		}
		log.Debugf("mfa results for %s: %v %v %v", fp, isVerified, url, m.RecoveryCodes)
	}
	goapi.BroadcastEvent(m)
}

//export ziti_mfa_cb_go
func ziti_mfa_cb_go(_ C.ziti_context, status C.int, fingerprintP unsafe.Pointer) {
	fp := C.GoString((*C.char)(fingerprintP))
	C.free(fingerprintP) //CString created when executing VerifyMFA

	log.Debugf("ziti_mfa_cb_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent: dto.MFA_ERROR,
		Fingerprint: fp,
		IsVerified:  false,
		//ProvisioningUrl: url,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when enrolling mfa: %v", ego)
	} else {
		log.Debugf("Identity with fingerprint %s has successfully verified MFA", fp)
	}

	goapi.BroadcastEvent(m)
}

func EnableMFA(id *ZIdentity, fingerprint string) {
	cfp := C.CString(fingerprint)
	//cfp is free'ed in ziti_mfa_enroll_cb_go
	C.ziti_mfa_enroll(id.czctx, C.ziti_mfa_cb(C.ziti_mfa_enroll_cb_go), unsafe.Pointer(cfp))
}
func VerifyMFA(id *ZIdentity, fingerprint string, totp string) {
	ctotp := C.CString(totp)
	defer C.free(unsafe.Pointer(ctotp))

	cfp := C.CString(fingerprint)
	//cfp is free'ed in ziti_mfa_cb_go
	log.Errorf("VERIFYMFA: %v %v", fingerprint, totp)
	C.ziti_mfa_verify(id.czctx, ctotp, C.ziti_mfa_cb(C.ziti_mfa_cb_go), unsafe.Pointer(cfp))
}
