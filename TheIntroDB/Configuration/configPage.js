define(["emby-input", "emby-button", "emby-checkbox"], () => (view) => {
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
            return "Unknown item";
        }

        var name = item.Name || item.SortName || "Unknown item";
        return item.ProductionYear ? name + " (" + item.ProductionYear + ")" : name;
    }

    function getLibraryDisplayLabel(item) {
        return item && item.Name ? item.Name : "Library";
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
                    Name: "Previously selected library",
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
                    Name: "Previously selected item",
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
                summaryParts.push("Libraries: " + selectedLibraryNames.join(", "));
            } else {
                summaryParts.push(selectedLibraryIds.length + " libraries selected: " + selectedLibraryNames.slice(0, 2).join(", ") + ", ...");
            }
        }

        if (selectedShowIds.length) {
            if (selectedShowNames.length <= 2) {
                summaryParts.push(selectedShowNames.join(", "));
            } else {
                summaryParts.push(selectedShowIds.length + " items selected: " + selectedShowNames.slice(0, 2).join(", ") + ", ...");
            }
        }

        return summaryParts.length ? summaryParts.join(" | ") : "All media";
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
            Name: item.Name || item.name || "Library",
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
                timeParts.push(hours + "h");
            }

            if (minutes || hours) {
                timeParts.push(minutes + "m");
            }

            timeParts.push(seconds + "s");
            return timeParts.join(" ");
        }

        return value;
    }

    function getStatLabelText(label) {
        return label.replace(/_/g, " ").replace(/\b\w/g, (character) => character.toUpperCase());
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
            infoButton.title = "Show API key stats";
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
            apiStatsContentElement.textContent = "No stats available.";
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
            renderApiValidationStatus(lastValidatedApiKeyResult, lastValidatedApiKeyResult === "success" ? "API key verified." : "Invalid or expired token.", lastValidatedApiKeyResult === "success" && !!apiStats);
            return Promise.resolve();
        }

        apiValidationRequestId += 1;
        var requestId = apiValidationRequestId;
        renderApiValidationStatus("loading", "Checking API key...", false);

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
                    renderApiValidationStatus("success", "API key verified.", true);
                    return;
                }

                apiStats = null;
                lastValidatedApiKeyResult = "error";
                renderApiValidationStatus("error", validationError || "Invalid or expired token.", false);
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
                    renderApiValidationStatus("error", responseError || "Invalid or expired token.", false);
                    return;
                }

                apiStats = null;
                lastValidatedApiKey = "";
                lastValidatedApiKeyResult = "";
                renderApiValidationStatus("error", "Could not validate API key right now.", false);
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

        showPickerTitleElement.textContent = isBrowsingLibrary ? getLibraryDisplayLabel(browsingLibrary) : "Choose Libraries";
        showPickerBackButton.style.display = isBrowsingLibrary ? "inline-flex" : "none";
        showPickerWhitelistButton.style.display = isBrowsingLibrary ? "inline-flex" : "none";
        showPickerWhitelistButton.textContent = isBrowsingLibrary && isIdSelected(currentBrowseLibraryId, pickerSelectedLibraryIds) ? "Remove whole folder" : "Whitelist whole folder";
        showPickerSearchLabelElement.textContent = isBrowsingLibrary ? "Search this library" : "Search libraries";
        showPickerHintElement.textContent = isBrowsingLibrary ? "Movies and series can be selected individually, or you can whitelist the whole folder to include everything inside it." : "Open a library to browse its movies and shows. Inside a library, you can whitelist the whole folder or pick individual movies and shows.";
    }

    function getPickerStatusText() {
        if (!currentBrowseLibraryId) {
            return libraryItems.length ? "Choose a library to browse its contents." : "No supported libraries were returned for this user.";
        }

        var libraryItem = getLibraryById(currentBrowseLibraryId);
        var libraryName = libraryItem ? getLibraryDisplayLabel(libraryItem) : "this library";
        return currentBrowseItems.length ? "Showing movies and series from " + libraryName + "." : "No movies or series were returned from " + libraryName + ".";
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
            showPickerListElement.textContent = currentBrowseLibraryId ? "No items found." : "No libraries found.";
            return;
        }

        showPickerListElement.style.display = "flex";
        showPickerListElement.style.flexWrap = "wrap";
        showPickerListElement.style.gap = "1em";
        showPickerListElement.style.alignItems = "flex-start";

        if (!currentBrowseLibraryId) {
            matchingItems.forEach((item) => {
                var card = createCardElement(item, getLibraryDisplayLabel(item), isIdSelected(item.Id, pickerSelectedLibraryIds) ? "Folder whitelisted" : "Open folder", isIdSelected(item.Id, pickerSelectedLibraryIds), true);

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
            var stateText = itemSelected ? (item.Type === "Series" ? "Selected show" : "Selected movie") : "Click to select";

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
        showPickerStatusElement.textContent = "Loading library contents...";

        loadLibraryContents(currentBrowseLibraryId)
            .then(() => {
                renderPickerHeader();
                showPickerStatusElement.textContent = getPickerStatusText();
                renderPickerList(showPickerSearchElement.value || "");
            })
            .catch(() => {
                showPickerStatusElement.textContent = "Could not load library contents right now.";
            });
    }

    function openShowPicker() {
        showPickerOverlay.style.display = "flex";
        showPickerSearchElement.value = "";
        pickerSelectedLibraryIds = selectedLibraryIds.slice();
        pickerSelectedShowIds = selectedShowIds.slice();
        renderPickerHeader();
        showPickerStatusElement.textContent = "Loading libraries...";

        loadLibraries()
            .then(() => {
                showLibraryRoot();
                showPickerSearchElement.focus();
            })
            .catch(() => {
                showPickerStatusElement.textContent = "Could not load libraries right now.";
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
                        page.querySelector("#IgnoreMediaWithExistingSegments").checked = config.IgnoreMediaWithExistingSegments !== false;
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
        renderApiValidationStatus("loading", "Checking API key...", false);
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
                config.IgnoreMediaWithExistingSegments = page.querySelector("#IgnoreMediaWithExistingSegments").checked;
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
                output.textContent = "Enter a valid InternalId.";
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
                    output.textContent = error && error.message ? error.message : "Failed to load segments.";
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
});
