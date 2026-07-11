# Official V21 API insights

Phase 3 adds bounded, vendor-neutral reads over prioritized Siemens V21 APIs:

- `list-unified-alarms`, `get-unified-runtime-settings`, and `list-unified-text-lists`;
- `get-hardware-drive-parameters` for Startdrive values, units, ranges, arrays, and enum maps;
- `validate-safety-configuration` without accepting a password;
- `get-multiuser-status` and `inspect-project-security`;
- `get-watch-table-details`, `get-technology-object-details`, and `get-plc-alarm-text-list-details`.

These commands are read-only. Optional Siemens fields are fail-soft: one unavailable property is represented as missing rather than aborting the whole result. Large object families use `max-results` and report `totalCount` plus `truncated`; Startdrive is capped at 5,000 records.

Safety validation reports a protected-but-not-logged-on F-program as a warning. It never logs in or changes safety state. Use an existing credential-reference workflow only for a separately authorized mutation.
