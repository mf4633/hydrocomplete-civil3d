#!/usr/bin/env python3
"""Verify PackageContents.xml is in sync with the [CommandMethod] commands in source.

Cross-platform CI check (no Civil 3D or PowerShell required): every HC_* command
declared in src/HydroComplete.Civil3D/Commands/*.cs must be registered in the manifest,
and the per-release ComponentEntry series must be present. Exits non-zero on any mismatch.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "dist" / "HydroComplete.bundle" / "PackageContents.xml"
COMMANDS_DIR = ROOT / "src" / "HydroComplete.Civil3D" / "Commands"
NET48_BUNDLE_DLL = ROOT / "dist" / "HydroComplete.bundle" / "Contents" / "net48" / "HydroComplete.Civil3D.dll"

COMMAND_ATTR = re.compile(r'\[CommandMethod\("([^"]+)"(?:,\s*[^)]+)?\)\]')
REQUIRED_SERIES = ["R25.0", "R25.1"]  # 2025, 2026 (net8)


def main() -> int:
    if not MANIFEST.exists():
        print(f"FAIL: manifest not found at {MANIFEST}")
        return 1
    if not COMMANDS_DIR.is_dir():
        print(f"FAIL: commands dir not found at {COMMANDS_DIR}")
        return 1

    manifest = MANIFEST.read_text(encoding="utf-8", errors="replace")

    # Source commands
    source_cmds: set[str] = set()
    for cs in COMMANDS_DIR.rglob("*.cs"):
        for m in COMMAND_ATTR.finditer(cs.read_text(encoding="utf-8", errors="replace")):
            name = m.group(1)
            if name.startswith("HC_"):
                source_cmds.add(name)

    if not source_cmds:
        print("FAIL: no HC_* [CommandMethod] declarations found in source")
        return 1

    problems: list[str] = []

    # Every source command must be registered in the manifest
    missing = sorted(c for c in source_cmds if f'Local="{c}"' not in manifest)
    if missing:
        problems.append("commands missing from PackageContents.xml: " + ", ".join(missing))

    # Required per-release series entries
    for series in REQUIRED_SERIES:
        if f'SeriesMin="{series}"' not in manifest:
            problems.append(f"missing ComponentEntry for series {series}")

    # net48 (2024) block required only when the net48 bundle is staged
    if NET48_BUNDLE_DLL.exists() and 'SeriesMin="R24.3"' not in manifest:
        problems.append("net48 bundle is staged but ComponentEntry for R24.3 is missing")

    if problems:
        print("MANIFEST CHECK FAILED:")
        for p in problems:
            print("  - " + p)
        return 1

    print(f"OK: {len(source_cmds)} HC_* commands, all registered; series {', '.join(REQUIRED_SERIES)} present.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
