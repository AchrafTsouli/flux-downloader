// Shared by the service worker, isolated content scripts and Node tests.
(() => {
  const formats = { 'video/mp4': 'MP4', 'video/webm': 'WEBM', 'video/ogg': 'OGV', 'video/x-m4v': 'M4V', 'video/quicktime': 'MOV' };
  function candidate(url, type = '', headers = []) {
    try {
      const target = new URL(url);
      if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password) return null;
      const mime = type.split(';')[0].trim().toLowerCase();
      if (mime && !formats[mime] && !['application/octet-stream', 'binary/octet-stream'].includes(mime)) return null;
      const ext = target.pathname.match(/\.(mp4|webm|ogv|mov|m4v)$/i)?.[1].toUpperCase();
      if (/\.(m3u8|mpd|m4s|ts|cmfv|cmfa)(?:$|\/)/i.test(target.pathname) || /mpegurl|dash\+xml|mp2t|iso.segment/i.test(mime)) return null;
      // Byte/chunk URLs and separate streaming tracks are not complete media files.
      if (['range', 'bytestart', 'byteend', 'sq', 'segment'].some(key => target.searchParams.has(key)) || target.searchParams.has('clen') && target.searchParams.has('itag')) return null;
      const format = formats[mime] || ext;
      if (!format) return null;
      const get = name => headers.find(h => h.name.toLowerCase() === name)?.value || '';
      const range = get('content-range').match(/^bytes\s+\d+-\d+\/(\d+)$/i);
      const length = range?.[1] || (!get('content-range') ? get('content-length') : '');
      const size = /^\d+$/.test(length) && Number.isSafeInteger(Number(length)) && Number(length) > 0 ? Number(length) : null;
      target.hash = '';
      return { url: target.href, format, size, type: mime };
    } catch { return null; }
  }
  function merge(items) {
    const values = new Map();
    for (const item of items) {
      const clean = candidate(item.url, item.type);
      if (!clean) continue;
      const previous = values.get(clean.url);
      values.set(clean.url, { ...previous, ...item, ...clean, size: item.size || previous?.size || null, height: item.height || previous?.height || null });
    }
    return [...values.values()].slice(-32);
  }
  const youtubeHosts = ['youtube.com','www.youtube.com','m.youtube.com','music.youtube.com','www.youtube-nocookie.com','youtube-nocookie.com','youtu.be'];
  // Keep in step with VideoPages.KnownPages in SwiftFetch.Core/VideoFormats.cs.
  const knownPages = [
    ['vimeo.com', /^\/(?:channels\/[\w-]+\/|groups\/[\w-]+\/videos\/)?\d+(?:\/\w+)?\/?$/],
    ['player.vimeo.com', /^\/video\/\d+\/?$/],
    ['dailymotion.com', /^\/video\/[A-Za-z0-9]+\/?$/],
    ['dai.ly', /^\/[A-Za-z0-9]+\/?$/],
    ['x.com', /^\/\w+\/status\/\d+(?:\/video\/\d+)?\/?$/],
    ['twitter.com', /^\/\w+\/status\/\d+(?:\/video\/\d+)?\/?$/],
    ['facebook.com', /^\/(?:watch\/?|reel\/\d+\/?|[\w.-]+\/videos\/(?:[\w.-]+\/)?\d+\/?|share\/[rv]\/[\w-]+\/?)$/],
    ['fb.watch', /^\/[\w-]+\/?$/],
    ['instagram.com', /^\/(?:[\w.]+\/)?(?:p|reel|reels|tv)\/[\w-]+\/?$/],
    ['tiktok.com', /^\/@[\w.-]+\/video\/\d+\/?$/],
    ['twitch.tv', /^\/(?:videos\/\d+|\w+\/clip\/[\w-]+)\/?$/],
    ['clips.twitch.tv', /^\/[\w-]+\/?$/],
    ['reddit.com', /^\/r\/\w+\/comments\/\w+(?:\/[^/]*)?\/?$/],
    ['streamable.com', /^\/\w+\/?$/],
    ['bilibili.com', /^\/video\/(?:BV\w+|av\d+)\/?$/],
    ['rumble.com', /^\/v[\w-]+\.html$/],
    ['odysee.com', /^\/@[^/]+\/[^/]+$/],
    ['ted.com', /^\/talks\/\w+\/?$/],
  ];
  const web = value => { try { const url = new URL(value); return ['http:','https:'].includes(url.protocol) && !url.username && !url.password ? url : null; } catch { return null; } };
  function youtube(page) {
    if (page.protocol !== 'https:' || page.port) return null;
    let id;
    if (page.hostname === 'youtu.be') id = page.pathname.slice(1);
    else id = page.pathname === '/watch' ? page.searchParams.get('v') : page.pathname.match(/^\/(?:shorts|embed|live)\/([\w-]{11})\/?$/)?.[1];
    return /^[\w-]{11}$/.test(id || '') ? `https://www.youtube.com/watch?v=${id}` : null;
  }
  const isYouTube = value => { const page = web(value); return !!page && youtubeHosts.includes(page.hostname.toLowerCase()); };
  // A single-video page on a site the bundled video tools understand, normalized; otherwise null.
  function videoPage(value) {
    const page = web(value);
    if (!page) return null;
    const host = page.hostname.toLowerCase();
    if (youtubeHosts.includes(host)) return youtube(page);
    const match = knownPages.some(([domain, path]) => (host === domain || host.endsWith('.' + domain)) && path.test(page.pathname));
    if (!match || host.endsWith('facebook.com') && /^\/watch\/?$/.test(page.pathname) && !page.searchParams.get('v')) return null;
    page.hash = '';
    return page.href;
  }
  // HLS/DASH manifests that players fetch for blob: video elements.
  function stream(url, type = '') {
    const target = web(url);
    if (!target) return null;
    const mime = type.split(';')[0].trim().toLowerCase();
    const path = target.pathname + target.search;
    const kind = /\.m3u8(?:$|[/?&])/i.test(path) || /mpegurl/.test(mime) ? 'HLS' : /\.mpd(?:$|[/?&])/i.test(path) || mime === 'application/dash+xml' ? 'DASH' : null;
    if (!kind) return null;
    target.hash = '';
    return { url: target.href, kind };
  }
  globalThis.ApexMedia = { candidate, merge, videoPage, isYouTube, stream };
})();
