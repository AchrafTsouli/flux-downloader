import test from 'node:test';
import assert from 'node:assert/strict';
import './media-policy.js';
import { installMedia } from './media-background.js';
const { candidate, merge } = globalThis.ApexMedia;
const header = (name, value) => ({ name, value });
test('progressive files, signed extensionless media and full range size', () => {
  assert.equal(candidate('https://cdn.test/movie.webm?token=abc').format, 'WEBM');
  const item = candidate('https://cdn.test/play?id=42', 'video/mp4; codecs="avc1"', [header('Content-Length', '1024'), header('Content-Range', 'bytes 0-1023/5000000')]);
  assert.equal(item.size, 5000000);
  assert.equal(item.format, 'MP4');
  assert.equal(merge([item])[0].url, item.url);
  assert.equal(candidate('https://cdn.test/a.mp4').size, null);
});
test('reject streams, chunks, credentials, unsafe schemes and unknown page URLs', () => {
  for (const url of ['blob:https://cdn.test/x', 'javascript:alert(1)', 'https://u:p@cdn.test/a.mp4', 'https://cdn.test/a.m3u8', 'https://cdn.test/a.mpd', 'https://cdn.test/a.ts', 'https://cdn.test/a.m4s', 'https://cdn.test/a.mp4?range=0-100', 'https://cdn.test/play?clen=123&itag=137']) assert.equal(candidate(url, 'video/mp4'), null, url);
  assert.equal(candidate('https://cdn.test/page'), null);
  assert.equal(candidate('https://cdn.test/a.mp4', 'application/dash+xml'), null);
  assert.equal(candidate('https://cdn.test/error.mp4', 'text/html'), null);
});
test('deduplicates while preserving metadata and limits the newest sources', () => {
  const url = 'https://cdn.test/a.mp4';
  assert.deepEqual(merge([{url, size: 123}, {url: url + '#x'}]).map(item => item.size), [123]);
  assert.equal(merge(Array.from({length: 50}, (_, i) => ({url: `https://cdn.test/${i}.mp4`})))[0].url, 'https://cdn.test/18.mp4');
});
function fixture() {
  const memory = {}, listeners = {}, sent = [];
  const event = name => ({ addListener(fn) { listeners[name] = fn; } });
  const api = {
    runtime: { id: 'extension', onMessage: event('message'), connectNative() {
      const port = { onMessage: event('nativeReply'), onDisconnect: event('disconnect'), disconnect() {}, postMessage(request) { sent.push(request); queueMicrotask(() => listeners.nativeReply(request.action === 'video-formats' ? {ok:true,video:{title:'Video fixture',formats:[{formatId:'137+140',label:'MP4 · 1080p · video + audio',extension:'mp4',totalBytes:120,approximate:false}]}} : {ok:true})); } }; return port;
    } },
    webRequest: { onResponseStarted: event('response') }, tabs: { onRemoved: event('removed') },
    storage: { local: { async get() { return {captureEnabled:true, videoPanelEnabled:true}; } }, session: { async get(key) { return key === null ? {...memory} : {[key]:memory[key]}; }, async set(values) { Object.assign(memory, values); }, async remove(keys) { for (const key of [].concat(keys)) delete memory[key]; } } }
  };
  installMedia(api);
  const sender = {id:'extension', tab:{id:42}, frameId:2, documentId:'doc-current', url:'https://page.test/player'};
  const request = message => new Promise(resolve => listeners.message(message, sender, resolve));
  const observe = (url, documentId = sender.documentId) => listeners.response({url, tabId:42, frameId:2, statusCode:206, documentId, documentUrl:sender.url, responseHeaders:[header('Content-Type','video/webm'),header('Content-Range','bytes 0-100/4000000')]});
  return { api, listeners, sender, request, observe, sent };
}
test('media detection preserves concurrent responses, frame scope and navigation isolation', async () => {
  const f = fixture();
  f.observe('https://cdn.test/one'); f.observe('https://cdn.test/two');
  const result = await f.request({action:'media-list'});
  assert.equal(result.items.length, 2); assert.equal(result.items[0].size, 4000000);
  f.sender.documentId = 'new-document';
  assert.equal((await f.request({action:'media-list'})).items.length, 0);
  f.sender.frameId = 3;
  assert.equal((await f.request({action:'media-list'})).items.length, 0);
});
test('selected media goes directly to native app with frame referer, no browser download', async () => {
  const f = fixture(); const url = 'https://cdn.test/video'; f.observe(url);
  assert.equal((await f.request({action:'media-download',url})).ok, true);
  assert.equal(f.sent[0].url, url); assert.equal(f.sent[0].session.referer, f.sender.url);
  assert.equal((await f.request({action:'media-download',url:'https://cdn.test/not-observed'})).ok, false);
});
test('disabled panel and excluded sites cannot dispatch a video', async () => {
  const f = fixture();
  f.api.storage.local.get = async () => ({captureEnabled:true,videoPanelEnabled:false});
  assert.equal((await f.request({action:'media-list',sources:[{url:'https://cdn.test/a.mp4'}]})).ok, false);
  f.api.storage.local.get = async () => ({captureEnabled:true,videoPanelEnabled:true,capturePolicy:{excludedSites:'page.test'}});
  assert.equal((await f.request({action:'media-list'})).ok, false);
  assert.equal(f.sent.length, 0);
});

