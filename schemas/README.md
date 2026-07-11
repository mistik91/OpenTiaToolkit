# Published schemas

`unified-hmi-json-1.0.schema.json` describes the stable top-level, screen, item, action, and property-map contract used by Unified HMI JSON export/diff/import workflows.

Runtime loading additionally uses strict `System.Text.Json` unmapped-member rejection for every nested DTO and semantic validation for schema version, document kind, actions, duplicate screen paths, duplicate item names, and required identities. The runtime validator is authoritative when the schema and implementation disagree.
