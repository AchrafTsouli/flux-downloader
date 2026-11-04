import test from "node:test";
import assert from "node:assert/strict";
import { captureDownload, directUrl, recoverHandoffs, sendNative } from "./transfer.js";
import "./link-policy.js";
import vm from "node:vm";
import fs from "node:fs";
import { downloadSession } from "./session.js";

test("session collection is scoped to the URL, profile, path and partition", async () => {
  const calls = [];
  const base = { name: "sid", value: "ordinary-test", domain: "example.test", path: "/files", hostOnly: true, secure: true };
  const api = { cookies: {
    async getAllCookieStores() { return [{ id: "profile-one", tabIds: [42] }]; },
    async getPartitionKey() { return { partitionKey: { topLevelSite: "https://page.test" } }; },
    async getAll(query) {
      calls.push(query);
      return query.partitionKey ? [{ ...base, value: "partitioned-test" }] : [base, { ...base, domain: "other.test" }, { ...base, path: "/account", name: "private" }, { ...base, name: "expired", expirationDate: 1 }];
    }
  } };
  const session = await downloadSession(api, "https://example.test/files/archive.zip", 42, "https://page.test/download");
  assert.equal(session.cookies.length, 1); assert.equal(session.cookies[0].value, "partitioned-test");
  assert.equal(session.origin, "https://example.test");
  assert.ok(calls.every(query => query.url === "https://example.test/files/archive.zip" && query.storeId === "profile-one"));
});
test("session access errors do not expose signed URLs", async () => {
  const api = { cookies: { async getAllCookieStores() { throw new Error("permission denied for https://example.test?token=private-test"); } } };
  await assert.rejects(downloadSession(api, "https://example.test/file.zip", 1), error => !error.message.includes("private-test") && error.message.includes("session access"));
});

test("partitioned cookies do not discard ordinary login cookies", async () => {
  const base = { domain: "example.test", path: "/", hostOnly: true, secure: true };
  const api = { cookies: {
    async getAllCookieStores() { return [{ id: "profile", tabIds: [42] }]; },
    async getPartitionKey() { return { partitionKey: { topLevelSite: "https://page.test" } }; },
    async getAll(query) { return query.partitionKey ? [{ ...base, name: "partition", value: "one" }] : [{ ...base, name: "login", value: "two" }]; }
  } };
  const session = await downloadSession(api, "https://example.test/file.zip", 42);
  assert.deepEqual(session.cookies.map(cookie => cookie.name).sort(), ["login", "partition"]);
});

test("private tab uses its own cookie store instead of the normal profile", async () => {
  const queries = [];
  const api = { extension: { inIncognitoContext: true }, cookies: {
    async getAllCookieStores() { return [{ id: "normal", tabIds: [1] }, { id: "private", tabIds: [42] }]; },
    async getAll(query) {
      queries.push(query);
      return [{ name: "sid", value: query.storeId === "private" ? "private-session" : "normal-session", domain: "example.test", path: "/", hostOnly: true, secure: true }];
    }
  } };
  const session = await downloadSession(api, item().url, 42);
  assert.equal(session.cookies[0].value, "private-session");
  assert.equal(queries[0].storeId, "private");
});

test("downloads without a tab ID use the worker's cookie context", async () => {
  const api = { extension: { inIncognitoContext: true }, cookies: {
    async getAllCookieStores() { return [{ id: "normal", tabIds: [1] }, { id: "private", tabIds: [42] }]; },
    async getAll(query) { assert.equal(Object.hasOwn(query, "storeId"), false); return []; }
  } };
  await downloadSession(api, item().url);
  const manifest = JSON.parse(fs.readFileSync(new URL("./manifest.json", import.meta.url), "utf8"));
  assert.equal(manifest.incognito, "split");
});

