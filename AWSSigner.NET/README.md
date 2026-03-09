# AWSSigner.NET

A custom Authenticode signing tool that uses **AWS KMS** as the HSM backend. The private key
never leaves AWS KMS — only a digest is sent over the wire.

## Purpose

Windows Authenticode signing normally requires the private key to be locally accessible (PKCS#12
file or hardware token). This tool implements a "detached digest" pattern so that an asymmetric
RSA key stored in AWS KMS can be used for Authenticode without ever exporting it.

### Why AWS KMS?

Since June 2023, CA/Browser Forum rules require EV code-signing private keys to be stored in a
**FIPS 140 Level 2** (or higher) certified module. GitHub Actions secrets do not meet this
requirement. AWS KMS is **FIPS 140 Level 3** certified — a superset of Level 2 — so it satisfies
the requirement while remaining accessible from CI/CD pipelines without ever exporting the key.

### AWS prerequisites

Before using this tool you need:

1. An asymmetric RSA signing key created (or imported) in AWS KMS
2. A code-signing certificate from your CA (the OpenZiti project uses GlobalSign) issued against a
   CSR generated from that KMS key — key usage must include **Digital Signature**, extended key
   usage must include **Code Signing** (`1.3.6.1.5.5.7.3.3`)
3. An IAM user (or role) with the following KMS permissions scoped to that key:
   `kms:ListKeys`, `kms:DescribeKey`, `kms:Sign`
4. AWS CLI credentials for that IAM user configured in the environment

## How it is used in the build

`Installer/build.ps1` wires everything together:

1. `AWSSigner.NET` is built (`dotnet build -c Release`) and staged to `Installer/AWSSigner.NET/`
2. `SIGNING_CERT` is set to the GlobalSign DV certificate (`Installer/GlobalSign-SigningCert-2024-2027.cert`)
3. `SIGNTOOL_PATH` is set to the `signtool.exe` bundled with Advanced Installer
4. Advanced Installer builds `ZitiDesktopEdge.aip` — the `.aip` project is configured to call
   `AWSSigner.NET.exe <file>` as its custom signing tool, which fires for every binary that needs
   to be signed during the installer build
5. Optionally, a **second (dual) Authenticode signature** is appended to the final installer EXE
   with `signtool /as` using the local `openziti_2024.p12` cert (ssl.com TSA) — only when the
   environment variable `OPENZITI_P12_PASS_2024` is set

## Signing steps performed by AWSSigner.NET

```
signtool sign /dg <dir> /fd sha256 /f <cert> <file>
  --> produces <file>.dig  (base64-encoded digest)

AWS KMS Sign(keyId, digest, RSASSA_PKCS1_V1_5_SHA_256)
  --> returns base64 signature, written to <file>.dig.signed

signtool sign /di <dir> <file>
  --> injects the KMS signature into the PE Authenticode slot

signtool timestamp /tr http://timestamp.digicert.com /td sha256 <file>
  --> countersigns with DigiCert RFC 3161 timestamp
  NOTE: without a timestamp the signature is only valid while the signing cert is valid.
        The timestamp proves the binary was signed while the cert was current, extending
        trust beyond the cert's expiration date.

signtool verify /pa <file>
  --> validates the completed signature chain

cleanup: <file>.dig  <file>.dig.signed  <file>.p7u
  NOTE: <file>.p7u is produced by the /dg step and is required as input for the /di step.
        All three temp files are removed after successful signing.
```

The signing algorithm used is `RSASSA_PKCS1_V1_5_SHA_256` which maps to the `sha256` file-digest
flag (`/fd sha256`) passed to `signtool`.

## Required environment variables

| Variable               | Description                                                        |
|------------------------|--------------------------------------------------------------------|
| `AWS_KEY_ID`           | ARN or alias of the KMS asymmetric signing key                     |
| `AWS_ACCESS_KEY_ID`    | AWS IAM access key (`kms:ListKeys`, `kms:DescribeKey`, `kms:Sign`) |
| `AWS_SECRET_ACCESS_KEY`| Corresponding IAM secret key                                       |
| `AWS_REGION`           | AWS region where the KMS key lives (e.g. `us-east-1`)             |
| `SIGNING_CERT`         | Path to the `.cert` / `.cer` file for the code-signing certificate |
| `SIGNTOOL_PATH`        | (optional) Full path to `signtool.exe` if not on `PATH`            |

`build.ps1` sets `SIGNING_CERT` and `SIGNTOOL_PATH` automatically. The AWS variables must be
provided by the CI/CD environment (or the operator's shell when building locally).

## Optional second signature (dual-signing)

If `OPENZITI_P12_PASS_2024` is set, `build.ps1` appends a second Authenticode signature via:

```powershell
signtool sign /f openziti_2024.p12 /p $env:OPENZITI_P12_PASS_2024 `
              /tr http://ts.ssl.com /fd sha512 /td sha512 /as <installer.exe>
```

This uses SHA-512 digests and the ssl.com TSA, complementing the SHA-256 KMS signature.

## Debugging locally

Set `AWSSIGNER_DEBUG=TRUE` to enable verbose logging. In a `DEBUG` build, `Program.cs` accepts
four positional arguments so you can test without Advanced Installer:

```
AWSSigner.NET.exe <env-var-file.ps1> <file-to-sign.exe> <cert.cert> <signtool.exe>
```

The env-var file should contain lines in the form `$env:VAR="value"`.

Logs are written to `AWSSigner.log` in the working directory, with daily rotation (7-day
retention).

## Known limitations / gotchas

- **Validation short-circuits incorrectly**: `VerifyEnvVar` is called for all four required AWS
  variables but each call overwrites the same `envVarsExist` boolean. Only the last variable
  checked (`SIGNING_CERT`) determines whether the process aborts. If the AWS variables are missing
  but `SIGNING_CERT` is set, the tool will proceed and fail at the KMS call rather than at
  startup validation.
- **Silent return on missing inputs**: when validation fails the tool exits with code 0, so
  Advanced Installer treats it as success. The resulting binary will be unsigned. Watch for the
  `ERROR:` lines in the console output or `AWSSigner.log`.
- **`AWS_KEY_ID` missing = unsigned build**: `build.ps1` warns but does not abort when
  `AWS_KEY_ID` is unset. A release built without this variable will ship unsigned.
- **Target framework**: The project targets .NET 4.7.2 (`net472`). `dotnet build` works but
  requires the .NET Framework 4.7.2 targeting pack to be installed on the build machine.
