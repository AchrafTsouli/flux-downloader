// Shared by the isolated content script and the package-free tests.
globalThis.SwiftFetchLinks = {
  isDownload(url, downloadAttribute = false) {
    try {
      const value = new URL(url);
      if (!["http:", "https:"].includes(value.protocol) || value.username || value.password) return false;
      return downloadAttribute || /\.(exe|msi|msix|msixbundle|zip|7z|rar|iso|dmg|pkg|deb|rpm|apk|tar|gz|bz2|xz|zst|torrent)$/i.test(value.pathname) || (value.hostname === "github.com" && /\/releases\/download\//.test(value.pathname));
    } catch { return false; }
  }
};
