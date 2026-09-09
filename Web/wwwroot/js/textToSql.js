"use strict";
const TEXT_TO_SQL_AUTH_STORAGE_KEY = "textToSql.loginUser";
/**
 * 以簡易提示顯示訊息。
 * @param {string} message 顯示文字
 */
let selectedHistoryFileName = null;
let currentQueryMessageElement = null;
let queryMessageCounter = 0;
let resultMessageCounter = 0;
let insuranceChatSessionId = null;
let conversationHistoryPanelInitialized = false;
let todoListPanelInitialized = false;
let currentTodoInfoItems = [];
let currentTodoDocumentItems = [];
let currentUploadedDocumentTitles = [];
let currentUploadedHistoryFiles = [];
const PROMPT_TEMPLATE_MODE_SALES_ASSISTANT = "salesAssistant";
const PROMPT_TEMPLATE_MODE_CUSTOMER_SERVICE = "customerService";
const PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT = "claimAssistant";
function simpleAlert(message) {
    const normalizedMessage = (message || "").toString().trim() || "發生未預期錯誤";
    console.info("ui-message", normalizedMessage);
    window.alert(normalizedMessage);
}
/**
 * 取得 API 基底網址。
 * @returns {string} API base url
 */
function getApiBaseUrl() {
    const hidden = document.getElementById("apiBaseUrl");
    return (hidden === null || hidden === void 0 ? void 0 : hidden.value.trim()) || "https://localhost:7170";
}

/**
 * 取得影像處理旗標（A=LLM, B=Tesseract）。
 * @returns {string} 影像處理旗標
 */
function getImageProcessFlag() {
    const hidden = document.getElementById("imageProcessFlag");
    const value = ((hidden === null || hidden === void 0 ? void 0 : hidden.value) || "A").toString().trim().toUpperCase();
    return value === "B" ? "B" : "A";
}
/**
 * 取得 LLM 提供者 radio 群組容器。

    const baseUrl = getApiBaseUrl();
    setVoiceOutputStatus("語音輸出請求中...");
    const response = await fetch(`${baseUrl}/api/g1/piper-live-player/play`, {
        method: "POST",
        credentials: "include",
        headers: buildInsuranceChatHeaders(),
        body: JSON.stringify({ text: normalizedText, saveToDocTestWaveFile: Boolean(saveToDocTestWaveFile) })
    });

    if (!response.ok) {
        let message = "語音輸出失敗。";
        try {
            const data = await response.json();
            message = (data === null || data === void 0 ? void 0 : data.msg) || message;
        }
        catch {
            // 忽略非 JSON 回應，保留預設錯誤訊息。
        }

        throw new Error(message);
    }

    let responseData = null;
    try {
        responseData = await response.json();
    }
    catch {
        responseData = null;
    }

    setVoiceOutputStatus(saveToDocTestWaveFile ? "語音輸出完成，已儲存 doc/test.wav" : "語音輸出完成");
    console.info("voice output completed", { textLength: normalizedText.length });
    return responseData;
}

async function playReplyVoice(reply) {
    return playVoiceText(reply, false, false);
}

async function playDebugVoiceTest() {
    return playVoiceText("測試測試一二三", true, true);
}
/**
 * 載入可選擇的 LLM 提供者。
 */
async function loadLlmProviderOptions() {
    const group = getLlmProviderGroup();
    if (!group) {
        return;
    }
    const delay = (ms) => new Promise((resolve) => window.setTimeout(resolve, ms));
    const baseUrl = getApiBaseUrl();
    const syncWindow = window;
    let lastError = null;
    for (let attempt = 1; attempt <= 3; attempt++) {
        try {
            const response = await fetch(`${baseUrl}/api/g1/text-to-sql/llm-providers`, {
                method: "GET",
                credentials: "include"
            });
            let data = null;
            try {
                data = await response.json();
            }
            catch {
                data = null;
            }
            if (response.status === 401 && syncWindow.syncAuthStateFromServer) {
                await syncWindow.syncAuthStateFromServer();
                throw new Error("登入狀態同步中，重試取得 LLM 選項");
            }
            if (!response.ok || (data === null || data === void 0 ? void 0 : data.code) !== 0 || !(data === null || data === void 0 ? void 0 : data.data)) {
                throw new Error((data === null || data === void 0 ? void 0 : data.msg) || "無法取得 LLM 選項");
            }
            const options = Array.isArray(data.data.providers) ? data.data.providers : [];
            const defaultProvider = data.data.defaultProvider;
            const providers = options
                .map((option) => {
                const provider = ((option === null || option === void 0 ? void 0 : option.provider) || (option === null || option === void 0 ? void 0 : option.Provider) || "").trim();
                const displayFlag = Boolean((option === null || option === void 0 ? void 0 : option.displayFlag) ?? (option === null || option === void 0 ? void 0 : option.DisplayFlag));
                return provider ? { provider, displayFlag } : null;
            })
                .filter((option) => (option === null || option === void 0 ? void 0 : option.displayFlag) === true)
                .map((option) => option.provider);
            if (providers.length === 0) {
                group.innerHTML = '<span class="small text-secondary">目前沒有可顯示的 LLM 選項</span>';
                return;
            }
            const effectiveDefault = defaultProvider && providers.includes(defaultProvider) ? defaultProvider : providers[0];
            group.innerHTML = "";
            for (const provider of providers) {
                const id = `llmProviderOption_${provider}`;
                const label = document.createElement("label");
                label.className = "llm-provider-option";
                label.setAttribute("for", id);
                const radio = document.createElement("input");
                radio.type = "radio";
                radio.id = id;
                radio.name = "llmProviderOption";
                radio.value = provider;
                radio.checked = provider === effectiveDefault;
                const text = document.createElement("span");
                text.textContent = provider;
                label.appendChild(radio);
                label.appendChild(text);
                group.appendChild(label);
            }
            return;
        }
        catch (error) {
            lastError = error;
            if (attempt < 3) {
                await delay(250 * attempt);
                continue;
            }
        }
    }
    console.error("loadLlmProviderOptions failed", lastError);
    group.innerHTML = "";
    const label = document.createElement("label");
    label.className = "llm-provider-option";
    label.setAttribute("for", "llmProviderOption_GoogleGemini");
    const radio = document.createElement("input");
    radio.type = "radio";
    radio.id = "llmProviderOption_GoogleGemini";
    radio.name = "llmProviderOption";
    radio.value = "GoogleGemini";
    radio.checked = true;
    const text = document.createElement("span");
    text.textContent = "GoogleGemini";
    label.appendChild(radio);
    label.appendChild(text);
    group.appendChild(label);
}
async function loadQueryHistoryList() {
    const list = document.getElementById("queryHistoryList");
    const status = document.getElementById("queryHistoryStatus");
    if (!list || !status) {
        return;
    }
    list.innerHTML = "";
    status.textContent = "載入中...";
    try {
        const baseUrl = getApiBaseUrl();
        const response = await fetch(`${baseUrl}/api/g1/query-history/list`, {
            method: "GET",
            credentials: "include"
        });
        if (response.status === 401) {
            status.textContent = "未登入不會有歷史查詢紀錄";
            return;
        }
        const data = await response.json();
        if (!response.ok || data.code !== 0 || !data.data) {
            throw new Error(data.msg || "無法取得查詢列表");
        }
        if (data.data.length === 0) {
            status.textContent = "目前沒有查詢紀錄。";
            return;
        }
        status.textContent = `共 ${data.data.length} 筆`;
        for (const item of data.data) {
            const li = document.createElement("li");
            li.className = "query-history-item";
            li.dataset.fileName = item.fileName;
            const title = document.createElement("div");
            title.className = "query-history-desc";
            title.textContent = item.queryDesc || "-";
            li.appendChild(title);
            list.appendChild(li);
            if (selectedHistoryFileName && selectedHistoryFileName === item.fileName) {
                li.classList.add("active");
            }
        }
    }
    catch (error) {
        console.error("loadQueryHistoryList failed", error);
        status.textContent = "讀取失敗，請稍後再試。";
    }
}
async function loadQueryHistoryDetail(fileName) {
    try {
        const baseUrl = getApiBaseUrl();
        const response = await fetch(`${baseUrl}/api/g1/query-history/detail?fileName=${encodeURIComponent(fileName)}`, {
            method: "GET",
            credentials: "include"
        });
        const data = await response.json();
        if (!response.ok || data.code !== 0 || !data.data) {
            throw new Error(data.msg || "無法取得查詢明細");
        }
        return data.data;
    }
    catch (error) {
        console.error("loadQueryHistoryDetail failed", error);
        return null;
    }
}
function renderHistoryView(detail) {
    const conversationList = document.getElementById("conversationList");
    if (!conversationList) {
        return;
    }
    setQuerySummary((detail === null || detail === void 0 ? void 0 : detail.querySummary) || null);
    conversationList.innerHTML = "";
    currentQueryMessageElement = null;
    for (const history of (detail === null || detail === void 0 ? void 0 : detail.histories) || []) {
        appendQueryMessage((history === null || history === void 0 ? void 0 : history.queryData) || "-");
        const report = normalizeHistoryReport(history === null || history === void 0 ? void 0 : history.queryResult);
        if (report) {
            appendResultMessage(report);
        }
        else {
            appendMessage(`<h2 class="message-title">回覆</h2><pre class="mb-0">${escapeHtml(JSON.stringify((history === null || history === void 0 ? void 0 : history.queryResult) || {}, null, 2))}</pre>`, "message-result", "left");
        }
    }

    updateQueryRailList();
}
function asObject(value) {
    if (!value || typeof value !== "object" || Array.isArray(value)) {
        return null;
    }
    return value;
}
function pickIgnoreCase(source, ...keys) {
    const keyMap = new Map();
    for (const [key, value] of Object.entries(source)) {
        keyMap.set(key.toLowerCase(), value);
    }
    for (const key of keys) {
        const found = keyMap.get(key.toLowerCase());
        if (found !== undefined) {
            return found;
        }
    }
    return undefined;
}
function normalizeHistoryReport(queryResult) {
    let root = queryResult;
    if (typeof root === "string") {
        try {
            root = JSON.parse(root);
        }
        catch {
            return null;
        }
    }
    const rootObj = asObject(root);
    if (!rootObj) {
        return null;
    }
    const nested = pickIgnoreCase(rootObj, "report", "Report");
    const candidate = asObject(nested) || rootObj;
    const rawRows = pickIgnoreCase(candidate, "rows", "Rows");
    if (!Array.isArray(rawRows)) {
        return null;
    }
    const rows = rawRows
        .map((row) => asObject(row))
        .filter((row) => row !== null);
    const rawColumns = pickIgnoreCase(candidate, "columns", "Columns");
    const columns = Array.isArray(rawColumns)
        ? rawColumns.map((column) => String(column))
        : (rows.length > 0 ? Object.keys(rows[0]) : []);
    return { columns, rows };
}
/**
 * 是否顯示 Debug 面板。
 * @returns {boolean} true 表示顯示
 */
function shouldShowDebugPanel() {
    const hidden = document.getElementById("showDebugPanel");
    return (hidden === null || hidden === void 0 ? void 0 : hidden.value.trim().toLowerCase()) === "true";
}
/**
 * 初始化 Debug 面板顯示與折疊行為。
 */
function initializeDebugPanel() {
    const debugPanel = document.getElementById("debugPanel");
    const debugBody = document.getElementById("debugPanelBody");
    const toggleButton = document.getElementById("debugToggleButton");
    const voiceTestButton = document.getElementById("voiceOutputTestButton");
    if (!debugPanel) {
        return;
    }
    if (!shouldShowDebugPanel()) {
        debugPanel.classList.add("debug-hidden");
        return;
    }
    if (!debugBody || !toggleButton) {
        return;
    }
    toggleButton.addEventListener("click", () => {
        const isCollapsed = debugPanel.classList.toggle("debug-collapsed");
        toggleButton.textContent = isCollapsed ? "展開" : "收合";
        toggleButton.setAttribute("aria-expanded", String(!isCollapsed));
    });
    if (voiceTestButton instanceof HTMLButtonElement) {
        voiceTestButton.addEventListener("click", () => {
            void playDebugVoiceTest().catch((error) => {
                console.error("playDebugVoiceTest failed", error);
                setVoiceOutputStatus("語音測試失敗");
            });
        });
    }
}
/**
 * 初始化 Debug 區塊複製按鈕。
 */
