import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from tools import sound_audit


class SoundAuditTests(unittest.TestCase):
    def test_original_keeps_repeated_id_registrations_in_same_file(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "sounds.cpp"
            source.write_text(
                'LoadWaveFile(SOUND_TEST, L"Data\\\\Sound\\\\first.wav", 1);\n'
                'LoadWaveFile(SOUND_TEST, L"Data\\\\Sound\\\\second.wav", 1);\n',
                encoding="utf-8",
            )
            entries = sound_audit.parse_original(Path(directory))
        self.assertEqual(entries["SOUND_TEST"]["sounds.cpp"], {"Sound/first.wav", "Sound/second.wav"})

    def test_strict_checks_each_spelling_of_same_path(self):
        refs = {
            "Sound/example.wav": ["bad.cs"],
            "Sound/Example.wav": ["good.cs"],
        }

        def status(path, _index, _data):
            return "case" if path == "Sound/example.wav" else "ok"

        output = io.StringIO()
        with patch.object(sys, "argv", ["sound_audit.py", "--strict"]), \
             patch.object(sound_audit.Path, "is_dir", return_value=True), \
             patch.object(sound_audit, "parse_original", return_value={}), \
             patch.object(sound_audit, "parse_port", side_effect=[refs, {}]), \
             patch.object(sound_audit, "build_data_index", return_value={"sound/example.wav": "Example.wav"}), \
             patch.object(sound_audit, "status_for", side_effect=status), \
             contextlib.redirect_stdout(output):
            result = sound_audit.main()

        self.assertEqual(result, 1)
        self.assertIn("[case] Sound/example.wav  <- bad.cs", output.getvalue())
        self.assertIn("Port reference data errors: 1", output.getvalue())


if __name__ == "__main__":
    unittest.main()