test("private fallback is captured and its status stays out of local storage", async () => {
  const value = { ...item(), incognito: true };
  const f = fake(value); f.api.extension = { inIncognitoContext: true };
  await captureDownload(f.api, value, async request => assert.equal(request.url, value.url));
  assert.deepEqual(f.calls, ["pause", "cancel"]);
  assert.equal(f.local.lastMessage, undefined);
  assert.equal(f.session.lastMessage, "Download sent to SwiftFetch.");
  assert.equal(f.session["handoff-7"], undefined);
});

test("private fallback resumes safely when the app is unavailable", async () => {
  const value = { ...item(), incognito: true };
  const f = fake(value); f.api.extension = { inIncognitoContext: true };
  await captureDownload(f.api, value, async () => { throw new Error("host missing"); });
  assert.deepEqual(f.calls, ["pause", "resume"]);
  assert.equal(f.local.lastMessage, undefined);
  assert.match(f.session.lastMessage, /Kept in browser/);
});

test("background accepts private direct clicks and context-menu transfers", async () => {
  const f = fake();
  let messageHandler, menuHandler;
  const requests = [];
  f.api.extension = { inIncognitoContext: true };
  f.api.downloads.onDeterminingFilename = { addListener() {} };
  f.api.webRequest = { onResponseStarted: { addListener() {} } };
  f.api.tabs = { onRemoved: { addListener() {} } };
  f.api.runtime = {
    id: "test-extension", onMessage: { addListener(fn) { messageHandler = fn; } }, onInstalled: { addListener() {} },
    connectNative() {
      let onMessage;
      return {
        onMessage: { addListener(fn) { onMessage = fn; } }, onDisconnect: { addListener() {} }, disconnect() {},
        postMessage(request) { requests.push(request); queueMicrotask(() => onMessage({ ok: true })); }
      };
    }
  };
  f.api.contextMenus = { onClicked: { addListener(fn) { menuHandler = fn; } } };
  const previous = globalThis.chrome;
  globalThis.chrome = f.api;
  try {
    await import("./background.js?private-test");
    const sender = { id: "test-extension", tab: { id: 42, incognito: true, url: "https://example.test/downloads" } };
    const response = await new Promise(resolve => {
      assert.equal(messageHandler({ action: "direct-click", url: item().url, fileName: "file.zip" }, sender, resolve), true);
    });
    assert.equal(response.ok, true);
    assert.equal(requests.filter(request => request.action === "capture-policy").length, 1);
    assert.equal(requests.filter(request => request.action === "download").length, 1);
    assert.equal(requests.find(request => request.action === "download").session.referer, sender.tab.url);
    menuHandler({ menuItemId: "swiftfetch-download", linkUrl: item().url, pageUrl: sender.tab.url }, sender.tab);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(requests.filter(request => request.action === "download").length, 2);
    assert.equal(f.local.lastMessage, undefined);
    assert.equal(f.session.lastMessage, "Link sent to SwiftFetch.");
    assert.equal(messageHandler({ action: "direct-click", url: item().url }, { ...sender, id: "other-extension" }, () => assert.fail()), undefined);
  } finally {
    if (previous === undefined) delete globalThis.chrome; else globalThis.chrome = previous;
  }
});

