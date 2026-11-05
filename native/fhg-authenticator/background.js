"use strict";
const HOST = "fhg.authenticator";
const TRUSTED = new Set(["https://chatgpt.com", "https://auth.openai.com"]);
browser.runtime.onMessage.addListener(async (message, sender) => {
  if (!message || typeof message !== "object") return {ok:false,error:"INVALID_REQUEST"};
  if (message.kind === "list") {
    if (sender.tab || sender.url !== browser.runtime.getURL("popup.html"))
      return {ok:false,error:"POPUP_ONLY"};
    return browser.runtime.sendNativeMessage(HOST, {action:"list"});
  }
  if (message.kind !== "login" || !sender.tab || sender.frameId !== 0)
    return {ok:false,error:"NOT_TRUSTED_TAB"};
  let url;
  try { url = new URL(sender.url); }
  catch { return {ok:false,error:"INVALID_URL"}; }
  if (url.protocol !== "https:" || !TRUSTED.has(url.origin))
    return {ok:false,error:"ORIGIN_REJECTED"};
  const action = message.action;
  const p = url.pathname;
  if (action === "email") {
    if (url.origin !== "https://chatgpt.com" ||
        !(p === "/" || p === "/login" || p.startsWith("/auth/login")))
      return {ok:false,error:"PATH_REJECTED"};
  } else if (action === "password") {
    if (url.origin !== "https://auth.openai.com" || !p.startsWith("/log-in/password"))
      return {ok:false,error:"PATH_REJECTED"};
  } else if (action === "totp") {
    if (url.origin !== "https://auth.openai.com" || !p.startsWith("/mfa-challenge/"))
      return {ok:false,error:"PATH_REJECTED"};
  } else return {ok:false,error:"UNKNOWN_ACTION"};
  return browser.runtime.sendNativeMessage(HOST, {action,origin:url.origin,path:p});
});
