# debug-ziti-desktop-edge-win — Claude Skill

A Claude Code skill that analyzes a ZDEW feedback zip and produces a structured diagnostic report.

## Setup

Copy `SKILL.md` into your Claude skills directory:

```bash
mkdir -p ~/.claude/skills/debug-ziti-desktop-edge-win
cp SKILL.md ~/.claude/skills/debug-ziti-desktop-edge-win/
```

## Usage

1. Place the feedback zip in your working directory (or `cd` there)
2. Open Claude Code in that directory
3. Run:

```
/debug ziti-desktop-edge-win
```

Claude will ask for the ticket number, extract the zip, and automatically work through:

- Crash / stall dump detection
- `.ziti` identity file analysis (controllers, routers, latency, connections)
- Service start/stop history and ungraceful exit investigation
- Error categorization — network errors vs. service/dial errors, with day-over-day spike detection

## What it handles

- **Single feedback zip** — standard `feedback.zip` from one machine
- **Aggregated zip** — a zip containing multiple timestamped feedback zips (e.g. from a bulk export);
  each inner zip is extracted and analyzed separately

## Output

A report per capture covering:

- Any crash or stall markers
- Controller and router health at time of capture
- Tunneler lifecycle (restarts, reboots, update installs, ungraceful exits)
- Error summary split by network vs. service failures, with flags for unusual spikes
