# Siemens SCL Patterns

- FCs have no retained static state. Use an FB for IEC timers, counters, edge detectors, and state retained between scans.
- Declare TON/TOF/TP, R_TRIG/F_TRIG, and CTU/CTD/CTUD instances in FB static data and call them cyclically.
- Guard division and normalization against zero ranges.
- Use explicit conversions when operand types differ.
- Bound every array index and avoid unbounded cyclic loops.
- Give `CASE` an `ELSE` branch and define safe outputs on every relevant path.
- Preserve optimized/non-optimized DB access required by HMI or communications.
