export const HOST = "com.swiftfetch.bridge";
import { downloadSession } from "./session.js";

// Local storage is shared with normal windows; keep private status in memory.
export function statusStorage(api) {
  return api.extension?.inIncognitoContext ? api.storage.session : api.storage.local;
}

export function directUrl(url) {
  try { return ["http:", "https:"].includes(new URL(url).protocol); }
  catch { return false; }
}

export function sendNative(api, request, timeoutMs = 22000) {
  return new Promise((resolve, reject) => {
    let port;
    let finished = false;
    const finish = (error, response) => {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      port?.disconnect();
      if (error) reject(error); else resolve(response);
    };
    const timer = setTimeout(() => finish(new Error("Flux did not respond. Check the browser bridge installation.")), timeoutMs);
    try {
      port = api.runtime.connectNative(HOST);
      port.onMessage.addListener(response => {
        if (response?.ok) finish(null, response);
        else finish(new Error(response?.error || "SwiftFetch rejected the download."));
      });
      port.onDisconnect.addListener(() => finish(new Error(api.runtime.lastError?.message || "SwiftFetch browser bridge is unavailable. Run Install-BrowserBridge.ps1.")));
      port.postMessage(request);
    } catch (error) { finish(error); }
  });
}

export async function captureDownload(api, item, native = request => sendNative(api, request)) {
  const url = item.url || item.finalUrl;
  if (item.state !== "in_progress" || item.paused || !directUrl(url) || (item.danger && item.danger !== "safe")) return;
  const { captureEnabled = true } = await api.storage.local.get("captureEnabled");
  if (!captureEnabled) return;
  const { browserFallbacks = [] } = await api.storage.session.get("browserFallbacks");
  const fallbackIndex = browserFallbacks.findIndex(value => value.expires > Date.now() && (value.url === item.url || value.url === url));
  if (fallbackIndex >= 0) {
    browserFallbacks.splice(fallbackIndex, 1);
    await api.storage.session.set({ browserFallbacks });
    return;
  }
  const key = `handoff-${item.id}`;
  let paused = false;
  let accepted = false;
  const request = { action: "download", url, fileName: item.filename?.split(/[\\/]/).pop() || "", requestId: crypto.randomUUID() };
  try {
    // Persist before pausing so a service-worker restart can recover an interrupted handoff.
    await api.storage.session.set({ [key]: { id: item.id, request, accepted: false } });
    await api.downloads.pause(item.id);
    paused = true;
    const [current] = await api.downloads.search({ id: item.id });
    if (!current || current.state !== "in_progress" || !current.paused) return;
    if (current.danger && current.danger !== "safe") throw new Error("This download needs browser approval.");
    request.fileName = current.filename?.split(/[\\/]/).pop() || request.fileName;
    let pageUrl = item.referrer;
    if (api.tabs && Number.isInteger(item.tabId)) {
      try { pageUrl = (await api.tabs.get(item.tabId)).url || pageUrl; } catch { }
    }
    request.session = await downloadSession(api, url, item.tabId, pageUrl);
    await api.storage.session.set({ [key]: { id: item.id, request, accepted: false } });
    await native(request);
    accepted = true;
    await api.storage.session.set({ [key]: { id: item.id, request, accepted: true } });
    await api.downloads.cancel(item.id);
    try { if (api.downloads.erase) await api.downloads.erase({ id: item.id }); } catch { /* Handoff succeeded; history cleanup is optional. */ }
    await statusStorage(api).set({ lastMessage: "Download sent to SwiftFetch." });
    await api.action.setBadgeText({ text: "" });
  } catch (error) {
    if (paused && !accepted) {
      try { await api.downloads.resume(item.id); } catch { /* Download ended or was cancelled by the user. */ }
    }
    await statusStorage(api).set({ lastMessage: accepted ? "SwiftFetch accepted the file, but the browser copy could not be cancelled." : `Kept in browser: ${error.message}` });
    await api.action.setBadgeText({ text: "!" });
    await api.action.setBadgeBackgroundColor({ color: "#C2410C" });
  } finally {
    if (!accepted) await api.storage.session.remove(key);
    else {
      const [current] = await api.downloads.search({ id: item.id });
      if (!current || current.state !== "in_progress") await api.storage.session.remove(key);
    }
  }
}

export async function recoverHandoffs(api, native = request => sendNative(api, request)) {
  const pending = await api.storage.session.get(null);
  for (const [key, value] of Object.entries(pending)) {
    if (!key.startsWith("handoff-")) continue;
    const [item] = await api.downloads.search({ id: value.id });
    if (!item || item.state !== "in_progress" || !item.paused) { await api.storage.session.remove(key); continue; }
    let accepted = value.accepted;
    try {
      // The app deduplicates request IDs, even if the acknowledgement was lost.
      if (!value.accepted) await native(value.request);
      accepted = true;
      await api.storage.session.set({ [key]: { ...value, accepted: true } });
      await api.downloads.cancel(value.id);
      try { if (api.downloads.erase) await api.downloads.erase({ id: value.id }); } catch { }
      await api.storage.session.remove(key);
    } catch {
      if (!accepted) {
        try { await api.downloads.resume(value.id); } catch { }
        await api.storage.session.remove(key);
      }
    }
  }
}
