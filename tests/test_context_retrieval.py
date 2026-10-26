"""Source contracts for bounded read-only continuity evidence retrieval."""
from pathlib import Path
import unittest

SRC=Path(__file__).resolve().parents[1] / "src" / "Novalist.AiBridge"

class ContextRetrievalTests(unittest.TestCase):
    def test_tool_definitions_and_dispatch(self):
        mcp=(SRC/"McpHttpServer.cs").read_text()
        for name in ("find_mentions", "search_codex_context"):
            self.assertIn('name = "'+name+'"',mcp)
            self.assertIn('"'+name+'" =>',mcp)
        self.assertIn('wholeWord = new { type = "boolean" }',mcp)
        self.assertIn('maximum = 20',mcp)

    def test_search_is_bounded_and_source_addressable(self):
        source=(SRC/"ProjectGateway.Context.cs").read_text()
        for needle in ("MaxEvidencePageSize = 20", "ValidateEvidencePage(offset, limit)",
                       "chapterId", "sceneId", "characterOffset", "contentHash = sceneHash",
                       "nextOffset", "matchedText", "ContextSnippet"):
            self.assertIn(needle,source)
        self.assertIn("ScenesCoreAsync(chapterId)",source)
        self.assertIn("ReadTextCoreAsync(chapterId, sceneId)",source)
        self.assertIn("ContentHash(text)",source)

    def test_only_allowed_codex_context_is_searched(self):
        source=(SRC/"ProjectGateway.Context.cs").read_text()
        self.assertIn("AllowedContextAsync(Id(chapter), Id(scene))",source)
        self.assertNotIn("LoadCharactersAsync(",source)
        self.assertNotIn("GetEntityContentAsync(",source)
        self.assertNotIn("GetCharacterDetailedAsync(",source)
        self.assertIn("sectionIndex = index",source)
        self.assertIn("if (sectionHits.Count < 3)",source)

    def test_still_no_writers(self):
        for name in ("ProjectGateway.Editorial.cs","ReviewWebViewController.cs","web/review.html"):
            self.assertFalse((SRC/name).exists(), name)
        mcp=(SRC/"McpHttpServer.cs").read_text()
        for tool in ("propose_comment","propose_edit","approve_edit","write_scene"):
            self.assertNotIn('"'+tool+'"',mcp)

if __name__=="__main__":
    unittest.main()
