"""Guard the public bridge against accidental manuscript write-back."""
from pathlib import Path
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "Novalist.AiBridge"

class ReadOnlyContractTests(unittest.TestCase):
    def test_mcp_dispatch_and_tool_list_have_no_mutations(self):
        server = (SRC / "McpHttpServer.cs").read_text()
        expected = {"book_info", "list_chapters", "list_scenes", "get_scene",
                    "get_scene_metadata", "get_scene_stats", "get_scene_excerpt",
                    "get_codex_context", "get_codex_section", "search_scenes",
                    "sdk_diagnostics"}
        defined = set(re.findall(r'name\s*=\s*"([^"]+)"', server))
        self.assertTrue(expected.issubset(defined))
        for name in ("propose_comment", "propose_edit", "approve", "reject",
                     "write_scene", "write_codex", "add_comment", "suggest_edit"):
            self.assertNotIn('"' + name + '"', server)
        for forbidden in ("ProposeCommentAsync", "ProposeEditAsync",
                          "ApproveEditorialAsync", "RejectEditorialAsync"):
            self.assertNotIn(forbidden, server)

    def test_extension_has_settings_view_only(self):
        manifest = json.loads((SRC / "extension.json").read_text())
        views = manifest["contributes"]["views"]
        self.assertEqual(len(views), 1)
        self.assertEqual(views[0]["key"], "com.whscott.novalist-ai-bridge.settings")
        self.assertEqual(views[0]["entry"], "web/settings.html")
        host = (SRC / "AiBridgeExtension.cs").read_text()
        self.assertNotIn("ReviewWebViewController", host)
        self.assertNotIn("ApproveEditorialAsync", host)

    def test_no_editorial_source_or_mutation_methods(self):
        self.assertFalse((SRC / "ProjectGateway.Editorial.cs").exists())
        self.assertFalse((SRC / "ReviewWebViewController.cs").exists())
        self.assertFalse((SRC / "web" / "review.html").exists())
        for path in SRC.glob("*.cs"):
            body = path.read_text()
            for forbidden in ("SuggestEditAsync(", "AddCommentAsync(",
                              "WriteSceneContentAsync(", "TakeSnapshotAsync(",
                              "ProposeCommentAsync(", "ProposeEditAsync("):
                self.assertNotIn(forbidden, body, str(path))

    def test_install_bundle_does_not_include_review_view(self):
        build = (ROOT / "build.sh").read_text()
        self.assertIn("settings.html", build)
        self.assertNotIn("review.html", build)

if __name__ == "__main__":
    unittest.main()
