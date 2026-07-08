# HydroComplete — Autodesk App Store Launch Kit

Everything needed to take HydroComplete for Civil 3D from code-complete to live on
the Autodesk App Store. **Start here**, then work the ordered checklist.

## The one-screen plan

The engineering is done (v1.7.2, 52 commands, manifest conformant). Four
things gate "Submit," and only one has external lead time.

```
▶ TODAY
  1. Order an OV/EV code-signing certificate ........ 1–10 business days (the long pole)
  2. Create the Autodesk Publisher account ........... ~1 hour

▶ ON A CIVIL 3D MACHINE (2–3 focused days)
  3. Build + stage the net8 bundle ................... scripts/preflight-net8.ps1 → release.ps1
  4. Sign the binaries (once cert arrives) ........... scripts/sign-release.ps1 → release.ps1
  5. Validate all 52 commands launch clean .......... VALIDATION_SESSION.md
  6. Capture screenshots ............................. SCREENSHOTS.md (1920×1080)
  7. Final gate ...................................... app-store-preflight.ps1 -RequireSigning  (must exit 0)

▶ SUBMIT
  8. Upload zip + fill the portal .................... PORTAL-FIELDS.md (copy from LISTING.md)
  9. Submit; respond to review within a business day . Autodesk ~1–2 weeks

▶ LAUNCH DAY
 10. Badge on the site, email waitlist, post ......... LAUNCH-COMMS.md
```

**Start the certificate today** — it's the only thing you can't compress, and
everything else is a few days of hands-on work.

## Kit map

### Plan & submission
| File | What it is |
|---|---|
| [`LAUNCH-PLAN.md`](LAUNCH-PLAN.md) | The sequenced plan: critical path, six phases, 90-day post-launch, pricing decision, risks |
| [`SUBMISSION_CHECKLIST.md`](SUBMISSION_CHECKLIST.md) | Line-item status tracker (phases 0–7) |
| [`PORTAL-FIELDS.md`](PORTAL-FIELDS.md) | Field-by-field Publisher-portal fill-in runbook |
| [`VALIDATION_SESSION.md`](VALIDATION_SESSION.md) | On-machine Civil 3D functional validation script |

### Listing assets
| File | What it is |
|---|---|
| [`LISTING.md`](LISTING.md) | All store copy: title, descriptions, 52-command reference, features, pricing, keywords, release notes, legal |
| [`SCREENSHOTS.md`](SCREENSHOTS.md) / [`SCREENSHOT_CAPTIONS.md`](SCREENSHOT_CAPTIONS.md) | The 10-shot list + captions |
| [`LAUNCH-COMMS.md`](LAUNCH-COMMS.md) | Waitlist email, LinkedIn post, forum post, ADN outreach — ready to send |

### Product & company docs (in [`../../docs/`](../../docs/))
| File | For |
|---|---|
| [`USER-GUIDE.md`](../../docs/USER-GUIDE.md) | End users — install, activate, workflows, methods |
| [`ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) | Technical diligence — engine + plugin + backend |
| [`ONE-PAGER.md`](../../docs/ONE-PAGER.md) | The Autodesk conversation — positioning + ask |
| [`COMMERCIAL.md`](../../docs/COMMERCIAL.md) | Free/Pro model and path to paid |

### Build & release scripts (in [`../../scripts/`](../../scripts/))
| Script | Does |
|---|---|
| `preflight-net8.ps1` | Verify SDK + Autodesk DLLs, build the shippable net8 plugin |
| `release.ps1` | Assemble the full bundle (net8 + net48) and zip it |
| `sign-release.ps1` | Sign the binaries (needs the cert) |
| `app-store-preflight.ps1` | Final gate — layout, versions, 52 commands, icon, **signing** (`-RequireSigning`) |

## Two standing actions on the web side (`hc-refactored`)

Independent of the App Store, needed before the backend/repo changes hands or the
plugin activation is used in review:

1. **Rotate the three leaked keys** — OpenAI + Supabase anon + service_role (they
   persist in git history).
2. **Provision the license keys as Fly secrets** — `HC_LICENSE_KEYS` /
   `HC_OPENCAD_LICENSE_KEYS` (or `HC_LICENSE_STORE_JSON`), or `HC_ACTIVATE` will
   404/403 now that keys aren't baked into source.

## Status at a glance

| Area | State |
|---|---|
| Engine + plugin code | ✅ v1.7.2, 52 commands, engine unit-tested |
| PackageContents.xml | ✅ conformant (schema, GUID, per-release RuntimeRequirements, 52/52 command sync, version synced) |
| Icon (96×96) | ✅ present |
| Listing copy + release notes | ✅ ready (`LISTING.md`) |
| Legal (privacy, EULA, disclaimer) | ✅ live |
| net8 bundle built + signed | ⬜ needs Civil 3D + cert |
| Functional validation (52 cmds) | ⬜ needs Civil 3D |
| Screenshots | ⬜ needs Civil 3D |
| Code-signing certificate | ⬜ **order today** |

---

*Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk,
Inc. HydroComplete is an independent product, not affiliated with or endorsed by
Autodesk.*
