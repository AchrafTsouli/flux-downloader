const{chromium}=require(process.env.APEX_PLAYWRIGHT_MODULE || 'playwright');const path=require('path'),assert=require('node:assert/strict');
(async()=>{const extension=path.resolve('dist/BrowserExtension');let context;try{
 context=await chromium.launchPersistentContext(path.resolve('.test-tmp/native-video-panel-'+Date.now()),{headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe',args:[`--disable-extensions-except=${extension}`,`--load-extension=${extension}`],viewport:{width:1100,height:750}});
 const worker=context.serviceWorkers()[0]||await context.waitForEvent('serviceworker');console.log('Extension worker loaded');
 const page=await context.newPage();const metadataArgument=process.argv.find(value=>value.startsWith('--metadata-file='));const usePlayer=process.argv.includes('--player-data')||!!metadataArgument;
 const initial=usePlayer?'<script>window.ytInitialPlayerResponse='+JSON.stringify(metadataArgument?JSON.parse(require('fs').readFileSync(metadataArgument.slice(16),'utf8')):{videoDetails:{videoId:'jNQXAC9IVRw',title:'Already loaded qualities'},playabilityStatus:{status:'OK'},streamingData:{adaptiveFormats:[{itag:160,mimeType:'video/mp4; codecs=\"avc1.4D400B\"',height:144,fps:15,bitrate:10000,contentLength:'195278'},{itag:140,mimeType:'audio/mp4; codecs=\"mp4a.40.2\"',bitrate:128000,contentLength:'147211'}]}})+'</script>':'';
 await page.route('https://www.youtube.com/**',route=>route.fulfill({contentType:'text/html',body:initial+'<meta charset="utf-8"><title>Video qualities integration test</title><body style="background:#19201b;color:white;font:16px Segoe UI"><main style="width:950px;margin:40px auto"><h2>Flux · YouTube qualities</h2><video controls style="width:950px;height:535px;background:#26352e"></video></main>'}));
 await page.goto('https://www.youtube.com/watch?v=jNQXAC9IVRw');await page.waitForSelector('[data-apex-video-panel]');
 const cdp=await context.newCDPSession(page);
 function nodes(root){return [root,...[...(root.children||[]),...(root.shadowRoots||[])].flatMap(nodes)];}
 let tree=(await cdp.send('DOM.getDocument',{depth:-1,pierce:true})).root;
 const trigger=nodes(tree).find(node=>node.nodeName==='BUTTON'&&(node.attributes||[]).includes('trigger'));assert.ok(trigger);
 let sources=[];
 for(let i=0;i<45;i++){await page.waitForTimeout(1000);tree=(await cdp.send('DOM.getDocument',{depth:-1,pierce:true})).root;sources=nodes(tree).filter(node=>node.nodeName==='BUTTON'&&(node.attributes||[]).includes('source'));if(sources.length)break;}
 if(!sources.length){const status=nodes(tree).find(node=>(node.attributes||[]).includes('status'));console.log('STATUS',JSON.stringify(status));throw Error('No quality entries returned from real native bridge');}
 const clickAt=Date.now();
 const {object}=await cdp.send('DOM.resolveNode',{backendNodeId:trigger.backendNodeId});await cdp.send('Runtime.callFunctionOn',{objectId:object.objectId,functionDeclaration:'function(){this.click()}'});
 console.log('PASS qualities ready before click; menu click handled in '+(Date.now()-clickAt)+' ms');
 if(usePlayer){const cache=await worker.evaluate(async()=>Object.entries(await chrome.storage.session.get(null)).filter(([key])=>key.startsWith('video-catalog:')).map(([,entry])=>entry.value.source));assert.deepEqual(cache,['player']);console.log('PASS page metadata used; no native format extraction required');}
 console.log(usePlayer?'PASS actual Brave extension read '+sources.length+' player-provided audio/video qualities.':'PASS actual Brave extension → native host → real YouTube extraction: '+sources.length+' audio/video qualities.');
 await page.screenshot({path:'.test-tmp/native-video-qualities-prefetched.png'});
}finally{await context?.close()}})().catch(e=>{console.error(e);process.exitCode=1});
