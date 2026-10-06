# Official V21 API insights

Phase 3 adds bounded, vendor-neutral reads over prioritized Siemens V21 APIs:

- `list-unified-alarms`, `get-unified-runtime-settings`, and `list-unified-text-lists`;
- `get-hardware-drive-parameters` for Startdrive values, units, ranges, arrays, and enum maps;
- `validate-safety-configuration` without accepting a password;
- `get-multiuser-status` and `inspect-project-security`;
- `get-watch-table-details`, `get-technology-object-details`, and `get-plc-alarm-text-list-details`.

These insight commands are read-only. In particular, `list-unified-alarms` retains its legacy shallow result across alarm classes, analog alarms, discrete alarms, and OPC UA alarm types. Optional Siemens fields are fail-soft: one unavailable property is represented as missing rather than aborting the whole result. Large object families use `max-results` and report `totalCount` plus `truncated`; Startdrive is capped at 5,000 records.

## Unified discrete-alarm engineering

The dedicated WinCC Unified discrete-alarm workflow is separate from the read-only insight service:

- `list-unified-discrete-alarms` returns complete canonical snapshots, localized `EventText`/`EventText1` through `EventText9`/`InfoText`, ordered parameter tags, Siemens validation diagnostics on request, and deterministic content hashes.
- `get-unified-discrete-alarm` resolves one exact, case-sensitive alarm name.
- `apply-unified-discrete-alarms` consumes a versioned JSON document and creates or patches one or more alarms atomically. Omitted fields are preserved. Cultures are patched independently and must already be configured in the HMI project.
- `delete-unified-discrete-alarm` requires the current canonical hash, resolves exactly one name, reports cross references, and blocks referenced deletion by default.

Apply defaults to `dry-run=true`. A persistent apply requires `dry-run=false`, `apply=true`, and `confirm=true`; deletion requires `dry-run=false` and `confirm=true`. Dry-runs execute the same Siemens setters and `Validate()` calls inside a real transaction, then roll back and fresh-read the alarm inventory. Successful applies change only the open engineering project and do not implicitly save, compile, download, or deploy it.

The workflow requires a WinCC Unified engineering license. A missing license is reported as `unified-license-required`, rather than as a not-found result. This surface configures engineering alarms; it does not acknowledge or generate runtime alarms. Analog alarms, OPC UA alarm types, alarm-class CRUD, logging archives, and PLC alarms remain outside this workflow.

Example input:

```json
{
  "schemaVersion": "1.0",
  "alarms": [
    {
      "name": "Motor_Overload",
      "expectedState": "absent",
      "id": 1001,
      "alarmClass": "Error",
      "priority": 10,
      "raisedStateTag": "Motor1_Status",
      "raisedStateTagBitNumber": 3,
      "triggerMode": "OnRisingEdge",
      "alarmParameterTags": ["Motor1_Speed"],
      "texts": {
        "en-US": { "eventText": "Motor overload", "infoText": "Check motor current and cooling." },
        "it-IT": { "eventText": "Sovraccarico motore", "infoText": "Controllare corrente e raffreddamento." }
      }
    }
  ]
}
```

## Multiuser collaboration workflow

OpenTiaToolkit exposes the explicit V21 Project Server lifecycle while ordinary PLC, HMI, and project commands remain usable after a local session is selected:

```text
list-multiuser-servers
ensure-multiuser-server --server-alias <alias> --protocol Https --host <host> --port <port> --confirm true
list-multiuser-projects --server-alias <alias>
add-project-to-multiuser-server --server-alias <alias> --project-path <closed-project.ap21> --confirm true
list-multiuser-sessions --server-alias <alias> --project-name <project>
create-multiuser-session --server-alias <alias> --project-name <project> --session-name <agent-session> --session-directory <path> --mode Multiuser --confirm true
open-project --project-path <session.als21>
get-multiuser-session-status
save-multiuser-session --confirm true
close-project --save true --confirm true
```

`create-multiuser-session` supports the same optional UMAC environment-variable credential parameters as `open-project` for protected projects. Project Server Windows permissions use the identity running TIA Portal/OpenTiaToolkit and cannot be impersonated with a command password. A practical same-machine setup runs a second TIA instance and worker under a dedicated Agent Windows account, with local sessions stored under that account's profile. Wrong-account instance discovery/attachment is rejected by Siemens.

TIA Portal V21 public Openness exposes local-session save, close, freshness, and markings, but no local-session check-in operation. Siemens documents `LocalSession.CloseAndCommit()` as committing changes made in an opened server-project view; live device-rich testing confirmed that using it after closing a local session creates a revision without the local marked changes. `commit-multiuser-session` therefore fails safely and directs callers to save locally, hand off check-in to the session owner's TIA UI, create a fresh session, and verify the exact changed objects. Never treat a returned revision number alone as persistence proof.

The UI handoff is deliberately outside OpenTiaToolkit's supported API surface. It can be performed manually or automated by a user-selected agent harness with computer-use/UI automation. Such automation remains responsible for focus, dialogs, localization, timeouts, and security confirmations; it must not be described as Siemens Openness check-in support. Regardless of the harness, a fresh local session and exact artifact inspection remain the persistence proof.

Siemens V21 also exposes an up-to-date check but no public refresh operation; recreate the agent session from the latest server revision when refresh is required. A fresh Run As TIA process may require the user to confirm the Project Server certificate in the Agent-owned window. Repeated local-session create/open cycles can leave disposed Siemens session objects cached in a long-lived TIA process; restart the empty Agent TIA instance and create a fresh session rather than retrying the disposed object.

`LocalSession.Close()` discards pending edits. Consequently `save-project` and `close-project --save=true` explicitly call `LocalSession.Save()` for local sessions; plain `close-project` continues to discard pending session edits.

Safety validation reports a protected-but-not-logged-on F-program as a warning. It never logs in or changes safety state. Use an existing credential-reference workflow only for a separately authorized mutation.
