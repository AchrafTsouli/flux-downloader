import { test } from "node:test";
import assert from "node:assert/strict";
import { shouldCapture } from "./capture-policy.js";
test("capture types and manual force", () => {
 assert.equal(shouldCapture("https://example.test/movie.mp4", "", {extensions:"zip mp4"}), true);
 assert.equal(shouldCapture("https://example.test/page.html", "", {extensions:"zip mp4"}), false);
 assert.equal(shouldCapture("https://example.test/download?id=1", "", {extensions:"zip"}, true), true);
 assert.equal(shouldCapture("https://example.test/file.exe", "opaque-name", {extensions:".EXE"}), true);
});
test("excluded sites and addresses also apply to force", () => {
 const policy = {excludedSites:"*.example.test", excludedAddresses:"https://files.test/private/"};
 assert.equal(shouldCapture("https://cdn.example.test/file.zip", "", policy,true), false);
 assert.equal(shouldCapture("https://example.test/file.zip", "", policy,true), false);
 assert.equal(shouldCapture("https://files.test/private/file.zip", "", policy,true), false);
 assert.equal(shouldCapture("https://example.test.evil.test/file.zip", "", policy), true);
 assert.equal(shouldCapture("https://cdn.example.test/file.zip", "", {excludedSites:"*.EXAMPLE.TEST"},true), false);
});
test("policy rejects credentials and disabled capture", () => {
 assert.equal(shouldCapture("https://name:secret@example.test/file.zip"), false);
 assert.equal(shouldCapture("https://example.test/file.zip", "", {enabled:false}, true), false);
});
