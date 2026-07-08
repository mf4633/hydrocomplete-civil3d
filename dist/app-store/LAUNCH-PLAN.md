# HydroComplete → Autodesk App Store — Launch Plan

How we get HydroComplete for Civil 3D from "code-complete" to **live on the
Autodesk App Store**, and what to do in the 90 days after. This is the sequenced
plan; the line-item status tracker lives in
[`SUBMISSION_CHECKLIST.md`](SUBMISSION_CHECKLIST.md).

**Current state (verified):** v1.7.2, 52 commands, manifest conformant, CI green,
engine unit-tested. Free/Pro licensing live. Web backend deployed.
**Not done:** code-signing certificate, signed net8 build staged, on-machine
Civil 3D functional validation, listing screenshots.

---

## The critical path (what actually gates a submit)

Four things block "Submit for review." Three need a Civil 3D machine; one has
**external lead time and should start first**.

```
        ┌─ Code-signing cert (OV/EV) ──── 1–10 business days (EXTERNAL) ─┐
        │                                                                │
Day 0 ──┼─ Publisher account + agreement (same day) ─┐                   │
        │                                             │                   ▼
        ├─ net8 build on C3D box (hours) ─┐           │            Sign binaries
        │                                 ▼           ▼                   │
        └─ Functional validation ── Screenshots ── Listing assets ───────┤
           (1–2 days on C3D)         (half day)     (ready)              ▼
                                                                   Submit for review
                                                                   (Autodesk: ~1–2 wks)
```

**Start the certificate today.** Everything else can be done in a few focused
days on a Civil 3D machine, but an OV/EV Authenticode cert involves identity
vetting that you can't compress. It is the long pole.

---

## Phase 1 — Publisher onboarding (Day 0, ~1 hour, no Civil 3D)

