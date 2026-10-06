# Third-Party Notices

OpenTiaToolkit depends on third-party software and platform APIs. This file summarizes the major dependencies and redistribution notes for beta testers and future public users.

## Siemens TIA Portal Openness

OpenTiaToolkit integrates with Siemens TIA Portal through the Siemens Openness API.

- Siemens TIA Portal and Siemens Openness assemblies are not included in this repository.
- Users must install and license TIA Portal separately.
- The project references the default TIA Portal V21 PublicAPI path, with an override documented in `docs/SETUP.md` and `docs/TIA_PORTAL_VERSION_SUPPORT.md`.
- Official Siemens API manuals and documentation are not redistributed here. Refer to Siemens documentation for authoritative API details.

## Redistributed .NET and NuGet Dependencies

NuGet package dependencies are declared in the project files under `src/` and `tests/`. Exact direct and transitive versions are locked in each `packages.lock.json`; release SBOM and checksum files are generated from the staged payload.

Major packages include:

- `ModelContextProtocol` and `ModelContextProtocol.Core` 2.0.0, from the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), Apache License 2.0.
- Microsoft.Extensions 10.0.10 components, including dependency injection, hosting, logging, caching, configuration, and options, MIT License.
- `System.Text.Json` 10.0.10 and related `System.*` compatibility packages, MIT License.
- `Microsoft.Extensions.AI.Abstractions` 10.8.3, MIT License.

Test-only dependencies include MSTest, Moq, Microsoft.NET.Test.Sdk, and coverlet.collector. They are not copied into the product payload.

This summary is not a replacement for the license metadata shipped by each dependency. Review the locked dependency graph, generated SBOM, and package metadata before redistributing binary builds.

## Codex Plugin Assets

The `plugins/opentia-toolkit` package contains OpenTiaToolkit-authored configuration, docs, and skill guidance for Codex. It does not bundle Siemens binaries or TIA Portal project files.
