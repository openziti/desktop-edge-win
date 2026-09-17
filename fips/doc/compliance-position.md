# Compliance position

This page records what we may say, what we may not say, and why. It exists because the distinction between a
validated *module* and a validated *product* is the single most common source of confusion in these
conversations, and because the answer has to be the same every time it is given.

## The two roles

The CMVP recognises a **vendor** -- the party that submits a cryptographic module to an accredited laboratory --
and everybody else, who are **users** of a validated module.

For the OpenSSL FIPS Provider, the vendor is the OpenSSL project. The certificate is theirs. We are a validated
module user, and so is every downstream party who deploys our software.

That role assignment drives everything else:

- We do not hold a certificate and we are not seeking one.
- Our obligation is to build the module from the validated source without modification, install it the way the
  security policy says, call it in an approved manner, and be able to show that we did.
- Our customers' obligation, in turn, is to run our software in an operational environment the certificate
  covers, and to be able to show *that*.

## What we may claim

**Use this wording:**

> Ziti Desktop Edge for Windows uses FIPS 140-3 validated cryptography. When FIPS mode is enabled, the
> tunneler (`ziti-edge-tunnel.exe`) performs all of its cryptography using the OpenSSL FIPS Provider version
> 3.1.2, validated under CMVP certificate #4985. That covers the OpenZiti data plane and control plane.

