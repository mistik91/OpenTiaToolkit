# Official V21 API insights

Phase 3 adds bounded, vendor-neutral reads over prioritized Siemens V21 APIs:

- `list-unified-alarms`, `get-unified-runtime-settings`, and `list-unified-text-lists`;
- `get-hardware-drive-parameters` for Startdrive values, units, ranges, arrays, and enum maps;
- `validate-safety-configuration` without accepting a password;
- `get-multiuser-status` and `inspect-project-security`;
- `get-watch-table-details`, `get-technology-object-details`, and `get-plc-alarm-text-list-details`.

These commands are read-only. Optional Siemens fields are fail-soft: one unavailable property is represented as missing rather than aborting the whole result. Large object families use `max-results` and report `totalCount` plus `truncated`; Startdrive is capped at 5,000 records.

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
commit-multiuser-session --comment <comment> --require-up-to-date true --require-no-conflicts true --confirm true
```

`create-multiuser-session` supports the same optional UMAC environment-variable credential parameters as `open-project` for protected projects. Project Server Windows permissions use the identity running TIA Portal/OpenTiaToolkit and cannot be impersonated with a command password. `commit-multiuser-session` saves first, blocks stale/conflicted sessions by default, closes the engineering-session view, opens the associated server-project view, calls `CloseAndCommit`, and returns the created server revision. Siemens V21 exposes an up-to-date check but no public refresh operation; recreate the agent session from the latest server revision when refresh is required.

`LocalSession.Close()` discards pending edits. Consequently `save-project` and `close-project --save=true` explicitly call `LocalSession.Save()` for local sessions; plain `close-project` continues to discard pending session edits.

Safety validation reports a protected-but-not-logged-on F-program as a warning. It never logs in or changes safety state. Use an existing credential-reference workflow only for a separately authorized mutation.
