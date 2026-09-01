<#
.SYNOPSIS
Turn a ziti-edge-tunnel crash dump into a readable stack.

.DESCRIPTION
Point this at a .dmp, at an unpacked feedback bundle, or at a feedback zip, and it prints the
faulting instruction and a symbolized backtrace.

It works out which ziti-edge-tunnel build produced the dump, downloads that exact binary from the
ziti-tunnel-sdk-c releases, and proves the download matches before trusting it. Nothing is staged by
hand.

The version comes from the bundle around the dump: the .ziti dump's "Application: name@version"
line, whose tlsuv entry also says which of the two Windows builds is installed, and failing that the
tunneler's startup banner. When there is no bundle, or when the dump is older than the version the
bundle reports, the dump's own module record names the build that wrote it and the matching release
is hunted down from that. All of this happens without being asked.

Give it any of these and it works out the rest:

  a .dmp                one dump
  a feedback .zip       unpacked to temp, every dump inside it read
  any folder            searched all the way down, so a whole ticket works

More than one dump gets a comparison table at the end. That table is the point on a fleet ticket:
one dump tells you where it broke, the set tells you whether they all broke the same way.

Dumps written before the MiniDumpWithModuleHeaders fix carry no unwind data of their own, so the
matching binary is what makes them readable at all. Dumps written after it still need the binary for
function names, because MinGW keeps debug info as DWARF inside the exe and no PDB is ever published.

Whatever is missing, the script degrades instead of failing: no debugger still gets you the faulting
address and instruction pointer, no addr2line still gets you ordered frames as module offsets.

TWO PREREQUISITES, both free, both one-time:

  cdb          Debugging Tools for Windows, from the Windows SDK installer. Untick everything except
               "Debugging Tools for Windows". Without it there is no backtrace at all.
               https://developer.microsoft.com/windows/downloads/windows-sdk/

  addr2line    MSYS2, then: pacman -S --needed mingw-w64-x86_64-binutils
               Without it the stack appears as raw module offsets instead of function names.
               https://www.msys2.org/

Take the default install locations and this script finds both by itself. Run it with -checkTools to
see what you have and what you are missing.

.PARAMETER path
A .dmp file, a feedback zip, or any folder. Folders are searched all the way down for every .dmp,
so a single machine's bundle and a whole ticket of them both work. If a folder holds only zips and
no dumps, the zips are unpacked and searched instead.

.PARAMETER checkTools
Report which prerequisites are installed and how to get the missing ones, then exit. Run this once
before your first real dump.

.PARAMETER addr2lineDir
Directory holding addr2line.exe, when it is somewhere this script does not look by itself. Taken as
final: if it is not there, addr2line is reported missing rather than searched for elsewhere. Point
this at an empty directory to see what the run looks like without it.

.PARAMETER cdbDir
Directory holding cdb.exe. Same rules as -addr2lineDir.

.PARAMETER version
ZET version to symbolize against, e.g. v1.11.4. Read from the bundle when not given.

.PARAMETER exeDir
Directory holding a ziti-edge-tunnel.exe to use as-is. Skips the download.

.PARAMETER cache
Where downloaded builds are kept. Defaults to %LOCALAPPDATA%\zet-symbols.

.PARAMETER force
Symbolize even when the binary does not match the dump. The output is then unreliable and is marked
as such.

.PARAMETER searchLimit
When the version read from the bundle does not match the dump, the matching build is hunted down by
trying releases until one verifies. This happens on its own; the limit caps how many are tried before
giving up. Defaults to 8.

.EXAMPLE
.\read-crash-dump.ps1 C:\temp\support\12345

Every dump on the whole ticket, every machine, plus the comparison table.

.EXAMPLE
.\read-crash-dump.ps1 C:\temp\support\12345\host-01

.EXAMPLE
.\read-crash-dump.ps1 .\ziti-edge-tunnel.crash.dmp
#>

