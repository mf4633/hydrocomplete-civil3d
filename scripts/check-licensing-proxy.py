#!/usr/bin/env python3
"""Sanity-check HydroComplete licensing API before App Store submission.

HC_ACTIVATE falls back to an offline stub when the server is unreachable.
A dead proxy (404, DNS failure, HTML error page) would still "activate" during
review — this script fails fast if online validation is not actually working.

Usage:
    python scripts/check-licensing-proxy.py
    python scripts/check-licensing-proxy.py --url https://hydrocomplete.com/api/licensing/validate
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request

DEFAULT_URL = "https://hydrocomplete.com/api/licensing/validate"
DEFAULT_TOKEN = "hc_live_beta_tester01"
TIMEOUT_SEC = 20


def check(url: str, token: str) -> int:
    payload = json.dumps(
        {
            "licenseKey": token,
            "features": ["reports", "export", "civil3d"],
        }
    ).encode("utf-8")

    req = urllib.request.Request(
        url,
        data=payload,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "User-Agent": "HydroComplete-licensing-check/1.0",
        },
    )

    print(f"POST {url}")
    print(f"token: {token}")

    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT_SEC) as resp:
            status = resp.status
            body = resp.read().decode("utf-8", errors="replace")
            content_type = resp.headers.get("Content-Type", "")
    except urllib.error.HTTPError as exc:
        print(f"FAIL: HTTP {exc.code} from licensing endpoint")
        try:
            print(exc.read().decode("utf-8", errors="replace")[:500])
        except Exception:
            pass
        print(
            "HC_ACTIVATE would fall back to offline stub for well-formed tokens — "
            "Autodesk review may not catch a dead proxy."
        )
        return 1
    except urllib.error.URLError as exc:
        print(f"FAIL: request error — {exc.reason}")
        print(
            "DNS/network failure: HC_ACTIVATE would use offline stub, not online validation."
        )
        return 1

    print(f"HTTP {status}  Content-Type: {content_type}")

    if "json" not in content_type.lower():
        print("FAIL: response is not JSON (proxy may be misconfigured)")
        print(body[:500])
        return 1

    try:
        data = json.loads(body)
    except json.JSONDecodeError:
        print("FAIL: invalid JSON body")
        print(body[:500])
        return 1

    if data.get("valid") is not True:
        print("FAIL: JSON parsed but valid != true")
        print(json.dumps(data, indent=2)[:800])
        return 1

    license_info = data.get("license") or {}
    expires = license_info.get("expires", "?")
    features = license_info.get("features") or []
    print("OK: online validation working")
    print(f"    expires: {expires}")
    print(f"    features: {', '.join(features)}")
    if data.get("accessToken"):
        print("    accessToken: present")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=DEFAULT_URL, help="licensing validate URL")
    parser.add_argument("--token", default=DEFAULT_TOKEN, help="beta token to test")
    args = parser.parse_args()
    return check(args.url, args.token)


if __name__ == "__main__":
    sys.exit(main())