test('video handoff collects partitioned cookies from the initiating embedded frame', async () => {
  const f = fixture();
  let partitionQuery;
  f.api.cookies = {
    async getAllCookieStores() { return [{id:'private-store',tabIds:[42]}]; },
    async getPartitionKey(query) { partitionQuery = query; return {partitionKey:{topLevelSite:'https://page.test'}}; },
    async getAll(query) { assert.equal(query.storeId,'private-store'); return []; }
  };
  f.observe('https://cdn.test/embedded');
  assert.equal((await f.request({action:'media-download',url:'https://cdn.test/embedded'})).ok,true);
  assert.deepEqual(partitionQuery,{tabId:42,frameId:2});
});
test('media messages from other extensions or without a tab are ignored', () => {
  const f=fixture();
  for(const sender of [{...f.sender,id:'other-extension'},{id:'extension'}])
    assert.equal(f.listeners.message({action:'media-download',url:'https://cdn.test/a.mp4'},sender,()=>assert.fail('untrusted reply')),undefined);
  assert.equal(f.sent.length,0);
});

test('resource timing recovers direct URLs when the media source is a blob', async () => {
  const f=fixture();
  const result=await f.request({action:'media-list',resources:[{url:'https://cdn.test/recovered.mp4'}]});
  assert.equal(result.ok,true);
  assert.ok(result.items.some(item=>item.url==='https://cdn.test/recovered.mp4'));
});
test('inherited about:srcdoc frames use their browser-provided origin for capture policy', async () => {
  const f=fixture(); f.sender.url='about:srcdoc'; f.sender.origin='https://page.test';
  const result=await f.request({action:'media-list',sources:[{url:'https://cdn.test/inherited.mp4'}]});
  assert.equal(result.ok,true);
  assert.ok(result.items.some(item=>item.url==='https://cdn.test/inherited.mp4'));
});

test('YouTube qualities are extracted on demand and dispatch a typed page job with audio', async () => {
  const f=fixture(); f.sender.url='https://www.youtube.com/watch?v=jNQXAC9IVRw';
  const list=await f.request({action:'media-list'});
  assert.equal(list.videoPage,f.sender.url); assert.equal(f.sent.length,0);
  const qualities=await f.request({action:'video-formats'});
  assert.equal(qualities.video.formats[0].formatId,'137+140');
  assert.equal((await f.request({action:'video-download',formatId:'137+140'})).ok,true);
  const job=f.sent.find(request=>request.action==='download');
  assert.equal(job.url,f.sender.url); assert.equal(job.video.formatId,'137+140'); assert.equal(job.fileName,'Video fixture (1080p).mp4');
  assert.equal((await f.request({action:'video-download',formatId:'--exec'})).ok,false);
  assert.equal(f.sent.filter(request=>request.action==='video-formats').length,1);
});
test('video-page messages cannot switch to a different origin', async () => {
  const f=fixture();
  const response=await f.request({action:'video-formats',pageUrl:'https://www.youtube.com/watch?v=jNQXAC9IVRw'});
  assert.equal(response.ok,false); assert.equal(f.sent.length,0);
});
test('YouTube page parsing supports embeds and rejects lookalike domains',()=>{
  assert.equal(ApexMedia.videoPage('https://www.youtube-nocookie.com/embed/jNQXAC9IVRw'),'https://www.youtube.com/watch?v=jNQXAC9IVRw');
  assert.equal(ApexMedia.videoPage('https://youtube.com.evil.test/watch?v=jNQXAC9IVRw'),null);
});
test('already loaded player metadata prepares qualities without starting native tools',async()=>{
 const f=fixture();f.sender.url='https://www.youtube.com/watch?v=jNQXAC9IVRw';
 f.api.scripting={async executeScript(query){assert.equal(query.world,'MAIN');assert.deepEqual(query.target.frameIds,[2]);return [{result:{id:'jNQXAC9IVRw',title:'Ready player',formats:[{id:'137',type:'video/mp4; codecs="avc1.640028"',height:1080,fps:30,bitrate:1000,size:100},{id:'140',type:'audio/mp4; codecs="mp4a.40.2"',bitrate:100,size:20}]}}]}};
 const ready=await f.request({action:'video-formats',prefetch:true});assert.equal(ready.video.source,'player');assert.equal(f.sent.length,0);
 await f.request({action:'video-download',formatId:'137+140'});assert.equal(f.sent.length,1);assert.equal(f.sent[0].action,'download');
});