function initializeDebugCopyButtons() {
    const debugPanelBody = document.getElementById("debugPanelBody");
    if (!debugPanelBody) {
        return;
    }
    debugPanelBody.addEventListener("click", async (event) => {
        var _a;
        const target = event.target;
        const button = target === null || target === void 0 ? void 0 : target.closest("button[data-copy-target]");
        if (!button) {
            return;
        }
        const contentId = button.dataset.copyTarget;
        if (!contentId) {
            return;
        }
        const contentElement = document.getElementById(contentId);
        if (!contentElement) {
            return;
        }
        const rawText = (_a = contentElement.textContent) !== null && _a !== void 0 ? _a : "";
        const fullText = (contentElement.dataset.copyFullText || "").toString();
        const preferredText = fullText.trim().length > 0 ? fullText : rawText;
        const textToCopy = preferredText.trim().length > 0 ? preferredText : "-";
        const labelSpan = button.querySelector("span");
        const defaultLabel = "複製";
        try {
            if ((navigator.clipboard === null || navigator.clipboard === void 0 ? void 0 : navigator.clipboard.writeText)) {
                await navigator.clipboard.writeText(textToCopy);
            }
            else {
                const tempInput = document.createElement("textarea");
                tempInput.value = textToCopy;
                tempInput.setAttribute("readonly", "readonly");
                tempInput.style.position = "absolute";
                tempInput.style.left = "-9999px";
                document.body.appendChild(tempInput);
                tempInput.select();
                document.execCommand("copy");
                document.body.removeChild(tempInput);
            }
            if (labelSpan) {
                labelSpan.textContent = "已複製";
                window.setTimeout(() => {
                    labelSpan.textContent = defaultLabel;
                }, 1200);
            }
        }
        catch {
            if (labelSpan) {
                labelSpan.textContent = "失敗";
                window.setTimeout(() => {
                    labelSpan.textContent = defaultLabel;
                }, 1200);
            }
        }
    });
}

function setDebugTextWithCopySource(targetElement, fullText, maxLength) {
    if (!targetElement) {
        return;
    }

    const normalizedFullText = (fullText || "").toString();
    if (normalizedFullText.trim().length === 0) {
        targetElement.textContent = "-";
        targetElement.removeAttribute("data-copy-full-text");
        return;
    }

    targetElement.dataset.copyFullText = normalizedFullText;
    targetElement.textContent = truncateDebugText(normalizedFullText, maxLength);
}
/**
 * 送出保險對話查詢。
 * @param {string} sessionId 對話工作階段
 * @param {string} userMessage 使用者查詢句
 * @returns {Promise<any>} API 回應資料
 */
async function queryInsuranceChat(sessionId, userMessage, llmProvider, promptTemplateMode) {
    const baseUrl = getApiBaseUrl();
    const selectedMode = (promptTemplateMode || "").toString().trim() || PROMPT_TEMPLATE_MODE_SALES_ASSISTANT;
    const uploadedDocumentTitles = selectedMode === PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT
        ? (Array.isArray(currentUploadedDocumentTitles)
            ? currentUploadedDocumentTitles.filter((title) => (title || "").toString().trim().length > 0)
            : [])
        : [];
    const payload = {
        sessionId,
        userMessage,
        llmProvider,
        promptTemplateMode: selectedMode,
        usePromptTemplate1: selectedMode === PROMPT_TEMPLATE_MODE_SALES_ASSISTANT,
        uploadedDocumentTitles
    };
    const response = await fetch(`${baseUrl}/api/insurance/chat`, {
        method: "POST",
        credentials: "include",
        headers: buildInsuranceChatHeaders(),
        body: JSON.stringify(payload)
    });
    const data = await response.json();
    if (!response.ok || !data || typeof data.reply !== "string") {
        const error = new Error((data === null || data === void 0 ? void 0 : data.msg) || "系統忙碌中，請稍後再試。");
        error.responseData = data || null;
        throw error;
    }
    return data;
}

/**
 * 重設保險對話工作階段。
 * @param {string} sessionId 對話工作階段
 */
async function resetInsuranceChatSession(sessionId) {
    if (!sessionId) {
        return;
    }

    const baseUrl = getApiBaseUrl();
    await fetch(`${baseUrl}/api/insurance/chat/reset/${encodeURIComponent(sessionId)}`, {
        method: "POST",
        credentials: "include",
        headers: buildInsuranceChatHeaders()
    });
}

async function extractPdfToJsonString(file, sessionId, llmProvider, documentType) {
    const baseUrl = getApiBaseUrl();
    const formData = new FormData();
    formData.append("file", file);
    if ((sessionId || "").toString().trim()) {
        formData.append("sessionId", (sessionId || "").toString().trim());
    }
    if ((llmProvider || "").toString().trim()) {
        formData.append("llmProvider", (llmProvider || "").toString().trim());
    }
    if ((documentType || "").toString().trim()) {
        formData.append("documentType", (documentType || "").toString().trim());
    }

    const headers = {};
    const user = getStorageUser();
    const userId = (user === null || user === void 0 ? void 0 : user.userId) ? user.userId.trim() : "";
    if (userId) {
        headers["X-User-Id"] = userId;
    }

    const response = await fetch(`${baseUrl}/api/g1/document-pipeline/extract-pdf-json`, {
        method: "POST",
        credentials: "include",
        headers,
        body: formData
    });

    const data = await response.json();
    if (!response.ok || !data || data.code !== 0 || typeof data.data !== "string") {
        throw new Error((data === null || data === void 0 ? void 0 : data.msg) || "PDF 解析失敗");
    }

    return data.data;
}

function readFileAsBase64(file) {
    return new Promise((resolve, reject) => {
        if (!(file instanceof File)) {
            reject(new Error("無效的影像檔案"));
            return;
        }

        const reader = new FileReader();
        reader.onload = () => {
            const dataUrl = (reader.result || "").toString();
            const commaIndex = dataUrl.indexOf(",");
            if (commaIndex < 0 || commaIndex >= dataUrl.length - 1) {
                reject(new Error("影像轉換失敗"));
                return;
            }

            resolve(dataUrl.slice(commaIndex + 1));
        };
        reader.onerror = () => reject(new Error("影像讀取失敗"));
        reader.readAsDataURL(file);
    });
}

async function analyzeImageByBase64(base64Data, mimeType, fileName, llmProvider, sessionId, imageProcessFlag) {
    const baseUrl = getApiBaseUrl();
    const payload = {
        base64Data,
        mimeType,
        fileName,
        llmProvider,
        sessionId
    };

    const normalizedFlag = (imageProcessFlag || "A").toString().trim().toUpperCase();
    const endpoint = normalizedFlag === "B"
        ? "/api/insurance/chat/image-upload-tesseract"
        : "/api/insurance/chat/image-upload";

    const response = await fetch(`${baseUrl}${endpoint}`, {
        method: "POST",
        credentials: "include",
        headers: buildInsuranceChatHeaders(),
        body: JSON.stringify(payload)
    });

    const data = await response.json();
    if (!response.ok || !data || typeof data.result !== "string") {
        throw new Error((data === null || data === void 0 ? void 0 : data.msg) || "影像分析失敗");
    }

    return data;
}

function getSelectedPromptTemplateMode() {
    const selectedPromptTemplateMode = document.querySelector('input[name="promptTemplateMode"]:checked');
    if (!(selectedPromptTemplateMode instanceof HTMLInputElement)) {
        return PROMPT_TEMPLATE_MODE_SALES_ASSISTANT;
    }

    const mode = (selectedPromptTemplateMode.value || "").trim();
    return mode || PROMPT_TEMPLATE_MODE_SALES_ASSISTANT;
}

function isConversationHistoryEnabledMode(mode) {
    return mode === PROMPT_TEMPLATE_MODE_SALES_ASSISTANT || mode === PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT;
}

function isSalesAssistantModeSelected() {
    return getSelectedPromptTemplateMode() === PROMPT_TEMPLATE_MODE_SALES_ASSISTANT;
}

function normalizeTodoInfoItems(todoInfo) {
    if (!Array.isArray(todoInfo)) {
        return [];
    }

    return todoInfo
        .map((item) => {
            if (!item || typeof item !== "object") {
                return null;
            }

            const name = (((item.name ?? item.Name) || "").toString().trim());
            const value = (((item.value ?? item.Value) || "").toString().trim());
            if (!name && !value) {
                return null;
            }

            return {
                name: name || value,
                value
            };
        })
        .filter((item) => item !== null);
}

function normalizeTodoDocumentItems(todoDocument) {
    if (!Array.isArray(todoDocument)) {
        return [];
    }

    return todoDocument
        .map((item) => {
            if (!item || typeof item !== "object") {
                return null;
            }

            const name = (((item.name ?? item.Name) || "").toString().trim());
            if (!name) {
                return null;
            }

            return {
                name,
                uploaded: Boolean(item.uploaded ?? item.Uploaded)
            };
        })
        .filter((item) => item !== null);
}

function resetTodoState() {
    currentTodoInfoItems = [];
    currentTodoDocumentItems = [];
    currentUploadedHistoryFiles = [];
}

function normalizeComparableText(value) {
    return (value || "")
        .toString()
        .replace(/[\s\-_\/\\]+/g, "")
        .toLowerCase()
        .trim();
}

function findUploadedHistoryFileByTodoName(todoName) {
    const normalizedTodoName = normalizeComparableText(todoName);
    if (!normalizedTodoName || !Array.isArray(currentUploadedHistoryFiles) || currentUploadedHistoryFiles.length === 0) {
        return null;
    }

    for (const file of currentUploadedHistoryFiles) {
        if (!file || typeof file !== "object") {
            continue;
        }

        const title = normalizeComparableText(file.title || "");
        const originalFileName = normalizeComparableText(file.originalFileName || "");
        const storedFileName = normalizeComparableText(file.storedFileName || "");
        const matched =
            (title && (title.includes(normalizedTodoName) || normalizedTodoName.includes(title)))
            || (originalFileName && (originalFileName.includes(normalizedTodoName) || normalizedTodoName.includes(originalFileName)))
            || (storedFileName && storedFileName.includes(normalizedTodoName));

        if (matched) {
            return file;
        }
    }

    return null;
}

function mergeTodoState(todoInfo, todoDocument) {
    const normalizedInfo = normalizeTodoInfoItems(todoInfo);
    const normalizedDocument = normalizeTodoDocumentItems(todoDocument);

    if (normalizedInfo.length > 0) {
        const infoMap = new Map(currentTodoInfoItems.map((item) => [item.name.toLowerCase(), item]));
        for (const item of normalizedInfo) {
            infoMap.set(item.name.toLowerCase(), item);
        }
        currentTodoInfoItems = Array.from(infoMap.values());
    }

    if (normalizedDocument.length > 0) {
        const documentMap = new Map(currentTodoDocumentItems.map((item) => [item.name.toLowerCase(), item]));
        for (const item of normalizedDocument) {
            const key = item.name.toLowerCase();
            const existing = documentMap.get(key);
            if (!existing) {
                documentMap.set(key, { ...item });
                continue;
            }

            if (item.uploaded && !existing.uploaded) {
                existing.uploaded = true;
            }
        }
        currentTodoDocumentItems = Array.from(documentMap.values());
    }
}

function replaceTodoState(todoInfo, todoDocument) {
    currentTodoInfoItems = normalizeTodoInfoItems(todoInfo);
    currentTodoDocumentItems = normalizeTodoDocumentItems(todoDocument);
}