test("direct-link policy catches file and release links while leaving page navigation alone", () => {
  const match = globalThis.SwiftFetchLinks.isDownload;
  assert.equal(match("https://github.com/org/repo/releases/download/v1/app.exe"), true);
  assert.equal(match("https://example.test/download?id=123", true), true);
  assert.equal(match("https://example.test/archive.ZIP?token=abc"), true);
  for (const url of ["https://example.test/article", "https://example.test/photo.jpg", "https://example.test/watch.mp4", "https://example.test/doc.pdf", "blob:https://example.test/id", "https://user:password@example.test/file.zip"]) assert.equal(match(url), false);
});
for (const incognito of [false, true]) test(`content script stops a real file click and falls back on rejection (private=${incognito})`, async () => {
  let handler, fallback = 0, requests = 0;
  let accept = true;
  const context = vm.createContext({ URL, setInterval() {}, location: { assign() { fallback++; } }, chrome: {
    extension: { inIncognitoContext: incognito },
    storage: { local: { async get() { return {}; } }, onChanged: { addListener() {} } },
    runtime: { async sendMessage(value) { requests++; assert.equal(value.action, "direct-click"); return { ok: accept }; } }
  }, document: { addEventListener(_name, fn) { handler = fn; } } });
  vm.runInContext(fs.readFileSync(new URL("./link-policy.js", import.meta.url), "utf8"), context);
  vm.runInContext(fs.readFileSync(new URL("./click-capture.js", import.meta.url), "utf8"), context);
  await Promise.resolve();
  let stopped = 0;
  const link = { tagName: "A", href: "https://example.test/file.exe", hasAttribute() { return false; }, getAttribute() { return null; }, isConnected: true, click() { fallback++; } };
  const event = () => ({ isTrusted: true, button: 0, composedPath() { return [link]; }, preventDefault() { stopped++; }, stopImmediatePropagation() {} });
  await handler(event());
  assert.equal(stopped, 1); assert.equal(requests, 1); assert.equal(fallback, 0);
  accept = false;
  await handler(event());
  assert.equal(fallback, 1);
  await handler({ ...event(), isTrusted: false });
  await handler({ ...event(), ctrlKey: true });
  assert.equal(requests, 2);
});
test("browser fallback after a rejected direct click is not intercepted again", async () => {
  const f = fake(); f.session.browserFallbacks = [{ url: item().url, expires: Date.now() + 60000 }];
  await captureDownload(f.api, item(), () => assert.fail("Fallback should stay in browser"));
  assert.deepEqual(f.calls, []);
  assert.deepEqual(f.session.browserFallbacks, []);
});
test("successfully redirected browser entries are removed from browser history", async () => {
  const f = fake(); f.api.downloads.erase = async ({ id }) => { assert.equal(id, 7); f.calls.push("erase"); };
  await captureDownload(f.api, item(), async () => {});
  assert.deepEqual(f.calls, ["pause", "cancel", "erase"]);
});

const item = () => ({ id: 7, url: "https://example.test/file.zip", filename: "C:\\Downloads\\file.zip", state: "in_progress", paused: false, danger: "safe" });
function fake(initial = item()) {
  let current = { ...initial };
  const local = {}, session = {}, calls = [];
  const storage = data => ({
    async get(key) { return key === null ? { ...data } : { [key]: data[key] }; },
    async set(values) { Object.assign(data, values); },
    async remove(key) { delete data[key]; }
  });
  const api = {
    storage: { local: storage(local), session: storage(session) },
    downloads: {
      async pause() { calls.push("pause"); current.paused = true; },
      async resume() { calls.push("resume"); current.paused = false; },
      async cancel() { calls.push("cancel"); current.state = "interrupted"; },
      async search() { return [{ ...current }]; }
    },
    action: { async setBadgeText() {}, async setBadgeBackgroundColor() {} }
  };
  return { api, local, session, calls, current };
}

