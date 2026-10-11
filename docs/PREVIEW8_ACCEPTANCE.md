# Preview.8 qualification and known limitations

Reviewed 10 October 2026. **The planned scoped first-install/PLC, Unified screen editing/compile and Startdrive acceptance gates have passed.** The repository remains private and the release draft pending publication authorization and final publication checks. This qualifies an unsigned preview for the workflows below; it does not establish exhaustive support for all exposed commands.

## Exact accepted candidate

| Item | Identity |
|---|---|
| Version | `1.0.0-preview.8` |
| Binary source | `7e8c5820ea5df7beb9afc4225575bf064af30cce` |
| Installer | `OpenTiaToolkit-1.0.0-preview.8-Setup.exe` |
| Installer SHA-256 | `489efe254f935bf3782eb3a70f15fab9583911c4f8fcb59121b75ac943065d16` |
| Worker SHA-256 | `3314a22e5a989b1052e6529b2acf5d35befaee6da3f98b5113269f66c0e2fc63` |
| Worker Core.dll SHA-256 | `0b01d67897731cf035ff9e62b0c29ce160a97c8d266581514eba5b6ff67697e2` |

Obtain checksums, the unsigned notice and sanitized `VALIDATION_PREVIEW8.md` from the [release assets](https://github.com/mistik91/OpenTiaToolkit/releases) when published, or with authorized private access during evaluation. SHA-256 establishes identity against the published checksum; the unsigned candidate has no Authenticode publisher signature.

Documentation follow-up commits supplement the accepted packages. They do not change the binary source, release target or artifact hashes. The installer, source archive and plugin bundles contain the earlier documentation snapshot; use the current repository guidance and release validation record for the completed acceptance status. Rebuilding or repacking produces another candidate requiring a new identity and applicable retesting.

## Environments and workflow scope

TIA Portal **V21 is the only supported major version**. Local staged validation used Update 2. Independent installed acceptance used Update 2 Hotfix 1 beside a development installation, and a separate first-use PC on Update 3. First-use eligibility combines human no-prior-use attestation with bounded pre-install checks; it is not a claim of forensic machine history or a fresh Windows VM image. Installed machines ran Windows 10 Pro x64 with .NET Framework 4.8.

| Workflow | Scoped result | Boundary |
|---|---|---|
| First installation and upgrade | Passed exact installer/payload identity, worker registration, sibling discovery, doctor and client integration | Separate first-use PC plus upgrade PC; SDK/source compilation was not needed |
| Uninstall/reinstall | Passed normal lifecycle and restored identity | Complete immediate root-directory removal was not observed on the first-use PC; shared registration/client retention limits below |
| MCP and portable client | Passed raw modern/legacy protocol execution, 189-tool backend discovery and actual portable desktop doctor | Codex was exercised, including 0.162.0-alpha.2; other client packages are not installed-client qualification |
| CPU and PLC software | Passed catalog-backed CPU 1214C creation preview/identical apply, fresh reads, zero-error/zero-warning PLC compile, guarded inverse and saved independent reopen | `OrderNumber:6ES7 214-1AG40-0XB0/V4.6`; installed Update 3 CPU smoke plus Update 2 Hotfix 1 tag/compile smoke |
| Unified provisioning | Passed repeated isolated device-creation previews, identical same-session apply and independent saved reopen | MTP700 Unified Comfort, `OrderNumber:6AV2 128-3GB06-0AXx/21.0.1.0`, on Update 2 Hotfix 1 |
| Unified screen/item editing | Passed button creation, text/geometry, duplicate/rename, Bool Visible binding, Tapped handler, save/reopen and inverse restoration | Same panel and Update 2 Hotfix 1; human internal-tag/start-screen preparation and normal TIA HMI software compilation |
| Startdrive Main telegram | Passed two rollback previews, identical apply, saved reopen, guarded inverse and saved baseline reopen | G120C PN configured firmware 4.7.14, measured Startdrive V21 Update 1; Main 352 → 350 → 352, input/output starts 1020 → 15000 → 1020 |
| Prospective instance isolation | One controlled cycle preserved an independent reference root/project at all 14 checkpoints through the other session's reads, human compile and lifecycle/cleanup | Both instances MCP-owned; human-opened reference and repeated cycles were not tested |

The Update 3 CPU result does not qualify that installation's changed WinCC Unified binary for screen editing. Startdrive qualification applies to the measured assembly SHA-256 `981042f3e233077dc06d584b05a3b88be63823ddd7a4152427ef44fd56d63cc8`, not every V21 Startdrive update or drive family.

The exact source also passed a Release build with zero warnings/errors, 795 unit, 54 architecture, 7 worker IPC and 1 licensed Unified regression tests. Hosted vendor-free CI passed; the hosted licensed job was skipped. Local and independent installed campaigns are separate evidence, not substitutes for one another. The maintainer audited supplied independent evidence read-only; no remote-machine re-execution is claimed.

## Evidence and operational limits

### Licensing and compatible binary drift

Install the legitimate Siemens components and engineering licenses for your selected workflow. TIA installation, Openness access and a healthy doctor do not establish licensing readiness: an initially license-blocked CPU attempt only passed after the environment prerequisite was resolved. No license purchase, activation or transfer is performed by the toolkit.

Doctor preserves the committed baseline and reports different Siemens binary hashes as visible warnings when public APIs remain compatible. The tested Update 2 Hotfix 1 installation had Startdrive drift; the Update 3 installation also had Unified drift. These scoped live results supplement the baseline without replacing its hashes. Missing assemblies/profiles, architecture failures and breaking APIs remain diagnostic failures. Validate affected workflows after further Siemens updates; see [API compatibility](API_COMPATIBILITY.md).

### Compiler and IO coverage

`compile-software` is PLC-only in preview.8. Unified software compilation was performed by the human in normal TIA UI. Edited and independently reopened HMI compiler screenshots show zero errors/warnings; baseline capture lacks a timestamp. Historical cleanup compilation is human attestation, supplemented by a later full zero-error/zero-warning diagnostic transcript without a compiler-pane image. Script compilation does not establish runtime execution. A real mid-batch HMI apply failure was not qualified; safety-preflight denials are separate negative controls.

Startdrive baseline, edited and restored controller hardware compile transcripts show zero errors and the **same 14 existing warnings**. The owner confirmed drive coverage; a dedicated drive compiler result was not independently captured. All 15 other telegram records, including safety, were preserved in this test. Native address lengths changed from 96 to 64 bits and back with the number change. Exposed address ranges were reviewed, but 14 connected interfaces were absent from the project-wide IO inventory: no blanket collision or application-code consistency certification is claimed.

### Process ownership and recovery

Historical protected user TIA process departures remain unresolved. Some occurred before engineering calls or between retries; the evidence does not establish Toolkit causation or complete process preservation. The prospective isolation cycle reproduced no cross-instance loss and supports only its observed boundaries. Human-confirmed closure in that new cycle does not explain the earlier disappearances.

API project/portal disposal can succeed while an owned GUI root remains. Test cleanup used exact PID, executable path and creation-time checks after closing disposable projects. Users should close remaining windows normally; never perform executable-name-wide termination or act on an instance whose ownership is uncertain. Session-loss reads fail explicitly with recovery guidance. Verify the intended instance/project after explicit reconnect or reopen before writing; do not replay a timed-out mutation blindly.

### Installer and client lifecycle

Uninstall removes product payload and machine OpenTia environment settings within the observed lifecycle. Exact worker Siemens registrations and client-managed settings/marketplace/cache can remain. The first-use test observed an empty worker directory and uninstaller immediately before reinstall, so complete immediate directory cleanup is unproven. Exact toolkit registrations were checked; retention of every unrelated Siemens registration lacks a complete before-image. Do not delete shared registrations or client state in bulk.

An upgrade can close the installed MCP/worker and leave an existing client transport closed; one normal client restart restored actual portable doctor execution. An already-running shell can retain old environment values. Explicit worker overrides take precedence over installed sibling discovery; check the actual path reported by doctor. Some desktop chats exposed 79 wrappers despite 189 backend tools; version/operation-status desktop wrappers were unavailable there. This does not establish a server defect or a promise that another restart will expose them.

### Archives and private evidence

MCP file parameters default to Documents, Desktop and `%TEMP%\OpenTiaToolkit` roots, with a 16 MiB existing-input limit. The Startdrive source archive exceeded this policy and was retrieved by its owner through normal TIA UI into a disposable copy. No policy bypass, original-project selection or automatic save was used. See [setup](SETUP.md#archive-and-file-access-policy) before supplying large archives.

Raw colleague ZIPs, screenshots, registry/process metadata and project copies remain private. Release records contain sanitized findings and hashes. Accessible MCP host stderr and returned worker results were retained; independently captured worker-only stderr is not claimed.

## Available features beyond this acceptance

The [command reference](generated/COMMAND_REFERENCE.md) describes the exposed API. Earlier development validation is documented elsewhere; it does not turn every feature into independently installed preview.8 qualification. Alarms, trend logging, faceplates, multilingual edge cases, broader PLC/code/recovery suites, VCI/libraries, Multiuser, simulator/runtime behavior and physical downloads are outside these scoped acceptance campaigns.

For Startdrive, commissioning parameters, insertion/erasure/size edits, safety telegram writes, other drive families/updates, online firmware, rewiring and motor/tuning behavior remain unqualified. Discover actual target capabilities and confirm target-supported alternatives through the owner's UI or Siemens documentation; see [hardware guidance](HARDWARE_AUTOMATION.md). Start on a disposable fixture and qualify the required domain before relying on it.

Publication requires the reviewed default-branch documentation, explicit authorization, the security-reporting route and public download/link checks in the [release checklist](RELEASE_CHECKLIST.md). No additional installer, CPU or scoped drive engineering repeat is required by these completed results alone.
