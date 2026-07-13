# Local validation prompt

Paste the block below into Claude Code (or run the steps yourself) from the repo root to
independently build and test the July 2026 engine audit on a real machine. Steps 1–3 run
anywhere with the .NET SDK; steps 4–5 need a Windows Civil 3D box and are skipped gracefully
otherwise. See `docs/HANDOFF.md` and `docs/AUDIT-2026-07.md` for context.

---

```
You're in the HydroComplete for Civil 3D repo. A deep engineering audit just landed
on `master` (PRs #1, #8, #9, #10) — 30 verified fixes to the stormwater calculation
engine plus a regression suite. It was validated in a sandbox that couldn't run the
Civil 3D plugin, so I need you to independently build and test everything on this
machine and confirm the results. Read `docs/HANDOFF.md` and `docs/AUDIT-2026-07.md`
first for full context.

Do the following and report back:

1. SYNC & BUILD ENGINE
   - `git checkout master && git pull`
   - Build the engine in Release; it must be 0 warnings / 0 errors:
     `dotnet build src/HydroComplete.Engine/HydroComplete.Engine.csproj -c Release`

2. RUN THE ENGINE TEST SUITE (this is the key check)
   - `dotnet test tests/HydroComplete.Engine.Tests/HydroComplete.Engine.Tests.csproj`
   - The tests target net7.0. If you only have the .NET 8 SDK, run with
     `DOTNET_ROLL_FORWARD=Major`.
   - EXPECTED: 420 passed, 0 failed, 1 skipped (the 1 skip is a preset generator,
     by design). If ANY test fails, stop and show me the failure name, the expected
     vs actual values, and the source of the assertion — do not "fix" it by loosening
     the test until we agree it's a legitimate re-baseline vs a real bug.

3. NUMERICALLY SANITY-CHECK THE TWO ALGORITHM-LEVEL CHANGES
   These changed computed outputs and deserve an independent look beyond the unit tests:
   - F9 — `src/HydroComplete.Engine/MuskingumCungeRouting.cs`: the routing was reworked
     to keep output on the input hydrograph's time axis with sub-stepping. Route a known
     inflow hydrograph through a typical reach and confirm: peak attenuates, peak lags
     (travel time > 0), volume is conserved (Σ outflow ≈ Σ inflow), and output timestamps
     equal the input Δt. Flag anything non-physical.
   - F4 — `src/HydroComplete.Engine/CulvertHydraulics.cs`: `Hc/D` is now computed from
     critical depth and capped at 1.0. Build a rating curve for a 24-in culvert and
     confirm headwater is monotonic in discharge, low-flow inlet HW is below one diameter,
     and there's no blow-up at over-capacity flow.

4. BUILD THE CIVIL 3D PLUGIN (only if this machine has Civil 3D 2024/2025/2026 installed)
   - `dotnet build src/HydroComplete.Civil3D/HydroComplete.Civil3D.csproj -c Release`
     (Net8AcadDir / Net48AcadDir must resolve to the installed AutoCAD/Civil 3D paths).
   - This is the one part the sandbox could NOT compile (no Autodesk managed refs), so it
     needs real verification. Report any compile errors — pay attention to the files
     touched by the audit: HydroCommands.cs, HglLabelWriter.cs, HglProfileWriter.cs
     (F26 HGL key change) and the F27 report try/catch.
   - If it builds, confirm `HC_ABOUT` lists 52 commands and, per
     `dist/app-store/VALIDATION_SESSION.md`, that the commands launch without unhandled
     exceptions.

5. PACKAGE & RUN THE APP-STORE PREFLIGHT GATE (Windows + Civil 3D box only)
   - Assemble the bundle: `scripts/release.ps1` (builds net8 + net48, produces
     dist/HydroComplete-1.7.2.zip).
   - Run the preflight gate: `scripts/app-store-preflight.ps1`. It checks bundle layout,
     version sync across manifest/csproj, the 52-command count, and the icon; it must
     exit 0.
   - If the code-signing certificate is installed, also run `scripts/sign-release.ps1`
     then `scripts/app-store-preflight.ps1 -RequireSigning` (must exit 0). If the cert
     isn't available yet, run preflight WITHOUT -RequireSigning and just note that signing
     is still pending — don't treat the missing cert as a failure.
   - Show me the preflight output and the produced zip path/size.

6. REPORT
   - Give me: engine build result, exact test tally, the F9/F4 sanity findings, the plugin
     build result (or "no Civil 3D on this machine, skipped"), and the preflight/packaging
     result (or skipped, with reason).
   - If everything's green, say so plainly. If anything failed, diagnose it and propose a
     fix, but don't push changes without asking.
```

---

## Expected results (what "green" looks like)

- Engine Release build: **0 warnings, 0 errors**.
- Engine tests: **420 passed, 0 failed, 1 skipped**.
- F9 routing: peak attenuates, positive travel time, volume conserved, output on the input Δt.
- F4 culvert: monotonic rating curve, low-flow inlet HW < one diameter, no high-flow blow-up.
- Plugin: compiles against the installed Civil 3D refs; `HC_ABOUT` lists 52 commands.
- Preflight: `app-store-preflight.ps1` exits 0 (add `-RequireSigning` once the cert is in).
