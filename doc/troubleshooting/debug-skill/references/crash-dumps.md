# Crash and stall markers

Read this when a `.dmp` file exists in the capture, or the ticket reports a crash, a hang, or the tray icon
going dead. Assumes the timezone offset and the service-lifecycle table from the main skill are already in hand.

```bash
ls -la /abs/path/capture/*.dmp /abs/path/capture/service/*.dmp 2>/dev/null
```

- `ziti-edge-tunnel.stalled.dmp` in the capture root -> **STALLED**
- `service/ziti-edge-tunnel.crash.dmp` -> **CRASHED**
- neither -> **no crash/stall markers**

Glob `**/*.dmp` once across the whole ticket folder, with sizes and dates, so you can see which captures have
them:

```bash
ls -la --time-style=long-iso /abs/path/<ticket>/*/*.dmp /abs/path/<ticket>/*/service/*.dmp
```

**Two different writers, and the distinction decides which repo the bug belongs to.**

- `stalled.dmp` is written by **ZDEW's monitor service** (`ZitiUpdateService/utils/MiniDump.cs`, called from
  `UpdateService.cs`) just before it kills a tunneler it believes is blocked. Bugs here are `desktop-edge-win`.
- `crash.dmp` is written by **ZET itself**, from its own `CrashFilter`
  (`programs/ziti-edge-tunnel/windows/minidump.c`). Bugs here are `ziti-tunnel-sdk-c`.

**`ziti-edge-tunnel.stalled.dmp` does not always contain `ziti-edge-tunnel`.** `stopProcessForcefully()` in
`UpdateService.cs` writes that filename for every process it dumps. `StopUI()` calls it against
`ZitiDesktopEdge.exe` during an upgrade, so upgrades produce a WPF UI dump under the tunneler's stall name.
Each write overwrites the previous dump at that path.

Observed across eight `stalled.dmp` files on one fleet: 1 `ziti-edge-tunnel.exe`, 3 `ZitiDesktopEdge.exe`,
4 zero-byte.

Read the process image out of the dump before using it. The script prints it and warns. A `ZitiDesktopEdge.exe`
dump carries no tunneler modules and no exception record.

`stalled.dmp` mtimes clustered within minutes across several machines indicate an upgrade rollout, not stalls.

**A 0-byte `.dmp` means `MiniDumpWriteDump` failed outright.** Do not report it as a stall or a crash; report
it as a lost diagnostic. Common on the `stalled.dmp` path, where the write races the `Kill()` that follows it.

**Date every dump before trusting it.** These are fixed filenames, overwritten by each new event, so a dump
tells you about the machine's *most recent* crash and nothing else. Convert the mtime from local to UTC
(main skill, step 4) before comparing it to anything. A dump months older than the reported incident is not
noise -- it is the previous occurrence, and its date is evidence.

**Correlate the dump mtime against the identity certificate's `Not Before`.** This is the highest-yield check
in the whole step and it is easy to miss:

```bash
grep -aA6 "Identity Cert:" /abs/path/capture/service/*.ziti | grep -aiE "Not (Before|After)"
```

A dump written within a minute of the certificate's `Not Before` means the crash happened during certificate
renewal. Six of six dumps matched this way on one fleet ticket, which turned a vague "it crashes sometimes"
into a named trigger. Renewal fires roughly 23–30 days before expiry on a one-year certificate, so it is a
rare event -- if the timestamps line up, that is not coincidence.

The same query gives you `Not After`, which lets you predict the *next* renewal per machine and say which
machines are still exposed. A machine with no crash dump whose certificate expires soon has simply not renewed
yet.

**Getting a stack out of a dump. Run the script; do not do this by hand.**

```
desktop-edge-win/scripts/read-crash-dump.ps1 <bundle dir | feedback zip | .dmp>
```