function renderTodoListPanel() {
    const todoInfoList = document.getElementById("todoInfoList");
    const todoInfoEmpty = document.getElementById("todoInfoEmpty");
    const todoInfoTableContainer = document.getElementById("todoInfoTableContainer");
    const todoDocumentList = document.getElementById("todoDocumentList");
    const todoDocumentEmpty = document.getElementById("todoDocumentEmpty");
    const todoDocumentTableContainer = document.getElementById("todoDocumentTableContainer");
    const todoListMeta = document.getElementById("todoListMeta");

    if (!todoInfoList || !todoInfoEmpty || !todoInfoTableContainer || !todoDocumentList || !todoDocumentEmpty || !todoDocumentTableContainer) {
        return;
    }

    todoInfoList.innerHTML = "";
    todoDocumentList.innerHTML = "";

    const hasTodoInfo = currentTodoInfoItems.length > 0;
    const hasTodoDocument = currentTodoDocumentItems.length > 0;

    todoInfoEmpty.style.display = hasTodoInfo ? "none" : "block";
    todoInfoTableContainer.hidden = !hasTodoInfo;
    todoDocumentEmpty.style.display = hasTodoDocument ? "none" : "block";
    todoDocumentTableContainer.hidden = !hasTodoDocument;

    for (const item of currentTodoInfoItems) {
        const row = document.createElement("tr");
        const valueText = ((item.value || "").toString().trim()) || "-";
        row.innerHTML = `<td class="todo-table-label">${escapeHtml((item.name || "-").toString())}</td><td class="todo-table-value">${escapeHtml(valueText)}</td>`;
        todoInfoList.appendChild(row);
    }

    for (const item of currentTodoDocumentItems) {
        const row = document.createElement("tr");
        const uploadedText = item.uploaded ? "已上傳" : "待上傳";
        const uploadedFile = item.uploaded ? findUploadedHistoryFileByTodoName(item.name) : null;
        const canView = Boolean(uploadedFile && selectedHistoryFileName);
        const actionHtml = canView
            ? `<button type="button" class="todo-view-btn" data-view-uploaded-file="${escapeHtml(uploadedFile.storedFileName || "")}" aria-label="檢視已上傳文件 ${escapeHtml((item.name || "").toString())}" title="檢視文件"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M16 8s-3-5.5-8-5.5S0 8 0 8s3 5.5 8 5.5S16 8 16 8M1.173 8a13 13 0 0 1 1.66-2.043C4.12 4.668 5.88 3.5 8 3.5s3.879 1.168 5.168 2.457A13 13 0 0 1 14.828 8c-.058.087-.122.183-.195.288-.335.48-.83 1.12-1.465 1.755C11.879 11.332 10.12 12.5 8 12.5s-3.879-1.168-5.168-2.457A13 13 0 0 1 1.172 8z"/><path d="M8 5.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 0 0 0-5M6.5 8a1.5 1.5 0 1 1 3 0a1.5 1.5 0 0 1-3 0"/></svg></button>`
            : `<span class="todo-view-placeholder">-</span>`;
        row.innerHTML = `<td class="todo-table-label">${escapeHtml((item.name || "-").toString())}</td><td class="todo-table-value">${escapeHtml(uploadedText)}</td><td class="todo-table-action">${actionHtml}</td>`;
        todoDocumentList.appendChild(row);
    }

    if (todoListMeta) {
        const totalInfo = currentTodoInfoItems.length;
        const totalDocument = currentTodoDocumentItems.length;
        todoListMeta.textContent = `待釐清訊息 ${totalInfo} 筆，待處理文件 ${totalDocument} 筆`;
    }
}

async function listInsuranceChatHistories() {
    const baseUrl = getApiBaseUrl();
    const promptTemplateMode = getSelectedPromptTemplateMode();
    const response = await fetch(`${baseUrl}/api/insurance/chat/histories?promptTemplateMode=${encodeURIComponent(promptTemplateMode)}`, {
        method: "GET",
        credentials: "include",
        headers: buildInsuranceChatHeaders()
    });
    const data = await response.json();
    if (!response.ok || !Array.isArray(data)) {
        const error = new Error((data === null || data === void 0 ? void 0 : data.msg) || "讀取對話紀錄失敗。");
        error.responseData = data || null;
        throw error;
    }

    return data;
}

async function getInsuranceChatHistory(fileName) {
    const baseUrl = getApiBaseUrl();
    const promptTemplateMode = getSelectedPromptTemplateMode();
    const response = await fetch(`${baseUrl}/api/insurance/chat/histories/${encodeURIComponent(fileName)}?promptTemplateMode=${encodeURIComponent(promptTemplateMode)}`, {
        method: "GET",
        credentials: "include",
        headers: buildInsuranceChatHeaders()
    });
    const data = await response.json();
    if (!response.ok || !data || !Array.isArray(data.conversations) || typeof data.sessionId !== "string") {
        const error = new Error((data === null || data === void 0 ? void 0 : data.msg) || "載入對話紀錄失敗。");
        error.responseData = data || null;
        throw error;
    }

    return data;
}

async function deleteInsuranceChatHistory(fileName) {
    const baseUrl = getApiBaseUrl();
    const promptTemplateMode = getSelectedPromptTemplateMode();
    const response = await fetch(`${baseUrl}/api/insurance/chat/histories/${encodeURIComponent(fileName)}?promptTemplateMode=${encodeURIComponent(promptTemplateMode)}`, {
        method: "DELETE",
        credentials: "include",
        headers: buildInsuranceChatHeaders()
    });

    if (response.status === 204) {
        return;
    }

    let data = null;
    try {
        data = await response.json();
    }
    catch {
        data = null;
    }

    if (!response.ok) {
        const error = new Error((data === null || data === void 0 ? void 0 : data.msg) || "刪除對話紀錄失敗。");
        error.responseData = data || null;
        throw error;
    }
}

async function listInsurancePolicies() {
    const baseUrl = getApiBaseUrl();
    const response = await fetch(`${baseUrl}/api/insurance/chat/policies`, {
        method: "GET",
        credentials: "include",
        headers: buildInsuranceChatHeaders()
    });

    const data = await response.json();
    if (!response.ok || !Array.isArray(data)) {
        const error = new Error((data === null || data === void 0 ? void 0 : data.msg) || "讀取保單清單失敗。");
        error.responseData = data || null;
        throw error;
    }

    return data;
}

function normalizePolicyInsureTypeList(value) {
    if (Array.isArray(value)) {
        return value
            .map((item) => (item || "").toString().trim())
            .filter((item) => item.length > 0);
    }

    const normalized = (value || "").toString().trim();
    if (!normalized) {
        return [];
    }

    return normalized
        .split(/[，,]/)
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
}

function formatPolicyInsureTypesForDisplay(value) {
    const items = normalizePolicyInsureTypeList(value);
    return items.length > 0 ? items.join(", ") : "-";
}

function buildPolicyQuerySentence(policy) {
    const policyNo = ((policy === null || policy === void 0 ? void 0 : policy.policyNo) || "").toString().trim();
    const insuredName = ((policy === null || policy === void 0 ? void 0 : policy.insuredName) || "").toString().trim();
    const plateNo = ((policy === null || policy === void 0 ? void 0 : policy.plateNo) || "").toString().trim();
    const insureTypes = formatPolicyInsureTypesForDisplay(policy === null || policy === void 0 ? void 0 : policy.insureTypeList);

    return `保單號碼: ${policyNo} 被保險人: ${insuredName} 車牌號碼: ${plateNo} 投保險種: ${insureTypes}`;
}

function buildPolicyListMessageHtml(policies, selectedPolicyNo) {
    const normalizedPolicies = Array.isArray(policies) ? policies : [];
    if (normalizedPolicies.length === 0) {
        return "<h2 class=\"message-title\">保單清單</h2><p class=\"mb-0\">目前沒有可選保單。</p>";
    }

    const selectedPolicy = normalizedPolicies.find((item) => ((item === null || item === void 0 ? void 0 : item.policyNo) || "").toString().trim() === selectedPolicyNo) || null;
    const selectedSummary = selectedPolicy
        ? `<div class="policy-selected-summary">已選取保單: ${escapeHtml((selectedPolicy.policyNo || "-").toString())} / ${escapeHtml((selectedPolicy.insuredName || "-").toString())} / ${escapeHtml((selectedPolicy.plateNo || "-").toString())} / ${escapeHtml(formatPolicyInsureTypesForDisplay(selectedPolicy === null || selectedPolicy === void 0 ? void 0 : selectedPolicy.insureTypeList))}</div>`
        : "";

    const listHtml = normalizedPolicies.map((policy) => {
        const policyNo = ((policy === null || policy === void 0 ? void 0 : policy.policyNo) || "").toString().trim();
        const insuredName = ((policy === null || policy === void 0 ? void 0 : policy.insuredName) || "").toString().trim();
        const plateNo = ((policy === null || policy === void 0 ? void 0 : policy.plateNo) || "").toString().trim();
        const insureTypeList = formatPolicyInsureTypesForDisplay(policy === null || policy === void 0 ? void 0 : policy.insureTypeList);
        const isSelected = selectedPolicyNo && selectedPolicyNo === policyNo;
        const selectedClass = isSelected ? " is-selected" : "";
        const selectedBadge = isSelected ? "<span class=\"policy-selected-badge\">已選取</span>" : "";

        return `
            <button type=\"button\" class=\"policy-item-btn${selectedClass}\" data-policy-no=\"${escapeHtml(policyNo)}\" data-insured-name=\"${escapeHtml(insuredName)}\" data-plate-no=\"${escapeHtml(plateNo)}\" data-insure-type-list=\"${escapeHtml(insureTypeList)}\">
                <div class=\"policy-item-title\">${escapeHtml(policyNo)} ${selectedBadge}</div>
                <div class=\"policy-item-sub\">被保險人: ${escapeHtml(insuredName)} / 車牌號碼: ${escapeHtml(plateNo)}</div>
                <div class=\"policy-item-sub\">投保險種: ${escapeHtml(insureTypeList)}</div>
            </button>`;
    }).join("");

    return `
        <h2 class=\"message-title\">保單清單</h2>
        ${selectedSummary}
        <div class=\"policy-list\">${listHtml}</div>`;
}

function appendPolicyListMessage(policies, selectedPolicyNo) {
    const html = buildPolicyListMessageHtml(policies, selectedPolicyNo || "");
    const article = appendMessage(html, "message-result", "left");
    if (!article) {
        return null;
    }

    article.dataset.policyList = "true";
    article.dataset.policyListItems = JSON.stringify(Array.isArray(policies) ? policies : []);
    article.dataset.selectedPolicyNo = selectedPolicyNo || "";
    return article;
}

function updatePolicyListMessageSelection(targetArticle, selectedPolicyNo) {
    if (!(targetArticle instanceof HTMLElement)) {
        return;
    }

    const rawItems = (targetArticle.dataset.policyListItems || "").trim();
    if (!rawItems) {
        return;
    }

    let policies = [];
    try {
        const parsed = JSON.parse(rawItems);
        policies = Array.isArray(parsed) ? parsed : [];
    }
    catch {
        policies = [];
    }

    targetArticle.innerHTML = buildPolicyListMessageHtml(policies, selectedPolicyNo || "");
    targetArticle.dataset.selectedPolicyNo = selectedPolicyNo || "";
}

function appendSystemPromptDebug(systemPrompt) {
    const promptList = document.getElementById("llmSystemPromptList");
    setDebugTextWithCopySource(promptList, systemPrompt, 1000);
}

function appendStoryScriptDebug(storyScript) {
    const scriptList = document.getElementById("llmStoryScriptList");
    setDebugTextWithCopySource(scriptList, storyScript, 1000);
}

function appendLlmLogErrorsDebug(llmLogErrors) {
    const errorListBlock = document.getElementById("llmLogErrorList");
    if (!errorListBlock) {
        return;
    }

    if (!Array.isArray(llmLogErrors) || llmLogErrors.length === 0) {
        errorListBlock.textContent = "-";
        return;
    }

    const normalizedErrors = llmLogErrors
        .map((item) => (item || "").toString().trim())
        .filter((item) => item.length > 0);

    if (normalizedErrors.length === 0) {
        errorListBlock.textContent = "-";
        return;
    }

    errorListBlock.textContent = normalizedErrors
        .map((item, index) => `${index + 1}. ${item}`)
        .join("\n");
}

function appendLlmResultDebug(llmResult) {
    const resultBlock = document.getElementById("llmLatestResult");
    if (!resultBlock) {
        return;
    }

    if (llmResult === null || llmResult === undefined) {
        resultBlock.textContent = "-";
        return;
    }

    if (typeof llmResult === "string") {
        const normalizedResult = llmResult.trim();
        resultBlock.textContent = normalizedResult || "-";
        return;
    }

    try {
        const debugPayload = buildDebugLlmResultPayload(llmResult);
        if (typeof debugPayload === "string") {
            resultBlock.textContent = debugPayload || "-";
            return;
        }

        resultBlock.textContent = JSON.stringify(debugPayload, null, 2);
    }
    catch {
        resultBlock.textContent = String(llmResult);
    }
}

function truncateDebugText(text, maxLength) {
    const normalizedText = (text || "").toString();
    if (!normalizedText || normalizedText.length <= maxLength) {
        return normalizedText;
    }

    return `${normalizedText.slice(0, maxLength)}\n\n... (已截斷，僅顯示前 ${maxLength} 字)`;
}

function buildDebugLlmResultPayload(llmResult) {
    if (!llmResult || typeof llmResult !== "object" || Array.isArray(llmResult)) {
        return llmResult;
    }

    const normalizedRawReply = ((llmResult.llmRawReply ?? llmResult.llm_raw_reply) || "").toString().trim();
    return normalizedRawReply || null;
}

function appendUploadedDocumentDebug(rawTextContent, structuredTextContent) {
    const rawTextBlock = document.getElementById("uploadedFileRawText");
    const structuredTextBlock = document.getElementById("uploadedFileStructuredText");

    if (rawTextBlock) {
        const normalizedRawText = (rawTextContent || "").toString().trim();
        rawTextBlock.textContent = normalizedRawText || "-";
    }

    if (structuredTextBlock) {
        const normalizedStructuredText = (structuredTextContent || "").toString().trim();
        structuredTextBlock.textContent = normalizedStructuredText || "-";
    }
}

