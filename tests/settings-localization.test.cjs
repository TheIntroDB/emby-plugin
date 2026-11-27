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

async function setup({host, culture, lang = "", browser = "en-US", fail = false, readSettings, readLibraries, apiStats, controllerSource = js} = {}) {
    const nodes = new Map();
    function node() {
        return { style: {}, events: {}, children: [], value: "", textContent: "",
            appendChild(child) { this.children.push(child); },
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
    const pageEvents = {};
    const saves = [];
    vm.runInNewContext(controllerSource, {
        define(_deps, factory) { controller = factory(null, null, null, host); },
        document: { createElement: node, documentElement: { lang, getAttribute() { return culture; } } },
        navigator: { language: browser },
        setTimeout() {},
        Dashboard: { showLoadingMsg() {}, hideLoadingMsg() {}, processPluginConfigurationUpdateResult() {} },
        ApiClient: {
            getPluginConfiguration: readSettings,
            getCurrentUserId: () => readLibraries ? "test-user" : "",
            updatePluginConfiguration(_id, config) { saves.push({...config}); return Promise.resolve(); },
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
                if (readLibraries && request.url === "Users/test-user/Views") return readLibraries();
                if (apiStats && request.url === "TheIntroDB/Validation/ApiKeyStats") {
                    return Promise.resolve({isValid: true, stats: apiStats});
                }
                clearUrl = request.url;
                return Promise.resolve({ ClearedEntries: 7 });
            }
        }
    });
    controller({querySelector: get, querySelectorAll: () => [...messages.values()], addEventListener(event, handler) { pageEvents[event] = handler; }});
    await new Promise(setImmediate);
    return {messages, get, requestedLocale, pageEvents, saves, get clearUrl() { return clearUrl; }};
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

test("Statistics use a translated label or the original readable fallback", async () => {
    const expectedChineseLabel = JSON.parse(fs.readFileSync("TheIntroDB/Configuration/locales/zh-CN.json", "utf8")).stat_total;
    const fixture = await setup({
        lang: "zh-CN", readSettings: () => Promise.resolve({ApiKey: "test-key"}),
        apiStats: {total: 12, cache_hits: 3},
        // Simulate an added statistic before its translation is contributed.
        controllerSource: js.replace('["total", "accepted", "pending"', '["cache_hits", "total", "accepted", "pending"'),
    });
    await fixture.pageEvents.pageshow();
    const infoButton = fixture.get("#ApiKeyValidationStatus").children.find((child) => child.events.click);
    infoButton.events.click({preventDefault() {}});
    const grid = fixture.get("#ApiStatsContent").children.at(-1);
    const labels = grid.children.map((card) => card.children[0].textContent);
    assert.ok(labels.includes(expectedChineseLabel));
    assert.ok(labels.includes("Cache Hits"));
});

const submitSelector = 'button[type="submit"]';
const configFormSelector = "#TheIntroDbConfigForm";
function trySave(fixture) {
    let prevented = false;
    fixture.get(configFormSelector).events.submit({preventDefault() { prevented = true; }});
    assert.ok(prevented);
}

test("Save is disabled before loading and waits for the full form before submitting", async () => {
    assert.match(html, /<button\b[^>]*type="submit"[^>]*\bdisabled\b/);
    let readCount = 0;
    let finishLibraries;
    const libraries = new Promise((resolve) => { finishLibraries = resolve; });
    const original = {EnableRecap: false, RefreshAfterDays: 45, MaxLookupsPerRun: 750, MarkerRepairIntervalHours: 20};
    const fixture = await setup({
        readSettings: () => { readCount += 1; return Promise.resolve({...original}); },
        readLibraries: () => libraries,
    });
    const button = fixture.get(submitSelector);
    trySave(fixture);
    assert.equal(readCount, 0);
    const load = fixture.pageEvents.pageshow();
    assert.equal(fixture.pageEvents.viewshow(), load);
    await new Promise(setImmediate);
    assert.equal(button.disabled, true);
    trySave(fixture);
    assert.equal(readCount, 1);
    assert.deepEqual(fixture.saves, []);
    finishLibraries({Items: []});
    await load;
    assert.equal(button.disabled, false);
    trySave(fixture);
    await new Promise(setImmediate);
    assert.equal(fixture.saves.length, 1);
    for (const [key, value] of Object.entries(original)) assert.equal(fixture.saves[0][key], value, key);
});

test("Failed settings loads leave Save blocked until a later successful load", async () => {
    let shouldFail = true;
    const fixture = await setup({
        readSettings: () => Promise.resolve({RefreshAfterDays: 42}),
        readLibraries: () => shouldFail ? Promise.reject(new Error("offline")) : Promise.resolve({Items: []}),
    });
    await fixture.pageEvents.pageshow();
    assert.equal(fixture.get(submitSelector).disabled, true);
    trySave(fixture);
    await new Promise(setImmediate);
    assert.deepEqual(fixture.saves, []);
    shouldFail = false;
    await fixture.pageEvents.viewshow();
    assert.equal(fixture.get(submitSelector).disabled, false);
});
