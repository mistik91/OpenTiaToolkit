# OpenTiaToolkit — Feature Suggestions / Gap Analysis

Date: 2026-06-12
Companion to: [report.md](report.md) (full repository review)

Scope: features that were **absent** from the command surface at the time of review. Every Siemens API referenced below was verified present in the V21 PublicAPI assemblies the project already links against (`Siemens.Engineering.Base/Step7/WinCCUnified/Startdrive` 21.0.0.0, checked via reflection and the shipped IntelliSense XML docs). None of these require new Siemens dependencies.

Status update: Section 1 has been implemented. The toolkit now exposes PLC online compare, guarded PLCSIM Advanced software download, online status, and guarded CPU start/stop attempts. Live validation against PLCSIM Advanced succeeded with `download-software --only-changes=true`; full fail-safe software loads may still be rejected by Siemens Openness.

Priority legend:
- **P0** — closes a loop agents currently cannot close at all; highest leverage.
- **P1** — fills an asymmetry or unblocks a common real-world workflow.
- **P2** — valuable, but defer until test coverage and the report.md P0/P1 fixes land.

---

## 1. Verification Loop Closure (P0) - Implemented

The toolkit can edit, compile, compare online state, and download software changes to PLCSIM Advanced. This closes the first guarded verification loop for agent workflows: edit -> compile -> download to sim -> observe/compare.

### 1.1 Download to PLCSIM / device
- **API**: `Siemens.Engineering.Download` namespace (Base, Step7, Startdrive XMLs all document it), `Siemens.Engineering.Online` for go-online and CPU state.
- **Implemented**: guarded `download-software` is restricted to simulation targets (`--to-simulator`, PLCSIM Advanced) with `--dry-run` and `--confirm` gates matching the existing mutation-command conventions. Real-hardware download remains blocked.
- **Payoff**: enables the canonical agent loop — edit → compile → download to sim → observe — instead of stopping at compile.
- **Fail-safe note**: use `--only-changes=true` for fail-safe projects when possible. Siemens Openness may reject full fail-safe software loads even when PLCSIM Advanced is configured and reachable.
- **Follow-ups implemented**: `start-cpu`, `stop-cpu`, and `get-online-status`. Some TIA Portal online providers expose connection state but no CPU RUN/STOP operation; those commands return structured not-supported results in that case.

### 1.2 PLC compare
- **API**: `Siemens.Engineering.Compare` (offline/offline and online/offline), present in Base + Step7.
- **Implemented**: `compare-software` supports online/offline compare and returns a structured diff, with `--output-path` artifact spill like the existing large-result commands.
- **Rationale**: the HMI side already has a full export/diff/import pipeline — the diff discipline is clearly understood and is the best-engineered part of the repo. The PLC side has nothing equivalent; an agent that mutates blocks cannot show "what changed" except indirectly through VCI text. This is the most glaring asymmetry in the product.

---

## 2. Library Workflows (P0)

- **API**: `TiaPortal.GlobalLibraries` (`GlobalLibraryComposition`) is already exposed on the connected portal object (verified by reflection); `MasterCopy` types exist in Base/Step7; project library is reachable via `Project`.
- **Current state**: zero commands touch libraries.
- **Suggestion**:
  - `open-global-library` / `list-global-libraries` / `close-global-library`
  - `list-master-copies` / `copy-from-master-copy` (into a target block group)
  - `list-library-types` / `get-library-type-versions`, and an update-instance check (report outdated instances before mutating)
- **Rationale**: in real TIA shops, standardization via company libraries *is* the workflow. A toolkit that can create blocks from scratch but cannot pull from the validated library fights the user's process and encourages agents to re-invent blocks that already exist in approved form.

---

## 3. PLC Surface Asymmetries (P1)

### 3.1 `export-tags`
`update-tags-from-csv` imports, `find-replace-tags` mutates — but there is no tag *export*. Agents must scrape list-command output to round-trip. Every import path should have its export twin; this one is cheap (the CSV writer infrastructure in `CommandArtifactSupport` already exists).

### 3.2 External source files (SCL/AWL text round-trip)
- **API**: `PlcSoftware.ExternalSourceGroup` (`PlcExternalSourceSystemGroup`, typed, verified present).
- **Suggestion**: `import-source` (add .scl/.awl file), `generate-blocks-from-source`, and `export-block-as-source` where supported.
- **Rationale**: plain-text source is the most LLM-friendly code path there is. VCI/S7DCL partially covers this but only through workspace ceremony (create workspace → connect block → sync); a direct source path is simpler, scriptable, and more portable to older portal versions.