function normalizeDebugDisplayText(value) {
    if (value === null || value === undefined) {
        return "";
    }

    if (typeof value === "string") {
        return value.trim();
    }

    try {
        return JSON.stringify(value, null, 2);
    }
    catch {
        return String(value);
    }
}

function extractStructuredDocumentTitle(value) {
    if (!value || typeof value !== "object") {
        return null;
    }

    const title = (value.title ?? value.Title ?? "").toString().trim();
    if (title) {
        return title;
    }

    const step1 = value.step1 ?? value.Step1;
    if (step1 && typeof step1 === "object") {
        const step1Structured = step1.structured ?? step1.Structured;
        if (step1Structured && typeof step1Structured === "object") {
            const nestedTitle = (step1Structured.title ?? step1Structured.Title ?? "").toString().trim();
            if (nestedTitle) {
                return nestedTitle;
            }
        }
    }

    return null;
}

function extractImageUploadDocumentTitle(resultText, fallbackFileName) {
    const normalizedText = (resultText || "").toString().trim();
    if (normalizedText) {
        try {
            const normalizedJson = normalizeRawJsonText(normalizedText);
            if (normalizedJson) {
                const parsed = JSON.parse(normalizedJson);
                const structuredTitle = extractStructuredDocumentTitle(parsed);
                if (structuredTitle) {
                    return structuredTitle;
                }
            }
        }
        catch {
            // Fallback below.
        }
    }

    const normalizedFallback = (fallbackFileName || "").toString().trim();
    if (!normalizedFallback) {
        return "";
    }

    return normalizedFallback.replace(/\.[^.]+$/, "").trim() || normalizedFallback;
}

function toComparableLabel(value) {
    return (value || "").toString().replace(/\s+/g, "").trim().toLowerCase();
}

function collectStructuredFieldPairs(value) {
    const pairs = [];
    if (!value || typeof value !== "object") {
        return pairs;
    }

    const tryCollect = (fieldList) => {
        const normalizedList = Array.isArray(fieldList) ? fieldList : [];
        for (const item of normalizedList) {
            if (!item || typeof item !== "object") {
                continue;
            }

            const label = ((item.label ?? item.Label) || "").toString().trim();
            const fieldValue = ((item.value ?? item.Value) || "").toString().trim();
            if (!label || !fieldValue) {
                continue;
            }

            pairs.push({ label, value: fieldValue });
        }
    };

    tryCollect(value.fields ?? value.Fields);
    const step1 = value.step1 ?? value.Step1;
    if (step1 && typeof step1 === "object") {
        const step1Structured = step1.structured ?? step1.Structured;
        if (step1Structured && typeof step1Structured === "object") {
            tryCollect(step1Structured.fields ?? step1Structured.Fields);
        }
    }

    return pairs;
}

function extractTodoInfoValuesFromStructured(structuredPayload, todoInfoItems) {
    const fieldPairs = collectStructuredFieldPairs(structuredPayload);
    if (fieldPairs.length === 0) {
        return [];
    }

    const normalizedTodoInfoItems = Array.isArray(todoInfoItems) ? todoInfoItems : [];
    const results = [];

    for (const todoItem of normalizedTodoInfoItems) {
        const todoName = ((todoItem === null || todoItem === void 0 ? void 0 : todoItem.name) || "").toString().trim();
        if (!todoName) {
            continue;
        }

        const targetName = toComparableLabel(todoName);
        const matched = fieldPairs.find((pair) => {
            const fieldName = toComparableLabel(pair.label);
            return fieldName.includes(targetName) || targetName.includes(fieldName);
        });

        if (!matched) {
            continue;
        }

        results.push({
            name: todoName,
            value: matched.value
        });
    }

    const uniqueByName = new Map();
    for (const item of results) {
        const key = toComparableLabel(item.name);
        if (!uniqueByName.has(key)) {
            uniqueByName.set(key, item);
        }
    }

    return Array.from(uniqueByName.values());
}

function enrichStructuredPayloadWithTodoInfo(structuredPayload, todoInfoItems) {
    if (!structuredPayload || typeof structuredPayload !== "object") {
        return structuredPayload;
    }

    const extractedTodoInfo = extractTodoInfoValuesFromStructured(structuredPayload, todoInfoItems);
    if (extractedTodoInfo.length === 0) {
        return structuredPayload;
    }

    return {
        ...structuredPayload,
        extracted_todo_info: extractedTodoInfo
    };
}

function buildUploadedDocumentDebugDisplay(items, selector) {
    const normalizedItems = Array.isArray(items) ? items : [];
    const sections = [];

    for (const item of normalizedItems) {
        if (!item || typeof item !== "object") {
            continue;
        }

        const fileName = (item.fileName || "(未命名檔案)").toString();
        const content = normalizeDebugDisplayText(selector(item));
        if (!content) {
            continue;
        }

        sections.push(`【${fileName}】\n${content}`);
    }

    return sections.join("\n\n");
}
/**
 * 清空本次查詢的 debug 與結果內容。
 */
function resetQueryView(clearConversation = false) {
    if (clearConversation) {
        const promptList = document.getElementById("llmSystemPromptList");
        const scriptList = document.getElementById("llmStoryScriptList");
        const resultBlock = document.getElementById("llmLatestResult");
        const llmLogErrorList = document.getElementById("llmLogErrorList");
        if (promptList) {
            promptList.textContent = "-";
            promptList.removeAttribute("data-copy-full-text");
        }
        if (scriptList) {
            scriptList.textContent = "-";
            scriptList.removeAttribute("data-copy-full-text");
        }
        if (resultBlock) {
            resultBlock.textContent = "-";
        }
        if (llmLogErrorList) {
            llmLogErrorList.textContent = "-";
        }
        appendUploadedDocumentDebug("", "");
    }

    if (clearConversation) {
        const conversationList = document.getElementById("conversationList");
        if (conversationList) {
            conversationList.innerHTML = "";
        }
        currentQueryMessageElement = null;
        updateQueryRailList();
    }
}

/**
 * 建立對話訊息節點並附加到右側。
 * @param {string} html 內容 HTML
 * @param {string} bubbleClass 泡泡樣式類名
 * @param {"left"|"right"} align 對齊方向
 */
function appendMessage(html, bubbleClass, align = "right") {
    const conversationList = document.getElementById("conversationList");
    if (!conversationList) {
        return;
    }
    const row = document.createElement("div");
    row.className = `message-row ${align === "left" ? "message-row-left" : "message-row-right"}`;
    const article = document.createElement("article");
    article.className = `message-bubble ${bubbleClass}`;
    article.innerHTML = html;
    row.appendChild(article);
    conversationList.appendChild(row);
    conversationList.scrollTop = conversationList.scrollHeight;
    return article;
}

/**
 * 新增查詢句訊息。
 * @param {string} query 查詢內容
 */
function appendQueryMessage(query) {
    const safeQuery = query
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\n", "<br />");
    const queryMessage = appendMessage(`<h2 class="message-title">查詢句</h2><p class="mb-0">${safeQuery}</p>`, "message-query");
    if (!queryMessage) {
        return;
    }

    if (currentQueryMessageElement && currentQueryMessageElement.isConnected) {
        currentQueryMessageElement.removeAttribute("data-current-query");
        currentQueryMessageElement.classList.remove("is-focus-target");
    }

    currentQueryMessageElement = queryMessage;
    queryMessageCounter += 1;
    currentQueryMessageElement.dataset.queryItemId = `query-item-${queryMessageCounter}`;
    currentQueryMessageElement.dataset.queryText = query;
    currentQueryMessageElement.setAttribute("data-current-query", "true");
    currentQueryMessageElement.setAttribute("tabindex", "-1");
    updateQueryRailList();
}

function shortenQueryText(text, maxLength = 42) {
    const normalized = (text || "").replace(/\s+/g, " ").trim();
    if (!normalized) {
        return "-";
    }

    if (normalized.length <= maxLength) {
        return normalized;
    }

    return `${normalized.slice(0, maxLength)}...`;
}

function focusQueryById(queryItemId, smooth = true) {
    if (!queryItemId) {
        return;
    }

    const target = document.querySelector(`.message-bubble.message-query[data-query-item-id="${queryItemId}"]`);
    if (!(target instanceof HTMLElement)) {
        return;
    }

    if (currentQueryMessageElement && currentQueryMessageElement.isConnected) {
        currentQueryMessageElement.removeAttribute("data-current-query");
        currentQueryMessageElement.classList.remove("is-focus-target");
    }

    currentQueryMessageElement = target;
    currentQueryMessageElement.setAttribute("data-current-query", "true");
    currentQueryMessageElement.setAttribute("tabindex", "-1");
    currentQueryMessageElement.classList.add("is-focus-target");
    currentQueryMessageElement.scrollIntoView({ block: "center", behavior: smooth ? "smooth" : "auto" });
    currentQueryMessageElement.focus({ preventScroll: true });
    window.setTimeout(() => {
        if (currentQueryMessageElement === target) {
            currentQueryMessageElement.classList.remove("is-focus-target");
        }
    }, 950);
    updateQueryRailList();
}

function updateQueryRailList() {
    const queryRail = document.getElementById("queryRail");
    const queryRailList = document.getElementById("queryRailList");
    const queryRailEmpty = document.getElementById("queryRailEmpty");
    const conversationList = document.getElementById("conversationList");
    if (!queryRail || !queryRailList || !queryRailEmpty || !conversationList) {
        return;
    }

    const queryMessages = Array.from(conversationList.querySelectorAll(".message-bubble.message-query[data-query-item-id]"));
    queryRailList.innerHTML = "";

    if (queryMessages.length === 0) {
        queryRail.style.display = "none";
        queryRailEmpty.style.display = "block";
        return;
    }

    queryRail.style.display = "block";
    queryRailEmpty.style.display = "none";
    const orderedMessages = [...queryMessages];
    for (const message of orderedMessages) {
        const queryItemId = message.getAttribute("data-query-item-id") || "";
        const queryText = message.getAttribute("data-query-text") || message.textContent || "";
        const item = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "query-rail-item-btn";
        button.dataset.queryTarget = queryItemId;
        button.textContent = shortenQueryText(queryText);
        if (currentQueryMessageElement === message) {
            button.classList.add("is-active");
        }

        item.appendChild(button);
        queryRailList.appendChild(item);
    }
}

function initializeQueryRail() {
    const queryRail = document.getElementById("queryRail");
    const queryRailHandle = queryRail === null || queryRail === void 0 ? void 0 : queryRail.querySelector(".query-rail-handle");
    const queryRailList = document.getElementById("queryRailList");
    if (!queryRail || !queryRailHandle || !queryRailList) {
        return;
    }

    queryRailHandle.addEventListener("mouseenter", () => {
        queryRail.classList.remove("is-closed");
    });

    queryRailList.addEventListener("click", (event) => {
        const target = event.target;
        const button = target === null || target === void 0 ? void 0 : target.closest("button[data-query-target]");
        if (!button) {
            return;
        }

        const queryTarget = (button.dataset.queryTarget || "").trim();
        if (!queryTarget) {
            return;
        }

        focusQueryById(queryTarget, true);
        queryRail.classList.add("is-closed");
        if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur();
        }
    });

    updateQueryRailList();
}

function scrollResultAndFocusCurrentQuery() {
    const conversationList = document.getElementById("conversationList");
    if (conversationList) {
        conversationList.scrollTop = conversationList.scrollHeight;
    }

    if (!currentQueryMessageElement || !currentQueryMessageElement.isConnected) {
        return;
    }

    currentQueryMessageElement.scrollIntoView({ block: "center", behavior: "smooth" });
    currentQueryMessageElement.focus({ preventScroll: true });
    updateQueryRailList();
}

/**
 * 新增查詢結果訊息。
 * @param {any} report 報表資料
 */
function appendResultMessage(report) {
    const resultMessageId = `result-message-${++resultMessageCounter}`;
    const tableHtml = buildResultTableHtml(report, resultMessageId);
    appendMessage(`<h2 class="message-title">回覆</h2>${tableHtml}`, "message-result", "left");
}

/**
 * 新增對話回覆結果訊息。
 * @param {string} reply AI 回覆內容
 */
function appendChatReplyMessage(reply) {
    const safeReply = escapeHtml((reply || "-").toString()).replaceAll("\n", "<br />");
    appendMessage(`<h2 class="message-title">回覆</h2><p class="mb-0">${safeReply}</p>`, "message-result", "left");
}

function setConversationHistoryPanelOpenState(isOpen) {
    const queryShell = document.querySelector(".query-shell");
    if (!queryShell) {
        return;
    }

    queryShell.classList.toggle("history-panel-open", isOpen);
}