It reads the dump, takes the ZET version from the bundle, downloads and caches that exact release binary,
**proves the binary matches the dump** by comparing PE `TimeDateStamp` and `SizeOfImage` against the recorded
module, unwinds with whatever debugger is installed, and symbolizes with whatever `addr2line` it can find. It
prints a `FRAME / ADDRESS / FUNCTION / SOURCE` table. Point it at a fleet directory and it walks every bundle,
then prints a table comparing the dumps against each other.

Requires `cdb` (Windows SDK Debugging Tools) and `addr2line` (MSYS2 `mingw-w64-x86_64-binutils`). Run
`read-crash-dump.ps1 -checkTools` to report which are present. Install steps: `scripts/read-crash-dump.md`.

It degrades rather than failing: no `cdb` still gives the faulting address and instruction pointer, no
`addr2line` still gives ordered frames as module offsets. Pass `-version` or `-exeDir` when the bundle has no
usable `.ziti` or log banner, and `-force` to symbolize against a binary that did not verify.

Two things it protects you from, both of which have already produced wrong conclusions on a real ticket:

- **A fleet ticket holds one bundle per machine.** Version detection is scoped to the bundle the dump came
  from. Reading a neighbour's `.ziti` yields the wrong build for the wrong machine, and the frames that come
  back look plausible.
- **The binary must be verified, not assumed.** ZDEW ships its own build of ZET, so a machine can run a
  `v1.11.4` whose `SizeOfImage` matches the public `v1.11.4` release while its `TimeDateStamp` differs. Line
  numbers from the wrong build are silently wrong. When the script refuses to symbolize, that is why.

Doing it by hand, if you must: download the exe from the ZET release matching the version in the `.ziti`
header (`references/ziti-dump.md` tells you whether it is the regular or `-win32crypto` build), then in WinDbg
`.exepath+ <dir holding the exe>`, `.reload /f`, `.ecxr`, `kb`. **`!analyze -v` is not a substitute** -- its own stack walk
returns a single frame on these dumps where `.ecxr` + `kb` returns the full stack.

**WinDbg will not give you names or line numbers, whatever its version.** MinGW keeps debug info as DWARF
inside the exe and no PDB is ever published, and WinDbg reads only PDB. Confirmed on 10.0.29617, which still
printed `ziti_edge_tunnel+0x8760`. Names and lines come from `addr2line` against the same exe, rebasing each
runtime address onto the file's preferred `ImageBase` and stepping back one byte on return addresses. The
script does all of that.

**You can triage a dump without a debugger,** which is worth doing before asking the operator to open WinDbg.
Every dump embeds a `MINIDUMP_EXCEPTION` record: locate the little-endian `ExceptionCode` bytes `050000c0`
(`0xc0000005`, access violation), then read `ExceptionInformation[1]` at +40 for the faulting address, and the
thread `CONTEXT` via the RVA at +156 (`Rax` at +0x78, `Rcx` +0x80, `Rdx` +0x88, `Rsp` +0x98, `Rip` +0xF8).

Two things that fall out of the registers alone:

- A faulting address of `0x0` is a NULL dereference. `0xFFFFFFFFFFFFFFFF` means the OS could not report the
  address, which usually means the bad pointer was not merely NULL but garbage -- check whether the argument
  register is *non-canonical* (bits 63–48 neither all-zero nor all-one), because such a value can never be a
  valid user-mode address and points at a use-after-free rather than a missing NULL check.
- Registers often hold string fragments. Decode them as little-endian ASCII -- `0x3534333231636261` reads
  `abc12345`. On one crash this recovered the surviving argument to a failed `strcmp`, and its shape (a ziti
  id) identified which of the two struct fields was NULL.

**Do not generalize one dump to the rest.** Compare `Rip` and the faulting address across every dump on the
ticket before concluding they share a cause. On one fleet ticket five dumps faulted at one `ucrtbase` offset
with address `-1` while the sixth faulted at a different offset with address `0` -- two distinct defects that
looked identical until the exception records were read side by side.

