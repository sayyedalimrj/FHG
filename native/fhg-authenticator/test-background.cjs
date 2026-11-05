// Pure Node security tests: no account records, passwords or Internet access.
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");

let listener;
const forwarded = [];
const sandbox = {
  URL,
  Set,
  browser: {
    runtime: {
      getURL: p => "moz-extension://test-addon-uuid/" + p,
      onMessage: {addListener: fn => { listener = fn; }},
      sendNativeMessage: async (host, body) => {
        forwarded.push({host, body});
        return {ok: true, accounts: [], value: "SYNTHETIC-NOT-A-REAL-SECRET"};
      }
    }
  }
};
vm.runInNewContext(
  fs.readFileSync(path.join(__dirname, "background.js"), "utf8"),
  sandbox, {filename: "background.js"}
);
assert.equal(typeof listener, "function");
const tab = url => ({url, tab: {id: 42}, frameId: 0});
const test = async () => {
  for (const [action, url] of [
    ["email", "https://chatgpt.com/login"],
    ["password", "https://auth.openai.com/log-in/password"],
    ["totp", "https://auth.openai.com/mfa-challenge/test"]
  ]) {
    const n = forwarded.length;
    const response = await listener({kind:"login", action}, tab(url));
    assert.equal(response.ok, true);
    assert.equal(forwarded.length, n + 1);
    assert.equal(forwarded[n].body.action, action);
    assert.equal(forwarded[n].host, "fhg.authenticator");
  }
  for (const url of [
    "https://evil.invalid/log-in/password",
    "http://auth.openai.com/log-in/password",
    "https://auth.openai.com.evil.invalid/mfa-challenge/test",
    "https://chatgpt.com/evil/form",
    "https://auth.openai.com/logout"
  ]) {
    const n = forwarded.length;
    const response = await listener({kind:"login", action:"password"},tab(url));
    assert.equal(response.ok, false);
    assert.equal(forwarded.length, n, "Host invoked for invalid origin/path");
  }
  const list = await listener({kind:"list"}, {url:"moz-extension://test-addon-uuid/popup.html"});
  assert.equal(list.ok, true);
  const n = forwarded.length;
  const rejected = await listener({kind:"list"}, tab("https://chatgpt.com/"));
  assert.equal(rejected.ok, false);
  assert.equal(forwarded.length, n, "Web page could enumerate OTP codes");
  console.log("PASS: native message routing, approved forms only, hostile origins and tab/popup boundary");
};
test().catch(err => {console.error(err); process.exit(1);});
