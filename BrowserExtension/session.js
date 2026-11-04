export async function downloadSession(api, url, tabId, pageUrl, frameId = 0) {
  try { return await collectDownloadSession(api, url, tabId, pageUrl, frameId); }
  catch { throw new Error("Browser session access is unavailable. Reload SwiftFetch and allow cookies for this site."); }
}

async function collectDownloadSession(api, url, tabId, pageUrl, frameId) {
  const origin = new URL(url).origin;
  const session = { origin, userAgent: globalThis.navigator?.userAgent || "", cookies: [] };
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
