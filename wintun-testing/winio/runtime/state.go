package runtime

import (
	"bufio"
	"encoding/json"
	"github.com/michaelquigley/pfxlog"
	"os"
	"wintun-testing/winio/config"
	"wintun-testing/winio/dto"
	"wintun-testing/winio/idutil"
)

var log = pfxlog.Logger()

type TunnelerState struct {
	TunnelActive bool
	Identities   []*dto.Identity
	IpInfo       *TunIpInfo `json:"IpInfo,omitempty"`
}

type TunIpInfo struct {
	Ip string
	Subnet string
	MTU uint16
	DNS string
}

func (t *TunnelerState) RemoveByFingerprint(fingerprint string) {
	log.Debugf("removing fingerprint: %s", fingerprint)
	if index, _ := t.Find(fingerprint); index < len(t.Identities) {
		t.Identities = append(t.Identities[:index], t.Identities[index+1:]...)
	}
}

func (t *TunnelerState) Find(fingerprint string) (int, *dto.Identity) {
	for i, n := range t.Identities {
		if n.FingerPrint == fingerprint {
			return i, n
		}
	}
	return len(t.Identities), nil
}

func (t *TunnelerState) RemoveByIdentity(id dto.Identity) {
	t.RemoveByFingerprint(id.FingerPrint)
}

func (t *TunnelerState) FindByIdentity(id dto.Identity) (int, *dto.Identity) {
	return t.Find(id.FingerPrint)
}

func SaveState(s *TunnelerState) {
	// overwrite file if it exists
	_ = os.MkdirAll(config.Path(), 0640)

	cfg, err := os.OpenFile(config.File(), os.O_RDWR|os.O_CREATE|os.O_TRUNC, 0640)
	if err != nil {
		panic(err)
	}
	w := bufio.NewWriter(bufio.NewWriter(cfg))
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	s.IpInfo = nil
	_ = enc.Encode(s)
	_ = w.Flush()

	err = cfg.Close()
	if err != nil{
		panic(err)
	}
}

func (s TunnelerState) Clean() TunnelerState {
	rtn := TunnelerState{
		TunnelActive: s.TunnelActive,
		Identities:   make([]*dto.Identity, len(s.Identities)),
		IpInfo:       s.IpInfo,
	}
	for i, id := range s.Identities {
		rtn.Identities[i] = idutil.Clean(*id)
	}

	return rtn
}