function renderConversationHistoryPanelItems(items) {
    const historyListElement = document.getElementById("conversationHistoryList");
    const historyEmptyElement = document.getElementById("conversationHistoryEmpty");
    if (!historyListElement || !historyEmptyElement) {
        return;
    }

    historyListElement.innerHTML = "";
    const normalizedItems = Array.isArray(items) ? items : [];
    historyEmptyElement.style.display = normalizedItems.length > 0 ? "none" : "block";
    for (const item of normalizedItems) {
        const fileName = (((item === null || item === void 0 ? void 0 : item.fileName) || "").toString().trim());
        const description = (((item === null || item === void 0 ? void 0 : item.description) || (item === null || item === void 0 ? void 0 : item.fileName) || "-").toString());
        const row = document.createElement("div");
        row.className = "conversation-history-item-row";

        const openButton = document.createElement("button");
        openButton.type = "button";
        openButton.className = "list-group-item list-group-item-action conversation-history-item";
        openButton.dataset.historyFileName = fileName;
        openButton.innerHTML = `<div class="conversation-history-item-title">${escapeHtml(description)}</div>`;
        openButton.onclick = () => {
            void window.loadConversationHistoryFromPanel(openButton.dataset.historyFileName || "");
        };

        const deleteButton = document.createElement("button");
        deleteButton.type = "button";
        deleteButton.className = "conversation-history-delete-btn";
        deleteButton.dataset.deleteHistoryFileName = fileName;
        deleteButton.setAttribute("aria-label", `刪除對話紀錄 ${description}`);
        deleteButton.setAttribute("title", "刪除對話紀錄");
        deleteButton.textContent = "X";
        deleteButton.onclick = () => {
            void window.deleteConversationHistoryFromPanel(deleteButton.dataset.deleteHistoryFileName || "");
        };

        row.appendChild(openButton);
        row.appendChild(deleteButton);
        historyListElement.appendChild(row);
    }
}

async function refreshConversationHistoryPanel() {
    const historyListElement = document.getElementById("conversationHistoryList");
    const historyEmptyElement = document.getElementById("conversationHistoryEmpty");
    const historyMetaElement = document.getElementById("conversationHistoryMeta");
    if (!historyListElement || !historyEmptyElement) {
        return;
    }

    let currentUser = getStorageUser();
    if ((!currentUser || !currentUser.userId) && window.syncAuthStateFromServer) {
        currentUser = await window.syncAuthStateFromServer();
    }

    const currentUserId = ((currentUser === null || currentUser === void 0 ? void 0 : currentUser.userId) || "未登入").toString().trim() || "未登入";
    historyEmptyElement.style.display = "block";
    historyEmptyElement.textContent = "載入中...";
    historyListElement.innerHTML = "";
    if (historyMetaElement) {
        historyMetaElement.textContent = `目前登入者：${currentUserId}`;
    }

    try {
        const items = await listInsuranceChatHistories();
        renderConversationHistoryPanelItems(items);
        if (historyMetaElement) {
            historyMetaElement.textContent = `目前登入者：${currentUserId}，找到 ${items.length} 筆對話紀錄`;
        }
        historyEmptyElement.textContent = "目前沒有可載入的對話紀錄。";
    }
    catch (error) {
        console.error("refreshConversationHistoryPanel failed", error);
        historyEmptyElement.textContent = buildQueryErrorMessage(error);
        if (historyMetaElement) {
            historyMetaElement.textContent = `目前登入者：${currentUserId}，讀取失敗`;
        }
    }
}

async function loadConversationHistoryFromPanel(fileName) {
    const normalizedFileName = (fileName || "").toString().trim();
    if (!normalizedFileName) {
        return;
    }

    const response = await getInsuranceChatHistory(normalizedFileName);
    replaceTodoState(response.todo_info || response.todoInfo, response.todo_document || response.todoDocument);
    currentUploadedHistoryFiles = Array.isArray(response.uploaded_files || response.uploadedFiles) ? (response.uploaded_files || response.uploadedFiles) : [];
    populateConversationFromHistory(response.conversations);
    insuranceChatSessionId = (response.sessionId || "").toString().trim() || null;
    selectedHistoryFileName = (response.fileName || normalizedFileName).toString().trim();

    const queryInput = document.getElementById("queryInput");
    const statusText = document.getElementById("statusText");
    if (queryInput instanceof HTMLTextAreaElement) {
        queryInput.value = "";
        queryInput.focus();
    }
    if (statusText) {
        statusText.textContent = "已載入對話紀錄";
    }

    const historyPanelElement = document.getElementById("conversationHistoryPanel");
    if (historyPanelElement && window.bootstrap) {
        window.bootstrap.Offcanvas.getOrCreateInstance(historyPanelElement).hide();
    }
}

async function deleteConversationHistoryFromPanel(fileName) {
    const normalizedFileName = (fileName || "").toString().trim();
    if (!normalizedFileName) {
        return;
    }

    await deleteInsuranceChatHistory(normalizedFileName);
    if (selectedHistoryFileName && selectedHistoryFileName === normalizedFileName) {
        selectedHistoryFileName = null;
        insuranceChatSessionId = null;
    }

    await refreshConversationHistoryPanel();
}

function initializeConversationHistoryPanel() {
    if (conversationHistoryPanelInitialized) {
        return;
    }

    const historyPanelElement = document.getElementById("conversationHistoryPanel");
    const historyButton = document.getElementById("historyQueryButton");
    if (!historyPanelElement || !historyButton) {
        return;
    }

    conversationHistoryPanelInitialized = true;
    window.openConversationHistoryPanel = () => {
        void refreshConversationHistoryPanel();
        if (window.bootstrap) {
            window.bootstrap.Offcanvas.getOrCreateInstance(historyPanelElement).show();
        }
    };
    window.loadConversationHistoryFromPanel = loadConversationHistoryFromPanel;
    window.deleteConversationHistoryFromPanel = deleteConversationHistoryFromPanel;

    historyPanelElement.addEventListener("show.bs.offcanvas", () => {
        setConversationHistoryPanelOpenState(true);
        void refreshConversationHistoryPanel();
    });

    historyPanelElement.addEventListener("hidden.bs.offcanvas", () => {
        setConversationHistoryPanelOpenState(false);
    });
}

function initializeTodoListPanel() {
    if (todoListPanelInitialized) {
        return;
    }

    const todoPanelElement = document.getElementById("todoListPanel");
    const todoButtonElement = document.getElementById("todoListButton");
    if (!todoPanelElement || !todoButtonElement) {
        return;
    }

    todoListPanelInitialized = true;
    window.openTodoListPanel = () => {
        renderTodoListPanel();
        if (window.bootstrap) {
            window.bootstrap.Offcanvas.getOrCreateInstance(todoPanelElement).show();
        }
    };

    todoPanelElement.addEventListener("show.bs.offcanvas", () => {
        setConversationHistoryPanelOpenState(true);
        renderTodoListPanel();
    });

    todoPanelElement.addEventListener("hidden.bs.offcanvas", () => {
        setConversationHistoryPanelOpenState(false);
    });
}

function populateConversationFromHistory(conversations) {
    resetQueryView(true);
    const items = Array.isArray(conversations) ? conversations : [];
    for (const conversation of items) {
        const ask = ((conversation === null || conversation === void 0 ? void 0 : conversation.ask) || "").toString().trim();
        const reply = ((conversation === null || conversation === void 0 ? void 0 : conversation.reply) || "").toString().trim();
        if (ask) {
            appendQueryMessage(ask);
        }
        if (reply) {
            appendChatReplyMessage(reply);
        }
    }

    const lastReply = items.length > 0
        ? (((items[items.length - 1] || {}).reply) || "").toString().trim()
        : "";
    setQuerySummary(lastReply || null);
}

/**
 * 新增查詢錯誤訊息（左側顯示）。
 * @param {string} message 錯誤訊息
 */
function appendErrorResultMessage(message) {
    const safeMessage = escapeHtml(message);
    appendMessage(`<h2 class="message-title">回覆</h2><p class="mb-0">查詢失敗: ${safeMessage}</p>`, "message-result message-error", "left");
}

/**
 * 組裝查詢失敗顯示訊息，確保 Step1~Step5 失敗可讀。
 * @param {unknown} error 原始錯誤
 * @returns {string} 顯示訊息
 */
function buildQueryErrorMessage(error) {
    var _a, _b, _c, _d;
    const defaultMessage = "查詢失敗";
    if (!(error instanceof Error)) {
        return defaultMessage;
    }
    const typedError = error;
    const responseData = typedError.responseData;
    const message = typedError.message || defaultMessage;
    const step = (_b = (_a = responseData === null || responseData === void 0 ? void 0 : responseData.details) === null || _a === void 0 ? void 0 : _a.step) === null || _b === void 0 ? void 0 : _b[0];
    const stepName = (_d = (_c = responseData === null || responseData === void 0 ? void 0 : responseData.details) === null || _c === void 0 ? void 0 : _c.stepName) === null || _d === void 0 ? void 0 : _d[0];
    if (step && stepName) {
        return message.includes("Step ") ? message : `Step ${step} (${stepName}) 失敗: ${message}`;
    }
    return message;
}

/**
 * 將字串轉成安全 HTML。
 * @param {string} value 原始字串
 * @returns {string} escaped html
 */
function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}
/**
 * 判斷欄位 CSS 對齊類別。
 * @param {unknown} value 欄位值
 * @returns {string} class name
 */
function getCellClass(value) {
    if (typeof value === "number") {
        return "cell-number";
    }
    if (typeof value === "string" && /^\d{4}[-/]\d{2}[-/]\d{2}/.test(value)) {
        return "cell-date";
    }
    return "cell-string";
}
/**
 * 將值轉成顯示字串。
 * @param {unknown} value 原始值
 * @returns {string} 顯示值
 */
function formatValue(value) {
    if (value === null || value === undefined) {
        return "";
    }
    if (typeof value === "string") {
        if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
            return value.replace(/-/g, "/");
        }
        if (/^\d{2}:\d{2}:\d{2}$/.test(value)) {
            return value;
        }
        return value;
    }
    return String(value);
}

/**
 * 渲染報表表格。
 * @param {any} report 報表資料
 */
function renderTable(report) {
    appendResultMessage(report);
}

/**
 * 組裝報表表格 HTML。
 * @param {any} report 報表資料
 * @returns {string} 結果 HTML
 */
function buildResultTableHtml(report, resultMessageId) {
    if (!report.rows.length) {
        return `<div class="alert alert-warning mb-0">查無資料</div>`;
    }
    const head = report.columns.map((column) => `<th scope="col">${column}</th>`).join("");
    const body = report.rows
        .map((row) => {
        const columns = report.columns
            .map((column) => {
            const value = row[column];
            const cellClass = getCellClass(value);
            return `<td class="${cellClass}">${formatValue(value)}</td>`;
        })
            .join("");
        return `<tr>${columns}</tr>`;
    })
        .join("");
    return `
        <div class="result-table-wrapper" data-result-id="${resultMessageId}">
            <table class="table table-striped table-bordered align-middle">
                <thead><tr>${head}</tr></thead>
                <tbody>${body}</tbody>
            </table>
            <div class="result-actions">
                <button type="button" class="result-copy-btn" data-copy-result-table="true" aria-label="複製查詢結果" title="複製">
                    <svg class="result-copy-icon" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                        <path d="M10 1H3a2 2 0 0 0-2 2v7h2V3h7V1z" />
                        <path d="M13 4H6a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zm0 9H6V6h7v7z" />
                    </svg>
                    <span>複製</span>
                </button>
            </div>
    </div>
  `;
}

function normalizeClipboardCellText(value) {
    return value.replace(/\r?\n/g, " ").trim();
}

function buildTablePlainText(table) {
    const rows = Array.from(table.querySelectorAll("tr"));
    return rows
        .map((row) => Array.from(row.querySelectorAll("th,td"))
        .map((cell) => normalizeClipboardCellText(cell.textContent || ""))
        .join("\t"))
        .join("\n");
}

function buildTableHtmlForClipboard(table) {
    const rows = Array.from(table.querySelectorAll("tr"));
    const rowHtml = rows
        .map((row) => {
        const cells = Array.from(row.querySelectorAll("th,td"))
            .map((cell) => {
            const tag = cell.tagName.toLowerCase() === "th" ? "th" : "td";
            const text = escapeHtml(normalizeClipboardCellText(cell.textContent || ""));
            return `<${tag}>${text}</${tag}>`;
        })
            .join("");
        return `<tr>${cells}</tr>`;
    })
        .join("");

    return `<table border="1" cellspacing="0" cellpadding="4">${rowHtml}</table>`;
}

