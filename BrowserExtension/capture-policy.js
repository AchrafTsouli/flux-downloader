export function shouldCapture(url, filename = "", policy = {}, force = false) {
  try {
    const target = new URL(url);
    if (!["http:", "https:"].includes(target.protocol) || target.username || target.password || policy.enabled === false) return false;
    const sites = (policy.excludedSites || "").toLowerCase().split(/[\s,;]+/).filter(Boolean);
    if (sites.some(site => site.startsWith("*.") ? target.hostname === site.slice(2) || target.hostname.endsWith(site.slice(1)) : target.hostname.toLowerCase() === site.toLowerCase())) return false;
    if ((policy.excludedAddresses || "").split(/\r?\n/).filter(Boolean).some(prefix => target.href.startsWith(prefix.trim()))) return false;
    if (force || !policy.extensions) return true;
    const source = filename && /\.[a-z0-9]{1,12}$/i.test(filename) ? filename : target.pathname;
    const extension = source.split(".").pop().toLowerCase();
    return policy.extensions.toLowerCase().split(/[\s,;]+/).map(value => value.replace(/^\./, "")).includes(extension);
  } catch { return false; }
}