### 3.3 Cross-references
- **API**: `CrossReference` service (Base).
- **Suggestion**: `get-cross-references --name <tag-or-block>` answering "who uses this?".
- **Rationale**: doubles as a safety feature — today `find-replace-tags`, `delete-blocks`, and the rename commands operate blind. Impact analysis before destructive operations fits the toolkit's existing dry-run/confirm philosophy, and the workflow skills could mandate an xref check before any rename/delete.

### 3.4 Watch & force tables
- **API**: `PlcWatchTable` etc. (Step7; `PlcSoftware.WatchAndForceTableGroup` is a typed property, verified).
- **Suggestion**: list/create/export watch tables. Becomes far more valuable once §1.1 (download/online) exists, since watch tables are the natural observation channel for the verification loop.

### 3.5 Technology objects
- **API**: `PlcSoftware.TechnologicalObjectGroup` (`TechnologicalInstanceDBGroup`, typed, verified).
- **Suggestion**: at minimum read/list TOs; later, axis parameter read/write.
- **Rationale**: the toolkit already configures Startdrive telegrams — so drives get configured, but the motion axes that reference those telegrams cannot even be enumerated. The hardware story stops one step short of usable.

### 3.6 PLC alarms & text lists
- **API**: `PlcAlarm`, `TextList` (Step7); `PlcSoftware.PlcAlarmTextlistGroup` is a typed property.
- **Suggestion**: list/export alarm text lists; pairs naturally with §5 (multi-language).

---

## 4. Project Lifecycle (P1)

Status update: Section 4 has been implemented. The toolkit now exposes `create-project`, `add-device`, `archive-project`, and `retrieve-project`, and `open-project` / `retrieve-project` accept optional UMAC credentials via `--umac-user` plus a password environment variable.

### 4.1 `create-project` / `add-device`
The toolkit assumes a project always exists; greenfield bootstrap is impossible. `apply-hardware-patch` already does the *harder* part (catalog lookup, module composition, plugging) — creating the project and the station from an order number (`Devices.CreateWithItem`) is the missing first step. One command each: `create-project --project-path`, `add-device --order-number --version --name`.

### 4.2 Archive / retrieve (`.zap21`)
- **API**: `Project.Archive` (verified in Base XML).
- **Suggestion**: `archive-project` / `retrieve-project`.
- **Rationale**: the docs themselves tell users to "use disposable project copies until you are comfortable" — the toolkit should be able to make those copies. Also the natural building block for an agent-side undo/backup convention: archive before mutation session, retrieve on failure.

### 4.3 Protected project credentials (UMAC)
- **API**: `UmacDelegate` (Base).
- **Current state**: `open-project` has no credential parameters, so UMAC-protected projects fail — and because of the swallowed-`bool` error pattern (report.md §5.4), they fail *undiagnosably* as a generic "Failed to open project".
- **Suggestion**: optional `--umac-user` plus a non-echoing credential mechanism (env var or stdin prompt; never a plain CLI arg in shell history). Even without full support, *detecting* a protection prompt and returning a specific error would remove a silent failure mode.

---

## 5. Internationalization (P1)

- **API**: `MultilingualText` (Base + WinCCUnified), project language settings.
- **Implemented**: `list-project-languages` exposes available, active, editing, and reference project languages. `export-texts` writes Siemens project text `.xlsx` workbooks with source/target cultures, required object scope, and optional category scope. `import-texts` imports the workbook with `--dry-run=true` by default so agents validate intent before mutating project texts.
- **Rationale**: translation export/import is a classic, tedious automation chore and an unusually good fit for LLM agents — high-volume, low-risk text transformation with a clean round-trip format.

---

## 6. Toolkit & Infrastructure Features (no Siemens API required)

### 6.1 CLI `--format json` (P0)
MCP clients get structured JSON; CLI users get reflection-built tables. A `--format json|table` flag would make the CLI scriptable and would let most of the 1,433-line `TableOutputFormatter` be deleted — directly shrinking the worst presentation-layer liability identified in report.md §4.2. This is the rare feature that *removes* code.

### 6.2 MCP `reset-session` tool + operation timeout (P0)
**Implemented**: MCP now exposes `reset-session`, `operation-status`, and `cancel-operation`. Tool calls run under a server operation context with timeout metadata, latest progress, and cooperative cancellation. `reset-session` cancels the active operation, releases the MCP gate, disposes the current TIA attachment, and clears `EngineeringSessionContext` without requiring the serialized Openness gate.

