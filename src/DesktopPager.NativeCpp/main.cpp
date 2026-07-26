#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <strsafe.h>
#include <tlhelp32.h>
#include <algorithm>
#include <string>
#include <cstdio>
#include "resource.h"

// DesktopPager nativo: sfoglia le "pagine" delle icone del desktop.
//
// Il motore NON sposta mai le icone: fa scorrere la vista della ListView
// del desktop con LVM_SCROLL. Il vecchio approccio a riposizionamento
// (LVM_SETITEMPOSITION32 con MAKELPARAM) passava coordinate dove la
// ListView si aspetta un puntatore a POINT: Explorer dereferenziava un
// puntatore non valido e crashava. Inoltre lo spostamento fisico e'
// incompatibile con la "disposizione automatica" delle icone.
//
// Nota chiave: il desktop ha lo stile LVS_NOSCROLL, che fa ignorare
// LVM_SCROLL. Va rimosso prima di scorrere e ripristinato quando si
// torna alla prima pagina, cosi' il desktop resta com'era.

namespace
{
constexpr UINT WM_TRAYICON = WM_APP + 1;

constexpr int HOTKEY_NEXT = 1;
constexpr int HOTKEY_PREV = 2;
constexpr int HOTKEY_HOME = 3;
constexpr int HOTKEY_RESTART_EXPLORER = 4;
constexpr int HOTKEY_FLIP_SCREEN = 5;     // Ctrl+Alt+Su: sottosopra
constexpr int HOTKEY_RESET_SCREEN = 6;    // Ctrl+Alt+Giu: normale
constexpr int HOTKEY_ROTATE_LEFT = 7;     // Ctrl+Alt+Sinistra
constexpr int HOTKEY_ROTATE_RIGHT = 8;    // Ctrl+Alt+Destra
constexpr int HOTKEY_PANIC_RESET = 9;     // Ctrl+Alt+Shift+^: emergenza

constexpr UINT MODIFIERS = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;

constexpr UINT IDM_NEXT = 1001;
constexpr UINT IDM_PREV = 1002;
constexpr UINT IDM_HOME = 1003;
constexpr UINT IDM_AUTOSTART = 1004;
constexpr UINT IDM_EXIT = 1005;
constexpr UINT IDM_RESTART_EXPLORER = 1006;
constexpr UINT IDM_ROTATE_180 = 1007;
constexpr UINT IDM_ROTATE_0 = 1008;
constexpr UINT IDM_ROTATE_90 = 1009;
constexpr UINT IDM_ROTATE_270 = 1010;

constexpr UINT LVM_SCROLL_MSG = LVM_FIRST + 20;
constexpr UINT LVM_GETITEMSPACING_MSG = LVM_FIRST + 51;
constexpr DWORD LVS_NOSCROLL_STYLE = 0x2000;
constexpr UINT SMTO_ABORT_IF_HUNG_FLAG = 0x0002;
constexpr UINT SEND_TIMEOUT_MS = 500;

// un delta enorme viene comunque limitato dal range: "salta all'ultima pagina"
constexpr int JUMP_TO_END_PAGES = 100;

constexpr wchar_t WINDOW_CLASS_NAME[] = L"DesktopPagerNativeWindow";
constexpr wchar_t APP_NAME[] = L"DesktopPager";
constexpr wchar_t RUN_KEY_PATH[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
constexpr wchar_t RUN_VALUE_NAME[] = L"DesktopPagerNative";

struct AppState
{
    HWND hwnd = nullptr;
    NOTIFYICONDATAW nid{};
    HICON icon = nullptr;

    int currentPage = 1;
    int totalPages = 1;

    bool autostartEnabled = false;
    UINT taskbarCreatedMessage = 0;
};

AppState g_state;

std::wstring GetLogFilePath()
{
    wchar_t localAppData[MAX_PATH]{};
    const auto len = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH);
    if (len == 0 || len >= MAX_PATH)
    {
        return L"DesktopPager.log";
    }

    std::wstring base = localAppData;
    base += L"\\DesktopPager";
    CreateDirectoryW(base.c_str(), nullptr);
    base += L"\\logs";
    CreateDirectoryW(base.c_str(), nullptr);
    base += L"\\desktoppager.log";
    return base;
}

void Log(const std::wstring& message)
{
    const auto path = GetLogFilePath();
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"a+, ccs=UTF-8") != 0 || file == nullptr)
    {
        return;
    }

    SYSTEMTIME st{};
    GetLocalTime(&st);
    fwprintf(
        file,
        L"[%04d-%02d-%02d %02d:%02d:%02d] %s\n",
        st.wYear,
        st.wMonth,
        st.wDay,
        st.wHour,
        st.wMinute,
        st.wSecond,
        message.c_str());
    fclose(file);
}

