// Headers the browser sent for a request a moment ago. Some sites accept only exactly that request
// (client hints, Sec-Fetch-*, language); Flux replays them. Kept in memory for two minutes.
const sentHeaders = new Map();
const keptHeaders = /^(accept|accept-language|sec-fetch-(site|mode|dest|user)|sec-ch-ua(-mobile|-platform)?|upgrade-insecure-requests|x-requested-with)$/i;
export function rememberRequest(details) {
  if (!/^https?:/i.test(details.url) || !Array.isArray(details.requestHeaders)) return;
  const headers = details.requestHeaders.filter(h => keptHeaders.test(h.name) && typeof h.value === 'string' && h.value.length <= 2048 && !/[\x00-\x1f]/.test(h.value)).slice(0, 16).map(h => ({ name: h.name, value: h.value }));
  if (!headers.length) return;
  sentHeaders.set(details.url, { at: Date.now(), headers });
  if (sentHeaders.size > 300) for (const [key, value] of sentHeaders) if (Date.now() - value.at > 120000 || sentHeaders.size > 300) sentHeaders.delete(key);
}
export function installHeaderCapture(api) {
  try {
    const extra = api.webRequest.OnSendHeadersOptions?.EXTRA_HEADERS;
    api.webRequest.onSendHeaders.addListener(rememberRequest, { urls: ['http://*/*', 'https://*/*'], types: ['main_frame', 'sub_frame', 'media', 'object', 'other', 'xmlhttprequest'] }, extra ? ['requestHeaders', extra] : ['requestHeaders']);
  } catch { /* Without header access the cookies and referring page still go along. */ }
}
export function browserHeaders(url) {
  const entry = sentHeaders.get(url);
  return entry && Date.now() - entry.at < 120000 ? entry.headers : [];
}

export async function downloadSession(api, url, tabId, pageUrl, frameId = 0) {
  try { return await collectDownloadSession(api, url, tabId, pageUrl, frameId); }
  catch { throw new Error("Browser session access is unavailable. Reload SwiftFetch and allow cookies for this site."); }
}

async function collectDownloadSession(api, url, tabId, pageUrl, frameId) {
  const origin = new URL(url).origin;
  const session = { origin, userAgent: globalThis.navigator?.userAgent || "", cookies: [], headers: browserHeaders(url) };
  if (pageUrl && /^https?:/.test(pageUrl)) session.referer = pageUrl;
  if (!api.cookies) return session;
  const stores = await api.cookies.getAllCookieStores();
  const store = stores.find(value => value.tabIds.includes(tabId));
  const query = { url };
  // In split mode the default store belongs to this worker's normal/private
  // context, including download events which provide no tab ID.
  if (store) query.storeId = store.id;
  const ordinary = await api.cookies.getAll(query);
  let partitioned = [];
  if (Number.isInteger(tabId) && api.cookies.getPartitionKey) {
    try {
      const key = await api.cookies.getPartitionKey({ tabId, frameId });
      if (key.partitionKey) partitioned = await api.cookies.getAll({ ...query, partitionKey: key.partitionKey });
    } catch { /* This browser does not expose the partition key for this tab. */ }
  }
  const host = new URL(url).hostname;
  const seen = new Set();
  for (const cookie of [...partitioned, ...ordinary]) {
    const domain = cookie.domain.replace(/^\./, "").toLowerCase();
    if (host !== domain && (cookie.hostOnly || !host.endsWith("." + domain))) continue;
    const target = new URL(url);
    if (cookie.secure && target.protocol !== "https:" || cookie.expirationDate != null && cookie.expirationDate <= Date.now() / 1000) continue;
    if (!(target.pathname === cookie.path || target.pathname.startsWith(cookie.path) && (cookie.path.endsWith("/") || target.pathname[cookie.path.length] === "/"))) continue;
    const key = cookie.name + "|" + cookie.domain + "|" + cookie.path;
    if (seen.has(key)) continue;
    seen.add(key);
    session.cookies.push({ name: cookie.name, value: cookie.value, domain: cookie.domain, path: cookie.path, hostOnly: cookie.hostOnly, secure: cookie.secure, expiresUnixSeconds: cookie.expirationDate });
  }
  return session;
}
