<#
.SYNOPSIS
Turn a ziti-edge-tunnel crash dump into a readable stack.

.DESCRIPTION
Point this at a .dmp, at an unpacked feedback bundle, or at a feedback zip, and it prints the
faulting instruction and a symbolized backtrace.

It works out which ziti-edge-tunnel build produced the dump, downloads that exact binary from the
ziti-tunnel-sdk-c releases, and proves the download matches before trusting it. Nothing is staged by
hand.

Dumps written before the MiniDumpWithModuleHeaders fix carry no unwind data of their own, so the
matching binary is what makes them readable at all. Dumps written after it still need the binary for
function names, because MinGW keeps debug info as DWARF inside the exe and no PDB is ever published.

Whatever is missing, the script degrades instead of failing: no debugger still gets you the faulting
address and instruction pointer, no addr2line still gets you ordered frames as module offsets.

.PARAMETER path
A .dmp file, a feedback zip, or a directory holding an unpacked bundle. Directories and zips are
searched for both ziti-edge-tunnel.crash.dmp and ziti-edge-tunnel.stalled.dmp.

.PARAMETER version
ZET version to symbolize against, e.g. v1.11.4. Read from the bundle when not given.

.PARAMETER exeDir
Directory holding a ziti-edge-tunnel.exe to use as-is. Skips the download.

.PARAMETER cache
Where downloaded builds are kept. Defaults to %LOCALAPPDATA%\zet-symbols.

.PARAMETER force
Symbolize even when the binary does not match the dump. The output is then unreliable and is marked
as such.

.EXAMPLE
.\read-crash-dump.ps1 C:\temp\support\16157\kiosk17047-rivonia

.EXAMPLE
.\read-crash-dump.ps1 .\ziti-edge-tunnel.crash.dmp -version v1.11.4
#>

