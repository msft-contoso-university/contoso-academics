import hashlib
import json
import os
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import package_candidate


class CandidateTests(unittest.TestCase):
    def prepare(self, directory):
        root = Path(directory)
        publish = root / "artifacts" / "publish"
        (publish / "sql").mkdir(parents=True)
        (publish / "Contoso.Academics.dll").write_bytes(b"already built")
        (publish / "sql" / "academics.sql").write_bytes(b"reviewed migration")
        (root / "contracts").mkdir()
        (root / "contracts" / "academics.openapi.yaml").write_bytes(b"published contract")
        return root

    def test_flat_zip_binds_all_files_and_matching_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.prepare(directory)
            sha = "a" * 40
            with patch.object(package_candidate, "ROOT", root), \
                    patch.object(package_candidate.subprocess, "check_output", side_effect=[sha, ""]), \
                    patch.dict(os.environ, {"GITHUB_SHA": sha}):
                package_candidate.main()
            candidate = root / "artifacts" / "candidate"
            archive_path = candidate / f"contoso-academics-{sha}.zip"
            manifest_bytes = (candidate / "candidate.json").read_bytes()
            manifest = json.loads(manifest_bytes)
            with zipfile.ZipFile(archive_path) as archive:
                self.assertIn("Contoso.Academics.dll", archive.namelist())
                self.assertEqual(archive.read("candidate.json"), manifest_bytes)
                self.assertEqual(set(manifest["files"]), set(archive.namelist()) - {"candidate.json"})
                for name, digest in manifest["files"].items():
                    self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), digest)
            self.assertEqual(
                (candidate / "SHA256SUMS").read_text(),
                f"{hashlib.sha256(archive_path.read_bytes()).hexdigest()}  {archive_path.name}\n",
            )

    def test_dirty_source_or_private_path_is_not_packaged(self):
        for dirty in (True, False):
            with self.subTest(dirty=dirty), tempfile.TemporaryDirectory() as directory:
                root = self.prepare(directory)
                if not dirty:
                    (root / "artifacts" / "publish" / "private.txt").write_text(str(root))
                with patch.object(package_candidate, "ROOT", root), \
                        patch.object(package_candidate.subprocess, "check_output",
                                     side_effect=["a" * 40, " M Program.cs" if dirty else ""]), \
                        patch.dict(os.environ, {"GITHUB_SHA": "a" * 40}):
                    with self.assertRaises(ValueError):
                        package_candidate.main()
                self.assertFalse(list((root / "artifacts").rglob("*.zip")))


if __name__ == "__main__":
    unittest.main()
