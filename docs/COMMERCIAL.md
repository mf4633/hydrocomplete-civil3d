# HydroComplete for Civil 3D — commercial model

HydroComplete is built to become a **paid Civil 3D add-in**. The codebase already
separates **Free** and **Pro** so we can turn on billing without rewriting commands.

## Today (v1.7.0)

| Tier | How to get it | What you get |
|------|----------------|--------------|
| **Free** | Install bundle / App Store (when listed) | Core hydraulics: `HC_PIPES`, `HC_CAPACITY`, `HC_HGL`, `HC_RATIONAL`, `HC_REPORT` (HTML), `HC_SOIL`, `HC_ATLAS14`, `HC_NETWORK_DIAGRAM`, DAG editor (`HC_DAG`) |
| **Pro** | `HC_ACTIVATE` with email + token from [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d), or `HYDROCOMPLETE_PRO=1` (dev only) | `HC_REPORT_PDF` — sealable PDF with full formula traces |

License file: `%APPDATA%\HydroComplete\license.json`  
Status: `HC_LICENSE`  
Gate implementation: `src/HydroComplete.Civil3D/Auth/LicenseGate.cs` + `HydroComplete.Engine/LicenseActivator.cs`

## Path to paid (not flipped yet)

1. **Checkout** — Stripe or Lemon Squeezy on hydrocomplete.com; issue `hc_live_*` (or subscription) tokens after purchase.
2. **Activation API** — production validate/refresh endpoints (partially wired; beta tokens work today).
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