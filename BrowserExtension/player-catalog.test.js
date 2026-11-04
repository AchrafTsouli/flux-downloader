import test from 'node:test';import assert from 'node:assert/strict';
import {playerCatalog,readPlayerMetadata} from './player-catalog.js';
const id='jNQXAC9IVRw';
const formats=[{id:'137',type:'video/mp4; codecs="avc1.640028"',height:1080,fps:60,bitrate:10000,size:100},{id:'140',type:'audio/mp4; codecs="mp4a.40.2"',bitrate:1000,size:20}];
test('player metadata yields a complete typed video+audio choice without media URLs',()=>{
 const result=playerCatalog({id,title:'Video',formats},id);assert.equal(result.source,'player');assert.equal(result.formats[0].formatId,'137+140');assert.equal(result.formats[0].totalBytes,120);assert.match(result.formats[0].label,/1080p.*60 fps.*H.264/);assert.equal(JSON.stringify(result).includes('https://'),false);
});
test('stale player IDs, audio-less and malformed formats are ignored',()=>{
 assert.equal(playerCatalog({id:'wrong',formats},id),null);assert.equal(playerCatalog({id,formats:[formats[0]]},id),null);
 assert.equal(playerCatalog({id,formats:[{...formats[0],id:'--exec'},formats[1]]},id),null);
});
test('MAIN-world reader copies only metadata and refuses protected or stale responses',()=>{
 const oldWindow=globalThis.window,oldDocument=globalThis.document;
 try{
  globalThis.document={getElementById(){return null}};
  globalThis.window={ytInitialPlayerResponse:{videoDetails:{videoId:id,title:'Title'},playabilityStatus:{status:'OK'},streamingData:{adaptiveFormats:[{itag:137,mimeType:'video/mp4',url:'https://cdn.test/private-token',signatureCipher:'secret',height:1080}]}}};
  const data=readPlayerMetadata(id);assert.equal(data.formats[0].id,'137');assert.equal(JSON.stringify(data).includes('private-token'),false);assert.equal(JSON.stringify(data).includes('secret'),false);
  assert.equal(readPlayerMetadata('aaaaaaaaaaa'),null);window.ytInitialPlayerResponse.streamingData.licenseInfos=[{}];assert.equal(readPlayerMetadata(id),null);
 }finally{if(oldWindow===undefined)delete globalThis.window;else globalThis.window=oldWindow;if(oldDocument===undefined)delete globalThis.document;else globalThis.document=oldDocument;}
});
