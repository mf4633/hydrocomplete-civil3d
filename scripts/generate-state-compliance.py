#!/usr/bin/env python3
"""Generate StateComplianceData.cs from hc-refactored StateConfigurations.js."""

from __future__ import annotations

import re
import sys
from pathlib import Path

JS_PATH = Path(r"C:\Users\michael.flynn\hc-refactored\src\config\StateConfigurations.js")
OUT_PATH = Path(__file__).resolve().parent.parent / "src" / "HydroComplete.Engine" / "StateComplianceData.cs"

# Drawdown max overrides for states without bmpDrawdown in JS (curated from regulatory manuals).
DRAWDOWN_MAX_OVERRIDES: dict[str, float] = {
    "NC": 120.0,
}

# Volume-control overrides when JS structure lacks flowAttenuation.volumeControl.
VOLUME_CONTROL_OVERRIDES: dict[str, bool] = {
    "VA": True,
    "FL": True,
    "CA": True,
    "NY": True,
}


def extract_state_blocks(text: str) -> dict[str, str]:
    pattern = re.compile(r"'([A-Z]{2}|DEFAULT)':\s*\{", re.MULTILINE)
    matches = list(pattern.finditer(text))
    blocks: dict[str, str] = {}
    for i, match in enumerate(matches):
        code = match.group(1)
        start = match.start()
        end = matches[i + 1].start() if i + 1 < len(matches) else text.find("};", start)
        blocks[code] = text[start:end]
    return blocks


def first_float(text: str | None, default: float = 0.0) -> float:
    if not text:
        return default
    match = re.search(r"(\d+(?:\.\d+)?)", text)
    return float(match.group(1)) if match else default


def parse_water_quality_inches(block: str) -> float:
    match = re.search(r"'water-quality':\s*'([^']+)'", block)
    if not match:
        return 1.0
    value = match.group(1)
    # CA: "~0.75 inch in LA region"
    tilde = re.search(r"~(\d+(?:\.\d+)?)\s*inch", value, re.IGNORECASE)
    if tilde:
        return float(tilde.group(1))
    return first_float(value, 1.0)


def parse_minimum_removal(block: str, pollutant: str) -> float:
    section = re.search(r"minimumRemoval:\s*\{([^}]+)\}", block, re.DOTALL)
    if not section:
        return 0.0
    body = section.group(1)
    match = re.search(rf"{pollutant}:\s*(\d+(?:\.\d+)?)", body)
    return float(match.group(1)) if match else 0.0


def parse_rfactor_default(block: str) -> float:
    match = re.search(r"rfactor:\s*\{([^}]+)\}", block, re.DOTALL)
    if not match:
        return 170.0
    body = match.group(1)
    default = re.search(r"(?:'default'|default):\s*(\d+(?:\.\d+)?)", body)
    return float(default.group(1)) if default else 170.0


def parse_tolerable_soil_loss(block: str) -> float:
    match = re.search(r"tolerableSoilLoss:\s*(\d+(?:\.\d+)?)", block)
    return float(match.group(1)) if match else 5.0


def parse_drawdown_max(block: str, code: str) -> float:
    if code in DRAWDOWN_MAX_OVERRIDES:
        return DRAWDOWN_MAX_OVERRIDES[code]
    match = re.search(r"bmpDrawdown:\s*\{[^}]*?(?:maxHours|maxDrawdownHours):\s*(\d+(?:\.\d+)?)", block, re.DOTALL)
    if match:
        return float(match.group(1))
    return 72.0


def parse_volume_control(block: str, code: str) -> bool:
    if code in VOLUME_CONTROL_OVERRIDES:
        return VOLUME_CONTROL_OVERRIDES[code]
    match = re.search(r"volumeControl:\s*\{[^}]*?required:\s*(true|false)", block, re.DOTALL)
    if match:
        return match.group(1) == "true"
    if "waterQualityVolume:" in block:
        return True
    return False