bool SendListMessageTimeout(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam, LRESULT* result)
{
    DWORD_PTR out = 0;
    const auto sent = SendMessageTimeoutW(
        hwnd,
        msg,
        wParam,
        lParam,
        SMTO_ABORT_IF_HUNG_FLAG,
        SEND_TIMEOUT_MS,
        &out);
    if (result != nullptr)
    {
        *result = static_cast<LRESULT>(out);
    }
    return sent != 0;
}

HWND GetDesktopListView()
{
    const auto progman = FindWindowW(L"Progman", L"Program Manager");
    auto shellView = FindWindowExW(progman, nullptr, L"SHELLDLL_DefView", nullptr);

    if (shellView == nullptr)
    {
        auto worker = FindWindowExW(nullptr, nullptr, L"WorkerW", nullptr);
        while (worker != nullptr && shellView == nullptr)
        {
            shellView = FindWindowExW(worker, nullptr, L"SHELLDLL_DefView", nullptr);
            worker = FindWindowExW(nullptr, worker, L"WorkerW", nullptr);
        }
    }

    if (shellView == nullptr)
    {
        return nullptr;
    }

    return FindWindowExW(shellView, nullptr, L"SysListView32", L"FolderView");
}

bool IsHorizontalLayout(HWND listView)
{
    // Con "disposizione automatica" il desktop riempie colonne da sinistra
    // (LVS_ALIGNLEFT): l'overflow finisce oltre il bordo destro e si
    // sfoglia in orizzontale. Altrimenti in verticale.
    const auto style = GetWindowLongPtrW(listView, GWL_STYLE);
    return (style & LVS_ALIGNMASK) == LVS_ALIGNLEFT;
}

void SetNoScrollStyle(HWND listView, bool enabled)
{
    const auto style = GetWindowLongPtrW(listView, GWL_STYLE);
    const bool hasNoScroll = (style & LVS_NOSCROLL_STYLE) != 0;
    if (enabled == hasNoScroll)
    {
        return;
    }

    const auto newStyle = enabled
        ? (style | LVS_NOSCROLL_STYLE)
        : (style & ~static_cast<LONG_PTR>(LVS_NOSCROLL_STYLE));
    SetWindowLongPtrW(listView, GWL_STYLE, newStyle);
}

bool TryGetScrollStatus(HWND listView, int& position, int& maxRange, int& pageSize)
{
    SCROLLINFO si{};
    si.cbSize = sizeof(si);
    si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
    const int bar = IsHorizontalLayout(listView) ? SB_HORZ : SB_VERT;
    if (!GetScrollInfo(listView, bar, &si))
    {
        return false;
    }

    if (si.nPage == 0 && si.nMax == 0)
    {
        return false;
    }

    position = si.nPos;
    maxRange = si.nMax;
    pageSize = static_cast<int>(si.nPage);
    return true;
}

