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
	"strings"
	"time"
	"unsafe"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/dto"
)

type mfaCodes struct {
	codes       []string
	fingerprint string
	err         error
}

var emptyCodes []string
var mfaAuthResults = make(chan string)
var mfaAuthVerifyResults = make(chan string)

func EnableMFA(id *ZIdentity) {
	C.ziti_mfa_enroll(id.czctx, C.ziti_mfa_cb(C.ziti_mfa_enroll_cb_go), unsafe.Pointer(C.CString(id.Fingerprint)))
}

//export ziti_mfa_enroll_cb_go
func ziti_mfa_enroll_cb_go(_ C.ziti_context, status C.int, enrollment *C.ziti_mfa_enrollment, cFingerprint *C.char) {
	defer C.free(unsafe.Pointer(cFingerprint))
	fp := C.GoString(cFingerprint)
	if unsafe.Pointer(enrollment) == C.NULL {
		log.Warnf("'enrollment' is null in mfa enroll cb for %s", fp)
		return
	}
	isVerified := bool(enrollment.is_verified)
	url := C.GoString(enrollment.provisioning_url)

	var m = dto.MfaEvent{
		ActionEvent:     dto.MFAEnrollmentChallengeEvent,
		Fingerprint:     fp,
		Successful:      isVerified,
		ProvisioningUrl: url,
		RecoveryCodes:   populateStringSlice(enrollment.recovery_codes),
	}
	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when enrolling mfa: %v", ego)
	} else {
		log.Infof("mfa enrollment begins for fingerprint: %s", fp)
		goapi.BroadcastEvent(m)
	}
}

// requires that the identity already be fully authenticated and used specifically for enrollment
func VerifyMFA(id *ZIdentity, code string) error {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))

	log.Tracef("verifying MFA for fingerprint: %s using code: %s", id.Fingerprint, code)
	C.ziti_mfa_verify(id.czctx, ccode, C.ziti_mfa_cb(C.ziti_mfa_cb_verify_go), unsafe.Pointer(C.CString(id.Fingerprint)))
	authVerifyResult := strings.TrimSpace(<-mfaAuthVerifyResults)
	if authVerifyResult == "" {
		return nil
	}
	return fmt.Errorf("error in verifyMFA: %v", authVerifyResult)
}

//export ziti_mfa_cb_verify_go
func ziti_mfa_cb_verify_go(_ C.ziti_context, status C.int, cFingerprint *C.char) {
	defer C.free(unsafe.Pointer(cFingerprint))
	fp := C.GoString(cFingerprint)
	log.Debugf("ziti_mfa_cb_verify_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent:   dto.MFAEnrollmentVerificationEvent,
		Fingerprint:   fp,
		Successful:    false,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when verifying mfa: %v", ego)
		m.Error = ego
		mfaAuthVerifyResults <- ego
	} else {
		log.Infof("mfa successfully verified for fingerprint: %s", fp)
		goapi.UpdateMfa(fp, true, false)
		m.Successful = true
		mfaAuthVerifyResults <- ""
	}

	log.Debugf("mfa verify callback. sending ziti_mfa_verify response back to UI for %s. verified: %t. error: %s", fp, m.Successful, m.Error)
	goapi.BroadcastEvent(m)
}

var rtnCodes = make(chan mfaCodes)

func ReturnMfaCodes(id *ZIdentity, code string) ([]string, error) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))
	cfp := C.CString(id.Fingerprint)
	defer C.free(unsafe.Pointer(cfp))
	log.Debugf("asking for ReturnMfaCodes for fingerprint: %s with code: %s", id.Fingerprint, code)
	C.ziti_mfa_get_recovery_codes(id.czctx, ccode, C.ziti_mfa_recovery_codes_cb(C.ziti_mfa_recovery_codes_cb_return), unsafe.Pointer(cfp))

	select {
	case rtn := <-rtnCodes:
		log.Debugf("mfa codes returned ReturnMfaCodes: %s", id.Fingerprint)
		if id.Fingerprint != rtn.fingerprint {
			log.Warnf("unexpected condition correlating mfa codes returned! %s != %s", id.Fingerprint, rtn.fingerprint)
			return emptyCodes, rtn.err
		}
		return rtn.codes, rtn.err
	case <-time.After(10 * time.Second):
		return emptyCodes, fmt.Errorf("returning mfa codes has timed out")
	}
}

// used when an identity is partially authenticated and waiting on 2fa, i.e. for authentication and for timeouts
func AuthMFA(id *ZIdentity, code string) error {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))

	log.Tracef("authenticating MFA for fingerprint: %s using code: %s", id.Fingerprint, code)
	C.ziti_mfa_auth(id.czctx, ccode, C.ziti_mfa_cb(C.ziti_auth_mfa_status_cb_go), unsafe.Pointer(C.CString(id.Fingerprint)))
	authResult := strings.TrimSpace(<-mfaAuthResults)

	if authResult == "" {
		id.MfaEnabled = true
		id.MfaNeeded = false
		return nil
	}
	return fmt.Errorf("error in authMFA: %v", authResult)
}