test("handoff pauses first and cancels only after app acceptance", async () => {
  const f = fake();
  await captureDownload(f.api, item(), async request => {
    f.calls.push("accepted");
    assert.equal(request.fileName, "file.zip");
    assert.match(request.requestId, /^[0-9a-f-]{36}$/);
  });
  assert.deepEqual(f.calls, ["pause", "accepted", "cancel"]);
  assert.deepEqual(f.session, {});
});
test("unavailable app resumes the browser and never cancels", async () => {
  const f = fake();
  await captureDownload(f.api, item(), async () => { throw new Error("host missing"); });
  assert.deepEqual(f.calls, ["pause", "resume"]);
  assert.match(f.local.lastMessage, /Kept in browser/);
});
test("disabled capture leaves downloads alone", async () => {
  const f = fake(); f.local.captureEnabled = false;
  await captureDownload(f.api, item(), () => assert.fail("No native request expected"));
  assert.deepEqual(f.calls, []);
});
test("unsupported URLs, paused, completed and flagged downloads stay in browser", async () => {
  for (const change of [{ url: "blob:https://example.test/id" }, { url: "data:text/plain,hi" }, { paused: true }, { state: "complete" }, { danger: "dangerous" }]) {
    const value = { ...item(), ...change }; const f = fake(value);
    await captureDownload(f.api, value, () => assert.fail("No native request expected"));
    assert.deepEqual(f.calls, []);
  }
  assert.equal(directUrl("file:///C:/secret.txt"), false);
});
test("download that completes before pause is not sent twice", async () => {
  const f = fake();
  f.api.downloads.pause = async () => { f.current.state = "complete"; };
  await captureDownload(f.api, item(), () => assert.fail("No native request expected"));
  assert.deepEqual(f.session, {});
});
test("accepted handoff is not resumed when browser cancellation fails", async () => {
  const f = fake();
  f.api.downloads.cancel = async () => { throw new Error("cancel failed"); };
  await captureDownload(f.api, item(), async () => {});
  assert.equal(f.session["handoff-7"].accepted, true);
  assert.deepEqual(f.calls, ["pause"]);
});
test("recovery reuses the original request ID", async () => {
  const f = fake({ ...item(), paused: true });
  const request = { action: "download", url: item().url, requestId: "original-id" };
  f.session["handoff-7"] = { id: 7, request, accepted: false };
  await recoverHandoffs(f.api, async value => assert.equal(value.requestId, "original-id"));
  assert.deepEqual(f.calls, ["cancel"]);
  assert.deepEqual(f.session, {});
});
test("recovery resumes the browser if the app is unavailable", async () => {
  const f = fake({ ...item(), paused: true });
  f.session["handoff-7"] = { id: 7, request: {}, accepted: false };
  await recoverHandoffs(f.api, async () => { throw new Error("offline"); });
  assert.deepEqual(f.calls, ["resume"]);
});
test("accepted recovery never retransmits to the app", async () => {
  const f = fake({ ...item(), paused: true });
  f.session["handoff-7"] = { id: 7, request: {}, accepted: true };
  await recoverHandoffs(f.api, () => assert.fail("Already accepted"));
  assert.deepEqual(f.calls, ["cancel"]);
});
test("recovery does not resume after acceptance even if cancellation fails", async () => {
  const f = fake({ ...item(), paused: true });
  f.session["handoff-7"] = { id: 7, request: {}, accepted: false };
  f.api.downloads.cancel = async () => { throw new Error("blocked"); };
  await recoverHandoffs(f.api, async () => {});
  assert.deepEqual(f.calls, []);
  assert.equal(f.session["handoff-7"].accepted, true);
});
test("native port uses the registered host and framed reply", async () => {
  let reply;
  let disconnected = false;
  const api = { runtime: { connectNative(host) {
    assert.equal(host, "com.swiftfetch.bridge");
    return {
      onMessage: { addListener(fn) { reply = fn; } },
      onDisconnect: { addListener() {} },
      postMessage(request) { assert.equal(request.action, "ping"); queueMicrotask(() => reply({ ok: true })); },
      disconnect() { disconnected = true; }
    };
  } } };
  assert.equal((await sendNative(api, { action: "ping" })).ok, true);
  assert.equal(disconnected, true);
});
test("native disconnect rejects with the browser error", async () => {
  let disconnect;
  const api = { runtime: { lastError: { message: "host not found" }, connectNative() { return {
    onMessage: { addListener() {} }, onDisconnect: { addListener(fn) { disconnect = fn; } },
    postMessage() { queueMicrotask(() => disconnect()); }, disconnect() {}
  }; } } };
  await assert.rejects(sendNative(api, { action: "ping" }), /host not found/);
});
