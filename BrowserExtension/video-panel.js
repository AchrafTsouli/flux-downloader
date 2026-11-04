(() => {
  if (globalThis.apexVideoPanelVersion === '1.0.0') return;
  globalThis.apexVideoPanelDispose?.();
  globalThis.apexVideoPanelVersion = '1.0.0';
  const events = new AbortController();
  document.querySelectorAll('[data-apex-video-panel]').forEach(host => host.remove());
  const panels = new Map();
  const playbackStarted = new WeakMap();
  let activeVideo = null;
  const mediaEvents = ['play', 'playing', 'pause', 'ended', 'error', 'emptied', 'loadeddata', 'loadedmetadata', 'volumechange', 'encrypted'];
  const playbackChanged = event => {
    if (event.type === 'play') playbackStarted.set(event.target, performance.now());
    requestScan();
  };
  let options = { captureEnabled: true, videoPanelEnabled: true, capturePolicy: {} };
  let updating = false;
  let discoveredVideos = [];
  let discoveryDirty = true;
  let discoveryAt = 0;
  const startedAt = Date.now();
  let polledAt = 0;
  let refreshTimer;
  const requestScan = () => { clearTimeout(refreshTimer); refreshTimer = setTimeout(scan, 100); };
  const started = video => playbackStarted.has(video) || video.currentTime > 0;
  const read = () => chrome.storage.local.get(['captureEnabled', 'videoPanelEnabled', 'capturePolicy']).then(value => { options = value; scan(); }).catch(() => {});
  const storageChanged = (changes, area) => { if (area === 'local' && ['captureEnabled', 'videoPanelEnabled', 'capturePolicy'].some(key => changes[key])) read(); };
  chrome.storage.onChanged.addListener(storageChanged);
  const allowed = () => options.captureEnabled !== false && options.capturePolicy?.enabled !== false && (options.videoPanelEnabled ?? true);
  function sources(video) {
    return [{ url: video.currentSrc || video.src, type: video.getAttribute('type') || '' }, ...[...video.querySelectorAll('source')].map(source => ({ url: source.src, type: source.type }))].filter(item => /^https?:\/\//i.test(item.url));
  }
  function videosIn(root = document) {
    if (root !== document && !watchedRoots.has(root)) {
      watchedRoots.add(root); observer.observe(root,{childList:true,subtree:true});
      for (const type of mediaEvents) root.addEventListener(type,playbackChanged,{capture:true,signal:events.signal});
    }
    const result = [...root.querySelectorAll('video')];
    for (const element of root.querySelectorAll('*')) if (element.shadowRoot) result.push(...videosIn(element.shadowRoot));
    return result;
  }
  function resourceSources() {
    return performance.getEntriesByType('resource').slice(-300).map(entry => ({url:entry.name})).filter(item => ApexMedia.candidate(item.url)).slice(-32);
  }
  const sizeText = size => !size ? 'Size unknown' : size >= 1073741824 ? `${(size / 1073741824).toFixed(2)} GB` : `${(size / 1048576).toFixed(2)} MB`;
  function makePanel(video) {
    const host = document.createElement('div');
    host.setAttribute('data-apex-video-panel', '1.0.0');
    host.style.cssText = 'all:initial!important;position:fixed!important;z-index:2147483647!important;display:none!important;';
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `<style>
      :host{color-scheme:dark}*{box-sizing:border-box} .panel{font:12px/1.4 "Segoe UI",Arial,sans-serif;color:#e6eef5;direction:ltr;text-align:left}
      button{font:inherit;color:inherit;cursor:pointer}button:focus-visible{outline:2px solid #7dd3fc;outline-offset:2px}button:disabled{opacity:.6;cursor:wait}
      .bar{display:flex;width:max-content;max-width:calc(100vw - 8px);border:1px solid #2f5b80;border-radius:5px;background:#141c25;box-shadow:0 2px 9px #0006;overflow:hidden;touch-action:none;user-select:none;cursor:grab}
      .move{border:0;background:transparent;padding:3px 6px;color:#9fb3c6;cursor:grab}.dragging,.dragging button{cursor:grabbing}
      .trigger{border:0;background:transparent;padding:5px 10px;font-weight:600}.arrow{color:#38bdf8;margin-right:6px}.close{border:0;border-left:1px solid #2f5b80;background:transparent;padding:3px 9px;color:#9fb3c6}
      button:hover{background:#1d2c3c}.menu{margin-top:4px;width:min(410px,calc(100vw - 20px));padding:6px;background:#111922;border:1px solid #2c3a4a;border-radius:7px;box-shadow:0 7px 25px #0007}
      [hidden]{display:none!important}.heading{padding:5px 7px;color:#9fb3c6;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.all{width:100%;text-align:left;background:#173451;border:0;border-radius:4px;padding:7px 9px;color:#7dd3fc}
      .list{max-height:220px;overflow:auto;margin-top:4px}.source{display:block;width:100%;text-align:left;background:transparent;border:0;border-radius:4px;padding:8px}.source strong{display:block;font-weight:600}.meta{display:block;color:#8496a8;font-size:11px;margin-top:2px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
      .load{margin:4px 0;background:transparent;color:#a9bccd;font-size:11px;border:1px solid #2c3a4a}
      .status{padding:6px 7px;color:#c4d1dd;font-size:11px}.note{padding:4px 7px;color:#8496a8;font-size:11px}
    </style><div class="panel"><div class="bar"><button class="trigger" aria-expanded="false" aria-haspopup="true"><span class="arrow">▼</span>Download this video</button><button class="close" aria-label="Hide video download panel" title="Hide for this player">×</button></div><div class="menu" hidden><div class="heading"></div><button class="all">↓ Download all sources</button><button class="load all" hidden>Load available qualities</button><div class="list"></div><div class="status" role="status" aria-live="polite"></div><div class="note">Flux video downloads · audio included in listed qualities</div></div></div>`;
    const select = selector => root.querySelector(selector);
    const panel = { video, host, root, items: [], signature: '', busy: false, loading: false, catalog: null, page: null, dismissed: false, source: video.currentSrc || video.src };
    const bar = select('.bar');
    const move = document.createElement('button'); move.className = 'move'; move.textContent = '⠿';
    move.setAttribute('aria-label', 'Move video download panel'); move.title = 'Drag to move · arrow keys to move · Home to reset'; bar.prepend(move);
    const menu = select('.menu'), trigger = select('.trigger'), list = select('.list'), status = select('.status');
    const title = () => panel.catalog?.title || video.getAttribute('title') || document.title || 'Video';
    function draw(items) {
      panel.items = items;
      const signature = JSON.stringify(items.map(item => [item.url, item.formatId, item.size, item.label, item.format]));
      if (signature === panel.signature) return;
      panel.signature = signature;
      list.replaceChildren(); select('.heading').textContent = title(); select('.heading').title = title();
      select('.all').hidden = items.length < 2;
      if (!panel.busy) status.textContent = ''; 
      for (const [index, item] of items.entries()) {
        const button = document.createElement('button'); button.className = 'source';
        const label = document.createElement('strong'); label.textContent = `${index + 1}. ${item.label || item.format + " video"} · ${item.approximate ? "≈ " : ""}${sizeText(item.size)}`;
        const meta = document.createElement('span'); meta.className = 'meta'; meta.textContent = `${title()} · ${new URL(item.url || panel.page).hostname}`;
        button.append(label, meta); button.addEventListener('click', () => download([item])); list.append(button);
      }
    }
    async function refresh() {
      const requestedSource = video.currentSrc || video.src;
      const requestedLocation = location.href;
      try {
        const response = await chrome.runtime.sendMessage({ action: 'media-list', sources: sources(video), resources: resourceSources(), pageUrl: location.href });
        if (requestedSource !== (video.currentSrc || video.src) || requestedLocation !== location.href || !video.isConnected) return;
        if (response?.ok) {
          const target = response.videoPage || response.stream || response.genericPage || null;
          if (panel.page !== target) {
            panel.page = target; panel.isStream = !response.videoPage && !!response.stream;
            panel.generic = !response.videoPage && !response.stream && !!response.genericPage;
            panel.catalog = null; panel.loadError = false;
            select('.load').textContent = panel.generic ? 'Find available qualities with Flux' : 'Load available qualities';
          }
          select('.load').hidden = !panel.page || !!video.mediaKeys;
          const direct = sources(video);
          const own = response.items.filter(item => direct.some(source => source.url.split('#')[0] === item.url));
          draw(video.mediaKeys ? [] : panel.catalog ? panel.catalog.formats.map(item => ({...item,size:item.totalBytes})) : direct.length ? own : discoveredVideos.length === 1 ? response.items : []);
        }
        else {
          draw([]);
          if (!panel.busy) status.textContent = response?.error || 'Video capture is disabled for this page.';
        }
        if (!panel.busy && !panel.loading && !panel.loadError && panel.items.length === 0 && response?.ok) status.textContent = video.mediaKeys
          ? 'Protected video: downloading is unavailable.'
          : panel.generic ? 'Click “Find available qualities” to let Flux look for this video.'
          : panel.page ? 'Open the quality list to download this video with its audio.' : (video.currentSrc || video.src).startsWith('blob:')
            ? 'Streaming player detected. Keep it playing for a moment so Flux can find its HLS/DASH stream.'
            : 'Waiting for a downloadable video source. Start playback to detect it.';
      } catch { draw([]); if (!panel.busy) status.textContent = 'Extension connection interrupted. Refresh this page after updating Flux.'; }
      position(panel);
      if (panel.page && !panel.generic && !panel.catalog && !panel.loading && !panel.loadError && !document.hidden && activeVideo === video && panel.inViewport !== false) loadFormats(false,true);
    }
    async function download(items) {
      if (panel.busy) return;
      panel.busy = true;
      root.querySelectorAll('.source,.all:not(.load)').forEach(button => button.disabled = true);
      let sent = 0;
      try {
        for (const item of items) {
          status.textContent = `Opening in Flux… ${sent + 1}/${items.length}`;
          const response = await chrome.runtime.sendMessage(item.formatId ? {action:'video-download',formatId:item.formatId,pageUrl:location.href,streamUrl:panel.isStream ? panel.page : undefined,generic:panel.generic} : { action: 'media-download', url: item.url, sources: sources(video), resources: resourceSources(), pageUrl: location.href });
          if (!response?.ok) throw new Error(response?.error || 'Flux is unavailable. Check the browser bridge.');
          sent++;
        }
        status.textContent = `${sent === 1 ? 'Video' : `${sent} sources`} sent to Flux.`;
      } catch (error) { status.textContent = `${sent ? `${sent} sent. ` : ''}${error.message}`; }
      finally { panel.busy = false; root.querySelectorAll('.source,.all:not(.load)').forEach(button => button.disabled = false); }
    }
    async function loadFormats(force = false, prefetch = false) {
      if (panel.loading || panel.busy || !panel.page) return;
      panel.loading = true; panel.loadError = false; select('.load').disabled = true; status.textContent = 'Finding available qualities…';
      const requestedPage = panel.page;
      try {
        const response = await chrome.runtime.sendMessage({action:'video-formats',pageUrl:location.href,refresh:force,prefetch,streamUrl:panel.isStream ? requestedPage : undefined,generic:panel.generic});
        if (response?.deferred) return;
        if (!response?.ok) throw new Error(response?.error || 'Could not load video qualities.');
        if (panel.page !== requestedPage || !panel.isStream && !panel.generic && requestedPage !== ApexMedia.videoPage(location.href)) return;
        panel.catalog = response.video;
        draw(response.video.formats.map(item => ({...item,size:item.totalBytes})));
        status.textContent = 'Audio is included; Flux assembles separate tracks automatically.';
        select('.load').textContent = 'Reload available qualities';
      } catch (error) { panel.loadError = true; status.textContent = error.message; }
      finally { panel.loading = false; select('.load').disabled = false; position(panel); }
    }
    select('.load').addEventListener('click',()=>loadFormats(true));
    let drag = null, suppressClickUntil = 0;
    bar.addEventListener('pointerdown', event => {
      if (event.button !== 0 || event.target.closest('.close')) return;
      const rect = host.getBoundingClientRect();
      drag = { id:event.pointerId, x:event.clientX, y:event.clientY, left:rect.left, top:rect.top, moved:false };
      event.stopPropagation();
    });
    bar.addEventListener('pointermove', event => {
      if (!drag || drag.id !== event.pointerId) return;
      const dx = event.clientX-drag.x, dy = event.clientY-drag.y;
      if (!drag.moved && Math.hypot(dx,dy) < 4) return;
      if (!drag.moved) bar.setPointerCapture(event.pointerId);
      drag.moved = true; bar.classList.add('dragging');
      panel.manualPosition = {x:drag.left+dx,y:drag.top+dy};
      position(panel); event.preventDefault(); event.stopPropagation();
    });
    const stopDrag = event => {
      if (!drag || drag.id !== event.pointerId) return;
      if (drag.moved) suppressClickUntil = performance.now()+400;
      drag = null; bar.classList.remove('dragging');
      if (bar.hasPointerCapture(event.pointerId)) bar.releasePointerCapture(event.pointerId);
    };
    bar.addEventListener('pointerup',stopDrag); bar.addEventListener('pointercancel',stopDrag); bar.addEventListener('lostpointercapture',stopDrag);
    move.addEventListener('keydown', event => {
      const deltas = {ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]};
      if (event.key === 'Home') { panel.manualPosition=null; position(panel); event.preventDefault(); }
      if (!deltas[event.key]) return;
      const rect=host.getBoundingClientRect(), step=event.shiftKey?24:8, [dx,dy]=deltas[event.key];
      panel.manualPosition={x:rect.left+dx*step,y:rect.top+dy*step}; position(panel); event.preventDefault();
    });
    trigger.addEventListener('click', async event => { event.stopPropagation(); if (performance.now()<suppressClickUntil) return; menu.hidden = !menu.hidden; trigger.setAttribute('aria-expanded', String(!menu.hidden)); position(panel); if (!menu.hidden) { await refresh(); if (!panel.catalog && !panel.generic) loadFormats(); } });
    select('.all').addEventListener('click', () => download([...panel.items]));
    select('.close').addEventListener('click', () => { panel.dismissed = true; host.style.setProperty('display', 'none', 'important'); });
    root.addEventListener('keydown', event => { if (event.key === 'Escape') { menu.hidden = true; trigger.setAttribute('aria-expanded', 'false'); trigger.focus(); } });
    root.addEventListener('click', event => event.stopPropagation());
    panel.refresh = refresh;
    (document.fullscreenElement && document.fullscreenElement.tagName !== 'VIDEO' ? document.fullscreenElement : document.documentElement).append(host);
    return panel;
  }
  function position(panel) {
    const rect = panel.video.getBoundingClientRect();
    const visible = allowed() && !document.hidden && activeVideo === panel.video && !panel.dismissed && (panel.items.length > 0 || !!panel.page) && ApexVideoPanelPolicy.playing(panel.video,rect,innerWidth,innerHeight,started(panel.video));
    panel.host.style.setProperty('display', visible ? 'block' : 'none', 'important');
    if (!visible) return;
    const point=panel.manualPosition || {x:rect.left+8,y:rect.top+8};
    const size=panel.host.getBoundingClientRect();
    const clamped=ApexVideoPanelPolicy.clamp(point,size,{width:innerWidth,height:innerHeight});
    panel.host.style.setProperty('left', `${clamped.x}px`, 'important');
    panel.host.style.setProperty('top', `${clamped.y}px`, 'important');
    if (panel.manualPosition) panel.manualPosition=clamped;
  }
  async function scan() {
    if (updating || !document.documentElement) return;
    updating = true;
    try {
      for (const [video, panel] of panels) if (!video.isConnected || !allowed()) { panel.host.remove(); intersection?.unobserve(video); panels.delete(video); }
      if (!allowed()) return;
      if (document.hidden) { panels.forEach(position); return; }
      if (discoveryDirty || Date.now()-discoveryAt > 10000 || discoveredVideos.length === 0 && Date.now()-startedAt < 10000) { discoveredVideos = videosIn().slice(0,16); discoveryDirty = false; discoveryAt = Date.now(); }
      activeVideo = ApexVideoPanelPolicy.choose(discoveredVideos.flatMap(video => {
        const rect=video.getBoundingClientRect(), style=getComputedStyle(video);
        if (!ApexVideoPanelPolicy.playing(video,rect,innerWidth,innerHeight,started(video)) || style.visibility === 'hidden' || style.display === 'none') return [];
        // YouTube ad players and auxiliary preview videos are not the selected title.
        if (ApexMedia.isYouTube(location.href) && ApexMedia.videoPage(location.href)) {
          const player=document.getElementById('movie_player');
          if (player && (!player.contains(video) || player.classList.contains('ad-showing'))) return [];
        }
        return [{video,audible:!video.muted && video.volume>0,started:playbackStarted.get(video)||0,area:Math.max(0,Math.min(rect.bottom,innerHeight)-Math.max(rect.top,0))*Math.max(0,Math.min(rect.right,innerWidth)-Math.max(rect.left,0))}];
      }));
      panels.forEach(position);
      for (const video of discoveredVideos) {
        if (video !== activeVideo) continue;
        let panel = panels.get(video);
        if (!panel) { panel = makePanel(video); panels.set(video, panel); intersection.observe(video); }
        const source = video.currentSrc || video.src;
        const pageKey=ApexMedia.videoPage(location.href);
        if (source !== panel.source || pageKey !== panel.pageKey) { panel.source = source; panel.pageKey=pageKey; panel.dismissed = false; panel.catalog=null; panel.items=[]; panel.signature=''; panel.loadError=false; panel.root.querySelector('.menu').hidden=true; panel.root.querySelector('.trigger').setAttribute('aria-expanded','false'); }
        position(panel);
        if (panel.inViewport !== false) await panel.refresh();
      }
    } finally { updating = false; }
  }
  addEventListener('scroll', () => {panels.forEach(position);requestScan();}, { passive: true, capture: true, signal: events.signal });
  addEventListener('resize', () => {panels.forEach(position);requestScan();}, { passive: true, signal: events.signal });
  document.addEventListener('click', event => panels.forEach(panel => {
    if (event.composedPath().includes(panel.host)) return;
    panel.root.querySelector('.menu').hidden = true;
    panel.root.querySelector('.trigger').setAttribute('aria-expanded', 'false');
  }),{signal:events.signal});
  document.addEventListener('fullscreenchange', () => { panels.forEach(panel => { const parent = document.fullscreenElement; (parent && parent.tagName !== 'VIDEO' ? parent : document.documentElement).append(panel.host); position(panel); }); },{signal:events.signal});
  const intersection = new IntersectionObserver(entries => {
    let visible = false;
    for (const entry of entries) { const panel = panels.get(entry.target); if (panel) { panel.inViewport = entry.isIntersecting; visible ||= entry.isIntersecting; } }
    if (visible) requestScan();
  });
  const watchedRoots = new WeakSet();
  const observer = new MutationObserver(records => {
    if (records.some(record => [...record.addedNodes,...record.removedNodes].some(node => node.nodeType === 1 && !node.hasAttribute('data-apex-video-panel') && (node.matches('video,source') || node.querySelector('video,source') || node.shadowRoot)))) {
      discoveryDirty = true; requestScan();
    }
  });
  observer.observe(document, {childList:true,subtree:true});
  for (const type of mediaEvents) document.addEventListener(type,playbackChanged,{capture:true,signal:events.signal});
  document.addEventListener('visibilitychange',()=>{panels.forEach(position);if(!document.hidden)requestScan();},{signal:events.signal});
  const fallback = setInterval(()=>{
    if(!document.hidden && (Date.now()-startedAt<10000 || Date.now()-polledAt>=10000)) {polledAt=Date.now();scan();}
  },2000);
  globalThis.apexVideoPanelDispose = () => {
    clearInterval(fallback); clearTimeout(refreshTimer); observer.disconnect(); intersection.disconnect(); events.abort();
    chrome.storage.onChanged.removeListener(storageChanged); panels.forEach(panel=>panel.host.remove()); panels.clear();
  };
  read();
})();
