# debug-ziti-desktop-edge-win — Claude Skill

A Claude Code skill that analyzes a ZDEW feedback zip and produces a structured diagnostic report.

Self-contained: all commands are inline in `SKILL.md`. Nothing to install, no helper scripts to run.

## Setup

```bash
mkdir -p ~/.claude/skills/debug-ziti-desktop-edge-win
cp SKILL.md ~/.claude/skills/debug-ziti-desktop-edge-win/
```

## Usage

```
/debug ziti-desktop-edge-win [ticket number or zip path]
```

Give it a ticket number and it fetches the bundle from whatever support-desk integration is available in the
session, listing the attachments first so you pick the right one. Give it a zip path, or leave a zip in the
working directory, and it uses that instead.

## What it does

1. **Locates the bundle** — invocation arg, working directory, or ticket attachment. Reads the ticket
   comments, including internal ones, to find the hypothesis under test.
2. **Dates the machine** — OS build, boot time, and `Original Install Date`, which an in-place Windows
   upgrade resets and which therefore dates the upgrade.
3. **Checks log coverage** — ZDEW prunes its own logs on startup, so bundles collected days later often do
   not contain the incident. This gates everything downstream.
4. **Verifies prior asks** — whether the restart or trace-logging that support requested actually happened.
5. **Service lifecycle** — starts, stops, reboots, monitor actions, aliveness peaks.
6. **Crash / stall dumps.**
7. **Config and identity handling** — the `Windows.old` / `Windows.~BT` restore path, for settings-loss and
   identity-loss tickets.
8. **Tun adapter, DNS range, routes** — configured vs default range, and which adapter actually owns each
   contested CGNAT route.
9. **Errors and warnings** — bucketed by message shape, split into network / service-dial / routing-NRPT,
   with resume-from-sleep and midnight-OIDC baselines so normal churn isn't escalated.
10. **`.ziti` dump** — controllers, channels, connections, API session, per identity.

## What it handles

- **Single feedback zip** — standard capture from one machine
- **Aggregated zip** — a zip of timestamped feedback zips; each inner capture is extracted and analyzed
  separately

## Handling the data

A feedback bundle is confidential customer material. Identity `.json` files contain enrollment tokens, private
keys, and client certificates; `.ziti` dumps contain session certificates and tokens; the system and UI logs
contain usernames, hostnames, domains, internal IPs, and internal service names. The skill keeps the extracted
tree out of committed directories, quotes the minimum log text needed as evidence, and never puts identity
file contents or tokens in a report.

## Design notes

The skill is opinionated about a few failure modes that produced wrong answers in practice:

- Absence of a log line is not evidence until log coverage, log level, and rolled files are all checked.
- `WARN` is scanned as well as `ERROR` — some of the highest-volume real defects never reach `ERROR`.
- Routes in `100.64.0.0/10` are attributed by interface, because Zscaler, Netskope, GlobalProtect and dock
  NICs all live in CGNAT space and get mistaken for ZDE.
- `uptime[Ns]` in a `.ziti` dump is per-identity context uptime, not process uptime.
- A list of known misleading strings, including `1168(The operation completed successfully.` — a failure
  whose message text says the opposite.

## Output

One report per capture, written to the working/repo directory (not into the extracted log tree), separating
observation from inference and closing with a numbered "what to act on" list.
