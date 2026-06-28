using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Reflection;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PalLocalManagerNative;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var splash = new SplashForm();
        splash.Show();
        splash.Update();
        Application.DoEvents();

        var launchStarted = Stopwatch.StartNew();
        var mainForm = new MainForm();
        var remaining = 1200 - (int)launchStarted.ElapsedMilliseconds;
        if (remaining > 0)
        {
            Thread.Sleep(remaining);
        }

        splash.Close();
        Application.Run(mainForm);
    }
}

internal static class LogoAssets
{
    public static Image? LoadPalworldLogo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("palworld-logo.png", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "palworld-logo.png");
        if (File.Exists(filePath))
        {
            using var image = Image.FromFile(filePath);
            return new Bitmap(image);
        }

        return null;
    }
}

internal sealed class SplashForm : Form
{
    private static readonly Color SplashBg = Color.FromArgb(42, 44, 56);
    private static readonly Color SplashBorder = Color.FromArgb(129, 113, 255);
    private static readonly Color SplashText = Color.FromArgb(142, 146, 160);

    public SplashForm()
    {
        Width = 560;
        Height = 410;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = SplashBg;
        DoubleBuffered = true;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        var title = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 84,
            Text = "Royalty Server Manager",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            ForeColor = SplashText,
            BackColor = SplashBg
        };
        Controls.Add(title);

        var logoWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SplashBg,
            Padding = new Padding(40, 54, 40, 24)
        };
        Controls.Add(logoWrap);

        var logo = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = SplashBg,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        logo.Image = LogoAssets.LoadPalworldLogo();
        logoWrap.Controls.Add(logo);

        Resize += (_, _) => ApplyRoundedRegion();
        Shown += (_, _) => ApplyRoundedRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(SplashBorder, 1.3f);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        DrawRoundedRectangle(e.Graphics, pen, rect, 18);
    }

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 18);
        Region = new Region(path);
        Invalidate();
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = RoundedRect(bounds, radius);
        graphics.DrawPath(pen, path);
    }
}

internal sealed class ManagerConfig
{
    public string ProfileName { get; set; } = "Sakura Sweetheart";
    public string ServerRoot { get; set; } = @"C:\PalworldServer\SakuraSweetheart_99795188";
    public string SteamCmdDir { get; set; } = @"C:\PalworldServer\steamcmd";
    public string PlayitContainer { get; set; } = "playit-agent";
    public string PlayitPublicAddress { get; set; } = "";
    public bool GuardianEnabled { get; set; }
    public bool UpdateBeforeStart { get; set; }
    public int AutoBackupMinutes { get; set; }
    public int AutoRestartMinutes { get; set; }
    public string DiscordWebhook { get; set; } = "";
    public DateTime LastAutoBackup { get; set; } = DateTime.MinValue;
    public DateTime LastAutoRestart { get; set; } = DateTime.MinValue;
}

