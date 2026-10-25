import json
from pathlib import Path
import re
import shutil
import subprocess
import unittest

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'src' / 'Novalist.AiBridge'

class BundleTests(unittest.TestCase):
    def test_manifest_and_version(self):
        manifest = json.loads((SRC / 'extension.json').read_text())
        self.assertEqual(manifest['id'], 'com.whscott.novalist-ai-bridge')
        self.assertEqual(manifest['version'], '0.4.0')
        self.assertEqual(manifest['minHostVersion'], '3.6.0')
        self.assertEqual(manifest['contributes']['views'][0]['entry'], 'web/settings.html')
        self.assertEqual(manifest['contributes']['views'][0]['key'], 'com.whscott.novalist-ai-bridge.settings')

    def test_sdk_pin(self):
        s=(SRC/'Novalist.AiBridge.csproj').read_text()
        self.assertIn('net10.0',s)
        self.assertIn('Version="15.0.0"',s)

    def test_bridge_uses_native_webview_channel(self):
        x=(SRC/'AiBridgeExtension.cs').read_text()
        c=(SRC/'SettingsWebViewController.cs').read_text()
        self.assertIn('IExtension, IWebViewContributor',x)
        self.assertIn('IWebViewController? CreateController(string viewKey)',x)
        self.assertIn('new SettingsWebViewController',x)
        self.assertIn('IWebViewController',c)
        self.assertIn('Task<string?> OnMessageAsync(string json)',c)
        self.assertIn('MessagePosted',c)
        self.assertIn('action is not ("reveal" or "copy")',c)
        self.assertIn('GetBearerValue()', (SRC/'McpHttpServer.cs').read_text())

    def test_no_browser_token_endpoint(self):
        server=(SRC/'McpHttpServer.cs').read_text()
        self.assertNotIn('BridgeSetup', server)
        self.assertNotIn('"/setup/token"',server)
        self.assertNotIn('"/setup/request-code"',server)
        self.assertIn('"/health"',server)
        self.assertIn('"http://127.0.0.1:8765/"',server)
        self.assertIn('CryptographicOperations.FixedTimeEquals',server)
        self.assertFalse((SRC/'BridgeSetup.cs').exists())

    def test_mcp_tools_preserved(self):
        m=(SRC/'McpHttpServer.cs').read_text()
        for tool in ['sdk_diagnostics','book_info','list_chapters','list_scenes','get_scene','search_scenes']:
            self.assertIn('"'+tool+'"',m)
        self.assertIn('version = "0.4.0"',m)
        project=(SRC/'ProjectGateway.cs').read_text()
        self.assertIn('GetChaptersOrdered()',project)

    def test_webview_controls_and_copy_fallback(self):
        html=(SRC/'web/settings.html').read_text()
        for needle in ['id="reveal"','id="hide"','id="copy"','id="bearer"',
                       'novalistExt','novalistHost','navigator.clipboard','box.select()', 'Bearer ']:
            self.assertIn(needle,html)
        self.assertNotIn('show pairing code in novalist',html.lower())
        self.assertNotIn('http://127.0.0.1:8765/setup/',html)
        match=re.search(r'<script>\s*(\(\(\) => \{.*?\}\)\(\);)\s*</script>',html,re.S)
        self.assertIsNotNone(match)
        js=match.group(1)
        if shutil.which('node'):
            result=subprocess.run(['node','--check'],input=js,text=True,capture_output=True)
            self.assertEqual(result.returncode,0,result.stderr)

    def test_ui_js_reveal_copy_hide_and_fallback(self):
        if not shutil.which('node'):
            self.skipTest('node missing')
        js=re.search(r'<script>\s*(\(\(\) => \{.*?\}\)\(\);)\s*</script>',
                     (SRC/'web/settings.html').read_text(),re.S).group(1)
        harness=r'''
          const vm=require('node:vm');
          const assert=require('node:assert/strict');
          const listeners={};const elements={}; const posted=[];
          for (const id of ['bearer','feedback','reveal','hide','copy']) {
            elements[id]={id,type:'password',value:'',disabled:id==='hide',
              textContent:'',addEventListener(k,fn){listeners[id+':'+k]=fn},
              focus(){this.focused=true},select(){this.selected=true}};
          }
          const parent={postMessage(obj){posted.push(JSON.parse(obj.novalistExt))}};
          const ctx={document:{getElementById:id=>elements[id]},
            window:{parent,addEventListener(k,fn){listeners['window:'+k]=fn}},
            navigator:{clipboard:{writeText:async text=>{ctx.copied=text}}}};
          vm.runInNewContext(JSON.parse(process.argv[1]),ctx);
          assert.equal(posted.length,0,'no token request on loading view');
          listeners['reveal:click']();assert.equal(posted[0].action,'reveal');
          const answer=(action,value)=>listeners['window:message']({source:parent,
            data:{novalistHost:JSON.stringify({action,value})}});
          answer('reveal','Bearer abc123');
          assert.equal(elements.bearer.value,'Bearer abc123');
          assert.equal(elements.bearer.type,'text');
          listeners['hide:click']();assert.equal(elements.bearer.type,'password');
          listeners['copy:click']();assert.equal(posted.at(-1).action,'copy');
          (async()=>{
            await answer('copy','Bearer abc123');assert.equal(ctx.copied,'Bearer abc123');
            ctx.navigator.clipboard.writeText=async()=>{throw Error('blocked')};
            await answer('copy','Bearer abc123');
            assert.equal(elements.bearer.selected,true);
            assert.equal(elements.bearer.value,'Bearer abc123');
          })().catch(e=>{console.error(e);process.exitCode=1});
        '''
        result=subprocess.run(['node','-e',harness,json.dumps(js)],capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stdout+'\n'+result.stderr)

    def test_actual_novalist_sdk15_manuscript_contract(self):
        project=(SRC/'ProjectGateway.cs').read_text()
        for method in ['GetChaptersOrdered()', 'GetScenesForChapter(chapterId)',
                       'ReadSceneContentAsync(chapterId, sceneId)', 'GetBooks()', 'GetDrafts()']:
            self.assertIn(method,project)
        self.assertIn('!s.Inactive && !s.ExcludeFromExport',project)
        self.assertNotIn('Could not locate a chapter-list API',project)
        self.assertIn('bridge = "0.4.0"', project)

    def test_packaging(self):
        build=(ROOT/'build.sh').read_text()
        self.assertIn('settings.html',build)
        self.assertIn('Novalist.AiBridge.dll',build)
        self.assertNotIn('BridgeSetup.cs',build)

if __name__=='__main__': unittest.main()
