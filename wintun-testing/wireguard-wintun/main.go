package main

import (
  "bufio"
  "fmt"
  "log"
  "strings"

  "wintun-testing/wireguard"

  "golang.org/x/sys/windows"
  "golang.org/x/sys/windows/svc"
  "golang.org/x/sys/windows/svc/mgr"
  "golang.zx2c4.com/wireguard/device"
  "golang.zx2c4.com/wireguard/ipc"
  "golang.zx2c4.com/wireguard/tun"
  "golang.zx2c4.com/wireguard/windows/conf"
  "golang.zx2c4.com/wireguard/windows/elevate"
  "golang.zx2c4.com/wireguard/windows/ringlogger"
  "golang.zx2c4.com/wireguard/windows/services"
  "golang.zx2c4.com/wireguard/windows/version"
)

const (
  ExitSetupSuccess = 0
  ExitSetupFailed  = 1
)

func main(){
  var r <-chan svc.ChangeRequest
  var changes chan<- svc.Status
  confPath := "c:\\git\\github\\ziti-windows-uwp\\wintun-testing\\simple.conf"
  interfaceName := "ZitiTUN"

  serviceError := services.ErrorSuccess
  conf, err := conf.LoadFromPath(confPath)
  if err != nil {
    serviceError = services.ErrorLoadConfiguration
    return
  }
  conf.DeduplicateNetworkEntries()

  logPrefix := fmt.Sprintf("[%s] ", conf.Name)
  log.SetPrefix(logPrefix)

  log.Println("Starting", version.UserAgent())


  if m, err := mgr.Connect(); err == nil {
    if lockStatus, err := m.LockStatus(); err == nil && lockStatus.IsLocked {
      /* If we don't do this, then the Wintun installation will block forever, because
       * installing a Wintun device starts a service too. Apparently at boot time, Windows
       * 8.1 locks the SCM for each service start, creating a deadlock if we don't announce
       * that we're running before starting additional services.
       */
      log.Printf("SCM locked for %v by %s, marking service as started", lockStatus.Age, lockStatus.Owner)
      changes <- svc.Status{State: svc.Running}
    }
    m.Disconnect()
  }


  if serviceError != services.ErrorSuccess {
    panic(serviceError)
  }


  var watcher *wireguard.InterfaceWatcher
  log.Println("Watching network interfaces")
  watcher, err = wireguard.WatchInterface()
  if err != nil {
    serviceError = services.ErrorSetNetConfig
    return
  }

  log.Println("Resolving DNS names")
  uapiConf, err := conf.ToUAPI()
  if err != nil {
    serviceError = services.ErrorDNSLookup
    return
  }

  log.Println("Creating Wintun interface")
  tunDevice, err := tun.CreateTUN(interfaceName, 0)
  defer tunDevice.Close()
  if err != nil {
    serviceError = services.ErrorCreateWintun
    return
  }
  nativeTun := tunDevice.(*tun.NativeTun)
  wintunVersion, ndisVersion, err := nativeTun.Version()
  if err != nil {
    log.Printf("Warning: unable to determine Wintun version: %v", err)
  } else {
    log.Printf("Using Wintun/%s (NDIS %s)", wintunVersion, ndisVersion)
  }

  log.Println("Enabling firewall rules")
  err = wireguard.EnableFirewall(conf, nativeTun)
  if err != nil {
    log.Printf("ERROR: %v", err)
    serviceError = services.ErrorFirewall
    return
  }

  log.Println("Enabling firewall rules")
  err = wireguard.EnableFirewall(conf, nativeTun)
  if err != nil {
    serviceError = services.ErrorFirewall
    return
  }

  log.Println("Dropping privileges")
  err = elevate.DropAllPrivileges(true)
  if err != nil {
    serviceError = services.ErrorDropPrivileges
    return
  }

  log.Println("Creating interface instance")
  logOutput := log.New(ringlogger.Global, logPrefix, 0)
  logger := &device.Logger{logOutput, logOutput, logOutput}
  dev := device.NewDevice(tunDevice, logger)

  log.Println("Setting interface configuration")
  uapi, err := ipc.UAPIListen(conf.Name)
  if err != nil {
    serviceError = services.ErrorUAPIListen
    return
  }
  ipcErr := dev.IpcSetOperation(bufio.NewReader(strings.NewReader(uapiConf)))
  if ipcErr != nil {
    err = ipcErr
    serviceError = services.ErrorDeviceSetConfig
    return
  }

  log.Println("Bringing peers up")
  dev.Up()

  watcher.Configure(dev, conf, nativeTun)

  log.Println("Listening for UAPI requests")
  go func() {
    for {
      conn, err := uapi.Accept()
      if err != nil {
        continue
      }
      go dev.IpcHandle(conn)
    }
  }()

  changes <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}
  log.Println("Startup complete")

  for {
    select {
    case c := <-r:
      switch c.Cmd {
      case svc.Stop, svc.Shutdown:
        return
      case svc.Interrogate:
        changes <- c.CurrentStatus
      default:
        log.Printf("Unexpected service control request #%d\n", c)
      }
    case <-dev.Wait():
      return
    case e := <-watcher.Errors:
      serviceError, err = e.ServiceError, e.Err
      return
    }
  }


/*


   "golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
   "golang.zx2c4.com/wireguard/windows/version"
   "log"
   "net"
   "os"
   "os/signal"
   user2 "os/user"
   "strings"
   "sync"
   "syscall"









  fmt.Printf("bootstrapped conf: %v", serviceName)
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
  wintunVersion, ndisVersion, err := nativeTunDevice.Version()
  if err != nil {
    log.Printf("Warning: unable to determine Wintun version: %v", err)
  } else {
    log.Printf("Using Wintun/%s (NDIS %s)", wintunVersion, ndisVersion)
  }

  log.Println("Dropping privileges")
  err = elevate.DropAllPrivileges(true)
  if err != nil {
    serviceError = services.ErrorDropPrivileges
    return
  }


  log.Println("Creating interface instance")
  logOutput := log.New(ringlogger.Global, logPrefix, 0)
  logger := &device.Logger{logOutput, logOutput, logOutput}
  dev := device.NewDevice(wintun, logger)








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
*/
  // clean up

//  logger.Info.Println("Shutting down")
}



func fatal(v ...interface{}) {
  panic(windows.StringToUTF16Ptr(fmt.Sprint(v...)))
}
