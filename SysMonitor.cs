using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

// ============================================================================
// SysMonitor 系统监视器 V1.0
// 轻量级 Windows 任务栏系统监视器
//
// 功能概述：
//   1. 实时监视 CPU 使用率、物理内存使用率、网络上传/下载速度；
//   2. 主窗口嵌入 Windows 任务栏（Shell_TrayWnd 的子窗口），不占用桌面空间；
//   3. 透明背景自绘文字界面，支持四种配色方案；
//   4. 右键暗色主题菜单：显示项目开关、网络适配器选择、开机自启、关于、退出；
//   5. 配置自动保存至注册表，重启后恢复；
//   6. 单实例运行（命名互斥体），支持高 DPI。
//
// 技术要点：
//   - 性能计数器（PerformanceCounter）采集 CPU/内存；
//   - NetworkInterface 统计网卡收发字节并按时间差计算速率；
//   - Win32 API（SetParent/SetWindowPos）实现任务栏嵌入；
//   - 自定义 ToolStripProfessionalRenderer + ProfessionalColorTable 实现暗色菜单；
//   - 菜单看门狗定时器：解决任务栏嵌入窗口的右键菜单无法自动关闭的问题。
//
// 编译方式（.NET Framework 4.x）：
//   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe SysMonitor.cs
// ============================================================================

