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

*/
import "C"
import (
	"fmt"
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
	"strings"
	"unsafe"
)

type mfaCodes struct {
	codes       []string
	fingerprint string
	err         error
}

var codes = make(chan mfaCodes)
var emptyCodes []string
var mfaAuthResults = make(chan string)

//export ziti_aq_mfa_cb_go
func ziti_aq_mfa_cb_go(ztx C.ziti_context, mfa_ctx unsafe.Pointer, aq_mfa *C.ziti_auth_query_mfa, response_cb C.ziti_ar_mfa_cb) {
	appCtx := C.ziti_app_ctx(ztx)
	if appCtx != C.NULL {
		log.Warnf("xxxxx ziti_aq_mfa_cb_go called")
		zid := (*ZIdentity)(appCtx)
		mfa := &Mfa{
			mfaContext: mfa_ctx,
			authQuery:  aq_mfa,
			responseCb: response_cb,
		}
		zid.mfa = mfa
		zid.MfaNeeded = true
		zid.MfaEnabled = true
		log.Warnf("xxxxx ziti_aq_mfa_cb_go mfa set on ziti id %p with name %s and fingerprint %s", zid, zid.Name, zid.Fingerprint)
	} else {
		log.Warnf("xxxxx ziti_aq_mfa_cb_go called but the context was NOT found in the map. This is unexpected. Please report.")
	}
}

func EnableMFA(id *ZIdentity, fingerprint string) {
	cfp := C.CString(fingerprint)
	//cfp is free'ed in ziti_mfa_enroll_cb_go
	C.ziti_mfa_enroll(id.czctx, C.ziti_mfa_cb(C.ziti_mfa_enroll_cb_go), unsafe.Pointer(cfp))
}

//export ziti_mfa_enroll_cb_go
func ziti_mfa_enroll_cb_go(_ C.ziti_context, status C.int, enrollment *C.ziti_mfa_enrollment, fingerprintP unsafe.Pointer) {
	isVerified := bool(enrollment.is_verified)
	url := C.GoString(enrollment.provisioning_url)
	fp := C.GoString((*C.char)(fingerprintP))
	C.free(fingerprintP) //CString created when executing EnableMFA

	var m = dto.MfaEvent{
		ActionEvent:     dto.MFA_ENROLLMENT_CHALLENGE,
		Fingerprint:     fp,
		IsVerified:      isVerified,
		ProvisioningUrl: url,
		RecoveryCodes:   populateStringSlice(enrollment.recovery_codes),
	}
	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when enrolling mfa: %v", ego)
	} else {
		log.Warnf("xxxx sending ziti_mfa_enroll response back to UI for %s. verified: %t. error: %s", fp, m.IsVerified, m.Error)
		goapi.BroadcastEvent(m)
	}
}

func VerifyMFA(id *ZIdentity, fingerprint string, code string) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))

	cfp := C.CString(fingerprint)
	//cfp is free'ed in ziti_mfa_cb_verify_go
	log.Errorf("VERIFYMFA: %v %v", fingerprint, code)
	C.ziti_mfa_verify(id.czctx, ccode, C.ziti_mfa_cb(C.ziti_mfa_cb_verify_go), unsafe.Pointer(cfp))
}

//export ziti_mfa_cb_verify_go
func ziti_mfa_cb_verify_go(_ C.ziti_context, status C.int, fingerprintP *C.char) {
	fp := C.GoString(fingerprintP)
	C.free(unsafe.Pointer(fingerprintP)) //CString created when executing VerifyMFA

	log.Debugf("ziti_mfa_cb_verify_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent: dto.MFA_AUTH_RESPONSE,
		Fingerprint: fp,
		IsVerified:  false,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when enrolling mfa: %v", ego)
		m.Error = ego
	} else {
		log.Warnf("xxxx Identity with fingerprint %s has successfully verified MFA", fp)
		m.IsVerified = true
	}

	log.Warnf("xxxx sending ziti_mfa_verify response back to UI for %s. verified: %t. error: %s", fp, m.IsVerified, m.Error)
	goapi.BroadcastEvent(m)
}

func ReturnMfaCodes(id *ZIdentity, fingerprint string, code string) ([]string, error) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))
	cfp := C.CString(fingerprint)
	defer C.free(unsafe.Pointer(cfp))
	log.Errorf("xxxx ReturnMfaCodesa: %v %v", fingerprint, code)
	C.ziti_mfa_get_recovery_codes(id.czctx, ccode, C.ziti_mfa_recovery_codes_cb(C.ziti_mfa_recovery_codes_cb_return), unsafe.Pointer(cfp))

	rtn := <-codes
	log.Errorf("xxxx ReturnMfaCodesc: %v %v", fingerprint, rtn)
	if fingerprint != rtn.fingerprint {
		log.Warnf("unexpected condition correlating mfa codes returned! %s != %s", fingerprint, rtn.fingerprint)
		return emptyCodes, rtn.err
	}
	return rtn.codes, rtn.err
}