bool ScrollByPages(HWND listView, int pageDelta)
{
    if (pageDelta == 0)
    {
        return true;
    }

    SetNoScrollStyle(listView, false);

    RECT rc{};
    if (!GetClientRect(listView, &rc))
    {
        return false;
    }

    LRESULT spacing = 0;
    SendListMessageTimeout(listView, LVM_GETITEMSPACING_MSG, FALSE, 0, &spacing);
    const int spacingX = static_cast<int>(LOWORD(spacing));
    const int spacingY = static_cast<int>(HIWORD(spacing));

    const bool horizontal = IsHorizontalLayout(listView);
    int dx = 0;
    int dy = 0;
    if (horizontal)
    {
        // una colonna di sovrapposizione per non perdere il filo
        int step = rc.right - spacingX;
        if (step < spacingX)
        {
            step = rc.right;
        }
        dx = pageDelta * step;
    }
    else
    {
        int step = rc.bottom - spacingY;
        if (step < spacingY)
        {
            step = rc.bottom;
        }
        dy = pageDelta * step;
    }

    int beforePos = 0;
    int maxRange = 0;
    int pageSize = 0;
    if (!TryGetScrollStatus(listView, beforePos, maxRange, pageSize))
    {
        beforePos = 0;
    }

    LRESULT result = 0;
    const bool ok = SendListMessageTimeout(listView, LVM_SCROLL_MSG, dx, dy, &result) && result != 0;

    // Quando la scrollbar compare per la prima volta la ListView fa un
    // re-layout che puo' assorbire parte dello scroll: verifica la
    // posizione raggiunta e correggi la differenza.
    int afterPos = 0;
    if (ok && TryGetScrollStatus(listView, afterPos, maxRange, pageSize))
    {
        const int maxPosition = std::max(0, maxRange - pageSize + 1);
        const int target = std::clamp(beforePos + (horizontal ? dx : dy), 0, maxPosition);
        const int diff = target - afterPos;
        if (diff != 0)
        {
            SendListMessageTimeout(
                listView,
                LVM_SCROLL_MSG,
                horizontal ? diff : 0,
                horizontal ? 0 : diff,
                nullptr);
        }
    }

    return ok;
}

bool ResetScroll(HWND listView)
{
    SetNoScrollStyle(listView, false);
    SendListMessageTimeout(listView, LVM_SCROLL_MSG, -32768, -32768, nullptr);
    SetNoScrollStyle(listView, true);
    InvalidateRect(listView, nullptr, TRUE);
    return true;
}

void UpdateTrayTooltip(AppState& state)
{
    wchar_t tooltip[128]{};
    StringCchPrintfW(tooltip, _countof(tooltip), L"DesktopPager - Pagina %d/%d", state.currentPage, state.totalPages);
    NOTIFYICONDATAW copy = state.nid;
    StringCchCopyW(copy.szTip, _countof(copy.szTip), tooltip);
    Shell_NotifyIconW(NIM_MODIFY, &copy);
}

void RefreshPageState(AppState& state, HWND listView)
{
    int pos = 0;
    int maxRange = 0;
    int pageSize = 0;
    if (listView != nullptr && TryGetScrollStatus(listView, pos, maxRange, pageSize) && pageSize > 0)
    {
        state.totalPages = std::max(1, static_cast<int>((maxRange + pageSize) / pageSize));
        const int maxPosition = std::max(0, maxRange - pageSize + 1);
        state.currentPage = (maxPosition == 0 || state.totalPages == 1)
            ? 1
            : 1 + static_cast<int>((static_cast<double>(pos) / maxPosition) * (state.totalPages - 1) + 0.5);
    }
    else
    {
        // scrollbar assente (LVS_NOSCROLL attivo): siamo alla prima pagina
        state.currentPage = std::clamp(state.currentPage, 1, state.totalPages);
    }
    UpdateTrayTooltip(state);
}

void GoToFirstPage(AppState& state)
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        Log(L"GoToFirstPage: desktop list view not found.");
        return;
    }

    ResetScroll(listView);
    state.currentPage = 1;
    UpdateTrayTooltip(state);
}

