# The engine against Autodesk Hydraflow Storm Sewers

What this is: two real Civil 3D storm sewer runs, imported from the `.stm`
files a firm actually keeps, pushed through this engine, and compared line by
line against the report Hydraflow printed for the same files (run date
2026-08-16).

Every expected number in `tests/HydroComplete.Engine.Tests/HydraflowReferenceTests.cs`
came off a Hydraflow page. None of it is this engine's own output frozen as a
baseline, which is the failure mode that makes most "validation" suites worth
nothing: they only prove the code still does what it did last week.

Nothing here is a certification, and Autodesk has not reviewed it. The word for
what happened is **compared**.

## The project

A four-line trunk, in two design alternatives.

| | Run 1 | Run 2 |
|---|---|---|
| Fixture | `civil3d-2015-storm-sewers.stm` | `civil3d-2015-storm-sewers-run2.stm` |
| Pipe sizes, outfall up | 18, 18, 18, 12 in | 24, 18, 18, 12 in |
| Slopes | about 0.50 % | about 0.50 % |
| Storm | 10-year, Atlas 14 fitted | 10-year, same curve |
| Inlets | 4, all in a sag, 5-min inlet time | 4, same |
| Starting HGL | 757.365 | 757.62 as printed |

Coordinates were moved to a local origin. Every hydraulic value is untouched.

Run 2's file on disk carries a 758.618 starting HGL because it was re-saved
after the report was printed. The comparison uses the printed 757.62, and a
separate test reads the file's own value so the fixture cannot quietly drift.

## What matches

To the digit Hydraflow printed, on both runs:

- **Slopes.** Every line, within the two decimals the file stores inverts to.
- **Accumulated C·A.** Walked upstream through the network, not assumed to be a
  chain. 1.50, 1.09, 0.71, 0.38 acres on Run 1.
- **Intensity**, given Hydraflow's own Tc. Same IDF fit, same curve.
- **Rational flow**, given that intensity.
- **Full-flow capacity.** Both programs use 1.486 in Manning, so the only
  residual is the two-decimal inverts moving √S. Within 0.8 %.
- **Which lines surcharge.** The line of the report a reviewer acts on.
- **Which structure floods.** Exactly one across the two runs: AI-4 on Run 1,
  where the HGL lands 0.08 ft over a 759.00 rim. Reproducing that call depends
  entirely on the rims being imported correctly.
- **Inlet capture.** All four inlets on each run, within 0.02 cfs.
- **The gradeline**, within 0.15 ft at every structure, when fed Hydraflow's own
  flows and full-pipe hydraulics.

## Where the two disagree, and why

Three method choices, none of them a defect in either program. Each is written
down because the engineer sealing the sheet owns the choice and can only own a
choice that is written down.

### 1. Which velocity feeds travel time

Hydraflow times a drowned reach at its backwater depth, which is full-pipe on
this network. This engine uses normal depth, which is shallower and faster.

On Run 1's line 3: 2.99 ft/s and 0.99 min in Hydraflow, 4.86 ft/s and 0.60 min
here. The gap accumulates downstream, which is why the top two lines agree
exactly and the outfall does not. Tc at the outfall is 7.4 min against 6.9.

Consequence, if you carry it through the same IDF curve: 6.83 against 7.00
in/hr, on the same 1.497 acres of C·A, so 10.23 against 10.49 cfs. That is the
whole flow difference on this network, about 2.5 %.

Hydraflow's is the better physics for a drowned pipe. It costs an iteration the
textbook never mentions: Q → HGL → V → Tc → Q. Normal depth errs toward a
shorter Tc, a higher intensity and so a larger design flow, which is the side
you would rather be on when sizing.

Tolerance: 0.55 min on Tc.

### 2. Manning's constant

Both use 1.486. Where a program uses 1.49 instead, every capacity moves 0.3 %.
Worth stating because it is the most common reason two capacity tables differ
by a hair and everyone hunts for a real bug.

### 3. The terminal inlet

Both charge K·V²/2g at each junction with the project K of 0.5, except that
Hydraflow puts **K = 1.0 at the terminal inlet of a run** and treats it as an
entrance. On Run 1's line 4 that is 0.22 ft against 0.11.

The reference table carries Hydraflow's K per line, so the gradeline test
reproduces this rather than papering over it.

## Two gaps this found in the engine

Both are fixed.

### Fixed: the gradeline had no flow-depth floor

**What was wrong.** `Hgl.SteadyBackwaterFromOutfall` stepped the chain
continuously and never held the gradeline up to the water already standing in
the pipe. Arriving at a structure beneath the crown of a surcharged pipe above
it, it carried the low number straight on up.

**Where it showed.** Run 2's 24-inch outfall pipe runs partly full, so the
gradeline reaches AI-8 at 758.16, below the 758.52 crown of the surcharged
18-inch pipe above. Hydraflow holds it at 758.52. Every structure above
inherited the 0.36 ft. Run 1's outfall pipe is surcharged, so the two agreed
there and the fault stayed hidden.

