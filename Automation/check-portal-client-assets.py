#!/usr/bin/env python3
"""Verify adjacent portal assets against CAN bytes and the pinned file manifest."""

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import sys
import zipfile


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "db/imports/official/maps/God2_exact_current_map_sha256.csv"
INVENTORY = ROOT / "db/imports/official/portals/portals.official.json"
PINNED_CLIENT_SHA256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"


def key(path: str) -> str:
    return path.replace("\\", "/").lower()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("can_zip", type=Path, help="archive containing the four source CAN files")
    parser.add_argument("asset_csv", type=Path, help="PowerShell export of installed adjacent files")
    parser.add_argument("--client-exe", type=Path,
                        help="optional God2_opt.exe to hash against the pinned protocol build")
    args = parser.parse_args()

    with MANIFEST.open(encoding="utf-8-sig", newline="") as stream:
        manifest = {key(row["RelativePath"]): row for row in csv.DictReader(stream)}
    inventory = json.loads(INVENTORY.read_text(encoding="utf-8"))
    links = [record for record in inventory["records"]
             if record["id"].startswith("client:can-link/")]
    issues = []
    expected = {}
    with zipfile.ZipFile(args.can_zip) as archive:
        zip_names = {key(Path(name).name): name for name in archive.namelist()}
        for record in links:
            source = record["canEvidence"]["sourcePath"]
            source_name = Path(source).name
            member = zip_names.get(key(source_name))
            if member is None:
                issues.append(f"missing CAN: {source}")
                continue
            raw = archive.read(member)
            source_key = key(source).replace("original/", "", 1)
            pinned = manifest.get(source_key)
            if pinned is None or len(raw) != int(pinned["Length"]) or (
                hashlib.sha256(raw).hexdigest().lower() != pinned["SHA256"].lower()
            ):
                issues.append(f"CAN hash/size differs from manifest: {source}")
                continue
            count = int.from_bytes(raw[8:12], "little")
            index = record["canEvidence"]["recordIndex"]
            offset = 12 + 64 * index
            if index < 1 or index >= count or offset + 64 > len(raw):
                issues.append(f"invalid CAN index: {record['id']}")
                continue
            if raw[offset] != record["canEvidence"]["recordType"]:
                issues.append(f"CAN record type differs: {record['id']}")
            name = raw[offset + 1:offset + 64].split(b"\0", 1)[0].decode("ascii")
            folder = key(str(Path(source).parent)).replace("original/", "", 1)
            path = f"{folder}/{key(name)}"
            if path in expected:
                issues.append(f"duplicate CAN destination: {path}")
            expected[path] = record["id"]

    installed = set()
    with args.asset_csv.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        required = {"CanFile", "RecordIndex", "RelativePath", "Length", "SHA256"}
        if not required.issubset(reader.fieldnames or []):
            raise ValueError(f"CSV requires columns: {sorted(required)}")
        for row in reader:
            path = key(row["RelativePath"])
            logical = path[:-1] if path.endswith((".mdtz", ".hmdz")) else path
            record_id = expected.get(logical)
            if record_id is None:
                issues.append(f"unexpected asset: {row['RelativePath']}")
                continue
            source, _, index = record_id.removeprefix("client:can-link/").partition("/")
            if (key(row["CanFile"]).split("/")[0] != source or
                    int(row["RecordIndex"]) != int(index)):
                issues.append(f"CAN source/index differs: {row['RelativePath']}")
            if logical in installed:
                issues.append(f"duplicate asset record: {row['RelativePath']}")
            installed.add(logical)
            pinned = manifest.get(path)
            if pinned is None or row["Length"] != pinned["Length"] or (
                row["SHA256"].lower() != pinned["SHA256"].lower()
            ):
                issues.append(f"asset hash/size differs from manifest: {row['RelativePath']}")

    missing = {path: record_id for path, record_id in expected.items()
               if path not in installed}
    exe_sha256 = None
    exe_matches_build = None
    if args.client_exe is not None:
        digest = hashlib.sha256()
        with args.client_exe.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        exe_sha256 = digest.hexdigest().upper()
        exe_matches_build = exe_sha256 == PINNED_CLIENT_SHA256
    print(json.dumps({"canLinks": len(links), "assetsMatched": len(installed),
                      "missing": missing, "issues": issues,
                      "assetFilesComplete": not missing and not issues,
                      "clientExeSha256": exe_sha256,
                      "clientExeMatchesPinnedBuild": exe_matches_build,
                      "clientFileProvenanceComplete": not missing and not issues
                      and exe_matches_build is True},
                     indent=2, ensure_ascii=False))
    return 1 if issues else 0


if __name__ == "__main__":
    sys.exit(main())
