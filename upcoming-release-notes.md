# Next Release
## What's New
n/a

## Bugs fixed
* [Issue 776](https://github.com/openziti/desktop-edge-win/issues/776) - Feedback collection no longer times out prematurely on large or verbose log bundles
    * Progress dialog shows the current phase (copy, collect, zip) and bundle size
    * Stall detection: if the service stops sending progress for 10 seconds the UI surfaces an error
    * A notice is shown when feedback is requested while a previous collection is still in progress
    * Error dialog reports the underlying error rather than always saying "monitor service is offline"
    * Symlinked log files are skipped, and a duplicate ZET log copy step was removed

## Other changes
n/a
