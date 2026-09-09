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

Both are real, both are recorded in the test, and neither is fixed yet.

### Known gap: no flow-depth floor on the gradeline

**What.** When a reach runs partly full, Hydraflow floors the HGL at its
upstream structure at the water surface the flow depth in that pipe implies. Its
gradeline jumps at that structure. `Hgl.SteadyBackwaterFromOutfall` steps the
chain continuously and never floors, so it comes out lower.

**Where it shows.** Run 2's 24-inch outfall pipe runs partly full, so Hydraflow
jumps from 758.16 to 758.52 at AI-8 and every structure above inherits the
0.36 ft. Run 1's outfall pipe is surcharged, so the two agree there.

**Why it matters.** This engine errs **low** on the HGL, which is the
unconservative direction. On a shallower system a 0.36 ft under-prediction is
the difference between reporting a flooded structure and not reporting one.

**Why it is not fixed here.** The floor is `invert + flow depth` at the upstream
node, and `NetworkReach` carries no invert elevation to floor against. Fixing it
means either adding inverts to `NetworkReach` or moving the floor up into
`NetworkAnalysisPipeline`, which has them. That is a deliberate change to a
shipped gradeline, not something to fold into an importer.

**How it is tracked.** `RefLine.LowerThanHydraflowByFt` records the 0.36 ft
exactly. The test subtracts it and still holds the shape to 0.15 ft, so the gap
cannot grow without failing.

### Known gap: sag inlet capacity takes a length, not a perimeter

`InletCapacity.SagCapacityCfs(grateLengthFt, flowDepthFt)` computes
`Cw · L · d^1.5`. HEC-22 Eq. 4-26 is `Cw · P · d^1.5`, where P is the grate
perimeter ignoring the side against the curb, so `2L + W` for a grate at a curb.

On the 4 ft × 4 ft grates in these files that is 4 ft where HEC-22 wants 12, so
the function returns about a third of the capacity, and the ponding depth it
implies is roughly twice what it should be. `Cw` itself is right at 3.27.

The direction is conservative: you would add inlets you do not need, not miss
flooding. That makes it a cost error rather than a safety one, which is why it
is documented rather than changed under a validation commit. Any change here
moves every existing inlet check in the add-in.

The reference test forms the perimeter explicitly and notes why.

## Running it

```
dotnet test tests/HydroComplete.Engine.Tests --filter "FullyQualifiedName~HydraflowReferenceTests"
```

19 tests, two runs each where the assertion is per-run.

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
