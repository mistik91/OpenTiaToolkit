# Third-Party Notices

OpenTiaToolkit depends on third-party software and platform APIs. This file summarizes the major dependencies and redistribution notes for beta testers and future public users.

## Siemens TIA Portal Openness

OpenTiaToolkit integrates with Siemens TIA Portal through the Siemens Openness API.

- Siemens TIA Portal and Siemens Openness assemblies are not included in this repository.
- Users must install and license TIA Portal separately.
- The project references the default TIA Portal V21 PublicAPI path, with an override documented in `docs/SETUP.md` and `docs/TIA_PORTAL_VERSION_SUPPORT.md`.
- Official Siemens API manuals and documentation are not redistributed here. Refer to Siemens documentation for authoritative API details.

## NuGet Dependencies

NuGet package dependencies are declared in the project files under `src/` and `tests/`.

Major packages include:

- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Logging`
- `ModelContextProtocol`
- `MSTest`
- `Moq`
- `System.Text.Json`

Each package is distributed under its own license. Review package metadata on NuGet before redistributing binary builds.

## Codex Plugin Assets

The `plugins/opentia-toolkit` package contains OpenTiaToolkit-authored configuration, docs, and skill guidance for Codex. It does not bundle Siemens binaries or TIA Portal project files.