param (
    [Parameter(Mandatory = $true, Position = 0)][string]$path,
    [string]$version,
    [string]$exeDir,
    [string]$cache = (Join-Path $env:LOCALAPPDATA "zet-symbols"),
    [switch]$force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repo = "openziti/ziti-tunnel-sdk-c"

# MINIDUMP_STREAM_TYPE values we care about
$STREAM_MODULE_LIST = 4
$STREAM_EXCEPTION   = 6

# x64 CONTEXT field offsets
$CTX_RAX = 0x78
$CTX_RSP = 0x98
$CTX_RIP = 0xF8

# ---------------------------------------------------------------- dump parsing

function Read-Minidump {
    param([string]$dumpPath)

    $bytes = [System.IO.File]::ReadAllBytes($dumpPath)
    if ([System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne "MDMP") {
        throw "not a minidump: $dumpPath"
    }

    $streamCount = [BitConverter]::ToUInt32($bytes, 8)
    $dirRva      = [BitConverter]::ToUInt32($bytes, 12)

    $result = [ordered]@{
        File          = $dumpPath
        Modules       = @()
        HasException  = $false
        ExceptionCode = $null
        FaultAddress  = $null
        Rip           = $null
        Rsp           = $null
        Registers     = [ordered]@{}
    }

    for ($i = 0; $i -lt $streamCount; $i++) {
        $entry = $dirRva + ($i * 12)
        $type  = [BitConverter]::ToUInt32($bytes, $entry)
        $rva   = [BitConverter]::ToUInt32($bytes, $entry + 8)

        switch ($type) {
            $STREAM_MODULE_LIST {
                $count = [BitConverter]::ToUInt32($bytes, $rva)
                for ($m = 0; $m -lt $count; $m++) {
                    # MINIDUMP_MODULE is 108 bytes on x64
                    $mod = $rva + 4 + ($m * 108)
                    $nameRva = [BitConverter]::ToUInt32($bytes, $mod + 20)
                    $nameLen = [BitConverter]::ToUInt32($bytes, $nameRva)
                    $name = [System.Text.Encoding]::Unicode.GetString($bytes, $nameRva + 4, $nameLen)

                    $result.Modules += [pscustomobject]@{
                        Name          = $name
                        BaseOfImage   = [BitConverter]::ToUInt64($bytes, $mod)
                        SizeOfImage   = [BitConverter]::ToUInt32($bytes, $mod + 8)
                        TimeDateStamp = [BitConverter]::ToUInt32($bytes, $mod + 16)
                    }
                }
            }
            $STREAM_EXCEPTION {
                # MINIDUMP_EXCEPTION_STREAM: ThreadId(4) __alignment(4) then MINIDUMP_EXCEPTION.
                # Inside that: ExceptionCode(0) ExceptionFlags(4) ExceptionRecord(8)
                # ExceptionAddress(16) NumberParameters(24) __unusedAlignment(28)
                # ExceptionInformation[15](32). For an access violation Information[1] is the
                # address that could not be touched.
                $exc = $rva + 8
                $result.HasException = $true
                $result.ExceptionCode = [BitConverter]::ToUInt32($bytes, $exc)
                $result.FaultAddress  = [BitConverter]::ToUInt64($bytes, $exc + 32 + 8)

                # MINIDUMP_EXCEPTION is 152 bytes; MINIDUMP_LOCATION_DESCRIPTOR follows it
                $ctxRva = [BitConverter]::ToUInt32($bytes, $rva + 8 + 152 + 4)
                $result.Rip = [BitConverter]::ToUInt64($bytes, $ctxRva + $CTX_RIP)
                $result.Rsp = [BitConverter]::ToUInt64($bytes, $ctxRva + $CTX_RSP)

                $regNames = @("Rax","Rcx","Rdx","Rbx","Rsp","Rbp","Rsi","Rdi",
                              "R8","R9","R10","R11","R12","R13","R14","R15")
                for ($r = 0; $r -lt $regNames.Count; $r++) {
                    $result.Registers[$regNames[$r]] =
                        [BitConverter]::ToUInt64($bytes, $ctxRva + $CTX_RAX + ($r * 8))
                }
            }
        }
    }

    return [pscustomobject]$result
}

function Get-ZetModule {
    param($dump)
    $dump.Modules | Where-Object { $_.Name -match "ziti-edge-tunnel\.exe$" } | Select-Object -First 1
}

function Get-ModuleFor {
    param($dump, [uint64]$addr)
    $dump.Modules |
        Where-Object { $addr -ge $_.BaseOfImage -and $addr -lt ($_.BaseOfImage + $_.SizeOfImage) } |
        Select-Object -First 1
}

function Format-ModuleOffset {
    param($module, [uint64]$addr)
    if (-not $module) { return ("0x{0:x} (no module)" -f $addr) }
    $leaf = Split-Path -Leaf $module.Name
    return ("{0}+0x{1:x}" -f $leaf, ($addr - $module.BaseOfImage))
}

# release binaries are built in CI, so every DWARF path is prefixed with the runner's checkout
# directory. Nobody needs to read that.
function Format-Location {
    param([string]$location)
    if (-not $location) { return "" }
    foreach ($marker in @("ziti-sdk-c-src/", "ziti-tunnel-sdk-c/", "_deps/")) {
        $idx = $location.LastIndexOf($marker)
        if ($idx -ge 0) { return $location.Substring($idx + $marker.Length) }
    }
    return $location
}

# ------------------------------------------------------------- PE header reads

function Read-PeInfo {
    param([string]$exePath)

    $fs = [System.IO.File]::OpenRead($exePath)
    try {
        $head = New-Object byte[] 1024
        [void]$fs.Read($head, 0, 1024)
    } finally {
        $fs.Dispose()
    }

    $peOffset = [BitConverter]::ToUInt32($head, 0x3C)
    $opt = $peOffset + 24   # skip PE signature (4) + COFF header (20)

    [pscustomobject]@{
        TimeDateStamp = [BitConverter]::ToUInt32($head, $peOffset + 8)
        ImageBase     = [BitConverter]::ToUInt64($head, $opt + 24)
        SizeOfImage   = [BitConverter]::ToUInt32($head, $opt + 56)
    }
}

# ------------------------------------------------------------ bundle inspection

function Find-Dumps {
    param([string]$root)

    if (Test-Path -PathType Leaf $root) {
        if ($root -match "\.dmp$") { return @($root) }
        if ($root -match "\.zip$") {
            $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("zet-dump-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
            Write-Host "unpacking $root"
            Expand-Archive -Path $root -DestinationPath $dest -Force
            return @(Get-ChildItem -Path $dest -Recurse -Filter "*.dmp" | ForEach-Object { $_.FullName })
        }
        throw "not a dump or a zip: $root"
    }

    Get-ChildItem -Path $root -Recurse -Filter "*.dmp" | ForEach-Object { $_.FullName }
}

# One bundle is one machine. A fleet ticket holds many side by side, so the search for a version has
# to stop at the bundle it started in - reading a neighbour's .ziti hands back the wrong build for
# the wrong machine, and the binary check downstream would only catch that by luck.
function Get-BundleRoot {
    param([string]$dumpPath)

    $dir = Get-Item (Split-Path -Parent $dumpPath)
    for ($i = 0; $i -lt 3 -and $dir; $i++) {
        if (Test-Path (Join-Path $dir.FullName "service")) { return $dir.FullName }
        $dir = $dir.Parent
    }
    return (Split-Path -Parent $dumpPath)
}

function Get-BundleVersion {
    param([string]$dumpPath)

    $root = Get-BundleRoot -dumpPath $dumpPath
    $service = Join-Path $root "service"
    $searchIn = if (Test-Path $service) { $service } else { $root }

    # the .ziti dump records "Application: <name>@<version>". That version is
    # ziti_tunneler_version() -- the ZET version -- even though the name is ZDEW's display name.
    $ziti = Get-ChildItem -Path $searchIn -Filter "*.ziti" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($ziti) {
        $header = Get-Content -Path $ziti.FullName -TotalCount 20
        $line = $header | Where-Object { $_ -match "^Application:" } | Select-Object -First 1
        if ($line -match "@(v?[\d.]+)") {
            $v = $Matches[1]
            if ($v -notmatch "^v") { $v = "v$v" }

            # tlsuv names its TLS backend, which is what separates the two Windows builds
            $tls = $header | Where-Object { $_ -match "^tlsuv:" } | Select-Object -First 1
            $flavor = if ($tls -match "OpenSSL") { "" } else { "-win32crypto" }

            return [pscustomobject]@{ Version = $v; Flavor = $flavor; Source = $ziti.FullName }
        }
    }

    # no .ziti, or a stale one with no header: the tunneler logs the same version at startup
    $log = Get-ChildItem -Path $searchIn -Filter "ziti-tunneler.log*" -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($log) {
        $hit = Select-String -Path $log.FullName -Pattern "Ziti Tunneler SDK \((v[\d.]+)\)" |
               Select-Object -First 1
        if ($hit) {
            return [pscustomobject]@{
                Version = $hit.Matches[0].Groups[1].Value
                Flavor  = ""
                Source  = $log.FullName
            }
        }
    }

    return $null
}

# --------------------------------------------------------------- binary supply

function Get-ZetExe {
    param([string]$ver, [string]$flavor)

    $tag = if ($ver -match "^v") { $ver } else { "v$ver" }
    $asset = "ziti-edge-tunnel-Windows_x86_64$flavor.zip"
    $dest = Join-Path $cache "$tag$flavor"
    $exe = Join-Path $dest "ziti-edge-tunnel.exe"

    if (Test-Path $exe) {
        Write-Host "using cached $tag$flavor"
        return $exe
    }

    if (-not (Test-Path $cache)) { New-Item -ItemType Directory -Path $cache | Out-Null }
    $zip = Join-Path $cache "$tag$flavor.zip"
    $url = "https://github.com/$repo/releases/download/$tag/$asset"

    Write-Host "downloading $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $zip
    } catch {
        throw "could not download $asset for $tag - $($_.Exception.Message)"
    }

    Expand-Archive -Path $zip -DestinationPath $dest -Force
    if (-not (Test-Path $exe)) {
        $found = Get-ChildItem -Path $dest -Recurse -Filter "ziti-edge-tunnel.exe" | Select-Object -First 1
        if (-not $found) { throw "no ziti-edge-tunnel.exe inside $asset" }
        $exe = $found.FullName
    }
    return $exe
}

# ------------------------------------------------------------- tool discovery

function Find-Tool {
    param([string]$name, [string[]]$candidates)

    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    foreach ($c in $candidates) {
        if ($c -match "\*") {
            $hit = Get-ChildItem -Path $c -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        } elseif (Test-Path $c) {
            return $c
        }
    }
    return $null
}

function Find-Cdb {
    Find-Tool -name "cdb" -candidates @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64\cdb.exe",
        "$env:ProgramFiles\Windows Kits\10\Debuggers\x64\cdb.exe",
        "$env:LOCALAPPDATA\Microsoft\WindowsApps\cdb.exe",
        "$env:LOCALAPPDATA\DBG\UI\*\amd64\cdb.exe"
    )
}

function Find-Addr2line {
    Find-Tool -name "addr2line" -candidates @(
        "$env:ProgramFiles\JetBrains\*\bin\mingw\bin\addr2line.exe",
        "C:\msys64\mingw64\bin\addr2line.exe",
        "D:\tools\msys64\mingw64\bin\addr2line.exe",
        "C:\ProgramData\chocolatey\bin\addr2line.exe",
        "$env:ProgramFiles\Git\usr\bin\addr2line.exe"
    )
}

# ------------------------------------------------------------------ unwinding

function Invoke-Cdb {
    param([string]$cdb, [string]$dumpPath, [string]$exeFolder, [string[]]$commands)

    $script = Join-Path ([System.IO.Path]::GetTempPath()) ("zet-cdb-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".txt")
    $lines = @()
    if ($exeFolder) { $lines += ".exepath+ $exeFolder"; $lines += ".reload /f" }
    $lines += $commands
    $lines += "q"
    $lines | Set-Content -Path $script -Encoding ASCII

    $out = & $cdb -z $dumpPath -cf $script 2>&1 | Out-String
    Remove-Item $script -ErrorAction SilentlyContinue
    return $out
}

# kb prints the return address of each frame in its first column. Frame 0 of a crash is the faulting
# instruction itself, which comes from .ecxr rather than from this column.
function Read-KbAddresses {
    param([string]$text)

    $addresses = @()
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match "^([0-9a-f]{8}``?[0-9a-f]{8})\s+:") {
            $hex = $Matches[1] -replace "``", ""
            $addr = [Convert]::ToUInt64($hex, 16)
            if ($addr -ne 0) { $addresses += $addr }
        }
    }
    return $addresses
}

