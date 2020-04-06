package main

import (
  "fmt"
  "golang.org/x/sys/windows"
  "golang.zx2c4.com/wireguard/device"
  "golang.zx2c4.com/wireguard/ipc"
  "golang.zx2c4.com/wireguard/tun"
  "golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
  "net"
  "os"
  "os/signal"
  user2 "os/user"
  "syscall"
)

const (
  ExitSetupSuccess = 0
  ExitSetupFailed  = 1
)

func main(){


  user,err := user2.Current()

  fmt.Printf("Hi there: %v", user)

  if len(os.Args) != 2 {
    os.Exit(ExitSetupFailed)
  }
  interfaceName := os.Args[1]

  fmt.Fprintln(os.Stderr, "Warning: this is a test program for Windows, mainly used for debugging this Go package. For a real WireGuard for Windows client, the repo you want is <https://git.zx2c4.com/wireguard-windows/>, which includes this code as a module.")

  logger := device.NewLogger(
    device.LogLevelDebug,
    fmt.Sprintf("(%s) ", interfaceName),
  )
  logger.Info.Println("Starting wireguard-go version", device.WireGuardGoVersion)
  logger.Debug.Println("Debug log enabled")

  tunDevice, err := tun.CreateTUN(interfaceName, 0)
  //defer tunDevice.Close() _note_ the device.close() seems to be closing the tun so this is not needed
  if err == nil {
    realInterfaceName, err2 := tunDevice.Name()
    if err2 == nil {
      interfaceName = realInterfaceName
    }
  } else {
    logger.Error.Println("Failed to create TUN device:", err)
    os.Exit(ExitSetupFailed)
  }


  nativeTunDevice := tunDevice.(*tun.NativeTun)
  luid := winipcfg.LUID(nativeTunDevice.LUID())
  //xxx ip, ipnet, _ := net.ParseCIDR("10.82.31.4/24")
  ip, ipnet, _ := net.ParseCIDR("169.254.100.200/32")
  err = luid.SetIPAddresses([]net.IPNet{{ip, ipnet.Mask}})
  if err != nil {
    fatal(err)
  }
  err = luid.SetRoutes([]*winipcfg.RouteData{{*ipnet, ipnet.IP, 0}})
  if err != nil {
    fatal(err)
  }










  device := device.NewDevice(tunDevice, logger)
  defer device.Close()
  device.Up()
  logger.Info.Println("Device started")

  uapi, err := ipc.UAPIListen(interfaceName)
  if err != nil {
    logger.Error.Println("Failed to listen on uapi socket:", err)
    os.Exit(ExitSetupFailed)
  }
  defer uapi.Close()

  errs := make(chan error)
  term := make(chan os.Signal, 1)

  go func() {
    for {
      conn, err := uapi.Accept()
      if err != nil {
        errs <- err
        return
      }
      go device.IpcHandle(conn)
    }
  }()
  logger.Info.Println("UAPI listener started")

  // wait for program to terminate

  signal.Notify(term, os.Interrupt)
  signal.Notify(term, os.Kill)
  signal.Notify(term, syscall.SIGTERM)

  select {
  case <-term:
  case <-errs:
  case <-device.Wait():
  }

  // clean up

  logger.Info.Println("Shutting down")
}



func fatal(v ...interface{}) {
  panic(windows.StringToUTF16Ptr(fmt.Sprint(v...)))
}
