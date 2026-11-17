"""Wrap already-built output; never rebuild, approve or deploy a release."""
import hashlib
import json
import os
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("An exact source commit is required")
    if "GITHUB_SHA" in os.environ and os.environ["GITHUB_SHA"] != sha:
        raise ValueError("Checkout SHA does not match workflow SHA")
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip():
        raise ValueError("Refusing to package a dirty source tree")
    publish = ROOT / "artifacts" / "publish"
    if not (publish / "Contoso.Academics.dll").is_file() or not (publish / "sql" / "academics.sql").is_file():
        raise ValueError("The published application and reviewed migration script are required")
    target = ROOT / "artifacts" / "candidate"
    target.mkdir(exist_ok=False)
    shutil.copytree(ROOT / "contracts", publish / "contracts")
    files = sorted(path for path in publish.rglob("*") if path.is_file())
    private_paths = {
        form.encode()
        for path in (str(ROOT), str(Path.home()))
        for text in (path, path.replace("\\", "/"))
        for form in (text, json.dumps(text)[1:-1])
    }
    for path in files:
        content = path.read_bytes()
        if any(form in content for form in private_paths):
            raise ValueError("Published output embeds a private build path")
    metadata = {
        "status": "UNTRUSTED CANDIDATE - NOT AN APPROVED RELEASE",
        "service": "contoso-academics",
        "sourceCommit": sha,
        "runtime": "net10.0",
        "frameworkDependent": True,
        "entrypoint": "dotnet Contoso.Academics.dll",
        "promotionOwner": "contoso-platform",
        "files": {
            path.relative_to(publish).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in files
        },
    }
    manifest = (json.dumps(metadata, indent=2) + "\n").encode()
    (publish / "candidate.json").write_bytes(manifest)
    archive_path = target / f"contoso-academics-{sha}.zip"
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(path for path in publish.rglob("*") if path.is_file()):
            info = zipfile.ZipInfo(path.relative_to(publish).as_posix(), date_time=(2020, 1, 1, 0, 0, 0))
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, path.read_bytes())
    (target / "candidate.json").write_bytes(manifest)
    checksum = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    (target / "SHA256SUMS").write_text(f"{checksum}  {archive_path.name}\n", encoding="utf-8")
    print(f"Untrusted candidate source {sha}; ZIP SHA256 {checksum}")


if __name__ == "__main__":
    main()
