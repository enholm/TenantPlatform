const records = [];
const failures = [];
const errors = [];
window.addEventListener("error", event => errors.push(String(event.error || event.message)));

function check(condition, message) {
    records.push({ passed: Boolean(condition), message });
    if (!condition) failures.push(message);
}
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const nextMutation = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

await nextMutation();
const manual = document.querySelector("#manual");
const automatic = document.querySelector("#automatic");
const renewed = document.querySelector("#renewed");
const long = document.querySelector("#long");
const semanticInfo = document.querySelector("#semantic-info");
const emptyInfo = document.querySelector("#empty-info");
const permanent = document.querySelector("#permanent");
const manualClose = manual.querySelector("[data-message-banner-close]");

check(Boolean(manualClose) && manualClose.type === "button" && manualClose.getAttribute("aria-label") === "Lukk melding",
    "close control is a localized, accessible non-submit button");
check([automatic, renewed, long, semanticInfo].every(x => x.querySelector("[data-message-banner-close]")),
    "success, warning, error and semantic information banners share the behavior");
check(!emptyInfo.querySelector("[data-message-banner-close]") && !permanent.querySelector("[data-message-banner-close]"),
    "empty information states and explicitly permanent status indicators are not changed");

manualClose.focus();
const focused = getComputedStyle(manualClose);
check(document.activeElement === manualClose && focused.outlineStyle !== "none" && focused.outlineWidth !== "0px",
    "close control has visible keyboard focus");
manualClose.click();
check(manual.hidden && !automatic.hidden, "manual close hides only the selected banner");

const longRect = long.getBoundingClientRect();
const longCloseRect = long.querySelector("[data-message-banner-close]").getBoundingClientRect();
check(long.scrollWidth <= long.clientWidth + 1 && longCloseRect.right <= longRect.right && longCloseRect.left >= longRect.left,
    "long messages keep the close control inside the banner at this viewport");

await wait(280);
check(automatic.hidden && renewed.hidden && semanticInfo.hidden,
    "eligible banners hide automatically after their configured display period");

renewed.textContent = "Ny advarsel";
renewed.dataset.messageId = "2";
await nextMutation();
check(!renewed.hidden && renewed.querySelector("[data-message-banner-close]"),
    "changed message content is shown again with a new close control period");
await wait(140);
renewed.dataset.messageId = "3";
await nextMutation();
await wait(140);
check(!renewed.hidden, "a new message identity restarts the automatic timeout");
await wait(140);
check(renewed.hidden, "the renewed message hides after its own full timeout");

const removed = document.createElement("div");
removed.className = "alert alert-danger";
removed.textContent = "Temporary";
document.querySelector("main").append(removed);
await nextMutation();
check(Boolean(removed.querySelector("[data-message-banner-close]")), "dynamically rendered banners are enhanced");
removed.remove();
await wait(280);
check(errors.length === 0, "removing a banner cleans up without delayed browser errors");

await fetch("/complete", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
        viewport: new URLSearchParams(location.search).get("viewport"),
        status: failures.length === 0 ? "passed" : "failed",
        checks: records.length,
        width: innerWidth,
        height: innerHeight,
        records,
        errors
    })
});
