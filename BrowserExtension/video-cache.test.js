import test from 'node:test';
import assert from 'node:assert/strict';
import {VideoCatalogCache} from './video-cache.js';
function fixture(){const data={};const api={storage:{session:{async get(key){return key===null?{...data}:{[key]:data[key]}},async set(v){Object.assign(data,v)},async remove(keys){for(const key of [].concat(keys))delete data[key]}}}};return {api,data};}
const pause=()=>new Promise(r=>setImmediate(r));
test('concurrent requests share an extraction and restart reads the session cache',async()=>{
 const f=fixture();let calls=0;const cache=new VideoCatalogCache(f.api,load=>load());
 const load=async()=>{calls++;await pause();return {title:'test',formats:[{formatId:'137+140'}]}};
 await Promise.all([cache.get(1,0,'video',load),cache.get(1,0,'video',load)]);
 assert.equal(calls,1);
 const restarted=new VideoCatalogCache(f.api,load=>load());
 assert.equal((await restarted.get(1,0,'video',load)).title,'test');assert.equal(calls,1);
 await restarted.get(1,0,'video',load,true);assert.equal(calls,2);
});
test('only one native extraction runs at once and errors release the next task',async()=>{
 const f=fixture();let active=0,peak=0;
 const cache=new VideoCatalogCache(f.api,load=>load());
 const load=async()=>{active++;peak=Math.max(peak,active);await pause();active--;return {formats:[{}]}};
 await Promise.all(Array.from({length:5},(_,i)=>cache.get(i,0,'page',load)));
 assert.equal(peak,1);
 await assert.rejects(cache.get(8,0,'bad',async()=>{throw Error('no access')}));
 await cache.get(9,0,'ok',load);assert.equal(active,0);
});
test('catalog memory/session caches are bounded and closed-tab results are removed',async()=>{
 const f=fixture();const cache=new VideoCatalogCache(f.api,load=>load(),{maximum:3});
 for(let i=0;i<22;i++)await cache.get(i,0,'page',async()=>({title:String(i)}));
 assert.equal(cache.values.size,3);assert.equal(Object.keys(f.data).length,16);
 let finish;const task=cache.get(99,0,'page',()=>new Promise(resolve=>{finish=resolve}));
 while(!finish)await pause();cache.close(99);finish({title:'closed'});await task;
 assert.equal(f.data[cache.key(99,0,'page')],undefined);assert.equal(cache.closed.size,0);
});