param (
    [Parameter(Position = 0)][string]$path,
    [switch]$checkTools,
    [string]$addr2lineDir,
    [string]$cdbDir,
    [string]$version,
    [string]$exeDir,
    [string]$cache = (Join-Path $env:LOCALAPPDATA "zet-symbols"),
    [switch]$force,
    [int]$searchLimit = 8
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

# Three shapes of input, so that nobody has to know which one they have: a single .dmp, a feedback
# zip, or any folder at all. Folders are always searched all the way down, because a ticket folder
# holds one folder per machine and the dumps sit two levels inside each - asking for a flag to reach
# them would only ever be answered yes.
function Find-Dumps {
    param([string]$root)

    if (Test-Path -PathType Leaf $root) {
        if ($root -match "\.dmp$") { return @($root) }
        if ($root -match "\.zip$") {
            $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("zet-dump-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
            Write-Host "unpacking $root"
            Expand-Archive -Path $root -DestinationPath $dest -Force
            return @(Get-ChildItem -Path $dest -Recurse -Filter "*.dmp" |
                     Sort-Object FullName | ForEach-Object { $_.FullName })
        }
        Write-Host "That file is not a crash dump or a feedback zip: $root"
        Write-Host "Give me a .dmp, a feedback .zip, or the folder holding them."
        return @()
    }

    # A ticket folder may still hold the zips it was extracted from. The extracted copies are the
    # same dumps, so unpacking them again would report every dump twice.
    $found = @(Get-ChildItem -Path $root -Recurse -Filter "*.dmp" -ErrorAction SilentlyContinue |
               Sort-Object FullName | ForEach-Object { $_.FullName })
    if ($found.Count -eq 0) {
        $zips = @(Get-ChildItem -Path $root -Recurse -Filter "*.zip" -ErrorAction SilentlyContinue)
        if ($zips.Count -gt 0) {
            Write-Host ("no dumps here yet, but {0} zip(s) are - unpacking them" -f $zips.Count)
            foreach ($z in $zips) { $found += Find-Dumps -root $z.FullName }
        }
    }
    return $found
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

# A dump's timestamp is the one number in the file that is not self-describing. Zip entries store a
# bare wall clock with no zone, so extracting a customer's bundle stamps the file with THEIR local
# time, which the filesystem then hands back as if it were OURS. Converting that to UTC applies the
# analyst's offset to the customer's clock and is wrong by the difference between the two.
#
# The tunneler prints both clocks in one line at startup, so the machine that wrote the dump also
# tells us its offset. Use that, and never the local machine's.
function Get-BundleUtcOffset {
    param([string]$dumpPath, [datetime]$wall)

    $root = Get-BundleRoot -dumpPath $dumpPath
    $service = Join-Path $root "service"
    $searchIn = if (Test-Path $service) { $service } else { $root }

    # Best source: the tunneler printed both clocks itself. Only exists if it restarted during the
    # window, which on a healthy machine it never does.
    #
    # Every banner is collected rather than the first one found, because a machine in a zone that
    # observes DST has a different offset either side of the changeover, and these dumps are often
    # months older than the logs around them. The banner nearest the dump is the one that was true
    # when the dump was written.
    $banners = @()
    $logs = Get-ChildItem -Path $searchIn -Filter "ziti-tunneler.log*" -ErrorAction SilentlyContinue
    foreach ($log in $logs) {
        $hits = Select-String -Path $log.FullName `
                    -Pattern "initialized at\s*:.*?(\d{2}):(\d{2}):(\d{2}).*?\(local time\).*?(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\s*\(UTC\)"
        foreach ($hit in $hits) {
            $g = $hit.Matches[0].Groups
            $local = ([int]$g[1].Value * 3600) + ([int]$g[2].Value * 60) + [int]$g[3].Value
            $utc   = ([int]$g[7].Value * 3600) + ([int]$g[8].Value * 60) + [int]$g[9].Value
            $diff  = $local - $utc
            if ($diff -gt 43200)  { $diff -= 86400 }
            if ($diff -lt -43200) { $diff += 86400 }
            $when = New-Object DateTime ([int]$g[4].Value), ([int]$g[5].Value), ([int]$g[6].Value), `
                                        ([int]$g[7].Value), ([int]$g[8].Value), ([int]$g[9].Value)
            $banners += [pscustomobject]@{
                Offset = [TimeSpan]::FromSeconds($diff)
                When   = $when
                Log    = $log.Name
            }
        }
    }

    if ($banners.Count -gt 0) {
        $best = $banners | Sort-Object { [Math]::Abs((New-TimeSpan -Start $_.When -End $wall).Ticks) } |
                Select-Object -First 1
        $spread = @($banners | Select-Object -ExpandProperty Offset -Unique)
        $note = if ($spread.Count -gt 1) { ", offset varies across the window so the nearest was used" } else { "" }
        return [pscustomobject]@{
            Offset = $best.Offset
            Source = ("startup banner of {0:yyyy-MM-dd} in {1}{2}" -f $best.When, $best.Log, $note)
        }
    }

    # Fallback: systeminfo.txt is captured on every bundle and names the zone outright. This is the
    # standard-time offset, so a dump written under DST reads an hour off - which is still far closer
    # than applying our own offset, and South Africa and most affected fleets do not observe DST.
    $root2 = Get-BundleRoot -dumpPath $dumpPath
    $sysinfo = Join-Path $root2 "systeminfo.txt"
    if (Test-Path $sysinfo) {
        $hit = Select-String -Path $sysinfo -List -Pattern "Time Zone:\s*\(UTC([+-])(\d{2}):(\d{2})\)"
        if ($hit) {
            $g = $hit.Matches[0].Groups
            $secs = ([int]$g[2].Value * 3600) + ([int]$g[3].Value * 60)
            if ($g[1].Value -eq "-") { $secs = -$secs }
            return [pscustomobject]@{
                Offset = [TimeSpan]::FromSeconds($secs)
                Source = "Time Zone in systeminfo.txt"
            }
        }
    }

    return $null
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

# The dump names the build that wrote it: the module list carries ziti-edge-tunnel.exe's
# TimeDateStamp and SizeOfImage, and those two identify a release exactly. So when the version read
# out of the bundle turns out to be the wrong one - which happens whenever a dump is older than the
# install, since these filenames are fixed and get overwritten - the dump can still be matched by
# trying releases until one verifies. Every candidate is proven against the dump before use, so this
# cannot silently symbolize against the wrong binary.
# Ordered for the case that actually happens. A dump that does not match the installed version is
# almost always OLDER than it - the file is overwritten by each event, so the one sitting in a bundle
# can predate the current install by months. Walking the newest releases first therefore searches
# away from the answer; this walks outward from the installed version, older side first.
function Get-ReleaseTags {
    param([string]$near)

    $url = "https://api.github.com/repos/$repo/releases?per_page=100"
    try {
        $releases = Invoke-RestMethod -Uri $url -Headers @{ "User-Agent" = "read-crash-dump" }
    } catch {
        Write-Host "           could not list releases - $($_.Exception.Message)"
        return @()
    }
    $tags = @($releases | Where-Object { -not $_.draft } | ForEach-Object { $_.tag_name })
    if (-not $near) { return $tags }

    $asVersion = {
        param($t)
        try { return [version]($t -replace "^v", "") } catch { return $null }
    }
    $pivot = & $asVersion $near
    if (-not $pivot) { return $tags }

    $older = @(); $newer = @()
    foreach ($t in $tags) {
        $v = & $asVersion $t
        if (-not $v) { continue }
        if ($v -lt $pivot) { $older += $t } elseif ($v -gt $pivot) { $newer += $t }
    }
    # older descending (nearest below the install first), then newer ascending
    return @($older | Sort-Object { & $asVersion $_ } -Descending) +
           @($newer | Sort-Object { & $asVersion $_ })
}

function Test-ExeMatchesDump {
    param([string]$exePath, $zetModule)
    if (-not (Test-Path $exePath) -or -not $zetModule) { return $false }
    $pe = Read-PeInfo -exePath $exePath
    return ($pe.TimeDateStamp -eq $zetModule.TimeDateStamp -and $pe.SizeOfImage -eq $zetModule.SizeOfImage)
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

# An explicit -cdbDir / -addr2lineDir wins outright and does not fall back to the search. Otherwise
# naming a directory that turns out to be wrong silently gets you a different tool than the one you
# asked for, and there is no way to make the script report a tool as missing when it is installed.
function Find-Cdb {
    if ($cdbDir) {
        $explicit = Join-Path $cdbDir "cdb.exe"
        if (Test-Path $explicit) { return $explicit }
        return $null
    }

    Find-Tool -name "cdb" -candidates @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64\cdb.exe",
        "$env:ProgramFiles\Windows Kits\10\Debuggers\x64\cdb.exe",
        "$env:LOCALAPPDATA\Microsoft\WindowsApps\cdb.exe",
        "$env:LOCALAPPDATA\DBG\UI\*\amd64\cdb.exe"
    )
}

# The mingw64 build is checked ahead of anything on PATH. Several addr2line builds exist on a typical
# dev box and only the x86_64-w64-mingw32 one reads the DWARF that ZET's release binaries carry -- the
# MSYS build under /usr/bin targets MSYS itself and is the one PATH usually finds first.
function Find-Addr2line {
    if ($addr2lineDir) {
        $explicit = Join-Path $addr2lineDir "addr2line.exe"
        if (Test-Path $explicit) { return $explicit }
        return $null
    }

    $mingw = @(
        "C:\msys64\mingw64\bin\addr2line.exe",
        "D:\msys64\mingw64\bin\addr2line.exe",
        "D:\tools\msys64\mingw64\bin\addr2line.exe",
        "$env:ProgramFiles\JetBrains\*\bin\mingw\bin\addr2line.exe"
    )
    foreach ($c in $mingw) {
        if ($c -match "\*") {
            $hit = Get-ChildItem -Path $c -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        } elseif (Test-Path $c) {
            return $c
        }
    }

    Find-Tool -name "addr2line" -candidates @(
        "C:\msys64\usr\bin\addr2line.exe",
        "D:\tools\msys64\usr\bin\addr2line.exe",
        "C:\ProgramData\chocolatey\bin\addr2line.exe",
        "$env:ProgramFiles\Git\usr\bin\addr2line.exe"
    )
}

# ---------------------------------------------------------------- prerequisites

function Test-Prerequisites {
    $cdb = Find-Cdb
    $addr2line = Find-Addr2line

    Write-Host ""
    Write-Host "prerequisite check"
    Write-Host ""

    if ($cdb) {
        Write-Host "  [ok]      cdb          $cdb"
    } else {
        Write-Host "  [MISSING] cdb          no debugger found"
    }
    if ($addr2line) {
        Write-Host "  [ok]      addr2line    $addr2line"
    } else {
        Write-Host "  [MISSING] addr2line    no symbolizer found"
    }

    if ($cdb -and $addr2line) {
        Write-Host ""
        Write-Host "  All set - you will get full stacks with function names and source lines."
        return $true
    }

    Write-Host ""
    if (-not $cdb) {
        Write-Host "  cdb is the debugger that walks the stack. Without it you get the faulting address"
        Write-Host "  and nothing else - no backtrace at all."
        Write-Host ""
        Write-Host "  Install: Debugging Tools for Windows, part of the Windows SDK."
        Write-Host "    1. https://developer.microsoft.com/windows/downloads/windows-sdk/"
        Write-Host "    2. Run the installer. Untick everything EXCEPT 'Debugging Tools for Windows'."
        Write-Host "    3. It lands in C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\ and this"
        Write-Host "       script finds it there by itself. No PATH change needed."
        Write-Host ""
    }
    if (-not $addr2line) {
        Write-Host "  addr2line turns addresses into function names and source lines. Without it the"
        Write-Host "  stack still comes out, but every frame reads as ziti-edge-tunnel.exe+0x4cb70c."
        Write-Host ""
        Write-Host "  Install: MSYS2, then the mingw-w64 binutils package."
        Write-Host "    1. https://www.msys2.org/  -- take the default install location, C:\msys64"
        Write-Host "    2. Open 'MSYS2 MINGW64' from the Start menu and run:"
        Write-Host "         pacman -S --needed mingw-w64-x86_64-binutils"
        Write-Host "    3. That puts addr2line in C:\msys64\mingw64\bin and this script finds it there."
        Write-Host ""
        Write-Host "  If you already have MSYS2 somewhere else, the mingw64\bin\addr2line.exe under it is"
        Write-Host "  the one to use. Pass -addr2lineDir if it is not in one of the usual places."
        Write-Host ""
    }
    return $false
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

# ------------------------------------------------------------------ registers

# x86-64 only implements 48 address bits, so a valid user pointer has bits 63-48 all zero. A value
# that fails this can never have been a live address, which separates "freed and reused" from "never
# set" - the two look identical once the fault address comes back unreportable.
function Test-NonCanonical {
    param([uint64]$v)
    if ($v -lt 0x10000) { return $false }   # small integers are not pointers
    $hi = $v -shr 48
    return -not ($hi -eq 0 -or $hi -eq 0xFFFF)
}

# Registers frequently still hold the string a failed str* call was walking. The shape of that
# string usually names which field was bad.
function Get-AsciiFragment {
    param([uint64]$v)
    $bytes = [BitConverter]::GetBytes($v)
    $text = ""
    foreach ($b in $bytes) {
        if ($b -eq 0) { break }
        if ($b -lt 0x20 -or $b -gt 0x7e) { return $null }
        $text += [char]$b
    }
    if ($text.Length -lt 4) { return $null }
    return $text
}

function Write-Registers {
    param($dump)

    Write-Host ""
    Write-Host "registers:"
    $names = @($dump.Registers.Keys)
    for ($i = 0; $i -lt $names.Count; $i += 4) {
        $cells = @()
        for ($j = $i; $j -lt [Math]::Min($i + 4, $names.Count); $j++) {
            $cells += ("{0,-4} {1:x16}" -f $names[$j], $dump.Registers[$names[$j]])
        }
        Write-Host ("  " + ($cells -join "  "))
    }

    $flagged = @()
    foreach ($n in $names) {
        $v = $dump.Registers[$n]
        if (Test-NonCanonical -v $v) { $flagged += ("{0}=0x{1:x}" -f $n, $v) }
    }
    if ($flagged.Count -gt 0) {
        Write-Host ("  non-canonical (never a valid address, so a freed/garbage pointer): " + ($flagged -join ", "))
    }

    $strings = @()
    foreach ($n in $names) {
        $frag = Get-AsciiFragment -v $dump.Registers[$n]
        if ($frag) { $strings += ("{0}=""{1}""" -f $n, $frag) }
    }
    if ($strings.Count -gt 0) {
        Write-Host ("  ascii fragments (surviving str* arguments): " + ($strings -join ", "))
    }
}

# --------------------------------------------------------------------- report

function Show-Dump {
    param([string]$dumpPath)

    # Filled in as we learn things and read back by the caller, so that every early return still
    # contributes a row to the cross-dump table. On a fleet ticket that table is the actual finding:
    # one dump tells you where it broke, the set tells you whether they broke the same way.
    $script:lastSummary = [pscustomobject]@{
        Machine = (Split-Path -Leaf (Get-BundleRoot -dumpPath $dumpPath))
        Kind    = if ($dumpPath -match "stalled") { "stall" } else { "crash" }
        Written = ""
        Process = ""
        Fault   = ""
        Rip     = ""
        Top     = ""
        Via     = ""
    }

    Write-Host ""
    Write-Host ("=" * 100)
    Write-Host "dump    : $dumpPath"

    $info = Get-Item $dumpPath
    Write-Host ("size    : {0:N0} bytes" -f $info.Length)

    # LastWriteTime's wall clock is the source machine's local time (see Get-BundleUtcOffset).
    # LastWriteTimeUtc would re-stamp it with our offset, so it is never used here.
    $wall = $info.LastWriteTime
    $tz = Get-BundleUtcOffset -dumpPath $dumpPath -wall $wall
    $writtenUtc = $null
    if ($tz) {
        $writtenUtc = $wall - $tz.Offset
        # TimeSpan formatting has no positive/negative section syntax, so the sign is applied here.
        $sign = if ($tz.Offset.Ticks -lt 0) { "-" } else { "+" }
        $offText = "{0}{1:00}:{2:00}" -f $sign, [Math]::Abs($tz.Offset.Hours), [Math]::Abs($tz.Offset.Minutes)
        Write-Host ("written : {0:yyyy-MM-dd HH:mm:ss} UTC   (machine clock said {1:HH:mm:ss}, UTC{2} per {3})" -f `
                    $writtenUtc, $wall, $offText, $tz.Source)
    } else {
        Write-Host ("written : {0:yyyy-MM-dd HH:mm:ss} on the machine's own clock" -f $wall)
        Write-Host "          Nothing in this bundle says which timezone that is, so it is NOT UTC and"
        Write-Host "          cannot be lined up against log timestamps until you know the offset."
    }

    $script:lastSummary.Written = if ($writtenUtc) { "{0:yyyy-MM-dd HH:mm}Z" -f $writtenUtc } `
                                  else { "{0:yyyy-MM-dd HH:mm}?" -f $wall }

    if ($info.Length -eq 0) {
        Write-Host "MiniDumpWriteDump failed outright - a 0-byte dump is a lost diagnostic, not a crash."
        $script:lastSummary.Process = "(none)"
        $script:lastSummary.Top = "0-byte - write failed"
        return
    }

    $dump = Read-Minidump -dumpPath $dumpPath
    $zet = Get-ZetModule -dump $dump

    if ($dump.HasException) {
        $ripModule = Get-ModuleFor -dump $dump -addr $dump.Rip
        Write-Host ("exception: 0x{0:x8}   faulting address: 0x{1:x}" -f $dump.ExceptionCode, $dump.FaultAddress)
        Write-Host ("rip      : 0x{0:x}   {1}" -f $dump.Rip, (Format-ModuleOffset -module $ripModule -addr $dump.Rip))
        $script:lastSummary.Fault = ("0x{0:x}" -f $dump.FaultAddress)
        $script:lastSummary.Rip = (Format-ModuleOffset -module $ripModule -addr $dump.Rip)

        if ($dump.FaultAddress -eq 0) {
            Write-Host "note     : NULL dereference"
        } elseif ($dump.FaultAddress -eq [uint64]::MaxValue) {
            Write-Host "note     : address unreportable - the OS could not name the page, which points at a"
            Write-Host "           garbage pointer rather than a plain NULL. See the registers below."
        }
    } else {
        Write-Host "kind     : no exception record - this is a stall snapshot, not a crash"
    }

    # the first module in the list is the process image, which is not always the process the
    # filename claims - the monitor service has been seen dumping the UI instead of the tunneler
    if ($dump.Modules.Count -gt 0) {
        $image = Split-Path -Leaf $dump.Modules[0].Name
        Write-Host "process  : $image"
        $script:lastSummary.Process = $image
        if ($image -ne "ziti-edge-tunnel.exe") {
            Write-Host "WARNING  : this dump is of $image, not ziti-edge-tunnel.exe, whatever the filename says"
            $script:lastSummary.Top = "WRONG PROCESS ($image)"

            # Nothing downstream can help: there is no ziti code in this process, so the tunneler
            # binary would symbolize nothing and its threads are all CLR and Win32 waits. Printing
            # thirty of those stacks buries the one line that matters.
            Write-Host ""
            Write-Host "Not reading any further. The monitor service names every dump it writes"
            Write-Host "ziti-edge-tunnel.stalled.dmp regardless of which process it actually dumped, so this"
            Write-Host "is a snapshot of the tray UI and says nothing about the tunneler. The real stall"
            Write-Host "dump for this machine, if there ever was one, was overwritten by this file."
            return
        }
    }

    if (-not $zet) {
        Write-Host "note     : no ziti-edge-tunnel.exe among the $($dump.Modules.Count) recorded modules"
    }

    if ($dump.HasException -and ($dump.FaultAddress -eq 0 -or $dump.FaultAddress -eq [uint64]::MaxValue)) {
        Write-Registers -dump $dump
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

                # The bundle reports the version installed the day it was collected. These dump
                # filenames are fixed and get overwritten, so a dump can be months older than the
                # install that is being read here, and older than every surviving log.
                $oldestLog = Get-ChildItem -Path (Split-Path -Parent $found.Source) -Filter "ziti-tunneler.log*" `
                                 -ErrorAction SilentlyContinue |
                             Sort-Object LastWriteTime | Select-Object -First 1
                if ($oldestLog -and $wall -lt $oldestLog.LastWriteTime.Date) {
                    Write-Host ("           this dump predates the oldest surviving log ({0:yyyy-MM-dd}), so that version" -f $oldestLog.LastWriteTime)
                    Write-Host "           is the one installed now, not necessarily the one that wrote the dump"
                }
            }
        }
        if (-not $ver) {
            Write-Host "version  : unknown - no .ziti or startup banner beside this dump"
        } else {
            $exe = Get-ZetExe -ver $ver -flavor $flavor

            # The two Windows builds share a version, and the log fallback cannot tell them apart -
            # only the .ziti header names the TLS backend. So when the first pick does not verify,
            # try the twin before giving up; it costs one download and is right about half the time.
            if ($zet -and -not (Test-ExeMatchesDump -exePath $exe -zetModule $zet)) {
                $twin = if ($flavor) { "" } else { "-win32crypto" }
                Write-Host "           first pick does not match the dump, trying the $(if ($twin) { $twin } else { 'standard' }) build"
                try {
                    $alt = Get-ZetExe -ver $ver -flavor $twin
                    if (Test-ExeMatchesDump -exePath $alt -zetModule $zet) {
                        $exe = $alt; $flavor = $twin
                        Write-Host "           the $twin build matches"
                    } else {
                        Write-Host "           that one does not match either, so neither published $ver build wrote this dump"
                    }
                } catch {
                    Write-Host "           no $twin build published for $ver"
                }
            }
        }

        # Last resort: ask the dump which build it was. This runs by itself rather than behind a
        # flag, because the person holding a dump has no way to know a flag would have helped.
        # A size match with a stamp mismatch means a rebuild of the same version, which no release
        # will carry. Searching for it would download the whole release history and find nothing.
        $rebuild = $false
        if ($zet -and $exe -and (Test-Path $exe)) {
            $peNow = Read-PeInfo -exePath $exe
            $rebuild = ($peNow.SizeOfImage -eq $zet.SizeOfImage -and $peNow.TimeDateStamp -ne $zet.TimeDateStamp)
        }

        if (-not $rebuild -and $zet -and (-not $exe -or -not (Test-ExeMatchesDump -exePath $exe -zetModule $zet))) {
            Write-Host "           looking for the build that wrote this dump, this may take a minute"
            $tried = 0
            foreach ($tag in (Get-ReleaseTags -near $ver)) {
                if ($tried -ge $searchLimit) {
                    Write-Host "           gave up after $searchLimit releases - raise -searchLimit to keep looking"
                    break
                }
                $tried++
                foreach ($f in @("", "-win32crypto")) {
                    try { $candidate = Get-ZetExe -ver $tag -flavor $f } catch { continue }
                    if (Test-ExeMatchesDump -exePath $candidate -zetModule $zet) {
                        Write-Host "           matched $tag$f"
                        $exe = $candidate
                        break
                    }
                }
                if ($exe -and (Test-ExeMatchesDump -exePath $exe -zetModule $zet)) { break }
            }
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
            if ($pe.SizeOfImage -eq $zet.SizeOfImage) {
                $dumpBuilt = ([datetime]"1970-01-01Z").AddSeconds($zet.TimeDateStamp)
                $exeBuilt  = ([datetime]"1970-01-01Z").AddSeconds($pe.TimeDateStamp)
                Write-Host ("           same image size, different build date: the dump's binary was built {0:yyyy-MM-dd}," -f $dumpBuilt)
                Write-Host ("           the downloaded one {0:yyyy-MM-dd}. That points at one version compiled twice," -f $exeBuilt)
                Write-Host "           so the release history was not searched. If you need this stack, take"
                Write-Host "           ziti-edge-tunnel.exe off the machine itself and pass -exeDir."
            }
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
        Write-Host "No debugger found, so the stack cannot be walked. Everything known about the crash"
        Write-Host "is above, plus the single faulting frame below. Install the Windows SDK Debugging"
        Write-Host "Tools and re-run for the full backtrace."
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
            $script:lastSummary.Top = "stall - no stacks could be walked"
            return
        }
        $named = $null
        foreach ($t in $interesting) {
            Write-Host ""
            Write-Host ("--- thread {0} ---" -f $t.Id)
            $rows = Resolve-Frames -dump $dump -addr2line $addr2line -exePath $exe `
                                   -preferredBase $preferredBase -addresses $t.Addresses
            Write-Frames -rows $rows
            if (-not $named) { $named = $rows | Where-Object { $_.Location } | Select-Object -First 1 }
        }

        # A stall has no faulting frame, so the summary row would otherwise be blank for the one
        # kind of dump where "it walked at all" is the finding.
        $script:lastSummary.Top = if ($named) {
            "stall - {0} ({1})" -f $named.Function, $named.Location
        } elseif ($exe) {
            "stall - {0} threads, no ziti frames named" -f $interesting.Count
        } else {
            "stall - {0} threads, unsymbolized (no matching binary)" -f $interesting.Count
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

    $rows = Resolve-Frames -dump $dump -addr2line $addr2line -exePath $exe `
                           -preferredBase $preferredBase -addresses $addresses
    Write-Frames -rows $rows

    # The first frame with source attached is the top of OUR code - the ucrtbase frame above it is
    # only ever strcmp/strdup and says nothing about which call was wrong.
    #
    # The CALLER is kept too. The same faulting line reached from a timer and from a controller
    # response callback is not the same bug, and collapsing the two hides the one difference across
    # a fleet that says whether the trigger is time-based or traffic-based.
    $named = @($rows | Where-Object { $_.Location } | Select-Object -First 2)
    if ($named.Count -gt 0) {
        $script:lastSummary.Top = ("{0} ({1})" -f $named[0].Function, $named[0].Location)
        if ($named.Count -gt 1) {
            $script:lastSummary.Via = $named[1].Function
        }
    }
}

function Write-Summary {
    param($rows)

    Write-Host ""
    Write-Host ("=" * 100)
    Write-Host "SUMMARY"
    Write-Host ""
    $w = [Math]::Max(8, ($rows | ForEach-Object { $_.Machine.Length } | Measure-Object -Maximum).Maximum)
    $fmt = "{0,-$w} {1,-6} {2,-18} {3,-20} {4}"
    Write-Host ($fmt -f "MACHINE", "KIND", "WRITTEN", "FAULT ADDR", "TOP FRAME")
    foreach ($r in ($rows | Sort-Object Machine, Kind)) {
        $top = if ($r.Via) { "{0}  <- {1}" -f $r.Top, $r.Via } else { $r.Top }
        Write-Host ($fmt -f $r.Machine, $r.Kind, $r.Written, $r.Fault, $top)
    }

    # Distinct crash signatures. Claiming one shared cause across a fleet is only defensible once
    # every dump has been shown to fault the same way, and the exceptions are what show it. The
    # caller is part of the signature: same line, different caller, different bug to chase.
    $faults = $rows | Where-Object { $_.Fault } | Group-Object Fault, Top, Via
    if ($faults.Count -gt 1) {
        Write-Host ""
        Write-Host "$($faults.Count) distinct crash signatures - do not report these as one cause without checking:"
        foreach ($g in ($faults | Sort-Object Count -Descending)) {
            $parts = $g.Name -split ", "
            $via = if ($parts.Count -gt 2 -and $parts[2]) { " via " + $parts[2] } else { "" }
            Write-Host ("  {0,2}x  {1}  {2}{3}" -f $g.Count, $parts[0], $parts[1], $via)
        }
    }

    $bad = @($rows | Where-Object { $_.Top -match "^(WRONG PROCESS|0-byte|failed:)" })
    if ($bad.Count -gt 0) {
        Write-Host ""
        Write-Host ("{0} of {1} dumps carry no usable evidence:" -f $bad.Count, $rows.Count)
        foreach ($g in ($bad | Group-Object { ($_.Top -split "[(:]")[0].Trim() } | Sort-Object Count -Descending)) {
            Write-Host ("  {0,2}x  {1}" -f $g.Count, $g.Name)
        }
    }
}

# ----------------------------------------------------------------------- main

if ($checkTools) {
    if (Test-Prerequisites) { exit 0 } else { exit 1 }
}

if (-not $path) {
    Write-Host "Give me a .dmp, a feedback .zip, or the folder holding them."
    Write-Host "  .\read-crash-dump.ps1 C:\temp\support\<ticket>"
    Write-Host ""
    Write-Host "Run .\read-crash-dump.ps1 -checkTools first to confirm you have what you need."
    exit 1
}

if (-not (Test-Path $path)) {
    Write-Host "There is nothing at that path: $path"
    exit 1
}

# Missing tools degrade the output rather than stopping the run, but silently producing a worse
# answer is how someone concludes a dump is unreadable when it is only unsymbolized.
if (-not (Test-Prerequisites)) {
    Write-Host "  Continuing anyway - the output below will be less useful than it could be."
    Write-Host ""
}

$dumps = @(Find-Dumps -root $path)
if ($dumps.Count -eq 0) {
    Write-Host ""
    Write-Host "No crash dumps found under $path"
    Write-Host ""
    Write-Host "A dump is named ziti-edge-tunnel.crash.dmp or ziti-edge-tunnel.stalled.dmp. If the"
    Write-Host "bundle has neither, the tunneler did not crash or stall on that machine while the"
    Write-Host "logs it kept were being written - which is itself worth saying on the ticket."
    exit 1
}

Write-Host ("found {0} dump(s)" -f $dumps.Count)
foreach ($d in $dumps) { Write-Host ("  " + $d) }
$summaries = @()
foreach ($d in $dumps) {
    $script:lastSummary = $null
    try {
        Show-Dump -dumpPath $d
    } catch {
        Write-Host "failed on ${d}: $($_.Exception.Message)"
        if ($script:lastSummary) { $script:lastSummary.Top = "failed: $($_.Exception.Message)" }
    }
    if ($script:lastSummary) { $summaries += $script:lastSummary }
}

if ($summaries.Count -gt 1) { Write-Summary -rows $summaries }