internal sealed class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private static readonly Color Bg = Color.FromArgb(16, 18, 25);
    private static readonly Color Sidebar = Color.FromArgb(8, 10, 15);
    private static readonly Color PanelBg = Color.FromArgb(28, 32, 43);
    private static readonly Color CardDark = Color.FromArgb(12, 14, 20);
    private static readonly Color Accent = Color.FromArgb(135, 108, 235);
    private static readonly Color Accent2 = Color.FromArgb(68, 196, 190);
    private static readonly Color Blue = Color.FromArgb(39, 137, 255);
    private static readonly Color Green = Color.FromArgb(105, 190, 113);
    private static readonly Color Warn = Color.FromArgb(245, 183, 77);
    private static readonly Color Danger = Color.FromArgb(217, 84, 94);
    private static readonly Color TextColor = Color.FromArgb(245, 247, 250);
    private static readonly Color Muted = Color.FromArgb(165, 174, 190);
    private const int ResizeGrip = 8;
    private const int CreateNoWindowFlag = 0x08000000;
    private const int DetachedProcessFlag = 0x00000008;

    private readonly string appDir = AppContext.BaseDirectory;
    private readonly string configPath;
    private readonly ManagerConfig config;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer schedulerTimer = new() { Interval = 30000 };

    private Process? serverProcess;
    private Label statusLabel = null!;
    private Label serverBadge = null!;
    private Label playitBadge = null!;
    private Label cardState = null!;
    private Label pidValue = null!;
    private Label uptimeValue = null!;
    private Label playersValue = null!;
    private Label topProfileTitle = null!;
    private Label heroProfileDescription = null!;
    private Label profileCardName = null!;
    private Label profileInstallPath = null!;
    private Label playitAddressValue = null!;
    private TextBox serverRootBox = null!;
    private TextBox playitBox = null!;
    private TextBox playitAddressBox = null!;
    private CheckBox guardianCheck = null!;
    private CheckBox updateBeforeStartCheck = null!;
    private Panel contentHost = null!;
    private readonly List<Panel> pages = new();
    private TextBox logBox = null!;
    private ComboBox logSourceBox = null!;
    private TextBox rconCommandBox = null!;
    private TextBox rconOutputBox = null!;
    private ListBox backupsList = null!;
    private ListBox modsList = null!;
    private readonly Dictionary<string, TextBox> settingBoxes = new();
    private string? currentServerLogPath;
    private string? currentPlayitLogPath;
    private Panel titleBar = null!;
    private Panel titleButtons = null!;

    public MainForm()
    {
        configPath = Path.Combine(appDir, "manager_config.json");
        config = LoadConfig();
        Text = "Pal Local Manager";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1050, 680);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        ControlBox = true;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9.5F);
        ResizeRedraw = true;
        var iconPath = Path.Combine(appDir, "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
        else
        {
            var embeddedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (embeddedIcon != null) Icon = embeddedIcon;
        }
        BuildUi();
        RefreshAll();
        refreshTimer.Tick += (_, _) => RefreshAll();
        refreshTimer.Start();
        schedulerTimer.Tick += async (_, _) => await RunScheduledJobs();
        schedulerTimer.Start();
    }

    private ManagerConfig LoadConfig()
    {
        try
        {
            if (File.Exists(configPath))
            {
                return JsonSerializer.Deserialize<ManagerConfig>(File.ReadAllText(configPath)) ?? new ManagerConfig();
            }
        }
        catch { }
        return new ManagerConfig();
    }

    private void SaveConfig()
    {
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string CurrentProfileName => string.IsNullOrWhiteSpace(config.ProfileName) ? DeriveProfileName(config.ServerRoot) : config.ProfileName.Trim();

    private string PalServerExe => Path.Combine(config.ServerRoot, "PalServer.exe");
    private string SettingsPath => Path.Combine(config.ServerRoot, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
    private string SavedDir => Path.Combine(config.ServerRoot, "Pal", "Saved");
    private string SavedLogsDir => Path.Combine(SavedDir, "Logs");
    private string BackupsDir => Directory.CreateDirectory(Path.Combine(config.ServerRoot, "_backups")).FullName;
    private string LogsDir => Directory.CreateDirectory(Path.Combine(config.ServerRoot, "_manager_logs")).FullName;
    private string ModsDir => Directory.CreateDirectory(Path.Combine(config.ServerRoot, "Pal", "Content", "Paks", "~mods")).FullName;
    private string SteamCmdExe => Path.Combine(config.SteamCmdDir, "steamcmd.exe");

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Bg,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        shell.Controls.Add(BuildTitleBar(), 0, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Bg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(root, 0, 1);

        var side = new Panel { Dock = DockStyle.Fill, BackColor = Sidebar, Padding = new Padding(12, 28, 12, 12) };
        root.Controls.Add(side, 0, 0);
        var iconPicture = new PictureBox { Dock = DockStyle.Top, Height = 162, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Sidebar };
        iconPicture.Image = LogoAssets.LoadPalworldLogo();
        side.Controls.Add(iconPicture);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 30, 0, 0), BackColor = Sidebar };
        side.Controls.Add(nav);
        AddNav(nav, "Back to profiles", () => SelectPage(0));
        AddNav(nav, "Settings", () => SelectPage(1));
        AddNav(nav, "Players", () => SelectPage(2));
        AddNav(nav, "Whitelist", () => SelectPage(3));
        AddNav(nav, "Open Live Map", () => SelectPage(11));
        AddNav(nav, "Backups", () => SelectPage(4));
        AddNav(nav, "Scheduler", () => SelectPage(5));
        AddNav(nav, "Tools", () => SelectPage(6));
        AddNav(nav, "RCON Console", () => SelectPage(7));
        AddNav(nav, "PalDefender", () => SelectPage(8));
        AddNav(nav, "Mods", () => SelectPage(9));
        AddNav(nav, "Logs", () => SelectPage(10));
        AddNav(nav, "Discord", () => SelectPage(12));
        AddNav(nav, "Event Mode", () => SelectPage(13));
        AddNav(nav, "Import / Export", () => SelectPage(14));
        AddNav(nav, "Multi Server", () => SelectPage(15));

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Bg, Padding = new Padding(24, 14, 24, 8) };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(main, 1, 0);

        var top = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        main.Controls.Add(top, 0, 0);
        topProfileTitle = Label(CurrentProfileName, 15, true, TextColor, Bg);
        topProfileTitle.Dock = DockStyle.Left;
        topProfileTitle.Width = 250;
        top.Controls.Add(topProfileTitle);
        playitBadge = Badge("Playit: unknown", PanelBg);
        playitBadge.Width = 240;
        top.Controls.Add(playitBadge);
        playitBadge.Dock = DockStyle.Right;
        serverBadge = Badge("Server: unknown", PanelBg);
        top.Controls.Add(serverBadge);
        serverBadge.Dock = DockStyle.Right;
        top.Controls.Add(Badge("LOCAL FREE", PanelBg));
        top.Controls[^1].Dock = DockStyle.Right;
        top.Controls.Add(Badge("v1.0", PanelBg));
        top.Controls[^1].Dock = DockStyle.Right;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = PanelBg, Padding = new Padding(10), WrapContents = false };
        main.Controls.Add(actions, 0, 1);
        actions.Controls.Add(Button("Start server", Blue, StartServer));
        actions.Controls.Add(Button("Restart", PanelBg, RestartServer));
        actions.Controls.Add(Button("Stop", PanelBg, StopServer));
        actions.Controls.Add(Button("Kill", Color.FromArgb(65, 27, 35), KillServer));
        actions.Controls.Add(Button("Update", PanelBg, async () => await UpdateServer()));
        actions.Controls.Add(Button("Backup", PanelBg, CreateBackup));
        actions.Controls.Add(Button("Start Playit", PanelBg, async () => await StartPlayit()));
        actions.Controls.Add(Button("Stop Playit", PanelBg, async () => await StopPlayit()));
        updateBeforeStartCheck = Check("Update before start", config.UpdateBeforeStart, v => { config.UpdateBeforeStart = v; SaveConfig(); });
        guardianCheck = Check("Guardian", config.GuardianEnabled, v => { config.GuardianEnabled = v; SaveConfig(); });
        actions.Controls.Add(updateBeforeStartCheck);
        actions.Controls.Add(guardianCheck);

        contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        main.Controls.Add(contentHost, 0, 2);
        pages.Add(ProfilePage());
        pages.Add(SettingsPage());
        pages.Add(PlayersPage());
        pages.Add(WhitelistPage());
        pages.Add(BackupsPage());
        pages.Add(SchedulerPage());
        pages.Add(ToolsPage());
        pages.Add(RconPage());
        pages.Add(PalDefenderPage());
        pages.Add(ModsPage());
        pages.Add(LogsPage());
        pages.Add(LiveMapPage());
        pages.Add(DiscordPage());
        pages.Add(EventModePage());
        pages.Add(ImportExportPage());
        pages.Add(MultiServerPage());
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            contentHost.Controls.Add(page);
        }
        SelectPage(0);

        statusLabel = Label("Ready", 9, false, Muted, Bg);
        statusLabel.Dock = DockStyle.Fill;
        main.Controls.Add(statusLabel, 0, 3);
    }

    private Panel ProfilePage()
    {
        var page = Page();
        var title = Label("Server Profiles", 24, true, TextColor, Bg);
        title.SetBounds(10, 18, 520, 38);
        page.Controls.Add(title);
        var hint = Label("Launch, monitor, back up, mod, and keep your Playit tunnel pointed at the right place.", 10.5F, false, Muted, Bg);
        hint.SetBounds(10, 58, 820, 24);
        page.Controls.Add(hint);

        var hero = Panel(10, 96, 1000, 76, PanelBg);
        hero.Controls.Add(LabelAt("Local Profile", 18, 14, 260, 24, TextColor, PanelBg, true, 12));
        heroProfileDescription = LabelAt(CurrentProfileName + " is wired to your Windows Palworld server folder and your Playit Docker agent.", 18, 42, 520, 22, Muted, PanelBg);
        hero.Controls.Add(heroProfileDescription);
        hero.Controls.Add(SmallButton("Create Server", 548, 22, async () => await CreateServerFlow(), Blue));
        hero.Controls.Add(SmallButton("Add Existing", 694, 22, AddExistingServerFlow));
        hero.Controls.Add(SmallButton("Playit Help", 836, 22, () => MessageBox.Show("Playit tunnel settings:\n\nType: Palworld or Custom UDP\nLocal address: 127.0.0.1\nLocal port: 8211\nPort count: 1\nProxy protocol: None", "Playit setup"), Blue));
        page.Controls.Add(hero);

        var card = Panel(10, 200, 430, 455, PanelBg);
        var header = Panel(0, 0, 430, 68, Accent);
        profileCardName = LabelAt(CurrentProfileName, 18, 7, 360, 28, Color.White, header.BackColor, true);
        header.Controls.Add(profileCardName);
        header.Controls.Add(LabelAt("local server profile", 18, 40, 320, 18, Color.FromArgb(232, 228, 255), header.BackColor));
        card.Controls.Add(header);
        card.Controls.Add(LabelAt("Install path", 20, 92, 180, 20, Muted, PanelBg));
        profileInstallPath = LabelAt(config.ServerRoot, 20, 116, 388, 22, TextColor, PanelBg);
        card.Controls.Add(profileInstallPath);
        var m1 = Panel(20, 154, 390, 68, CardDark);
        m1.Controls.Add(LabelAt("Port", 14, 8, 100, 18, Muted, CardDark));
        m1.Controls.Add(LabelAt("8211 / 27015", 14, 30, 120, 20, TextColor, CardDark, true));
        m1.Controls.Add(LabelAt("REST API", 148, 8, 100, 18, Muted, CardDark));
        m1.Controls.Add(LabelAt("8212", 148, 30, 100, 20, TextColor, CardDark, true));
        m1.Controls.Add(LabelAt("Version", 250, 8, 100, 18, Muted, CardDark));
        m1.Controls.Add(LabelAt("v0.7.x", 250, 30, 100, 20, TextColor, CardDark, true));
        card.Controls.Add(m1);
        var m2 = Panel(20, 238, 390, 96, CardDark);
        m2.Controls.Add(LabelAt("Players", 14, 8, 100, 18, Muted, CardDark));
        playersValue = LabelAt("0 / 32", 14, 30, 100, 20, TextColor, CardDark, true);
        m2.Controls.Add(playersValue);
        m2.Controls.Add(LabelAt("Uptime", 148, 8, 100, 18, Muted, CardDark));
        uptimeValue = LabelAt("--", 148, 30, 100, 20, TextColor, CardDark, true);
        m2.Controls.Add(uptimeValue);
        m2.Controls.Add(LabelAt("PID", 250, 8, 100, 18, Muted, CardDark));
        pidValue = LabelAt("--", 250, 30, 100, 20, TextColor, CardDark, true);
        m2.Controls.Add(pidValue);
        card.Controls.Add(m2);
        cardState = LabelAt("STOPPED", 20, 352, 130, 24, Muted, PanelBg, true);
        card.Controls.Add(cardState);
        card.Controls.Add(SmallButton("Restart", 20, 392, RestartServer));
        card.Controls.Add(SmallButton("Stop", 112, 392, StopServer));
        card.Controls.Add(SmallButton("Kill", 204, 392, KillServer, Color.FromArgb(65, 27, 35)));
        card.Controls.Add(SmallButton("Update", 296, 392, async () => await UpdateServer()));
        page.Controls.Add(card);

        var guide = Panel(468, 200, 542, 220, PanelBg);
        guide.Controls.Add(LabelAt("Connection Checklist", 22, 18, 340, 30, TextColor, PanelBg, true, 15));
        guide.Controls.Add(LabelAt("Server listening locally", 22, 64, 260, 24, Muted, PanelBg));
        guide.Controls.Add(LabelAt("Palworld UDP 8211", 318, 64, 180, 24, Accent2, PanelBg, true));
        guide.Controls.Add(LabelAt("Playit target", 22, 100, 260, 24, Muted, PanelBg));
        guide.Controls.Add(LabelAt("127.0.0.1:8211 UDP", 318, 100, 190, 24, Accent2, PanelBg, true));
        guide.Controls.Add(LabelAt("Friends join with", 22, 136, 260, 24, Muted, PanelBg));
        playitAddressValue = LabelAt(GetPlayitDisplayAddress(), 318, 136, 210, 24, Accent2, PanelBg, true);
        guide.Controls.Add(playitAddressValue);
        guide.Controls.Add(SmallButton("Copy setup notes", 22, 174, () =>
        {
            Clipboard.SetText("Playit tunnel: 127.0.0.1:8211 UDP, port count 1, proxy protocol none.");
            Status("Playit setup notes copied.");
        }, Blue));
        page.Controls.Add(guide);

        var health = Panel(468, 444, 542, 211, PanelBg);
        health.Controls.Add(LabelAt("Server Health", 22, 18, 340, 30, TextColor, PanelBg, true, 15));
        health.Controls.Add(LabelAt("Guardian", 22, 66, 180, 24, Muted, PanelBg));
        health.Controls.Add(LabelAt(config.GuardianEnabled ? "Enabled" : "Disabled", 318, 66, 160, 24, config.GuardianEnabled ? Green : Warn, PanelBg, true));
        health.Controls.Add(LabelAt("Auto-update before start", 22, 102, 220, 24, Muted, PanelBg));
        health.Controls.Add(LabelAt(config.UpdateBeforeStart ? "Enabled" : "Disabled", 318, 102, 160, 24, config.UpdateBeforeStart ? Green : Warn, PanelBg, true));
        health.Controls.Add(LabelAt("Backups folder", 22, 138, 180, 24, Muted, PanelBg));
        health.Controls.Add(LabelAt(BackupsDir, 318, 138, 200, 24, TextColor, PanelBg));
        page.Controls.Add(health);

        var paths = Panel(10, 682, 1000, 126, PanelBg);
        paths.Controls.Add(LabelAt("Server folder", 14, 14, 120, 22, Muted, PanelBg));
        serverRootBox = new TextBox { Text = config.ServerRoot, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        serverRootBox.SetBounds(135, 12, 620, 24);
        paths.Controls.Add(serverRootBox);
        paths.Controls.Add(SmallButton("Browse", 765, 10, BrowseServerRoot));
        paths.Controls.Add(SmallButton("Save", 855, 10, SaveServerRoot));
        paths.Controls.Add(LabelAt("Playit container", 14, 54, 120, 22, Muted, PanelBg));
        playitBox = new TextBox { Text = config.PlayitContainer, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        playitBox.SetBounds(135, 52, 620, 24);
        paths.Controls.Add(playitBox);
        paths.Controls.Add(SmallButton("Save", 765, 50, SavePlayitName));
        paths.Controls.Add(LabelAt("Playit address", 14, 82, 120, 22, Muted, PanelBg));
        playitAddressBox = new TextBox { Text = config.PlayitPublicAddress, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        playitAddressBox.SetBounds(135, 80, 620, 24);
        paths.Controls.Add(playitAddressBox);
        paths.Controls.Add(SmallButton("Save", 765, 78, SavePlayitAddress));
        page.Controls.Add(paths);
        return page;
    }

    private Panel SettingsPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Settings", 12, 12, 220, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Loaded 0 unknown key(s) preserved.", 12, 54, 850, 22, Muted, Bg));

        var topActions = Panel(386, 10, 616, 48, Bg);
        topActions.Controls.Add(SmallButton("Copy from...", 0, 6, () => Status("Copy settings placeholder.")));
        topActions.Controls.Add(SmallButton("Revert changes", 124, 6, LoadSettingsIntoForm));
        topActions.Controls.Add(SmallButton("Reload from file", 274, 6, LoadSettingsIntoForm));
        topActions.Controls.Add(SmallButton("Save", 442, 6, SaveSettingsFromForm, Blue));
        page.Controls.Add(topActions);

        var guardian = Panel(12, 82, 990, 104, PanelBg);
        guardian.Controls.Add(LabelAt("Guardian", 18, 12, 250, 24, TextColor, PanelBg, true));
        guardian.Controls.Add(Check("Enable the Guardian for this server", config.GuardianEnabled, v => { config.GuardianEnabled = v; SaveConfig(); }).WithBounds(18, 42, 280, 24));
        guardian.Controls.Add(LabelAt("When enabled, this app keeps the server online: it restarts after a crash or freeze, can apply updates, and sends Discord notices.", 18, 70, 900, 22, Muted, PanelBg));
        page.Controls.Add(guardian);

        var nav = Panel(12, 210, 990, 44, Bg);
        var host = Panel(12, 270, 990, 430, Bg);
        page.Controls.Add(nav);
        page.Controls.Add(host);

        var panels = new List<Panel>
        {
            SettingsGeneralPanel(),
            SettingsNetworkPanel(),
            SettingsMultipliersPanel(),
            SettingsRulesPanel(),
            SettingsAdvancedPanel(),
            SettingsOptimizationPanel(),
            SettingsAllPanel()
        };
        var names = new[] { "General", "Network & API", "Multipliers", "Rules", "Advanced", "Optimization", "All settings" };
        void ShowSettingsTab(int index)
        {
            foreach (var p in panels) p.Visible = false;
            panels[index].Visible = true;
            panels[index].BringToFront();
        }
        for (var i = 0; i < names.Length; i++)
        {
            var local = i;
            var b = SmallButton(names[i], i * 132, 4, () => ShowSettingsTab(local), i == 0 ? Accent : Bg);
            nav.Controls.Add(b);
        }
        foreach (var p in panels)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            host.Controls.Add(p);
        }
        ShowSettingsTab(0);
        LoadSettingsIntoForm();
        return page;
    }

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 12, 16) };
        titleBar = bar;
        var title = Label("  Pal Local Manager", 9, true, TextColor, bar.BackColor);
        title.Dock = DockStyle.Left;
        title.Width = 260;
        bar.Controls.Add(title);

        var buttons = new Panel
        {
            Dock = DockStyle.Right,
            BackColor = bar.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Width = 144,
        };
        titleButtons = buttons;
        var min = TitleButton("\u2212", () => WindowState = FormWindowState.Minimized);
        var max = TitleButton("\u25A1", () => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
        var close = TitleButton("\u00D7", () => Close());
        close.BackColor = Color.FromArgb(40, 14, 20);
        min.SetBounds(0, 0, 48, 34);
        max.SetBounds(48, 0, 48, 34);
        close.SetBounds(96, 0, 48, 34);
        buttons.Controls.Add(min);
        buttons.Controls.Add(max);
        buttons.Controls.Add(close);
        bar.Controls.Add(buttons);

        void StartWindowDrag(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        void ToggleMaximize(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            }
        }

        void OpenSystemMenu(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowSystemMenu(PointToScreen(e.Location));
            }
        }

        bar.MouseDown += (_, e) => StartWindowDrag(e);
        title.MouseDown += (_, e) => StartWindowDrag(e);
        bar.MouseDoubleClick += (_, e) => ToggleMaximize(e);
        title.MouseDoubleClick += (_, e) => ToggleMaximize(e);
        bar.MouseUp += (_, e) => OpenSystemMenu(e);
        title.MouseUp += (_, e) => OpenSystemMenu(e);
        return bar;
    }

    private Panel SettingsGeneralPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Server identity", 18, 10, 260, 28, Accent, Bg, true, 13));
        AddSettingText(p, "ServerName", "Server name (in-game)", 18, 60, "Sakura Sweetheart");
        AddSettingText(p, "ServerDescription", "Description", 18, 100, "");
        AddSettingText(p, "ServerPassword", "Server password (to join)", 18, 140, "Sakura26");
        AddSettingText(p, "AdminPassword", "Admin password (REST/RCON)", 18, 180, "SakuraAdmin2026!");
        AddSettingText(p, "ServerPlayerMaxNum", "Max players", 18, 220, "32");
        AddSettingText(p, "Difficulty", "Difficulty", 18, 260, "None");
        AddSettingText(p, "Region", "Region", 18, 300, "");
        p.Controls.Add(Check("Update Palworld before each server start", config.UpdateBeforeStart, v => { config.UpdateBeforeStart = v; SaveConfig(); }).WithBounds(240, 348, 360, 24));
        return p;
    }

    private Panel SettingsNetworkPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Ports & APIs", 18, 10, 260, 28, Accent, Bg, true, 13));
        AddSettingText(p, "PublicIP", "Public IP (optional)", 18, 58, "");
        AddSettingText(p, "PublicPort", "Public port", 18, 98, "8211");
        p.Controls.Add(Check("Enable REST API (recommended)", true, _ => { }).WithBounds(18, 142, 310, 24));
        AddSettingText(p, "RESTAPIPort", "REST API port", 38, 178, "8212");
        p.Controls.Add(Check("Enable RCON (admin fallback)", true, _ => { }).WithBounds(18, 222, 310, 24));
        AddSettingText(p, "RCONPort", "RCON port", 38, 258, "25575");
        p.Controls.Add(Check("Authentication enabled (recommended)", true, _ => { }).WithBounds(18, 306, 330, 24));
        p.Controls.Add(Check("Allow client-side mods", true, _ => { }).WithBounds(18, 334, 280, 24));
        return p;
    }

    private Panel SettingsMultipliersPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Time speed & XP", 18, 10, 260, 28, Accent, Bg, true, 13));
        AddSliderRow(p, "Day speed (x)", 18, 58, "1");
        AddSliderRow(p, "Night speed (x)", 18, 98, "1");
        AddSliderRow(p, "XP gain (x)", 18, 138, "1");
        p.Controls.Add(LabelAt("Pals", 18, 190, 260, 28, Accent, Bg, true, 13));
        AddSliderRow(p, "Capture rate (x)", 18, 238, "1");
        AddSliderRow(p, "Pal spawn rate (x)", 18, 278, "1");
        AddSliderRow(p, "Pal damage attack (x)", 18, 318, "1");
        AddSliderRow(p, "Egg hatch time (hours)", 18, 358, "72");
        return p;
    }

    private Panel SettingsRulesPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Death & respawn", 18, 10, 260, 28, Accent, Bg, true, 13));
        p.Controls.Add(Check("Hardcore mode (death = character wiped)", false, _ => { }).WithBounds(18, 50, 360, 24));
        p.Controls.Add(Check("Pals lost on death", false, _ => { }).WithBounds(18, 80, 260, 24));
        AddSettingText(p, "DeathPenalty", "Death penalty", 18, 124, "All");
        AddSettingText(p, "BlockRespawnTime", "Respawn delay (s)", 18, 164, "5");
        p.Controls.Add(LabelAt("Guild & camp", 18, 220, 260, 28, Accent, Bg, true, 13));
        AddSettingText(p, "GuildPlayerMaxNum", "Max players per guild", 18, 268, "20");
        AddSettingText(p, "BaseCampMaxNumInGuild", "Max camps per guild", 18, 308, "4");
        AddSettingText(p, "BaseCampWorkerMaxNum", "Max worker Pals per camp", 18, 348, "15");
        return p;
    }

    private Panel SettingsAdvancedPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Performance & logs", 18, 10, 260, 28, Accent, Bg, true, 13));
        AddSettingText(p, "MaxBuildingLimitNum", "Building limit (0 = default)", 18, 58, "0");
        AddSettingText(p, "ServerReplicatePawnCullDistance", "Pawn replication distance (cm)", 18, 98, "15000");
        AddSettingText(p, "ChatPostLimitPerMinute", "Chat limit per minute", 18, 138, "30");
        AddSettingText(p, "LogFormatType", "Log format", 18, 178, "Text");
        p.Controls.Add(LabelAt("Fast travel & world", 18, 232, 260, 28, Accent, Bg, true, 13));
        p.Controls.Add(Check("Fast travel enabled", true, _ => { }).WithBounds(18, 272, 280, 24));
        p.Controls.Add(Check("Pick start location on the map", true, _ => { }).WithBounds(18, 302, 320, 24));
        p.Controls.Add(Check("Enable enemy invasions", true, _ => { }).WithBounds(18, 332, 300, 24));
        p.Controls.Add(Check("Show join/leave messages", true, _ => { }).WithBounds(18, 362, 320, 24));
        return p;
    }

    private Panel SettingsOptimizationPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("Tuning options based on community-tested safe recommendations.", 18, 10, 760, 24, TextColor, Bg));
        p.Controls.Add(SmallButton("Apply safe recommendations", 18, 50, () => Status("Safe recommendations staged."), Blue));
        p.Controls.Add(SmallButton("Restore Palworld defaults", 250, 50, () => Status("Defaults restored placeholder.")));
        p.Controls.Add(LabelAt("Network & tick rate (Engine.ini)", 18, 112, 360, 28, Accent, Bg, true, 13));
        AddSettingText(p, "ServerTickRate", "Server tick rate", 18, 160, "30");
        AddSettingText(p, "PerPlayerBandwidth", "Per-player bandwidth (bytes/s)", 18, 200, "15000");
        AddSettingText(p, "AssumedInternetSpeed", "Assumed internet speed (bytes/s)", 18, 240, "10000");
        p.Controls.Add(LabelAt("Server load (PalWorldSettings.ini)", 18, 304, 400, 28, Accent, Bg, true, 13));
        AddSettingText(p, "BaseCampWorkerMaxNum", "Workers per base", 18, 352, "15");
        AddSettingText(p, "DropItemMaxNum", "Max ground items", 18, 392, "3000");
        return p;
    }

    private Panel SettingsAllPanel()
    {
        var p = Page();
        p.Controls.Add(LabelAt("This tab exposes every major setting. If you do not understand one, leave it on default.", 18, 10, 860, 42, TextColor, Bg));
        p.Controls.Add(LabelAt("Identity & network", 18, 74, 300, 28, Accent, Bg, true, 13));
        AddSettingText(p, "BanListURL", "Shared ban list URL", 18, 122, "https://api.palworldgame.com/api/banlist.txt");
        p.Controls.Add(Check("LAN / local multiplayer", false, _ => { }).WithBounds(18, 166, 260, 24));
        p.Controls.Add(Check("Steam (PC)", true, _ => { }).WithBounds(18, 206, 120, 24));
        p.Controls.Add(Check("Xbox", true, _ => { }).WithBounds(140, 206, 90, 24));
        p.Controls.Add(Check("PlayStation 5", true, _ => { }).WithBounds(230, 206, 160, 24));
        p.Controls.Add(Check("Mac", true, _ => { }).WithBounds(390, 206, 80, 24));
        p.Controls.Add(LabelAt("Spawn & randomizer", 18, 268, 300, 28, Accent, Bg, true, 13));
        AddSettingText(p, "CoopPlayerMaxNum", "Coop party size", 18, 316, "4");
        AddSettingText(p, "RandomizerType", "Pal randomizer", 18, 356, "None");
        AddSettingText(p, "RandomizerSeed", "Randomizer seed", 18, 396, "");
        return p;
    }

    private void AddSettingText(Panel parent, string key, string label, int x, int y, string fallback)
    {
        parent.Controls.Add(LabelAt(label, x, y + 4, 220, 24, Muted, parent.BackColor));
        var box = new TextBox { Text = fallback, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        box.SetBounds(x + 240, y, 700, 30);
        parent.Controls.Add(box);
        settingBoxes[key] = box;
    }

    private void AddSliderRow(Panel parent, string label, int x, int y, string value)
    {
        parent.Controls.Add(LabelAt(label, x, y + 2, 230, 24, TextColor, parent.BackColor, true));
        var slider = new TrackBar { Minimum = 0, Maximum = 100, Value = 20, TickStyle = TickStyle.None, BackColor = parent.BackColor };
        slider.SetBounds(x + 260, y, 560, 28);
        parent.Controls.Add(slider);
        parent.Controls.Add(TextInput(value, x + 840, y, 90));
    }

    private Button TitleButton(string text, Action action)
    {
        var b = new Button { Text = text, Width = 48, Height = 34, BackColor = Color.FromArgb(10, 12, 16), ForeColor = TextColor, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PanelBg;
        b.Click += (_, _) => action();
        return b;
    }

    private Panel PlayersPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Players", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Every player seen by this server. Online data refreshes from RCON when available.", 12, 56, 760, 24, Muted, Bg));
        page.Controls.Add(SmallButton("Refresh", 880, 28, () => SendRcon("ShowPlayers"), Blue));

        page.Controls.Add(LabelAt("Online players", 12, 112, 260, 28, TextColor, Bg, true, 14));
        page.Controls.Add(LabelAt("Currently connected to this server.", 12, 140, 520, 22, Muted, Bg));
        var table = Panel(12, 178, 980, 100, PanelBg);
        var head = Panel(0, 0, 980, 36, CardDark);
        head.Controls.Add(LabelAt("Display name", 18, 9, 260, 20, Muted, CardDark));
        head.Controls.Add(LabelAt("Steam ID", 500, 9, 180, 20, Muted, CardDark));
        head.Controls.Add(LabelAt("Level", 665, 9, 90, 20, Muted, CardDark));
        head.Controls.Add(LabelAt("Ping (ms)", 760, 9, 100, 20, Muted, CardDark));
        head.Controls.Add(LabelAt("Action", 880, 9, 90, 20, Muted, CardDark));
        table.Controls.Add(head);
        table.Controls.Add(LabelAt("No online players detected yet.", 18, 55, 360, 22, Muted, PanelBg));
        page.Controls.Add(table);

        page.Controls.Add(LabelAt("Player history", 12, 328, 300, 30, TextColor, Bg, true, 14));
        page.Controls.Add(LabelAt("Players will appear here after you run ShowPlayers or they join while the server is tracked.", 12, 358, 760, 22, Muted, Bg));
        var empty = Panel(12, 395, 980, 230, PanelBg);
        empty.Controls.Add(LabelAt("Nobody to show yet", 390, 88, 260, 26, TextColor, PanelBg, true, 13));
        empty.Controls.Add(LabelAt("Start the server and use Refresh to query connected players.", 312, 120, 420, 22, Muted, PanelBg));
        page.Controls.Add(empty);
        return page;
    }

    private Panel WhitelistPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Whitelist", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Pre-authorized SteamIDs for this server. Useful for private servers.", 12, 56, 760, 24, Muted, Bg));

        var mode = Panel(12, 96, 980, 104, PanelBg);
        mode.Controls.Add(LabelAt("Whitelist mode", 16, 14, 260, 24, TextColor, PanelBg, true, 11));
        mode.Controls.Add(LabelAt("Native mode stores the list locally. PalDefender mode writes to the mod config folder.", 16, 42, 850, 22, Muted, PanelBg));
        mode.Controls.Add(LabelAt("○ Native (REST kick)", 16, 72, 220, 22, TextColor, PanelBg));
        mode.Controls.Add(LabelAt("○ PalDefender (mod-enforced)", 240, 72, 270, 22, TextColor, PanelBg));
        page.Controls.Add(mode);

        var add = Panel(12, 224, 980, 114, PanelBg);
        add.Controls.Add(LabelAt("Add a SteamID", 16, 14, 260, 24, TextColor, PanelBg, true, 11));
        var steam = TextInput("", 16, 62, 220);
        var name = TextInput("", 246, 62, 220);
        var notes = TextInput("", 476, 62, 380);
        add.Controls.Add(LabelAt("SteamID", 16, 42, 120, 18, Muted, PanelBg));
        add.Controls.Add(LabelAt("Display name", 246, 42, 160, 18, Muted, PanelBg));
        add.Controls.Add(LabelAt("Notes", 476, 42, 160, 18, Muted, PanelBg));
        add.Controls.Add(steam);
        add.Controls.Add(name);
        add.Controls.Add(notes);
        add.Controls.Add(SmallButton("+ Add", 872, 58, () => Status("Whitelist entry staged locally."), Accent));
        page.Controls.Add(add);

        var list = Panel(12, 362, 980, 250, PanelBg);
        list.Controls.Add(LabelAt("No whitelist entry", 385, 92, 260, 26, TextColor, PanelBg, true, 13));
        list.Controls.Add(LabelAt("Add a SteamID above to start.", 390, 124, 360, 22, Muted, PanelBg));
        page.Controls.Add(list);
        return page;
    }

    private Panel RconPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("RCON Console", 12, 18, 400, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Send admin commands to the running server: broadcast, kick/ban, save, info, and player queries.", 12, 56, 900, 24, Muted, Bg));
        page.Controls.Add(SmallButton("ShowPlayers", 12, 102, () => SendRcon("ShowPlayers")));
        page.Controls.Add(SmallButton("Save", 140, 102, () => SendRcon("Save")));
        page.Controls.Add(SmallButton("Info", 230, 102, () => SendRcon("Info")));
        page.Controls.Add(SmallButton("Broadcast", 320, 102, () => SendRcon("Broadcast Server_restart_soon")));
        var box = Panel(12, 150, 980, 486, PanelBg);
        box.Controls.Add(LabelAt("Raw RCON command", 18, 18, 300, 26, Accent, PanelBg, true, 12));
        box.Controls.Add(LabelAt("Type any command. Server reply appears below. Up/Down history can be added later.", 18, 50, 760, 22, Muted, PanelBg));
        rconCommandBox = new TextBox { Text = "ShowPlayers", BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        rconCommandBox.SetBounds(18, 88, 790, 26);
        box.Controls.Add(rconCommandBox);
        box.Controls.Add(SmallButton("Send", 822, 86, () => SendRcon(rconCommandBox.Text), Blue));
        box.Controls.Add(LabelAt("Server reply", 18, 134, 160, 20, Muted, PanelBg));
        rconOutputBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        rconOutputBox.SetBounds(18, 160, 930, 240);
        rconOutputBox.Text = "Server reply will appear here.";
        box.Controls.Add(rconOutputBox);
        box.Controls.Add(LabelAt("Available RCON commands: Info, Save, ShowPlayers, Broadcast, KickPlayer, BanPlayer, TeleportToPlayer.", 18, 426, 850, 22, Muted, PanelBg));
        page.Controls.Add(box);
        return page;
    }

    private Panel PalDefenderPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("PalDefender", 12, 18, 360, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Manage PalDefender configuration files and quick RCON/admin commands.", 12, 56, 760, 24, Muted, Bg));
        page.Controls.Add(SmallButton("Regenerate Token", 620, 94, () => Status("Token regeneration placeholder.")));
        page.Controls.Add(SmallButton("Open folder", 780, 94, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender"))));
        page.Controls.Add(SmallButton("Rescan", 910, 94, () => Status("PalDefender files rescanned.")));

        var panel = Panel(12, 146, 980, 490, PanelBg);
        panel.Controls.Add(LabelAt("Config.json", 18, 16, 240, 26, TextColor, PanelBg, true, 12));
        panel.Controls.Add(LabelAt(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender", "Config.json"), 18, 48, 880, 20, Muted, PanelBg));
        panel.Controls.Add(LabelAt("version", 18, 88, 180, 22, TextColor, PanelBg, true));
        panel.Controls.Add(TextInput("1.0.0", 270, 84, 670));
        panel.Controls.Add(LabelAt("MOTD", 18, 158, 180, 22, TextColor, PanelBg, true));
        var motd = new TextBox { Multiline = true, Text = "[\r\n  \"Welcome {PlayerName} to {ServerName}!\",\r\n  \"GlobalPalboxImport: {AllowGlobalPalboxImport}\",\r\n  \"GlobalPalboxExport: {AllowGlobalPalboxExport}\"\r\n]", BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical };
        motd.SetBounds(270, 132, 670, 98);
        panel.Controls.Add(motd);
        var checks = new[] { "exitServerOnStartupFailure", "preventAdminPasswordInChat", "shouldWarnCheaters", "shouldKickCheaters", "shouldBanCheaters", "shouldIPBanCheaters" };
        for (var i = 0; i < checks.Length; i++)
        {
            panel.Controls.Add(LabelAt(checks[i], 18, 260 + i * 28, 250, 22, TextColor, PanelBg));
            panel.Controls.Add(Check("", i < 4, _ => { }).WithBounds(270, 258 + i * 28, 30, 22));
        }
        panel.Controls.Add(LabelAt("RCONTimeout", 18, 438, 180, 22, TextColor, PanelBg, true));
        panel.Controls.Add(TextInput("31.0", 270, 434, 670));
        page.Controls.Add(panel);
        return page;
    }
    private Panel BackupsPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Backups", 12, 12, 300, 34, TextColor, Bg, true, 18));
        page.Controls.Add(SmallButton("Backup Now", 12, 58, CreateBackup, Blue));
        page.Controls.Add(SmallButton("Restore Selected", 132, 58, RestoreSelectedBackup));
        page.Controls.Add(SmallButton("Refresh", 282, 58, RefreshBackups));
        backupsList = new ListBox { BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        backupsList.SetBounds(12, 105, 990, 560);
        page.Controls.Add(backupsList);
        RefreshBackups();
        return page;
    }

    private Panel SchedulerPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Scheduler & Discord", 12, 12, 380, 34, TextColor, Bg, true, 18));
        page.Controls.Add(LabelAt("Auto backup every N minutes", 12, 70, 240, 24, Muted, Bg));
        var backupBox = TextInput(config.AutoBackupMinutes.ToString(), 270, 68, 170);
        page.Controls.Add(backupBox);
        page.Controls.Add(LabelAt("Auto restart every N minutes", 12, 110, 240, 24, Muted, Bg));
        var restartBox = TextInput(config.AutoRestartMinutes.ToString(), 270, 108, 170);
        page.Controls.Add(restartBox);
        page.Controls.Add(LabelAt("Discord webhook URL", 12, 150, 240, 24, Muted, Bg));
        var webhookBox = TextInput(config.DiscordWebhook, 270, 148, 620);
        page.Controls.Add(webhookBox);
        page.Controls.Add(SmallButton("Save Scheduler", 270, 192, () =>
        {
            config.AutoBackupMinutes = ParseInt(backupBox.Text);
            config.AutoRestartMinutes = ParseInt(restartBox.Text);
            config.DiscordWebhook = webhookBox.Text.Trim();
            SaveConfig();
            Status("Scheduler saved.");
        }, Blue));
        page.Controls.Add(SmallButton("Send Test Discord", 420, 192, async () => await DiscordNotify("Pal Local Manager test message.")));
        return page;
    }

    private Panel ToolsPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Tools", 12, 12, 300, 34, TextColor, Bg, true, 18));
        page.Controls.Add(SmallButton("Install SteamCMD", 12, 62, async () => await InstallSteamCmd()));
        page.Controls.Add(SmallButton("Open Server Folder", 162, 62, () => OpenPath(config.ServerRoot)));
        page.Controls.Add(SmallButton("Open Config Folder", 332, 62, () => OpenPath(Path.GetDirectoryName(SettingsPath)!)));
        page.Controls.Add(SmallButton("Open PalDefender", 502, 62, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender"))));
        page.Controls.Add(SmallButton("Docker ps", 672, 62, ShowDockerPs));
        return page;
    }

    private Panel ModsPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Mods", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Manage UE4SS, PalDefender, and manually downloaded Palworld mod files.", 12, 56, 900, 24, Muted, Bg));

        var ue = Panel(12, 104, 980, 206, PanelBg);
        ue.Controls.Add(LabelAt("UE4SS - mod loader", 18, 18, 360, 26, TextColor, PanelBg, true, 13));
        ue.Controls.Add(LabelAt("Required for Lua/LogicMods on a dedicated server. Install once per server.", 18, 48, 760, 22, Muted, PanelBg));
        ue.Controls.Add(LabelAt("✓ UE4SS folder ready", 18, 78, 360, 22, Green, PanelBg, true));
        ue.Controls.Add(SmallButton("Install UE4SS", 18, 112, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "ue4ss")), Accent));
        ue.Controls.Add(SmallButton("Check for update", 162, 112, () => Status("UE4SS update check placeholder.")));
        ue.Controls.Add(SmallButton("Repair / Reinstall", 18, 152, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "ue4ss"))));
        ue.Controls.Add(SmallButton("Remove", 190, 152, () => Status("Remove UE4SS manually from the folder."), Color.FromArgb(65, 27, 35)));
        page.Controls.Add(ue);

        var pd = Panel(12, 336, 980, 214, PanelBg);
        pd.Controls.Add(LabelAt("PalDefender - admin mod", 18, 18, 360, 26, TextColor, PanelBg, true, 13));
        pd.Controls.Add(LabelAt("Adds admin tools, whitelist, REST helpers, and anti-cheat style protections.", 18, 48, 840, 22, Muted, PanelBg));
        pd.Controls.Add(LabelAt("✓ PalDefender folder ready", 18, 78, 360, 22, Green, PanelBg, true));
        pd.Controls.Add(SmallButton("Install PalDefender", 18, 112, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender")), Accent));
        pd.Controls.Add(SmallButton("Check for update", 198, 112, () => Status("PalDefender update check placeholder.")));
        pd.Controls.Add(SmallButton("Repair / Reinstall", 18, 152, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender"))));
        pd.Controls.Add(SmallButton("Open folder", 190, 152, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "PalDefender"))));
        pd.Controls.Add(SmallButton("Remove", 320, 152, () => Status("Remove PalDefender manually from the folder."), Color.FromArgb(65, 27, 35)));
        page.Controls.Add(pd);

        var more = Panel(12, 576, 980, 208, PanelBg);
        more.Controls.Add(LabelAt("Manual mod installs", 18, 18, 300, 26, TextColor, PanelBg, true, 13));
        more.Controls.Add(LabelAt("Download mods from Nexus, then import zips/files here. Pak mods go to ~mods; Lua-style files go to LogicMods.", 18, 48, 880, 22, Muted, PanelBg));
        more.Controls.Add(SmallButton("Open Nexus", 18, 88, () => Process.Start(new ProcessStartInfo("https://www.nexusmods.com/palworld") { UseShellExecute = true })));
        more.Controls.Add(SmallButton("Install Mod Zip", 134, 88, InstallModZip, Blue));
        more.Controls.Add(SmallButton("Import Files", 278, 88, ImportModFiles));
        more.Controls.Add(SmallButton("Open ~mods", 18, 128, () => OpenPath(ModsDir)));
        more.Controls.Add(SmallButton("UE4SS Mods", 144, 128, () => OpenPath(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "ue4ss", "Mods"))));
        modsList = new ListBox { BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        modsList.SetBounds(18, 168, 930, 28);
        more.Controls.Add(modsList);
        page.Controls.Add(more);
        RefreshMods();
        return page;
    }

    private Panel LogsPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Logs", 12, 14, 120, 28, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Debug both the Palworld server and the Playit tunnel without showing any console windows.", 12, 48, 820, 22, Muted, Bg));
        page.Controls.Add(LabelAt("Source", 12, 82, 80, 22, TextColor, Bg, true));
        logSourceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardDark, ForeColor = TextColor, Width = 180 };
        logSourceBox.Items.AddRange(new object[] { "Server", "Playit", "SteamCMD", "All" });
        logSourceBox.SelectedIndexChanged += (_, _) => RefreshLogs();
        logSourceBox.SetBounds(102, 78, 160, 26);
        page.Controls.Add(logSourceBox);
        if (logSourceBox.SelectedIndex < 0) logSourceBox.SelectedIndex = 0;
        page.Controls.Add(SmallButton("Refresh Logs", 282, 76, RefreshLogs));
        page.Controls.Add(SmallButton("Open Logs Folder", 412, 76, () => OpenPath(LogsDir)));
        page.Controls.Add(SmallButton("Capture Playit Now", 570, 76, () => { CapturePlayitLogs(); RefreshLogs(); }, Accent));
        logBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        logBox.SetBounds(12, 118, 990, 550);
        page.Controls.Add(logBox);
        RefreshLogs();
        return page;
    }

    private Panel LiveMapPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Live Map", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Configure and open a local or Playit-exposed live map URL.", 12, 56, 760, 24, Muted, Bg));
        var panel = Panel(12, 104, 980, 180, PanelBg);
        panel.Controls.Add(LabelAt("Map URL", 18, 24, 140, 22, TextColor, PanelBg, true));
        var mapUrl = TextInput("http://127.0.0.1:8212", 160, 20, 610);
        panel.Controls.Add(mapUrl);
        panel.Controls.Add(SmallButton("Open", 790, 18, () => Process.Start(new ProcessStartInfo(mapUrl.Text) { UseShellExecute = true }), Blue));
        panel.Controls.Add(LabelAt("Tip: if you use a modded live map, paste its URL here. This app keeps it local and free.", 18, 70, 850, 24, Muted, PanelBg));
        page.Controls.Add(panel);
        return page;
    }

    private Panel DiscordPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Discord", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Alerts, status messages, and webhook notifications without a paid manager.", 12, 56, 850, 24, Muted, Bg));
        var panel = Panel(12, 104, 980, 260, PanelBg);
        panel.Controls.Add(LabelAt("Webhook URL", 18, 24, 160, 22, TextColor, PanelBg, true));
        var webhook = TextInput(config.DiscordWebhook, 180, 20, 680);
        panel.Controls.Add(webhook);
        panel.Controls.Add(SmallButton("Save", 875, 18, () =>
        {
            config.DiscordWebhook = webhook.Text.Trim();
            SaveConfig();
            Status("Discord webhook saved.");
        }, Blue));
        panel.Controls.Add(LabelAt("Alerts", 18, 80, 160, 22, TextColor, PanelBg, true));
        panel.Controls.Add(Check("Server started", true, _ => { }).WithBounds(180, 78, 160, 22));
        panel.Controls.Add(Check("Backup created", true, _ => { }).WithBounds(340, 78, 170, 22));
        panel.Controls.Add(Check("Scheduled restart", true, _ => { }).WithBounds(520, 78, 190, 22));
        panel.Controls.Add(SmallButton("Send test", 180, 126, async () => await DiscordNotify("Pal Local Manager Discord test."), Accent));
        panel.Controls.Add(LabelAt("Bot and voice-channel status can be added with a Discord bot token later; webhooks work now.", 18, 190, 850, 22, Muted, PanelBg));
        page.Controls.Add(panel);
        return page;
    }

    private Panel EventModePage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Event Mode", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Apply temporary server presets for XP, capture rate, drops, and night/day speed.", 12, 56, 850, 24, Muted, Bg));
        var panel = Panel(12, 104, 980, 310, PanelBg);
        panel.Controls.Add(LabelAt("Preset", 18, 24, 160, 22, TextColor, PanelBg, true));
        panel.Controls.Add(SmallButton("Double XP", 18, 62, () => EventPreset("EXP_RATE", "2.000000"), Blue));
        panel.Controls.Add(SmallButton("Fast Capture", 140, 62, () => EventPreset("PAL_CAPTURE_RATE", "2.000000"), Blue));
        panel.Controls.Add(SmallButton("More Drops", 290, 62, () => EventPreset("COLLECTION_DROP_RATE", "2.000000"), Blue));
        panel.Controls.Add(SmallButton("Reset Defaults", 430, 62, () => Status("Use Settings to reset individual values.")));
        panel.Controls.Add(LabelAt("Event note", 18, 128, 160, 22, TextColor, PanelBg, true));
        var note = TextInput("Weekend event is live!", 18, 158, 620);
        panel.Controls.Add(note);
        panel.Controls.Add(SmallButton("Broadcast note", 660, 156, () => SendRcon("Broadcast " + note.Text.Replace(' ', '_')), Accent));
        panel.Controls.Add(LabelAt("Changes are written through the settings system where supported. Restart the server after applying presets.", 18, 220, 850, 22, Muted, PanelBg));
        page.Controls.Add(panel);
        return page;
    }

    private Panel ImportExportPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Import / Export Setups", 12, 18, 420, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Export your manager config and PalWorldSettings.ini, or import them onto another machine.", 12, 56, 900, 24, Muted, Bg));
        var panel = Panel(12, 104, 980, 220, PanelBg);
        panel.Controls.Add(SmallButton("Export setup", 18, 24, ExportSetup, Blue));
        panel.Controls.Add(SmallButton("Import setup", 160, 24, ImportSetup, Accent));
        panel.Controls.Add(LabelAt("Export includes manager_config.json and PalWorldSettings.ini when present.", 18, 82, 850, 22, Muted, PanelBg));
        panel.Controls.Add(LabelAt("Import writes the selected JSON bundle next to this app and can restore PalWorldSettings.ini.", 18, 112, 850, 22, Muted, PanelBg));
        page.Controls.Add(panel);
        return page;
    }

    private Panel MultiServerPage()
    {
        var page = Page();
        page.Controls.Add(LabelAt("Server Setup", 12, 18, 300, 34, TextColor, Bg, true, 20));
        page.Controls.Add(LabelAt("Create a brand-new dedicated server through SteamCMD, or add one that is already installed on this PC.", 12, 56, 940, 24, Muted, Bg));
        var panel = Panel(12, 104, 980, 320, PanelBg);
        panel.Controls.Add(LabelAt("Create a new server", 18, 24, 260, 26, TextColor, PanelBg, true, 13));
        panel.Controls.Add(LabelAt("Downloads SteamCMD if needed, installs the Palworld dedicated server, and binds the app to the new folder.", 18, 56, 860, 22, Muted, PanelBg));
        panel.Controls.Add(SmallButton("Create Server", 18, 94, async () => await CreateServerFlow(), Blue));
        panel.Controls.Add(LabelAt("Add an existing server", 18, 160, 260, 26, TextColor, PanelBg, true, 13));
        panel.Controls.Add(LabelAt("Pick a folder that already contains PalServer.exe, or a parent folder that contains an installed server.", 18, 192, 860, 22, Muted, PanelBg));
        panel.Controls.Add(SmallButton("Add Existing", 18, 230, AddExistingServerFlow));
        panel.Controls.Add(LabelAt("Current bound folder: " + config.ServerRoot, 18, 278, 900, 22, Accent2, PanelBg));
        page.Controls.Add(panel);
        return page;
    }

    private Panel Page() => new() { BackColor = Bg, ForeColor = TextColor, AutoScroll = false };

    private Label Label(string text, float size, bool bold, Color fg, Color bg)
    {
        return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = fg, BackColor = bg };
    }

    private Label LabelAt(string text, int x, int y, int w, int h, Color fg, Color bg, bool bold = false, float size = 9)
    {
        var label = Label(text, size, bold, fg, bg);
        label.SetBounds(x, y, w, h);
        return label;
    }

    private Panel Panel(int x, int y, int w, int h, Color bg)
    {
        var panel = new Panel { BackColor = bg };
        panel.SetBounds(x, y, w, h);
        return panel;
    }

    private Label Badge(string text, Color bg)
    {
        var badge = Label(text, 8.5F, true, Color.White, bg);
        badge.TextAlign = ContentAlignment.MiddleCenter;
        badge.Width = 120;
        badge.Margin = new Padding(4);
        return badge;
    }

    private Button Button(string text, Color bg, Action action)
    {
        var b = new Button { Text = text, Width = MeasureButtonWidth(text, false), Height = 34, BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = Color.FromArgb(64, 68, 80);
        b.FlatAppearance.MouseOverBackColor = Lighten(bg);
        b.Click += (_, _) => action();
        return b;
    }

    private Button SmallButton(string text, int x, int y, Action? action, Color? bg = null)
    {
        var b = Button(text, bg ?? PanelBg, action ?? (() => { }));
        b.SetBounds(x, y, MeasureButtonWidth(text, true), 32);
        return b;
    }

    private int MeasureButtonWidth(string text, bool small)
    {
        using var font = new Font("Segoe UI", 9F, FontStyle.Regular);
        var measured = TextRenderer.MeasureText(text, font).Width + (small ? 18 : 24);
        return Math.Max(small ? 84 : 96, measured);
    }

    private TextBox TextInput(string text, int x, int y, int w)
    {
        var box = new TextBox { Text = text, BackColor = CardDark, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        box.SetBounds(x, y, w, 26);
        return box;
    }

    private CheckBox Check(string text, bool isChecked, Action<bool> changed)
    {
        var c = new CheckBox { Text = text, Checked = isChecked, AutoSize = true, ForeColor = TextColor, BackColor = PanelBg, Margin = new Padding(16, 8, 0, 0), Cursor = Cursors.Hand };
        c.CheckedChanged += (_, _) => changed(c.Checked);
        return c;
    }

    private void AddNav(FlowLayoutPanel nav, string text, Action action)
    {
        var b = new Button { Text = "  " + text, Width = 190, Height = 40, TextAlign = ContentAlignment.MiddleLeft, BackColor = Sidebar, ForeColor = Muted, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PanelBg;
        b.Click += (_, _) => action();
        nav.Controls.Add(b);
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        foreach (var page in pages) page.Visible = false;
        pages[index].Visible = true;
        pages[index].BringToFront();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateMaximizedBounds();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsThickFrame = 0x00040000;
            var cp = base.CreateParams;
            cp.Style |= wsThickFrame;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int wmNcHitTest = 0x84;
        const int htClient = 1;
        const int htCaption = 2;
        const int htLeft = 10;
        const int htRight = 11;
        const int htTop = 12;
        const int htTopLeft = 13;
        const int htTopRight = 14;
        const int htBottom = 15;
        const int htBottomLeft = 16;
        const int htBottomRight = 17;

        base.WndProc(ref m);

        if (m.Msg != wmNcHitTest || (int)m.Result != htClient || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        var left = point.X <= ResizeGrip;
        var right = point.X >= ClientSize.Width - ResizeGrip;
        var top = point.Y <= ResizeGrip;
        var bottom = point.Y >= ClientSize.Height - ResizeGrip;

        if (left && top) m.Result = htTopLeft;
        else if (right && top) m.Result = htTopRight;
        else if (left && bottom) m.Result = htBottomLeft;
        else if (right && bottom) m.Result = htBottomRight;
        else if (left) m.Result = htLeft;
        else if (right) m.Result = htRight;
        else if (top) m.Result = htTop;
        else if (bottom) m.Result = htBottom;
        else if (IsCaptionHit(point)) m.Result = htCaption;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateMaximizedBounds();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Space))
        {
            var topLeft = PointToScreen(new Point(10, titleBar.Bottom));
            ShowSystemMenu(topLeft);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static Color Lighten(Color c)
    {
        return Color.FromArgb(Math.Min(255, c.R + 18), Math.Min(255, c.G + 18), Math.Min(255, c.B + 18));
    }

    private string DeriveProfileName(string serverRoot)
    {
        var leaf = Path.GetFileName(serverRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return "Palworld Server";
        }

        leaf = leaf.Replace('_', ' ');
        return leaf;
    }

    private string SlugifyFolderName(string name)
    {
        var cleaned = Regex.Replace(name.Trim(), @"[^A-Za-z0-9]+", "");
        return string.IsNullOrWhiteSpace(cleaned) ? "PalworldServer" : cleaned;
    }

    private void RefreshProfileBindings()
    {
        if (topProfileTitle != null) topProfileTitle.Text = CurrentProfileName;
        if (heroProfileDescription != null) heroProfileDescription.Text = CurrentProfileName + " is wired to your Windows Palworld server folder and your Playit Docker agent.";
        if (profileCardName != null) profileCardName.Text = CurrentProfileName;
        if (profileInstallPath != null) profileInstallPath.Text = config.ServerRoot;
        if (serverRootBox != null) serverRootBox.Text = config.ServerRoot;
        if (playitBox != null) playitBox.Text = config.PlayitContainer;
        if (playitAddressBox != null) playitAddressBox.Text = config.PlayitPublicAddress;
        if (playitAddressValue != null) playitAddressValue.Text = GetPlayitDisplayAddress();
    }

    private async Task CreateServerFlow()
    {
        using var dialog = new Form
        {
            Text = "Create Server",
            Width = 620,
            Height = 290,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = PanelBg,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9.5F),
        };

        var profileName = TextInput(CurrentProfileName, 176, 26, 390);
        var baseRoot = TextInput(Path.GetDirectoryName(config.ServerRoot) ?? @"C:\PalworldServer", 176, 70, 300);
        var steamCmdRoot = TextInput(config.SteamCmdDir, 176, 114, 300);
        dialog.Controls.Add(LabelAt("Profile name", 22, 30, 140, 22, TextColor, PanelBg, true));
        dialog.Controls.Add(profileName);
        dialog.Controls.Add(LabelAt("Server root", 22, 74, 140, 22, TextColor, PanelBg, true));
        dialog.Controls.Add(baseRoot);
        dialog.Controls.Add(LabelAt("SteamCMD folder", 22, 118, 140, 22, TextColor, PanelBg, true));
        dialog.Controls.Add(steamCmdRoot);
        dialog.Controls.Add(SmallButton("Browse", 486, 68, () =>
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = Directory.Exists(baseRoot.Text) ? baseRoot.Text : @"C:\" };
            if (dlg.ShowDialog() == DialogResult.OK) baseRoot.Text = dlg.SelectedPath;
        }));
        dialog.Controls.Add(SmallButton("Browse", 486, 112, () =>
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = Directory.Exists(steamCmdRoot.Text) ? steamCmdRoot.Text : @"C:\" };
            if (dlg.ShowDialog() == DialogResult.OK) steamCmdRoot.Text = dlg.SelectedPath;
        }));

        var create = Button("Create & Install", Blue, () => dialog.DialogResult = DialogResult.OK);
        create.SetBounds(360, 182, 150, 36);
        var cancel = Button("Cancel", PanelBg, () => dialog.DialogResult = DialogResult.Cancel);
        cancel.SetBounds(520, 182, 80, 36);
        dialog.Controls.Add(create);
        dialog.Controls.Add(cancel);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(profileName.Text) ? "Palworld Server" : profileName.Text.Trim();
        var root = string.IsNullOrWhiteSpace(baseRoot.Text) ? @"C:\PalworldServer" : baseRoot.Text.Trim();
        var steamRoot = string.IsNullOrWhiteSpace(steamCmdRoot.Text) ? config.SteamCmdDir : steamCmdRoot.Text.Trim();
        var installFolder = Path.Combine(root, SlugifyFolderName(name));

        Directory.CreateDirectory(installFolder);
        config.ProfileName = name;
        config.ServerRoot = installFolder;
        config.SteamCmdDir = steamRoot;
        SaveConfig();
        RefreshProfileBindings();
        Status("Installing Palworld dedicated server...");
        await UpdateServer();
        Status("Server created and installed.");
        RefreshProfileBindings();
        RefreshAll();
    }

    private void AddExistingServerFlow()
    {
        using var dlg = new FolderBrowserDialog { InitialDirectory = Directory.Exists(config.ServerRoot) ? config.ServerRoot : @"C:\" };
        if (dlg.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var selected = dlg.SelectedPath;
        var directExe = Path.Combine(selected, "PalServer.exe");
        if (File.Exists(directExe))
        {
            BindExistingServer(selected);
            return;
        }

        var nestedExe = Directory.GetFiles(selected, "PalServer.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (nestedExe != null)
        {
            BindExistingServer(Path.GetDirectoryName(nestedExe)!);
            return;
        }

        MessageBox.Show("I couldn't find PalServer.exe in that folder.\n\nPick the actual Palworld dedicated server install folder, or a parent folder that contains it.", "Server not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BindExistingServer(string serverRoot)
    {
        config.ServerRoot = serverRoot;
        config.ProfileName = DeriveProfileName(serverRoot);
        SaveConfig();
        RefreshProfileBindings();
        RefreshAll();
        Status("Existing server added.");
    }

    private void UpdateMaximizedBounds()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
    }

    private bool IsCaptionHit(Point point)
    {
        if (titleBar == null || !titleBar.Bounds.Contains(point))
        {
            return false;
        }

        if (titleButtons != null && titleButtons.Bounds.Contains(point))
        {
            return false;
        }

        return true;
    }

    private void ShowSystemMenu(Point screenPoint)
    {
        const int wmSysCommand = 0x112;
        const uint tpmLeftAlign = 0x0000;
        const uint tpmTopAlign = 0x0000;
        const uint tpmReturnCmd = 0x0100;

        var menu = GetSystemMenu(Handle, false);
        if (menu == IntPtr.Zero)
        {
            return;
        }

        var command = TrackPopupMenuEx(menu, tpmLeftAlign | tpmTopAlign | tpmReturnCmd, screenPoint.X, screenPoint.Y, Handle, IntPtr.Zero);
        if (command != 0)
        {
            PostMessage(Handle, wmSysCommand, new IntPtr(command), IntPtr.Zero);
        }
    }

    private void Status(string message) => statusLabel.Text = message;

    private Process? FindServerProcess()
    {
        return Process.GetProcesses().FirstOrDefault(p => p.ProcessName.StartsWith("PalServer", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsServerRunning() => serverProcess is { HasExited: false } || FindServerProcess() != null;

    private void RefreshAll()
    {
        var proc = FindServerProcess();
        var running = proc != null || serverProcess is { HasExited: false };
        serverBadge.Text = running ? "Server: running" : "Server: stopped";
        serverBadge.BackColor = running ? Green : PanelBg;
        cardState.Text = running ? "ONLINE" : "STOPPED";
        cardState.ForeColor = running ? Green : Muted;
        pidValue.Text = proc?.Id.ToString() ?? serverProcess?.Id.ToString() ?? "--";
        uptimeValue.Text = proc != null ? FormatUptime(DateTime.Now - proc.StartTime) : "--";
        playersValue.Text = "0 / " + (ReadSettings().GetValueOrDefault("ServerPlayerMaxNum") ?? "32");
        RefreshPlayit();
    }

    private string FormatUptime(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m {span.Seconds}s";

    private void RefreshPlayit()
    {
        try
        {
            var status = GetPlayitStatus();
            playitBadge.Text = "Playit: " + status;
            playitBadge.BackColor = status.Contains("running", StringComparison.OrdinalIgnoreCase)
                ? Green
                : status.Contains("exited", StringComparison.OrdinalIgnoreCase)
                    ? Warn
                    : status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                        ? Danger
                    : PanelBg;
        }
        catch
        {
            playitBadge.Text = "Playit: unavailable";
            playitBadge.BackColor = Danger;
        }
    }

    private string GetPlayitDisplayAddress()
    {
        if (!string.IsNullOrWhiteSpace(config.PlayitPublicAddress))
        {
            return config.PlayitPublicAddress.Trim();
        }

        var cachedLog = !string.IsNullOrWhiteSpace(currentPlayitLogPath) && File.Exists(currentPlayitLogPath)
            ? ReadTextShared(currentPlayitLogPath)
            : string.Empty;
        var detected = TryDetectPlayitAddress(cachedLog);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            config.PlayitPublicAddress = detected;
            SaveConfig();
            return detected;
        }

        return "Set Playit address";
    }

    private string? TryDetectPlayitAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var patterns = new[]
        {
            @"(?i)\b(?:https?://)?([a-z0-9-]+(?:\.[a-z0-9-]+)*\.playit\.gg(?::\d+)?)\b",
            @"(?i)\b([a-z0-9-]+(?:\.[a-z0-9-]+)*\.playit\.gg)\b",
            @"(?i)\b(?:udp|tcp)://([a-z0-9-]+(?:\.[a-z0-9-]+)*\.playit\.gg(?::\d+)?)\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private string GetPlayitStatus()
    {
        try
        {
            var output = NormalizePlayitStatus(RunCapture("docker", $"inspect -f \"{{{{.State.Status}}}}\" {config.PlayitContainer}", 5000));
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }

            var psOutput = NormalizePlayitStatus(RunCapture("docker", $"ps -a --filter name=^{config.PlayitContainer}$ --format \"{{{{.Status}}}}\"", 5000));
            if (!string.IsNullOrWhiteSpace(psOutput))
            {
                return psOutput;
            }
        }
        catch
        {
        }

        var nativeProcess = Process.GetProcesses()
            .FirstOrDefault(p => p.ProcessName.Contains("playit", StringComparison.OrdinalIgnoreCase));
        if (nativeProcess != null)
        {
            return "running (native)";
        }

        return "not found";
    }

    private string NormalizePlayitStatus(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        if (text.Contains("dockerDesktopLinuxEngine", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed to connect", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("daemon is not running", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("open //./pipe/", StringComparison.OrdinalIgnoreCase))
        {
            return "docker unavailable";
        }

        if (text.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        {
            return "not found";
        }

        if (text.Equals("running", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Up ", StringComparison.OrdinalIgnoreCase))
        {
            return "running";
        }

        if (text.Equals("exited", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Exited ", StringComparison.OrdinalIgnoreCase))
        {
            return "exited";
        }

        if (text.Equals("created", StringComparison.OrdinalIgnoreCase))
        {
            return "created";
        }

        return text;
    }

    private async void StartServer()
    {
        if (IsServerRunning())
        {
            Status("Server is already running.");
            return;
        }
        if (!File.Exists(PalServerExe))
        {
            MessageBox.Show($"PalServer.exe not found:\n{PalServerExe}", "Pal Local Manager");
            return;
        }
        if (config.UpdateBeforeStart)
        {
            await UpdateServer();
        }
        await StartPlayit();
        Directory.CreateDirectory(LogsDir);
        currentServerLogPath = Path.Combine(LogsDir, $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(currentServerLogPath, $"[{DateTime.Now:HH:mm:ss}] Starting detached PalServer process in {config.ServerRoot}{Environment.NewLine}");
        serverProcess = StartDetachedServerProcess(PalServerExe, "-port=8211 -queryport=27015 -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS", config.ServerRoot);
        serverProcess.Exited += (_, _) =>
        {
            try
            {
                File.AppendAllText(currentServerLogPath, $"[{DateTime.Now:HH:mm:ss}] Server process exited.{Environment.NewLine}");
            }
            catch
            {
            }
            if (config.GuardianEnabled)
            {
                BeginInvoke(async () =>
                {
                    Status("Guardian restarting server...");
                    await Task.Delay(2500);
                    StartServer();
                });
            }
        };
        File.AppendAllText(currentServerLogPath, $"[{DateTime.Now:HH:mm:ss}] Started detached. PID {serverProcess.Id}.{Environment.NewLine}");
        await DiscordNotify("Palworld server started.");
        Status($"Started headless. PID {serverProcess.Id}.");
        RefreshAll();
    }

    private async Task StartPlayit()
    {
        try
        {
            var status = await Task.Run(GetPlayitStatus);
            if (status.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                Status("Playit is already running.");
                RefreshPlayit();
                return;
            }

            if (status.Contains("not found", StringComparison.OrdinalIgnoreCase) || status.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "I couldn't find the Playit container.\n\nCreate it once with your Playit secret key, then this app can start and stop it by container name.",
                    "Playit not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                RefreshPlayit();
            return;
        }

        Status($"Starting Playit container '{config.PlayitContainer}'...");
        var output = await Task.Run(() => RunCapture("docker", $"start {config.PlayitContainer}", 15000));
        Status(string.IsNullOrWhiteSpace(output) ? "Playit start requested." : "Playit: " + output.Trim());
        currentPlayitLogPath = Path.Combine(LogsDir, $"playit-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        CapturePlayitLogs();
        RefreshPlayit();
    }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start Playit");
            Status("Playit start failed: " + ex.Message);
        }
    }

    private async Task StopPlayit()
    {
        try
        {
            var status = await Task.Run(GetPlayitStatus);
            if (!status.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                Status("Playit is not running.");
                RefreshPlayit();
                return;
            }

            Status($"Stopping Playit container '{config.PlayitContainer}'...");
            var output = await Task.Run(() => RunCapture("docker", $"stop {config.PlayitContainer}", 15000));
            Status(string.IsNullOrWhiteSpace(output) ? "Playit stop requested." : "Playit: " + output.Trim());
            CapturePlayitLogs();
            RefreshPlayit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stop Playit");
            Status("Playit stop failed: " + ex.Message);
        }
    }

    private void StopServer()
    {
        var proc = FindServerProcess();
        try
        {
            if (serverProcess is { HasExited: false }) serverProcess.CloseMainWindow();
            if (proc != null) proc.CloseMainWindow();
            Task.Delay(2500).ContinueWith(_ =>
            {
                if (serverProcess is { HasExited: false }) serverProcess.Kill(true);
                if (proc is { HasExited: false }) proc.Kill(true);
            });
            Status("Stop requested.");
        }
        catch (Exception ex)
        {
            Status("Stop failed: " + ex.Message);
        }
        RefreshAll();
    }

    private void KillServer()
    {
        if (MessageBox.Show("Force-kill the Palworld server process?\n\nUse this only if Stop does not work.", "Kill server", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        var proc = FindServerProcess();
        try
        {
            if (serverProcess is { HasExited: false }) serverProcess.Kill(true);
            if (proc != null) proc.Kill(true);
            Status("Server process killed.");
        }
        catch (Exception ex)
        {
            Status("Kill failed: " + ex.Message);
        }
        RefreshAll();
    }

    private void RestartServer()
    {
        StopServer();
        Task.Delay(2500).ContinueWith(_ => BeginInvoke(StartServer));
    }

    private async Task InstallSteamCmd()
    {
        Directory.CreateDirectory(config.SteamCmdDir);
        var zipPath = Path.Combine(config.SteamCmdDir, "steamcmd.zip");
        Status("Downloading SteamCMD...");
        using var client = new HttpClient();
        var bytes = await client.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        await File.WriteAllBytesAsync(zipPath, bytes);
        ZipFile.ExtractToDirectory(zipPath, config.SteamCmdDir, overwriteFiles: true);
        File.Delete(zipPath);
        Status("SteamCMD installed.");
    }

    private async Task UpdateServer()
    {
        if (!File.Exists(SteamCmdExe))
        {
            await InstallSteamCmd();
        }
        Status("Updating Palworld server...");
        Directory.CreateDirectory(LogsDir);
        var logPath = Path.Combine(LogsDir, $"steamcmd-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var args = $"+force_install_dir \"{config.ServerRoot}\" +login anonymous +app_update 2394010 validate +quit";
        await Task.Run(() => RunToFile(SteamCmdExe, args, logPath, 30 * 60 * 1000));
        Status("Update finished.");
        RefreshLogs();
    }

    private async void CreateBackup()
    {
        if (!Directory.Exists(SavedDir))
        {
            Status("Saved folder not found.");
            return;
        }
        var dest = Path.Combine(BackupsDir, $"Saved-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        Status("Creating backup...");
        await Task.Run(() =>
        {
            if (File.Exists(dest)) File.Delete(dest);
            ZipFile.CreateFromDirectory(SavedDir, dest, CompressionLevel.Optimal, includeBaseDirectory: true);
        });
        await DiscordNotify($"Palworld backup created: {Path.GetFileName(dest)}");
        Status("Backup created.");
        RefreshBackups();
    }

    private void RestoreSelectedBackup()
    {
        if (backupsList.SelectedItem == null) return;
        if (IsServerRunning())
        {
            MessageBox.Show("Stop the server before restoring a backup.", "Pal Local Manager");
            return;
        }
        var zip = Path.Combine(BackupsDir, backupsList.SelectedItem.ToString()!);
        if (MessageBox.Show("Restore selected backup? This overwrites the Saved folder.", "Pal Local Manager", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        if (Directory.Exists(SavedDir)) Directory.Delete(SavedDir, recursive: true);
        ZipFile.ExtractToDirectory(zip, Path.GetDirectoryName(SavedDir)!, overwriteFiles: true);
        Status("Backup restored.");
    }

    private void RefreshBackups()
    {
        backupsList.Items.Clear();
        foreach (var file in Directory.GetFiles(BackupsDir, "*.zip").OrderByDescending(f => f))
        {
            backupsList.Items.Add(Path.GetFileName(file));
        }
    }

    private void RefreshMods()
    {
        modsList.Items.Clear();
        foreach (var file in Directory.GetFiles(ModsDir).OrderBy(Path.GetFileName))
        {
            modsList.Items.Add(Path.GetFileName(file));
        }
        var logic = Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "ue4ss", "Mods", "LogicMods");
        if (Directory.Exists(logic))
        {
            foreach (var file in Directory.GetFiles(logic).OrderBy(Path.GetFileName))
            {
                modsList.Items.Add("LogicMods: " + Path.GetFileName(file));
            }
        }
    }

    private void InstallModZip()
    {
        using var dlg = new OpenFileDialog { Filter = "Zip files|*.zip|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var temp = Path.Combine(config.ServerRoot, "_mod_import_temp");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        ZipFile.ExtractToDirectory(dlg.FileName, temp);
        CopyModPayload(temp);
        Directory.Delete(temp, true);
        RefreshMods();
        Status("Mod zip imported. Restart server if running.");
    }

    private void ImportModFiles()
    {
        using var dlg = new OpenFileDialog { Filter = "Palworld mod files|*.pak;*.ucas;*.utoc;*.lua;*.dll;*.json;*.ini;*.txt|All files|*.*", Multiselect = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        foreach (var file in dlg.FileNames) CopySingleMod(file);
        RefreshMods();
        Status("Mod files imported. Restart server if running.");
    }

    private void CopyModPayload(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) CopySingleMod(file);
    }

    private void CopySingleMod(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is ".pak" or ".ucas" or ".utoc")
        {
            File.Copy(file, Path.Combine(ModsDir, Path.GetFileName(file)), overwrite: true);
        }
        else if (ext is ".lua" or ".dll" or ".json" or ".ini" or ".txt")
        {
            var logic = Directory.CreateDirectory(Path.Combine(config.ServerRoot, "Pal", "Binaries", "Win64", "ue4ss", "Mods", "LogicMods")).FullName;
            File.Copy(file, Path.Combine(logic, Path.GetFileName(file)), overwrite: true);
        }
    }

    private Dictionary<string, string> ReadSettings()
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(SettingsPath)) return result;
        var settingsText = File.ReadAllText(SettingsPath);
        foreach (var key in new[] { "ServerName", "ServerPassword", "AdminPassword", "ServerPlayerMaxNum", "PublicPort", "RCONPort", "RESTAPIPort", "bAllowClientMod" })
        {
            var match = Regex.Match(settingsText, Regex.Escape(key) + "=([^,)]+)");
            if (match.Success) result[key] = match.Groups[1].Value.Trim().Trim('"');
        }
        return result;
    }

    private void LoadSettingsIntoForm()
    {
        var settings = ReadSettings();
        foreach (var (key, box) in settingBoxes) box.Text = settings.GetValueOrDefault(key) ?? "";
    }

    private void SaveSettingsFromForm()
    {
        if (!File.Exists(SettingsPath))
        {
            MessageBox.Show($"Settings file not found:\n{SettingsPath}", "Pal Local Manager");
            return;
        }
        var settingsText = File.ReadAllText(SettingsPath);
        foreach (var (key, box) in settingBoxes)
        {
            var value = FormatSetting(key, box.Text.Trim());
            settingsText = Regex.Replace(settingsText, "(" + Regex.Escape(key) + "=)([^,)]+)", "$1" + value, RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        File.WriteAllText(SettingsPath, settingsText);
        Status("Settings saved.");
    }

    private void EventPreset(string key, string value)
    {
        if (!File.Exists(SettingsPath))
        {
            Status("Settings file not found.");
            return;
        }
        var text = File.ReadAllText(SettingsPath);
        text = Regex.Replace(text, "(" + Regex.Escape(key) + "=)([^,)]+)", "$1" + value, RegexOptions.None, TimeSpan.FromSeconds(2));
        File.WriteAllText(SettingsPath, text);
        Status($"Event preset applied: {key}={value}. Restart server to apply.");
    }

    private void ExportSetup()
    {
        using var dlg = new SaveFileDialog { Filter = "Pal setup json|*.json", FileName = "pal-local-setup.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var bundle = new Dictionary<string, string>
        {
            ["manager_config"] = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }),
            ["palworld_settings"] = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : ""
        };
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
        Status("Setup exported.");
    }

    private void ImportSetup()
    {
        using var dlg = new OpenFileDialog { Filter = "Pal setup json|*.json|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var bundle = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(dlg.FileName));
        if (bundle == null) return;
        if (bundle.TryGetValue("manager_config", out var managerConfig))
        {
            File.WriteAllText(configPath, managerConfig);
        }
        if (bundle.TryGetValue("palworld_settings", out var settings) && !string.IsNullOrWhiteSpace(settings))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, settings);
        }
        Status("Setup imported. Restart app to reload manager config.");
    }

    private static string FormatSetting(string key, string value)
    {
        if (key.StartsWith("b", StringComparison.Ordinal) || key is "RCONEnabled" or "RESTAPIEnabled")
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" ? "True" : "False";
        if (key.Contains("Port", StringComparison.OrdinalIgnoreCase) || key.Contains("MaxNum", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private async void SendRcon(string command)
    {
        try
        {
            var settings = ReadSettings();
            var password = settings.GetValueOrDefault("AdminPassword") ?? "";
            var port = ParseInt(settings.GetValueOrDefault("RCONPort") ?? "25575");
            var response = await Task.Run(() => RconSend("127.0.0.1", port, password, command));
            rconOutputBox.AppendText($"> {command}\r\n{response}\r\n\r\n");
        }
        catch (Exception ex)
        {
            rconOutputBox.AppendText($"> {command}\r\nERROR: {ex.Message}\r\n\r\n");
        }
    }

    private static string RconSend(string host, int port, string password, string command)
    {
        using var client = new TcpClient();
        client.Connect(host, port);
        using var stream = client.GetStream();
        SendPacket(stream, 1, 3, password);
        var auth = ReadPacket(stream);
        if (auth.Id == -1) throw new InvalidOperationException("RCON authentication failed.");
        SendPacket(stream, 2, 2, command);
        return ReadPacket(stream).Body;
    }

    private static void SendPacket(Stream stream, int id, int type, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body + "\0\0");
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(payload.Length + 8);
        bw.Write(id);
        bw.Write(type);
        bw.Write(payload);
        stream.Write(ms.ToArray());
    }

    private static (int Id, int Type, string Body) ReadPacket(Stream stream)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        stream.ReadExactly(lenBuf);
        var len = BitConverter.ToInt32(lenBuf);
        var buf = new byte[len];
        stream.ReadExactly(buf);
        var id = BitConverter.ToInt32(buf, 0);
        var type = BitConverter.ToInt32(buf, 4);
        var body = Encoding.UTF8.GetString(buf, 8, Math.Max(0, buf.Length - 10));
        return (id, type, body);
    }

    private async Task RunScheduledJobs()
    {
        if (config.AutoBackupMinutes > 0 && DateTime.Now - config.LastAutoBackup >= TimeSpan.FromMinutes(config.AutoBackupMinutes))
        {
            config.LastAutoBackup = DateTime.Now;
            SaveConfig();
            CreateBackup();
        }
        if (config.AutoRestartMinutes > 0 && IsServerRunning() && DateTime.Now - config.LastAutoRestart >= TimeSpan.FromMinutes(config.AutoRestartMinutes))
        {
            config.LastAutoRestart = DateTime.Now;
            SaveConfig();
            await DiscordNotify("Scheduled Palworld restart starting.");
            RestartServer();
        }
    }

    private async Task DiscordNotify(string message)
    {
        if (string.IsNullOrWhiteSpace(config.DiscordWebhook)) return;
        try
        {
            using var client = new HttpClient();
            var json = JsonSerializer.Serialize(new { content = message });
            await client.PostAsync(config.DiscordWebhook, new StringContent(json, Encoding.UTF8, "application/json"));
        }
        catch { }
    }

    private void BrowseServerRoot()
    {
        using var dlg = new FolderBrowserDialog { InitialDirectory = Directory.Exists(config.ServerRoot) ? config.ServerRoot : @"C:\" };
        if (dlg.ShowDialog() == DialogResult.OK) serverRootBox.Text = dlg.SelectedPath;
    }

    private void SaveServerRoot()
    {
        config.ServerRoot = serverRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(config.ProfileName))
        {
            config.ProfileName = DeriveProfileName(config.ServerRoot);
        }
        SaveConfig();
        RefreshProfileBindings();
        Status("Server folder saved.");
        RefreshAll();
    }

    private void SavePlayitName()
    {
        config.PlayitContainer = string.IsNullOrWhiteSpace(playitBox.Text) ? "playit-agent" : playitBox.Text.Trim();
        SaveConfig();
        RefreshPlayit();
    }

    private void SavePlayitAddress()
    {
        config.PlayitPublicAddress = playitAddressBox?.Text.Trim() ?? "";
        SaveConfig();
        RefreshAll();
    }

    private void RefreshLogs()
    {
        if (logBox == null) return;
        try
        {
            var source = logSourceBox?.SelectedItem?.ToString() ?? "Server";
            logBox.Text = source switch
            {
                "Playit" => ReadPlayitLogs(),
                "SteamCMD" => ReadLatestLog("steamcmd-*.log"),
                "All" => ReadAllLogs(),
                _ => ReadServerLogs()
            };
        }
        catch (Exception ex)
        {
            logBox.Text = "Log refresh failed: " + ex.Message;
        }
    }

    private string ReadServerLogs()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(currentServerLogPath) && File.Exists(currentServerLogPath))
        {
            parts.Add("=== Manager ===");
            parts.Add(ReadTextShared(currentServerLogPath));
        }
        else
        {
            var managerLog = ReadLatestLog("server-*.log");
            if (!string.IsNullOrWhiteSpace(managerLog) && managerLog != "No logs yet.")
            {
                parts.Add("=== Manager ===");
                parts.Add(managerLog);
            }
        }

        var gameLog = ReadLatestSavedServerLog();
        if (!string.IsNullOrWhiteSpace(gameLog) && gameLog != "No logs yet.")
        {
            parts.Add("=== Palworld ===");
            parts.Add(gameLog);
        }

        return parts.Count == 0 ? "No logs yet." : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private string ReadPlayitLogs()
    {
        if (!string.IsNullOrWhiteSpace(currentPlayitLogPath) && File.Exists(currentPlayitLogPath))
        {
            return SummarizePlayitLog(ReadTextShared(currentPlayitLogPath));
        }
        return SummarizePlayitLog(CapturePlayitLogs());
    }

    private string ReadAllLogs()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Server ===");
        sb.AppendLine(ReadServerLogs());
        sb.AppendLine();
        sb.AppendLine("=== Playit ===");
        sb.AppendLine(ReadPlayitLogs());
        sb.AppendLine();
        sb.AppendLine("=== SteamCMD ===");
        sb.AppendLine(ReadLatestLog("steamcmd-*.log"));
        return sb.ToString();
    }

    private string ReadLatestLog(string pattern)
    {
        try
        {
            var latest = Directory.Exists(LogsDir) ? Directory.GetFiles(LogsDir, pattern).OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
            return latest == null ? "No logs yet." : ReadTextShared(latest);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string ReadLatestSavedServerLog()
    {
        try
        {
            var latest = Directory.Exists(SavedLogsDir)
                ? Directory.GetFiles(SavedLogsDir, "*.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                : null;
            return latest == null ? "No logs yet." : ReadTextShared(latest);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string ReadTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private string CapturePlayitLogs()
    {
        try
        {
            var output = RunCapture("docker", $"logs --tail 500 {config.PlayitContainer}", 10000);
            if (!string.IsNullOrWhiteSpace(currentPlayitLogPath))
            {
                Directory.CreateDirectory(LogsDir);
                File.WriteAllText(currentPlayitLogPath, output);
            }
            var detected = TryDetectPlayitAddress(output);
            if (!string.IsNullOrWhiteSpace(detected) && !string.Equals(config.PlayitPublicAddress, detected, StringComparison.OrdinalIgnoreCase))
            {
                config.PlayitPublicAddress = detected;
                SaveConfig();
                if (playitAddressValue != null) playitAddressValue.Text = detected;
                if (playitAddressBox != null) playitAddressBox.Text = detected;
            }
            return string.IsNullOrWhiteSpace(output) ? "No Playit logs yet." : output;
        }
        catch (Exception ex)
        {
            return "Playit log capture failed: " + ex.Message;
        }
    }

    private string SummarizePlayitLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No Playit logs yet.";
        }

        if (text.Contains("InvalidSecret", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("checking if secret key is valid", StringComparison.OrdinalIgnoreCase))
        {
            var hint = "Playit secret key is invalid. Replace the agent secret in Docker and restart the container.";
            var tail = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.Contains("checking if secret key is valid", StringComparison.OrdinalIgnoreCase))
                .TakeLast(8);
            var sb = new StringBuilder();
            sb.AppendLine(hint);
            foreach (var line in tail)
            {
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var collapsed = new List<string>();
        string? last = null;
        var run = 0;
        foreach (var line in lines)
        {
            if (string.Equals(line, last, StringComparison.Ordinal))
            {
                run++;
                if (run <= 2) collapsed.Add(line);
                continue;
            }

            last = line;
            run = 1;
            collapsed.Add(line);
        }

        return string.Join(Environment.NewLine, collapsed);
    }

    private Process StartDetachedServerProcess(string file, string args, string workingDirectory)
    {
        var startupInfo = new STARTUPINFO();
        startupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFO>();
        var commandLine = "\"" + file + "\" " + args;

        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindowFlag | DetachedProcessFlag, IntPtr.Zero, workingDirectory, ref startupInfo, out var processInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var process = Process.GetProcessById((int)processInfo.dwProcessId);
            process.EnableRaisingEvents = true;
            return process;
        }
        finally
        {
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
        }
    }

    private void ShowDockerPs()
    {
        try { MessageBox.Show(RunCapture("docker", "ps --format \"table {{.Names}}\\t{{.Status}}\\t{{.Image}}\"", 8000), "docker ps"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "docker ps"); }
    }

    private static string RunCapture(string file, string args, int timeoutMs)
    {
        var psi = HiddenProcessStartInfo(file, args);
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(timeoutMs)) p.Kill(true);
        return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    }

    private static void RunToFile(string file, string args, string logPath, int timeoutMs)
    {
        var psi = HiddenProcessStartInfo(file, args);
        using var p = Process.Start(psi)!;
        using var writer = new StreamWriter(logPath);
        p.OutputDataReceived += (_, e) => { if (e.Data != null) writer.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) writer.WriteLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutMs)) p.Kill(true);
    }

    private static ProcessStartInfo HiddenProcessStartInfo(string file, string args)
    {
        return new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }

    private static void OpenPath(string path)
    {
        if (Path.HasExtension(path))
        {
            if (!File.Exists(path)) return;
        }
        else
        {
            Directory.CreateDirectory(path);
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static int ParseInt(string value) => int.TryParse(value, out var n) ? Math.Max(0, n) : 0;
}

internal static class ControlExtensions
{
    public static T WithBounds<T>(this T control, int x, int y, int width, int height) where T : Control
    {
        control.SetBounds(x, y, width, height);
        return control;
    }
}




