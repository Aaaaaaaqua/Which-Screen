"""A small Windows window switcher that makes monitor location visible."""

import ctypes
from ctypes import wintypes
import queue
import threading
import tkinter as tk
from tkinter import ttk


user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

# Use physical display coordinates so high-DPI scaling does not affect monitor lookup.
try:
    user32.SetProcessDPIAware()
except AttributeError:
    pass

WM_HOTKEY = 0x0312
PM_REMOVE = 0x0001
MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
VK_SPACE = 0x20
SW_RESTORE = 9
GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW = 0x00000080
MONITOR_DEFAULTTONEAREST = 2


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class MSG(ctypes.Structure):
    _fields_ = [("hwnd", wintypes.HWND), ("message", wintypes.UINT),
                ("wParam", wintypes.WPARAM), ("lParam", wintypes.LPARAM),
                ("time", wintypes.DWORD), ("pt", POINT)]


class MONITORINFOEX(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", RECT),
                ("rcWork", RECT), ("dwFlags", wintypes.DWORD),
                ("szDevice", ctypes.c_wchar * 32)]


def monitor_labels():
    monitors = []
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HMONITOR,
                                       wintypes.HDC, ctypes.POINTER(RECT), wintypes.LPARAM)

    @callback_type
    def callback(handle, _hdc, rect, _data):
        info = MONITORINFOEX()
        info.cbSize = ctypes.sizeof(info)
        user32.GetMonitorInfoW(handle, ctypes.byref(info))
        monitors.append((handle, info, info.rcMonitor))
        return True

    user32.EnumDisplayMonitors(None, None, callback, 0)
    primary = next((item for item in monitors if item[1].dwFlags & 1), monitors[0])
    primary_rect = primary[2]
    labeled = []
    for handle, info, rect in monitors:
        if info.dwFlags & 1:
            label = "主屏幕"
        elif rect.left >= primary_rect.right:
            label = "右侧屏幕"
        elif rect.right <= primary_rect.left:
            label = "左侧屏幕"
        elif rect.top >= primary_rect.bottom:
            label = "下方屏幕"
        else:
            label = "上方屏幕"
        labeled.append((handle, label, rect))
    return labeled


def visible_windows(monitors, excluded_hwnd=0):
    found = []
    monitor_map = {int(handle): label for handle, label, _rect in monitors}
    enum_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    @enum_type
    def callback(hwnd, _data):
        if hwnd == excluded_hwnd or not user32.IsWindowVisible(hwnd):
            return True
        if user32.GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW:
            return True
        length = user32.GetWindowTextLengthW(hwnd)
        if not length:
            return True
        title = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, title, length + 1)
        rect = RECT()
        if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
            return True
        # MonitorFromWindow retains a useful display association for minimized windows.
        handle = user32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)
        label = monitor_map.get(int(handle), "未知屏幕")
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        status = "已最小化" if user32.IsIconic(hwnd) else "打开"
        found.append((label, title.value, int(hwnd), status))
        return True

    user32.EnumWindows(callback, 0)
    return sorted(found, key=lambda item: (item[0], item[1].lower()))


class Switcher:
    def __init__(self):
        self.root = tk.Tk()
        self.root.withdraw()
        self.root.title("屏幕窗口切换器")
        self.root.geometry("680x520")
        self.root.minsize(520, 360)
        self.root.attributes("-topmost", True)
        self.root.protocol("WM_DELETE_WINDOW", self.hide)
        self.root.bind("<Escape>", lambda _event: self.hide())
        self.root.bind("<Control-r>", lambda _event: self.refresh())
        self.root.bind("<Return>", lambda _event: self.activate_selected())

        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Treeview", rowheight=31, font=("Microsoft YaHei UI", 10))
        style.configure("Treeview.Heading", font=("Microsoft YaHei UI", 10, "bold"))

        container = ttk.Frame(self.root, padding=16)
        container.pack(fill="both", expand=True)
        top = ttk.Frame(container)
        top.pack(fill="x", pady=(0, 12))
        ttk.Label(top, text="窗口在哪个屏幕？", font=("Microsoft YaHei UI", 16, "bold")).pack(side="left")
        ttk.Button(top, text="刷新", command=self.refresh).pack(side="right")
        ttk.Label(container, text="Ctrl+Alt+Space 唤出  ·  Enter 切换  ·  Esc 隐藏", foreground="#555").pack(anchor="w", pady=(0, 10))

        columns = ("screen", "window", "status")
        self.tree = ttk.Treeview(container, columns=columns, show="headings", selectmode="browse")
        self.tree.heading("screen", text="所在屏幕")
        self.tree.heading("window", text="窗口标题")
        self.tree.heading("status", text="状态")
        self.tree.column("screen", width=120, minwidth=100, stretch=False)
        self.tree.column("window", width=390, minwidth=200)
        self.tree.column("status", width=100, minwidth=80, stretch=False)
        scrollbar = ttk.Scrollbar(container, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        self.tree.bind("<Double-1>", lambda _event: self.activate_selected())

        self.handles = {}
        self.events = queue.Queue()
        self.hotkey_thread = threading.Thread(target=self.hotkey_loop, daemon=True)
        self.hotkey_thread.start()
        self.root.after(100, self.check_events)

    def hotkey_loop(self):
        hotkey_id = 1
        if not user32.RegisterHotKey(None, hotkey_id, MOD_CONTROL | MOD_ALT, VK_SPACE):
            return
        message = MSG()
        while user32.GetMessageW(ctypes.byref(message), None, 0, 0) > 0:
            if message.message == WM_HOTKEY and message.wParam == hotkey_id:
                self.events.put("toggle")
        user32.UnregisterHotKey(None, hotkey_id)

    def check_events(self):
        while not self.events.empty():
            if self.root.state() == "withdrawn":
                self.show()
            else:
                self.hide()
        self.root.after(100, self.check_events)

    def refresh(self):
        self.handles.clear()
        for item in self.tree.get_children():
            self.tree.delete(item)
        for screen, title, hwnd, status in visible_windows(
            monitor_labels(), self.root.winfo_id()
        ):
            item = self.tree.insert("", "end", values=(screen, title, status))
            self.handles[item] = hwnd
        children = self.tree.get_children()
        if children:
            self.tree.selection_set(children[0])
            self.tree.focus(children[0])

    def show(self):
        self.refresh()
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()

    def hide(self):
        self.root.withdraw()

    def activate_selected(self):
        selected = self.tree.selection()
        if not selected:
            return
        hwnd = self.handles.get(selected[0])
        if hwnd and user32.IsWindow(hwnd):
            user32.ShowWindow(hwnd, SW_RESTORE)
            user32.SetForegroundWindow(hwnd)
        self.hide()

    def run(self):
        self.show()
        self.root.mainloop()


if __name__ == "__main__":
    Switcher().run()