Ties to report.md §2: a hung Openness call currently bricks the MCP server permanently (gate held forever, no recovery path). Suggest: a watchdog timeout surfaced in the busy-error payload, plus a `reset-session` tool that disposes the portal attachment and clears `EngineeringSessionContext`. This is both a bug fix and a missing feature.

### 6.3 Session persistence for single-shot CLI (P2)
**Implemented**: CLI now supports `--script <file>|-`, executing REPL-shaped commands from a file or stdin in one process without the interactive banner or prompt. Blank lines and full-line `#` comments are skipped, `exit`/`quit` stop the script successfully, and execution stops with a non-zero exit code at the first parse or command failure. The model is now: REPL for human stateful sessions, MCP for agent stateful automation, script mode for shell/CI automation, and single-shot CLI for one-off stateless commands. A named-pipe daemon can stay a later option if users strongly need shell-native persistent sessions.

### 6.4 Progress reporting & cancellation (P1)
**Implemented**: The MCP bridge now creates a per-operation cancellation token and progress reporter, exposes latest progress through `operation-status` and busy payloads, and supports opt-in contextual commands through `IContextualCommand` / `CommandExecutionContext`. Existing commands remain source-compatible while long-running commands can incrementally adopt cooperative cancellation and richer progress.

Long compiles/imports/downloads give zero feedback until completion, and no `CancellationToken` exists anywhere in the interfaces (report.md §2). MCP progress notifications are supported by the SDK already in use. Do this together with the report.md interface changes rather than as a separate pass.

### 6.5 An actual installer (P1)
**Implemented**: The repo now includes an Inno Setup installer definition and `scripts\Build-Installer.ps1`. The installer stages Release MCP/CLI binaries, plugins, canonical skills, docs, MCP snippets, and `Register-OpennessAllowList.ps1`; installs to `C:\Program Files\OpenTiaToolkit`; sets `OPEN_TIA_MCP_EXE`; and offers setup tasks to register MCP/CLI executables with Siemens Openness. Docs now steer users to ask their agent to install/configure the local plugin from the installed `plugins\` folder instead of having the installer mutate every client config.

Installed-product snippets point at `C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe`, so the repo needs a real product install path. Keep the installer as the source of that stable location, and keep client-specific plugin/config setup agent-driven or manual through `docs\mcp-client-configs`.

### 6.6 Multi-version support — build it or drop the claim (P2)
README and `TIA_PORTAL_VERSION_SUPPORT.md` promise "best-effort compatibility paths for older versions", and the `V21*Adapter` naming implies a version seam — but only V21 assemblies are referenced and no abstraction exists for a V20 adapter to plug into. Either introduce the real seam (adapter interface + per-version assembly loading, which the custom `TiaPortalAssemblyResolver` half-anticipates with its V21/V20/V19 lists) or remove the claim from the docs. The current state is a promise with no mechanism.

---

## 7. Deliberately Not Suggested

- **Project documentation/printout export, Teamcenter gateway, AddIn APIs** — niche audiences, low agent value.
- **Real-hardware download as a default path** — too dangerous for the current maturity level; simulation targets first.
- **Further HMI breadth** — the Unified HMI surface is already the largest and best-covered area; PLC-side parity matters more.

The general posture: the command surface is already wide relative to its test coverage (21/36 service files untested, per report.md §7). Prefer the four loop-closing features — **download-to-PLCSIM (§1.1), PLC compare (§1.2), tag export (§3.1), cross-references (§3.3)** — over additional breadth, and land the report.md P0 fixes (concurrency, CI, error transparency) before or alongside any of this, since every new feature inherits those systemic weaknesses.

---

## Suggested Sequencing

| Order | Item | Why first |
|-------|------|-----------|
| 1 | CLI `--format json` (§6.1) | Removes code; unblocks scripting; zero Siemens risk |
| 2 | MCP reset/timeout (§6.2) | Converts a P0 reliability bug into a feature |
| 3 | `export-tags` (§3.1) | Trivial; closes an import/export asymmetry |
| 4 | Cross-references (§3.3) | Safety net for existing destructive commands |
| 5 | PLC compare (§1.2) | Read-only; mirrors proven HMI diff design |
| 6 | Download to PLCSIM (§1.1) | The big one; needs the guard/confirm design |
| 7 | Libraries (§2) | High workflow value; medium surface area |
| 8 | Archive/retrieve, create-project (§4) | Lifecycle completeness |
| 9 | External sources, watch tables, TOs, i18n (§3.2/3.4/3.5/§5) | Breadth, after the loops close |