async function copyResultTable(table, button) {
    const plainText = buildTablePlainText(table);
    const htmlText = buildTableHtmlForClipboard(table);
    const labelSpan = button.querySelector("span");
    const defaultLabel = "複製";

    try {
        if ((navigator.clipboard === null || navigator.clipboard === void 0 ? void 0 : navigator.clipboard.write) && typeof ClipboardItem !== "undefined") {
            const item = new ClipboardItem({
                "text/plain": new Blob([plainText], { type: "text/plain" }),
                "text/html": new Blob([htmlText], { type: "text/html" })
            });
            await navigator.clipboard.write([item]);
        }
        else if (navigator.clipboard === null || navigator.clipboard === void 0 ? void 0 : navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(plainText);
        }
        else {
            const tempInput = document.createElement("textarea");
            tempInput.value = plainText;
            tempInput.setAttribute("readonly", "readonly");
            tempInput.style.position = "absolute";
            tempInput.style.left = "-9999px";
            document.body.appendChild(tempInput);
            tempInput.select();
            document.execCommand("copy");
            document.body.removeChild(tempInput);
        }

        if (labelSpan) {
            labelSpan.textContent = "已複製";
            window.setTimeout(() => {
                labelSpan.textContent = defaultLabel;
            }, 1200);
        }
    }
    catch {
        if (labelSpan) {
            labelSpan.textContent = "失敗";
            window.setTimeout(() => {
                labelSpan.textContent = defaultLabel;
            }, 1200);
        }
    }
}

function initializeResultTableCopyButtons() {
    const conversationList = document.getElementById("conversationList");
    if (!conversationList) {
        return;
    }

    conversationList.addEventListener("click", async (event) => {
        const target = event.target;
        const button = target === null || target === void 0 ? void 0 : target.closest("button[data-copy-result-table]");
        if (!button) {
            return;
        }

        const bubble = button.closest(".message-bubble");
        const table = bubble === null || bubble === void 0 ? void 0 : bubble.querySelector("table");
        if (!table) {
            return;
        }

        await copyResultTable(table, button);
    });
}
/**
 * 初始化按鈕事件。
 */
