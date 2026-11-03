# Resubmitting after Autodesk's 2026-10-12 review (HydroComplete 1.9.0 + Field Kit 1.1.0)

Review email: Sripathi (sripathi.acharya@autodesk.com), "Design and Make Marketplace : Civil 3D :
Survey & Parcel Field Kit / HydroComplete", 2026-10-12, cc Prashant. Ticket 34878.
Both apps went back to Draft on 10/12. Four findings, all addressed:

| Finding | HydroComplete 1.9.0 | Survey & Parcel Field Kit 1.1.0 |
|---|---|---|
| Icon has text | Letter-like mark removed (store-icon-512.png, PackageIcon.png) | Repo icon already has no text; the portal still shows an older "C3D" icon, so upload the repo one |
| Support Civil 3D 2027 | New net10 build compiled against Autodesk's 2027 API NuGet packages (AutoCAD.NET 26.0.0, Civil3D.NET 13.9.628); R26.0 entry in PackageContents.xml | Declared R24.2-R26.0 (AutoLISP + CUIX, no recompile) |
| Remove "for Civil 3D" from the name | Name = "HydroComplete" | Name = "Survey & Parcel Field Kit" |
| Black Duck: .NET components 2.1.x / 6.0.0 / 8.0.0-8.0.10 | Every shipped DLL now current: net48 polyfills 10.0.14 / 4.6.x / 6.1.2; Logging.Abstractions + DI.Abstractions 10.0.14; PDFsharp 6.2.4; WebView2 1.0.4314.35. Unused DLLs (DependencyInjection, Logging, Options, Primitives, Pkcs, PDFsharp BarCodes/Quality/Snippets/Cryptography) no longer shipped | n/a (no .NET) |

## Artifacts

- HydroComplete: `dist/HydroComplete-1.9.0-appstore.zip` (user guide + HydroComplete.bundle)
- Field Kit: `~/Desktop/AI/C3D-AutoCAD/dist/HydroCompleteFieldKit_v1.1.0_bundle.zip`
  SHA256 973BBED141F2CAA6E23C359F76FA20B10A37AD0EACEF20D6B9856EA3E338792F (1.1.0: portal already had 1.1, so 1.0.1 would have looked like a downgrade)
- Field Kit icon: `~/Desktop/AI/C3D-AutoCAD/marketplace/HydroCompleteFieldKit.bundle/Resources/icon.png`
- HydroComplete icon: `dist/app-store/store-icon-512.png`

## Verified

- Engine tests: 480 passed, 1 skipped.
- Civil 3D 2026 headless sweep (accoreconsole): 45/45, including HC_REPORT_PDF writing a PDF.
- App Store preflight passed (DLLs unsigned, by choice 2026-08-04).
- 2027 build references acmgd/acdbmgd/accoremgd 26.0.0.0 and AeccDbMgd 13.9.0.628 (the only 13.9 API package).

## NOT verified (say so if asked)

- Civil 3D 2027 is not installed on this machine, so neither app has been loaded in 2027.
  Load each once in Civil 3D 2027 before resubmitting if you can (HC_ABOUT; FIELDKIT).
- Civil 3D 2024 (net48) has never been load-tested here either.

## Portal steps (Michael)

1. apps.autodesk.com > Publisher Corner > Apps > Unpublished > HydroComplete for Civil 3D > Manage > Edit the English version.
2. Name: `HydroComplete`. Icon: upload store-icon-512.png. Binary: HydroComplete-1.9.0-appstore.zip. Version 1.9.0.
3. Compatibility: tick Civil 3D 2024, 2025, 2026, 2027.
4. Release notes: the v1.9.0 block in LISTING.md.
5. Save, then click **Submit** on the app page (the orange banner says it stays in Draft until Submit is clicked).
6. Same for Survey & Parcel Field Kit: name, icon.png, zip 1.0.1, version 1.0.1, compatibility 2023-2027, Submit.
7. Reply to Sripathi's 10/12 email (reply all, keeps Prashant and the ticket): draft in REVIEW_REPLY_2026-11-02.md.

## Done 2026-11-03 (Claude, via Chrome)

Both apps edited and SUBMITTED; both show "Pending review" dated 2026-11-03.
- HydroComplete: name, icon, 1.9.0 zip, version 1.9.0 + notes, Civil 3D 2024-2027 ticked, 2027 added to six text fields.
- Field Kit: name, icon.png, 1.1.0 zip, version 1.1.0 + notes, Civil 3D 2023-2027 (2021/2022 unticked: bundle never declared them), stale "C3D Field Kit"/C3DFieldKit.bundle text fixed.
- Reviewer reply: SENT 2026-11-03 (reply-all to Sripathi + Prashant).