**Why it mattered.** The error is **low**, which is the unconservative
direction: an under-predicted gradeline under-reports flooding. Run 1 shows how
thin that margin is. Hydraflow called AI-4 flooded because its gradeline landed
0.08 ft over a 759.00 rim. A 0.36 ft error is four times the number that decided
it.

**The rule, derived from both reports.** The water surface at either end of a
reach is at least `invert + depth`, where depth is the crown for a surcharged
reach and the normal depth for one running partly full. Checked against all six
reaches across the two runs, this floor binds in exactly the two places
Hydraflow's own gradeline jumps and nowhere else.

The one exception: it is never applied to the outfall reach's downstream end. In
Run 1 that pipe is surcharged with a crown at 757.50 while the tailwater is
757.37, and Hydraflow reports 757.37. A specified tailwater is a boundary
condition, not a number to override.

**The fix.** `NetworkReach` now carries optional `InvertUpFt` / `InvertDnFt`, and
`HglProfileOptions.EnforceFlowDepthFloor` applies the rule. It is **on by
default**, because a defect that only goes away when someone opts in is still
shipping, but it does nothing unless the caller supplies inverts, so existing
callers see byte-identical output. `ReachFactory` consumers get the inverts
wired through `NetworkAnalysisPipeline` and the Civil 3D reader, so `HC_HGL`
gets the corrected gradeline. Each raise is recorded in the calculation trace as
`floor_ds` or `floor_us` with its reason, so a jump in the profile is explained
rather than mysterious.

**How it is held.** Every `RefLine.LowerThanHydraflowByFt` is now zero and the
gradeline matches to 0.15 ft on both runs with no offset. `HglFlowDepthFloorTests`
covers the crown floor, the outfall exemption, the on-by-default contract, and
the guarantee that a reach without inverts is left exactly as it was.

### Fixed: sag inlet capacity took a length, and had no orifice branch

Two faults, pointing opposite ways, which is the worst arrangement because they
partly cancelled and the answer looked plausible.

**The perimeter.** `SagCapacityCfs` was fed the grate's LENGTH where HEC-22
Eq. 4-26 wants its perimeter: `Cw · P · d^1.5`, with P ignoring the side against
the curb, so `2L + W`. On the 4 ft × 4 ft grates in these files that is 4 ft
where HEC-22 wants 12, a factor of three low. `Cw` itself was right at 3.27.
That error is conservative and costs money, not safety.

**The orifice.** There was no orifice branch at all. A grate drowns as the water
deepens and then meters like an orifice, `Co · Ag · sqrt(2 g d)` with Co = 0.67.
Weir flow grows as d^1.5 and orifice flow only as d^0.5, so past about 1.4 ft of
ponding the weir form promises capacity the grate does not have. That error is
**not** conservative, and it bites at exactly the ponding depth where a sag inlet
stops being academic.

**The fix.** `SagGrateCapacityCfs(length, width, depth)` takes the lesser of the
two branches, which is the usual conservative reading of the band between them
and is continuous, so a design does not jump at the boundary. `SagCapacityCfs`
keeps its signature but its parameter is now named and documented as a
perimeter, so callers passing a length get exactly the number they got before
and the API stops lying about what it wants. `CapacityCfs` and `CheckInlet` take
an optional grate width: given one they use the full form, without one they fall
back to the old conservative answer and the trace says so in as many words.

`HC_INLETS` now asks for the grate width, and the calculation trace carries the
perimeter, both branches, and which one governed.

On AI-4, which captures 3.19 cfs at 100 %, the ponding goes from 0.41 ft under
the old formula to about 0.19 ft, against 2.07 ft of structure depth.

This moves every inlet check that supplies a grate width, which is the point.
Checks that do not supply one are untouched, so nothing changes underneath a
user who has not been asked for the extra number yet.

## Running it

```
dotnet test tests/HydroComplete.Engine.Tests --filter "FullyQualifiedName~HydraflowReferenceTests"
```

19 tests, two runs each where the assertion is per-run, plus
`HglFlowDepthFloorTests` for the grade-line floor and `SagInletCapacityTests`
for the sag grate.

The reader itself has its own suite, `StmReaderTests`, covering the ways the
`.stm` format silently corrupts a run if read naively: two different signature
lines, "Gutter N-Value" containing the substring "N-Value", Rise and Span being
feet in the Civil 3D format and inches in the standalone one, per-line end
inverts that are real drops through a structure, and an outfall whose rim is
written as zero.

## Adding to this

The comparison is only as good as the projects in it, and two runs of one trunk
is not many. If you have a stamped network and the Hydraflow or Stormwater
Studio report printed for it, that is exactly what this needs.