function initializePage() {
    initializeDebugPanel();
    initializeDebugCopyButtons();
    initializeResultTableCopyButtons();
    initializeQueryRail();
    setSelectedModel(null);
    setQuerySummary(null);
    void loadLlmProviderOptions();
    const queryButton = document.getElementById("queryButton");
    const queryInput = document.getElementById("queryInput");
    const imageUploadButton = document.getElementById("imageUploadButton");
    const imageUploadInput = document.getElementById("imageUploadInput");
    const fileUploadButton = document.getElementById("fileUploadButton");
    const fileUploadInput = document.getElementById("fileUploadInput");
    const uploadedFileList = document.getElementById("uploadedFileList");
    const voiceInputButton = document.getElementById("voiceInputButton");
    const llmProviderGroup = getLlmProviderGroup();
    const statusText = document.getElementById("statusText");
    const newQueryButton = document.getElementById("newQueryButton");
    const policyQueryButton = document.getElementById("policyQueryButton");
    const todoListButton = document.getElementById("todoListButton");
    const historyQueryButton = document.getElementById("historyQueryButton");
    const historyPanelElement = document.getElementById("conversationHistoryPanel");
    const todoPanelElement = document.getElementById("todoListPanel");
    const historyListElement = document.getElementById("conversationHistoryList");
    const historyEmptyElement = document.getElementById("conversationHistoryEmpty");
    const historyMetaElement = document.getElementById("conversationHistoryMeta");
    if (!queryButton || !queryInput || !llmProviderGroup || !statusText) {
        return;
    }

    const uploadedFiles = [];

    const getUploadedFileKey = (file) => `${file.name}__${file.size}__${file.lastModified}`;

    const getFileExtension = (fileName) => {
        const normalizedName = (fileName || "").toString().trim();
        if (!normalizedName || !normalizedName.includes(".")) {
            return "FILE";
        }

        const parts = normalizedName.split(".");
        const extension = (parts[parts.length - 1] || "").trim().toUpperCase();
        return extension || "FILE";
    };

    const renderUploadedFileList = () => {
        if (!(uploadedFileList instanceof HTMLElement)) {
            return;
        }

        // 依需求：上傳後不顯示檔案圖示/檔案 chip。
        uploadedFileList.innerHTML = "";
    };

    const appendUploadedFiles = (files) => {
        if (!Array.isArray(files) || files.length === 0) {
            return;
        }

        let addedCount = 0;
        for (const file of files) {
            if (!(file instanceof File)) {
                continue;
            }

            const fileKey = getUploadedFileKey(file);
            const exists = uploadedFiles.some((item) => item.key === fileKey);
            if (exists) {
                continue;
            }

            uploadedFiles.push({ key: fileKey, file });
            addedCount += 1;
        }

        renderUploadedFileList();

        if (addedCount > 0) {
            statusText.textContent = `已接收 ${uploadedFiles.length} 個檔案`;
        }
    };

    const resolveDocumentTypeByPromptMode = (promptMode) => {
        if (promptMode === PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT) {
            return "claimApplication";
        }

        return "unknown";
    };

    const normalizeRawJsonText = (rawJson) => {
        let text = (rawJson || "").toString().trim();
        text = text.replace(/^```(?:json)?\s*/i, "");
        text = text.replace(/\s*```$/i, "");
        text = text.trim();

        const firstBrace = text.indexOf("{");
        const lastBrace = text.lastIndexOf("}");
        if (firstBrace >= 0 && lastBrace > firstBrace) {
            text = text.slice(firstBrace, lastBrace + 1);
        }

        return text;
    };

    const resolveStructuredPayload = (parsed, fileName) => {
        if (!parsed || typeof parsed !== "object") {
            return {
                fileName,
                error: "結構化抽取結果為空"
            };
        }

        const simpleTitle = (parsed.Title ?? parsed.title ?? "").toString().trim();
        if (simpleTitle) {
            return {
                Title: simpleTitle
            };
        }

        const structuredData = parsed.StructuredData || parsed.structuredData;
        if (structuredData && typeof structuredData === "object") {
            return structuredData;
        }

        const rawJson = parsed.RawJson || parsed.rawJson;
        if (typeof rawJson === "string" && rawJson.trim()) {
            try {
                const normalized = normalizeRawJsonText(rawJson);
                if (normalized) {
                    return JSON.parse(normalized);
                }
            }
            catch {
                // Keep fallback payload below.
            }
        }

        return {
            fileName,
            documentType: parsed.DocumentType || parsed.documentType || "unknown",
            error: parsed.ParseError || parsed.parseError || "無法取得結構化資料",
            rawJson: typeof rawJson === "string" ? rawJson : null
        };
    };

    const fillQueryInputByPdfFiles = async (files, sessionId, llmProvider, documentType) => {
        const pdfFiles = (Array.isArray(files) ? files : [])
            .filter((file) => file instanceof File)
            .filter((file) => (file.name || "").toLowerCase().endsWith(".pdf"));

        if (pdfFiles.length === 0) {
            return;
        }

        const debugItems = [];
        const uploadedTitles = [];
        for (const pdfFile of pdfFiles) {
            const jsonString = await extractPdfToJsonString(pdfFile, sessionId, llmProvider, documentType);
            let parsed;
            try {
                parsed = JSON.parse(jsonString);
            }
            catch {
                parsed = {
                    fileName: pdfFile.name,
                    payload: jsonString
                };
            }

            const resolvedStructuredPayload = resolveStructuredPayload(parsed, pdfFile.name);

            const step1 = (parsed === null || parsed === void 0 ? void 0 : parsed.Step1) || (parsed === null || parsed === void 0 ? void 0 : parsed.step1) || null;
            const rawText = (step1 === null || step1 === void 0 ? void 0 : step1.ExtractedText)
                || (step1 === null || step1 === void 0 ? void 0 : step1.extractedText)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.RawText)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.rawText)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.FullText)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.fullText)
                || "";
            const structuredText = (step1 === null || step1 === void 0 ? void 0 : step1.Structured)
                || (step1 === null || step1 === void 0 ? void 0 : step1.structured)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.StructuredData)
                || (parsed === null || parsed === void 0 ? void 0 : parsed.structuredData)
                || resolvedStructuredPayload;
            const structuredTextWithTodoInfo = enrichStructuredPayloadWithTodoInfo(structuredText, currentTodoInfoItems);

            const structuredTitle = extractStructuredDocumentTitle(structuredTextWithTodoInfo);
            if (structuredTitle) {
                uploadedTitles.push(structuredTitle);
            }

            debugItems.push({
                fileName: pdfFile.name,
                title: structuredTitle || "",
                rawText,
                structuredText: structuredTextWithTodoInfo
            });
        }

        currentUploadedDocumentTitles = Array.from(new Set([
            ...currentUploadedDocumentTitles,
            ...uploadedTitles.map((title) => title.trim()).filter((title) => title.length > 0)
        ]));

        const rawTextContent = buildUploadedDocumentDebugDisplay(debugItems, (item) => item.rawText);
        const structuredTextContent = buildUploadedDocumentDebugDisplay(debugItems, (item) => item.structuredText);
        appendUploadedDocumentDebug(rawTextContent, structuredTextContent);

        const uploadedInputPayload = debugItems
            .map((item) => {
                const normalizedTitle = (item.title || "").toString().trim();
                const normalizedRawText = (item.rawText || "").toString().trim();

                if (!normalizedTitle && !normalizedRawText) {
                    return "";
                }

                return normalizeDebugDisplayText({
                    Title: normalizedTitle,
                    RawText: normalizedRawText
                });
            })
            .filter((text) => !!text)
            .join("\n\n");

        queryInput.value = uploadedInputPayload;
        syncQueryButtonState();
    };

    const removeUploadedFile = (fileKey) => {
        const normalizedKey = (fileKey || "").toString();
        if (!normalizedKey) {
            return;
        }

        const targetIndex = uploadedFiles.findIndex((item) => item.key === normalizedKey);
        if (targetIndex < 0) {
            return;
        }

        uploadedFiles.splice(targetIndex, 1);
        currentUploadedDocumentTitles = [];
        renderUploadedFileList();
        queryInput.value = "";
        syncQueryButtonState();
        appendUploadedDocumentDebug("", "");
        statusText.textContent = uploadedFiles.length > 0 ? `已上傳 ${uploadedFiles.length} 個檔案` : "待命中";
    };

    if (fileUploadButton instanceof HTMLButtonElement && fileUploadInput instanceof HTMLInputElement) {
        fileUploadButton.addEventListener("click", () => {
            fileUploadInput.click();
        });

        fileUploadInput.addEventListener("change", async () => {
            const files = fileUploadInput.files;
            if (!files || files.length === 0) {
                return;
            }

            const selectedFiles = Array.from(files);
            appendUploadedFiles(selectedFiles);

            try {
                statusText.textContent = "PDF 解析中...";
                if (!insuranceChatSessionId) {
                    insuranceChatSessionId = (window.crypto && typeof window.crypto.randomUUID === "function")
                        ? window.crypto.randomUUID()
                        : `chat-${Date.now()}`;
                }
                const selectedProvider = getSelectedLlmProvider();
                const promptMode = getSelectedPromptTemplateMode();
                const documentType = resolveDocumentTypeByPromptMode(promptMode);
                await fillQueryInputByPdfFiles(selectedFiles, insuranceChatSessionId, selectedProvider, documentType);
                if (selectedFiles.some((file) => (file.name || "").toLowerCase().endsWith(".pdf"))) {
                    statusText.textContent = "已將 PDF 結構化抽取為 JSON，自動送出查詢中...";
                    if (queryInput.value.trim().length > 0) {
                        await executeQuery();
                    }
                }
            }
            catch (error) {
                const message = error instanceof Error ? error.message : "PDF 解析失敗";
                statusText.textContent = message;
                appendUploadedDocumentDebug("", `PDF 解析失敗: ${message}`);
                simpleAlert(message);
            }

            fileUploadInput.value = "";
        });
    }

    if (imageUploadButton instanceof HTMLButtonElement && imageUploadInput instanceof HTMLInputElement) {
        imageUploadButton.addEventListener("click", () => {
            imageUploadInput.click();
        });

        imageUploadInput.addEventListener("change", async () => {
            const files = imageUploadInput.files;
            if (!files || files.length === 0) {
                return;
            }

            const selectedFile = files[0];
            const normalizedMimeType = (selectedFile.type || "").trim() || "application/octet-stream";
            if (!normalizedMimeType.toLowerCase().startsWith("image/")) {
                statusText.textContent = "請選擇影像檔案";
                imageUploadInput.value = "";
                return;
            }

            try {
                const imageProcessFlag = getImageProcessFlag();
                statusText.textContent = imageProcessFlag === "B" ? "影像掃描中..." : "影像分析中...";
                if (!insuranceChatSessionId) {
                    insuranceChatSessionId = (window.crypto && typeof window.crypto.randomUUID === "function")
                        ? window.crypto.randomUUID()
                        : `chat-${Date.now()}`;
                }
                const selectedProvider = getSelectedLlmProvider();
                const imageBase64 = await readFileAsBase64(selectedFile);
                const result = await analyzeImageByBase64(
                    imageBase64,
                    normalizedMimeType,
                    selectedFile.name || "image",
                    selectedProvider,
                    insuranceChatSessionId,
                    imageProcessFlag
                );
                const imageDocumentTitle = ((result.documentTitle || result.document_title) || "").toString().trim()
                    || extractImageUploadDocumentTitle(result.result, selectedFile.name || "image");
                currentUploadedDocumentTitles = Array.from(new Set([
                    ...currentUploadedDocumentTitles,
                    imageDocumentTitle
                ].map((title) => (title || "").toString().trim()).filter((title) => title.length > 0)));
                queryInput.value = (result.result || "").toString().trim();
                syncQueryButtonState();
                statusText.textContent = imageProcessFlag === "B"
                    ? "影像掃描完成，準備送出查詢中..."
                    : "影像分析完成，準備送出查詢中...";
                if (queryInput.value.trim().length > 0) {
                    await executeQuery();
                }
            }
            catch (error) {
                const message = error instanceof Error ? error.message : "影像分析失敗";
                statusText.textContent = message;
                simpleAlert(message);
            }

            imageUploadInput.value = "";
        });
    }

    if (uploadedFileList instanceof HTMLElement) {
        uploadedFileList.addEventListener("click", (event) => {
            const target = event.target;
            const removeButton = target === null || target === void 0 ? void 0 : target.closest("button[data-remove-uploaded-file]");
            if (!(removeButton instanceof HTMLButtonElement)) {
                return;
            }

            const fileKey = (removeButton.dataset.removeUploadedFile || "").trim();
            removeUploadedFile(fileKey);
        });
    }

    const SpeechRecognitionConstructor = window.SpeechRecognition || window.webkitSpeechRecognition;
    let speechRecognition = null;
    let isVoiceRecording = false;
    let voiceBaseText = "";
    let voiceFinalText = "";
    let voiceSubmitAfterStop = false;
    let voiceAutoSubmitTimerId = null;
    const voiceAutoSubmitDelayMs = 5000;

    const containsGoCommand = (text) => /\bgo\b/i.test(text || "");

    const removeGoCommand = (text) => (text || "")
        .replace(/\bgo\b/ig, " ")
        .replace(/\s{2,}/g, " ")
        .trim();

    const removeTrailingGoCommand = (text) => (text || "")
        .replace(/(?:\s|^)(?:go(?:[\s。.!?！？、，,]|$))+$/i, " ")
        .replace(/\s{2,}/g, " ")
        .trim();

    const syncVoiceButtonState = (recording) => {
        if (!(voiceInputButton instanceof HTMLButtonElement)) {
            return;
        }

        voiceInputButton.classList.toggle("is-recording", recording);
        voiceInputButton.title = recording ? "停止語音輸入" : "開始語音輸入";
        voiceInputButton.setAttribute("aria-label", recording ? "停止語音輸入" : "語音輸入");
    };

    const appendVoiceTextToInput = (interimText) => {
        const mergedText = [voiceBaseText, voiceFinalText, interimText]
            .filter((part) => part && part.trim().length > 0)
            .join(" ")
            .trim();

        queryInput.value = mergedText;
    };

    const clearVoiceAutoSubmitTimer = () => {
        if (voiceAutoSubmitTimerId !== null) {
            window.clearTimeout(voiceAutoSubmitTimerId);
            voiceAutoSubmitTimerId = null;
        }
    };

    const scheduleVoiceAutoSubmit = () => {
        if (!isVoiceRecording || !speechRecognition) {
            return;
        }

        clearVoiceAutoSubmitTimer();
        voiceAutoSubmitTimerId = window.setTimeout(() => {
            if (!isVoiceRecording || !speechRecognition) {
                return;
            }

            voiceSubmitAfterStop = queryInput.value.trim().length > 0;
            speechRecognition.stop();
        }, voiceAutoSubmitDelayMs);
    };

    if (voiceInputButton instanceof HTMLButtonElement) {
        if (typeof SpeechRecognitionConstructor !== "function") {
            voiceInputButton.disabled = true;
            voiceInputButton.title = "目前瀏覽器不支援語音輸入";
            voiceInputButton.setAttribute("aria-label", "目前瀏覽器不支援語音輸入");
        }
        else {
            speechRecognition = new SpeechRecognitionConstructor();
            speechRecognition.lang = "zh-TW";
            speechRecognition.interimResults = true;
            speechRecognition.continuous = true;

            speechRecognition.addEventListener("start", () => {
                isVoiceRecording = true;
                syncVoiceButtonState(true);
                scheduleVoiceAutoSubmit();
            });

            speechRecognition.addEventListener("result", (event) => {
                let rebuiltFinalTranscript = "";
                let interimTranscript = "";

                // Rebuild transcripts from the current recognition snapshot to avoid duplicate accumulation.
                for (let index = 0; index < event.results.length; index++) {
                    const transcript = (event.results[index][0]?.transcript || "").trim();
                    if (!transcript) {
                        continue;
                    }

                    if (event.results[index].isFinal) {
                        rebuiltFinalTranscript = [rebuiltFinalTranscript, transcript]
                            .filter((part) => part && part.trim().length > 0)
                            .join(" ")
                            .trim();
                    }
                    else {
                        interimTranscript = [interimTranscript, transcript]
                            .filter((part) => part && part.trim().length > 0)
                            .join(" ")
                            .trim();
                    }
                }

                voiceFinalText = rebuiltFinalTranscript;

                appendVoiceTextToInput(interimTranscript);
                syncQueryButtonState();
                scheduleVoiceAutoSubmit();

                if (!voiceSubmitAfterStop && containsGoCommand(queryInput.value)) {
                    const queryWithoutCommand = removeGoCommand(queryInput.value);
                    queryInput.value = queryWithoutCommand;
                    voiceBaseText = queryWithoutCommand;
                    voiceFinalText = "";
                    voiceSubmitAfterStop = true;
                    syncQueryButtonState();

                    if (isVoiceRecording && speechRecognition) {
                        speechRecognition.stop();
                    }
                }
            });

            speechRecognition.addEventListener("end", () => {
                isVoiceRecording = false;
                clearVoiceAutoSubmitTimer();
                appendVoiceTextToInput("");
                syncVoiceButtonState(false);
                syncQueryButtonState();

                if (voiceSubmitAfterStop) {
                    voiceSubmitAfterStop = false;
                    void executeQuery();
                }
            });

            speechRecognition.addEventListener("error", (event) => {
                clearVoiceAutoSubmitTimer();
                if (event.error === "not-allowed") {
                    simpleAlert("請允許瀏覽器使用麥克風權限。");
                }
                else if (event.error !== "aborted" && event.error !== "no-speech") {
                    simpleAlert("語音輸入失敗，請稍後再試。");
                }
            });

            voiceInputButton.addEventListener("click", () => {
                if (!speechRecognition) {
                    return;
                }

                if (isVoiceRecording) {
                    clearVoiceAutoSubmitTimer();
                    speechRecognition.stop();
                    return;
                }

                voiceBaseText = queryInput.value.trim();
                voiceFinalText = "";
                voiceSubmitAfterStop = false;
                try {
                    speechRecognition.start();
                }
                catch (_error) {
                    // Ignore repeated start exceptions when the engine is still transitioning state.
                }
            });
        }
    }

    const syncQueryButtonState = () => {
        if (queryButton.dataset.busy === "true") {
            queryButton.disabled = true;
            return;
        }

        queryButton.disabled = queryInput.value.trim().length === 0;
    };

    const syncHistoryButtonVisibilityByMode = () => {
        if (!(historyQueryButton instanceof HTMLButtonElement)) {
            return;
        }

        const mode = getSelectedPromptTemplateMode();
        const canOpenConversationHistory = isConversationHistoryEnabledMode(mode);
        const canQueryPolicies = mode === PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT;
        const canOpenTodoList = mode === PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT;

        historyQueryButton.hidden = !canOpenConversationHistory;
        if (policyQueryButton instanceof HTMLButtonElement) {
            policyQueryButton.hidden = !canQueryPolicies;
        }
        if (todoListButton instanceof HTMLButtonElement) {
            todoListButton.hidden = !canOpenTodoList;
        }

        if (queryShell) {
            queryShell.classList.toggle("policy-query-enabled", canQueryPolicies);
        }

        if (!canOpenConversationHistory) {
            const historyPanelElement = document.getElementById("conversationHistoryPanel");
            if (historyPanelElement && window.bootstrap) {
                window.bootstrap.Offcanvas.getOrCreateInstance(historyPanelElement).hide();
            }
            setConversationHistoryPanelOpenState(false);
        }

        if (!canOpenTodoList) {
            const todoPanelElement = document.getElementById("todoListPanel");
            if (todoPanelElement && window.bootstrap) {
                window.bootstrap.Offcanvas.getOrCreateInstance(todoPanelElement).hide();
            }
            setConversationHistoryPanelOpenState(false);
        }
    };

    const queryShell = document.querySelector(".query-shell");

    const openHistoryPanel = () => {
        if (historyPanelElement && window.bootstrap) {
            window.bootstrap.Offcanvas.getOrCreateInstance(historyPanelElement).show();
        }
    };

    const closeHistoryPanel = () => {
        if (historyPanelElement && window.bootstrap) {
            window.bootstrap.Offcanvas.getOrCreateInstance(historyPanelElement).hide();
        }
    };

    const openTodoPanel = () => {
        if (todoPanelElement && window.bootstrap) {
            window.bootstrap.Offcanvas.getOrCreateInstance(todoPanelElement).show();
        }
    };

    const setHistoryPanelOpenState = (isOpen) => {
        if (!queryShell) {
            return;
        }

        queryShell.classList.toggle("history-panel-open", isOpen);
    };

    const renderHistoryList = (items) => {
        if (!historyListElement || !historyEmptyElement) {
            return;
        }

        historyListElement.innerHTML = "";
        const normalizedItems = Array.isArray(items) ? items : [];
        historyEmptyElement.style.display = normalizedItems.length > 0 ? "none" : "block";
        for (const item of normalizedItems) {
            const fileName = (((item === null || item === void 0 ? void 0 : item.fileName) || "").toString().trim());
            const description = (((item === null || item === void 0 ? void 0 : item.description) || (item === null || item === void 0 ? void 0 : item.fileName) || "-").toString());
            const row = document.createElement("div");
            row.className = "conversation-history-item-row";

            const openButton = document.createElement("button");
            openButton.type = "button";
            openButton.className = "list-group-item list-group-item-action conversation-history-item";
            openButton.dataset.historyFileName = fileName;
            openButton.innerHTML = `<div class="conversation-history-item-title">${escapeHtml(description)}</div>`;

            const deleteButton = document.createElement("button");
            deleteButton.type = "button";
            deleteButton.className = "conversation-history-delete-btn";
            deleteButton.dataset.deleteHistoryFileName = fileName;
            deleteButton.setAttribute("aria-label", `刪除對話紀錄 ${description}`);
            deleteButton.setAttribute("title", "刪除對話紀錄");
            deleteButton.textContent = "X";

            row.appendChild(openButton);
            row.appendChild(deleteButton);
            historyListElement.appendChild(row);
        }
    };

    const loadConversationHistory = async (fileName) => {
        const normalizedFileName = (fileName || "").toString().trim();
        if (!normalizedFileName) {
            return;
        }

        const response = await getInsuranceChatHistory(normalizedFileName);
        replaceTodoState(response.todo_info || response.todoInfo, response.todo_document || response.todoDocument);
        currentUploadedHistoryFiles = Array.isArray(response.uploaded_files || response.uploadedFiles) ? (response.uploaded_files || response.uploadedFiles) : [];
        populateConversationFromHistory(response.conversations);
        insuranceChatSessionId = (response.sessionId || "").toString().trim() || null;
        selectedHistoryFileName = (response.fileName || normalizedFileName).toString().trim();
        queryInput.value = "";
        queryInput.focus();
        statusText.textContent = "已載入對話紀錄";
        syncQueryButtonState();
        closeHistoryPanel();
    };

    const refreshConversationHistoryList = async () => {
        if (!historyListElement || !historyEmptyElement) {
            return;
        }

        let currentUser = getStorageUser();
        if ((!currentUser || !currentUser.userId) && window.syncAuthStateFromServer) {
            currentUser = await window.syncAuthStateFromServer();
        }

        historyEmptyElement.style.display = "block";
        historyEmptyElement.textContent = "載入中...";
        historyListElement.innerHTML = "";
        if (historyMetaElement) {
            const currentUserId = ((currentUser === null || currentUser === void 0 ? void 0 : currentUser.userId) || "未登入").toString().trim() || "未登入";
            historyMetaElement.textContent = `目前登入者：${currentUserId}`;
        }
        try {
            const items = await listInsuranceChatHistories();
            renderHistoryList(items);
            if (historyMetaElement) {
                const currentUserId = ((currentUser === null || currentUser === void 0 ? void 0 : currentUser.userId) || "未登入").toString().trim() || "未登入";
                historyMetaElement.textContent = `目前登入者：${currentUserId}，找到 ${items.length} 筆對話紀錄`;
            }
            historyEmptyElement.textContent = "目前沒有可載入的對話紀錄。";
        }
        catch (error) {
            console.error("listInsuranceChatHistories failed", error);
            historyEmptyElement.textContent = buildQueryErrorMessage(error);
        }
    };

    queryButton.dataset.busy = "false";
    syncQueryButtonState();
    queryInput.addEventListener("input", syncQueryButtonState);

    const promptModeRadios = document.querySelectorAll('input[name="promptTemplateMode"]');
    for (const radio of promptModeRadios) {
        radio.addEventListener("change", syncHistoryButtonVisibilityByMode);
    }
    syncHistoryButtonVisibilityByMode();

    if (newQueryButton) {
        newQueryButton.addEventListener("click", () => {
            const previousSessionId = insuranceChatSessionId;
            insuranceChatSessionId = null;
            selectedHistoryFileName = null;
            void resetInsuranceChatSession(previousSessionId || "");
            queryInput.value = "";
            if (fileUploadInput instanceof HTMLInputElement) {
                fileUploadInput.value = "";
            }
            uploadedFiles.length = 0;
            renderUploadedFileList();
            queryInput.focus();
            statusText.textContent = "待命中";
            setSelectedModel(null);
            resetQueryView(true);
            setQuerySummary(null);
            resetTodoState();
            currentUploadedDocumentTitles = [];
            currentUploadedHistoryFiles = [];
            voiceBaseText = "";
            voiceFinalText = "";
            voiceSubmitAfterStop = false;
            if (isVoiceRecording && speechRecognition) {
                clearVoiceAutoSubmitTimer();
                speechRecognition.stop();
            }
            syncQueryButtonState();
        });
    }

    if (historyQueryButton) {
        historyQueryButton.addEventListener("click", () => {
            void refreshConversationHistoryList();
            if (!historyPanelElement || !window.bootstrap) {
                openHistoryPanel();
            }
        });
    }

    if (historyPanelElement) {
        historyPanelElement.addEventListener("show.bs.offcanvas", () => {
            setHistoryPanelOpenState(true);
            void refreshConversationHistoryList();
        });

        historyPanelElement.addEventListener("hidden.bs.offcanvas", () => {
            setHistoryPanelOpenState(false);
        });
    }

    if (todoListButton instanceof HTMLButtonElement) {
        todoListButton.addEventListener("click", () => {
            renderTodoListPanel();
            if (!todoPanelElement || !window.bootstrap) {
                openTodoPanel();
            }
        });
    }

    if (todoPanelElement) {
        todoPanelElement.addEventListener("show.bs.offcanvas", () => {
            setHistoryPanelOpenState(true);
            renderTodoListPanel();
        });

        todoPanelElement.addEventListener("hidden.bs.offcanvas", () => {
            setHistoryPanelOpenState(false);
        });
    }

    const todoDocumentList = document.getElementById("todoDocumentList");
    if (todoDocumentList instanceof HTMLElement) {
        todoDocumentList.addEventListener("click", (event) => {
            const target = event.target;
            const button = target === null || target === void 0 ? void 0 : target.closest("button[data-view-uploaded-file]");
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            const storedFileName = (button.dataset.viewUploadedFile || "").trim();
            if (!storedFileName || !selectedHistoryFileName) {
                return;
            }

            const baseUrl = getApiBaseUrl();
            const promptTemplateMode = getSelectedPromptTemplateMode();
            const url = `${baseUrl}/api/insurance/chat/histories/${encodeURIComponent(selectedHistoryFileName)}/uploaded-files/${encodeURIComponent(storedFileName)}?promptTemplateMode=${encodeURIComponent(promptTemplateMode)}`;
            window.open(url, "_blank", "noopener");
        });
    }

    if (historyListElement) {
        historyListElement.addEventListener("click", (event) => {
            const target = event.target;

            const deleteButton = target === null || target === void 0 ? void 0 : target.closest("button[data-delete-history-file-name]");
            if (deleteButton instanceof HTMLButtonElement) {
                const deleteFileName = (deleteButton.dataset.deleteHistoryFileName || "").trim();
                if (!deleteFileName) {
                    return;
                }

                void deleteInsuranceChatHistory(deleteFileName)
                    .then(async () => {
                        if (selectedHistoryFileName && selectedHistoryFileName === deleteFileName) {
                            selectedHistoryFileName = null;
                            insuranceChatSessionId = null;
                        }

                        await refreshConversationHistoryList();
                        simpleAlert("已刪除對話紀錄");
                    })
                    .catch((error) => {
                        console.error("deleteInsuranceChatHistory failed", error);
                        simpleAlert(buildQueryErrorMessage(error));
                    });
                return;
            }

            const button = target === null || target === void 0 ? void 0 : target.closest("button[data-history-file-name]");
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            const fileName = (button.dataset.historyFileName || "").trim();
            if (!fileName) {
                return;
            }

            void loadConversationHistory(fileName).catch((error) => {
                console.error("getInsuranceChatHistory failed", error);
                simpleAlert(buildQueryErrorMessage(error));
            });
        });
    }

    const tryQueryByPolicySelection = async (policyButton) => {
        if (!(policyButton instanceof HTMLButtonElement)) {
            return;
        }

        const parentBubble = policyButton.closest(".message-bubble");
        const policy = {
            policyNo: (policyButton.dataset.policyNo || "").trim(),
            insuredName: (policyButton.dataset.insuredName || "").trim(),
            plateNo: (policyButton.dataset.plateNo || "").trim(),
            insureTypeList: (policyButton.dataset.insureTypeList || "").trim()
        };

        if (!policy.policyNo) {
            return;
        }

        updatePolicyListMessageSelection(parentBubble, policy.policyNo);
        queryInput.value = buildPolicyQuerySentence(policy);
        statusText.textContent = "已帶入保單資訊，準備查詢";
        syncQueryButtonState();
        await executeQuery();
    };

    const conversationList = document.getElementById("conversationList");
    if (conversationList) {
        conversationList.addEventListener("click", (event) => {
            const target = event.target;
            const button = target === null || target === void 0 ? void 0 : target.closest("button.policy-item-btn");
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            void tryQueryByPolicySelection(button).catch((error) => {
                const message = buildQueryErrorMessage(error);
                appendErrorResultMessage(message);
                statusText.textContent = message;
            });
        });
    }

    const executeQuery = async () => {
        if (queryButton.dataset.busy === "true") {
            return;
        }

        const rawQuery = queryInput.value.trim();
        const query = removeTrailingGoCommand(rawQuery);
        if (query !== rawQuery) {
            queryInput.value = query;
        }

        if (!query) {
            simpleAlert("請先輸入查詢內容。");
            return;
        }
        let currentUser = getStorageUser();
        if (!currentUser && window.syncAuthStateFromServer) {
            currentUser = await window.syncAuthStateFromServer();
        }
        if (isVoiceRecording && speechRecognition) {
            clearVoiceAutoSubmitTimer();
            speechRecognition.stop();
        }
        queryButton.dataset.busy = "true";
        syncQueryButtonState();
        statusText.textContent = "查詢中...";
        setSelectedModel(null);
        resetQueryView(false);
        appendQueryMessage(query);
        try {
            if (!insuranceChatSessionId) {
                insuranceChatSessionId = (window.crypto && typeof window.crypto.randomUUID === "function")
                    ? window.crypto.randomUUID()
                    : `chat-${Date.now()}`;
            }

            const selectedProvider = getSelectedLlmProvider();
            const promptTemplateMode = getSelectedPromptTemplateMode();
            const response = await queryInsuranceChat(insuranceChatSessionId, query, selectedProvider, promptTemplateMode);
            const reply = (response.reply || "").toString().trim();
            appendSystemPromptDebug((response.systemPrompt || "").toString());
            appendStoryScriptDebug((response.storyScript || "").toString());
            appendLlmLogErrorsDebug(response.llmLogErrors || response.llm_log_errors || []);
            appendLlmResultDebug(response);
            if (!reply) {
                simpleAlert("無有效回傳資料。");
                statusText.textContent = "查詢完成（無資料）";
                appendMessage("<h2 class=\"message-title\">回覆</h2><p class=\"mb-0\">無有效回傳資料。</p>", "message-result", "left");
                return;
            }

            const usedModel = ((response === null || response === void 0 ? void 0 : response.llmModel) || (response === null || response === void 0 ? void 0 : response.model) || "").toString().trim();
            const usedProvider = ((response === null || response === void 0 ? void 0 : response.llmProvider) || selectedProvider || "GoogleGemini").toString().trim();
            // 以最近一次 LLM 回傳為準，避免跨輪次殘留舊待辦項目。
            replaceTodoState(response.todo_info || response.todoInfo || [], response.todo_document || response.todoDocument || []);
            const responseHistoryFileName = ((response === null || response === void 0 ? void 0 : response.historyFileName) || (response === null || response === void 0 ? void 0 : response.history_file_name) || "").toString().trim();
            if (responseHistoryFileName) {
                selectedHistoryFileName = responseHistoryFileName;
            }
            currentUploadedHistoryFiles = Array.isArray((response === null || response === void 0 ? void 0 : response.uploadedFiles) || (response === null || response === void 0 ? void 0 : response.uploaded_files))
                ? (((response === null || response === void 0 ? void 0 : response.uploadedFiles) || (response === null || response === void 0 ? void 0 : response.uploaded_files)))
                : [];
            setSelectedModel(usedModel || usedProvider);
            setQuerySummary(reply);
            appendChatReplyMessage(reply);
            try {
                await playReplyVoice(reply);
            }
            catch (voiceError) {
                console.error("playReplyVoice failed", voiceError);
                setVoiceOutputStatus("語音輸出失敗");
            }
            statusText.textContent = "查詢完成";
            console.log("queryInsuranceChat success", response);
        }
        catch (error) {
            if ((error === null || error === void 0 ? void 0 : error.responseData) && error.responseData.code === 401 && window.syncAuthStateFromServer) {
                await window.syncAuthStateFromServer();
            }
            const message = buildQueryErrorMessage(error);
            console.error("queryInsuranceChat failed", error);
            appendLlmResultDebug((error === null || error === void 0 ? void 0 : error.responseData) || { message });
            appendLlmLogErrorsDebug(((error === null || error === void 0 ? void 0 : error.responseData) === null || (error === null || error === void 0 ? void 0 : error.responseData) === void 0 ? void 0 : error.responseData.llmLogErrors) || ((error === null || error === void 0 ? void 0 : error.responseData) === null || (error === null || error === void 0 ? void 0 : error.responseData) === void 0 ? void 0 : error.responseData.llm_log_errors) || []);
            appendErrorResultMessage(message);
            statusText.textContent = message;
        }
        finally {
            queryInput.value = "";
            queryButton.dataset.busy = "false";
            syncQueryButtonState();
            scrollResultAndFocusCurrentQuery();
        }
    };

    queryButton.addEventListener("click", () => {
        void executeQuery();
    });

    if (policyQueryButton instanceof HTMLButtonElement) {
        policyQueryButton.addEventListener("click", () => {
            const mode = getSelectedPromptTemplateMode();
            if (mode !== PROMPT_TEMPLATE_MODE_CLAIM_ASSISTANT) {
                simpleAlert("僅理賠助理模式可查詢保單。");
                return;
            }

            statusText.textContent = "保單清單載入中...";
            void listInsurancePolicies()
                .then((policies) => {
                    appendPolicyListMessage(policies, "");
                    statusText.textContent = "請選取保單以自動送出查詢";
                })
                .catch((error) => {
                    const message = buildQueryErrorMessage(error);
                    appendErrorResultMessage(message);
                    statusText.textContent = message;
                });
        });
    }
}
document.addEventListener("DOMContentLoaded", initializePage);
document.addEventListener("DOMContentLoaded", initializeConversationHistoryPanel);
document.addEventListener("DOMContentLoaded", initializeTodoListPanel);
