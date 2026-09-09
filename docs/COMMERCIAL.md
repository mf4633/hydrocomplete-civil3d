# HydroComplete for Civil 3D — commercial model

HydroComplete is built to become a **paid Civil 3D add-in**. The codebase already
separates **Free** and **Pro** so we can turn on billing without rewriting commands.

## Today (v1.7.0)

| Tier | How to get it | What you get |
|------|----------------|--------------|
| **Free** | Install bundle / App Store (when listed) | Core hydraulics: `HC_PIPES`, `HC_CAPACITY`, `HC_HGL`, `HC_RATIONAL`, `HC_REPORT` (HTML), `HC_SOIL`, `HC_ATLAS14`, `HC_NETWORK_DIAGRAM`, DAG editor (`HC_DAG`), Hydraflow `.stm` import (`HC_STM_IMPORT`) |
| **Pro** | `HC_ACTIVATE` with email + token from [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d), or `HYDROCOMPLETE_PRO=1` (dev only) | `HC_REPORT_PDF` — sealable PDF with full formula traces |

License file: `%APPDATA%\HydroComplete\license.json`  
Status: `HC_LICENSE`  
Gate implementation: `src/HydroComplete.Civil3D/Auth/LicenseGate.cs` + `HydroComplete.Engine/LicenseActivator.cs`

## What is free somewhere else

Say this before a buyer finds it out on their own.

**StormSewer** (<https://github.com/mf4633/stormsewer>) is a free, GPL-3.0
desktop program by the same author, and it does the core storm sewer work this
add-in does, at no cost and with no trial:

- Rational method down a network, Manning capacity, standard-step HGL/EGL
  backwater with junction losses and tailwater, HEC-22 inlets with bypass
- Reads the same Hydraflow `.stm` files `HC_STM_IMPORT` reads, plus LandXML
  and DXF
- Writes a submittal PDF with schedules, plan and profile

It is checked against Hydraflow on the same two projects this engine is; see
[`../VALIDATION-HYDRAFLOW.md`](../VALIDATION-HYDRAFLOW.md).

What the Civil 3D add-in is actually worth paying for is the part StormSewer
cannot do: **working on the drawing's own pipe networks and catchments in
place**, with no re-entry and no second model to keep in sync. Labels and
profile polylines land back in the drawing. Design review, BMP optimization,
detention routing, live SSURGO soils and Atlas 14 run against the objects that
are already there.

If an engineer does not need it inside Civil 3D, the honest answer is that the
free desktop app is the better buy, and telling them so costs less than a
refund.

`HC_STM_IMPORT` is free for the same reason: nobody should have to pay to get
their own decade of Hydraflow projects out of a format Autodesk retired.

## Path to paid

1. **Checkout** — Stripe on hydrocomplete.com/civil3d (`POST /api/stripe/create-plugin-checkout`, product `civil3d`). Webhook mints `hc_live_c3d_*` and emails the key. Requires `STRIPE_PRICE_CIVIL3D_PRO` on Fly.
2. **Activation API** — production validate/refresh live; beta token `hc_live_beta_tester01` works today.
3. **App Store** — paid listing or in-app purchase per Autodesk policy; listing copy in `dist/app-store/`.
4. **Enforcement** — expand Pro gate beyond PDF when ready (e.g. batch export, team seats, SLA support); keep a useful Free tier for trial/evaluation.
5. **Offline grace** — keep 30-day refresh + offline stub for field laptops (same pattern as OCS plugin).

## Adding a new Pro-only command

```csharp
if (!LicenseGate.IsProEnabled())
{
    ed.WriteMessage("\n--- Pro feature ---\n  Activate: HC_ACTIVATE | https://hydrocomplete.com/civil3d\n");
    return;
}
```

## OCS vs Civil 3D

| Product | License | Notes |
|---------|---------|--------|
| OpenCAD HydroComplete | GPL plugin + Pro gate | Marketplace install |
| **Civil 3D HydroComplete** | **Proprietary / commercial** | Bundle + App Store; same engine, separate distribution |

Do not ship Civil 3D binaries under GPL; keep engine shared internally, Civil host as commercial add-in.