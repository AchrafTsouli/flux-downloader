import { sendNative, statusStorage } from "./transfer.js";
const capture = document.querySelector("#capture");
const status = document.querySelector("#status");
document.querySelector('#version').textContent = 'Extension ' + chrome.runtime.getManifest().version;
const stored = await chrome.storage.local.get(["captureEnabled", "videoPanelEnabled", "capturePolicy"]);
const { lastMessage } = await statusStorage(chrome).get("lastMessage");
capture.checked = stored.captureEnabled !== false;
status.textContent = lastMessage || "Click Test connection to check the Windows bridge.";
capture.addEventListener("change", () => chrome.storage.local.set({ captureEnabled: capture.checked }));
async function perform(action, button) {
  button.disabled = true;
  status.textContent = "Connecting…";
  try {
    await sendNative(chrome, { action });
    status.textContent = action === "ping" ? "Browser bridge installed and ready." : "SwiftFetch is open.";
    await chrome.action.setBadgeText({ text: "" });
  } catch (error) { status.textContent = error.message; }
  finally { button.disabled = false; }
}
document.querySelector("#open").addEventListener("click", event => perform("show", event.target));
document.querySelector("#test").addEventListener("click", event => perform("ping", event.target));

const videos = document.querySelector("#videos");
videos.checked = stored.videoPanelEnabled ?? true;
videos.addEventListener("change", () => chrome.storage.local.set({ videoPanelEnabled: videos.checked }));
document.querySelector('#refresh-videos').addEventListener('click', async event => {
  event.target.disabled = true;
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id || !/^https?:/.test(tab.url || '')) throw new Error('Open a normal video webpage first.');
    await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ['media-policy.js', 'video-panel.js'] });
    status.textContent = 'Video panel detection attached. Capture and video panels must be enabled above.';
  } catch (error) { status.textContent = error.message; }
  finally { event.target.disabled = false; }
});
