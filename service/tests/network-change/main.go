package main

import (
	"log"
	"syscall"
	"unsafe"

	"golang.org/x/sys/windows"
)

var (
	modws2_32   = windows.NewLazySystemDLL("ws2_32.dll")
	modiphlpapi = windows.NewLazySystemDLL("iphlpapi.dll")

	procWSACreateEvent   = modws2_32.NewProc("WSACreateEvent")
	procNotifyAddrChange = modiphlpapi.NewProc("NotifyAddrChange")
	procNotifyRouteChange = modiphlpapi.NewProc("NotifyRouteChange")
)

func main() {
	log.Printf("Library [ws2_32.dll] loaded at %#v", modws2_32.Handle())
	log.Printf("Library [iphlpapi.dll] loaded at %#v", modiphlpapi.Handle())

	log.Printf("Symbol [WSACreateEvent] loaded at %#v", procWSACreateEvent.Addr())
	log.Printf("Symbol [NotifyAddrChange] loaded at %#v", procNotifyAddrChange.Addr())
	log.Printf("Symbol [NotifyRouteChange] loaded at %#v", procNotifyRouteChange.Addr())

	var (
		err error
		overlap *windows.Overlapped = &windows.Overlapped{}
	)

	log.Println("Invoking WSACreateEvent()")
	overlap.HEvent, err = WSACreateEvent()
	if err != nil {
		log.Fatalf("failed to create internal windows event: %s", err)
	} else {
		log.Printf("Got handle at: %#v\n", overlap.HEvent)
	}

event_loop:
	for {
		log.Println("Invoking NotifyAddrChange()")
		notifyHandle := windows.Handle(0)
		syscall.Syscall(uintptr(procNotifyRouteChange.Addr()), 2, uintptr(notifyHandle), uintptr(unsafe.Pointer(overlap)), 0)

		log.Println("Waiting for network changes")
		event, err := windows.WaitForSingleObject(overlap.HEvent, windows.INFINITE)

		switch event {
		case windows.WAIT_OBJECT_0:
			log.Println("Windows kernel notified of a network address change")
			// TODO(lh): Act upon interface changes

		default:
			log.Println(err)
			break event_loop
		}
	}

	windows.Close(overlap.HEvent)
}

func WSACreateEvent() (windows.Handle, error) {
	handlePtr, _, errNum := syscall.Syscall(procWSACreateEvent.Addr(), 0, 0, 0, 0)
	if handlePtr == 0 {
		return 0, errNum
	} else {
		return windows.Handle(handlePtr), nil
	}
}