function Get-CrashFrames {
    param([string]$cdb, [string]$dumpPath, [string]$exeFolder)
    $out = Invoke-Cdb -cdb $cdb -dumpPath $dumpPath -exeFolder $exeFolder -commands @(".ecxr", "kb 100")
    return Read-KbAddresses -text $out
}

# A stall dump has no exception record, because nothing faulted - the monitor service took it because
# the tunneler stopped responding. The question is what every thread is blocked on, so walk them all.
function Get-ThreadStacks {
    param([string]$cdb, [string]$dumpPath, [string]$exeFolder)

    $out = Invoke-Cdb -cdb $cdb -dumpPath $dumpPath -exeFolder $exeFolder -commands @("~*kb 40")

    $threads = @()
    $current = $null
    foreach ($line in ($out -split "`r?`n")) {
        if ($line -match "^\s*(\d+)\s+Id:\s+\S+\s+Suspend:") {
            if ($current) { $threads += $current }
            $current = [pscustomobject]@{ Id = [int]$Matches[1]; Addresses = @() }
            continue
        }
        if ($current -and $line -match "^([0-9a-f]{8}``?[0-9a-f]{8})\s+:") {
            $hex = $Matches[1] -replace "``", ""
            $addr = [Convert]::ToUInt64($hex, 16)
            if ($addr -ne 0) { $current.Addresses += $addr }
        }
    }
    if ($current) { $threads += $current }
    return $threads
}

