TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Create two new reusable PLC objects in the root groups of `PLC_Main` from one native SCL source:

- PLC data type `OTT30_RecipeWindow` with `Target` and `Tolerance` arrays of four `Real` values indexed `0..3`, an `Enabled` array of four `Bool` values indexed `0..3`, and `HoldTime : Time`.
- function block `OTT30_RecipeWindowMonitor` with inputs `Start : Bool`, `Reset : Bool`, `Actual : Array[0..3] of Real`, and `Recipe : OTT30_RecipeWindow`; outputs `Active : Bool`, `Ready : Bool`, `FirstOutsideIndex : Int`, and `State : USInt`.

Implement a bounded three-state monitor. State 0 is idle, state 1 is monitoring, and state 2 is ready. A rising edge of `Start` enters monitoring. `Reset` has priority and returns to idle. Disabled channels are ignored. Treat a negative tolerance as its magnitude. On every scan, `FirstOutsideIndex` must be the lowest enabled channel outside its inclusive target window, or `-1` if none is outside. Readiness requires at least one enabled channel and every enabled channel to remain continuously inside tolerance for `Recipe.HoldTime`; any loss of that condition while ready returns to monitoring. An unexpected state must fail safely to idle. Drive `Active`, `Ready`, and `State` from the final state.

Use a static `R_TRIG` instance for the start edge and a static `TON_TIME` instance for the dwell time; neither may be temporary. Call both instances every scan. Use a bounded `FOR` loop for the four channels and an explicit `CASE` with an `ELSE` safe default.

Discover the exact live code capability for each intended object before authoring and follow the selected route. Write the complete source to `artifacts/OTT30_RecipeWindowMonitor.scl` in the benchmark workspace as UTF-8 with BOM, validate it as SCL, and require a verified offline classification with no errors or warnings before mutation.

Bind both fresh Create capability IDs to one `CleanCompile` PLC code transaction. Add exactly one SCL-source operation for the complete file, plan the transaction once, review that both exact missing targets are safe creates, and apply once with the one-use token and explicit confirmation. Do not bypass the transaction with lower-level source commands or use a probe, SimaticML, SIMATIC SD, VCI, or a promoted-production transaction.

After apply, independently verify the FB identity and SCL language, export the FB as source and the PLC type as XML into `artifacts/readback`, rediscover both exact targets as existing Modify targets on `DirectScl`, and run a fresh clean PLC compile. Do not create an instance DB, modify an OB, save the project, or delete/discard the successful transaction. Summarize the exact objects, route, transaction state, canonical verification, and final compile counts.