//export ziti_mfa_recovery_codes_cb_return
func ziti_mfa_recovery_codes_cb_return(_ C.ziti_context, status C.int, recoveryCodes **C.char, fingerprintP *C.char) {
	log.Errorf("xxxx ReturnMfaCodesb: %v %p", status, fingerprintP)
	fp := C.GoString((*C.char)(fingerprintP))
	var ego error
	var theCodes []string
	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego = fmt.Errorf("%s", C.GoString(e))
		log.Errorf("Error encounted when returning mfa recovery codes: %v", ego)
	} else {
		theCodes = populateStringSlice(recoveryCodes)
		ego = nil
	}
	codes <- mfaCodes{
		codes:       theCodes,
		fingerprint: fp,
		err: ego,
	}
}

func GenerateMfaCodes(id *ZIdentity, fingerprint string, code string) ([]string, error) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))
	cfp := C.CString(fingerprint)
	defer C.free(unsafe.Pointer(cfp))
	log.Errorf("xxxx GenerateMfaCodesa: %v %v == %p %p ==", fingerprint, code, cfp, cfp)
	C.ziti_mfa_new_recovery_codes(id.czctx, ccode, C.ziti_mfa_recovery_codes_cb(C.ziti_mfa_recovery_codes_cb_generate), unsafe.Pointer(cfp))

	rtn := <-codes
	log.Errorf("xxxx GenerateMfaCodesc: %v %v", fingerprint, rtn)
	if fingerprint != rtn.fingerprint {
		log.Warnf("unexpected condition correlating mfa codes when regenerating! %s != %s", fingerprint, rtn.fingerprint)
		return emptyCodes, rtn.err
	}
	return rtn.codes, rtn.err
}

//export ziti_mfa_recovery_codes_cb_generate
func ziti_mfa_recovery_codes_cb_generate(_ C.ziti_context, status C.int, recoveryCodes **C.char, fingerprintP *C.char) {
	log.Errorf("xxxx GenerateMfaCodesb: %v == %p ==", status, fingerprintP)
	fp := C.GoString((*C.char)(fingerprintP))
	var theCodes []string
	var ego error
	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego = fmt.Errorf("%s", C.GoString(e))
		log.Errorf("Error encountedziti_ar_mfa_cb when generating mfa recovery codes: %v", ego)
	} else {
		theCodes = populateStringSlice(recoveryCodes)
		ego = nil
	}
	codes <- mfaCodes{
		codes:       theCodes,
		fingerprint: fp,
		err: ego,
	}
}

func populateStringSlice(c_char_array **C.char) []string {
	var strs []string
	i := 0
	for {
		var cstr = C.ziti_char_array_get(c_char_array, C.int(i))
		if cstr == nil {
			break
		}
		strs = append(strs, C.GoString(cstr))
		i++
	}
	return strs
}

func AuthMFA(id *ZIdentity, fingerprint string, code string) string {
	if id.mfa == nil {
		log.Warnf("xxxx AuthMFA called but mfa is nil. This usually is because AuthMFA is called from an unenrolled MFA endpoint")
		return "Identity has not authenticated yet"
	}
	if id.mfa.responseCb == nil {
		log.Warnf("xxxx AuthMFA called but response cb is nil. This usually is because the session is already validiated. returning true from AuthMFA")
		return ""
	}

	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))
	log.Errorf("AuthMFA: %v %v", fingerprint, code)

	cfp := C.CString(fingerprint)
	defer C.free(unsafe.Pointer(cfp))
	C.ziti_mfa_auth_request(id.mfa.responseCb, id.czctx, id.mfa.mfaContext, ccode, C.ziti_ar_mfa_status_cb(C.ziti_ar_mfa_status_cb_go), cfp)
	r := strings.TrimSpace(<-mfaAuthResults)

	if r == "" {
		log.Error("xxxx mfa successfully authenticated. removing callback from mfa")
		id.mfa.responseCb = nil
	}
	return r
}

//export ziti_ar_mfa_status_cb_go
func ziti_ar_mfa_status_cb_go(ztx C.ziti_context, mfa_ctx unsafe.Pointer, status C.int, fingerprintC *C.char) {
	log.Error("xxxx ziti_ar_mfa_status_cb_go with status %v", status)
	if status == C.ZITI_OK {
		mfaAuthResults <- ""
	} else {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		mfaAuthResults <- ego
	}
}

func RemoveMFA(id *ZIdentity, fingerprint string, code string) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))

	cfp := C.CString(fingerprint)
	//cfp is free'ed in ziti_mfa_cb_verify_go
	log.Errorf("VERIFYMFA: %v %v", fingerprint, code)
	C.ziti_mfa_remove(id.czctx, ccode, C.ziti_mfa_cb(C.ziti_mfa_cb_verify_go), unsafe.Pointer(cfp))
}

//export ziti_mfa_cb_remove_go
func ziti_mfa_cb_remove_go(_ C.ziti_context, status C.int, fingerprintP *C.char) {
	fp := C.GoString(fingerprintP)
	C.free(unsafe.Pointer(fingerprintP)) //CString created when executing VerifyMFA

	log.Debugf("ziti_mfa_cb_remove_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent: dto.MFA_AUTH_RESPONSE,
		Fingerprint: fp,
		IsVerified:  false,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when removing mfa: %v", ego)
		m.Error = ego
	} else {
		log.Warnf("xxxx Identity with fingerprint %s has successfully removed MFA", fp)
		m.IsVerified = true
	}

	log.Warnf("xxxx sending ziti_mfa_verify response back to UI for %s. verified: %t. error: %s", fp, m.IsVerified, m.Error)
	goapi.BroadcastEvent(m)
}