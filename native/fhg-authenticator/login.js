"use strict";
// Form selectors grounded in the user's saved login.zip (email / password / MFA).
// No credential is logged, persisted or sent to any non-official origin.
(() => {
  if (window !== window.top || location.protocol !== "https:") return;
  const host = location.hostname;
  if (host !== "chatgpt.com" && host !== "auth.openai.com") return;
  const submitted = new Set();
  const challenged = () => Boolean(document.querySelector(
    '[name="cf-turnstile-response"], iframe[src*="challenges.cloudflare.com"], [data-sitekey], input[name="captcha"]'
  ));
  async function complete(step, input) {
    const form = input?.closest("form");
    if (!form || challenged() || input.disabled) return;
    const id = location.origin + location.pathname + ":" + step;
    if (submitted.has(id)) return;
    submitted.add(id);
    try {
      const response = await browser.runtime.sendMessage({kind:"login",action:step});
      if (!response?.ok || !response.value || !input.isConnected) return;
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,"value")?.set;
      if (!setter) return;
      setter.call(input,String(response.value));
      input.dispatchEvent(new Event("input",{bubbles:true}));
      input.dispatchEvent(new Event("change",{bubbles:true}));
      await new Promise(resolve => setTimeout(resolve,600));
      if (challenged() || !input.isConnected || input.value !== String(response.value)) return;
      const button = form.querySelector('button[type="submit"],input[type="submit"]');
      if (button?.disabled) return;
      form.requestSubmit(button || undefined);
    } catch { /* No logs of secrets */ }
  }
  function scan() {
    if (challenged()) return;
    if (host === "chatgpt.com" &&
        (location.pathname === "/" || location.pathname === "/login" || location.pathname.startsWith("/auth/login"))) {
      const field = document.querySelector('input[type="email"][name="username"],input[autocomplete="email"][type="email"]');
      if (field) complete("email",field);
    } else if (host === "auth.openai.com" && location.pathname.startsWith("/log-in/password")) {
      const field = document.querySelector('input[name="current-password"][type="password"]');
      if (field) complete("password",field);
    } else if (host === "auth.openai.com" && location.pathname.startsWith("/mfa-challenge/")) {
      const field = document.querySelector('input[name="code"][autocomplete="one-time-code"]');
      if (field) complete("totp",field);
    }
  }
  scan();
  const observer = new MutationObserver(scan);
  observer.observe(document.documentElement,{childList:true,subtree:true});
  setTimeout(() => observer.disconnect(),20000);
})();
