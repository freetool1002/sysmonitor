using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

class SysMonitor : Form
{
    private System.Windows.Forms.Timer timer;
    private PerformanceCounter cpuCounter;
    private PerformanceCounter memAvailCounter;
    private long prevRecv, prevSent;
    private DateTime prevNet = DateTime.Now;
    private float totalMemMB;

    private string txt_cpu = "C: 0%", txt_mem = "M: 0%", txt_ul = "上: 0.00K", txt_dl = "下: 0.00K";
    private int col1_w, col2_w;

    Color bg_key = Color.FromArgb(254, 254, 252);

    private Color[][] schemes = {
        new Color[] {
            Color.FromArgb(200, 0, 0),
            Color.FromArgb(0, 70, 200),
            Color.FromArgb(0, 140, 0),
            Color.FromArgb(190, 90, 0),
        },
        new Color[] {
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
            Color.FromArgb(0, 0, 0),
        },
        new Color[] {
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
            Color.FromArgb(30, 100, 200),
        },
        new Color[] {
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
            Color.FromArgb(200, 50, 50),
        },
    };
    private int schemeIdx = 0;
    private static Mutex mutex;

    [DllImport("kernel32.dll")]
    static extern void GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll")]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;

    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public SysMonitor()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = bg_key;
        TransparencyKey = bg_key;

        cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        memAvailCounter = new PerformanceCounter("Memory", "Available MBytes");

        var mse = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        GlobalMemoryStatusEx(ref mse);
        totalMemMB = (float)(mse.ullTotalPhys / (1024.0 * 1024.0));

        ResetNetCounters();

        SystemEvents.DisplaySettingsChanged += (s, e) => DoLayout();

        BuildMenu();
        CalcWidths();
        Shown += (s, e) =>
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
                SetParent(Handle, taskbar);
            DoLayout();
        };

        timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += UpdateStats;
        timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;
            return cp;
        }
    }

    private void ResetNetCounters()
    {
        prevRecv = 0; prevSent = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            {
                var s = n.GetIPv4Statistics();
                prevRecv += s.BytesReceived;
                prevSent += s.BytesSent;
            }
        }
        prevNet = DateTime.Now;
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var colorMenu = new ToolStripMenuItem("字体颜色");
        colorMenu.DropDownItems.Add("彩色", null, (s, e) => { schemeIdx = 0; Invalidate(); });
        colorMenu.DropDownItems.Add("黑色", null, (s, e) => { schemeIdx = 1; Invalidate(); });
        colorMenu.DropDownItems.Add("蓝色", null, (s, e) => { schemeIdx = 2; Invalidate(); });
        colorMenu.DropDownItems.Add("红色", null, (s, e) => { schemeIdx = 3; Invalidate(); });
        menu.Items.Add(colorMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => Application.Exit());
        ContextMenuStrip = menu;
    }

    private void CalcWidths()
    {
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
        {
            var sf = new StringFormat(StringFormat.GenericTypographic);
            col1_w = (int)Math.Max(g.MeasureString("C: 100%", f, 999, sf).Width,
                                    g.MeasureString("M: 100%", f, 999, sf).Width);
            col2_w = (int)Math.Max(g.MeasureString("上: 999.99M", f, 999, sf).Width,
                                    g.MeasureString("下: 999.99M", f, 999, sf).Width);
        }
    }

    private void UpdateStats(object sender, EventArgs e)
    {
        try
        {
            float cpu = cpuCounter.NextValue();
            float availMb = memAvailCounter.NextValue();
            float memPct = Math.Max(0, 100f - (availMb * 100f / totalMemMB));

            long recv = 0, sent = 0;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var s = n.GetIPv4Statistics();
                    recv += s.BytesReceived;
                    sent += s.BytesSent;
                }
            }

            var now = DateTime.Now;
            double elapsed = (now - prevNet).TotalSeconds;

            long drecv = recv - prevRecv;
            long dsent = sent - prevSent;
            if (drecv < 0) { drecv = recv; ResetNetCounters(); }
            if (dsent < 0) { dsent = sent; ResetNetCounters(); }

            double dlBps = elapsed > 0 ? drecv / elapsed : 0;
            double ulBps = elapsed > 0 ? dsent / elapsed : 0;
            prevRecv = recv; prevSent = sent; prevNet = now;

            txt_cpu = string.Format("C: {0:F0}%", cpu);
            txt_mem = string.Format("M: {0:F0}%", memPct);
            txt_ul = string.Format("上: {0}", Fmt(ulBps));
            txt_dl = string.Format("下: {0}", Fmt(dlBps));
            Invalidate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("SysMonitor: " + ex.Message);
        }
    }

    private string Fmt(double bps)
    {
        if (bps >= 1000000) return string.Format("{0:F1}M", bps / 1000000);
        return string.Format("{0:F2}K", bps / 1000);
    }

    private void DoLayout()
    {
        if (!IsHandleCreated) return;
        int sh = Screen.PrimaryScreen.Bounds.Height;
        int th = sh - Screen.PrimaryScreen.WorkingArea.Height;
        if (th < 6) th = 40;
        int barW = col1_w + col2_w + 20;
        SetWindowPos(Handle, (IntPtr)0, 60, 0, barW, th, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        int row1y = 3;
        int row2y = row1y + 15 + 6;

        var c = schemes[schemeIdx];
        using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            using (var b = new SolidBrush(c[0]))
                g.DrawString(txt_cpu, f, b, 8, row1y, sf);
            using (var b = new SolidBrush(c[1]))
                g.DrawString(txt_mem, f, b, 8, row2y, sf);

            int col2x = 8 + col1_w + 4;
            using (var b = new SolidBrush(c[2]))
                g.DrawString(txt_ul, f, b, col2x, row1y, sf);
            using (var b = new SolidBrush(c[3]))
                g.DrawString(txt_dl, f, b, col2x, row2y, sf);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (timer != null) timer.Dispose();
            if (cpuCounter != null) cpuCounter.Dispose();
            if (memAvailCounter != null) memAvailCounter.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
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
