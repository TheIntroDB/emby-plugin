// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

define(["emby-input", "emby-button", "emby-checkbox", "globalize"], function (_input, _button, _checkbox, globalize) {
    const strings = {
        en: {
            "connection": "Connection",
            "apiKey": "API key (optional)",
            "apiKeyHelp": "Optional Bearer token; when set, your pending and accepted submissions are weighted higher in results, and requests use the signed-in rate limits (120 requests per 10 seconds, 100,000 lookups per day instead of 30 and 5,000). A key is what lets a large library scan finish in one run.",
            "scanScope": "Scan scope",
            "scanScopeLabel": "Only scan selected shows or libraries (optional)",
            "allMedia": "All media",
            "browse": "Browse libraries and shows",
            "clearSelection": "Clear selection",
            "scanScopeHelp": "Whitelisting a library scans all movies and episodes inside it. Selecting individual movies or shows only scans those specific items. Any media outside the selected libraries or items is left unchanged.",
            "missingCache": "Not-found cache",
            "clearMissing": "Clear not-found cache",
            "missingHelp": "Items TheIntroDB has no data for are skipped for 14 days to save API requests. If you just submitted segments on theintrodb.org, clear the cache and run the Media segment scan to pull them into your library.",
            "segments": "Segments",
            "intro": "Provide intro segments",
            "introHelp": "Include intro segments from TheIntroDB in Emby media processing.",
            "recap": "Provide recap segments",
            "recapHelp": "Include recap segments from TheIntroDB.",
            "credits": "Provide credits (outro) segments",
            "creditsHelp": "Include credits/outro segments from TheIntroDB.",
            "preview": "Provide preview segments",
            "previewHelp": "Include preview segments from TheIntroDB.",
            "createChapters": "Create chapter entries for segments",
            "createChaptersHelp": "When enabled, readable chapter entries are created alongside native markers (IntroStart, CreditsStart). Recaps and Previews have no native marker support in Emby and will be invisible without chapters. Disable this to hide the identification string from chapter names.",
            "markerProtection": "Marker protection",
            "protectIntro": "Protect existing intro markers",
            "protectIntroHelp": "Recommended. Any existing complete or partial native intro marker prevents TheIntroDB from changing that intro pair.",
            "protectCredits": "Protect existing credits markers",
            "protectCreditsHelp": "Recommended. An existing native credits marker remains authoritative.",
            "replaceOwned": "Replace existing TheIntroDB-owned markers",
            "replaceOwnedHelp": "Off by default. Only chapters carrying a matching durable TheIntroDB ownership token are removed before updated markers are applied. Native, manual, legacy and third-party chapters remain unchanged.",
            "scanBehavior": "Scanning behavior",
            "ignoreExisting": "Ignore media that already has segments",
            "ignoreExistingHelp": "When enabled, items that already have segments are skipped to avoid refetching, until their own segments are due for a refresh.",
            "refreshAfterDays": "Refresh TheIntroDB segments after (days)",
            "refreshAfterDaysHelp": "Items with segments from TheIntroDB are fetched again after this many days, so corrected submissions arrive. Default 30. 0 never refreshes.",
            "fetchOnAdd": "Automatically fetch segments on item addition",
            "fetchOnAddHelp": "When enabled, segments are fetched for newly added items — after their metadata download, and on playback start. Existing segments are kept or refreshed as configured above.",
            "maxLookups": "Maximum API lookups per run",
            "maxLookupsHelp": "Bounds large scans: how many API lookups a single run may perform. Default 400. Zero disables API lookups for the task. Large libraries may need more; re-running the scan continues where it left off.",
            "privacy": "Privacy & logging",
            "usageReporting": "Enable anonymous usage reporting",
            "usageReportingHelp": "When enabled, anonymous usage metrics may be collected to help improve the plugin.",
            "fileLogging": "Enable file logging",
            "fileLoggingHelp": "When enabled, plugin logs are written to a separate file (data/theintrodb/theintrodb.log) instead of the main Emby log.",
            "maintenance": "Maintenance",
            "repairHours": "Marker repair interval (hours)",
            "repairHoursHelp": "How often the plugin checks items it has segments for and restores TheIntroDB markers that Emby may have overwritten (e.g. when Emby regenerates placeholder chapters for files without embedded chapters). Set to 0 to disable automatic repair.",
            "repairTip": "Tip: Emby's library setting \"Generate chapters for videos that don't contain embedded chapter information\" (Dashboard → Libraries → edit a library) can overwrite TheIntroDB markers on files without embedded chapters. Disabling it there prevents the overwrite entirely.",
            "debug": "Debug",
            "internalId": "Item InternalId",
            "internalIdHelp": "Loads stored segments for a specific item from the local DB.",
            "loadSegments": "Load Segments",
            "save": "Save",
            "back": "Back",
            "chooseLibraries": "Choose Libraries",
            "cancel": "Cancel",
            "done": "Done",
            "includeFolder": "Whitelist whole folder",
            "chooseLibraryHelp": "Open a library to browse its movies and shows. Inside a library, you can whitelist the whole folder or pick individual movies and shows.",
            "searchLibraries": "Search libraries",
            "loadingLibraries": "Loading libraries...",
            "apiStats": "API Key Stats",
            "close": "Close",
            "overviewBefore": "Segment data (intro, recap, credits, preview) is fetched from",
            "overviewAfter": "(v4) using each item's TVDB, TMDB or IMDB ID. TVDB is preferred: it is the API's identity, and it is what lets series with a non-default display order (DVD, absolute) resolve to the right episode. Segments are not loaded when you press play; run the TheIntroDB media segment scan scheduled task to populate them.",
            "unknownItem": "Unknown item",
            "library": "Library",
            "previousLibrary": "Previously selected library",
            "previousItem": "Previously selected item",
            "apiStatsButton": "Show API key stats",
            "noStats": "No stats available.",
            "apiVerified": "API key verified.",
            "invalidToken": "Invalid or expired token.",
            "checkingKey": "Checking API key...",
            "validationUnavailable": "Could not validate API key right now.",
            "excludeFolder": "Remove whole folder",
            "searchThisLibrary": "Search this library",
            "browseLibraryHelp": "Movies and series can be selected individually, or you can whitelist the whole folder to include everything inside it.",
            "browseLibrary": "Choose a library to browse its contents.",
            "noSupportedLibraries": "No supported libraries were returned for this user.",
            "thisLibrary": "this library",
            "noItems": "No items found.",
            "noLibraries": "No libraries found.",
            "folderIncluded": "Folder whitelisted",
            "openFolder": "Open folder",
            "selectedShow": "Selected show",
            "selectedMovie": "Selected movie",
            "selectItem": "Click to select",
            "loadingContents": "Loading library contents...",
            "contentsFailed": "Could not load library contents right now.",
            "librariesFailed": "Could not load libraries right now.",
            "clearing": "Clearing...",
            "clearFailed": "Failed to clear the not-found cache.",
            "invalidInternalId": "Enter a valid InternalId.",
            "segmentsFailed": "Failed to load segments.",
            "librariesSummary": "Libraries: {names}",
            "manyLibraries": "{count} libraries selected: {names}, ...",
            "manyItems": "{count} items selected: {names}, ...",
            "showingContents": "Showing movies and series from {library}.",
            "emptyContents": "No movies or series were returned from {library}.",
            "clearedEntries": "Cleared {count} cached not-found item(s). Run the Media segment scan to re-check your library.",
            "stat_total": "Total",
            "stat_accepted": "Accepted",
            "stat_pending": "Pending",
            "stat_rejected": "Rejected",
            "stat_acceptance_rate": "Acceptance Rate",
            "stat_current_streak": "Current Streak",
            "stat_best_streak": "Best Streak",
            "stat_total_time_saved_ms": "Total Time Saved Ms",
            "hours": "{value}h",
            "minutes": "{value}m",
            "seconds": "{value}s"
        },
        "zh-CN": {
            "connection": "连接",
            "apiKey": "API 密钥（可选）",
            "apiKeyHelp": "填写 API 密钥后，你提交的待审核和已通过标记在匹配结果中权重更高，限额也从每 10 秒 30 次、每天 5,000 次提升到每 10 秒 120 次、每天 100,000 次，有助于一次扫完大型媒体库。",
            "scanScope": "扫描范围",
            "scanScopeLabel": "仅扫描选定的影片、剧集或媒体库（可选）",
            "allMedia": "全部媒体",
            "browse": "浏览媒体库和影片",
            "clearSelection": "清空选择",
            "scanScopeHelp": "选中整个媒体库会扫描其中所有电影和剧集。也可以单独选择某部电影或剧集，范围之外的媒体不受影响。",
            "missingCache": "未命中缓存",
            "clearMissing": "清除未命中缓存",
            "missingHelp": "TheIntroDB 暂无数据的项目会跳过 14 天，以节省 API 请求。刚在 theintrodb.org 提交标记的话，可以清除缓存并运行「TheIntroDB Media Segment Scan」任务重新查询。",
            "segments": "片段类型",
            "intro": "获取片头片段",
            "introHelp": "将 TheIntroDB 的片头片段用于 Emby 媒体处理。",
            "recap": "获取前情回顾片段",
            "recapHelp": "从 TheIntroDB 获取前情回顾片段。",
            "credits": "获取片尾片段",
            "creditsHelp": "从 TheIntroDB 获取片尾片段。",
            "preview": "获取下集预告片段",
            "previewHelp": "从 TheIntroDB 获取下集预告片段。",
            "createChapters": "为片段创建章节条目",
            "createChaptersHelp": "除 Emby 原生的片头和片尾标记外，同时生成带名称的章节。Emby 没有原生的前情回顾和预告标记，这两类片段关闭本项后不会出现在章节列表中。",
            "markerProtection": "标记保护",
            "protectIntro": "保护现有片头标记",
            "protectIntroHelp": "建议开启。只要已有完整或部分原生片头标记，TheIntroDB 就不会修改这组标记。",
            "protectCredits": "保护现有片尾标记",
            "protectCreditsHelp": "建议开启。已有原生片尾标记时以它为准。",
            "replaceOwned": "更新 TheIntroDB 自己写入的标记",
            "replaceOwnedHelp": "默认关闭。更新时只替换能确认由 TheIntroDB 写入的章节，不动原生、手动、旧版或第三方章节。",
            "scanBehavior": "扫描行为",
            "ignoreExisting": "跳过已有片段的媒体",
            "ignoreExistingHelp": "跳过已有片段的项目，避免重复请求；由 TheIntroDB 写入的片段到期后仍会刷新。",
            "refreshAfterDays": "TheIntroDB 片段刷新间隔（天）",
            "refreshAfterDaysHelp": "已有 TheIntroDB 片段的项目会在指定天数后重新查询，以获取修正后的标记。默认 30 天，设为 0 则不自动刷新。",
            "fetchOnAdd": "新增媒体时自动获取片段",
            "fetchOnAddHelp": "开启后，会在新增媒体的元数据下载完成后和开始播放时获取片段。已有片段按上方设置保留或刷新。",
            "maxLookups": "单次扫描 API 查询上限",
            "maxLookupsHelp": "限制单次扫描的查询次数。默认 400，设为 0 则任务不查询 API。大型媒体库可以适当提高，或再次运行扫描继续处理。",
            "privacy": "隐私与日志",
            "usageReporting": "发送匿名使用统计",
            "usageReportingHelp": "开启后，插件可能收集匿名使用统计，用于改进插件。",
            "fileLogging": "单独保存插件日志",
            "fileLoggingHelp": "开启后，插件日志写入 data/theintrodb/theintrodb.log，而不是 Emby 主日志。",
            "maintenance": "维护",
            "repairHours": "标记修复间隔（小时）",
            "repairHoursHelp": "定期检查已记录的片段，并恢复可能被 Emby 覆盖的标记，例如 Emby 为无内嵌章节的影片生成章节时。设为 0 可关闭自动修复。",
            "repairTip": "提示：Emby 的「为没有内嵌章节的视频生成章节」可能覆盖插件标记。若遇到这个问题，可在「控制台 → 媒体库 → 编辑对应媒体库」中关闭该选项。",
            "debug": "调试",
            "internalId": "媒体内部 ID（InternalId）",
            "internalIdHelp": "从本地数据库读取指定媒体的片段。",
            "loadSegments": "读取片段",
            "save": "保存",
            "back": "返回",
            "chooseLibraries": "选择媒体库",
            "cancel": "取消",
            "done": "完成",
            "includeFolder": "包含整个媒体库",
            "chooseLibraryHelp": "打开媒体库浏览其中的电影和剧集。可以选中整个媒体库，也可以逐部选择。",
            "searchLibraries": "搜索媒体库",
            "loadingLibraries": "正在加载媒体库…",
            "apiStats": "API 密钥统计",
            "close": "关闭",
            "overviewBefore": "本插件通过",
            "overviewAfter": "（v4），按媒体的 TVDB、TMDB 或 IMDb ID 获取片头、前情回顾、片尾和预告的时间点。建议优先使用 TVDB ID，尤其是采用 DVD 顺序或绝对集数的剧集。请运行「TheIntroDB Media Segment Scan」计划任务预先获取片段。",
            "unknownItem": "未知项目",
            "library": "媒体库",
            "previousLibrary": "此前选中的媒体库",
            "previousItem": "此前选中的项目",
            "apiStatsButton": "查看 API 密钥统计",
            "noStats": "暂无统计数据。",
            "apiVerified": "API 密钥验证通过。",
            "invalidToken": "API 密钥无效或已过期。",
            "checkingKey": "正在验证 API 密钥…",
            "validationUnavailable": "暂时无法验证 API 密钥。",
            "excludeFolder": "不再包含整个媒体库",
            "searchThisLibrary": "搜索当前媒体库",
            "browseLibraryHelp": "可逐部选择电影和剧集，也可以包含整个媒体库。",
            "browseLibrary": "请选择要浏览的媒体库。",
            "noSupportedLibraries": "此用户没有可供选择的媒体库。",
            "thisLibrary": "当前媒体库",
            "noItems": "没有找到匹配的项目。",
            "noLibraries": "没有找到匹配的媒体库。",
            "folderIncluded": "已包含整个媒体库",
            "openFolder": "打开媒体库",
            "selectedShow": "已选中剧集",
            "selectedMovie": "已选中电影",
            "selectItem": "点击选择",
            "loadingContents": "正在加载媒体库内容…",
            "contentsFailed": "暂时无法加载媒体库内容。",
            "librariesFailed": "暂时无法加载媒体库。",
            "clearing": "正在清除…",
            "clearFailed": "未能清除未命中缓存。",
            "invalidInternalId": "请输入有效的媒体内部 ID。",
            "segmentsFailed": "未能读取片段。",
            "librariesSummary": "媒体库：{names}",
            "manyLibraries": "已选 {count} 个媒体库：{names}…",
            "manyItems": "已选 {count} 个项目：{names}…",
            "showingContents": "正在显示「{library}」中的电影和剧集。",
            "emptyContents": "「{library}」中没有可选的电影或剧集。",
            "clearedEntries": "已清除 {count} 条未命中记录。运行「TheIntroDB Media Segment Scan」可重新查询媒体库。",
            "stat_total": "总提交数",
            "stat_accepted": "已通过",
            "stat_pending": "待审核",
            "stat_rejected": "未通过",
            "stat_acceptance_rate": "通过率",
            "stat_current_streak": "当前连续提交",
            "stat_best_streak": "最长连续提交",
            "stat_total_time_saved_ms": "累计节省时间",
            "hours": "{value} 小时",
            "minutes": "{value} 分",
            "seconds": "{value} 秒"
        }
    };

    return function (view) {

    function resolveLocale() {
        let hostLocale;
        try {
            const host = globalize && (typeof globalize.getCurrentLocale === "function" ? globalize : globalize.default);
            if (host && typeof host.getCurrentLocale === "function") hostLocale = host.getCurrentLocale();
        } catch (error) { /* Older hosts may not expose a locale. */ }
        const root = document.documentElement;
        return [hostLocale, root.getAttribute("data-culture"), document.body && document.body.getAttribute("data-culture"), root.lang, navigator.language]
            .find((candidate) => typeof candidate === "string" && candidate.trim()) || "";
    }

    const locale = resolveLocale().trim().toLowerCase().replace(/_/g, "-");
    const language = /^(zh|zh-cn|zh-sg|zh-hans(?:-.*)?)$/.test(locale) ? "zh-CN" : "en";

    function t(key, values) {
        return (strings[language][key] || strings.en[key] || key).replace(/\{(\w+)\}/g, (match, name) =>
            values && Object.prototype.hasOwnProperty.call(values, name) ? String(values[name]) : match
        );
    }

    view.querySelectorAll("[data-i18n]").forEach((element) => {
        element.textContent = t(element.getAttribute("data-i18n"));
    });

    var pluginUniqueId = "424b8e01-03d2-40a1-ba58-a2b9306f115d";
    var selectedShowIds = [];
    var selectedShowNames = [];
    var selectedLibraryIds = [];
    var selectedLibraryNames = [];
    var pickerSelectedShowIds = [];
    var pickerSelectedLibraryIds = [];
    var knownShowItems = {};
    var knownLibraryItems = {};
    var currentUserId = "";
    var libraryItems = [];
    var contentItemsByLibrary = {};
    var currentBrowseLibraryId = "";
    var currentBrowseItems = [];
    var apiStats = null;
    var apiValidationRequestId = 0;
    var apiValidationTimer = 0;
    var lastValidatedApiKey = "";
    var lastValidatedApiKeyResult = "";

    var page = view;
    var apiKeyInput = page.querySelector("#ApiKey");
    var apiKeyValidationStatusElement = page.querySelector("#ApiKeyValidationStatus");
    var apiStatsOverlay = page.querySelector("#ApiStatsOverlay");
    var apiStatsContentElement = page.querySelector("#ApiStatsContent");
    var selectedShowSummaryElement = page.querySelector("#SelectedShowSummary");
    var clearSelectedShowButton = page.querySelector("#ClearSelectedShow");
    var showPickerOverlay = page.querySelector("#ShowPickerOverlay");
    var showPickerTitleElement = page.querySelector("#ShowPickerTitle");
    var showPickerBackButton = page.querySelector("#BackToLibraryList");
    var showPickerWhitelistButton = page.querySelector("#ToggleLibraryWhitelist");
    var showPickerHintElement = page.querySelector("#ShowPickerHint");
    var showPickerSearchLabelElement = page.querySelector("#ShowPickerSearchLabel");
    var showPickerSearchElement = page.querySelector("#ShowPickerSearch");
    var showPickerStatusElement = page.querySelector("#ShowPickerStatus");
    var showPickerListElement = page.querySelector("#ShowPickerList");

    function normalizeId(value) {
        return (value || "").toString().replace(/-/g, "").toLowerCase();
    }

    function dedupeIds(ids) {
        var seen = {};
        return (ids || []).filter((id) => {
            var normalizedId = normalizeId(id);
            if (!normalizedId || seen[normalizedId]) {
                return false;
            }

            seen[normalizedId] = true;
            return true;
        });
    }

    function isIdSelected(id, selectedIds) {
        var normalizedId = normalizeId(id);
        return (selectedIds || []).some((selectedId) => normalizeId(selectedId) === normalizedId);
    }

    function getDisplayLabel(item) {
        if (!item) {
            return t("unknownItem");
        }

        var name = item.Name || item.SortName || t("unknownItem");
        return item.ProductionYear ? name + " (" + item.ProductionYear + ")" : name;
    }

    function getLibraryDisplayLabel(item) {
        return item && item.Name ? item.Name : t("library");
    }

    function rememberLibraryItems(items) {
        (items || []).forEach((item) => {
            if (!item || !item.Id) {
                return;
            }

            knownLibraryItems[normalizeId(item.Id)] = item;
        });
    }

    function rememberShowItems(items) {
        (items || []).forEach((item) => {
            if (!item || !item.Id) {
                return;
            }

            if (item.Type !== "Series" && item.Type !== "Movie") {
                return;
            }

            knownShowItems[normalizeId(item.Id)] = item;
        });
    }

    function ensureLibraryItemsForIds(ids) {
        return dedupeIds(ids).map((id) => {
            var normalizedId = normalizeId(id);
            var item = knownLibraryItems[normalizedId];

            if (!item) {
                item = {
                    Id: id,
                    Name: t("previousLibrary"),
                    Type: "Library",
                };
                knownLibraryItems[normalizedId] = item;
            }

            return item;
        });
    }

    function ensureShowItemsForIds(ids) {
        return dedupeIds(ids).map((id) => {
            var normalizedId = normalizeId(id);
            var item = knownShowItems[normalizedId];

            if (!item) {
                item = {
                    Id: id,
                    Name: t("previousItem"),
                    Type: "Series",
                    ProductionYear: null,
                };
                knownShowItems[normalizedId] = item;
            }

            return item;
        });
    }

    function setSelectedLibraries(items) {
        var validItems = (items || []).filter((item) => item && item.Id);

        selectedLibraryIds = dedupeIds(validItems.map((item) => item.Id));
        selectedLibraryNames = validItems.map((item) => getLibraryDisplayLabel(item));
        updateSelectedSummary();
    }

    function setSelectedShows(items) {
        var validItems = (items || []).filter((item) => item && item.Id);

        selectedShowIds = dedupeIds(validItems.map((item) => item.Id));
        selectedShowNames = validItems.map((item) => getDisplayLabel(item));
        updateSelectedSummary();
    }

    function getSelectionSummaryText() {
        var summaryParts = [];

        if (selectedLibraryIds.length) {
            if (selectedLibraryNames.length <= 2) {
                summaryParts.push(t("librariesSummary", { names: selectedLibraryNames.join(", ") }));
            } else {
                summaryParts.push(t("manyLibraries", { count: selectedLibraryIds.length, names: selectedLibraryNames.slice(0, 2).join(", ") }));
            }
        }

        if (selectedShowIds.length) {
            if (selectedShowNames.length <= 2) {
                summaryParts.push(selectedShowNames.join(", "));
            } else {
                summaryParts.push(t("manyItems", { count: selectedShowIds.length, names: selectedShowNames.slice(0, 2).join(", ") }));
            }
        }

        return summaryParts.length ? summaryParts.join(" | ") : t("allMedia");
    }

    function updateSelectedSummary() {
        selectedShowSummaryElement.textContent = getSelectionSummaryText();
        clearSelectedShowButton.disabled = selectedLibraryIds.length === 0 && selectedShowIds.length === 0;
    }

    function getNormalizedLibraryItem(item) {
        if (!item) {
            return null;
        }

        var id = item.Id || item.id || "";
        if (!id) {
            return null;
        }

        return {
            Id: id,
            Name: item.Name || item.name || t("library"),
            SortName: item.SortName || item.sortName || item.Name || item.name || "",
            CollectionType: (item.CollectionType || item.collectionType || "").toLowerCase(),
            Type: "Library",
        };
    }

    function getNormalizedContentItem(item) {
        if (!item) {
            return null;
        }

        var id = item.Id || item.id || "";
        if (!id) {
            return null;
        }

        var type = item.Type || item.type || "";
        if (type !== "Series" && type !== "Movie") {
            return null;
        }

        return {
            Id: id,
            Name: item.Name || item.name || "",
            SortName: item.SortName || item.sortName || item.Name || item.name || "",
            ProductionYear: item.ProductionYear || item.productionYear || null,
            Type: type,
        };
    }

    function libraryCanContainSelectableMedia(item) {
        if (!item) {
            return false;
        }

        return !item.CollectionType || item.CollectionType === "tvshows" || item.CollectionType === "movies" || item.CollectionType === "mixed";
    }

    function getConfiguredIdList(rawValue) {
        var configuredIds = [];

        if (Array.isArray(rawValue)) {
            configuredIds = configuredIds.concat(rawValue);
        } else if (typeof rawValue === "string" && rawValue) {
            configuredIds = configuredIds.concat(
                rawValue
                    .split(",")
                    .map((id) => id.trim())
                    .filter((id) => !!id),
            );
        } else if (rawValue) {
            configuredIds.push(rawValue);
        }

        return dedupeIds(configuredIds);
    }

    function formatStatValue(label, value) {
        if (label === "acceptance_rate" && typeof value === "number") {
            return value + "%";
        }

        if (label === "total_time_saved_ms" && typeof value === "number") {
            var totalSeconds = Math.floor(value / 1000);
            var hours = Math.floor(totalSeconds / 3600);
            var minutes = Math.floor((totalSeconds % 3600) / 60);
            var seconds = totalSeconds % 60;
            var timeParts = [];

            if (hours) {
                timeParts.push(t("hours", { value: hours }));
            }

            if (minutes || hours) {
                timeParts.push(t("minutes", { value: minutes }));
            }

            timeParts.push(t("seconds", { value: seconds }));
            return timeParts.join(" ");
        }

        return value;
    }

    function getStatLabelText(label) {
        return t("stat_" + label);
    }

    function renderApiValidationStatus(type, message, showInfoButton) {
        apiKeyValidationStatusElement.innerHTML = "";

        if (!message) {
            apiKeyValidationStatusElement.style.display = "none";
            return;
        }

        apiKeyValidationStatusElement.style.display = "flex";

        var badge = document.createElement("span");
        badge.textContent = type === "success" ? "\u2713" : type === "error" ? "\u2715" : "\u2026";
        badge.style.display = "inline-flex";
        badge.style.alignItems = "center";
        badge.style.justifyContent = "center";
        badge.style.width = "1.5em";
        badge.style.height = "1.5em";
        badge.style.borderRadius = "999px";
        badge.style.fontWeight = "700";
        badge.style.flexShrink = "0";
        badge.style.color = "#fff";
        badge.style.background = type === "success" ? "#2f9e44" : type === "error" ? "#c92a2a" : "#495057";

        var text = document.createElement("span");
        text.textContent = message;

        apiKeyValidationStatusElement.appendChild(badge);
        apiKeyValidationStatusElement.appendChild(text);

        if (showInfoButton) {
            var infoButton = document.createElement("button");
            infoButton.type = "button";
            infoButton.className = "emby-button";
            infoButton.textContent = "i";
            infoButton.title = t("apiStatsButton");
            infoButton.style.display = "inline-flex";
            infoButton.style.alignItems = "center";
            infoButton.style.justifyContent = "center";
            infoButton.style.width = "1.9em";
            infoButton.style.height = "1.9em";
            infoButton.style.padding = "0";
            infoButton.style.margin = "0";
            infoButton.style.borderRadius = "999px";
            infoButton.style.fontWeight = "700";
            infoButton.style.minWidth = "0";
            infoButton.style.background = "rgba(117, 117, 117, 0.08)";
            infoButton.style.border = "1px solid rgba(117, 117, 117, 0.16)";
            infoButton.style.color = "inherit";
            infoButton.addEventListener("click", () => {
                openApiStatsOverlay();
            });
            apiKeyValidationStatusElement.appendChild(infoButton);
        }
    }

    function renderApiStatsContent(stats) {
        apiStatsContentElement.innerHTML = "";

        if (!stats) {
            apiStatsContentElement.textContent = t("noStats");
            return;
        }

        var statsGrid = document.createElement("div");
        statsGrid.style.display = "grid";
        statsGrid.style.gridTemplateColumns = "repeat(auto-fit, minmax(180px, 1fr))";
        statsGrid.style.gap = "0.75em";

        ["total", "accepted", "pending", "rejected", "acceptance_rate", "current_streak", "best_streak", "total_time_saved_ms"].forEach((key) => {
            var pascalKey = key.replace(/(^|_)([a-z])/g, (_match, _prefix, character) => character.toUpperCase());
            var statValue = stats[key] != null ? stats[key] : stats[pascalKey];
            var card = document.createElement("div");
            card.style.padding = "0.85em";
            card.style.border = "1px solid rgba(117, 117, 117, 0.12)";
            card.style.borderRadius = "0.5em";
            card.style.background = "rgba(117, 117, 117, 0.03)";

            var label = document.createElement("div");
            label.textContent = getStatLabelText(key);
            label.style.fontSize = "0.85em";
            label.style.opacity = "0.75";

            var value = document.createElement("div");
            value.textContent = formatStatValue(key, statValue != null ? statValue : 0);
            value.style.marginTop = "0.35em";
            value.style.fontSize = "1.1em";
            value.style.fontWeight = "600";

            card.appendChild(label);
            card.appendChild(value);
            statsGrid.appendChild(card);
        });

        apiStatsContentElement.appendChild(statsGrid);
    }

    function openApiStatsOverlay() {
        if (!apiStats) {
            return;
        }

        renderApiStatsContent(apiStats);
        apiStatsOverlay.style.display = "flex";
    }

    function closeApiStatsOverlay() {
        apiStatsOverlay.style.display = "none";
    }

    function resetApiValidationState() {
        apiStats = null;
        lastValidatedApiKey = "";
        lastValidatedApiKeyResult = "";
        renderApiValidationStatus("", "", false);
        closeApiStatsOverlay();
    }

    function validateApiKey(apiKey, forceRefresh) {
        var trimmedApiKey = (apiKey || "").trim();

        if (!trimmedApiKey) {
            resetApiValidationState();
            return Promise.resolve();
        }

        if (!forceRefresh && trimmedApiKey === lastValidatedApiKey) {
            renderApiValidationStatus(lastValidatedApiKeyResult, lastValidatedApiKeyResult === "success" ? t("apiVerified") : t("invalidToken"), lastValidatedApiKeyResult === "success" && !!apiStats);
            return Promise.resolve();
        }

        apiValidationRequestId += 1;
        var requestId = apiValidationRequestId;
        renderApiValidationStatus("loading", t("checkingKey"), false);

        return postJson(ApiClient.getUrl("TheIntroDB/Validation/ApiKeyStats"), {
            apiKey: trimmedApiKey,
        })
            .then((data) => {
                var isValid = !!(data && (data.isValid || data.IsValid));
                var stats = data ? data.stats || data.Stats : null;
                var validationError = data ? data.error || data.Error : null;

                if (requestId !== apiValidationRequestId) {
                    return;
                }

                lastValidatedApiKey = trimmedApiKey;

                if (isValid && stats) {
                    apiStats = stats;
                    lastValidatedApiKeyResult = "success";
                    renderApiValidationStatus("success", t("apiVerified"), true);
                    return;
                }

                apiStats = null;
                lastValidatedApiKeyResult = "error";
                renderApiValidationStatus("error", validationError || t("invalidToken"), false);
            })
            .catch((error) => {
                if (requestId !== apiValidationRequestId) {
                    return;
                }

                var responseData = error && error.response ? error.response.responseJSON : null;
                var statusCode = error && error.response ? error.response.status : 0;
                var responseError = responseData ? responseData.error || responseData.Error : null;

                if (statusCode === 401) {
                    apiStats = null;
                    lastValidatedApiKey = trimmedApiKey;
                    lastValidatedApiKeyResult = "error";
                    renderApiValidationStatus("error", responseError || t("invalidToken"), false);
                    return;
                }

                apiStats = null;
                lastValidatedApiKey = "";
                lastValidatedApiKeyResult = "";
                renderApiValidationStatus("error", t("validationUnavailable"), false);
            });
    }

    function scheduleApiKeyValidation() {
        if (apiValidationTimer) {
            clearTimeout(apiValidationTimer);
        }

        apiValidationTimer = setTimeout(() => {
            apiValidationTimer = 0;
            validateApiKey(apiKeyInput.value, false);
        }, 650);
    }

    function isValidGuid(value) {
        if (!value) return false;
        var normalized = normalizeId(value);
        return normalized.length === 32 && /^[0-9a-f]{32}$/i.test(normalized);
    }

    function getSelectedShowIdsFromConfig(config) {
        return dedupeIds(getConfiguredIdList(config.SelectedShowIds));
    }

    function getSelectedLibraryIdsFromConfig(config) {
        return getConfiguredIdList(config.SelectedLibraryIds);
    }

    function getImageUrl(item) {
        if (!item || !item.Id) {
            return "";
        }

        if (typeof ApiClient.getScaledImageUrl === "function") {
            return ApiClient.getScaledImageUrl(item.Id, {
                type: "Primary",
                width: 320,
            });
        }

        return ApiClient.getUrl("Items/" + item.Id + "/Images/Primary", {
            maxWidth: 320,
        });
    }

    function getFallbackImageUrl(item) {
        if (!item || item.Type !== "Library") {
            return "";
        }

        // TODO: Make an emby version of this image
        return "https://raw.githubusercontent.com/TheIntroDB/jellyfin-plugin/main/assets/folder.png";
    }

    function createCardElement(item, titleText, stateText, isSelected, isInteractive) {
        var element = document.createElement(isInteractive ? "button" : "div");
        if (isInteractive) {
            element.type = "button";
            element.className = "emby-button";
        }

        element.style.display = "flex";
        element.style.flexDirection = "column";
        element.style.alignItems = "center";
        element.style.justifyContent = "flex-start";
        element.style.width = "160px";
        element.style.margin = "0";
        element.style.padding = "0.75em";
        element.style.textAlign = "center";
        element.style.border = isSelected ? "2px solid rgba(117, 117, 117, 0.85)" : "1px solid hsla(var(--theme-text-color-hue, 0), var(--theme-text-color-saturation, 0%), var(--theme-text-color-lightness, 0%), 0.12)";
        element.style.borderRadius = "0.5em";
        element.style.background = isSelected ? "rgba(117, 117, 117, 0.12)" : "hsla(var(--card-background-hue, 0), var(--card-background-saturation, 0%), var(--card-background-lightness, 100%), var(--card-background-alpha, 1))";
        element.style.cursor = isInteractive ? "pointer" : "default";

        var image = document.createElement("img");
        var imageUrl = getImageUrl(item);
        var fallbackImageUrl = getFallbackImageUrl(item);
        image.src = imageUrl || fallbackImageUrl;
        image.alt = titleText;
        image.style.display = "block";
        image.style.width = "100%";
        image.style.aspectRatio = "2 / 3";
        image.style.objectFit = "cover";
        image.style.borderRadius = "0.35em";
        image.style.background = "rgba(117, 117, 117, 0.08)";
        image.addEventListener("error", () => {
            if (fallbackImageUrl && image.src !== fallbackImageUrl) {
                image.src = fallbackImageUrl;
                return;
            }

            image.style.display = "none";
        });

        var title = document.createElement("div");
        title.textContent = titleText;
        title.style.marginTop = "0.75em";
        title.style.fontSize = "0.95em";
        title.style.lineHeight = "1.35";
        title.style.whiteSpace = "normal";

        var state = document.createElement("div");
        state.textContent = stateText;
        state.style.marginTop = "0.4em";
        state.style.fontSize = "0.8em";
        state.style.opacity = isSelected ? "1" : "0.7";

        element.appendChild(image);
        element.appendChild(title);
        element.appendChild(state);
        return element;
    }

    function getLibraryById(libraryId) {
        return knownLibraryItems[normalizeId(libraryId)] || null;
    }

    function renderPickerHeader() {
        var browsingLibrary = getLibraryById(currentBrowseLibraryId);
        var isBrowsingLibrary = !!browsingLibrary;

        showPickerTitleElement.textContent = isBrowsingLibrary ? getLibraryDisplayLabel(browsingLibrary) : t("chooseLibraries");
        showPickerBackButton.style.display = isBrowsingLibrary ? "inline-flex" : "none";
        showPickerWhitelistButton.style.display = isBrowsingLibrary ? "inline-flex" : "none";
        showPickerWhitelistButton.textContent = isBrowsingLibrary && isIdSelected(currentBrowseLibraryId, pickerSelectedLibraryIds) ? t("excludeFolder") : t("includeFolder");
        showPickerSearchLabelElement.textContent = isBrowsingLibrary ? t("searchThisLibrary") : t("searchLibraries");
        showPickerHintElement.textContent = isBrowsingLibrary ? t("browseLibraryHelp") : t("chooseLibraryHelp");
    }

    function getPickerStatusText() {
        if (!currentBrowseLibraryId) {
            return libraryItems.length ? t("browseLibrary") : t("noSupportedLibraries");
        }

        var libraryItem = getLibraryById(currentBrowseLibraryId);
        var libraryName = libraryItem ? getLibraryDisplayLabel(libraryItem) : t("thisLibrary");
        return currentBrowseItems.length ? t("showingContents", { library: libraryName }) : t("emptyContents", { library: libraryName });
    }

    function renderPickerList(filterText) {
        var normalizedFilter = (filterText || "").trim().toLowerCase();
        var matchingItems;

        if (!currentBrowseLibraryId) {
            matchingItems = libraryItems.filter((item) => !normalizedFilter || getLibraryDisplayLabel(item).toLowerCase().indexOf(normalizedFilter) !== -1);
        } else {
            matchingItems = currentBrowseItems.filter((item) => !normalizedFilter || getDisplayLabel(item).toLowerCase().indexOf(normalizedFilter) !== -1);
        }

        showPickerListElement.innerHTML = "";
        showPickerListElement.style.display = "block";

        if (!matchingItems.length) {
            showPickerListElement.textContent = currentBrowseLibraryId ? t("noItems") : t("noLibraries");
            return;
        }

        showPickerListElement.style.display = "flex";
        showPickerListElement.style.flexWrap = "wrap";
        showPickerListElement.style.gap = "1em";
        showPickerListElement.style.alignItems = "flex-start";

        if (!currentBrowseLibraryId) {
            matchingItems.forEach((item) => {
                var card = createCardElement(item, getLibraryDisplayLabel(item), isIdSelected(item.Id, pickerSelectedLibraryIds) ? t("folderIncluded") : t("openFolder"), isIdSelected(item.Id, pickerSelectedLibraryIds), true);

                card.addEventListener("click", () => {
                    openLibrary(item.Id);
                });

                showPickerListElement.appendChild(card);
            });

            return;
        }

        matchingItems.forEach((item) => {
            var itemSelected = isIdSelected(item.Id, pickerSelectedShowIds);
            var libraryWhitelisted = isIdSelected(currentBrowseLibraryId, pickerSelectedLibraryIds);
            var stateText = itemSelected ? (item.Type === "Series" ? t("selectedShow") : t("selectedMovie")) : t("selectItem");

            var card = createCardElement(item, getDisplayLabel(item), stateText, libraryWhitelisted || itemSelected, true);

            card.addEventListener("click", () => {
                if (itemSelected) {
                    pickerSelectedShowIds = pickerSelectedShowIds.filter((selectedId) => normalizeId(selectedId) !== normalizeId(item.Id));
                } else {
                    pickerSelectedShowIds = dedupeIds(pickerSelectedShowIds.concat([item.Id]));
                }

                renderPickerList(showPickerSearchElement.value || "");
            });

            showPickerListElement.appendChild(card);
        });
    }

    function applyPickerSelection() {
        setSelectedLibraries(ensureLibraryItemsForIds(pickerSelectedLibraryIds));
        setSelectedShows(ensureShowItemsForIds(pickerSelectedShowIds));
        closeShowPicker();
    }

    function getCurrentUserId() {
        if (typeof ApiClient.getCurrentUserId === "function") {
            return Promise.resolve(ApiClient.getCurrentUserId());
        }

        if (typeof ApiClient.getCurrentUser === "function") {
            return ApiClient.getCurrentUser().then((user) => (user && user.Id ? user.Id : ""));
        }

        return Promise.resolve("");
    }

    function getJson(url) {
        if (typeof ApiClient.getJSON === "function") {
            return ApiClient.getJSON(url);
        }

        if (typeof ApiClient.ajax === "function") {
            return ApiClient.ajax({
                type: "GET",
                url: url,
                dataType: "json",
            });
        }

        throw new Error("No authenticated ApiClient JSON method is available");
    }

    function postJson(url, payload) {
        if (typeof ApiClient.ajax === "function") {
            return ApiClient.ajax({
                type: "POST",
                url: url,
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: JSON.stringify(payload || {}),
            });
        }

        return fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json",
            },
            credentials: "same-origin",
            body: JSON.stringify(payload || {}),
        }).then((response) =>
            response.text().then((text) => {
                var data = {};

                try {
                    data = text ? JSON.parse(text) : {};
                } catch (error) {
                    data = {};
                }

                if (!response.ok) {
                    throw {
                        response: {
                            status: response.status,
                            responseJSON: data,
                        },
                    };
                }

                return data;
            }),
        );
    }

    function loadLibraries() {
        if (!currentUserId) {
            return Promise.reject(new Error("Missing current user id"));
        }

        if (libraryItems.length) {
            return Promise.resolve(libraryItems);
        }

        return getJson(ApiClient.getUrl("Users/" + currentUserId + "/Views")).then((result) => {
            var rawItems = (result && (result.Items || result.items)) || [];
            libraryItems = rawItems
                .map(getNormalizedLibraryItem)
                .filter((item) => !!item && libraryCanContainSelectableMedia(item))
                .sort((left, right) => getLibraryDisplayLabel(left).localeCompare(getLibraryDisplayLabel(right)));

            rememberLibraryItems(libraryItems);
            return libraryItems;
        });
    }

    function loadAllShowReferences(selectedIds) {
        if (!currentUserId || !selectedIds.length) {
            return Promise.resolve();
        }

        return getJson(
            ApiClient.getUrl("Users/" + currentUserId + "/Items", {
                Recursive: true,
                IncludeItemTypes: "Series,Movie",
                SortBy: "SortName",
                SortOrder: "Ascending",
                Fields: "ProductionYear",
                EnableTotalRecordCount: false,
                Limit: 10000,
            }),
        )
            .then((result) => {
                var rawItems = (result && (result.Items || result.items)) || [];
                rememberShowItems(rawItems.map(getNormalizedContentItem).filter((item) => !!item));
            })
            .catch(() => {
                // Keep fallback labels when item references cannot be loaded.
            });
    }

    function loadLibraryContents(libraryId) {
        var normalizedLibraryId = normalizeId(libraryId);
        if (!normalizedLibraryId) {
            currentBrowseItems = [];
            return Promise.resolve(currentBrowseItems);
        }

        if (contentItemsByLibrary[normalizedLibraryId]) {
            currentBrowseItems = contentItemsByLibrary[normalizedLibraryId];
            return Promise.resolve(currentBrowseItems);
        }

        return getJson(
            ApiClient.getUrl("Users/" + currentUserId + "/Items", {
                ParentId: libraryId,
                Recursive: true,
                IncludeItemTypes: "Series,Movie",
                SortBy: "SortName",
                SortOrder: "Ascending",
                Fields: "ProductionYear",
                EnableTotalRecordCount: false,
                Limit: 10000,
            }),
        ).then((result) => {
            var rawItems = (result && (result.Items || result.items)) || [];
            var items = rawItems.map(getNormalizedContentItem).filter((item) => !!item);

            rememberShowItems(items);
            contentItemsByLibrary[normalizedLibraryId] = items;
            currentBrowseItems = items;
            return currentBrowseItems;
        });
    }

    function showLibraryRoot() {
        currentBrowseLibraryId = "";
        currentBrowseItems = [];
        renderPickerHeader();
        showPickerStatusElement.textContent = getPickerStatusText();
        renderPickerList(showPickerSearchElement.value || "");
    }

    function openLibrary(libraryId) {
        currentBrowseLibraryId = libraryId || "";
        currentBrowseItems = [];
        renderPickerHeader();
        showPickerStatusElement.textContent = t("loadingContents");

        loadLibraryContents(currentBrowseLibraryId)
            .then(() => {
                renderPickerHeader();
                showPickerStatusElement.textContent = getPickerStatusText();
                renderPickerList(showPickerSearchElement.value || "");
            })
            .catch(() => {
                showPickerStatusElement.textContent = t("contentsFailed");
            });
    }

    function openShowPicker() {
        showPickerOverlay.style.display = "flex";
        showPickerSearchElement.value = "";
        pickerSelectedLibraryIds = selectedLibraryIds.slice();
        pickerSelectedShowIds = selectedShowIds.slice();
        renderPickerHeader();
        showPickerStatusElement.textContent = t("loadingLibraries");

        loadLibraries()
            .then(() => {
                showLibraryRoot();
                showPickerSearchElement.focus();
            })
            .catch(() => {
                showPickerStatusElement.textContent = t("librariesFailed");
                showPickerSearchElement.focus();
            });
    }

    function closeShowPicker() {
        showPickerOverlay.style.display = "none";
        showLibraryRoot();
    }

    function loadConfigPage() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginUniqueId)
            .then((config) => {
                var configuredShowIds = getSelectedShowIdsFromConfig(config);
                var configuredLibraryIds = getSelectedLibraryIdsFromConfig(config);

                apiKeyInput.value = config.ApiKey || "";
                resetApiValidationState();
                setSelectedLibraries(ensureLibraryItemsForIds(configuredLibraryIds));
                setSelectedShows(ensureShowItemsForIds(configuredShowIds));

                return getCurrentUserId()
                    .then((userId) => {
                        currentUserId = userId || "";
                        if (!currentUserId) {
                            return;
                        }

                        return loadLibraries().then(() => {
                            setSelectedLibraries(ensureLibraryItemsForIds(configuredLibraryIds));
                            return loadAllShowReferences(configuredShowIds).then(() => {
                                setSelectedShows(ensureShowItemsForIds(configuredShowIds));
                            });
                        });
                    })
                    .then(() => {
                        page.querySelector("#EnableIntro").checked = config.EnableIntro !== false;
                        page.querySelector("#EnableRecap").checked = config.EnableRecap !== false;
                        page.querySelector("#EnableCredits").checked = config.EnableCredits !== false;
                        page.querySelector("#EnablePreview").checked = config.EnablePreview !== false;
                        page.querySelector("#EnableChapterCreation").checked = config.EnableChapterCreation !== false;
                        page.querySelector("#IgnoreMediaWithExistingSegments").checked = config.IgnoreMediaWithExistingSegments !== false;
                        page.querySelector("#RefreshAfterDays").value = config.RefreshAfterDays != null ? config.RefreshAfterDays : 30;
                        page.querySelector("#ProtectExistingIntroMarkers").checked = config.ProtectExistingIntroMarkers !== false;
                        page.querySelector("#ProtectExistingCreditsMarkers").checked = config.ProtectExistingCreditsMarkers !== false;
                        page.querySelector("#ReplaceExistingMarkers").checked = config.ReplaceExistingMarkers === true;
                        page.querySelector("#MaxLookupsPerRun").value = config.MaxLookupsPerRun != null ? config.MaxLookupsPerRun : 400;
                        page.querySelector("#EnableOnDemandFetch").checked = config.EnableOnDemandFetch !== false;
                        page.querySelector("#EnableAnonymousUsageReporting").checked = config.EnableAnonymousUsageReporting !== false;
                        page.querySelector("#EnableFileLogging").checked = config.EnableFileLogging === true;
                        page.querySelector("#MarkerRepairIntervalHours").value = config.MarkerRepairIntervalHours != null ? config.MarkerRepairIntervalHours : 12;
                        if (apiKeyInput.value) {
                            return validateApiKey(apiKeyInput.value, true);
                        }
                    });
            })
            .then(() => {
                Dashboard.hideLoadingMsg();
            })
            .catch(() => {
                Dashboard.hideLoadingMsg();
            });
    }

    view.addEventListener("pageshow", loadConfigPage);
    view.addEventListener("viewshow", loadConfigPage);
    setTimeout(loadConfigPage, 0);

    page.querySelector("#OpenShowPicker").addEventListener("click", () => {
        openShowPicker();
    });

    page.querySelector("#ClearNotFoundCache").addEventListener("click", () => {
        const statusElement = page.querySelector("#NotFoundCacheStatus");
        statusElement.textContent = t("clearing");
        postJson(ApiClient.getUrl("TheIntroDB/NotFoundCache/Clear"), {})
            .then((response) => {
                statusElement.textContent = t("clearedEntries", { count: response.ClearedEntries });
            })
            .catch(() => {
                statusElement.textContent = t("clearFailed");
            });
    });

    page.querySelector("#CloseShowPicker").addEventListener("click", () => {
        closeShowPicker();
    });

    page.querySelector("#DoneShowPicker").addEventListener("click", () => {
        applyPickerSelection();
    });

    showPickerBackButton.addEventListener("click", () => {
        showLibraryRoot();
    });

    showPickerWhitelistButton.addEventListener("click", () => {
        if (!currentBrowseLibraryId) {
            return;
        }

        if (isIdSelected(currentBrowseLibraryId, pickerSelectedLibraryIds)) {
            pickerSelectedLibraryIds = pickerSelectedLibraryIds.filter((selectedId) => normalizeId(selectedId) !== normalizeId(currentBrowseLibraryId));
        } else {
            pickerSelectedLibraryIds = dedupeIds(pickerSelectedLibraryIds.concat([currentBrowseLibraryId]));
        }

        renderPickerHeader();
        showPickerStatusElement.textContent = getPickerStatusText();
        renderPickerList(showPickerSearchElement.value || "");
    });

    clearSelectedShowButton.addEventListener("click", () => {
        selectedLibraryIds = [];
        selectedLibraryNames = [];
        selectedShowIds = [];
        selectedShowNames = [];
        pickerSelectedLibraryIds = [];
        pickerSelectedShowIds = [];
        updateSelectedSummary();
    });

    showPickerOverlay.addEventListener("click", (event) => {
        if (event.target === showPickerOverlay) {
            closeShowPicker();
        }
    });

    apiStatsOverlay.addEventListener("click", (event) => {
        if (event.target === apiStatsOverlay) {
            closeApiStatsOverlay();
        }
    });

    page.querySelector("#CloseApiStats").addEventListener("click", () => {
        closeApiStatsOverlay();
    });

    showPickerSearchElement.addEventListener("input", () => {
        renderPickerList(showPickerSearchElement.value || "");
    });

    apiKeyInput.addEventListener("input", () => {
        if (!apiKeyInput.value.trim()) {
            resetApiValidationState();
            return;
        }

        apiStats = null;
        lastValidatedApiKey = "";
        lastValidatedApiKeyResult = "";
        renderApiValidationStatus("loading", t("checkingKey"), false);
        scheduleApiKeyValidation();
    });

    apiKeyInput.addEventListener("blur", () => {
        if (apiValidationTimer) {
            clearTimeout(apiValidationTimer);
            apiValidationTimer = 0;
        }

        validateApiKey(apiKeyInput.value, false);
    });

    page.querySelector("#TheIntroDbConfigForm").addEventListener("submit", (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginUniqueId)
            .then((config) => {
                config.ApiKey = apiKeyInput.value || "";
                config.SelectedLibraryIds = selectedLibraryIds.join(",");
                config.SelectedShowIds = selectedShowIds.join(",");
                config.EnableIntro = page.querySelector("#EnableIntro").checked;
                config.EnableRecap = page.querySelector("#EnableRecap").checked;
                config.EnableCredits = page.querySelector("#EnableCredits").checked;
                config.EnablePreview = page.querySelector("#EnablePreview").checked;
                config.EnableChapterCreation = page.querySelector("#EnableChapterCreation").checked;
                config.IgnoreMediaWithExistingSegments = page.querySelector("#IgnoreMediaWithExistingSegments").checked;
                var refreshAfterDays = parseInt(page.querySelector("#RefreshAfterDays").value, 10);
                config.RefreshAfterDays = isNaN(refreshAfterDays) || refreshAfterDays < 0 ? 30 : refreshAfterDays;
                config.ProtectExistingIntroMarkers = page.querySelector("#ProtectExistingIntroMarkers").checked;
                config.ProtectExistingCreditsMarkers = page.querySelector("#ProtectExistingCreditsMarkers").checked;
                config.ReplaceExistingMarkers = page.querySelector("#ReplaceExistingMarkers").checked;
                config.MaxLookupsPerRun = Math.max(0, parseInt(page.querySelector("#MaxLookupsPerRun").value || "0", 10) || 0);
                config.EnableOnDemandFetch = page.querySelector("#EnableOnDemandFetch").checked;
                config.EnableAnonymousUsageReporting = page.querySelector("#EnableAnonymousUsageReporting").checked;
                config.EnableFileLogging = page.querySelector("#EnableFileLogging").checked;
                config.MarkerRepairIntervalHours = parseInt(page.querySelector("#MarkerRepairIntervalHours").value || "0", 10) || 0;
                return ApiClient.updatePluginConfiguration(pluginUniqueId, config).then((result) => {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            })
            .then(() => {
                Dashboard.hideLoadingMsg();
            })
            .catch(() => {
                Dashboard.hideLoadingMsg();
            });
        return false;
    });

    var loadSegmentsButton = page.querySelector(".btnLoadSegments");
    if (loadSegmentsButton) {
        loadSegmentsButton.addEventListener("click", (event) => {
            event.preventDefault();
            var output = page.querySelector(".segmentOutput");
            if (!output) {
                return;
            }

            output.textContent = "";
            var internalId = parseInt((page.querySelector("#DebugInternalId").value || "").trim(), 10);
            if (!internalId) {
                output.textContent = t("invalidInternalId");
                return;
            }

            Dashboard.showLoadingMsg();
            getJson(
                ApiClient.getUrl("TheIntroDB/Segments", {
                    InternalId: internalId,
                }),
            )
                .then((result) => {
                    output.textContent = JSON.stringify(result, null, 2);
                })
                .catch((error) => {
                    output.textContent = error && error.message ? error.message : t("segmentsFailed");
                })
                .then(() => {
                    Dashboard.hideLoadingMsg();
                })
                .catch(() => {
                    Dashboard.hideLoadingMsg();
                });
        });
    }

    updateSelectedSummary();
    showLibraryRoot();
    };
});
