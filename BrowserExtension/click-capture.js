(() => {
  let enabled = false;
  let policy = {};
  chrome.storage.local.get(["captureEnabled", "capturePolicy"]).then(value => { enabled = value.captureEnabled !== false; policy = value.capturePolicy || {}; });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.captureEnabled) enabled = changes.captureEnabled.newValue !== false;
    if (changes.capturePolicy) policy = changes.capturePolicy.newValue || {};
  });
  const refresh = async () => { try { const response = await chrome.runtime.sendMessage({ action: "capture-policy" }); if (response?.ok && response.policy) policy = response.policy; } catch { } };
  setInterval(refresh, 30000);
  const key = (event, name) => ({ Alt: event.altKey, Control: event.ctrlKey, Shift: event.shiftKey })[name] || false;
  document.addEventListener("click", async event => {
    if (!enabled || policy.enabled === false || !event.isTrusted || event.defaultPrevented || event.button !== 0 || event.metaKey || key(event, policy.bypassKey || "Alt")) return;
    const force = key(event, policy.forceKey || "Shift");
    if (!force && (event.ctrlKey || event.shiftKey || event.altKey)) return;
    const link = event.composedPath().find(element => element?.tagName === "A");
    if (!link || typeof link.href !== "string") return;
    try {
      const url = new URL(link.href);
      if (!["http:","https:"].includes(url.protocol) || url.username || url.password) return;
      if ((policy.excludedSites || "").toLowerCase().split(/[\s,;]+/).filter(Boolean).some(site => site.startsWith("*.") ? url.hostname === site.slice(2) || url.hostname.endsWith(site.slice(1)) : url.hostname === site)) return;
      if ((policy.excludedAddresses || "").split(/\r?\n/).filter(Boolean).some(prefix => url.href.startsWith(prefix.trim()))) return;
      const ext = url.pathname.split(".").pop().toLowerCase();
      const recognized = policy.extensions ? policy.extensions.toLowerCase().split(/[\s,;]+/).map(value => value.replace(/^\./,"" )).includes(ext) : SwiftFetchLinks.isDownload(link.href, link.hasAttribute("download"));
      if (!force && !recognized && !link.hasAttribute("download")) return;
    } catch { return; }
    event.preventDefault(); event.stopImmediatePropagation();
    const url = link.href;
    try { const response = await chrome.runtime.sendMessage({ action: "direct-click", url, fileName: link.getAttribute("download") || "", force: force || link.hasAttribute("download") }); if (response?.ok) return; } catch { }
    if (link.isConnected) link.click(); else location.assign(url);
  }, true);
})();


