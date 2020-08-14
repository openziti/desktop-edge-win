package ipchanges

import (
	"github.com/michaelquigley/pfxlog"
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
    log = pfxlog.Logger()
)

func OnIPChange(callback func()) {
	log.Debugf("Library [ws2_32.dll] loaded at %#v", modws2_32.Handle())
	log.Debugf("Library [iphlpapi.dll] loaded at %#v", modiphlpapi.Handle())

	log.Debugf("Symbol [WSACreateEvent] loaded at %#v", procWSACreateEvent.Addr())
	log.Debugf("Symbol [NotifyAddrChange] loaded at %#v", procNotifyAddrChange.Addr())

	var (
		err error
		overlap *windows.Overlapped = &windows.Overlapped{}
	)

	log.Debugf("Invoking WSACreateEvent()")
	overlap.HEvent, err = WSACreateEvent()
	if err != nil {
		log.Fatalf("failed to create internal windows event: %s", err)
	} else {
		log.Debugf("Got handle at: %#v\n", overlap.HEvent)
	}

event_loop:
	for {
		log.Debugf("Invoking NotifyAddrChange()")
		notifyHandle := windows.Handle(0)
		syscall.Syscall(uintptr(procNotifyAddrChange.Addr()), 2, uintptr(notifyHandle), uintptr(unsafe.Pointer(overlap)), 0)

		log.Debugf("Waiting for network changes")
		event, err := windows.WaitForSingleObject(overlap.HEvent, windows.INFINITE)

		switch event {
		case windows.WAIT_OBJECT_0:
			log.Info("Windows kernel notified of a network address change")
			callback()
		default:
			log.Error(err)
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