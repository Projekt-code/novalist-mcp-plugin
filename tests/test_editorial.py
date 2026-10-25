import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1] / "src" / "Novalist.AiBridge"
class EditorialTests(unittest.TestCase):
 def test_proposals_only_mcp(self):
  m=(ROOT/"McpHttpServer.cs").read_text()
  for n in ("propose_comment","propose_edit"):
   self.assertIn('name = "'+n+'"',m)
   self.assertIn('"'+n+'" =>',m)
  self.assertNotIn('"approve_editorial" =>',m)
 def test_native_approval(self):
  g=(ROOT/"ProjectGateway.Editorial.cs").read_text()
  for n in ("ApproveEditorialAsync","GetSceneStats","GetScene","TakeSnapshotAsync","SuggestEditAsync","AddCommentAsync","IsSceneBusyAsync"):
   if n not in ("GetSceneStats","GetScene"):
    self.assertIn(n,g)
  self.assertIn("SavedHtmlHash",g)
  self.assertIn("ActiveDraftId",g)
  self.assertIn("ActiveBookId",g)
  self.assertIn("ProjectRoot",g)
 def test_ui_controller(self):
  c=(ROOT/"ReviewWebViewController.cs").read_text()
  self.assertIn('"approve" =>',c)
  self.assertIn('"reject" =>',c)