# ---------------------------------------------------------------- symbolizing

function Resolve-Frames {
    param(
        $dump,
        [string]$addr2line,
        [string]$exePath,
        [uint64]$preferredBase,
        [uint64[]]$addresses
    )

    $zet = Get-ZetModule -dump $dump
    $rows = @()
    $index = 0
    $orphans = 0

    foreach ($addr in $addresses) {
        $module = Get-ModuleFor -dump $dump -addr $addr

        # With no unwind data the debugger falls back to scanning raw stack memory, which yields
        # runs of values that belong to no module at all - often decodable as ASCII. A few in a row
        # means the walk has left the real stack, so stop rather than print pages of noise.
        if (-not $module) {
            $orphans++
            if ($orphans -ge 3) {
                $rows += [pscustomobject]@{
                    Frame = $index; Address = ""; Function = "... walk abandoned (stack scan, not real frames)"; Location = ""
                }
                break
            }
        } else {
            $orphans = 0
        }

        $inZet = ($module -and $zet -and $module.Name -eq $zet.Name)

        $row = [ordered]@{
            Frame    = $index
            Address  = ("0x{0:x}" -f $addr)
            Function = (Format-ModuleOffset -module $module -addr $addr)
            Location = ""
        }

        if ($inZet -and $addr2line -and $exePath) {
            # rebase onto the file's preferred base so the DWARF in the exe applies. Every frame
            # past the first is a return address, so step back one byte to land in the call rather
            # than the instruction after it.
            $static = $preferredBase + $addr - $zet.BaseOfImage
            if ($index -gt 0) { $static -= 1 }

            $res = & $addr2line -e $exePath -f -C -i -p ("0x{0:x}" -f $static) 2>&1 | Select-Object -First 1
            if ($res -match "^(.+?) at (.+)$") {
                $fn = $Matches[1]
                if ($fn -ne "??") {
                    $row.Function = $fn
                    $row.Location = Format-Location -location $Matches[2]
                }
            }
        }

        $rows += [pscustomobject]$row
        $index++
    }
    return $rows
}

