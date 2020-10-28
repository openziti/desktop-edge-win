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

package api

import (
	"net"
	"unsafe"
)

type DesktopEdgeIface interface {
	AddRoute(destination net.IPNet, nextHop net.IP, metric uint32) error
	RemoveRoute(destination net.IPNet, nextHop net.IP) error

	InterceptDNS()
	ReleaseDNS()

	InterceptIP()
	ReleaseIP()

	Close()
}

type DesktopEdgeManager interface {
	AddIdentity()
	RemoveIdentity()
	Status()
	TunnelState()
	IdentityToggle()
	SetLogLevel()
	Debug()
	SaveState()
	CreateTun()
	FindIdentity()
}

type Connection interface {
	Shutdown()
	GetMetrics() (int64, int64, bool)
	UnsafePointer() unsafe.Pointer

	Controller() string
	Name() string
}