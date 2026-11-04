// Deduplicates format extraction and bounds expensive native tool processes.
export class VideoCatalogCache {
  constructor(api, extract, {ttl=15*60*1000, maximum=8}={}) { this.api=api;this.extract=extract;this.ttl=ttl;this.maximum=maximum;this.values=new Map();this.pending=new Map();this.tail=Promise.resolve();this.closed=new Set(); }
  key(tab,frame,page) { return `video-catalog:${tab}:${frame}:${page}`; }
  async get(tab,frame,page,load,refresh=false,quickLoad=null) {
    const key=this.key(tab,frame,page);
    if(this.closed.has(tab)) throw new Error('The video tab was closed.');
    if(this.pending.has(key)) return this.pending.get(key);
    const task=(async()=>{
      let saved=this.values.get(key);
      if(!saved) saved=(await this.api.storage.session.get(key))[key];
      if(this.closed.has(tab))throw new Error('The video tab was closed.');
      if(!refresh && saved && Date.now()-saved.at<this.ttl) {this.remember(key,saved);return saved.value;}
      if (!refresh && quickLoad) { const ready = await quickLoad(); if (ready) { await this.persist(key,tab,ready); return ready; } }
      const preceding=this.tail;let release;
      this.tail=new Promise(resolve=>{release=resolve;});
      await preceding;
      try {
        if(this.closed.has(tab))throw new Error('The video tab was closed.');
        const value=await this.extract(load);
        await this.persist(key,tab,value);
        return value;
      } finally {release();}
    })();
    this.pending.set(key,task);
    try{return await task;}finally{
      this.pending.delete(key);
      if(this.closed.has(tab)){
        await this.api.storage.session.remove(key);
        if(![...this.pending.keys()].some(name=>name.startsWith(`video-catalog:${tab}:`)))this.closed.delete(tab);
      }
    }
  }
  async persist(key,tab,value) {
    if(!this.closed.has(tab)) {
          const entry={at:Date.now(),value};this.remember(key,entry);await this.api.storage.session.set({[key]:entry});
          const stored=await this.api.storage.session.get(null);
          const items=Object.entries(stored).filter(([name])=>name.startsWith('video-catalog:')).sort((a,b)=>(b[1]?.at||0)-(a[1]?.at||0));
          const stale=items.filter(([,item],i)=>i>=16||Date.now()-(item?.at||0)>=this.ttl).map(([name])=>name);
          if(stale.length)await this.api.storage.session.remove(stale);
        }
  }
  remember(key,value) {this.values.delete(key);this.values.set(key,value);while(this.values.size>this.maximum)this.values.delete(this.values.keys().next().value);}
  close(tab) {if([...this.pending.keys()].some(name=>name.startsWith(`video-catalog:${tab}:`)))this.closed.add(tab);for(const key of this.values.keys())if(key.startsWith(`video-catalog:${tab}:`))this.values.delete(key);}
}