function Write-Frames {
    param($rows)
    Write-Host ""
    Write-Host ("{0,-5} {1,-18} {2,-34} {3}" -f "FRAME", "ADDRESS", "FUNCTION", "SOURCE")
    foreach ($r in $rows) {
        Write-Host ("{0,-5} {1,-18} {2,-34} {3}" -f $r.Frame, $r.Address, $r.Function, $r.Location)
    }
}

# --------------------------------------------------------------------- report

function Show-Dump {
    param([string]$dumpPath)

    Write-Host ""
    Write-Host ("=" * 100)
    Write-Host "dump    : $dumpPath"

    $info = Get-Item $dumpPath
    Write-Host ("size    : {0:N0} bytes" -f $info.Length)
    Write-Host ("written : {0:u}" -f $info.LastWriteTimeUtc)

    if ($info.Length -eq 0) {
        Write-Host "MiniDumpWriteDump failed outright - a 0-byte dump is a lost diagnostic, not a crash."
        return
    }

    $dump = Read-Minidump -dumpPath $dumpPath
    $zet = Get-ZetModule -dump $dump

    if ($dump.HasException) {
        $ripModule = Get-ModuleFor -dump $dump -addr $dump.Rip
        Write-Host ("exception: 0x{0:x8}   faulting address: 0x{1:x}" -f $dump.ExceptionCode, $dump.FaultAddress)
        Write-Host ("rip      : 0x{0:x}   {1}" -f $dump.Rip, (Format-ModuleOffset -module $ripModule -addr $dump.Rip))

        if ($dump.FaultAddress -eq 0) {
            Write-Host "note     : NULL dereference"
        } elseif ($dump.FaultAddress -eq [uint64]::MaxValue) {
            Write-Host "note     : address unreportable - suspect a garbage pointer, check for non-canonical registers"
        }
    } else {
        Write-Host "kind     : no exception record - this is a stall snapshot, not a crash"
    }

    # the first module in the list is the process image, which is not always the process the
    # filename claims - the monitor service has been seen dumping the UI instead of the tunneler
    if ($dump.Modules.Count -gt 0) {
        $image = Split-Path -Leaf $dump.Modules[0].Name
        Write-Host "process  : $image"
        if ($image -ne "ziti-edge-tunnel.exe") {
            Write-Host "WARNING  : this dump is of $image, not ziti-edge-tunnel.exe, whatever the filename says"
        }
    }

    if (-not $zet) {
        Write-Host "note     : no ziti-edge-tunnel.exe among the $($dump.Modules.Count) recorded modules"
    }

    # find the binary
    $exe = $null
    if ($exeDir) {
        $exe = Join-Path $exeDir "ziti-edge-tunnel.exe"
        if (-not (Test-Path $exe)) { throw "no ziti-edge-tunnel.exe in $exeDir" }
    } else {
        $ver = $version
        $flavor = ""
        if (-not $ver) {
            $found = Get-BundleVersion -dumpPath $dumpPath
            if ($found) {
                $ver = $found.Version
                $flavor = $found.Flavor
                Write-Host "version  : $ver$flavor (from $($found.Source))"
            }
        }
        if (-not $ver) {
            Write-Host "version  : unknown - pass -version or -exeDir to symbolize"
        } else {
            $exe = Get-ZetExe -ver $ver -flavor $flavor
        }
    }

    $preferredBase = 0
    if ($exe -and -not $zet) {
        $pe = Read-PeInfo -exePath $exe
        $preferredBase = $pe.ImageBase
        Write-Host "binary   : $exe (cannot be verified - the dump records no ziti-edge-tunnel module)"
    } elseif ($exe) {
        $pe = Read-PeInfo -exePath $exe
        $preferredBase = $pe.ImageBase
        $matched = ($pe.TimeDateStamp -eq $zet.TimeDateStamp -and $pe.SizeOfImage -eq $zet.SizeOfImage)
        if ($matched) {
            Write-Host "binary   : $exe (verified against the dump)"
        } else {
            Write-Host "binary   : $exe"
            Write-Host ("WARNING  : does not match the dump (exe stamp 0x{0:x}/size {1}, dump wants 0x{2:x}/size {3})" -f `
                        $pe.TimeDateStamp, $pe.SizeOfImage, $zet.TimeDateStamp, $zet.SizeOfImage)
            if (-not $force) {
                Write-Host "           symbolizing anyway would give wrong lines. Re-run with -force to override."
                $exe = $null
            }
        }
    }

    # unwind
    $cdb = Find-Cdb
    $addr2line = Find-Addr2line
    Write-Host ("tools    : cdb={0} addr2line={1}" -f `
                $(if ($cdb) { $cdb } else { "not found" }),
                $(if ($addr2line) { $addr2line } else { "not found" }))

    if (-not $cdb) {
        Write-Host ""
        Write-Host "No debugger found, so there is no stack - only the faulting frame above."
        Write-Host "Install the Windows SDK Debugging Tools to get the full backtrace."
        if ($dump.HasException -and $exe -and $addr2line) {
            Write-Frames -rows (Resolve-Frames -dump $dump -addr2line $addr2line -exePath $exe `
                                               -preferredBase $preferredBase -addresses @($dump.Rip))
        }
        return
    }

    $exeFolder = if ($exe) { Split-Path -Parent $exe } else { "" }

    if (-not $dump.HasException) {
        $threads = Get-ThreadStacks -cdb $cdb -dumpPath $dumpPath -exeFolder $exeFolder
        $interesting = $threads | Where-Object { $_.Addresses.Count -gt 0 }
        if (-not $interesting) {
            Write-Host ""
            Write-Host "No thread stacks could be walked."
            return
        }
        foreach ($t in $interesting) {
            Write-Host ""
            Write-Host ("--- thread {0} ---" -f $t.Id)
            Write-Frames -rows (Resolve-Frames -dump $dump -addr2line $addr2line -exePath $exe `
                                               -preferredBase $preferredBase -addresses $t.Addresses)
        }
        return
    }

    $returns = Get-CrashFrames -cdb $cdb -dumpPath $dumpPath -exeFolder $exeFolder
    $addresses = @($dump.Rip) + $returns
    if ($addresses.Count -le 1) {
        Write-Host ""
        Write-Host "The stack did not unwind. Either the dump predates MiniDumpWithModuleHeaders and the"
        Write-Host "binary above is not the one that produced it, or the stack is too damaged to walk."
        return
    }

    Write-Frames -rows (Resolve-Frames -dump $dump -addr2line $addr2line -exePath $exe `
                                       -preferredBase $preferredBase -addresses $addresses)
}

# ----------------------------------------------------------------------- main

if (-not (Test-Path $path)) {
    Write-Host "no such path: $path"
    exit 1
}

$dumps = @(Find-Dumps -root $path)
if ($dumps.Count -eq 0) {
    Write-Host "no .dmp files under $path"
    exit 1
}

Write-Host ("found {0} dump(s)" -f $dumps.Count)
foreach ($d in $dumps) {
    try {
        Show-Dump -dumpPath $d
    } catch {
        Write-Host "failed on ${d}: $($_.Exception.Message)"
    }
}