def parse_roadway_tss(block: str, default_tss: float) -> float | None:
    match = re.search(r"roadway:\s*\{[^}]*?minimum:\s*(\d+(?:\.\d+)?)", block, re.DOTALL)
    if not match:
        return None
    value = float(match.group(1))
    return value if abs(value - default_tss) > 0.01 else None


def parse_name(block: str) -> str:
    match = re.search(r"name:\s*'([^']+)'", block)
    return match.group(1) if match else ""


def parse_regulatory_body(block: str) -> str:
    match = re.search(r"regulatoryBody:\s*'([^']+)'", block)
    if not match:
        return ""
    body = match.group(1)
    # Shorten long parenthetical names for UI display.
    short = re.sub(r"\s*\([^)]+\)", "", body).strip()
    return short if len(short) <= 80 else body[:77] + "..."


def csharp_escape(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def emit_config(code: str, block: str) -> str:
    name = parse_name(block)
    regulatory = parse_regulatory_body(block)
    design_storm = parse_water_quality_inches(block)
    tss = parse_minimum_removal(block, "TSS") or 80.0
    tn = parse_minimum_removal(block, "TN")
    tp = parse_minimum_removal(block, "TP")
    r_factor = parse_rfactor_default(block)
    tolerable = parse_tolerable_soil_loss(block)
    drawdown_max = parse_drawdown_max(block, code)
    volume_required = parse_volume_control(block, code)
    roadway = parse_roadway_tss(block, tss)

    lines = [
        f'            ["{code}"] = new StateComplianceConfig',
        "            {",
        f'                Code = "{code}",',
        f'                Name = "{csharp_escape(name)}",',
        f'                RegulatoryBody = "{csharp_escape(regulatory)}",',
        f"                DesignStormInches = {design_storm:.4g},",
        f"                WqVolumeFactorInches = {design_storm:.4g},",
        "                PeakAttenuationPercent = 100.0,",
        "                DrawdownMinHours = 48.0,",
        f"                DrawdownMaxHours = {drawdown_max:.4g},",
        f"                TssRemovalPercent = {tss:.4g},",
        f"                TnRemovalPercent = {tn:.4g},",
        f"                TpRemovalPercent = {tp:.4g},",
    ]
    if roadway is not None:
        lines.append(f"                RoadwayTssRemovalPercent = {roadway:.4g},")
    lines.extend(
        [
            f"                TolerableSoilLossTonsPerAcYr = {tolerable:.4g},",
            f"                DefaultRFactor = {r_factor:.4g},",
            f"                VolumeControlRequired = {'true' if volume_required else 'false'},",
            "            },",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    if not JS_PATH.exists():
        print(f"JS source not found: {JS_PATH}", file=sys.stderr)
        return 1

    text = JS_PATH.read_text(encoding="utf-8")
    blocks = extract_state_blocks(text)

    # 50 states + DC + PR + VI + DEFAULT
    state_codes = [c for c in blocks if c != "DEFAULT"]
    state_codes.sort()

    entries: list[str] = []
    for code in state_codes:
        entries.append(emit_config(code, blocks[code]))
    entries.append(emit_config("DEFAULT", blocks["DEFAULT"]))

    header = """// <auto-generated>
// Generated by scripts/generate-state-compliance.py — do not edit by hand.
// Source: hc-refactored/src/config/StateConfigurations.js
// </auto-generated>

using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    public static partial class StateCompliance
    {
        private static Dictionary<string, StateComplianceConfig> BuildConfigs()
        {
            return new Dictionary<string, StateComplianceConfig>(StringComparer.OrdinalIgnoreCase)
            {
"""

    footer = f"""            }};
        }}

        /// <summary>Number of embedded jurisdiction codes (excludes DEFAULT).</summary>
        public const int EmbeddedStateCount = {len(state_codes)};
    }}
}}
"""

    OUT_PATH.write_text(header + "\n".join(entries) + "\n" + footer, encoding="utf-8")
    print(f"Wrote {OUT_PATH} ({len(state_codes)} states/territories + DEFAULT)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())