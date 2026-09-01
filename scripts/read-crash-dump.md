# read-crash-dump.ps1

Turns a `ziti-edge-tunnel` crash dump into a readable stack, so a `.dmp` on a support ticket becomes a
function name and a source line instead of an opaque blob.

> **This is new and has not been used outside the machine it was written on.** If you are one of the first
> people to run it, expect something to be wrong. The most useful thing you can report back is **what you had
> to install**, because the prerequisites below are the author's best reconstruction and have never been
> followed on a clean machine.

## What you need first

Two free tools, one-time install. The script finds both by itself if you take the default install locations.

### 1. cdb (the debugger)

Without it there is no backtrace at all. You still get the faulting address and the registers, nothing more.

1. Download the Windows SDK installer: https://developer.microsoft.com/windows/downloads/windows-sdk/
2. Run it and **untick everything except "Debugging Tools for Windows"**. You do not need the rest of the SDK.
3. It installs to `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\`. No PATH change needed.

### 2. addr2line (the symbolizer)

Without it you still get the stack, but every frame reads as `ziti-edge-tunnel.exe+0x3d314` instead of
`ziti_send_posture_data (library/posture.c:198)`.

1. Install MSYS2: https://www.msys2.org/ -- take the default location, `C:\msys64`.
2. Open **MSYS2 MINGW64** from the Start menu and run:
   ```
   pacman -S --needed mingw-w64-x86_64-binutils
   ```
3. That puts `addr2line.exe` in `C:\msys64\mingw64\bin`.

The `mingw64` copy is the one that matters. MSYS2 also ships an `addr2line` under `usr\bin` that targets MSYS
rather than Windows binaries, and the script deliberately prefers the `mingw64` one over whatever is on PATH.

### Check you are ready

```powershell
.\read-crash-dump.ps1 -checkTools
```

```
prerequisite check

  [ok]      cdb          C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe
  [ok]      addr2line    C:\msys64\mingw64\bin\addr2line.exe

  All set - you will get full stacks with function names and source lines.
```

Anything missing is reported with the install steps repeated inline. If you keep a tool somewhere unusual,
pass `-cdbDir` or `-addr2lineDir`.

## Running it

Point it at any of these. It works out the rest.

```powershell
# a whole ticket - every machine, plus a comparison table at the end
.\read-crash-dump.ps1 C:\temp\support\12345

# one machine's bundle
.\read-crash-dump.ps1 C:\temp\support\12345\host-01

# a single dump
.\read-crash-dump.ps1 .\ziti-edge-tunnel.crash.dmp

# a feedback zip, still zipped
.\read-crash-dump.ps1 .\2026-08-24_091225.zip
```

Folders are always searched all the way down, so pointing at a ticket folder finds every dump on every
machine. If a folder holds only zips and no dumps, the zips are unpacked first.

There is nothing to configure. It works out which `ziti-edge-tunnel` build produced each dump, downloads that
exact binary from the ziti-tunnel-sdk-c releases, and proves the download matches the dump before trusting it.
Downloads are cached in `%LOCALAPPDATA%\zet-symbols`, so the first run of a given version is slow and the rest
are not.

## Reading the output

```
dump    : ...\host-01\service\ziti-edge-tunnel.crash.dmp
size    : 65,353 bytes
written : 2026-08-24 11:43:04 UTC   (machine clock said 13:43:04, UTC+02:00 per startup banner ...)
exception: 0xc0000005   faulting address: 0x0
rip      : 0x7ffa72ad5673   ucrtbase.dll+0x25673
note     : NULL dereference
process  : ziti-edge-tunnel.exe
```

- **`written`** is converted to real UTC using the *customer's* timezone, read from their own logs or
  `systeminfo.txt`. Do not use the file's timestamp in Explorer: a zip stores a bare wall clock with no zone,
  so your machine will show it shifted by the difference between your timezone and theirs.
- **`faulting address: 0x0`** is a NULL dereference. `0xffffffffffffffff` means the OS could not name the
  address, which usually means a freed or garbage pointer rather than a plain NULL.
- **`process`** is read from the dump itself, not the filename. If it says anything other than
  `ziti-edge-tunnel.exe`, the dump is of a different program and the script says so and stops.

Then the stack, most recent call first. The first frame with a source file is the top of OpenZiti code; the
`ucrtbase.dll` frame above it is a C library function like `strcmp` and rarely tells you anything.

### The summary table

More than one dump gets a table at the end. On a fleet ticket this is usually the actual answer:

```
MACHINE                KIND   WRITTEN            FAULT ADDR           TOP FRAME
host-01     crash  2026-08-24 11:43Z  0x0                  ziti_send_posture_data (posture.c:198)  <- ziti_pr_ticker_cb
host-02    crash  2026-08-20 02:10Z  0xffffffffffffffff   ziti_send_posture_data (posture.c:218)  <- process_connect

3 distinct crash signatures - do not report these as one cause without checking:
```

The `<-` names the calling function. The same line reached from two different callers is often two different
problems, so the signature count is deliberately conservative.

### "carries no usable evidence"

Some dumps tell you nothing, and the script says which and why:

- **0 bytes** -- the dump failed to write. A lost diagnostic, not a crash.
- **WRONG PROCESS** -- a snapshot of the tray UI stored under the tunneler's filename. A known ZDEW defect;
  the real dump for that machine was overwritten.

Both are worth reporting on the ticket. Neither means the machine is healthy.

## When it cannot symbolize

Occasionally you will see:

```
WARNING  : does not match the dump (exe stamp 0x69d3f28e/size 29908992, dump wants 0x69b886a7/size 29908992)
           same image size, different build date ...
```

That means the machine is running a build that is not one of the published releases, so no download can match
it. Getting a stack needs `ziti-edge-tunnel.exe` copied off that machine, then:

```powershell
.\read-crash-dump.ps1 <dump> -exeDir C:\path\to\folder\holding\that\exe
```

## Known gaps

- The install instructions above have not been followed on a clean machine. Please report what was actually
  needed.
- Tested on Windows PowerShell 5.1 and PowerShell 7.6.5.
- Stall dumps (`ziti-edge-tunnel.stalled.dmp`) written by ZDEW before the 2026-09 monitor fix are frequently
  0 bytes or the wrong process. That is a bug in ZDEW, not in this script.
- Sensitive data: dumps and bundles are customer material and contain hostnames, usernames and session state.
  Keep them out of anything public, and quote the minimum needed when pasting into a ticket.
