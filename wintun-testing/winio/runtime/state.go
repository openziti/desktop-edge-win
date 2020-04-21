package runtime

import (
	"bufio"
	"encoding/json"
	"github.com/michaelquigley/pfxlog"
	"os"
	"wintun-testing/winio/config"
	"wintun-testing/winio/dto"
)

var log = pfxlog.Logger()
var TunState bool

type TunnelerState struct {
	TunnelActive bool
	Identities   []dto.Identity
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
			return i, &n
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

	file, err := os.OpenFile(config.File(), os.O_RDWR|os.O_CREATE|os.O_TRUNC, 0640)
	if err != nil {
		panic(err)
	}

	err = file.Close()
	if err != nil{
		panic(err)
	}

	w := bufio.NewWriter(bufio.NewWriter(file))
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	_ = enc.Encode(s)
	_ = w.Flush()
}
