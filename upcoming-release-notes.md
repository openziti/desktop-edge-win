# Next Release
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

## Bugs fixed
n/a

## Other changes
n/a
