# Siemens SCL Patterns

- A source may declare multiple blocks and `TYPE` objects. Treat every declaration as an expected transaction target; snapshot existing types as SimaticML, canonical-export generated types, and reject duplicate identities before import.
- FCs have no retained static state. Use an FB for IEC timers, counters, edge detectors, and state retained between scans.
- Declare TON/TOF/TP, R_TRIG/F_TRIG, and CTU/CTD/CTUD instances in FB static data and call them cyclically.
- Guard division and normalization against zero ranges.
- Use explicit conversions when operand types differ.
- Bound every array index and avoid unbounded cyclic loops.
- Give `CASE` an `ELSE` branch and define safe outputs on every relevant path.
- Preserve optimized/non-optimized DB access required by HMI or communications.
- Inspect calls, UDT references, DB references, and library instructions before apply. The planner infers dependencies that match transaction target names and topologically orders operations. Declare non-call prerequisites with `depends-on`; combine mutually dependent declarations in one source or resolve the cycle explicitly.
- AWL/STL text and fail-safe block generation are not accepted through this standard SCL workflow.
