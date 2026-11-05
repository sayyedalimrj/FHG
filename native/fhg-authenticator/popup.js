"use strict";
const grid = document.getElementById("accounts");
const status = document.getElementById("status");
let accounts = [], loadedAt = Date.now(), refreshing = false;
async function refresh() {
  if (refreshing) return;
  refreshing = true;
  try {
    const result = await browser.runtime.sendMessage({kind:"list"});
    if (!result?.ok || !Array.isArray(result.accounts)) throw new Error("HOST_UNAVAILABLE");
    accounts = result.accounts; loadedAt = Date.now();
    status.classList.remove("error");
    status.textContent = accounts.length ? accounts.length + " account(s) · Automatic 2FA" :
      "No saved accounts. Add accounts in FHG Accounts.";
    render();
  } catch {
    grid.replaceChildren();
    status.classList.add("error");
    status.textContent = "FHG vault unavailable. Reinstall the signed companion, then restart FHG Firefox.";
  } finally { refreshing = false; }
}
function render() {
  grid.replaceChildren();
  for (const account of accounts) {
    const card=document.createElement("article"); card.className="card"+(account.active?" active":"");
    const top=document.createElement("div"); top.className="label";
    const title=document.createElement("span"); title.textContent=account.label; top.appendChild(title);
    if(account.active){const tag=document.createElement("span");tag.className="badge";tag.textContent="ACTIVE";top.appendChild(tag)}
    card.appendChild(top);
    const code=document.createElement("div"); code.className="code";code.textContent=account.error?"Invalid key":account.code;
    card.appendChild(code);
    const foot=document.createElement("div");foot.className="foot";
    const timer=document.createElement("span");timer.dataset.remaining=String(account.secondsRemaining||0);foot.appendChild(timer);
    const copy=document.createElement("button");copy.className="copy";copy.textContent="Copy code";
    copy.disabled=Boolean(account.error||!account.code);
    copy.addEventListener("click",async()=>{try{await navigator.clipboard.writeText(account.code);copy.textContent="Copied"}catch{copy.textContent="Copy failed"}});
    foot.appendChild(copy);card.appendChild(foot);grid.appendChild(card);
  }
  countdown();
}
function countdown(){
  let expired=false;
  const elapsed=Math.floor((Date.now()-loadedAt)/1000);
  grid.querySelectorAll("[data-remaining]").forEach((node,i)=>{
    const data=accounts[i];
    const sec=Math.max(0,(data.secondsRemaining||0)-elapsed);
    node.textContent=data.error?"Repair the Base32 key in FHG Accounts":sec+" seconds remaining";
    if(sec===0&&!data.error)expired=true;
  });
  if(expired)refresh();
}
document.getElementById("refresh").addEventListener("click",refresh);
setInterval(countdown,1000);
refresh();
