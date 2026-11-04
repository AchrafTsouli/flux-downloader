// Local Chromium smoke test. Set APEX_PLAYWRIGHT_MODULE if Playwright is not on NODE_PATH.
const {chromium}=require(process.env.APEX_PLAYWRIGHT_MODULE || 'playwright');
const http=require('node:http');
const fs=require('node:fs/promises');
const path=require('node:path');
const assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..');
const out=path.join(root,'.test-tmp','video-panel');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  let clip;
  const server=http.createServer((req,res)=>{
    if(req.url==='/clip.webm' && clip){res.writeHead(200,{'Content-Type':'video/webm'});return res.end(clip);}
    if(req.url==='/broken.webm'){res.writeHead(404);return res.end();}
    res.writeHead(200,{'Content-Type':'text/html'});
    res.end('<!doctype html><title>Apex local video panel test</title><style>body{margin:20px;background:#242826;color:white;height:1800px;font:16px Arial}video{display:inline-block;background:black;width:280px;height:180px;margin:5px}h1{font-size:22px}</style><h1>Preview / playing / failed player</h1><video id="preview" muted loop></video><video id="main" loop></video><video id="failed"></video>');
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let browser;
  try {
    browser=await chromium.launch({executablePath:process.env.APEX_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
    const page=await browser.newPage({viewport:{width:1100,height:750}});
    const errors=[];page.on('pageerror',error=>errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    // Control media state deterministically; exercise the real DOM and real pointer input.
    await page.evaluate(()=>{
      window.testRoots=new WeakMap();const attach=Element.prototype.attachShadow;
      Element.prototype.attachShadow=function(options){const shadow=attach.call(this,options);window.testRoots.set(this,shadow);return shadow;};
      window.chrome={storage:{local:{get:async()=>({captureEnabled:true,videoPanelEnabled:true})},onChanged:{addListener(){},removeListener(){}}},runtime:{sendMessage:async message=>{
        if(message.action==='media-list')return {ok:true,items:message.sources.map(s=>ApexMedia.candidate(s.url,s.type)).filter(Boolean),videoPage:null};
        return {ok:true};
      }}};
      window.playerState={};
      for(const id of ['preview','main','failed']){
        const v=document.getElementById(id),state={paused:true,source:location.origin+'/clip.webm',error:id==='failed'?{}:null};window.playerState[id]=state;
        for(const [key,get] of Object.entries({paused:()=>state.paused,ended:()=>false,readyState:()=>state.error?0:3,videoWidth:()=>320,videoHeight:()=>180,error:()=>state.error,currentSrc:()=>state.source}))Object.defineProperty(v,key,{get,configurable:true});
        v.play=async()=>{state.paused=false;v.dispatchEvent(new Event('play'));};
        v.pause=()=>{state.paused=true;v.dispatchEvent(new Event('pause'));};
      }
    });
    for(const file of ['media-policy.js','video-panel-policy.js','video-panel.js'])await page.addScriptTag({path:path.join(root,'BrowserExtension',file)});
    const visible=()=>page.evaluate(()=>[...document.querySelectorAll('[data-apex-video-panel]')].filter(h=>getComputedStyle(h).display!=='none').length);
    await page.waitForTimeout(300);assert.equal(await visible(),0,'paused previews and failed source must not show a panel');
    await page.evaluate(()=>{document.querySelector('#preview').play();document.querySelector('#main').play();});
    await page.waitForFunction(()=>[...document.querySelectorAll('[data-apex-video-panel]')].filter(h=>getComputedStyle(h).display!=='none').length===1, null, {timeout:10000}).catch(async error=>{
      console.error(errors,await page.evaluate(()=>({videos:[...document.querySelectorAll('video')].map(v=>({id:v.id,paused:v.paused,state:v.readyState,w:v.videoWidth,error:v.error?.message,src:v.currentSrc})),panels:[...document.querySelectorAll('[data-apex-video-panel]')].map(h=>({style:h.style.cssText,text:window.testRoots.get(h).textContent})),version:window.apexVideoPanelVersion})));throw error;
    });
    const barBox=()=>page.evaluate(()=>{
      const host=[...document.querySelectorAll('[data-apex-video-panel]')].find(h=>getComputedStyle(h).display!=='none');const shadow=window.testRoots.get(host),rect=shadow.querySelector('.trigger').getBoundingClientRect();
      return {x:rect.x,y:rect.y,width:rect.width,height:rect.height,menuHidden:shadow.querySelector('.menu').hidden};
    });
    const start=await barBox();assert.ok(start.x>300,'audible main player should be selected over autoplay preview');
    await page.screenshot({path:path.join(out,'playing.png')});
    await page.mouse.click(start.x+60,start.y+12);assert.equal((await barBox()).menuHidden,false,'normal click should still open list');
    await page.mouse.click(start.x+60,start.y+12);assert.equal((await barBox()).menuHidden,true);
    await page.mouse.move(start.x+60,start.y+12);await page.mouse.down();await page.mouse.move(start.x+240,start.y+180,{steps:12});await page.mouse.up();
    const moved=await barBox();assert.ok(moved.x>start.x+140 && moved.y>start.y+140,'bar must move with mouse');assert.equal(moved.menuHidden,true,'drag must not open the list');
    await page.evaluate(()=>scrollTo(0,40));await page.waitForTimeout(250);const scrolled=await barBox();assert.ok(Math.abs(scrolled.x-moved.x)<2 && Math.abs(scrolled.y-moved.y)<2,'manual placement survives scrolling');
    await page.setViewportSize({width:650,height:450});await page.waitForTimeout(250);const resized=await barBox();assert.ok(resized.x+resized.width<=646 && resized.y+resized.height<=446,'panel stays inside resized viewport');
    await page.evaluate(()=>document.querySelectorAll('video').forEach(video=>video.pause()));await page.waitForTimeout(300);assert.equal(await visible(),0,'paused videos hide panel');
    // A playing browser-only stream has no complete downloadable source.
    await page.evaluate(async()=>{window.playerState.main.source='blob:unsupported';await document.querySelector('#main').play();});
    await page.waitForTimeout(500);assert.equal(await visible(),0,'unsupported source must stay hidden while playing');
    assert.deepEqual(errors,[],'content script must not throw');
    await fs.writeFile(path.join(out,'result.json'),JSON.stringify({passed:true,checks:['paused and failed sources hidden','one audible playing video','menu click','real pointer drag','no click after drag','placement survives scroll','resize clamps position','pause hides panel','unsupported stream hidden'],errors},null,2));
    console.log('Chromium DOM/pointer smoke test passed (simulated media state). Evidence: '+out);
  } finally {if(browser)await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});