Naming the component is the point: the claim is about the tunneler, not about every process ZDEW installs.
See [what the claim covers](#what-the-claim-covers) before rewording it.

**Do not use this wording:**

- "Ziti Desktop Edge for Windows is FIPS validated."
- "Ziti Desktop Edge for Windows is FIPS certified."
- "FIPS 140-2 validated" -- the certificate is 140-3. FIPS 140-2 validations move to the CMVP historical list in
  September 2026.

"FIPS Inside" is an accurate informal shorthand: the product bundles and uses a validated module, and the product
itself was never a candidate for validation.

## What the claim covers

ZDEW is three processes. Only one of them uses the OpenSSL FIPS Provider, and anyone answering a
questionnaire needs to be able to say which, without hesitating.

| Process | Cryptography it performs | Provided by |
| --- | --- | --- |
| `ziti-edge-tunnel.exe` (service `ziti`) | Ziti data plane and control plane: all TLS, key generation, enrollment, certificate handling | **OpenSSL FIPS Provider 3.1.2, CMVP #4985** |
| `ZitiUpdateService.exe` (service `ziti-monitor`) | HTTPS to the release stream; Authenticode signature verification on downloaded updates | Windows Schannel, CryptoAPI and .NET |
| `ZitiDesktopEdge.exe` (tray UI) | None | -- |

Both planes are covered: the **data plane**, application traffic carried over an OpenZiti connection, and the
**control plane**, the tunneler's sessions with the controller and routers, enrollment, and the identity
private keys behind both. That is what customers mean when they ask whether their zero-trust traffic uses
validated cryptography.

The monitor service is a different matter and should be described differently rather than quietly folded in:

- It performs no OpenZiti cryptography and handles no identity key material.
- Its cryptography is Microsoft's -- Schannel for HTTPS, CryptoAPI for Authenticode. Those are themselves
  FIPS-validated modules with their own CMVP certificates, held by Microsoft, and on a machine with the
  Windows FIPS algorithm policy enabled they run in their approved modes. So "not covered by certificate
  #4985" is not the same as "unvalidated"; it is covered by a different certificate that is not ours.
- Routing it through the OpenSSL FIPS Provider is not possible without rewriting it away from .NET's crypto
  stack, and would replace Microsoft's validated implementations with ours to no benefit.

**How to answer the question honestly:** the tunneler carries the data and control planes, and its
cryptography is performed by the OpenSSL FIPS Provider under certificate #4985. Update-channel HTTPS and
installer signature verification happen in a separate process, using the operating system's own validated
modules. No cryptography in the product falls outside those two.

If a customer requires everything on the machine to run in approved mode, that is the Windows FIPS algorithm
policy, which is their configuration decision and covers the Microsoft side. It does not affect OpenSSL and
the FIPS provider does not need it. See [zdew-integration.md](zdew-integration.md).

The reason the careful version is the right version is not legal timidity. Regulatory frameworks that reference
FIPS -- CJIS, the DISA STIGs, FedRAMP, PCI DSS -- require the *use of validated cryptographic modules*. They do
not require that every application touching those modules be itself validated, because applications are not
validatable units under the CMVP. Saying "we use validated cryptography, here is the certificate, here is our
evidence" satisfies the actual requirement. Saying "we are validated" claims something the CMVP does not issue
for products, and invites a reviewer to ask for a certificate number we cannot produce.

## Building the module ourselves

The objection worth taking seriously: if the validated artifact is the binary that a laboratory tested, then
anyone who compiles their own binary has become a vendor and needs their own validation. That reading explains
why several large distributors hold their own OpenSSL-derived certificates.

It turns out the CMVP answers this directly, and in our favour, with one sharp condition.

### The rule that permits us to rebuild

CMVP Management Manual §7.9 covers "Vendor or User Affirmation of Modules" (this is where FIPS 140-2 IG G.5
landed under 140-3; it is *not* IG 2.3.A, which is about algorithm certificate binding). It splits into two
roles. §7.9.1 is the module vendor -- OpenSSL. §7.9.2 is us.

§7.9.2 opens with a hard statement:

> A user may not modify a validated module. Any user modifications invalidate a module validation.

The footnote attached to that sentence is the entire basis of our position:

> **Footnote 5:** A user may post-validation recompile a module if the unmodified source code is available and
> the module's Security Policy provides specific guidance on acceptable recompilation methods to be followed as
> a specific exception to this guidance. The methods in the Security Policy must be followed without
> modification to comply with this guidance.

Both conditions are satisfied here. The source is available and unmodified. §11.1 of the #4985 security policy
gives specific recompilation guidance for Windows:

```
perl Configure enable-fips
nmake
nmake install
```

So the answer to "can we build it ourselves" is yes, explicitly, by name. Which makes the operative question a
much narrower one: **are we following that method without modification?** Every Configure flag, every compiler
choice, every packaging convenience is now a compliance question rather than an engineering preference. That is
why [build-fips-provider.md](build-fips-provider.md) recommends shipping a redistributable rather than adding
`/MT` to the Configure line.

Supporting points, which matter less now but are still worth having:

- OpenSSL's own guidance is explicit that the validated provider is to be built from validated source by the
  deployer: *"Please follow the Security Policy instructions to download, build and install a validated OpenSSL
  FIPS provider. Other OpenSSL Releases MAY use the validated FIPS provider, but MUST NOT build and use their own
  FIPS provider."*
- The module's integrity self-test uses an HMAC recorded in `fipsmodule.cnf` at installation time. That
  mechanism only makes sense if the module is expected to be built and installed by parties other than the
  laboratory.
- The distributors who hold their own certificates have reasons beyond compliance necessity: they modify source,
  they need specific operational environments in their own certificate's tested list, or they need a certificate
  in their own name for their own customers' procurement processes.

### Two unresolved points

Be aware of these rather than surprised by them.

**The compiler.** §5.3 of the security policy, under ISO/IEC 19790 Annex B "Open-Source Parameters", records the
compiler used for each tested environment, including **"Windows 10: Visual Studio 2019"**. §11.1's prescribed
method names no compiler. Whether §5.3 is part of "the methods in the Security Policy" is arguable. Building
with VS 2019 costs one VM and makes the argument moot, which is why that is the recommendation for shipping
builds. The build *host OS* is a separate and easier question: the operational environment is where the module
executes (ISO/IEC 19790:2012 §3.83), so compiling on one Windows version and running on another costs nothing
by itself.

**The missing appendix.** §11.1 says *"Please see Appendix A for further information on porting the Module to
platforms apart from the Tested Configurations in Table 3."* There is no Appendix A in the published document.
The porting guidance the policy points at does not exist in the text we are given. If a customer pushes hard on
untested operational environments, this is where the trail runs cold, and the answer is to route the question to
OpenSSL or their CSTL rather than to improvise one.

The mitigation for both is the same, and it is not a better argument. It is evidence. Which brings us to:

## Evidence we retain

The claim is only as good as the ability to demonstrate it. Per module build, retain:

- The source tarball and both the published and computed SHA-256.
- The full build configuration (`perl configdata.pm --dump`), and an explicit statement of any deviation from
  the security policy's §11.1 method. `Build-FipsProvider.ps1` records this as `methodDeviations` in the
  manifest, and writes `none` when there are none. "None" is the answer you want to be able to point at.
- Complete build and test logs, including the self-test results.
- SHA-256 of `fips.dll` and `openssl.exe` as shipped, and their Authenticode signatures and timestamps.
- Toolchain and operating-system versions of the build host.
- A record of any deviation from the security policy's documented build, with its justification.

`fips\scripts\Build-FipsProvider.ps1` produces all of this as a manifest. Archive it outside the build machine's
lifetime. A stopped VM is not an archive.

Per installation, the evidence is on the machine and is reproducible by the operator at any time:
`fipsmodule.cnf` holds the module MAC and self-test status, and the tunneler reports the configuration it loaded.
[test-plan.md](test-plan.md) is written so it can be handed to a customer as a self-verification procedure.

## Operational environment scope

Certificate #4985's tested operational environments include exactly one Windows entry: **Windows 10 Pro on
x86-64**. The remaining tested environments are Linux, FreeBSD and macOS, which are irrelevant to ZDEW.

ZDEW supports Windows 10, Windows 11 and Windows Server. Only the Windows 10 x64 case falls inside the tested
list, and the policy's own table says plainly: *"No operational environments are vendor affirmed."*

The governing rule for the rest is Management Manual §7.9.2, user porting:

> For Level 1 OE, a software, firmware, or hybrid cryptographic module will remain compliant with the FIPS 140-3
> validation on any general-purpose platform/processor that supports the specified operating system listed on
> the validation entry, **or another compatible operating system**.

with footnote 4 defining compatibility: *"OSs of the same 'family' could be another example of compatibility."*
Windows 11 and Windows Server are the same family as Windows 10, so this is a reasonable read. It is also
followed immediately by the limit:

> The user may affirm that the module works correctly in the new OE if the porting rules are followed. However,
> the CMVP makes no statement as to the correct operation of the module or the security strengths of the
> generated keys when ported and executed in an OE not listed on the validation certificate.

So: we may affirm it, the CMVP will not. That is our exposure rather than the laboratory's, so be straight about
it:

- Say which environments are tested and which are vendor-affirmed. Do not present them as equivalent.
- Some auditors will accept vendor affirmation without comment. Some will require a tested environment. That is
  the customer's auditor's call, not something we can settle in advance.
- Where a customer will not accept vendor affirmation on Windows 11 or Server, the alternatives are Microsoft's
  own validated CNG modules -- which are validated per Windows release and would require a different TLS backend
  in the tunneler -- or a commercially validated library. Both are substantial engineering programmes, not
  configuration changes.
- Windows ARM64 has no tested environment on this certificate at all. If ZDEW ever ships ARM64, it ships without
  a FIPS claim unless something changes.

## CVEs in the validated module

The security policy addresses this directly in §11.1(b):

> The publication of a CVE does not require immediate re-validation or maintenance in the CMVP process. The
> module may be updated in the field as needed depending on the severity or consequences of the CVE. The Module
> will be kept up to date with re-validation and maintenance as required, generally bundling fixes for known
> CVEs in a next release.

Read that carefully. It says the CMVP does not *force* revalidation when a CVE is published, and that you may
patch in the field if the severity warrants it. It does not say a patched build remains the validated module --
it cannot, because the validated module is 3.1.2. Patching is permitted; the FIPS claim does not survive it.

So the policy is:

- Do **not** silently rebuild the provider from a patched 3.1.x release in order to fix a CVE. Doing so is
  permitted by the policy and is sometimes the right call, but it ends the FIPS claim, and it must be a stated
  decision rather than a quiet dependency bump.
- Track OpenSSL's FIPS and CVE announcements. When a CVE affects the FIPS provider, assess exposure for ZDEW's
  usage specifically, and make a deliberate, documented choice: accept the risk and keep the validated module, or
  drop the FIPS claim and ship a patched provider.
- Watch for a new OpenSSL FIPS provider validation. 3.1.2 is the only OpenSSL-project provider with an active
  certificate today; when a newer one is validated, moving to it is a planned project with its own evidence
  package, not a version bump.
- The core library is a separate matter. `ziti-edge-tunnel` may and should track current OpenSSL releases -- it
  is on 3.6.3 today. Only the provider is pinned.

Certificate #4985 sunsets on **10 March 2030**. That is a hard deadline, and it should be on a roadmap rather
than discovered.
