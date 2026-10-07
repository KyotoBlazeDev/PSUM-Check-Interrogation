# Security Policy for PSUM Check Interrogation

## Supported Versions

This PSUM Check Interrogation is currently maintained as a stable WinUI 3 application.
It may change without notice if features and/or bugs are added.

| Version | Supported |
| --- | --- |
| 1.0.x | Yes |
| < 1.0 | No |

As current scope, security fixes may be released as maintenance updates when necessary.

## Reporting a vulnerability
> [!WARNING]
> Please do not disclose suspected security vulnerabilities publicly before they have been reviewed for added security reasons.

> [!NOTE]
> When reporting a vulnerability, you must include the following information as much as possible:
> - A clear description of the issue
> - The affected PSUM version
> - Steps required to reproduce the problem.
> - Expected behavior.
> - Actual behavior.
> - Relevant logs, screenshots, or error messages.
> - Whether administrator privileges are required.
> - Whether the issue affects local data, system configuration, WMI access, installer behavior, or release integrity.

> [!CAUTION]
> Please avoid including passwords, authentication tokens, private keys, personal information, or other sensitive data in reports.
> If no private reporting channel is available, open a minimal GitHub issue requesting a private security contact without publishing exploit details.

## Security Scope

Security scope is useful when they involve this:
- Arbitrary code execution.
- Privilege escalation.
- Unsafe handling of administrator privileges.
- Unauthorized modification of system settings.
- Unsafe file or path handling.
- Malicious or malformed database/import data.
- Exposure of sensitive local information.
- Installer or uninstaller security problems.
- Dependency or supply-chain compromise.
- Release artifact tampering.
- Code-signing or signature-verification failures.
- Unexpected write operations through Windows or Lenovo interfaces.

PSUM is staged, designed to be interrogated and record battery information. This may use Lenovo-documented WMI and Microsoft-documented WMI as suggested.
Features intended to be read-only should not silently modify firmware, Lenovo settings, or Windows configuration.

## Out of Scope

The following are out of scope and not supported as vulnerability reporting by themselves:
- Unsupported battery telemetry.
- Missing Lenovo WMI interfaces.
- Firmware or hardware values reported incorrectly by the device.
- Cosmetic UI problems.
- Expected administrator prompts.
- SmartScreen warnings on currently unsigned releases.
- Antivirus heuristic detections without evidence of malicious behavior.
- Problems caused by unsupported modifications to PSUM binaries or databases.

Therefore, it may still be reported as normal bugs. If you see the bugs, please report them in the [Issues section](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/issues).

## Current State of Release Signing Status
PSUM Check Interrogation v1.0.x is currently distributed unsigned.

Users should verify downloads against the SHA-256 checksum published with the release.

A future release is planned to introduce Authenticode code signing for non-Microsoft-Store distribution.

Until signed releases are available, an unsigned installer should not be interpreted as evidence that the file is malicious; users should obtain releases only from the official KyotoBlazeDev repository and verify published hashes.

## Planned Code-Signing Security

> [!NOTE]
> Beginning with the planned 2027 v1.1.0 generation, production releases are intended to be used for validation code-signing identity.

It includes the planned release:
1. Build from an authorized source revision.
2. Run automated tests.
3. Perform dependency and release-integrity checks.
4. Perform a pre-signing malware scan on supported artifacts.
5. Stop the signing process if a malware finding is unresolved.
6. Sign eligible application binaries.
7. Apply a trusted timestamp.
8. Verify the resulting Authenticode signatures.
9. Build and sign the final installer where applicable.
10. Verify the final installer signature.
11. Generate and publish SHA-256 checksums.
12. Publish the release only after required checks pass.

A successful malware scan or a valid digital signature **does not guarantee** that software contains no vulnerabilities. These controls are additional safeguards, not a claim of absolute security.

## Malware Scanning

KyotoBlazeDev is intended to be used as pre-signing malware scanning as a release gate for supported artifacts.

A positive malware result must be investigated before the affected artifact can receive the production signing identity.

False positives may occur and will be reviewed rather than automatically treated as proof of malicious code.

Malware scanning does not replace source review, dependency review, testing, or vulnerability reporting.

## Code-Signing Key Protection
Production signing credentials must not be committed to the repository.

Signing keys should remain within approved hardware-backed or cloud-backed key protection.

Pull requests and ordinary CI jobs must not automatically receive access to production signing credentials.

Only authorized release workflows should be able to request production signatures.

If the signing identity or signing credentials are suspected to be compromised, signing should be suspended until the incident has been investigated and appropriate revocation or recovery actions have been completed.

## Release Integrity

Official releases should be traceable to an intended source revision.

Where practical, release validation should include:

- Source commit or tag.
- Build result.
- Test result.
- Malware-scan result.
- Signature-verification result.
- Timestamp-verification result.
- SHA-256 checksum.
- Installer validation result.

A release must not be published if required security checks fail.

## Dependencies and Supply Chain

Third-party dependencies should be obtained from trusted sources and kept reasonably current.

Security-sensitive dependency updates should be reviewed before release.

Build and release workflows should use minimal permissions.

Production signing should be separated from untrusted pull-request execution.

Workflow dependencies should be pinned or otherwise constrained where practical.

## Local Data and Privacy

PSUM stores local battery and inspection information.

Security-sensitive data handling should follow these principles:

- Collect only data needed for PSUM functionality.
- Avoid storing credentials or secrets.
- Avoid including unnecessary personal data in logs.
- Treat imported files and database content as untrusted input.
- Validate file paths and imported data before use.
- Document where PSUM stores application data and backups.
- Keep installer logs separate from application diagnostic records where practical.

## Administrator Privileges

PSUM should follow least-privilege principles.

>[!NOTE]
> Administrator privileges should only be requested when required for a specific operation or installation context.
> Read-only battery interrogation should not require elevation solely for convenience.
> Business or deployment editions may use machine-wide installation locations and registry keys where appropriate, but this does not justify elevated runtime privileges for unrelated functionality.

## Security Updates

Confirmed vulnerabilities may be fixed through maintenance releases.

Depending on severity, affected releases may be marked unsafe, replaced, or accompanied by upgrade guidance.

Users should upgrade promptly when a release is identified as containing a security-sensitive fix.

## Disclosure

KyotoBlazeDev values responsible disclosure and transparency.

Security controls such as malware scanning, digital signatures, checksums, and automated validation improve trust in the release process, but none of them provide a guarantee that software is completely free from defects or vulnerabilities.

The project will document known limitations rather than represent security controls as guarantees.