- [ ] Create an Autodesk Publisher account at
      [aps.autodesk.com/app-store/publisher-center](https://aps.autodesk.com/app-store/publisher-center).
- [ ] Accept the Developer/Publisher agreement.
- [ ] Set up the payout method. **Note:** Autodesk's paid-listing payouts run
      through Autodesk's commerce, not Stripe. Decide the pricing model here
      (see *Pricing decision* below) — it determines whether you need this.
- [ ] Confirm the three public URLs the manifest/listing point to resolve:
      product page `hydrocomplete.com/civil3d`, privacy `…/privacy.html`,
      support `support@hydrocomplete.com`. *(All currently live.)*

## Phase 2 — Code signing (start Day 0, completes in 1–10 days, EXTERNAL)

Autodesk expects signed binaries; unsigned assemblies trigger SmartScreen and
review friction.

- [ ] Purchase an **OV or EV Authenticode** code-signing certificate (DigiCert,
      Sectigo, SSL.com, etc.). EV ships on a hardware token and clears SmartScreen
      reputation immediately; OV is cheaper but builds reputation over time.
- [ ] Complete the CA's business-identity vetting (this is the slow part).
- [ ] Once issued: `setx HC_SIGN_CERT_THUMBPRINT <thumbprint>`, then the release
      flow signs automatically. `app-store-preflight.ps1 -RequireSigning` will
      **fail** if anything is unsigned, so you can't ship an unsigned bundle by
      accident.

## Phase 3 — Build & validate on a Civil 3D machine (2–3 focused days)

Do these on a box with Civil 3D 2026 (and ideally 2025 + 2024 to validate all
three series).

- [ ] **Build the shippable net8 bundle:** `.\scripts\preflight-net8.ps1` then
      `.\scripts\release.ps1`. This stages the 2025/2026 binaries into `Contents/`
      and produces `HydroComplete-1.7.2.zip`. *(The repo currently ships only the
      net48/2024 payload.)*
- [ ] **Sign** the built binaries (`.\scripts\sign-release.ps1`) and re-run
      `release.ps1` so the zip contains signed DLLs.
- [ ] **Functional validation** — work through
      [`VALIDATION_SESSION.md`](VALIDATION_SESSION.md) Blocks A–I on a real
      storm-sewer drawing. Autodesk's reviewers run the app; every advertised
      command must launch without an unhandled exception. Prioritize the "hero"
      commands you'll screenshot and demo: `HC_PIPES`, `HC_CAPACITY`, `HC_HGL`,
      `HC_RATIONAL`, `HC_ANALYZE`, `HC_REPORT`, `HC_ACTIVATE`.
- [ ] **Verify the ribbon** loads (HydroComplete › Analysis tab, all panels) and
      `HC_ABOUT` reports 52 commands.
- [ ] **Test activation end-to-end:** provision the license keys as Fly secrets
      first (`HC_LICENSE_KEYS` / `HC_OPENCAD_LICENSE_KEYS`), then confirm
      `HC_ACTIVATE` → online validate → `HC_REPORT_PDF` unlocks, and the offline
      stub still works with the network unplugged.
- [ ] **Final gate:** `.\scripts\app-store-preflight.ps1 -RequireSigning` exits 0.

## Phase 4 — Listing assets (half day, on the same C3D box)

- [ ] Capture screenshots per [`SCREENSHOTS.md`](SCREENSHOTS.md) at 1920×1080 —
      the 10-shot list is defined; use [`SCREENSHOT_CAPTIONS.md`](SCREENSHOT_CAPTIONS.md)
      for captions. Scrub any client-identifying data from drawings.
- [ ] Record an optional 60–90s demo video (materially lifts conversion).
- [ ] Paste listing copy from [`LISTING.md`](LISTING.md): title, short + long
      description, 52-command reference, key features, keywords, release notes,
      supported products (2024/2025/2026), category (Civil Engineering).
- [ ] Prepare the store icon/thumbnail (distinct from the 96×96 `PackageIcon.png`).

## Phase 5 — Submit & review (~1–2 weeks, Autodesk-side)

- [ ] Upload `HydroComplete-1.7.2.zip`; pass Autodesk's automated manifest
      validation (our manifest is already conformant — command sync, GUID, version,
      per-release RuntimeRequirements all verified).
- [ ] Submit for review. Autodesk installs and runs the app on supported Civil 3D
      versions and checks: installs/uninstalls cleanly, no crashes, does what the
      listing claims, EULA present, no undisclosed data collection or external
      downloads.
- [ ] Respond to any reviewer feedback quickly (turnaround is the main lever on
      total time-to-live).

## Phase 6 — Launch (Day of approval)

- [ ] Add the App Store badge/link to `hydrocomplete.com/civil3d`.
- [ ] Email the beta waitlist with the store link + activation instructions.
- [ ] Announce: LinkedIn (civil/stormwater PE audience), relevant subreddits
      (r/civilengineering), the Autodesk Civil 3D community forum (as a new app,
      not spam), and any state-ASCE or land-development newsletters you can reach.
- [ ] Turn on the Stripe Pro checkout on `civil3d.html` if it isn't already
      (`STRIPE_PRICE_CIVIL3D_PRO` on Fly).

## Phase 7 — Post-launch, first 90 days

- [ ] **Instrument** install→activate→convert (which commands get used, where free
      users hit the Pro wall). You already emit analytics events — watch them.
- [ ] **Support SLA:** answer `support@` within 1 business day; a fast, human
      response early is worth more than any feature.
- [ ] **Ratings:** ask happy activated users to review on the App Store — listing
      rank is driven by ratings + installs.
- [ ] **Iterate the Free/Pro line** based on where conversion actually happens
      (see `COMMERCIAL.md` — batch export, team seats, and sealed-PDF are the
      natural Pro expansions).
- [ ] **Close the validation table** in `README.md` from the Phase 3 results.

---

## Pricing decision (make before Phase 1)

Current plan (per `COMMERCIAL.md` / `LISTING.md`): **Freemium — Free tier + Pro at
$199/yr**, with Pro sold via your own Stripe checkout on hydrocomplete.com and
the App Store listing as **Free**. Two viable models:

| Model | Mechanics | Trade-off |
|---|---|---|
| **Free listing + external Pro (current)** | App Store lists Free; Pro unlocked by `HC_ACTIVATE` token bought on hydrocomplete.com | You keep 100% of Pro revenue and own the customer; Autodesk gets no cut, but you handle billing/support yourself. |
| **Paid listing / in-app on App Store** | Autodesk handles commerce and takes a revenue share | Less billing work and Autodesk-driven discovery/trust, but a revenue share and less direct customer relationship. |

The current model is the right start — you already have the billing rail and it
maximizes margin. Revisit an Autodesk-commerce paid listing only if App Store
discovery meaningfully outperforms your own funnel.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Cert lead time slips the launch | Start Phase 2 today; it's the only externally-gated item. |
| Reviewer hits a crash in an un-validated command | Validate **all 52** commands launch (Phase 3), not just the hero set. |
| Activation fails during review (keys not provisioned) | Provision Fly secrets before Phase 3; test the offline stub as the safety net. |
| net8 bundle incomplete at upload | `release.ps1` on a C3D box stages it; `preflight -RequireSigning` is the gate. |
| Trademark/наming objection | Trademark disclaimer already in `LISTING.md` + README footer. |

## One-page summary

1. **Today:** order the code-signing cert, create the Publisher account.
2. **This week (on a C3D box):** build + sign the net8 bundle, validate all 52
   commands, capture screenshots.
3. **Then:** upload, submit, respond fast to review.
4. **On approval:** link the badge, email the waitlist, announce, watch the
   funnel, answer support same-day.

The engineering is done. The remaining work is procurement (cert), a few days of
hands-on-Civil-3D validation, and marketing execution.
