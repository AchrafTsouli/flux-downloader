import test from 'node:test';
import assert from 'node:assert/strict';
import './video-panel-policy.js';
const { playing, choose, clamp } = globalThis.ApexVideoPanelPolicy;
const video = { paused: false, ended: false, error: null, readyState: 3, videoWidth: 1280, videoHeight: 720 };
const rect = { width: 640, height: 360, top: 80, bottom: 440, left: 20, right: 660 };
test('a paused or finished video keeps its panel once playback started', () => {
  for (const patch of [{paused:true},{paused:true,ended:true}]) {
    assert.equal(playing({...video,...patch}, rect, 1280, 720, true), true, JSON.stringify(patch));
    assert.equal(playing({...video,...patch}, rect, 1280, 720, false), false, 'never-played ' + JSON.stringify(patch));
  }
  for (const patch of [{error:{}},{mediaKeys:{}},{readyState:1}]) assert.equal(playing({...video,paused:true,...patch}, rect, 1280, 720, true), false, JSON.stringify(patch));
  assert.equal(playing({...video,paused:true}, {...rect,top:800,bottom:1160}, 1280, 720, true), false, 'scrolled away');
});
test('only a playing, decoded, visible, unprotected video qualifies', () => {
  assert.equal(playing(video, rect, 1280, 720), true);
  for (const patch of [{paused:true},{ended:true},{error:{}},{readyState:1},{videoWidth:0},{videoHeight:0},{mediaKeys:{}}])
    assert.equal(playing({...video,...patch}, rect, 1280, 720), false, JSON.stringify(patch));
  assert.equal(playing(video, {...rect,top:800,bottom:1160},1280,720),false);
  assert.equal(playing(video, {...rect,width:0},1280,720),false);
});
test('feed selects one audible player instead of every autoplay preview', () => {
  const preview={video:'preview',audible:false,started:20,area:90000};
  const main={video:'main',audible:true,started:10,area:60000};
  assert.equal(choose([preview,main]),'main');
  assert.equal(choose([preview,{...preview,video:'new',started:21}]),'new');
  assert.equal(choose([]),null);
});
test('drag stays within viewport including menu expansion and resize', () => {
  assert.deepEqual(clamp({x:900,y:700},{width:410,height:300},{width:1000,height:800}),{x:586,y:496});
  assert.deepEqual(clamp({x:-20,y:-50},{width:200,height:30},{width:1000,height:800}),{x:4,y:4});
  assert.deepEqual(clamp({x:400,y:400},{width:410,height:300},{width:320,height:200}),{x:4,y:4});
  assert.deepEqual(clamp({x:80,y:90},{width:200,height:30},{width:1000,height:800}),{x:80,y:90});
});
