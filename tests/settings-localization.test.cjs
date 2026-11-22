// SPDX-License-Identifier: GPL-3.0-or-later


const assert = require("node:assert/strict");
const fs = require("node:fs");
const test = require("node:test");
const vm = require("node:vm");
const html = fs.readFileSync("TheIntroDB/Configuration/configPage.html", "utf8");
const js = fs.readFileSync("TheIntroDB/Configuration/configPage.js", "utf8");
const keys = [...html.matchAll(/data-i18n="([^"]+)"/g)].map((match) => match[1]);

function setup({host, culture, lang = "", browser = "en-US"} = {}) {
    const nodes = new Map();
    function node() {
        return { style: {}, events: {}, value: "", textContent: "",
            addEventListener(event, handler) { this.events[event] = handler; },
            querySelector: get, querySelectorAll() { return []; } };
    }
    function get(selector) {
        if (!nodes.has(selector)) nodes.set(selector, node());
        return nodes.get(selector);
    }
    const messages = new Map(keys.map((key) => [key, {getAttribute: () => key}]));
    let controller;
    let clearUrl;
    vm.runInNewContext(js, {
        define(_deps, factory) { controller = factory(null, null, null, host); },
        document: { documentElement: { lang, getAttribute() { return culture; } } },
        navigator: { language: browser },
        setTimeout() {},
        ApiClient: {
            getUrl(url) { return url; },
            ajax(request) { clearUrl = request.url; return Promise.resolve({ ClearedEntries: 7 }); }
        }
    });
    controller({querySelector: get, querySelectorAll: () => [...messages.values()], addEventListener() {}});
    return {messages, get, get clearUrl() { return clearUrl; }};
}

test("Emby locale overrides the browser, and all localized HTML keys resolve", () => {
    const cn = setup({host: { getCurrentLocale: () => "zh_CN" }, lang:"en"});
    const en = setup({host: { getCurrentLocale: () => "en-US" }, lang:"zh-CN", browser:"zh-CN"});
    assert.equal(cn.messages.get("save").textContent, "保存");
    assert.equal(en.messages.get("save").textContent, "Save");
    for (const key of keys) {
        assert.ok(/[\p{Script=Han}]/u.test(cn.messages.get(key).textContent), key);
        assert.notEqual(en.messages.get(key).textContent, key);
    }
    assert.match(html, /href="https:\/\/theintrodb\.org"/);
});
test("culture, document and browser fallbacks, with unsupported locales remaining English", () => {
    assert.equal(setup({host:{getCurrentLocale(){throw Error("unavailable");}}, culture:"zh-Hans-CN"}).messages.get("save").textContent, "保存");
    assert.equal(setup({lang:"zh-CN"}).messages.get("save").textContent, "保存");
    assert.equal(setup({browser:"zh-CN"}).messages.get("save").textContent, "保存");
    assert.equal(setup({lang:"zh-TW", browser:"zh-CN"}).messages.get("save").textContent, "Save");
    assert.equal(setup({lang:"fr-FR"}).messages.get("save").textContent, "Save");
});
test("localized dynamic message preserves the original cache API route and count", async () => {
    const fixture = setup({lang:"zh-CN"});
    fixture.get("#ClearNotFoundCache").events.click();
    await new Promise(setImmediate);
    assert.equal(fixture.clearUrl, "TheIntroDB/NotFoundCache/Clear");
    assert.match(fixture.get("#NotFoundCacheStatus").textContent, /已清除 7 条/);
});