test('other video sites are recognized as single-video pages; listings and lookalikes are not',()=>{
  for (const url of ['https://vimeo.com/76979871','https://x.com/user/status/1234567890','https://www.tiktok.com/@a.b/video/7000000000000000000','https://www.instagram.com/reel/AbC_d-1/','https://www.dailymotion.com/video/x8abc12','https://www.twitch.tv/videos/123456','https://www.facebook.com/watch/?v=123'])
    assert.ok(ApexMedia.videoPage(url),url);
  for (const url of ['https://vimeo.com/','https://x.com/home','https://www.facebook.com/watch/','https://vimeo.com.evil.test/123','javascript:alert(1)'])
    assert.equal(ApexMedia.videoPage(url),null,url);
  assert.equal(ApexMedia.isYouTube('https://vimeo.com/1'),false);
});
test('HLS and DASH manifests are recognized by path or content type only',()=>{
  assert.deepEqual(ApexMedia.stream('https://cdn.test/live/master.m3u8?token=1#x'),{url:'https://cdn.test/live/master.m3u8?token=1',kind:'HLS'});
  assert.equal(ApexMedia.stream('https://cdn.test/manifest','application/dash+xml').kind,'DASH');
  assert.equal(ApexMedia.stream('https://cdn.test/play','application/vnd.apple.mpegurl').kind,'HLS');
  assert.equal(ApexMedia.stream('https://cdn.test/a.mp4','video/mp4'),null);
  assert.equal(ApexMedia.stream('https://u:p@cdn.test/a.m3u8'),null);
});
test('a stream the player loaded lists qualities and dispatches a job titled after the tab', async () => {
  const f=fixture(); f.sender.tab.title='Concert recording';
  f.listeners.response({url:'https://cdn.test/hls/master.m3u8',tabId:42,frameId:2,statusCode:200,documentId:f.sender.documentId,documentUrl:f.sender.url,responseHeaders:[header('Content-Type','application/vnd.apple.mpegurl')]});
  f.listeners.response({url:'https://cdn.test/hls/720.m3u8',tabId:42,frameId:2,statusCode:200,documentId:f.sender.documentId,documentUrl:f.sender.url,responseHeaders:[]});
  const list=await f.request({action:'media-list'});
  assert.equal(list.stream,'https://cdn.test/hls/master.m3u8'); assert.equal(list.streamKind,'HLS');
  const formats=await f.request({action:'video-formats',streamUrl:list.stream});
  assert.equal(formats.ok,true); assert.equal(f.sent[0].url,list.stream); assert.equal(f.sent[0].session.referer,f.sender.url);
  assert.equal((await f.request({action:'video-download',formatId:'137+140',streamUrl:list.stream})).ok,true);
  const job=f.sent.find(request=>request.action==='download');
  assert.equal(job.url,list.stream); assert.equal(job.fileName,'Concert recording (1080p).mp4');
});
test('a page cannot request formats for a manifest its frame never loaded', async () => {
  const f=fixture();
  const response=await f.request({action:'video-formats',streamUrl:'https://elsewhere.test/secret.m3u8'});
  assert.equal(response.ok,false); assert.equal(f.sent.length,0);
  f.sender.documentId='other-document';
  f.listeners.response({url:'https://cdn.test/old.m3u8',tabId:42,frameId:2,statusCode:200,documentId:'doc-current',documentUrl:f.sender.url,responseHeaders:[]});
  assert.equal((await f.request({action:'media-list'})).stream,null);
});

test('any page can be searched on request, never as an automatic prefetch', async () => {
  const f=fixture(); f.sender.url='https://unknown-site.test/watch/42#comments'; f.sender.documentId='doc-generic';
  const list=await f.request({action:'media-list'});
  assert.equal(list.genericPage,'https://unknown-site.test/watch/42'); assert.equal(list.videoPage,null);
  assert.equal((await f.request({action:'video-formats',generic:true,prefetch:true})).deferred,true);
  assert.equal(f.sent.length,0);
  assert.equal((await f.request({action:'video-formats'})).ok,false, 'generic search requires the explicit flag');
  const found=await f.request({action:'video-formats',generic:true});
  assert.equal(found.ok,true); assert.equal(f.sent[0].url,'https://unknown-site.test/watch/42');
  assert.equal((await f.request({action:'video-download',formatId:'137+140',generic:true})).ok,true);
  assert.equal(f.sent.find(request=>request.action==='download').url,'https://unknown-site.test/watch/42');
});
test('manifests fetched by a player worker and extensionless HLS paths are captured', async () => {
  const f=fixture(); f.sender.frameId=0; f.sender.url='https://worker-player.test/'; f.sender.documentId='doc-worker';
  f.listeners.response({url:'https://cdn.test/play/master.m3u8/index?x=1',tabId:42,frameId:-1,statusCode:200,documentUrl:undefined,initiator:'https://worker-player.test',responseHeaders:[]});
  assert.equal((await f.request({action:'media-list'})).stream,'https://cdn.test/play/master.m3u8/index?x=1');
  assert.equal(ApexMedia.stream('https://cdn.test/hls?format=x.m3u8&t=1').kind,'HLS');
});
