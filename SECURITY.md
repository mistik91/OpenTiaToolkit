# Security Policy

## Supported release

During preview development, security fixes target the latest published preview. Stable-version support will be documented when a stable release ships. TIA Portal V21 is the only validated Siemens runtime profile until another compatibility profile is published.

## Reporting a vulnerability

Do not open a public issue for an unpatched vulnerability. Use the repository Security tab's **Report a vulnerability** action to submit a [private report](https://github.com/mistik91/OpenTiaToolkit/security/advisories/new). See GitHub's [private vulnerability reporting guidance](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository).

If the report action is unavailable, use your existing private maintainer contact or request one without including vulnerability or exploit details. The [maintainer profile](https://github.com/mistik91) identifies the account but is not itself a confidential reporting channel. In the private report, include the affected version, reproduction, impact and suggested mitigation. Avoid attaching proprietary TIA projects, credentials or Siemens binaries.

## Security boundaries

- CLI and MCP are vendor-free hosts. They cannot load Siemens assemblies or own a TIA session.
- Siemens Openness runs only inside `OpenTiaToolkit.Worker.V21.exe`, through one serialized request queue.
- MCP paths are allow-listed, size-limited, checked for reparse points, and protected against accidental overwrite.
- Mutation policy executes in the Application dispatcher before IPC. Mutations require preview or explicit confirmation and are written to a redacted JSON-lines audit journal.
- Passwords are referenced through environment-variable names and are never accepted as raw command values.
- A timed-out or cancelled MCP operation can terminate its worker process without disposing Siemens objects concurrently in the host.

Release artifacts include an SPDX SBOM, SHA-256 checksums, provenance, exact Siemens API compatibility hashes, and Authenticode signatures when a release signing certificate is supplied. Stable publication builds require a valid trusted signature. Explicitly labeled unsigned previews are permitted under [the release policy](docs/SUPPLY_CHAIN.md) and include an unsigned notice.
