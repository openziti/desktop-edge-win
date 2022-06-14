@echo off
REM Copyright NetFoundry, Inc.
REM
REM Licensed under the Apache License, Version 2.0 (the "License");
REM you may not use this file except in compliance with the License.
REM You may obtain a copy of the License at
REM
REM https://www.apache.org/licenses/LICENSE-2.0
REM
REM Unless required by applicable law or agreed to in writing, software
REM distributed under the License is distributed on an "AS IS" BASIS,
REM WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
REM See the License for the specific language governing permissions and
REM limitations under the License.
REM
SET TUNNELER_SDK_DIR=%SVC_ROOT_DIR%deps\ziti-tunneler-sdk-c\
SET CGO_CFLAGS=-DNOGDI -I %TUNNELER_SDK_DIR%install\include
SET CGO_LDFLAGS=-L %TUNNELER_SDK_DIR%install\lib

set ZITI_TUNNEL_WIN_ROOT=%SVC_ROOT_DIR%..\

set /p BUILD_VERSION=<%ZITI_TUNNEL_WIN_ROOT%version

REM a stupid env var JUST to allow a space to be set into an environment variable using substring...
set ZITI_SPACES=:   :