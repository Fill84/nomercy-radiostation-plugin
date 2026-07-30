#!/usr/bin/env python3
"""Check every pinned seed UUID before a release.

Three things, in order: radio-browser still has the station, it still passes the
plugin's admission gates, and its stream actually answers.

That last check is the point. radio-browser's `lastcheckok` is a claim, not a fact:
it reported Tomorrowland Anthems' OWR_DAB.mp3 as healthy while the URL was a 404,
which is why that station had to be resubmitted upstream. Nothing else in this
repository connects to a stream.

Run before tagging a release. Deliberately NOT on the push path - a station's outage
is not a reason for this repository's build to go red.

    python scripts/resolve-seeds.py

Exit code 0 when every seed is fine, 1 when any needs attention.
"""

import json
import pathlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

API = "https://all.api.radio-browser.info"
# No patch version in the string: this file is not touched by the release bump, so a
# pinned version here would go stale the moment CI opened the next one.
UA = "nomercy-radiostation-plugin (+https://forgejo.phillippepelzer.me/FiLL/nomercy-radiostation-plugin)"

SEEDS_FILE = (
    pathlib.Path(__file__).resolve().parent.parent
    / "src" / "NoMercy.Plugin.InternetRadio" / "Catalog" / "SeedStations.cs"
)

UUID_PATTERN = re.compile(
    r'"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})"'
)


def read_seed_uuids() -> list[str]:
    """The UUIDs, read out of the C# rather than duplicated here.

    Two lists that could disagree would make this script's PASS meaningless.
    """
    if not SEEDS_FILE.exists():
        sys.exit(f"seed file not found: {SEEDS_FILE}")

    uuids = UUID_PATTERN.findall(SEEDS_FILE.read_text(encoding="utf-8"))
    if not uuids:
        sys.exit(f"no seed uuids found in {SEEDS_FILE}")

    # Ordered, de-duplicated - a duplicate would silently leave us one station short.
    return list(dict.fromkeys(uuids))


def fetch_records(uuids: list[str]) -> dict[str, dict]:
    """One POST for every seed, the same call the plugin makes."""
    request = urllib.request.Request(
        f"{API}/json/stations/byuuid",
        data=urllib.parse.urlencode({"uuids": ",".join(uuids)}).encode(),
        headers={"User-Agent": UA},
    )

    with urllib.request.urlopen(request, timeout=60) as response:
        return {record["stationuuid"]: record for record in json.load(response)}


def gate_failures(record: dict) -> list[str]:
    """The plugin's own admission rules, kept in step with StationGates.Admits."""
    url = record.get("url_resolved") or record.get("url") or ""
    failures = []

    if not str(record.get("name") or "").strip():
        failures.append("no-name")
    if not url.startswith("https://"):
        failures.append("not-https")
    if record.get("hls"):
        failures.append("hls")
    if record.get("lastcheckok") != 1:
        failures.append("not-checked")

    return failures


def stream_answers(url: str) -> tuple[bool, str]:
    """A range request for the first couple of kilobytes.

    Ranged so this takes a moment rather than streaming indefinitely, and because a
    station that refuses a range request but serves audio is still fine - what is
    being tested is that something answers, not how.
    """
    request = urllib.request.Request(
        url, headers={"User-Agent": UA, "Range": "bytes=0-2047"}
    )

    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            body = response.read(2048)
            content_type = response.headers.get("Content-Type", "?")
            main_type = content_type.split(";", 1)[0].strip().lower()

            if response.status not in (200, 206) or not body:
                return False, f"{response.status}, {len(body)} bytes"

            # A denylist, not an audio/* allowlist: real stations legitimately serve
            # application/ogg, application/octet-stream, and other non-audio types
            # that are still genuine streams, so requiring audio/* would false-fail
            # a working station. What is worth rejecting is a response that is
            # unambiguously NOT a stream - an HTML or plain-text body (a soft-404
            # error page served with status 200) or a JSON API error envelope. This
            # is the same failure mode as the historical Anthems case - a claim of
            # health that isn't one - just arriving as a 200 instead of a
            # radio-browser lastcheckok lie.
            if main_type.startswith("text") or main_type == "application/json":
                return False, f"{response.status} {content_type} (not audio)"

            return True, f"{response.status} {content_type}"
    except urllib.error.HTTPError as error:
        return False, f"HTTP {error.code}"
    except Exception as error:                          # noqa: BLE001 - report, never raise
        return False, type(error).__name__


def main() -> int:
    # No options worth having, but any argument (most likely --help) should not
    # silently trigger ten live fetches against third-party servers.
    if len(sys.argv) > 1:
        print(
            "usage: python scripts/resolve-seeds.py\n\n"
            "Checks every pinned seed UUID in SeedStations.cs against radio-browser\n"
            "and its live stream. Requires network access. Exit code 0 means every\n"
            "seed resolves, passes the plugin's admission gates, and answers."
        )
        return 0

    uuids = read_seed_uuids()
    print(f"checking {len(uuids)} seed stations\n")

    try:
        records = fetch_records(uuids)
    except Exception as error:                          # noqa: BLE001
        sys.exit(f"could not reach radio-browser: {error}")

    failures = 0

    for uuid in uuids:
        record = records.get(uuid)

        if record is None:
            print(f"MISSING  {uuid}  radio-browser no longer has this station")
            failures += 1
            continue

        name = record.get("name", "").strip()
        url = record.get("url_resolved") or record.get("url") or ""

        gated = gate_failures(record)
        if gated:
            print(f"GATED    {name[:44]:<46} {uuid}  ({', '.join(gated)})")
            failures += 1
            continue

        alive, detail = stream_answers(url)
        if alive:
            print(f"OK       {name[:44]:<46} {uuid}  {detail}")
        else:
            print(f"DEAD     {name[:44]:<46} {uuid}  stream: {detail}")
            failures += 1

    print()
    if failures:
        print(f"{failures} of {len(uuids)} seeds need attention before release")
        return 1

    print(f"all {len(uuids)} seeds resolve, pass the gates, and answer")
    return 0


if __name__ == "__main__":
    sys.exit(main())
