const {chromium}=require(process.env.APEX_PLAYWRIGHT_MODULE || 'playwright');
const http=require('http'),path=require('path'),assert=require('node:assert/strict');
(async()=>{
 let base;
 const server=http.createServer((req,res)=>{
  if(req.url.startsWith('/media')){res.writeHead(200,{'Content-Type':'video/mp4','Content-Length':'24'});res.end(Buffer.alloc(24));return;}
  let content;
  const video=`<video controls src="${base}/media.mp4" style="width:800px;height:450px"></video>`;
  if(req.url==='/shadow')content=`<div id="player"></div><script>setTimeout(()=>{document.querySelector('#player').attachShadow({mode:'open'}).innerHTML=${JSON.stringify(video)}},200)</script>`;
  else if(req.url==='/blob')content=`<video controls style="width:800px;height:450px"></video><script>document.querySelector('video').src=URL.createObjectURL(new Blob(['fixture'],{type:'video/mp4'}))</script>`;
  else if(req.url==='/frame')content=`<iframe style="width:850px;height:520px" srcdoc='${video}'></iframe>`;
  else content=video;
  res.writeHead(200,{'Content-Type':'text/html'});res.end(`<title>Actual Flux extension test</title><body style="background:#222;color:white"><h1>${req.url}</h1>${content}`);
 });
 await new Promise(r=>server.listen(0,'127.0.0.1',r));base='http://127.0.0.1:'+server.address().port;
 let context;
 try{
 const folder=path.resolve('BrowserExtension');
 context=await chromium.launchPersistentContext(path.resolve('.test-tmp/extension-visibility-'+Date.now()),{headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe',args:[`--disable-extensions-except=${folder}`,`--load-extension=${folder}`],viewport:{width:1000,height:700}});
 const worker=context.serviceWorkers()[0]||await context.waitForEvent('serviceworker',{timeout:15000});
 const page=await context.newPage();
 for(const route of ['/direct','/shadow','/blob','/frame']){
  await page.goto(base+route);
  const frame=route==='/frame'?page.frames().find(frame=>frame.parentFrame()):page.mainFrame();
  await frame.waitForFunction(()=>{const panel=document.querySelector('[data-apex-video-panel]');return panel&&getComputedStyle(panel).display!=='none';},null,{timeout:10000});
  if(route==='/blob'){
   const cdp=await context.newCDPSession(page);let {root}=await cdp.send('DOM.getDocument',{depth:-1,pierce:true});
   function find(node){if(node.nodeName==='BUTTON'&&(node.attributes||[]).includes('trigger'))return node;for(const child of [...(node.children||[]),...(node.shadowRoots||[])]){const found=find(child);if(found)return found;}}
   const button=find(root);assert.ok(button);
   const {object}=await cdp.send('DOM.resolveNode',{backendNodeId:button.backendNodeId});
   await cdp.send('Runtime.callFunctionOn',{objectId:object.objectId,functionDeclaration:'function(){this.click()}'});
   await page.waitForTimeout(200);root=(await cdp.send('DOM.getDocument',{depth:-1,pierce:true})).root;
   assert.ok(JSON.stringify(root).includes('Streaming player detected.'));
   await page.screenshot({path:'.test-tmp/blob-video-panel-fixed.png'});
  }
  console.log('PASS visible actual extension panel: '+route);
 }
 await page.goto(base+'/direct');await page.waitForSelector('[data-apex-video-panel]');
 const tab=await worker.evaluate(async(base)=>(await chrome.tabs.query({})).find(tab=>tab.url===base+'/direct'),base);
 for(let i=0;i<2;i++)await worker.evaluate(tabId=>chrome.scripting.executeScript({target:{tabId,allFrames:true},files:['media-policy.js','video-panel.js']}),tab.id);
 assert.equal(await page.locator('[data-apex-video-panel]').count(),1);
 console.log('PASS repeated script injection does not duplicate panels');
 }finally{await context?.close();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
