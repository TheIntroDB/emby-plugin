// SPDX-License-Identifier: GPL-3.0-or-later


const assert = require("node:assert/strict");
const fs = require("node:fs");
const test = require("node:test");
const vm = require("node:vm");
const html = fs.readFileSync("TheIntroDB/Configuration/configPage.html", "utf8");
const englishJson = fs.readFileSync("TheIntroDB/Configuration/locales/en.json", "utf8");
const js = fs.readFileSync("TheIntroDB/Configuration/configPage.js", "utf8")
    .replace("/* DEFAULT_LOCALE */ null", () => englishJson);
const keys = [...html.matchAll(/data-i18n="([^"]+)"/g)].map((match) => match[1]);

async function setup({host, culture, lang = "", browser = "en-US", fail = false} = {}) {
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
    let requestedLocale;
    vm.runInNewContext(js, {
        define(_deps, factory) { controller = factory(null, null, null, host); },
        document: { documentElement: { lang, getAttribute() { return culture; } } },
        navigator: { language: browser },
        setTimeout() {},
        ApiClient: {
            getUrl(url, params) {
                if (url === "TheIntroDB/Settings/Translations") requestedLocale = params.Locale;
                return url;
            },
            ajax(request) {
                if (request.url === "TheIntroDB/Settings/Translations") {
                    if (fail) return Promise.reject(new Error("not available"));
                    const locale = requestedLocale.toLowerCase().replace(/_/g, "-");
                    const filename = /^(zh|zh-cn|zh-sg|zh-hans(?:-.*)?)$/.test(locale) ? "zh-CN" : "en";
                    return Promise.resolve(JSON.parse(fs.readFileSync("TheIntroDB/Configuration/locales/" + filename + ".json", "utf8")));
                }
                clearUrl = request.url;
                return Promise.resolve({ ClearedEntries: 7 });
            }
        }
    });
    controller({querySelector: get, querySelectorAll: () => [...messages.values()], addEventListener() {}});
    await new Promise(setImmediate);
    return {messages, get, requestedLocale, get clearUrl() { return clearUrl; }};
}

test("Emby locale overrides the browser, and all localized HTML keys resolve", async () => {
    const cn = await setup({host: { getCurrentLocale: () => "zh_CN" }, lang:"en"});
    const en = await setup({host: { getCurrentLocale: () => "en-US" }, lang:"zh-CN", browser:"zh-CN"});
    assert.equal(cn.requestedLocale, "zh_CN");
    assert.equal(en.requestedLocale, "en-US");
    assert.equal(cn.messages.get("save").textContent, "保存");
    assert.equal(en.messages.get("save").textContent, "Save");
    for (const key of keys) {
        assert.ok(/[\p{Script=Han}]/u.test(cn.messages.get(key).textContent), key);
        assert.notEqual(en.messages.get(key).textContent, key);
    }
    assert.match(html, /href="https:\/\/theintrodb\.org"/);
});
test("culture, document and browser fallbacks, with unsupported locales remaining English", async () => {
    assert.equal((await setup({host:{getCurrentLocale(){throw Error("unavailable");}}, culture:"zh-Hans-CN"})).messages.get("save").textContent, "保存");
    assert.equal((await setup({lang:"zh-CN"})).messages.get("save").textContent, "保存");
    assert.equal((await setup({browser:"zh-CN"})).messages.get("save").textContent, "保存");
    assert.equal((await setup({lang:"zh-TW", browser:"zh-CN"})).messages.get("save").textContent, "Save");
    assert.equal((await setup({lang:"fr-FR"})).messages.get("save").textContent, "Save");
});
test("localized dynamic message preserves the original cache API route and count", async () => {
    const fixture = await setup({lang:"zh-CN"});
    fixture.get("#ClearNotFoundCache").events.click();
    await new Promise(setImmediate);
    assert.equal(fixture.clearUrl, "TheIntroDB/NotFoundCache/Clear");
    assert.match(fixture.get("#NotFoundCacheStatus").textContent, /已清除 7 条/);
});

test("failed locale requests leave original text and the settings controls available", async () => {
    const fixture = await setup({lang: "zh-CN", fail: true});
    assert.equal(fixture.messages.get("save").textContent, undefined);
    assert.equal(typeof fixture.get("#TheIntroDbConfigForm").events.submit, "function");
    fixture.get("#ClearNotFoundCache").events.click();
    assert.equal(fixture.get("#NotFoundCacheStatus").textContent, "Clearing...");
    await new Promise(setImmediate);
    assert.match(fixture.get("#NotFoundCacheStatus").textContent, /^Cleared 7 cached/);
});

test("locale resource keys and interpolation placeholders stay in sync", () => {
    const load = (locale) => JSON.parse(fs.readFileSync("TheIntroDB/Configuration/locales/" + locale + ".json", "utf8"));
    const en = load("en");
    const cn = load("zh-CN");
    assert.deepEqual(Object.keys(cn).sort(), Object.keys(en).sort());
    const placeholders = (value) => [...value.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
    for (const key of Object.keys(en)) {
        assert.deepEqual(placeholders(cn[key]), placeholders(en[key]), key);
        assert.ok(en[key].trim() && cn[key].trim(), key);
    }
});
