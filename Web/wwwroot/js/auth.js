"use strict";
const AUTH_STORAGE_KEY = "textToSql.loginUser";
const LOGIN_ICON = `
<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false">
  <path d="M8 1a3 3 0 1 1 0 6 3 3 0 0 1 0-6zm-4 8a4 4 0 0 0-4 4v2h16v-2a4 4 0 0 0-4-4H4z"/>
</svg>`;
const LOGOUT_ICON = `
<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false">
  <path d="M6.5 13a.5.5 0 0 1-.5-.5V11H2V5h4V3.5a.5.5 0 0 1 .8-.4l4.5 3.5a.5.5 0 0 1 0 .8l-4.5 3.5a.5.5 0 0 1-.3.1z"/>
  <path d="M10 2h3a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1h-3v-1h3V3h-3V2z"/>
</svg>`;
function getApiBaseUrl() {
    const hidden = document.getElementById("apiBaseUrl");
    if ((hidden === null || hidden === void 0 ? void 0 : hidden.value.trim())) {
        return hidden.value.trim();
    }
    const body = document.body;
    return (body === null || body === void 0 ? void 0 : body.dataset.apiBaseUrl) || "https://localhost:7170";
}
function getStorageUser() {
    const raw = window.sessionStorage.getItem(AUTH_STORAGE_KEY);
    if (!raw) {
        return null;
    }
    try {
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.userId !== "string" || typeof parsed.userName !== "string") {
            return null;
        }
        return parsed;
    }
    catch {
        return null;
    }
}
function setStorageUser(user) {
    if (!user) {
        window.sessionStorage.removeItem(AUTH_STORAGE_KEY);
        return;
    }
    window.sessionStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(user));
}
function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}
function setLoginError(message) {
    const errorText = document.getElementById("loginErrorText");
    if (!errorText) {
        return;
    }
    if (!message) {
        errorText.textContent = "";
        errorText.classList.add("d-none");
        return;
    }
    errorText.innerHTML = escapeHtml(message);
    errorText.classList.remove("d-none");
}
function renderAuthState() {
    const user = getStorageUser();
    const userInfo = document.getElementById("authUserInfo");
    const userId = document.getElementById("authUserId");
    const userName = document.getElementById("authUserName");
    const actionButton = document.getElementById("authActionButton");
    const actionIcon = document.getElementById("authActionIcon");
    const actionText = document.getElementById("authActionText");
    if (!userInfo || !userId || !userName || !actionButton || !actionIcon || !actionText) {
        return;
    }
    if (user) {
        userInfo.classList.remove("d-none");
        userId.textContent = user.userId;
        userName.textContent = user.userName;
        actionText.textContent = "登出";
        actionIcon.innerHTML = LOGOUT_ICON;
        actionButton.setAttribute("aria-label", "登出");
    }
    else {
        userInfo.classList.add("d-none");
        userId.textContent = "";
        userName.textContent = "";
        actionText.textContent = "登入";
        actionIcon.innerHTML = LOGIN_ICON;
        actionButton.setAttribute("aria-label", "登入");
    }
}
async function syncAuthStateFromServer() {
    const baseUrl = getApiBaseUrl();
    try {
        const response = await fetch(`${baseUrl}/api/g1/auth/status`, {
            method: "GET",
            credentials: "include"
        });
        const data = await response.json();
        const user = response.ok && data.code === 0 ? data.data : null;
        setStorageUser(user || null);
        renderAuthState();
        return user || null;
    }
    catch {
        setStorageUser(null);
        renderAuthState();
        return null;
    }
}
async function submitLogin() {
    const input = document.getElementById("loginUserIdInput");
    const submitButton = document.getElementById("loginSubmitButton");
    if (!input || !submitButton) {
        return;
    }
    const userId = input.value.trim();
    if (!userId) {
        setLoginError("請輸入使用者ID。");
        return;
    }
    const baseUrl = getApiBaseUrl();
    submitButton.disabled = true;
    setLoginError("");
    try {
        const response = await fetch(`${baseUrl}/api/g1/auth/login`, {
            method: "POST",
            credentials: "include",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ userId })
        });
        const data = await response.json();
        if (!response.ok || data.code !== 0 || !data.data) {
            throw new Error(data.msg || "登入失敗");
        }
        setStorageUser(data.data);
        renderAuthState();
        const modalElement = document.getElementById("loginModal");
        if (modalElement && window.bootstrap) {
            window.bootstrap.Modal.getOrCreateInstance(modalElement).hide();
        }
    }
    catch (error) {
        const message = error instanceof Error ? error.message : "登入失敗";
        setLoginError(message);
    }
    finally {
        submitButton.disabled = false;
    }
}
async function logout() {
    const baseUrl = getApiBaseUrl();
    try {
        await fetch(`${baseUrl}/api/g1/auth/logout`, {
            method: "POST",
            credentials: "include"
        });
    }
    finally {
        setStorageUser(null);
        renderAuthState();
        setLoginError("");
        const input = document.getElementById("loginUserIdInput");
        if (input) {
            input.value = "";
        }
    }
}
function openLoginModal() {
    const modalElement = document.getElementById("loginModal");
    const input = document.getElementById("loginUserIdInput");
    if (!modalElement || !window.bootstrap) {
        return;
    }
    setLoginError("");
    if (input) {
        input.value = "";
        window.setTimeout(() => input.focus(), 150);
    }
    window.bootstrap.Modal.getOrCreateInstance(modalElement).show();
}
function initializeAuth() {
    renderAuthState();
    window.syncAuthStateFromServer = syncAuthStateFromServer;
    void syncAuthStateFromServer();
    const actionButton = document.getElementById("authActionButton");
    const submitButton = document.getElementById("loginSubmitButton");
    const input = document.getElementById("loginUserIdInput");
    if (actionButton) {
        actionButton.addEventListener("click", () => {
            const user = getStorageUser();
            if (user) {
                void logout();
                return;
            }
            openLoginModal();
        });
    }
    if (submitButton) {
        submitButton.addEventListener("click", submitLogin);
    }
    if (input) {
        input.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                submitLogin();
            }
        });
    }
    const modalElement = document.getElementById("loginModal");
    if (modalElement) {
        modalElement.addEventListener("hidden.bs.modal", () => setLoginError(""));
    }
    window.addEventListener("storage", (event) => {
        if (event.key === AUTH_STORAGE_KEY) {
            renderAuthState();
        }
    });
}
document.addEventListener("DOMContentLoaded", initializeAuth);
