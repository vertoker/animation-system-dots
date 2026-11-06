import importlib.util
from pathlib import Path
import tempfile
import unittest


class SandboxExportTests(unittest.TestCase):
    def test_missing_changed_and_extra_files_are_detected(self):
        spec = importlib.util.spec_from_file_location("sync", Path(__file__).parents[1] / "SyncSandbox.py")
        self.assertTrue(spec and spec.loader and Path(spec.origin).exists(), "Sandbox export tool is missing")
        sync = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(sync)
        with tempfile.TemporaryDirectory() as folder:
            source, destination = Path(folder) / "source", Path(folder) / "destination"
            source.mkdir()
            (source / "scene.unity").write_bytes(b"scene\r\n")
            (source / "scene.unity.meta").write_bytes(b"guid: stable\n")
            self.assertTrue(sync.differences(source, destination))
            sync.export(source, destination)
            self.assertEqual(sync.differences(source, destination), [])
            (destination / "scene.unity").write_bytes(b"changed")
            self.assertIn("Changed: scene.unity", sync.differences(source, destination))
            (destination / "extra").write_bytes(b"extra")
            with self.assertRaisesRegex(ValueError, "Unexpected"):
                sync.export(source, destination)
            (destination / "extra").unlink()
            sync.export(source, destination)
            self.assertEqual(sync.differences(source, destination), [])


if __name__ == "__main__":
    unittest.main()
