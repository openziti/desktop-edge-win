# Release 2.11.2.2
## What's New
* Enroll to cert / token via external JWT signers
    * Joining a network by URL now discovers the controller's external JWT signers and routes
      enrollment through them when applicable
    * If the controller has no external signers, behavior is unchanged
    * If a single signer is offered with a single mode, the user is taken straight through
    * If a choice is required (multiple signers, or one signer that supports both cert and
      token enrollment), a new picker dialog appears
    * When the controller asks the user to authenticate in a browser, ZDEW launches it and
      the identity arrives once sign-in completes
* Welcome screen for first-run / no-identities state ([#1006](https://github.com/openziti/desktop-edge-win/issues/1006))
    * Appears automatically when the app has zero identities, dismissable per session
    * Inline "Add by JWT" / "Add by URL" links route straight into the existing add-identity flow
    * Pasting a URL from the clipboard pre-fills the Add-by-URL field and pre-selects the text
      so the first keystroke replaces it
    * NetFoundry logo doubles as a drag handle (left-click drag to detach, right-click to
      reattach) -- same gesture as the central Z
* Tray-icon context menu rebuilt ([#1007](https://github.com/openziti/desktop-edge-win/issues/1007))
    * "By NetFoundry vX.Y.Z" branding header with the NF mark
    * "Open OpenZiti Desktop Edge" brings the main window forward
    * Identities section (visible when at least one is enrolled) shows each identity with
      service count and status; clicking opens that identity's details panel
    * "Switch Tunneler" submenu (visible only when more than one ziti-edge-tunnel instance is
      running) mirrors the Ctrl+Shift+T dev picker
    * Add Identity by JWT / URL shortcuts -- same handlers as the in-app + button
    * Logging submenu: Set Log Level (Trace/Verbose/Debug/Info/Warn/Error with live
      checkmark) plus Open log folder
    * Help submenu: Show Welcome screen, Check for updates (with in-place spinner that keeps
      the menu open while running, and an "Update Now (vX.Y.Z)" entry that appears when an
      update is staged), Capture Feedback, Discourse Community, NetFoundry Support
    * "Close UI" preserved at the bottom
* Maintenance window can now run daily, weekly, or monthly
    * Weekly: pick a day of the week
    * Monthly: pick a specific day of the month, or pick an ordinal weekday like the third
      Tuesday or the last Friday
    * Hour-of-day start and end still applies within the qualifying day
    * Settings available via Group Policy, the bundled helper script, and the Automatic
      Upgrades panel in the app

## Bugs fixed
n/a

## Other changes
n/a



## Dependencies
* ziti-tunneler: v1.17.0
* ziti-sdk:      1.17.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]
