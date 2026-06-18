# Self-hosted GitHub Actions runner (Windows + Civil 3D)

GitHub-hosted `windows-latest` runners do **not** ship Autodesk Civil 3D. The
`dotnet build` step in `ci.ps1` references managed host assemblies
(`AcMgd.dll`, `AeccDbMgd.dll`, etc.) from a local install. A **self-hosted
Windows runner** with Civil 3D (or AutoCAD + Civil 3D Object Enabler APIs) is
the supported way to get green CI on every push/PR.

See also: [scripts/README.md](README.md) (script usage) and
[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) (workflow wiring).

---

## Checklist

### 1. Machine requirements

- [ ] **Windows 10/11 or Windows Server 2019+** (x64)
- [ ] **Autodesk Civil 3D 2026** installed (default `AcadDir` in the csproj), **or**
      Civil 3D 2025 with workflow/csproj override (see §4)
- [ ] Verify host DLLs exist, e.g.:
      `C:\Program Files\Autodesk\AutoCAD 2026\AcMgd.dll`
      `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll`
- [ ] **.NET SDK 8.0** and **7.0** (workflow installs via `setup-dotnet`; pre-install
      optional but speeds first job)
- [ ] **PowerShell 5.1+** (built into Windows)
- [ ] **Git** (runner installer bundles a copy; system Git is fine)
- [ ] Stable network egress to `github.com` and `api.github.com`
- [ ] **~10 GB free disk** for SDK caches, build artifacts, and runner work dirs

### 2. Licensing and legal

- [ ] Civil 3D license is valid on the runner machine (named user, network, or
      subscription as permitted by Autodesk)
- [ ] Runner host is **not** a shared public VM unless repo access is restricted
- [ ] Organization policy allows self-hosted runners for this repository

### 3. Install the GitHub Actions runner

1. In GitHub: **Settings → Actions → Runners → New self-hosted runner**
2. Choose **Windows** and follow the download/configure commands
3. Install as a **Windows service** (recommended) or run interactively for testing
4. Apply labels when prompted or afterward:

   | Label | Purpose |
   |---|---|
   | `self-hosted` | Required by GitHub for non-hosted runners |
   | `Windows` | OS family (GitHub default label) |
   | `X64` | Architecture (GitHub default label) |
   | `civil3d` | **Custom** — pin CI jobs to Civil 3D-capable machines |

5. Confirm the runner shows **Idle** in the repository (or org) runner list

### 4. Civil 3D version alignment

`HydroComplete.Civil3D.csproj` defaults to Civil 3D **2026**:

```text
AcadDir = C:\Program Files\Autodesk\AutoCAD 2026\
```

For **Civil 3D 2025** on the runner, either:

- Set a repo/org **Actions variable** `ACAD_DIR` and pass it in the workflow
  (see commented example in `ci.yml`), or
- Pre-set a machine environment variable used by a wrapper script, or
- Change the default in the csproj (not recommended for multi-version fleets)

Local verification before wiring CI:

```powershell
cd C:\path\to\hydrocomplete-civil3d
.\scripts\ci.ps1
```

### 5. Wire the workflow

In `.github/workflows/ci.yml`, switch `runs-on` from `windows-latest` to your
labeled runner, e.g.:

```yaml
runs-on: [self-hosted, Windows, civil3d]
```

Commit and push; confirm the job is picked up by the new runner (Actions tab →
workflow run → job details show runner name).

### 6. Smoke test

- [ ] Push a trivial commit or re-run the workflow manually
- [ ] Job steps: Checkout → Setup .NET → Run CI script — all green
- [ ] Build log shows `AcMgd.dll` resolved from the expected `AcadDir`
- [ ] `Verify PackageContents.xml` lists **R25.0**, **R25.1**, and all `HC_*` commands

### 7. Hardening (production)

- [ ] Runner service account is a **dedicated low-privilege** user (not your daily login)
- [ ] Runner machine is **patched** and reachable only over VPN or private network if possible
- [ ] **Fork PRs from untrusted contributors**: use
      [approval for first-time contributors](https://docs.github.com/en/actions/managing-workflow-runs/approving-workflow-runs-from-public-forks)
      or disable self-hosted runners for `pull_request` from forks
- [ ] Rotate the runner registration token if the host is reimaged
- [ ] Document who owns the machine and escalation path when the runner is offline

### 8. Operations

| Task | Command / location |
|---|---|
| Runner logs | `_diag` folder under the runner install directory |
| Restart service | `services.msc` → *GitHub Actions Runner* |
| Update runner | Re-run the configure script from GitHub Settings |
| Offline CI fallback | Run `.\scripts\ci.ps1` locally before tagging a release |

### 9. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `Could not find AcMgd.dll` | Civil 3D not installed or wrong `AcadDir` | Install C3D 2026 or pass `-p:AcadDir=...` |
| Job queued forever | No runner with matching labels | Add `civil3d` label or fix `runs-on` |
| `dotnet test` fails, build OK | SDK 7.0 missing | Ensure `setup-dotnet` step runs or install SDK 7 |
| Access denied copying DLLs | Permissions on work folder | Fix service account ACL on runner `_work` |
| Intermittent failures | Disk full or antivirus locking `bin\` | Free space; exclude `_work` from real-time scan |

---

## Quick reference: what CI exercises

`ci.ps1` (no Civil 3D **process** required — only **compile-time** API refs):

1. `dotnet test` on `HydroComplete.Civil3D.sln`
2. `dotnet build` on `HydroComplete.Civil3D.csproj`
3. Manifest check on `dist/HydroComplete.bundle/PackageContents.xml`

No `acad.exe` launch is needed for CI; interactive command tests remain manual on
a licensed Civil 3D workstation.