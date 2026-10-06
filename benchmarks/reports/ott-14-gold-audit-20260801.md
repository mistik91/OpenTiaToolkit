# `ott-14` gold audit — 2026-08-01

## Outcome

The current `ott-14` fixture, task, and evaluator agree with live Siemens TIA
Portal behavior. A guarded dry-run that requested input start address `64` for
`DI_Candidate` was rejected because `DI_Occupied` already owns that range. TIA
reported `66` as the next free address. The operation was not applied, the
transaction reported rollback, and a fresh I/O export was byte-for-byte
identical to the baseline.

No prompt or rubric change is justified by this audit. In particular, the
existing output assertions for `already being used` and `next free address 66`
match the live response exactly.

## Reproduction identity

- Repository commit: `a658d6a53a6e9582a9e4d7e2e1b4fba8d604afeb`
- TIA Portal: V21 Update 2, product version `2100.0.121.1`
- Toolkit CLI SHA-256:
  `CCE60BE58F3EA015137B9BFDDED482F76720E5F9FF562615240676412FE87B2A`
- Fixture archive:
  `OTT_Benchmark_V21_20260731.zap21`
- Fixture size: `5,582,162` bytes
- Fixture state: read-only
- Fixture SHA-256 before and after:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Restored project:
  `OTT_Benchmark`
- Audit window: 2026-08-01 01:25–01:27 UTC

The archive was restored to a unique disposable directory. The project was
closed with `save=false`, and the benchmark-owned TIA process and disposable
directory were removed after verification.

## Baseline evidence

`get-hardware-io-addresses` returned two address objects and six rows:

| Item path | Address index | I/O type | Start | Length |
|---|---:|---|---:|---:|
| `DI_Occupied/DI_Occupied` | 0 | Input | 64 | 16 |
| `DI_Candidate/DI_Candidate` | 0 | Input | 2 | 16 |

The hardware tree independently confirmed the reusable target path
`PLC_Main` / `DI_Candidate` / `DI_Candidate`.

## Guarded dry-run

The single requested operation was:

```json
{
  "operations": [
    {
      "op": "set-io-address-start",
      "target": {
        "deviceName": "PLC_Main",
        "itemPath": ["DI_Candidate", "DI_Candidate"]
      },
      "addressIndex": 0,
      "startAddress": 64,
      "expectedStartAddress": 2
    }
  ]
}
```

It was submitted with `apply-hardware-patch --dry-run=true --confirm=true`.
The structured response contained:

```text
success: false
message: This address is already being used. Next free address: 66.
DryRun: true
Applied: false
RolledBack: true
SafetyBlocked: true
error.code: SiemensRejected
```

The CLI exit code was `1`, correctly reflecting the rejected request rather
than a successful patch.

## Independent no-mutation proof

After the rejected dry-run, the I/O addresses were exported again using the
same command and attributes. The two CSV files were identical:

```text
before SHA-256: 7DE124B7C2CB7F962CFE35BAC73268ADDACAE8FED6142E8F025B6081B6A230BD
after  SHA-256: 7DE124B7C2CB7F962CFE35BAC73268ADDACAE8FED6142E8F025B6081B6A230BD
Compare-Object: no differences
```

## Benchmark decision

Keep `ott-14` unchanged for the GPT-5.6 Sol qualification run. Prior model
failures on this case are agent-execution failures, not a mismatch between the
fixture, live SDK behavior, and evaluator. The task remains a useful test of
whether a model gathers enough hardware evidence, uses the required dry-run,
and reports the refusal honestly without applying a conflicting address.
