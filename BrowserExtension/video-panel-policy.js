// Shared pure rules keep preview/failed-player detection and dragging testable.
(() => {
  // A video qualifies while playing, and stays eligible after the user pauses or finishes it
  // (started = it played at least once). Never-played previews and protected players do not.
  function playing(video, rect, width, height, started = false) {
    return (!video.paused && !video.ended || started) && !video.error && !video.mediaKeys &&
      video.readyState >= 2 && video.videoWidth > 0 && video.videoHeight > 0 &&
      rect.width >= 120 && rect.height >= 65 && rect.bottom > 30 &&
      rect.top < height && rect.right > 0 && rect.left < width;
  }
  function choose(candidates) {
    // One player per frame: prefer audible playback, then the most recently
    // started player and visible area. Muted autoplay previews cannot swamp it.
    return candidates.slice().sort((a, b) =>
      Number(b.audible) - Number(a.audible) || b.started - a.started || b.area - a.area
    )[0]?.video || null;
  }
  function clamp(point, size, viewport) {
    return {
      x: Math.max(4, Math.min(point.x, Math.max(4, viewport.width - size.width - 4))),
      y: Math.max(4, Math.min(point.y, Math.max(4, viewport.height - size.height - 4)))
    };
  }
  globalThis.ApexVideoPanelPolicy = { playing, choose, clamp };
})();
