package service

import (
	"fmt"
)

func GetIdentities(active bool) {
	log.Info("fetching identities...")

	rts.LoadConfig()
	tunState := rts.IsTunConnected()
	fmt.Printf("Tun status %t", tunState)

	fmt.Println(rts.ids)
}