// 程序集元信息：显示在文件属性与关于信息中
[assembly: System.Reflection.AssemblyTitle("SysMonitor")]
[assembly: System.Reflection.AssemblyDescription("轻量级 Windows 任务栏系统监视器")]
[assembly: System.Reflection.AssemblyProduct("SysMonitor 系统监视器")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (C) 2026 liyang")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

// 主窗体类：继承自 Form，是程序唯一窗口（任务栏嵌入条）
class SysMonitor : Form
{
    // ---------- 定时器与菜单看门狗 ----------
    private System.Windows.Forms.Timer timer;      // 数据刷新定时器，每秒更新一次指标
    private System.Windows.Forms.Timer menuWatch;  // 右键菜单看门狗定时器，50ms 轮询一次
    private ContextMenuStrip menu;                 // 右键菜单对象引用（看门狗需要操作它）
    private bool menuArmed;                        // 看门狗是否已"武装"（开菜单的那一击结束后才生效）

    // ---------- 性能计数器 ----------
    private PerformanceCounter cpuCounter;         // CPU 总使用率计数器（Processor / % Processor Time / _Total）
    private PerformanceCounter memAvailCounter;    // 可用内存计数器（Memory / Available MBytes）

    // ---------- 网络速度计算 ----------
    private long prevRecv, prevSent;               // 上一次采样时网卡的累计收发字节数
    private DateTime prevNet = DateTime.Now;       // 上一次采样的时刻（用于计算时间差）
    private float totalMemMB;                      // 物理内存总量（MB），用于把可用内存换算成使用率

    // 四个指标当前显示的文本（每秒刷新）
    private string txt_cpu = "C: 0%", txt_mem = "M: 0%", txt_ul = "上: 0.00K", txt_dl = "下: 0.00K";

    // 透明键颜色：与背景色一致的颜色会被渲染为透明，从而只显示文字
    Color bg_key = Color.FromArgb(254, 254, 252);

    // 四套配色方案，每套 4 个颜色分别对应 CPU / 内存 / 上传 / 下载
    private Color[][] schemes = {
        new Color[] {                              // 0：彩色
            Color.FromArgb(200, 0, 0),             //   CPU 红
            Color.FromArgb(0, 70, 200),            //   内存 蓝
            Color.FromArgb(0, 140, 0),             //   上传 绿
            Color.FromArgb(190, 90, 0),            //   下载 橙
        },
        new Color[] {                              // 1：黑色
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
        },
        new Color[] {                              // 2：蓝色
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
        },
        new Color[] {                              // 3：红色
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
        },
    };
    private int schemeIdx = 0;                     // 当前选中的配色方案下标
    private bool showCpu = true, showMem = true, showUl = true, showDl = true;  // 四个指标的显示开关
    private string adapterId = "";                 // 要监视的网卡 Id，空串表示"全部网络"
    private int[] colW;                            // 每一列的宽度（按显示项目动态计算）
    private static Mutex mutex;                    // 单实例互斥体
    private const string RegPath = @"Software\SysMonitor";  // 配置保存的注册表路径
    private const string AppVersion = "1.0";       // 版本号（关于对话框中显示）
    private const string CopyrightText = "Copyright (C) 2026 liyang";  // 版权信息
    // 测量文本宽度时用的样文本：取各指标可能出现的最大值，保证文字不溢出
    private static readonly string[] MeasureText = { "C: 100%", "M: 100%", "上: 999.99M", "下: 999.99M" };

    // ---------- Win32 API 声明（P/Invoke） ----------

    // 获取系统内存状态（总量/可用量等）
    [DllImport("kernel32.dll")]
    static extern void GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // 按窗口类名查找窗口，用于定位任务栏（Shell_TrayWnd）
    [DllImport("user32.dll")]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    // 设置窗口父子关系：把本窗口变成任务栏的子窗口，实现"嵌入任务栏"
    [DllImport("user32.dll")]
    static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    // 设置窗口位置和尺寸（嵌入任务栏后用它摆放到任务栏左侧）
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // 查询某个虚拟按键的当前状态（看门狗用）
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    // 获取鼠标光标的屏幕坐标（看门狗用）
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    // ---------- Win32 常量 ----------
    const uint SWP_NOZORDER = 0x0004;    // SetWindowPos 标志：保持 Z 序不变
    const uint SWP_NOACTIVATE = 0x0010;  // SetWindowPos 标志：不激活窗口
    const int WM_MOUSEACTIVATE = 0x21;   // 窗口消息：鼠标点击即将激活窗口时，询问是否激活
    const int MA_NOACTIVATE = 3;         // WM_MOUSEACTIVATE 的应答：不激活窗口，也不丢弃鼠标消息
    const int VK_LBUTTON = 0x01;         // 虚拟键码：鼠标左键
    const int VK_RBUTTON = 0x02;         // 虚拟键码：鼠标右键
    const int VK_MBUTTON = 0x04;         // 虚拟键码：鼠标中键

    // 光标坐标结构（GetCursorPos 用）
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    // 内存状态结构（GlobalMemoryStatusEx 用），按 Win32 MEMORYSTATUSEX 布局定义
    struct MEMORYSTATUSEX
    {
        public uint dwLength;                        // 结构体大小（调用前必须赋值）
        public uint dwMemoryLoad;                    // 内存使用率（0-100）
        public ulong ullTotalPhys;                   // 物理内存总量（字节）
        public ulong ullAvailPhys;                   // 可用物理内存（字节）
        public ulong ullTotalPageFile;               // 页面文件总量
        public ulong ullAvailPageFile;               // 可用页面文件
        public ulong ullTotalVirtual;                // 虚拟内存总量
        public ulong ullAvailVirtual;                // 可用虚拟内存
        public ulong ullAvailExtendedVirtual;        // 保留字段
    }

    // 构造函数：初始化窗口属性、数据源、定时器和菜单
    public SysMonitor()
    {
        // 先从注册表恢复上次的配置（配色/显示项目/网卡选择）
        LoadConfig();

        // ---------- 窗口基本属性 ----------
        AutoScaleMode = AutoScaleMode.None;   // 关闭自动缩放，布局完全由代码控制
        FormBorderStyle = FormBorderStyle.None;  // 无边框窗体
        ShowInTaskbar = false;                // 不在任务栏显示按钮（本体已嵌入任务栏）
        BackColor = bg_key;                   // 背景色
        TransparencyKey = bg_key;             // 透明键：与背景同色的区域变透明，只留文字

        // ---------- 初始化性能计数器 ----------
        cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");      // CPU 总使用率
        memAvailCounter = new PerformanceCounter("Memory", "Available MBytes");              // 可用内存(MB)

        // 读取物理内存总量（MB），供使用率换算
        var mse = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        GlobalMemoryStatusEx(ref mse);
        totalMemMB = (float)(mse.ullTotalPhys / (1024.0 * 1024.0));

        // 记录网络计数的初始基准值
        ResetNetCounters();

        // 监听显示设置变化（分辨率/任务栏高度改变时自动重新布局）
        SystemEvents.DisplaySettingsChanged += (s, e) => DoLayout();

        // 构建右键菜单，并计算各列初始宽度
        BuildMenu();
        CalcWidths();

        // 窗口显示后：把自己挂到任务栏窗口下面，成为任务栏的子窗口
        Shown += (s, e) =>
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
                SetParent(Handle, taskbar);
            DoLayout();
        };

        // 启动 1 秒定时器，周期性刷新系统指标
        timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += UpdateStats;
        timer.Start();
    }

    // 窗口创建参数：保留此重写以便设置扩展窗口样式（当前使用默认值）
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            return cp;
        }
    }

    // 窗口消息处理：拦截"鼠标即将激活窗口"消息，返回"不激活"
    // 作用：点击本窗口（任务栏条）时不会把焦点从当前应用抢走
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    // 从注册表加载配置（任何一项读取失败都不影响其他项）
    private void LoadConfig()
    {
        try
        {
            var key = Registry.CurrentUser.OpenSubKey(RegPath, false);   // 只读方式打开
            if (key == null) return;                                     // 首次运行无配置，使用默认值
            schemeIdx = ToInt(key.GetValue("Scheme"), 0);                // 配色方案下标
            showCpu = ToBool(key.GetValue("ShowCpu"), true);             // CPU 显示开关
            showMem = ToBool(key.GetValue("ShowMem"), true);             // 内存显示开关
            showUl = ToBool(key.GetValue("ShowUl"), true);               // 上传显示开关
            showDl = ToBool(key.GetValue("ShowDl"), true);               // 下载显示开关
            var ad = key.GetValue("Adapter") as string;                  // 网卡 Id（空=全部网络）
            adapterId = ad == null ? "" : ad;
            key.Close();
        }
        catch { }   // 注册表不可用时静默失败，使用默认配置
    }

    // 把当前配置写入注册表
    private void SaveConfig()
    {
        try
        {
            var key = Registry.CurrentUser.CreateSubKey(RegPath);        // 不存在则创建
            if (key == null) return;
            key.SetValue("Scheme", schemeIdx);
            key.SetValue("ShowCpu", showCpu ? 1 : 0);
            key.SetValue("ShowMem", showMem ? 1 : 0);
            key.SetValue("ShowUl", showUl ? 1 : 0);
            key.SetValue("ShowDl", showDl ? 1 : 0);
            key.SetValue("Adapter", adapterId);
            key.Close();
        }
        catch { }
    }

    // 安全读取整型配置：值为空或格式错误时返回默认值
    private static int ToInt(object v, int def)
    {
        if (v == null) return def;
        try { return Convert.ToInt32(v); }
        catch { return def; }
    }

    // 安全读取布尔配置（注册表中用 1/0 存储）
    private static bool ToBool(object v, bool def)
    {
        if (v == null) return def;
        try { return Convert.ToInt32(v) != 0; }
        catch { return def; }
    }

    // 按下标读取某个指标的显示开关（0=CPU 1=内存 2=上传 3=下载）
    private bool GetShow(int idx)
    {
        if (idx == 0) return showCpu;
        if (idx == 1) return showMem;
        if (idx == 2) return showUl;
        return showDl;
    }

    // 设置某个指标的显示开关
    private void SetShow(int idx, bool v)
    {
        if (idx == 0) showCpu = v;
        else if (idx == 1) showMem = v;
        else if (idx == 2) showUl = v;
        else showDl = v;
    }

    // 返回当前需要显示的指标下标列表（按显示开关过滤）
    private System.Collections.Generic.List<int> ShownIndices()
    {
        var list = new System.Collections.Generic.List<int>();
        if (showCpu) list.Add(0);
        if (showMem) list.Add(1);
        if (showUl) list.Add(2);
        if (showDl) list.Add(3);
        return list;
    }

    // 按下标返回某个指标要绘制的文本
    private string ItemText(int idx)
    {
        if (idx == 0) return txt_cpu;
        if (idx == 1) return txt_mem;
        if (idx == 2) return txt_ul;
        return txt_dl;
    }

    // 累加所有活动网卡（或指定网卡）的收发字节总数
    private void AddNetTotals(ref long recv, ref long sent)
    {
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            // 只统计"已启用"且"非回环"的网卡
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            // 指定了网卡时，跳过其它网卡（空串=全部网络）
            if (adapterId != "" && n.Id != adapterId) continue;
            var s = n.GetIPv4Statistics();
            recv += s.BytesReceived;
            sent += s.BytesSent;
        }
    }

    // 重置网络计数基准（记录当前累计值与时刻）
    private void ResetNetCounters()
    {
        long recv = 0, sent = 0;
        AddNetTotals(ref recv, ref sent);
        prevRecv = recv;
        prevSent = sent;
        prevNet = DateTime.Now;
    }

    // 判断当前是否已设置开机自启（检查当前用户 Run 启动项）
    private bool IsAutoStart()
    {
        var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        if (key == null) return false;
        var val = key.GetValue("SysMonitor") as string;
        key.Close();
        return val != null && val == Application.ExecutablePath;   // 值等于本程序路径才算已启用
    }

    // 切换开机自启状态（写入/移除 Run 启动项），并同步菜单勾选状态
    private void ToggleAutoStart(ToolStripMenuItem item)
    {
        if (IsAutoStart())
        {
            var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null) { key.DeleteValue("SysMonitor", false); key.Close(); }
            item.Checked = false;
        }
        else
        {
            var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null) { key.SetValue("SysMonitor", Application.ExecutablePath); key.Close(); }
            item.Checked = true;
        }
    }

    // 构建右键菜单（暗色主题）并装配菜单看门狗
    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Font = new Font("Segoe UI", 10);

        // "字体颜色"子菜单：四套配色，点击后保存配置并重绘
        var colorMenu = new ToolStripMenuItem("字体颜色");
        colorMenu.DropDownItems.Add("彩色", null, (s, e) => { schemeIdx = 0; SaveConfig(); Invalidate(); });
        colorMenu.DropDownItems.Add("黑色", null, (s, e) => { schemeIdx = 1; SaveConfig(); Invalidate(); });
        colorMenu.DropDownItems.Add("蓝色", null, (s, e) => { schemeIdx = 2; SaveConfig(); Invalidate(); });
        colorMenu.DropDownItems.Add("红色", null, (s, e) => { schemeIdx = 3; SaveConfig(); Invalidate(); });
        menu.Items.Add(colorMenu);

        // "显示项目"子菜单：四个指标的勾选框，切换后重新计算布局
        var showMenu = new ToolStripMenuItem("显示项目");
        AddShowItem(showMenu, "CPU", 0);
        AddShowItem(showMenu, "内存", 1);
        AddShowItem(showMenu, "上传", 2);
        AddShowItem(showMenu, "下载", 3);
        menu.Items.Add(showMenu);

        // "网络适配器"子菜单："全部网络" + 本机所有活动网卡
        var netMenu = new ToolStripMenuItem("网络适配器");
        var allItem = new ToolStripMenuItem("全部网络");
        allItem.Tag = "";
        allItem.Checked = adapterId == "";
        allItem.Click += (s, e) => SetAdapter((ToolStripMenuItem)s, "");
        netMenu.DropDownItems.Add(allItem);
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            var item = new ToolStripMenuItem(n.Name);
            item.Tag = n.Id;    // 用 Tag 携带网卡 Id，点击时取出
            item.Checked = adapterId == n.Id;
            item.Click += (s, e) => SetAdapter((ToolStripMenuItem)s, (string)((ToolStripMenuItem)s).Tag);
            netMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(netMenu);

        // "开机自启"开关
        var autoStart = new ToolStripMenuItem("开机自启");
        autoStart.Checked = IsAutoStart();
        autoStart.Click += (s, e) => ToggleAutoStart(autoStart);
        menu.Items.Add(autoStart);

        // 分隔线 + 关于 + 退出
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于", null, (s, e) => ShowAbout());
        menu.Items.Add("退出", null, (s, e) => Application.Exit());

        // 应用暗色主题渲染器
        menu.Renderer = new MenuRenderer();

        // ---------- 装配菜单看门狗 ----------
        // 背景：本窗口嵌入任务栏且不参与激活，菜单弹出窗无法获得前台窗口，
        //       WinForms 自带的"失活自动关闭"机制不会触发，菜单会一直挂着。
        // 方案：菜单打开期间用 50ms 定时器轮询鼠标按键，
        //       一旦检测到按键且光标在菜单（含展开的子菜单）之外，立即关闭菜单。
        menuWatch = new System.Windows.Forms.Timer { Interval = 50 };
        menuWatch.Tick += MenuWatchTick;
        menu.Opened += (s, e) => { menuArmed = false; menuWatch.Start(); };  // 打开：先 disarm，避开开菜单那一击
        menu.Closed += (s, e) => menuWatch.Stop();                           // 关闭：停止轮询
        this.menu = menu;   // 保存引用供看门狗使用
        ContextMenuStrip = menu;
    }

    // 菜单看门狗：菜单打开期间周期性检查鼠标是否在菜单外发生了点击
    private void MenuWatchTick(object sender, EventArgs e)
    {
        // 菜单已不在显示状态（被菜单项点击、Esc 等方式关闭），停止看门狗
        if (menu == null || !menu.Visible)
        {
            menuWatch.Stop();
            return;
        }
        // 检测任一鼠标按键是否处于按下状态（0x8000 位表示"当前按下"）
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                 || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        if (!down)
        {
            // 所有按键都已释放：看门狗武装，此后第一次按下才会触发判定
            // （这样可以忽略"右键按下→弹出菜单"过程中尚未释放的按键）
            menuArmed = true;
            return;
        }
        if (!menuArmed) return;   // 开菜单的那一击还没结束，不处理
        POINT pt;
        GetCursorPos(out pt);
        // 光标在菜单（含子菜单）之外 => 关闭菜单
        if (!PointInMenu(menu, new Point(pt.X, pt.Y)))
            menu.Close();
    }

    // 判断屏幕坐标是否落在菜单或其已展开的子菜单范围内（递归）
    private static bool PointInMenu(ToolStripDropDown dd, Point p)
    {
        if (dd.Bounds.Contains(p)) return true;
        foreach (ToolStripItem it in dd.Items)
        {
            var mi = it as ToolStripMenuItem;
            if (mi != null && mi.DropDown.Visible && PointInMenu(mi.DropDown, p))
                return true;
        }
        return false;
    }

    // 向父菜单添加一个带勾选框的显示项
    private void AddShowItem(ToolStripMenuItem parent, string text, int idx)
    {
        var it = new ToolStripMenuItem(text);
        it.Checked = GetShow(idx);
        it.Click += (s, e) => ToggleShow((ToolStripMenuItem)s, idx);
        parent.DropDownItems.Add(it);
    }

    // 切换某个指标的显示状态：保存配置、重算列宽、重新布局并重绘
    private void ToggleShow(ToolStripMenuItem item, int idx)
    {
        bool v = !GetShow(idx);
        SetShow(idx, v);
        item.Checked = v;
        SaveConfig();
        CalcWidths();
        DoLayout();
        Invalidate();
    }

    // 弹出"关于"对话框
    private void ShowAbout()
    {
        MessageBox.Show("SysMonitor 系统监视器\n版本 V" + AppVersion + "\n\n轻量级 Windows 任务栏系统监视器\n" + CopyrightText,
            "关于 SysMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 切换监视的网卡：更新选中状态、保存配置并重置计数基准
    private void SetAdapter(ToolStripMenuItem item, string id)
    {
        adapterId = id;
        // 同步父菜单下所有网卡项的勾选状态
        var parent = item.OwnerItem as ToolStripMenuItem;
        if (parent != null)
        {
            foreach (ToolStripItem it in parent.DropDownItems)
            {
                var mi = it as ToolStripMenuItem;
                if (mi != null) mi.Checked = ((string)mi.Tag) == id;
            }
        }
        SaveConfig();
        ResetNetCounters();   // 换网卡后基准值作废，重新取基准
    }

    // 暗色菜单渲染器：在专业渲染器基础上把菜单文字改为浅色
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(220, 220, 225);   // 浅灰白文字
            base.OnRenderItemText(e);
        }
    }

    // 暗色菜单配色表：定义选中态、背景、边框、分隔线等各元素颜色
    class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Color.FromArgb(50, 50, 55); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(50, 50, 55); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(50, 50, 55); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(40, 40, 45); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(40, 40, 45); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 30, 33); } }
        public override Color SeparatorDark { get { return Color.FromArgb(60, 60, 65); } }
        public override Color SeparatorLight { get { return Color.FromArgb(60, 60, 65); } }
        public override Color MenuBorder { get { return Color.FromArgb(60, 60, 65); } }
    }

    // 按当前显示的项目测量并计算每一列的宽度（每列两个指标，上下排列）
    private void CalcWidths()
    {
        var shown = ShownIndices();
        int ncol = (shown.Count + 1) / 2;   // 列数 = 指标数除以 2 向上取整
        if (ncol < 1) ncol = 1;
        colW = new int[ncol];
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
        {
            var sf = new StringFormat(StringFormat.GenericTypographic);
            for (int i = 0; i < shown.Count; i++)
            {
                int col = i / 2;   // 第 i 个指标属于第 col 列
                // 用预置的样文本测量最大宽度，保证任何实际值都不溢出
                int w = (int)g.MeasureString(MeasureText[shown[i]], f, 999, sf).Width;
                if (w > colW[col]) colW[col] = w;   // 取该列所有指标的最大宽度
            }
        }
    }

    // 定时刷新系统指标（每秒触发一次）
    private void UpdateStats(object sender, EventArgs e)
    {
        try
        {
            // 读取 CPU 使用率与可用内存，换算内存使用率
            float cpu = cpuCounter.NextValue();
            float availMb = memAvailCounter.NextValue();
            float memPct = Math.Max(0, 100f - (availMb * 100f / totalMemMB));

            // 读取网卡累计收发字节
            long recv = 0, sent = 0;
            AddNetTotals(ref recv, ref sent);

            // 与上次采样比较，按时间差计算实时速率
            var now = DateTime.Now;
            double elapsed = (now - prevNet).TotalSeconds;

            long drecv = recv - prevRecv;
            long dsent = sent - prevSent;
            // 网卡被禁用/计数器归零时出现负差值：当作重新取基准处理
            if (drecv < 0) { drecv = recv; ResetNetCounters(); }
            if (dsent < 0) { dsent = sent; ResetNetCounters(); }

            double dlBps = elapsed > 0 ? drecv / elapsed : 0;   // 下载速率（字节/秒）
            double ulBps = elapsed > 0 ? dsent / elapsed : 0;   // 上传速率（字节/秒）
            prevRecv = recv; prevSent = sent; prevNet = now;

            // 格式化文本并触发重绘
            txt_cpu = string.Format("C: {0:F0}%", cpu);
            txt_mem = string.Format("M: {0:F0}%", memPct);
            txt_ul = string.Format("上: {0}", Fmt(ulBps));
            txt_dl = string.Format("下: {0}", Fmt(dlBps));
            Invalidate();
        }
        catch (Exception ex)
        {
            // 计数器瞬时不可用等异常不应导致程序崩溃，仅记录
            Debug.WriteLine("SysMonitor: " + ex.Message);
        }
    }

    // 把字节/秒格式化为 K 或 M 单位的可读文本
    private string Fmt(double bps)
    {
        if (bps >= 1000000) return string.Format("{0:F1}M", bps / 1000000);
        return string.Format("{0:F2}K", bps / 1000);
    }

    // 重新布局：按列宽计算窗口总宽度，按任务栏高度计算高度，摆放到任务栏左侧
    private void DoLayout()
    {
        if (!IsHandleCreated) return;   // 窗口句柄尚未创建时无需布局
        int sh = Screen.PrimaryScreen.Bounds.Height;
        // 任务栏高度 = 屏幕高度 - 工作区高度；异常时回退到 40
        int th = sh - Screen.PrimaryScreen.WorkingArea.Height;
        if (th < 6) th = 40;
        // 窗口总宽 = 左右边距 + 各列宽 + 列间距
        int barW = 16;
        if (colW != null)
        {
            for (int i = 0; i < colW.Length; i++)
            {
                barW += colW[i];
                if (i < colW.Length - 1) barW += 4;
            }
        }
        SetWindowPos(Handle, (IntPtr)0, 0, 0, barW, th, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    // 自绘界面：按两行布局绘制各指标文本（第一行/第二行各放一半）
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (colW == null) return;
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;   // 文字抗锯齿

        // 两行的垂直位置（行高 15 + 间距 6）
        int row1y = 3;
        int row2y = row1y + 15 + 6;

        var shown = ShownIndices();   // 当前要显示的指标
        var c = schemes[schemeIdx];   // 当前配色

        // 计算每一列的水平起始坐标（左边距 8，列间距 4）
        int[] colX = new int[colW.Length];
        int acc = 8;
        for (int k = 0; k < colW.Length; k++)
        {
            colX[k] = acc;
            acc += colW[k] + 4;
        }

        using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            for (int i = 0; i < shown.Count; i++)
            {
                int idx = shown[i];
                int col = i / 2;                              // 所属列
                int y = (i % 2 == 0) ? row1y : row2y;         // 偶数在上一行，奇数在下一行
                using (var b = new SolidBrush(c[idx]))
                    g.DrawString(ItemText(idx), f, b, colX[col], y, sf);
            }
        }
    }

    // 释放定时器与性能计数器等非内存资源
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (timer != null) timer.Dispose();
            if (menuWatch != null) menuWatch.Dispose();
            if (cpuCounter != null) cpuCounter.Dispose();
            if (memAvailCounter != null) memAvailCounter.Dispose();
        }
        base.Dispose(disposing);
    }

    // 声明进程为 DPI 感知，避免高分屏下界面被系统缩放变模糊
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    // 程序入口
    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        // 单实例检查：命名互斥体，第二个实例直接提示并退出
        bool created;
        mutex = new Mutex(true, "SysMonitor_SingleInstance", out created);
        if (!created)
        {
            MessageBox.Show("SysMonitor 已在运行");
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SysMonitor());
    }
}
