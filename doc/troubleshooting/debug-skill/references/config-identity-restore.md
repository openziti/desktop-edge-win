# Config, identities, and the Windows.old restore path

Read this for any ticket about **settings reverting, identities disappearing, or IP range changing
after a Windows update.** It is a known ZDEW defect path, not a customer error. The behavior is
**version-dependent** (fixed in ZET v1.18.6), so get the ZET version first: the `.ziti` dump header
(`references/ziti-dump.md`) or the startup banner (main skill, step 6).

```bash
grep -ahoE "^\[[^]]+\].*load_tunnel_status_from_file\(\) Loading config file from .*" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -ah "Restored old identity from the backup path\|Removing old identity from the backup path\|failed to copy backup identity file\|keeping existing file\|failed to remove backup file" \
  /abs/path/capture/service/ziti-tunneler*.log*
grep -ahoE "load_identities\(\) loading identity file: .*" /abs/path/capture/service/ziti-tunneler*.log*
```

What the code does (`ziti-tunnel-sdk-c`, `programs/ziti-edge-tunnel/ziti-edge-tunnel.c`,
`move_config_from_previous_windows_backup()`), on every version:

- Runs **unconditionally on every startup**, over `%SystemDrive%\Windows.~BT\...` and
  `%SystemDrive%\Windows.old\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry`.
- Copies **every regular file** in that folder despite the "identity" naming -- `config.json` included.

On **ZET before v1.18.6** (the defect path):

- Overwrites the live file without an exclusive flag.
- Runs **after** the config has already been read and after the IP range has been resolved from it. So a
  restored `config.json` is never read on the run that restores it, and the next save writes in-memory
  state back over it. On a fresh post-upgrade profile that state is defaults. Net effect: **identities come
  back, the config does not.**
- Deletes the backup file after a successful copy, but does not check or log whether the delete succeeded.
  `Windows.old` is TrustedInstaller-owned; if the delete fails, the copy repeats on every startup.

On **ZET v1.18.6 and later**:

- Runs **before** the config load, so a restored `config.json` is read on the same startup and the
  "identities come back, the config does not" outcome no longer applies.
- Never overwrites an existing live file: `keeping existing file X, backup file left at Y` (WARN) marks
  each skipped copy.
- Logs delete failures: `failed to remove backup file[...]` (WARN). Repeated on every startup, it means the
  backup copy of that file is stuck in `Windows.old` (TrustedInstaller-owned) but is now harmless.

So: `Restored old identity...` lines present -> the restore ran, name the files. Absent -> note it, and
**immediately restate the step-4 coverage caveat**, because `Windows.old` is pruned by Windows roughly 10
days post-upgrade and the startup that mattered is usually in a deleted log. Absent-and-out-of-window is
not evidence against this path.

**The monitor service restores its own settings the same way** (ZDEW 2.11.3.0 and later): on startup,
before its settings load and defaults are written, it copies missing files into
`...\systemprofile\AppData\Roaming\NetFoundry\ZitiUpdateService` from `Windows.old` then `$Windows.~BT`,
never overwriting an existing file and skipping empty backups. Check this for tickets about automatic
update or maintenance-window settings reverting after a Windows upgrade -- the evidence is in
`ZitiMonitorService/`, not the tunneler logs:

```bash
grep -ah "Windows upgrade backup\|keeping existing file\|ignoring empty backup file\|failed to copy backup file\|failed to remove backup file\|failed restoring backup files" \
  /abs/path/capture/ZitiMonitorService/*.log
```

Worth asking the customer for directly (a directory listing is enough):

```
C:\Windows.old\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry
C:\Windows.~BT\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry
C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\config.json
```

A colleague who upgraded more recently is a better evidence source than a customer whose `Windows.old` has
already been pruned.

