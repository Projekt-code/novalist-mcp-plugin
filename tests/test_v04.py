import json
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[1] / "src" / "Novalist.AiBridge"

class V04ContractTests(unittest.TestCase):
    def test_version_and_tool_dispatch(self):
        self.assertEqual(json.loads((SRC / "extension.json").read_text())["version"], "0.5.0")
        mcp = (SRC / "McpHttpServer.cs").read_text()
        for name in ("get_scene_metadata", "get_scene_stats", "get_scene_excerpt", "get_codex_context", "get_codex_section"):
            self.assertIn('name = "' + name + '"', mcp)
            self.assertIn('"' + name + '" =>', mcp)

    def test_codex_ai_policy(self):
        codex = (SRC / "ProjectGateway.Codex.cs").read_text()
        self.assertIn("GetAiContextAsync(chapterId, sceneId)", codex)
        self.assertNotIn(".LoadLoreAsync(", codex)
        self.assertNotIn(".GetEntityContentAsync(", codex)
        self.assertIn("allowed.FirstOrDefault", codex)

    def test_bounded_paging(self):
        paging = (SRC / "ProjectGateway.Paging.cs").read_text()
        for expected in ("MaxPageCharacters = 4000", "nextStart = end", "expectedHash", "char.IsLowSurrogate", "ContentHash(text)"):
            self.assertIn(expected, paging)