//export ziti_auth_mfa_status_cb_go
func ziti_auth_mfa_status_cb_go(ztx C.ziti_context, status C.int, cFingerprint *C.char) {
	defer C.free(unsafe.Pointer(cFingerprint))
	fp := C.GoString(cFingerprint)

	log.Debugf("ziti_auth_mfa_status_cb_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent:   dto.MFAAuthenticationEvent,
		Fingerprint:   fp,
		Successful:    false,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when authenticating 2f mfa: %v", ego)
		m.Error = ego
		mfaAuthResults <- ego
	} else {
		log.Infof("Identity with fingerprint %s has successfully authenticated MFA", fp)
		m.Successful = true
		goapi.UpdateMfa(fp, true, false)
		mfaAuthResults <- ""
	}

	log.Debugf("sending ziti_mfa_auth response back to UI for %s. verified: %t. error: %s", fp, m.Successful, m.Error)
	goapi.BroadcastEvent(m)
}

//export ziti_mfa_recovery_codes_cb_return
func ziti_mfa_recovery_codes_cb_return(_ C.ziti_context, status C.int, recoveryCodes **C.char, cFingerprint *C.char) {
	fp := C.GoString(cFingerprint)
	log.Debugf("ziti_mfa_recovery_codes_cb_return called with status and fingerprint: %s with status: %v", fp, status)
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
	rtnCodes <- mfaCodes{
		codes:       theCodes,
		fingerprint: fp,
		err:         ego,
	}
	log.Infof("recovery codes have been returned for fingerprint: %s", fp)
}

var genCodes = make(chan mfaCodes)

func GenerateMfaCodes(id *ZIdentity, code string) ([]string, error) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))
	cfp := C.CString(id.Fingerprint)
	defer C.free(unsafe.Pointer(cfp))
	log.Debugf("GenerateMfaCodes called for fingerprint: %s with code: %s", id.Fingerprint, code)
	C.ziti_mfa_new_recovery_codes(id.czctx, ccode, C.ziti_mfa_recovery_codes_cb(C.ziti_mfa_recovery_codes_cb_generate), unsafe.Pointer(cfp))
	select {
	case rtn := <-genCodes:
		log.Debugf("GenerateMfaCodes complete for fingerprint: %s", id.Fingerprint)
		if id.Fingerprint != rtn.fingerprint {
			log.Warnf("unexpected condition correlating mfa codes when regenerating! %s != %s", id.Fingerprint, rtn.fingerprint)
			return emptyCodes, rtn.err
		}
		return rtn.codes, rtn.err
	case <-time.After(10 * time.Second):
		return emptyCodes, fmt.Errorf("generating mfa codes has timed out for fingerprint: %s", id.Fingerprint)
	}
}

//export ziti_mfa_recovery_codes_cb_generate
func ziti_mfa_recovery_codes_cb_generate(_ C.ziti_context, status C.int, recoveryCodes **C.char, cFingerprint *C.char) {
	fp := C.GoString(cFingerprint)
	log.Debugf("csdk has called back for GenerateMfaCodes for fingerprint: %s with status: %v", fp, status)
	var theCodes []string
	var ego error
	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego = fmt.Errorf("%s", C.GoString(e))
		log.Errorf("Error when generating mfa recovery codes: %v", ego)
	} else {
		theCodes = populateStringSlice(recoveryCodes)
		ego = nil
	}
	genCodes <- mfaCodes{
		codes:       theCodes,
		fingerprint: fp,
		err:         ego,
	}
	log.Infof("recovery codes have been regenerated for fingerprint: %s", fp)
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

func RemoveMFA(id *ZIdentity, code string) {
	ccode := C.CString(code)
	defer C.free(unsafe.Pointer(ccode))

	log.Tracef("removing MFA for fingerprint: %s using code: %s", id.Fingerprint, code)
	C.ziti_mfa_remove(id.czctx, ccode, C.ziti_mfa_cb(C.ziti_mfa_cb_remove_go), unsafe.Pointer(C.CString(id.Fingerprint))) //c string freed in callback
}

//export ziti_mfa_cb_remove_go
func ziti_mfa_cb_remove_go(_ C.ziti_context, status C.int, cFingerprint *C.char) {
	defer C.free(unsafe.Pointer(cFingerprint))
	fp := C.GoString(cFingerprint)

	log.Debugf("ziti_mfa_cb_remove_go called for %s. status: %d for ", fp, int(status))
	var m = dto.MfaEvent{
		ActionEvent:   dto.MFAEnrollmentRemovedEvent,
		Fingerprint:   fp,
		Successful:    false,
		RecoveryCodes: nil,
	}

	if status != C.ZITI_OK {
		e := C.ziti_errorstr(status)
		ego := C.GoString(e)
		log.Errorf("Error encounted when removing mfa: %v", ego)
		m.Error = ego
	} else {
		log.Infof("Identity with fingerprint %s has successfully removed MFA", fp)
		m.Successful = true
		goapi.UpdateMfa(fp, false, false)
	}

	log.Debugf("sending ziti_mfa_verify response back to UI for %s. verified: %t. error: %s", fp, m.Successful, m.Error)
	goapi.BroadcastEvent(m)
}

func EndpointStateChanged(id *ZIdentity, woken bool, unlocked bool) {
	C.ziti_endpoint_state_change(id.czctx, C.bool(woken), C.bool(unlocked))
	log.Debugf("Endpoint status changed for id %s:%s - woken %t, unlocked %t", id.Name, id.Fingerprint, woken, unlocked)
}