void GoToNextPage(AppState& state)
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        Log(L"GoToNextPage: desktop list view not found.");
        return;
    }

    int before = 0;
    int maxRange = 0;
    int pageSize = 0;
    const bool hadStatus = TryGetScrollStatus(listView, before, maxRange, pageSize);

    ScrollByPages(listView, +1);

    // se eravamo gia' a fine corsa la posizione non cambia: wrap alla prima
    int after = 0;
    if (hadStatus && TryGetScrollStatus(listView, after, maxRange, pageSize) && after == before)
    {
        ResetScroll(listView);
        state.currentPage = 1;
        UpdateTrayTooltip(state);
        return;
    }

    RefreshPageState(state, listView);
}

void GoToPreviousPage(AppState& state)
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        Log(L"GoToPreviousPage: desktop list view not found.");
        return;
    }

    int pos = 0;
    int maxRange = 0;
    int pageSize = 0;
    if (!TryGetScrollStatus(listView, pos, maxRange, pageSize) || pos <= 0)
    {
        // prima pagina: wrap all'ultima
        ScrollByPages(listView, JUMP_TO_END_PAGES);
        RefreshPageState(state, listView);
        return;
    }

    ScrollByPages(listView, -1);

    // se siamo tornati all'origine, ripristina lo stile originale del desktop
    if (TryGetScrollStatus(listView, pos, maxRange, pageSize) && pos <= 0)
    {
        ResetScroll(listView);
        state.currentPage = 1;
        UpdateTrayTooltip(state);
        return;
    }

    RefreshPageState(state, listView);
}

// Ruota lo schermo principale (DMDO_DEFAULT/90/180/270), come le
// hotkey dei driver video Intel.
bool RotateScreen(int orientation)
{
    if (orientation < DMDO_DEFAULT || orientation > DMDO_270)
    {
        return false;
    }

    DEVMODEW dm{};
    dm.dmSize = sizeof(dm);
    if (!EnumDisplaySettingsW(nullptr, ENUM_CURRENT_SETTINGS, &dm))
    {
        return false;
    }

    if (static_cast<int>(dm.dmDisplayOrientation) == orientation)
    {
        return true;
    }

    // passando tra orizzontale e verticale vanno scambiate le dimensioni
    if (((dm.dmDisplayOrientation + orientation) & 1) == 1)
    {
        std::swap(dm.dmPelsWidth, dm.dmPelsHeight);
    }

    dm.dmDisplayOrientation = orientation;
    const auto result = ChangeDisplaySettingsW(&dm, 0);
    if (result != DISP_CHANGE_SUCCESSFUL)
    {
        Log(L"RotateScreen: ChangeDisplaySettings failed.");
        return false;
    }
    return true;
}

// Tasto del carattere '^' nel layout corrente (italiana: Shift+ì, 0xDD)
UINT GetCaretVirtualKey()
{
    const SHORT scan = VkKeyScanW(L'^');
    return scan == -1 ? 0xDD : static_cast<UINT>(scan & 0xFF);
}

void RestartExplorer(AppState& state)
{
    Log(L"RestartExplorer: requested.");

    // chiudi tutte le istanze di explorer.exe
    const auto snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot != INVALID_HANDLE_VALUE)
    {
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Process32FirstW(snapshot, &entry))
        {
            do
            {
                if (_wcsicmp(entry.szExeFile, L"explorer.exe") == 0)
                {
                    const auto process = OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE, FALSE, entry.th32ProcessID);
                    if (process != nullptr)
                    {
                        TerminateProcess(process, 0);
                        WaitForSingleObject(process, 5000);
                        CloseHandle(process);
                    }
                }
            } while (Process32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);
    }

    // Winlogon di solito rilancia la shell da solo: dagli un momento.
    Sleep(1500);

    bool running = false;
    const auto check = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (check != INVALID_HANDLE_VALUE)
    {
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Process32FirstW(check, &entry))
        {
            do
            {
                if (_wcsicmp(entry.szExeFile, L"explorer.exe") == 0)
                {
                    running = true;
                    break;
                }
            } while (Process32NextW(check, &entry));
        }
        CloseHandle(check);
    }

    if (!running)
    {
        // usa il percorso assoluto per evitare rischi di search-order hijacking
        wchar_t winDir[MAX_PATH]{};
        GetWindowsDirectoryW(winDir, MAX_PATH);
        std::wstring explorerPath = std::wstring(winDir) + L"\\explorer.exe";
        ShellExecuteW(nullptr, L"open", explorerPath.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }

    state.currentPage = 1;
    state.totalPages = 1;
    Log(L"RestartExplorer: done.");
}

