import { installMedia } from "./media-background.js";
installMedia(chrome);
import { shouldCapture } from "./capture-policy.js";
import { captureDownload, directUrl, recoverHandoffs, sendNative, statusStorage } from "./transfer.js";
import { downloadSession } from "./session.js";

const recovery = recoverHandoffs(chrome).catch(console.error);
chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
  // Unlike onCreated, this event includes the server/browser's suggested filename.
  recovery.then(async () => { await refreshPolicy(); const { capturePolicy = {} } = await chrome.storage.local.get("capturePolicy"); if (shouldCapture(item.url || item.finalUrl, item.filename, capturePolicy)) await captureDownload(chrome, item); }).catch(console.error).finally(() => suggest());
  return true;
});

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id || !sender.tab) return;
  if (message.action === "capture-policy") {
    refreshPolicy().then(async () => reply({ ok: true, policy: (await chrome.storage.local.get("capturePolicy")).capturePolicy || {} })).catch(() => reply({ ok: false }));
    return true;
  }
  if (message.action !== "direct-click" || !directUrl(message.url)) return;
  (async () => {
    const { captureEnabled = true } = await chrome.storage.local.get("captureEnabled");
    await refreshPolicy(); const { capturePolicy = {} } = await chrome.storage.local.get("capturePolicy"); if (!captureEnabled || !shouldCapture(message.url, message.fileName, capturePolicy, message.force)) { reply({ ok: false }); return; }
    try {
      const session = await downloadSession(chrome, message.url, sender.tab.id, sender.tab.url);
      await sendNative(chrome, { action: "download", url: message.url, fileName: message.fileName || "", requestId: crypto.randomUUID(), session });
      await statusStorage(chrome).set({ lastMessage: "Download opened directly in SwiftFetch." }).catch(() => {});
      reply({ ok: true });
    } catch (error) {
      const { browserFallbacks = [] } = await chrome.storage.session.get("browserFallbacks");
      await chrome.storage.session.set({ browserFallbacks: [...browserFallbacks.filter(value => value.expires > Date.now()), { url: message.url, expires: Date.now() + 60000 }] });
      await statusStorage(chrome).set({ lastMessage: `Kept in browser: ${error.message}` });
      reply({ ok: false });
    }
  })().catch(error => { console.error(error); reply({ ok: false }); });
  return true;
});

chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.get("videoPanelEnabled").then(value => { if (value.videoPanelEnabled === undefined) return chrome.storage.local.set({ videoPanelEnabled: true }); });
  // Static content scripts only run on navigation; attach panels to tabs already open during an update.
  chrome.tabs.query({}).then(tabs => Promise.allSettled(tabs.filter(tab => /^https?:/.test(tab.url || '')).map(tab =>
    chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ['media-policy.js', 'video-panel-policy.js', 'video-panel.js'] })
  ))).catch(console.error);
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: "swiftfetch-download", title: "Download with SwiftFetch", contexts: ["link"], targetUrlPatterns: ["http://*/*", "https://*/*"] });
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== "swiftfetch-download" || !directUrl(info.linkUrl)) return;
  downloadSession(chrome, info.linkUrl, tab?.id, info.pageUrl).then(session => sendNative(chrome, { action: "download", url: info.linkUrl, requestId: crypto.randomUUID(), session }))
    .then(() => statusStorage(chrome).set({ lastMessage: "Link sent to SwiftFetch." }))
    .catch(async error => {
      await statusStorage(chrome).set({ lastMessage: error.message });
      await chrome.action.setBadgeText({ text: "!" });
    });
});

let policyAt = 0;
let refreshing;
async function refreshPolicy() {
  if (Date.now() - policyAt < 15000) return;
  if (refreshing) return refreshing;
  refreshing = (async () => {
    try {
      const response = await sendNative(chrome, { action: "capture-policy" });
      if (response.policy) {
        await chrome.storage.local.set({ capturePolicy: response.policy });
        await chrome.contextMenus.update("swiftfetch-download", { visible: response.policy.contextMenu !== false }).catch(() => {});
      }
    } catch { /* Keep last saved capture policy if the native host is unavailable. */ }
    finally { policyAt = Date.now(); refreshing = null; }
  })();
  return refreshing;
}
refreshPolicy();

