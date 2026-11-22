# AutomationML configuration

The AutomationML commands read CAEX files exported using AutomationML AR APC. They cover PLC hardware and its slot and subslot structure, communications interfaces, subnet membership, port connections, and PLC channels and tags.

## CLI and MCP

Run the project commands in a single CLI session, or use the tools with the same names in MCP. Select the target TIA Portal project before running `apply-automationml`.

```text
inspect-automationml --source-path="C:\Engineering\Example.aml"
apply-automationml --source-path="C:\Engineering\Example.aml"
apply-automationml --source-path="C:\Engineering\Example.aml" --dry-run=false --confirm=true
```

`inspect-automationml` works offline and accepts either `.aml` or `.xml` content. It returns the normalized objects, links, and parser diagnostics. The default `apply-automationml` preview compares the source with a CAx export of the selected project. Its plan lists missing objects, missing links, and differences in source-specified settings such as catalog identifiers and PLC addresses. TIA-generated defaults and additional project objects can coexist with the requested configuration.

Apply uses the TIA V21 CAx API on an empty standalone project. The installed hardware catalog, device support and licenses determine which source objects TIA can import. CAx runs outside the Openness transaction mechanism and may import only part of a file. The command exports the resulting configuration and reports remaining differences before you save. Once the comparison matches, a repeated apply returns without importing. Existing projects use the additive workflow below.

The comparison follows the owning station, module, and interface when TIA changes the CAx layout. It can match channels and addresses under an integrated submodule, recognize default tag table assignment, and resolve legacy port aliases when the topology is unambiguous.

TIA can export some catalog order numbers as masks. If a mask covers the requested number, the plan includes both values in `UnverifiedCatalogIdentifiers`. `Matches` means the comparison found no changes to apply. `FullyVerified` also requires readback at the requested catalog specificity. [Siemens AML type identifiers](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-hardware-data/aml-type-identifiers)

## Additions to an existing project

Use the same preview and apply commands against a standalone project. The `additive` section lists planned stations, modules, network membership, and PLC tags. `conflicts` lists mismatches that require review, including occupied slots, conflicting catalog items, duplicate tag names, and overlapping addresses. If AML leaves firmware unspecified, the preview shows the installed catalog identifier selected for the write.

Module I/O address conflicts are checked against the owning PLC. CAx I/O-system connections establish ownership of remote stations. The same address can be used on different PLCs when ownership is clear. An overlap remains a conflict when ownership is unknown or ambiguous.

This workflow can add catalog ET 200SP or ET 200MP remote I/O stations with one PROFINET interface and rack-mounted modules. The station joins an existing subnet and PLC I/O system. Tags can be added to existing PLC tag tables. New subnets, physical port links, explicit non-default submodules, fail-safe and custom station parameters, replacements, deletions, and changes to existing settings require review.

Apply takes a fresh snapshot under exclusive access and writes the additions in one Openness transaction. It checks the requested configuration and preservation of the existing CAx configuration before commit. A failed write or verification triggers rollback. Review the returned rollback verification before retrying.

Generate the synthetic acceptance fixtures after a Release build using Windows PowerShell 5.1. The generator starts its own TIA V21 instance and creates a read-only `Baseline.zap21` and `Desired.aml` in an empty output directory. The V21 installation needs the catalog devices used by the selected scenario.

```powershell
powershell.exe -NoProfile -File .\scripts\New-AutomationMlAcceptanceFixtures.ps1 -Scenario SinglePlc -OutputDirectory C:\Temp\aml-single
powershell.exe -NoProfile -File .\scripts\New-AutomationMlAcceptanceFixtures.ps1 -Scenario MultiPlc -OutputDirectory C:\Temp\aml-multi
```

The opt-in test `AutomationMlAdditiveAcceptanceTests` uses a read-only V21 archive from `OPEN_TIA_AUTOMATIONML_BASELINE` and the desired CAx AML from `OPEN_TIA_AUTOMATIONML_ADDITIVE_FIXTURE`. The acceptance data has a PLC station named `SeedController`, an existing tag, and a desired remote station with an input module named `RemoteInputs` and one new PLC tag. Set `OPEN_TIA_ACCEPTANCE_WORKER_EXE` to the V21 worker. The test retrieves separate project copies for apply and rollback.

The multi-PLC acceptance case uses `OPEN_TIA_AUTOMATIONML_MULTIPLC_BASELINE` and `OPEN_TIA_AUTOMATIONML_MULTIPLC_FIXTURE`. Its baseline adds `CompanionController` and `CompanionRemote` to the single-PLC fixture. The companion station and the proposed ET 200MP station both use input byte 20. Each belongs to a different PLC and I/O system. The test also moves the proposed station onto the companion I/O system in a copy of the AML to verify that the overlap is blocked.

## Public fixture

Siemens application example 109748224 documents the EPLAN export workflow and names the companion project archive. Use the **EPLAN-to-TIA** AML from that archive as the public acceptance input. The integration test takes the fixture from an external path through `OPEN_TIA_AUTOMATIONML_FIXTURE` and uses a disposable project. Set `OPEN_TIA_ACCEPTANCE_WORKER_EXE` to the V21 worker executable and run the `AutomationMlAcceptanceTests` test class. [Siemens application example 109748224](https://cache.industry.siemens.com/dl/files/224/109748224/att_988353/v2/109748224_TST_to_EPLAN_to_TIA_Portal_v20_en.pdf)

For the offline regression test, also set `OPEN_TIA_AUTOMATIONML_READBACK` to a V21 CAx export of the imported sample and run `AutomationMlReadbackTests`. It compares the pair and checks that changes to a tag address, module slot, and port link still appear in the plan.
