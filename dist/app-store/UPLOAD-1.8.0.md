# Uploading 1.8.0 to the pending review (2026-10-07)

Autodesk support (ticket 34878, Prashant Potadar, 2026-10-06) assigned the review to Sripathi and invited changes: "please make changes to the app and let us know on this.. we will take it." So updating the pending submission is welcome and does not lose the queue position.

Artifact: dist/HydroComplete-1.8.0.zip
SHA256: 5D2E1D4ACFB543233ECDD7CB732E69C56AE65C2DC6C8A9D499676C76561C0AE8
Unsigned, by choice (2026-08-04 decision).

Verified before upload:
- Engine tests: 480 passed, 1 skipped (by design).
- App Store preflight PASSED: 53 commands, all 27 command classes registered, runtime entries for Civil 3D 2024/2025/2026.
- Installed-bundle smoke in Civil 3D 2026 (accoreconsole): NETLOAD, HC_ABOUT, HC_NETWORK, HC_PIPES all pass.
- HC_STM_IMPORT run headless on tests/.../civil3d-2015-storm-sewers.stm: read 4 lines, 5 structures and 711.2 ft, and wrote the LandXML.

Found and fixed during packaging: StmCommands had no [assembly: CommandClass], so HC_STM_IMPORT, the headline 1.8.0 feature, would not have been registered in Civil 3D.

Portal steps (Michael):
1. apps.autodesk.com > Publisher Corner > HydroComplete for Civil 3D (6481677613669567077) > edit the pending version.
2. Replace the binary with HydroComplete-1.8.0.zip, set the version to 1.8.0.
3. Paste the "Release Notes (Publisher portal - v1.8.0)" block from LISTING.md.
4. Optional: add the Hydraflow Storm Sewers line to "WHAT IT IS NOT" (see IN-REVIEW-1.7.4.md).
5. Reply to ticket 34878 once it's uploaded.
