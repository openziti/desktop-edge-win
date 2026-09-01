# Write the report

Write to the document location chosen in the main skill's step 2 (the working directory, e.g.
`<ticket>-analysis.md`) -- not inside the extracted log tree.

Drop any section whose reference file you did not read: a ticket with no dumps gets no "Crash / Stall Markers"
heading rather than one saying "none". Keep the sections you did work, in this order.

```markdown
# ZDEW Debug Report -- Ticket <ticket_number>

**Source zip:** `<filename>` (<size>)
**Captures:** N (single / aggregated)
**Generated:** <date>

---

## <capture_name> -- <hostname>

| Item | Value |
|---|---|
| ZDE app | |
| Tunneler SDK | |
| C SDK | (with commit) |
| tlsuv / OpenSSL | (absence = the -win32crypto build) |
| OS | (name, build, release name) |
| Original install date | (= in-place upgrade date, if recent) |
| System boot | (corroborating only -- unreliable under Fast Startup) |
| Identity cert | `Not Before` / `Not After` |
| Dump taken | |
| Controller | (version, HA or single) |

### Log Window Coverage
Oldest log per directory, the incident date, and whether the window covers it. Name any days destroyed by
`delete_older_logs()`.

### Prior Asks
What support requested, and whether the logs show it was done.

### Crash / Stall Markers
Which writer produced each dump, its mtime converted to UTC, whether it lines up with the certificate
`Not Before`, and the faulting address and `Rip` per dump. Say explicitly when dumps do *not* share a signature.
0-byte dumps are a lost diagnostic, not an event.

### Service History
Chronological table. Reboots labeled as reboots, crashes labeled as crashes, with the evidence for each
(orphan `service begins`, monitor `did not exit cleanly`, or `OnShutdown`).

### Config / Identity Handling
Only for settings-loss or identity-loss tickets: what the startup loaded, whether the Windows.old restore
ran, and the coverage caveat if it's silent.

### Tun Adapter / DNS Range / Routes
Configured vs default range, actual adapter IP and mask, and which adapter owns each contested route.

### Error & Warning Analysis
Per-file counts, bucketed messages, categories 1–3, and anything periodic.

### .ziti Analysis
Per identity: controllers, channels, connections, API session.

---

## Summary

The most significant findings across all captures, in one short paragraph.

## What to act on

Numbered, concrete: what to ask the customer, what to file as a bug, what cannot be answered from this
bundle and what capture would answer it.
```

Rules for the report:

- **Separate observation from inference.** Quote the log line, then say what you think it means. A support
  engineer relaying your words to a customer needs to know which is which.
- **Lead with what changed the conclusion**, not with the checklist order.
- **Say plainly when the symptom does not reproduce in the bundle** -- and then say what would be needed to
  catch it, rather than padding with healthy-system findings.
- **Every "no evidence of X" carries its coverage caveat** or it will be misread as "X did not happen."
- Keep a healthy-system section short. Three lines confirming channels, connections, and lifecycle are fine
  are worth more than three paragraphs.
- **Assume the report gets forwarded.** Quote the minimum log text needed, and never include identity file
  contents, session tokens, certificates, or full controller URLs. If a finding requires a customer
  hostname or identity name to make sense, use it -- but don't paste surrounding lines that add nothing but
  exposure.

After writing, tell the operator the path and the one finding that matters most.