bool IsAutostartEnabled()
{
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, RUN_KEY_PATH, 0, KEY_READ, &key) != ERROR_SUCCESS)
    {
        return false;
    }

    wchar_t value[1024]{};
    DWORD type = REG_SZ;
    DWORD size = sizeof(value);
    const auto status = RegQueryValueExW(key, RUN_VALUE_NAME, nullptr, &type, reinterpret_cast<LPBYTE>(value), &size);
    RegCloseKey(key);
    return status == ERROR_SUCCESS && value[0] != L'\0';
}

bool SetAutostartEnabled(bool enabled)
{
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, RUN_KEY_PATH, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS)
    {
        return false;
    }

    bool ok = false;
    if (enabled)
    {
        wchar_t exePath[MAX_PATH]{};
        const DWORD len = GetModuleFileNameW(nullptr, exePath, MAX_PATH);
        if (len > 0)
        {
            wchar_t quoted[MAX_PATH + 4]{};
            StringCchPrintfW(quoted, _countof(quoted), L"\"%s\"", exePath);
            const auto bytes = static_cast<DWORD>((wcslen(quoted) + 1) * sizeof(wchar_t));
            ok = RegSetValueExW(key, RUN_VALUE_NAME, 0, REG_SZ, reinterpret_cast<const BYTE*>(quoted), bytes) == ERROR_SUCCESS;
        }
    }
    else
    {
        const auto status = RegDeleteValueW(key, RUN_VALUE_NAME);
        ok = status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND;
    }

    RegCloseKey(key);
    return ok;
}

void ShowContextMenu(HWND hwnd)
{
    HMENU menu = CreatePopupMenu();
    if (menu == nullptr)
    {
        return;
    }

    AppendMenuW(menu, MF_STRING, IDM_NEXT, L"Pagina avanti\tCtrl+Alt+PgGiu");
    AppendMenuW(menu, MF_STRING, IDM_PREV, L"Pagina indietro\tCtrl+Alt+PgSu");
    AppendMenuW(menu, MF_STRING, IDM_HOME, L"Prima pagina\tCtrl+Alt+Home");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    HMENU rotateMenu = CreatePopupMenu();
    AppendMenuW(rotateMenu, MF_STRING, IDM_ROTATE_180, L"Sottosopra\tCtrl+Alt+Su");
    AppendMenuW(rotateMenu, MF_STRING, IDM_ROTATE_0, L"Normale\tCtrl+Alt+Giu");
    AppendMenuW(rotateMenu, MF_STRING, IDM_ROTATE_90, L"Barra a sinistra\tCtrl+Alt+Sinistra");
    AppendMenuW(rotateMenu, MF_STRING, IDM_ROTATE_270, L"Barra a destra\tCtrl+Alt+Destra");
    AppendMenuW(rotateMenu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(rotateMenu, MF_STRING, IDM_ROTATE_0, L"Emergenza: ripristina\tCtrl+Alt+Shift+^");
    AppendMenuW(menu, MF_POPUP, reinterpret_cast<UINT_PTR>(rotateMenu), L"Ruota schermo");
    AppendMenuW(menu, MF_STRING, IDM_RESTART_EXPLORER, L"Riavvia Explorer\tCtrl+Alt+Fine");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING | (g_state.autostartEnabled ? MF_CHECKED : MF_UNCHECKED), IDM_AUTOSTART, L"Avvio automatico con Windows");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, IDM_EXIT, L"Esci");

    POINT pt{};
    GetCursorPos(&pt);
    SetForegroundWindow(hwnd);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, nullptr);
    DestroyMenu(menu);
}

