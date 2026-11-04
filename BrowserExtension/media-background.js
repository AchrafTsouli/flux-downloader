import { VideoCatalogCache } from './video-cache.js';
import { readPlayerMetadata, playerCatalog } from './player-catalog.js';
import './media-policy.js';
import { shouldCapture } from './capture-policy.js';
import { downloadSession } from './session.js';
import { sendNative } from './transfer.js';

const entries = new Map();
const pending = new Map();
const keyFor = (tab, frame) => `media:${tab}:${frame}`;
const streamKeyFor = (tab, frame) => `streams:${tab}:${frame}`;
const recent = items => (items || []).filter(item => Date.now() - item.at < 30 * 60 * 1000);

export function installMedia(api) {
  const catalogCache = new VideoCatalogCache(api,load=>load());
  api.webRequest.onResponseStarted.addListener(details => {
    if (details.tabId < 0 || details.statusCode < 200 || details.statusCode >= 300) return;
    // Players that fetch in a worker report no frame; attribute those to the top frame.
    if (details.frameId < 0) details = { ...details, frameId: 0, documentId: undefined };
    const headers = details.responseHeaders || [];
    const type = headers.find(h => h.name.toLowerCase() === 'content-type')?.value || '';
    const manifest = ApexMedia.stream(details.url, type);
    if (manifest) {
      // The first manifest a document loads is normally the master playlist with every quality.
      const key = streamKeyFor(details.tabId, details.frameId);
      const work = (pending.get(key) || Promise.resolve()).catch(() => {}).then(async () => {
        const saved = recent(entries.get(key) || (await api.storage.session.get(key))[key]);
        if (saved.some(item => item.url === manifest.url)) return;
        const items = [...saved, { ...manifest, at: Date.now(), documentId: details.documentId, pageUrl: details.documentUrl || details.initiator }].slice(-8);
        entries.set(key, items);
        await api.storage.session.set({ [key]: items });
      });
      pending.set(key, work);
      work.catch(console.error).finally(() => { if (pending.get(key) === work) pending.delete(key); });
      return;
    }
    const item = ApexMedia.candidate(details.url, type, headers);
    if (!item) return;
    const key = keyFor(details.tabId, details.frameId);
    // Serialize writes per frame so parallel media requests cannot overwrite one another.
    const work = (pending.get(key) || Promise.resolve()).catch(() => {}).then(async () => {
      const saved = entries.get(key) || (await api.storage.session.get(key))[key];
      const items = ApexMedia.merge([...recent(saved), { ...item, at: Date.now(), type, documentId: details.documentId, pageUrl: details.documentUrl || details.initiator }]);
      entries.set(key, items);
      await api.storage.session.set({ [key]: items });
    });
    pending.set(key, work);
    work.catch(console.error).finally(() => { if (pending.get(key) === work) pending.delete(key); });
  }, { urls: ['http://*/*', 'https://*/*'], types: ['media', 'xmlhttprequest', 'other'] }, ['responseHeaders']);
  api.tabs.onRemoved.addListener(tab => {
    catalogCache.close(tab);
    api.storage.session.get(null).then(saved => api.storage.session.remove(Object.keys(saved).filter(key => key.startsWith(`media:${tab}:`) || key.startsWith(`streams:${tab}:`) || key.startsWith(`video-catalog:${tab}:`)))).catch(console.error);
    for (const key of entries.keys()) if (key.startsWith(`media:${tab}:`) || key.startsWith(`streams:${tab}:`)) entries.delete(key);
  });
  api.runtime.onMessage.addListener((message, sender, reply) => {
    if (sender.id !== api.runtime.id || !sender.tab || !['media-list', 'media-download', 'video-formats', 'video-download'].includes(message.action)) return;
    (async () => {
      const { captureEnabled = true, capturePolicy = {}, videoPanelEnabled } = await api.storage.local.get(['captureEnabled', 'capturePolicy', 'videoPanelEnabled']);
      const pageUrl = /^https?:/.test(sender.url || '') ? sender.url : sender.origin;
      if (!captureEnabled || capturePolicy.enabled === false || !(videoPanelEnabled ?? true) || !shouldCapture(pageUrl, '', capturePolicy, true)) return { ok: false, items: [] };
      let videoPage = ApexMedia.videoPage(pageUrl);
      if (message.pageUrl && /^https?:/.test(sender.url || '') && new URL(message.pageUrl).origin === new URL(sender.url).origin) videoPage = ApexMedia.videoPage(message.pageUrl);
      // Streams this frame's player loaded; a page can only ask for a manifest it actually fetched.
      const streamKey = streamKeyFor(sender.tab.id, sender.frameId || 0);
      await pending.get(streamKey);
      const streams = recent(entries.get(streamKey) || (await api.storage.session.get(streamKey))[streamKey])
        .filter(item => sender.documentId && item.documentId ? item.documentId === sender.documentId : !item.documentId || item.pageUrl === sender.url);
      // Any other page: yt-dlp's site extractors and generic player detection, only on the user's request.
      const genericPage = /^https?:/.test(sender.url || '') ? sender.url.split('#')[0] : null;
      const stream = videoPage ? null : message.streamUrl ? streams.find(item => item.url === message.streamUrl) : streams[0];
      if (['video-formats','video-download'].includes(message.action)) {
        if (message.prefetch && sender.tab.active === false) return {ok:false,deferred:true};
        if (!videoPage && stream) videoPage = stream.url;
        if (!videoPage && message.generic === true && genericPage) {
          if (message.prefetch) return {ok:false,deferred:true};
          videoPage = genericPage;
        }
        if (!videoPage || !shouldCapture(videoPage, '', capturePolicy, true)) return {ok:false,error:'Open a supported video page, or start playback so the stream can be detected.'};
        const available = await catalogCache.get(sender.tab.id,sender.frameId || 0,videoPage,async()=>{
          if (catalogCache.closed.has(sender.tab.id)) throw new Error('The video tab was closed.');
          const session = await downloadSession(api,videoPage,sender.tab.id,sender.url,sender.frameId || 0);
          const response = await sendNative(api,{action:'video-formats',url:videoPage,session},120000);
          if (!response.video?.formats?.length) throw new Error('No downloadable qualities were returned.');
          return response.video;
        },message.action === 'video-formats' && message.refresh === true,async()=>{
          if (ApexMedia.isYouTube(videoPage) && api.scripting?.executeScript) {
            try {
              const id = new URL(videoPage).searchParams.get('v');
              const values = await api.scripting.executeScript({target:{tabId:sender.tab.id,frameIds:[sender.frameId || 0]},world:'MAIN',func:readPlayerMetadata,args:[id]});
              const ready = playerCatalog(values[0]?.result,id);
              if (ready) return ready;
            } catch { /* A player without format metadata uses the bounded native fallback. */ }
          }
          return null;
        });
        if (message.action === 'video-formats') return {ok:true,video:available,page:videoPage};
        const selected = available.formats.find(format => format.formatId === message.formatId);
        if (!selected) return {ok:false,error:'This quality is no longer available. Reload the quality list.'};
        const session = await downloadSession(api,videoPage,sender.tab.id,sender.url,sender.frameId || 0);
        const quality = selected.audioOnly ? 'audio' : selected.label.match(/\d+p/)?.[0] || selected.formatId;
        // Manifests rarely carry a useful title; the tab title names the video instead.
        const title = stream && sender.tab.title ? sender.tab.title.slice(0, 200) : available.title;
        await sendNative(api,{action:'download',url:videoPage,fileName:`${title} (${quality}).${selected.extension}`,requestId:crypto.randomUUID(),session,video:selected});
        return {ok:true};
      }
      const key = keyFor(sender.tab.id, sender.frameId || 0);
      await pending.get(key);
      const saved = recent(entries.get(key) || (await api.storage.session.get(key))[key]);
      const observed = saved.filter(item => sender.documentId && item.documentId ? item.documentId === sender.documentId : item.pageUrl === sender.url);
      const dom = Array.isArray(message.sources) ? message.sources.slice(0, 32).map(item => ApexMedia.candidate(item.url, item.type)).filter(Boolean) : [];
      const resources = Array.isArray(message.resources) ? message.resources.slice(-32).map(item => ApexMedia.candidate(item.url)).filter(Boolean) : [];
      const items = ApexMedia.merge([...resources, ...dom, ...observed]).filter(item => shouldCapture(item.url, '', capturePolicy, true));
      if (message.action === 'media-list') return { ok: true, items, videoPage, stream: stream?.url || null, streamKind: stream?.kind || null, genericPage };
      const item = items.find(item => item.url === message.url);
      if (!item) return { ok: false, error: 'This video source is no longer available. Refresh the player.' };
      const session = await downloadSession(api, item.url, sender.tab.id, pageUrl, sender.frameId || 0);
      // Keep server-provided filenames authoritative; the app resolves Content-Disposition.
      await sendNative(api, { action: 'download', url: item.url, requestId: crypto.randomUUID(), session });
      return { ok: true };
    })().then(reply).catch(error => reply({ ok: false, error: error.message }));
    return true;
  });
}