void InitTrayIcon(HWND hwnd)
{
    g_state.nid = {};
    g_state.nid.cbSize = sizeof(g_state.nid);
    g_state.nid.hWnd = hwnd;
    g_state.nid.uID = 1;
    g_state.nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_state.nid.uCallbackMessage = WM_TRAYICON;
    g_state.nid.hIcon = g_state.icon;
    StringCchCopyW(g_state.nid.szTip, _countof(g_state.nid.szTip), L"DesktopPager");
    Shell_NotifyIconW(NIM_ADD, &g_state.nid);
}

void Cleanup()
{
    // lascia il desktop come l'abbiamo trovato
    const auto listView = GetDesktopListView();
    if (listView != nullptr)
    {
        ResetScroll(listView);
    }

    Shell_NotifyIconW(NIM_DELETE, &g_state.nid);
    if (g_state.icon != nullptr)
    {
        DestroyIcon(g_state.icon);
        g_state.icon = nullptr;
    }
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_CREATE:
    {
        g_state.hwnd = hwnd;
        g_state.icon = static_cast<HICON>(LoadImageW(
            GetModuleHandleW(nullptr),
            MAKEINTRESOURCEW(IDI_APP_ICON),
            IMAGE_ICON,
            0,
            0,
            LR_DEFAULTSIZE));
        if (g_state.icon == nullptr)
        {
            g_state.icon = LoadIconW(nullptr, IDI_APPLICATION);
        }

        g_state.taskbarCreatedMessage = RegisterWindowMessageW(L"TaskbarCreated");
        InitTrayIcon(hwnd);
        g_state.autostartEnabled = IsAutostartEnabled();
        UpdateTrayTooltip(g_state);

        const bool h1 = RegisterHotKey(hwnd, HOTKEY_NEXT, MODIFIERS, VK_NEXT) != 0;
        const bool h2 = RegisterHotKey(hwnd, HOTKEY_PREV, MODIFIERS, VK_PRIOR) != 0;
        const bool h3 = RegisterHotKey(hwnd, HOTKEY_HOME, MODIFIERS, VK_HOME) != 0;
        const bool h4 = RegisterHotKey(hwnd, HOTKEY_RESTART_EXPLORER, MODIFIERS, VK_END) != 0;
        const bool h5 = RegisterHotKey(hwnd, HOTKEY_FLIP_SCREEN, MODIFIERS, VK_UP) != 0;
        const bool h6 = RegisterHotKey(hwnd, HOTKEY_RESET_SCREEN, MODIFIERS, VK_DOWN) != 0;
        const bool h7 = RegisterHotKey(hwnd, HOTKEY_ROTATE_LEFT, MODIFIERS, VK_LEFT) != 0;
        const bool h8 = RegisterHotKey(hwnd, HOTKEY_ROTATE_RIGHT, MODIFIERS, VK_RIGHT) != 0;
        const bool h9 = RegisterHotKey(hwnd, HOTKEY_PANIC_RESET, MODIFIERS | MOD_SHIFT, GetCaretVirtualKey()) != 0;
        if (!(h1 && h2 && h3 && h4 && h5 && h6 && h7 && h8 && h9))
        {
            MessageBoxW(hwnd, L"Impossibile registrare una o più hotkey globali.", APP_NAME, MB_ICONWARNING | MB_OK);
            Log(L"WM_CREATE: one or more hotkeys could not be registered.");
        }
        return 0;
    }
    case WM_HOTKEY:
        switch (wParam)
        {
        case HOTKEY_NEXT:
            GoToNextPage(g_state);
            break;
        case HOTKEY_PREV:
            GoToPreviousPage(g_state);
            break;
        case HOTKEY_HOME:
            GoToFirstPage(g_state);
            break;
        case HOTKEY_RESTART_EXPLORER:
            RestartExplorer(g_state);
            break;
        case HOTKEY_FLIP_SCREEN:
            RotateScreen(DMDO_180);
            break;
        case HOTKEY_RESET_SCREEN:
        case HOTKEY_PANIC_RESET:
            RotateScreen(DMDO_DEFAULT);
            break;
        case HOTKEY_ROTATE_LEFT:
            RotateScreen(DMDO_90);
            break;
        case HOTKEY_ROTATE_RIGHT:
            RotateScreen(DMDO_270);
            break;
        default:
            break;
        }
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDM_NEXT:
            GoToNextPage(g_state);
            break;
        case IDM_PREV:
            GoToPreviousPage(g_state);
            break;
        case IDM_HOME:
            GoToFirstPage(g_state);
            break;
        case IDM_RESTART_EXPLORER:
            RestartExplorer(g_state);
            break;
        case IDM_ROTATE_180:
            RotateScreen(DMDO_180);
            break;
        case IDM_ROTATE_0:
            RotateScreen(DMDO_DEFAULT);
            break;
        case IDM_ROTATE_90:
            RotateScreen(DMDO_90);
            break;
        case IDM_ROTATE_270:
            RotateScreen(DMDO_270);
            break;
        case IDM_AUTOSTART:
        {
            const bool target = !g_state.autostartEnabled;
            if (SetAutostartEnabled(target))
            {
                g_state.autostartEnabled = target;
            }
            else
            {
                MessageBoxW(hwnd, L"Impossibile aggiornare l'avvio automatico.", APP_NAME, MB_ICONWARNING | MB_OK);
                Log(L"WM_COMMAND: failed to update autostart.");
            }
            break;
        }
        case IDM_EXIT:
            DestroyWindow(hwnd);
            break;
        default:
            break;
        }
        return 0;
    case WM_TRAYICON:
        if (lParam == WM_RBUTTONUP || lParam == WM_CONTEXTMENU || lParam == WM_LBUTTONUP)
        {
            ShowContextMenu(hwnd);
        }
        return 0;
    case WM_DESTROY:
        UnregisterHotKey(hwnd, HOTKEY_NEXT);
        UnregisterHotKey(hwnd, HOTKEY_PREV);
        UnregisterHotKey(hwnd, HOTKEY_HOME);
        for (int id = HOTKEY_RESTART_EXPLORER + 1; id <= HOTKEY_PANIC_RESET; ++id)
        {
            UnregisterHotKey(hwnd, id);
        }
        UnregisterHotKey(hwnd, HOTKEY_RESTART_EXPLORER);
        Cleanup();
        PostQuitMessage(0);
        return 0;
    default:
        // Explorer riavviato: la taskbar e' nuova, la tray icon va ricreata
        if (message == g_state.taskbarCreatedMessage && g_state.taskbarCreatedMessage != 0)
        {
            Shell_NotifyIconW(NIM_ADD, &g_state.nid);
            UpdateTrayTooltip(g_state);
            return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
} // namespace

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, PWSTR, int)
{
    // istanza singola
    CreateMutexW(nullptr, TRUE, L"DesktopPagerNative_SingleInstance");
    if (GetLastError() == ERROR_ALREADY_EXISTS)
    {
        MessageBoxW(nullptr, L"DesktopPager è già in esecuzione.", APP_NAME, MB_ICONINFORMATION | MB_OK);
        return 0;
    }

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = WINDOW_CLASS_NAME;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);

    if (!RegisterClassExW(&wc))
    {
        return 1;
    }

    const auto hwnd = CreateWindowExW(
        0,
        WINDOW_CLASS_NAME,
        APP_NAME,
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        300,
        200,
        nullptr,
        nullptr,
        hInstance,
        nullptr);

    if (hwnd == nullptr)
    {
        return 1;
    }

    ShowWindow(hwnd, SW_HIDE);

    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    return static_cast<int>(msg.wParam);
}
