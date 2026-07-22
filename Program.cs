using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("D2R Sprite Toolkit")]
[assembly: AssemblyProduct("D2R Sprite Toolkit")]
[assembly: AssemblyDescription("PNG and Sprite conversion toolkit for Diablo II: Resurrected modding")]
[assembly: AssemblyCopyright("Copyright © 2026 D2R Sprite Toolkit contributors")]
[assembly: AssemblyVersion("4.0.1.0")]
[assembly: AssemblyFileVersion("4.0.1.0")]

namespace D2RSpriteToolkit
{
    internal enum PreviewNavigationInputMode
    {
        ArrowKeys = 0,
        MouseWheel = 1,
        PageKeys = 2
    }

    internal static class Localization
    {
        private static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string currentLanguage = "en";

        public static string CurrentLanguage { get { return currentLanguage; } }

        public static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "en";
            string clean = language.Trim().ToLowerInvariant();
            return clean.StartsWith("ko") ? "ko" : "en";
        }

        public static void Load(string language)
        {
            currentLanguage = NormalizeLanguage(language);
            values.Clear();

            string[] lines = ReadEmbeddedLanguageLines(currentLanguage);
            if (lines == null) lines = ReadExternalLanguageLines(currentLanguage);
            if (lines == null) return;

            LoadLanguageLines(lines);
        }

        private static string[] ReadEmbeddedLanguageLines(string language)
        {
            try
            {
                string normalized = NormalizeLanguage(language);
                string resourceName = "D2RSpriteToolkit.lang." + normalized + ".lng";
                Stream stream = typeof(Localization).Assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    string suffix = ".lang." + normalized + ".lng";
                    string[] names = typeof(Localization).Assembly.GetManifestResourceNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            stream = typeof(Localization).Assembly.GetManifestResourceStream(names[i]);
                            break;
                        }
                    }
                }

                if (stream == null) return null;
                using (stream)
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string text = reader.ReadToEnd();
                    return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string[] ReadExternalLanguageLines(string language)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "lang", NormalizeLanguage(language) + ".lng");
                if (!File.Exists(path)) return null;
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return null;
            }
        }

        private static void LoadLanguageLines(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null) continue;
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);
                value = value.Replace("\\r\\n", "\r\n").Replace("\\n", "\r\n");
                if (key.Length > 0) values[key] = value;
            }
        }

        public static string T(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;
            string value;
            if (values.TryGetValue(key, out value)) return value;
            return fallback ?? key;
        }

        public static string T(string key, string fallback, params object[] args)
        {
            string format = T(key, fallback);
            try { return string.Format(format, args); }
            catch { return format; }
        }
    }

    internal static class ImageFileUtil
    {
        public static Bitmap LoadBitmapNoLock(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image img = Image.FromStream(ms, false, false))
            {
                return new Bitmap(img);
            }
        }

        public static Image LoadImageNoLock(string path)
        {
            return LoadBitmapNoLock(path);
        }

        public static Image LoadImageNoStream(Stream stream)
        {
            if (stream == null) return null;
            using (Image img = Image.FromStream(stream, false, false))
            {
                return new Bitmap(img);
            }
        }

        public static bool TryReadImageInfoNoLock(string path, out int width, out int height, out int bitDepth)
        {
            width = 0;
            height = 0;
            bitDepth = 0;

            try
            {
                using (Bitmap img = LoadBitmapNoLock(path))
                {
                    width = img.Width;
                    height = img.Height;
                    bitDepth = Image.GetPixelFormatSize(img.PixelFormat);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\D2RSpriteToolkit_SingleInstance_v4";
        private const string RestoreEventName = @"Local\D2RSpriteToolkit_RestoreEvent_v4";
        internal static readonly int RestoreWindowMessage = RegisterWindowMessage("D2RSpriteToolkit_RestoreWindowMessage_v4");
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private static Mutex singleInstanceMutex;
        private static EventWaitHandle restoreEvent;
        private static volatile bool applicationExiting;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                SignalExistingInstanceToRestore();
                return;
            }

            try
            {
                try { restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RestoreEventName); }
                catch { restoreEvent = null; }

                try { Directory.SetCurrentDirectory(Application.StartupPath); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StartupSplashForm splash = null;
                DateTime splashStartTime = DateTime.UtcNow;
                try
                {
                    splash = StartupSplashForm.TryShowSplash();
                    if (splash != null) splash.SetProgress(10);
                    MainForm mainForm = new MainForm(splash);
                    StartRestoreSignalThread(mainForm);
                    if (splash != null)
                    {
                        splash.SetProgress(100);
                        WaitForMinimumSplashTime(splash, splashStartTime, 3000);
                        try { splash.Close(); splash.Dispose(); } catch { }
                        splash = null;
                    }
                    Application.Run(mainForm);
                }
                finally
                {
                    if (splash != null)
                    {
                        try { splash.Close(); splash.Dispose(); } catch { }
                        splash = null;
                    }
                }
            }
            finally
            {
                applicationExiting = true;
                try
                {
                    if (restoreEvent != null)
                    {
                        restoreEvent.Set();
                        restoreEvent.Dispose();
                        restoreEvent = null;
                    }
                }
                catch { }

                try
                {
                    if (singleInstanceMutex != null)
                    {
                        singleInstanceMutex.ReleaseMutex();
                        singleInstanceMutex.Dispose();
                    }
                }
                catch { }
            }
        }

        private static void SignalExistingInstanceToRestore()
        {
            bool signaled = false;
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(RestoreEventName))
                {
                    ev.Set();
                    signaled = true;
                }
            }
            catch { }

            if (!signaled && RestoreWindowMessage != 0)
            {
                try { PostMessage(HwndBroadcast, RestoreWindowMessage, IntPtr.Zero, IntPtr.Zero); }
                catch { }
            }
        }

        private static void StartRestoreSignalThread(MainForm mainForm)
        {
            if (restoreEvent == null || mainForm == null) return;

            Thread thread = new Thread(delegate()
            {
                while (!applicationExiting)
                {
                    try
                    {
                        restoreEvent.WaitOne();
                        if (applicationExiting) return;
                        if (mainForm.IsDisposed) return;

                        try
                        {
                            mainForm.BeginInvoke(new MethodInvoker(delegate
                            {
                                mainForm.RestoreFromExternalLaunch();
                            }));
                        }
                        catch { return; }
                    }
                    catch { return; }
                }
            });
            thread.IsBackground = true;
            thread.Name = "D2RSpriteToolkitRestoreSignal";
            thread.Start();
        }

        private static void WaitForMinimumSplashTime(StartupSplashForm splash, DateTime startTime, int minimumMs)
        {
            if (splash == null || minimumMs <= 0) return;

            while (true)
            {
                int remainingMs = minimumMs - (int)Math.Round((DateTime.UtcNow - startTime).TotalMilliseconds);
                if (remainingMs <= 0) break;

                try
                {
                    splash.Refresh();
                    splash.KeepOnTop();
                    Application.DoEvents();
                }
                catch { }

                Thread.Sleep(Math.Min(50, Math.Max(1, remainingMs)));
            }
        }
    }


    internal sealed class StartupSplashForm : Form
    {
        private Image splashImage;
        private int progress;
        private readonly Font progressFont;
        private readonly Brush shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        private readonly Brush textBrush = new SolidBrush(Color.FromArgb(255, 255, 241, 176));
        private readonly Pen borderPen = new Pen(Color.FromArgb(150, 255, 241, 176));
        private readonly Brush progressBackBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        private readonly Brush progressFillBrush = new SolidBrush(Color.FromArgb(210, 255, 241, 176));
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const int WsExNoActivate = 0x08000000;
        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private StartupSplashForm(Image image)
        {
            splashImage = image;
            progressFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.Black;
            ClientSize = splashImage == null ? new Size(800, 400) : splashImage.Size;
        }

        public static StartupSplashForm TryShowSplash()
        {
            try
            {
                StartupSplashForm form = new StartupSplashForm(LoadSplashImage());
                form.SetProgress(0);
                form.Show();
                form.KeepOnTop();
                form.Refresh();
                Application.DoEvents();
                return form;
            }
            catch
            {
                return null;
            }
        }

        public void KeepOnTop()
        {
            try
            {
                TopMost = true;
                if (IsHandleCreated)
                {
                    SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                }
            }
            catch { }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseActivate)
            {
                m.Result = new IntPtr(MaNoActivate);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            KeepOnTop();
        }

        private static Image LoadSplashImage()
        {
            try
            {
                string[] names = typeof(Program).Assembly.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith("loading.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream(names[i]))
                        {
                            if (stream != null) return ImageFileUtil.LoadImageNoStream(stream);
                        }
                    }
                }
            }
            catch { }

            try
            {
                string path = Path.Combine(Application.StartupPath, "loading.png");
                if (File.Exists(path)) return ImageFileUtil.LoadImageNoLock(path);
            }
            catch { }

            return null;
        }

        public void SetProgress(int value)
        {
            progress = Math.Max(0, Math.Min(100, value));
            try
            {
                Invalidate();
                Update();
                KeepOnTop();
                Application.DoEvents();
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (splashImage != null) g.DrawImage(splashImage, ClientRectangle);
            else
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 20, 14), Color.Black, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, ClientRectangle);
                }
                using (Font titleFont = new Font("Segoe UI", 34f, FontStyle.Bold, GraphicsUnit.Point))
                {
                    g.DrawString("D2R Sprite Toolkit", titleFont, textBrush, new PointF(48, 135));
                }
            }

            Rectangle barBounds = new Rectangle(ClientSize.Width - 236, ClientSize.Height - 27, 188, 7);
            Rectangle fillBounds = new Rectangle(barBounds.Left, barBounds.Top, (int)Math.Round(barBounds.Width * (progress / 100.0)), barBounds.Height);
            g.FillRectangle(progressBackBrush, barBounds);
            if (fillBounds.Width > 0) g.FillRectangle(progressFillBrush, fillBounds);
            g.DrawRectangle(borderPen, barBounds);

            string label = progress.ToString() + "%";
            float x = barBounds.Right + 8;
            float y = barBounds.Top - 5;
            g.DrawString(label, progressFont, shadowBrush, x + 1, y + 1);
            g.DrawString(label, progressFont, textBrush, x, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (splashImage != null)
                {
                    splashImage.Dispose();
                    splashImage = null;
                }
                progressFont.Dispose();
                shadowBrush.Dispose();
                textBrush.Dispose();
                borderPen.Dispose();
                progressBackBrush.Dispose();
                progressFillBrush.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class MainForm : Form
    {
        private const string RegistryPath = @"Software\D2RSpriteToolkit";
        private const string AppVersion = "4.0.1";

        private const string InvenLinkUrl = "https://www.inven.co.kr/board/diablo2/5842/7796";
        private const string NexusLinkUrl = "https://www.nexusmods.com/diablo2resurrected/mods/1144";
        private static readonly Color ReferenceItemColor = Color.FromArgb(160, 160, 160);
        private static readonly Color LowendPreviewInfoColor = Color.Purple;

        private readonly MenuStrip menuStrip;
        private ToolStripMenuItem menuFile;
        private ToolStripMenuItem itemAddFiles;
        private ToolStripMenuItem itemAddFolder;
        private ToolStripMenuItem itemExit;
        private ToolStripMenuItem menuSettings;
        private ToolStripMenuItem menuInfo;
        private ToolStripMenuItem menuLanguage;
        private ToolStripMenuItem itemLanguageEnglish;
        private ToolStripMenuItem itemLanguageKorean;
        private string currentLanguage = "en";

        private bool autoSizeByLowend = true;
        private bool loadMatchingSprites = true;
        private bool includeLowendSprites = true;

        private readonly GroupBox groupForce;
        private readonly CheckBox chkForceSize;
        private readonly NumericUpDown numForceWidth;
        private readonly NumericUpDown numForceHeight;

        private readonly GroupBox groupCustomSuffix;
        private readonly CheckBox chkCustomSuffix;
        private readonly TextBox txtCustomSuffix;

        private readonly DropHintControl lblDropHint;
        private bool clearBeforeDrop = true;
        private bool includeSubfolders = true;
        private bool closeToTray = true;
        private bool convertSelectedOnly = false;
        private bool allowExit;
        private string lastFolderPath = string.Empty;

        private readonly Panel panelList;
        private readonly Button btnClear;
        private readonly Button btnRefresh;
        private readonly Button btnClearSelected;
        private readonly ListView listFiles;
        private readonly Button btnSelectAll;
        private readonly Button btnSelectNone;
        private readonly CheckBox chkIncludeLowendPngInList;
        private readonly CheckBox chkIncludeSpritesInList;
        private readonly CheckBox chkIncludeLowendSpritesInList;
        private readonly Button btnPrefix;
        private readonly Button btnSuffix;
        private readonly Button btnReplace;
        private readonly Button btnRemoveSubstring;
        private readonly Button btnExecuteRename;
        private readonly Button btnResetRename;
        private readonly Button btnConvert;
        private readonly Button btnResetAll;
        private readonly Button btnSpriteToPng;
        private readonly CheckBox chkOutputPngFolder;
        private readonly Button btnPngToSprite;
        private readonly CheckBox chkOutputSpriteFolder;
        private readonly Panel bottomNoticePanel;
        private readonly Label lblBottomNotice1;
        private readonly Label lblBottomNotice2;
        private readonly Panel spriteNoticePanel;
        private readonly Label lblSpriteNotice1;
        private readonly Label lblSpriteNotice2;
        private readonly Label lblStatus;

        private readonly GroupBox groupPreview;
        private readonly PreviewFramePanel previewFrame;
        private readonly TransparentPreviewPanel previewPanel;
        private readonly CheckBox chkPreviewConverted;
        private readonly PreviewInfoPanel lblPreviewInfo;

        private readonly ToolTip toolTip;
        private NotifyIcon trayIcon;
        private Icon traySmallIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayItemSettings;
        private ToolStripMenuItem trayItemExit;
        private System.Windows.Forms.Timer trayAnimationTimer;
        private bool trayAnimationInProgress;
        private bool trayAnimationHiding;
        private Rectangle trayAnimationStartBounds;
        private Rectangle trayAnimationEndBounds;
        private DateTime trayAnimationStartTime;
        private Rectangle lastVisibleBounds;
        private const int TrayAnimationDurationMs = 300;

        private readonly List<FileEntry> entries = new List<FileEntry>();
        private readonly HashSet<string> fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ColumnSpec> columnSpecs = new List<ColumnSpec>();

        private int nextOrder = 1;
        private string sortColumnId = "Order";
        private bool sortAscending = true;
        private bool loadingSettings;
        private bool buildingColumns;
        private PreviewViewerForm previewViewer;
        private PreviewNavigationInputMode previewNavigationMode = PreviewNavigationInputMode.ArrowKeys;
        private int previewZoomStepPercent = 30;
        private int previewFixedZoomBasePercent = 100;
        private bool previewUseCanvasColor = true;
        private Color previewCanvasColor = Color.Black;
        private bool previewUseAlphaColor = false;
        private Color previewAlphaColor = Color.White;
        private string currentHeaderInfoText = string.Empty;
        private string renamePrefixText = string.Empty;
        private string renameSuffixText = string.Empty;
        private string renameReplaceFindText = string.Empty;
        private string renameReplaceText = string.Empty;
        private string renameRemoveStartText = "1";
        private string renameRemoveEndText = "1";
        private bool renameRemoveFromEnd = false;

        public MainForm() : this(null)
        {
        }

        public MainForm(StartupSplashForm splash)
        {
            currentLanguage = LoadLanguageSettingOnly();
            Localization.Load(currentLanguage);
            UpdateSplash(splash, 15);
            Text = T("app.title", "D2R Sprite Toolkit");
            ClientSize = new Size(1200, 1030);
            MinimumSize = new Size(1100, 1010);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;
            Padding = new Padding(0);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            InitializeColumnSpecs();
            UpdateSplash(splash, 25);

            menuStrip = CreateMenuStrip();
            MainMenuStrip = menuStrip;


            Panel dropPanel = new Panel { Left = 16, Top = 40, Width = 1168, Height = 58, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BorderStyle = BorderStyle.FixedSingle };
            lblDropHint = new DropHintControl
            {
                PrimaryText = T("drop.primary", "Drag and drop PNG / folder / .sprite here"),
                SecondaryText = T("drop.secondary", ".sprite preview reads only the selected .sprite file itself."),
                Left = 8,
                Top = 7,
                Width = 1150,
                Height = 42,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular)
            };
            dropPanel.Controls.Add(lblDropHint);
            dropPanel.Resize += delegate
            {
                lblDropHint.Width = Math.Max(1, dropPanel.ClientSize.Width - 16);
            };

            groupForce = new GroupBox { Text = T("group.force_size", "Force size"), Tag = "lng:group.force_size|Force size", Left = 16, Top = 110, Width = 280, Height = 136, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            chkForceSize = new CenteredCheckBox { Text = T("check.force_size", "Ignore ratio and force size"), Tag = "lng:check.force_size|Ignore ratio and force size", Left = 14, Top = 26, Width = 220 };
            Label lblForceW = new Label { Text = T("label.width_short", "Width"), Tag = "lng:label.width_short|Width", Left = 24, Top = 61, Width = 45, Height = 24 };
            numForceWidth = new NumericUpDown { Left = 80, Top = 57, Width = 92, Minimum = 1, Maximum = 99999, Value = 20, Enabled = false };
            Label lblForceH = new Label { Text = T("label.height_short", "Height"), Tag = "lng:label.height_short|Height", Left = 24, Top = 101, Width = 45, Height = 24 };
            numForceHeight = new NumericUpDown { Left = 80, Top = 97, Width = 92, Minimum = 1, Maximum = 99999, Value = 20, Enabled = false };
            groupForce.Controls.AddRange(new Control[] { chkForceSize, lblForceW, numForceWidth, lblForceH, numForceHeight });

            groupCustomSuffix = new GroupBox { Text = T("group.custom_suffix", "Use a custom suffix instead of .lowend"), Tag = "lng:group.custom_suffix|Use a custom suffix instead of .lowend", Left = 306, Top = 110, Width = 878, Height = 136, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            chkCustomSuffix = new CenteredCheckBox { Text = T("check.custom_suffix", "Enter custom suffix"), Tag = "lng:check.custom_suffix|Enter custom suffix", Left = 14, Top = 31, Width = 145, Height = 24, Checked = false };
            txtCustomSuffix = new TextBox { Left = 168, Top = 32, Width = 220, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            Label lblCustomSuffixDesc = new Label { Text = T("custom_suffix.desc", "When checked, the output filename uses the entered suffix instead of .lowend."), Tag = "lng:custom_suffix.desc|When checked, the output filename uses the entered suffix instead of .lowend.", Left = 14, Top = 61, Width = 840, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Label lblCustomSuffixExample1 = new Label { Text = T("custom_suffix.example1", "Example 1)  .suffix → item.suffix.png"), Tag = "lng:custom_suffix.example1|Example 1)  .suffix → item.suffix.png", Left = 28, Top = 84, Width = 212, Height = 18 };
            Label lblCustomSuffixExample1Note = new Label { Text = T("custom_suffix.example1_note", "(correct example)"), Tag = "lng:custom_suffix.example1_note|(correct example)", Left = 246, Top = 84, Width = 150, Height = 18, ForeColor = Color.RoyalBlue };
            Label lblCustomSuffixExample2 = new Label { Text = T("custom_suffix.example2", "Example 2)  suffix → itemsuffix.png"), Tag = "lng:custom_suffix.example2|Example 2)  suffix → itemsuffix.png", Left = 28, Top = 109, Width = 212, Height = 18 };
            Label lblCustomSuffixExample2Note = new Label { Text = T("custom_suffix.example2_note", "(without separator)"), Tag = "lng:custom_suffix.example2_note|(without separator)", Left = 246, Top = 109, Width = 260, Height = 18, ForeColor = Color.RoyalBlue };
            groupCustomSuffix.Controls.AddRange(new Control[] { chkCustomSuffix, txtCustomSuffix, lblCustomSuffixDesc, lblCustomSuffixExample1, lblCustomSuffixExample1Note, lblCustomSuffixExample2, lblCustomSuffixExample2Note });


            panelList = new Panel { Left = 16, Top = 254, Width = 828, Height = 576, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            btnRefresh = new Button { Text = T("button.refresh", "Refresh (F5)"), Tag = "lng:button.refresh|Refresh (F5)", Left = 0, Top = 4, Width = 130, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            btnClear = new Button { Text = T("button.clear_all", "Clear all"), Tag = "lng:button.clear_all|Clear all", Left = 136, Top = 4, Width = 140, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            btnClearSelected = new Button { Text = T("button.remove_selected", "Remove selected files/folders"), Tag = "lng:button.remove_selected|Remove selected files/folders", Left = 568, Top = 4, Width = 260, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            listFiles = new ListView
            {
                Left = 0,
                Top = 46,
                Width = 828,
                Height = 414,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                HideSelection = false,
                AllowColumnReorder = true,
                UseCompatibleStateImageBehavior = false
            };

            btnPrefix = new Button { Text = T("button.prefix", "Add prefix"), Tag = "lng:button.prefix|Add prefix", Left = 0, Top = 470, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnSuffix = new Button { Text = T("button.suffix", "Add suffix"), Tag = "lng:button.suffix|Add suffix", Left = 134, Top = 470, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnReplace = new Button { Text = T("button.replace", "Replace name"), Tag = "lng:button.replace|Replace name", Left = 268, Top = 470, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnExecuteRename = new Button { Text = T("button.execute_rename", "Apply rename"), Tag = "lng:button.execute_rename|Apply rename", Left = 560, Top = 470, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnResetRename = new Button { Text = T("button.reset_rename", "Reset names"), Tag = "lng:button.reset_rename|Reset names", Left = 694, Top = 470, Width = 134, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

            btnRemoveSubstring = new Button { Text = T("button.remove_substring", "Delete name part"), Tag = "lng:button.remove_substring|Delete name part", Left = 0, Top = 508, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnSelectAll = new Button { Text = T("button.select_all", "Select all"), Tag = "lng:button.select_all|Select all", Left = 134, Top = 508, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnSelectNone = new Button { Text = T("button.select_none", "Select none"), Tag = "lng:button.select_none|Select none", Left = 268, Top = 508, Width = 128, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            chkIncludeLowendPngInList = new CenteredCheckBox { Text = T("check.show_lowend_png", "Show .lowend.png"), Tag = "lng:check.show_lowend_png|Show .lowend.png", Left = 636, Top = 506, Width = 192, Height = 22, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft, Checked = true };
            chkIncludeSpritesInList = new CenteredCheckBox { Text = T("check.show_sprite", "Show .sprite"), Tag = "lng:check.show_sprite|Show .sprite", Left = 636, Top = 530, Width = 192, Height = 22, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft, Checked = false };
            chkIncludeLowendSpritesInList = new CenteredCheckBox { Text = T("check.show_lowend_sprite", "Show .lowend.sprite"), Tag = "lng:check.show_lowend_sprite|Show .lowend.sprite", Left = 636, Top = 554, Width = 192, Height = 22, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft, Checked = false, Enabled = false };

            lblStatus = new Label { Left = 0, Top = 554, Width = 628, Height = 22, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Text = T("status.ready", "Ready"), Tag = "lng:status.ready|Ready", ForeColor = Color.ForestGreen, TextAlign = ContentAlignment.MiddleLeft };
            btnConvert = new ColoredKeywordButton { Text = T("button.convert", "PNG → lowend\r\n\r\n(50% resize)"), Tag = "lng:button.convert|PNG → lowend\r\n\r\n(50% resize)", Left = 16, Top = 846, Width = 262, Height = 64, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, TextAlign = ContentAlignment.MiddleCenter };
            btnResetAll = new ColoredKeywordButton { Text = T("button.reset", "Reset"), Tag = "lng:button.reset|Reset", Left = 284, Top = 846, Width = 128, Height = 64, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, TextAlign = ContentAlignment.MiddleCenter };
            btnSpriteToPng = new ColoredKeywordButton { Text = T("button.sprite_to_png", "sprite → PNG\r\n\r\n(loaded files only)"), Tag = "lng:button.sprite_to_png|sprite → PNG\r\n\r\n(loaded files only)", Left = 16, Top = 918, Width = 262, Height = 64, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, TextAlign = ContentAlignment.MiddleCenter };
            chkOutputPngFolder = new CenteredCheckBox { Text = T("check.output_png", "Save to \\output_png"), Tag = "lng:check.output_png|Save to \\output_png", Left = 18, Top = 986, Width = 250, Height = 24, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Checked = true };
            btnPngToSprite = new ColoredKeywordButton { Text = T("button.png_to_sprite", "PNG → sprite\r\n\r\n(loaded PNGs)"), Tag = "lng:button.png_to_sprite|PNG → sprite\r\n\r\n(loaded PNGs)", Left = 284, Top = 918, Width = 262, Height = 64, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, TextAlign = ContentAlignment.MiddleCenter };
            chkOutputSpriteFolder = new CenteredCheckBox { Text = T("check.output_sprite", "Save to \\output_sprite"), Tag = "lng:check.output_sprite|Save to \\output_sprite", Left = 286, Top = 986, Width = 250, Height = 24, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Checked = true };
            bottomNoticePanel = new LightBorderPanel { Left = 558, Top = 847, Width = 626, Height = 62, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control };
            lblBottomNotice1 = new Label { Text = T("notice.resize_png_only", "- Size conversion applies only to png; .lowend.png is not converted."), Tag = "lng:notice.resize_png_only|- Size conversion applies only to png; .lowend.png is not converted.", Left = 14, Top = 11, Width = 730, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft };
            lblBottomNotice2 = new Label { Text = T("notice.preview_doubleclick", "- Expanded preview: double-click a file row or thumbnail / Esc = close"), Tag = "lng:notice.preview_doubleclick|- Expanded preview: double-click a file row or thumbnail / Esc = close", Left = 14, Top = 35, Width = 730, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft };
            bottomNoticePanel.Controls.Add(lblBottomNotice1);
            bottomNoticePanel.Controls.Add(lblBottomNotice2);

            spriteNoticePanel = new LightBorderPanel { Left = 558, Top = 919, Width = 626, Height = 62, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control };
            lblSpriteNotice1 = new Label { Text = T("notice.sprite_no_frame", "- PNG → sprite: loaded same-folder/same-name frame sprite is used as a template."), Tag = "lng:notice.sprite_no_frame|- PNG → sprite: loaded same-folder/same-name frame sprite is used as a template.", Left = 14, Top = 11, Width = 598, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft };
            lblSpriteNotice2 = new Label { Text = T("notice.sprite_no_frame_example", "Without a template, PNG is converted as a static sprite."), Tag = "lng:notice.sprite_no_frame_example|Without a template, PNG is converted as a static sprite.", Left = 28, Top = 35, Width = 584, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.RoyalBlue };
            spriteNoticePanel.Controls.Add(lblSpriteNotice1);
            spriteNoticePanel.Controls.Add(lblSpriteNotice2);

            panelList.Controls.AddRange(new Control[] { btnClear, btnRefresh, btnClearSelected, listFiles, btnPrefix, btnSuffix, btnReplace, btnExecuteRename, btnResetRename, btnRemoveSubstring, btnSelectAll, btnSelectNone, chkIncludeLowendPngInList, chkIncludeSpritesInList, chkIncludeLowendSpritesInList, lblStatus });

            groupPreview = new CenteredBorderlessGroupBox { Text = T("group.preview", "Selected file thumbnail"), Tag = "lng:group.preview|Selected file thumbnail", Left = 858, Top = 254, Width = 326, Height = 576, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };
            previewFrame = new PreviewFramePanel { Left = 12, Top = 45, Width = 302, Height = 493, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            previewPanel = new TransparentPreviewPanel { Left = 1, Top = 1, Width = 300, Height = 414, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BorderStyle = BorderStyle.None };
            lblPreviewInfo = new PreviewInfoPanel { Left = 1, Top = 416, Width = 300, Height = 76, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Control, TabStop = false, Cursor = Cursors.Default };
            lblPreviewInfo.HeaderLinkClicked += delegate { ShowCurrentMetadataInfoDialog(); };
            previewFrame.Controls.Add(previewPanel);
            previewFrame.Controls.Add(lblPreviewInfo);
            previewPanel.Cursor = Cursors.Hand;
            previewPanel.MouseDoubleClick += delegate { OpenPreviewViewerForSelectedFile(); };
            previewFrame.MouseDoubleClick += delegate { OpenPreviewViewerForSelectedFile(); };
            previewFrame.Resize += delegate { LayoutPreviewFrame(); };
            panelList.Resize += delegate { LayoutListFooterControls(); LayoutPreviewArea(); };
            groupPreview.Resize += delegate { LayoutPreviewArea(); };
            chkPreviewConverted = new CenteredCheckBox { Text = T("check.preview_converted", "Thumbnail after resize"), Tag = "lng:check.preview_converted|Thumbnail after resize", Left = 53, Top = 548, Width = 220, Height = 22, Anchor = AnchorStyles.Bottom, CenterContent = true };
            groupPreview.Controls.Add(previewFrame);
            groupPreview.Controls.Add(chkPreviewConverted);
            LayoutListFooterControls();
            LayoutPreviewArea();
            ApplyFixedNoticePanels();
            ApplyPreviewDisplaySettings();

            toolTip = new ToolTip();
            toolTip.InitialDelay = 0;
            toolTip.ReshowDelay = 0;
            toolTip.AutoPopDelay = 30000;
            toolTip.ShowAlways = true;
            toolTip.SetToolTip(btnPrefix, T("tooltip.prefix", "Add text before the selected filenames."));
            toolTip.SetToolTip(btnSuffix, T("tooltip.suffix", "Add text between the selected filename and extension."));
            toolTip.SetToolTip(btnReplace, T("tooltip.replace", "Replace text in the selected filenames."));
            toolTip.SetToolTip(chkPreviewConverted, T("tooltip.preview_converted", "Preview the selected PNG at its converted size."));
            toolTip.SetToolTip(listFiles, T("tooltip.list", "Double-click a file row to open the expanded preview window."));
            toolTip.SetToolTip(previewPanel, T("tooltip.thumbnail", "Double-click the thumbnail to open the expanded preview window."));
            toolTip.SetToolTip(previewFrame, T("tooltip.thumbnail", "Double-click the thumbnail to open the expanded preview window."));
            toolTip.SetToolTip(chkIncludeLowendPngInList, T("tooltip.show_lowend_png", "Show .lowend.png entries in the list. They are not conversion targets."));
            toolTip.SetToolTip(chkIncludeSpritesInList, T("tooltip.show_sprite", "Show regular .sprite entries. Preview reads only the .sprite file itself."));
            toolTip.SetToolTip(chkIncludeLowendSpritesInList, T("tooltip.show_lowend_sprite", "Show .lowend.sprite entries. Preview reads only the .lowend.sprite file itself."));
            toolTip.SetToolTip(btnResetAll, T("tooltip.reset", "Reset work options and clear the file list."));
            toolTip.SetToolTip(btnSpriteToPng, T("tooltip.sprite_to_png", "Convert loaded .sprite files to PNG."));
            toolTip.SetToolTip(btnPngToSprite, T("tooltip.png_to_sprite", "Convert loaded .png and .lowend.png files independently to .sprite files."));
            toolTip.SetToolTip(chkOutputPngFolder, T("tooltip.output_png", "When checked, save converted PNG files in an output_png folder."));
            toolTip.SetToolTip(chkOutputSpriteFolder, T("tooltip.output_sprite", "When checked, save converted sprite files in an output_sprite folder."));
            toolTip.SetToolTip(lblPreviewInfo, T("tooltip.metadata_view", "View metadata."));

            InitializeTrayIcon();
            UpdateSplash(splash, 70);

            ApplyGroupTitleBold(groupForce);
            ApplyGroupTitleBold(groupCustomSuffix);
            ApplyGroupTitleBold(groupPreview);

            Controls.AddRange(new Control[] { menuStrip, dropPanel, groupForce, groupCustomSuffix, panelList, groupPreview, btnConvert, btnResetAll, btnSpriteToPng, chkOutputPngFolder, btnPngToSprite, chkOutputSpriteFolder, bottomNoticePanel, spriteNoticePanel });

            Size = MinimumSize;
            CenterToScreen();

            BuildColumns(false);
            WireEvents();
            LoadSettings();
            UpdateSplash(splash, 86);
            UpdateTrayVisibility();
            ApplyLanguage();
            ApplyPreviewDisplaySettings();
            BuildColumns(false);
            ApplyOptionState();
            UpdatePreview();
            if (WindowState == FormWindowState.Normal) lastVisibleBounds = Bounds;
            UpdateSplash(splash, 96);
        }

        private void UpdateSplash(StartupSplashForm splash, int progress)
        {
            if (splash == null || splash.IsDisposed) return;
            splash.SetProgress(progress);
        }

        private void ApplyGroupTitleBold(GroupBox group)
        {
            if (group == null) return;

            group.Font = new Font(Font, FontStyle.Bold);
            SetChildrenFont(group, Font);
        }

        private void SetChildrenFont(Control parent, Font font)
        {
            foreach (Control child in parent.Controls)
            {
                child.Font = font;
                SetChildrenFont(child, font);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (WindowState == FormWindowState.Normal) lastVisibleBounds = Bounds;
            BeginInvoke(new MethodInvoker(BringAppToFrontOnce));
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            RememberVisibleBounds();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RememberVisibleBounds();
        }

        private void RememberVisibleBounds()
        {
            if (trayAnimationInProgress) return;
            if (!Visible) return;
            if (WindowState != FormWindowState.Normal) return;
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            lastVisibleBounds = Bounds;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && closeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                UpdateColumnSettingsFromListView();
                SaveSettings();
                e.Cancel = true;
                HideToTray();
                return;
            }

            UpdateColumnSettingsFromListView();
            SaveSettings();
            base.OnFormClosing(e);
        }

        private void BringAppToFrontOnce()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
                TopMost = true;
                BringToFront();
                Activate();
                SetForegroundWindow(Handle);
                TopMost = false;
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O))
            {
                SelectFiles();
                return true;
            }

            if (keyData == Keys.F5)
            {
                RefreshFileList();
                return true;
            }

            if (keyData == (Keys.Control | Keys.A) && listFiles != null && (listFiles.Focused || listFiles.ContainsFocus))
            {
                SelectAllEntries();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseLoadedFileResources();
            StopTrayHideAnimation();
            if (trayAnimationTimer != null)
            {
                trayAnimationTimer.Dispose();
                trayAnimationTimer = null;
            }
            if (previewViewer != null && !previewViewer.IsDisposed) previewViewer.Close();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (traySmallIcon != null)
            {
                traySmallIcon.Dispose();
                traySmallIcon = null;
            }
            if (trayMenu != null)
            {
                trayMenu.Dispose();
                trayMenu = null;
            }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (Program.RestoreWindowMessage != 0 && m.Msg == Program.RestoreWindowMessage)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private static Icon LoadEmbeddedIcon(string resourceName)
        {
            try
            {
                using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (Icon source = new Icon(stream))
                    {
                        return (Icon)source.Clone();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayItemSettings = new ToolStripMenuItem();
            trayItemSettings.Click += delegate
            {
                RestoreFromTray();
                ShowSettingsDialog();
            };
            trayItemExit = new ToolStripMenuItem();
            trayItemExit.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(trayItemSettings);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(trayItemExit);

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = trayMenu;
            traySmallIcon = LoadEmbeddedIcon("D2RSpriteToolkit.tray.ico");
            try { trayIcon.Icon = traySmallIcon ?? Icon ?? Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { trayIcon.Icon = SystemIcons.Application; }
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            UpdateTrayTexts();
        }

        private void UpdateTrayTexts()
        {
            if (trayItemSettings != null) trayItemSettings.Text = T("tray.settings", "Settings");
            if (trayItemExit != null) trayItemExit.Text = T("tray.exit", "Exit");
            if (trayIcon != null) trayIcon.Text = TrimTrayText(Text);
        }

        private static string TrimTrayText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "D2R Sprite Toolkit";
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void UpdateTrayVisibility()
        {
            if (trayIcon == null) return;
            trayIcon.Visible = closeToTray;
        }

        private void HideToTray()
        {
            if (trayIcon != null) trayIcon.Visible = true;
            SetStatus(T("status.tray_hidden", "Running in the system tray."), StatusKind.Normal);

            if (!Visible)
            {
                ShowInTaskbar = false;
                Opacity = 1.0;
                return;
            }

            BeginTraySlideAnimation(true);
        }

        private void BeginTraySlideAnimation(bool hide)
        {
            StopTrayHideAnimation();
            trayAnimationInProgress = true;
            trayAnimationHiding = hide;

            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (hide)
                {
                    if (Bounds.Width > 0 && Bounds.Height > 0) lastVisibleBounds = Bounds;
                    trayAnimationStartBounds = Bounds;
                    trayAnimationEndBounds = GetTrayHiddenBounds(trayAnimationStartBounds);
                }
                else
                {
                    Rectangle target = lastVisibleBounds;
                    if (target.Width <= 0 || target.Height <= 0) target = new Rectangle(Location, Size);
                    target = EnsureBoundsOnScreen(target);
                    trayAnimationStartBounds = GetTrayHiddenBounds(target);
                    trayAnimationEndBounds = target;
                    StartPosition = FormStartPosition.Manual;
                    Bounds = trayAnimationStartBounds;
                    Opacity = 1.0;
                    ShowInTaskbar = true;
                    Show();
                    BringToFront();
                }

                trayAnimationStartTime = DateTime.UtcNow;
                if (trayAnimationTimer == null)
                {
                    trayAnimationTimer = new System.Windows.Forms.Timer();
                    trayAnimationTimer.Interval = 10;
                    trayAnimationTimer.Tick += delegate { AdvanceTraySlideAnimation(); };
                }
                trayAnimationTimer.Start();
                AdvanceTraySlideAnimation();
            }
            catch
            {
                FinishTraySlideAnimation();
            }
        }

        private Rectangle GetTrayHiddenBounds(Rectangle visibleBounds)
        {
            Rectangle source = visibleBounds.Width > 0 && visibleBounds.Height > 0 ? visibleBounds : new Rectangle(Location, Size);
            Rectangle workArea;
            try { workArea = Screen.FromRectangle(source).WorkingArea; }
            catch { workArea = Screen.PrimaryScreen.WorkingArea; }

            int visibleHeight = Math.Max(140, source.Height / 4);
            visibleHeight = Math.Min(visibleHeight, Math.Max(60, source.Height - 24));
            int x = workArea.Left + (int)Math.Round(workArea.Width * 0.60);
            x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - 80));
            int y = workArea.Bottom - visibleHeight;
            return new Rectangle(x, y, source.Width, source.Height);
        }

        private Rectangle EnsureBoundsOnScreen(Rectangle bounds)
        {
            Rectangle workArea;
            try { workArea = Screen.FromRectangle(bounds).WorkingArea; }
            catch { workArea = Screen.PrimaryScreen.WorkingArea; }

            int width = Math.Min(bounds.Width, workArea.Width);
            int height = Math.Min(bounds.Height, workArea.Height);
            int x = Math.Max(workArea.Left, Math.Min(bounds.X, workArea.Right - width));
            int y = Math.Max(workArea.Top, Math.Min(bounds.Y, workArea.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        private void AdvanceTraySlideAnimation()
        {
            try
            {
                double elapsed = (DateTime.UtcNow - trayAnimationStartTime).TotalMilliseconds;
                double t = Math.Max(0.0, Math.Min(1.0, elapsed / TrayAnimationDurationMs));
                double eased = t * t * (3.0 - (2.0 * t));
                int x = Lerp(trayAnimationStartBounds.X, trayAnimationEndBounds.X, eased);
                int y = Lerp(trayAnimationStartBounds.Y, trayAnimationEndBounds.Y, eased);
                int w = Lerp(trayAnimationStartBounds.Width, trayAnimationEndBounds.Width, eased);
                int h = Lerp(trayAnimationStartBounds.Height, trayAnimationEndBounds.Height, eased);
                Bounds = new Rectangle(x, y, w, h);
                Opacity = trayAnimationHiding ? Math.Max(0.18, 1.0 - (eased * 0.82)) : Math.Min(1.0, 0.18 + (eased * 0.82));

                if (t >= 1.0) FinishTraySlideAnimation();
            }
            catch
            {
                FinishTraySlideAnimation();
            }
        }

        private static int Lerp(int from, int to, double amount)
        {
            return (int)Math.Round(from + ((to - from) * amount));
        }

        private void FinishTraySlideAnimation()
        {
            if (trayAnimationTimer != null) trayAnimationTimer.Stop();
            try
            {
                if (trayAnimationHiding)
                {
                    Hide();
                    ShowInTaskbar = false;
                    if (lastVisibleBounds.Width > 0 && lastVisibleBounds.Height > 0) Bounds = lastVisibleBounds;
                            if (trayIcon != null) trayIcon.Visible = true;
                }
                else
                {
                    Bounds = trayAnimationEndBounds;
                    ShowInTaskbar = true;
                    UpdateTrayVisibility();
                    BringAppToFrontOnce();
                }
            }
            finally
            {
                Opacity = 1.0;
                trayAnimationInProgress = false;
            }
        }

        private void StopTrayHideAnimation()
        {
            if (trayAnimationTimer != null) trayAnimationTimer.Stop();
            trayAnimationInProgress = false;
            try { Opacity = 1.0; } catch { }
        }

        internal void RestoreFromExternalLaunch()
        {
            RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            if (Visible && !trayAnimationInProgress)
            {
                ShowInTaskbar = true;
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                BringAppToFrontOnce();
                UpdateTrayVisibility();
                return;
            }

            BeginTraySlideAnimation(false);
        }

        private void ExitApplication()
        {
            allowExit = true;
            StopTrayHideAnimation();
            if (trayIcon != null) trayIcon.Visible = false;
            Close();
        }

        private MenuStrip CreateMenuStrip()
        {
            MenuStrip strip = new MenuStrip();
            strip.Dock = DockStyle.Top;
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.RenderMode = ToolStripRenderMode.System;
            strip.BackColor = SystemColors.Control;

            menuFile = new ToolStripMenuItem(T("menu.file", "&File"));
            menuFile.Tag = "lng:menu.file|&File";
            itemAddFiles = new ToolStripMenuItem(T("menu.add_files", "Add files..."));
            itemAddFiles.Tag = "lng:menu.add_files|Add files...";
            itemAddFiles.ShortcutKeys = Keys.Control | Keys.O;
            itemAddFiles.Click += delegate { SelectFiles(); };

            itemAddFolder = new ToolStripMenuItem(T("menu.add_folder", "Add folder..."));
            itemAddFolder.Tag = "lng:menu.add_folder|Add folder...";
            itemAddFolder.Click += delegate { AddFolderFromDialog(); };

            itemExit = new ToolStripMenuItem(T("menu.exit", "Exit"));
            itemExit.Tag = "lng:menu.exit|Exit";
            itemExit.Click += delegate { ConfirmExitFromFileMenu(); };

            menuFile.DropDownItems.Add(itemAddFiles);
            menuFile.DropDownItems.Add(itemAddFolder);
            menuFile.DropDownItems.Add(new ToolStripSeparator());
            menuFile.DropDownItems.Add(itemExit);

            menuSettings = new ToolStripMenuItem(T("menu.settings", "&Settings"));
            menuSettings.Tag = "lng:menu.settings|&Settings";
            menuSettings.Click += delegate { ShowSettingsDialog(); };

            menuInfo = new ToolStripMenuItem(T("menu.info", "&Info"));
            menuInfo.Tag = "lng:menu.info|&Info";
            menuInfo.Click += delegate { ShowCreditDialog(); };

            menuLanguage = new ToolStripMenuItem(T("menu.language", "&Language"));
            menuLanguage.Tag = "lng:menu.language|&Language";
            itemLanguageEnglish = new ToolStripMenuItem(T("language.english", "English"));
            itemLanguageEnglish.Tag = "lng:language.english|English";
            itemLanguageEnglish.Click += delegate { SetLanguage("en"); };
            itemLanguageKorean = new ToolStripMenuItem(T("language.korean", "한국어"));
            itemLanguageKorean.Tag = "lng:language.korean|한국어";
            itemLanguageKorean.Click += delegate { SetLanguage("ko"); };
            menuLanguage.DropDownItems.Add(itemLanguageEnglish);
            menuLanguage.DropDownItems.Add(itemLanguageKorean);

            strip.Items.Add(menuFile);
            strip.Items.Add(menuSettings);
            strip.Items.Add(menuInfo);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(menuLanguage);
            UpdateLanguageMenuChecks();
            return strip;
        }

        private static string T(string key, string fallback)
        {
            return Localization.T(key, fallback);
        }

        private static string T(string key, string fallback, params object[] args)
        {
            return Localization.T(key, fallback, args);
        }

        private string LoadLanguageSettingOnly()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return "en";
                    return Localization.NormalizeLanguage(Convert.ToString(key.GetValue("Language", "en")));
                }
            }
            catch { return "en"; }
        }

        private void SetLanguage(string language)
        {
            string normalized = Localization.NormalizeLanguage(language);
            if (string.Equals(currentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            {
                UpdateLanguageMenuChecks();
                return;
            }
            currentLanguage = normalized;
            Localization.Load(currentLanguage);
            ApplyLanguage();
            SaveSettings();
            SetStatus(T("status.language_changed", "Language changed."), StatusKind.Normal);
        }

        private void ApplyLanguage()
        {
            Text = T("app.title", "D2R Sprite Toolkit");
            if (lblDropHint != null)
            {
                lblDropHint.PrimaryText = T("drop.primary", "Drag and drop PNG / folder / .sprite here");
                lblDropHint.SecondaryText = T("drop.secondary", ".sprite preview reads only the selected .sprite file itself.");
                lblDropHint.Invalidate();
            }
            ApplyLanguageToControlTree(this);
            if (menuStrip != null) ApplyLanguageToToolStripItems(menuStrip.Items);
            UpdateColumnTitles();
            UpdateLanguageMenuChecks();
            ApplyToolTips();
            UpdateTrayTexts();
            ApplyFixedNoticePanels();
            LayoutListFooterControls();
            BuildColumns(true);
            UpdatePreview();
        }

        private void ApplyLanguageToControlTree(Control parent)
        {
            if (parent == null) return;
            string key;
            string fallback;
            if (TryGetLanguageTag(parent.Tag, out key, out fallback)) parent.Text = T(key, fallback);
            foreach (Control child in parent.Controls) ApplyLanguageToControlTree(child);
        }

        private void ApplyLanguageToToolStripItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ToolStripItem item = items[i];
                string key;
                string fallback;
                if (TryGetLanguageTag(item.Tag, out key, out fallback)) item.Text = T(key, fallback);
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null) ApplyLanguageToToolStripItems(menuItem.DropDownItems);
            }
        }

        private static bool TryGetLanguageTag(object tag, out string key, out string fallback)
        {
            key = string.Empty;
            fallback = string.Empty;
            string text = tag as string;
            if (string.IsNullOrEmpty(text) || !text.StartsWith("lng:", StringComparison.OrdinalIgnoreCase)) return false;
            string payload = text.Substring(4);
            int split = payload.IndexOf('|');
            if (split < 0)
            {
                key = payload;
                fallback = payload;
            }
            else
            {
                key = payload.Substring(0, split);
                fallback = payload.Substring(split + 1).Replace("\\r\\n", "\r\n").Replace("\\n", "\r\n");
            }
            return key.Length > 0;
        }

        private void UpdateLanguageMenuChecks()
        {
            if (itemLanguageEnglish != null) itemLanguageEnglish.Checked = string.Equals(currentLanguage, "en", StringComparison.OrdinalIgnoreCase);
            if (itemLanguageKorean != null) itemLanguageKorean.Checked = string.Equals(currentLanguage, "ko", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyToolTips()
        {
            if (toolTip == null) return;
            toolTip.SetToolTip(btnPrefix, T("tooltip.prefix", "Add text before the selected filenames."));
            toolTip.SetToolTip(btnSuffix, T("tooltip.suffix", "Add text between the selected filename and extension."));
            toolTip.SetToolTip(btnReplace, T("tooltip.replace", "Replace text in the selected filenames."));
            toolTip.SetToolTip(chkPreviewConverted, T("tooltip.preview_converted", "Preview the selected PNG at its converted size."));
            toolTip.SetToolTip(listFiles, T("tooltip.list", "Double-click a file row to open the expanded preview window."));
            toolTip.SetToolTip(previewPanel, T("tooltip.thumbnail", "Double-click the thumbnail to open the expanded preview window."));
            toolTip.SetToolTip(previewFrame, T("tooltip.thumbnail", "Double-click the thumbnail to open the expanded preview window."));
            toolTip.SetToolTip(chkIncludeLowendPngInList, T("tooltip.show_lowend_png", "Show .lowend.png entries in the list. They are not conversion targets."));
            toolTip.SetToolTip(chkIncludeSpritesInList, T("tooltip.show_sprite", "Show regular .sprite entries. Preview reads only the .sprite file itself."));
            toolTip.SetToolTip(chkIncludeLowendSpritesInList, T("tooltip.show_lowend_sprite", "Show .lowend.sprite entries. Preview reads only the .lowend.sprite file itself."));
            toolTip.SetToolTip(btnResetAll, T("tooltip.reset", "Reset work options and clear the file list."));
            toolTip.SetToolTip(btnSpriteToPng, T("tooltip.sprite_to_png", "Convert loaded .sprite files to PNG."));
            toolTip.SetToolTip(btnPngToSprite, T("tooltip.png_to_sprite", "Convert loaded .png and .lowend.png files independently to .sprite files."));
            toolTip.SetToolTip(chkOutputPngFolder, T("tooltip.output_png", "When checked, save converted PNG files in an output_png folder."));
            toolTip.SetToolTip(chkOutputSpriteFolder, T("tooltip.output_sprite", "When checked, save converted sprite files in an output_sprite folder."));
            toolTip.SetToolTip(lblPreviewInfo, T("tooltip.metadata_view", "View metadata."));
        }

        private void UpdateColumnTitles()
        {
            SetColumnTitle("Order", T("column.order", "#"));
            SetColumnTitle("FileName", T("column.filename", "File name"));
            SetColumnTitle("NewName", T("column.newname", "New file name"));
            SetColumnTitle("Folder", T("column.folder", "Path"));
            SetColumnTitle("Type", T("column.type", "Type"));
            SetColumnTitle("ConversionResult", T("column.conversion_result", "Result"));
            SetColumnTitle("Created", T("column.created", "Created"));
            SetColumnTitle("Modified", T("column.modified", "Modified"));
            SetColumnTitle("Accessed", T("column.accessed", "Accessed"));
            SetColumnTitle("ImageSize", T("column.image_size", "Image size"));
            SetColumnTitle("Width", T("column.width", "Width"));
            SetColumnTitle("Height", T("column.height", "Height"));
            SetColumnTitle("BitDepth", T("column.bit_depth", "Bit depth"));
            SetColumnTitle("FileSize", T("column.file_size", "File size"));
        }

        private void SetColumnTitle(string id, string title)
        {
            ColumnSpec spec = GetColumnSpec(id);
            if (spec != null) spec.Title = title;
        }

        private void ShowSettingsDialog()
        {
            using (SettingsDialog dialog = new SettingsDialog(
                autoSizeByLowend,
                clearBeforeDrop,
                loadMatchingSprites,
                includeLowendSprites,
                includeSubfolders,
                previewNavigationMode,
                previewZoomStepPercent,
                previewFixedZoomBasePercent,
                previewUseCanvasColor,
                previewCanvasColor,
                previewUseAlphaColor,
                previewAlphaColor,
                closeToTray,
                convertSelectedOnly))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                autoSizeByLowend = dialog.AutoSizeByLowend;
                clearBeforeDrop = dialog.ClearBeforeDrop;
                loadMatchingSprites = dialog.LoadMatchingSprites;
                includeLowendSprites = dialog.IncludeLowendSprites;
                includeSubfolders = dialog.IncludeSubfolders;
                previewNavigationMode = dialog.PreviewNavigationMode;
                previewZoomStepPercent = dialog.PreviewZoomStepPercent;
                previewFixedZoomBasePercent = dialog.PreviewFixedZoomBasePercent;
                previewUseCanvasColor = dialog.PreviewUseCanvasColor;
                previewCanvasColor = dialog.PreviewCanvasColor;
                previewUseAlphaColor = dialog.PreviewUseAlphaColor;
                previewAlphaColor = dialog.PreviewAlphaColor;
                closeToTray = dialog.CloseToTray;
                convertSelectedOnly = dialog.ConvertSelectedOnly;
                UpdateTrayVisibility();
                ApplyPreviewDisplaySettings();
                ApplyPreviewViewerSettings();
                ApplyOptionState();
                SaveSettings();
                UpdatePreview();
                SetStatus(T("status.settings_saved", "Settings saved."), StatusKind.Normal);
            }
        }

        private void WireEvents()
        {
            EnableDragDropForAllControls(this);

            chkForceSize.CheckedChanged += delegate { ApplyOptionState(); SaveSettings(); UpdatePreview(); };
            numForceWidth.ValueChanged += delegate { SaveSettings(); UpdatePreview(); };
            numForceHeight.ValueChanged += delegate { SaveSettings(); UpdatePreview(); };
            chkPreviewConverted.CheckedChanged += delegate { SaveSettings(); UpdatePreview(); };
            chkCustomSuffix.CheckedChanged += delegate { ApplyOptionState(); SaveSettings(); };
            txtCustomSuffix.TextChanged += delegate { SaveSettings(); };

            btnClear.Click += delegate { ClearList(); };
            btnRefresh.Click += delegate { RefreshFileList(); };
            btnClearSelected.Click += delegate { RemoveSelectedEntries(); };
            btnConvert.Click += delegate { ConvertFiles(); };
            btnSpriteToPng.Click += delegate { ConvertSpritesToPng(); };
            btnPngToSprite.Click += delegate { ConvertPngsToSprite(); };
            chkOutputPngFolder.CheckedChanged += delegate { SaveSettings(); };
            chkOutputSpriteFolder.CheckedChanged += delegate { SaveSettings(); };
            btnResetAll.Click += delegate { ResetMainWorkState(); };

            btnSelectAll.Click += delegate { SelectAllEntries(); };
            btnSelectNone.Click += delegate { SelectNoneEntries(); };
            chkIncludeLowendPngInList.CheckedChanged += delegate
            {
                if (loadingSettings) return;
                SaveSettings();
                RefreshFileList();
            };
            chkIncludeSpritesInList.CheckedChanged += delegate
            {
                ApplyOptionState();
                if (loadingSettings) return;
                SaveSettings();
                RefreshFileList();
            };
            chkIncludeLowendSpritesInList.CheckedChanged += delegate
            {
                if (loadingSettings) return;
                SaveSettings();
                RefreshFileList();
            };
            btnPrefix.Click += delegate { ApplyPrefixToSelected(); };
            btnSuffix.Click += delegate { ApplySuffixToSelected(); };
            btnReplace.Click += delegate { ApplyReplaceToSelected(); };
            btnRemoveSubstring.Click += delegate { ApplyRemoveSubstringFromSelected(); };
            btnExecuteRename.Click += delegate { ExecutePendingRenames(); };
            btnResetRename.Click += delegate { ResetPendingRenames(); };

            listFiles.SelectedIndexChanged += delegate { UpdatePreview(); };
            listFiles.ColumnClick += OnColumnClick;
            listFiles.MouseUp += OnListMouseUp;
            listFiles.MouseDoubleClick += OnListMouseDoubleClick;
            listFiles.KeyDown += OnListKeyDown;
            listFiles.ColumnWidthChanged += delegate { if (!buildingColumns && !loadingSettings) { UpdateColumnSettingsFromListView(); SaveSettings(); } };
            listFiles.ColumnReordered += delegate { if (!buildingColumns && !loadingSettings) BeginInvoke(new MethodInvoker(delegate { UpdateColumnSettingsFromListView(); SaveSettings(); })); };

        }

        private void ApplyOptionState()
        {
            bool force = chkForceSize.Checked;
            numForceWidth.Enabled = force;
            numForceHeight.Enabled = force;

            txtCustomSuffix.Enabled = chkCustomSuffix.Checked;
            bool includeSprites = chkIncludeSpritesInList.Checked;
            if (!includeSprites && chkIncludeLowendSpritesInList.Checked)
            {
                bool oldLoading = loadingSettings;
                loadingSettings = true;
                chkIncludeLowendSpritesInList.Checked = false;
                loadingSettings = oldLoading;
            }
            chkIncludeLowendSpritesInList.Enabled = includeSprites;
            LayoutListFooterControls();
        }

        private void EnableDragDropForAllControls(Control root)
        {
            if (root == null) return;
            root.AllowDrop = true;
            root.DragEnter -= OnAnyDragEnter;
            root.DragDrop -= OnAnyDragDrop;
            root.DragEnter += OnAnyDragEnter;
            root.DragDrop += OnAnyDragDrop;

            foreach (Control child in root.Controls)
            {
                EnableDragDropForAllControls(child);
            }
        }

        private void OnAnyDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        private void OnAnyDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            AddPaths(paths, clearBeforeDrop, true);
        }

        private void ShowCreditDialog()
        {
            using (CreditDialog dialog = new CreditDialog(InvenLinkUrl, NexusLinkUrl, AppVersion))
            {
                dialog.ShowDialog(this);
            }
        }

        private void ConfirmExitFromFileMenu()
        {
            ExitMenuChoice choice = ExitConfirmDialog.ShowPrompt(this);
            if (choice == ExitMenuChoice.Exit)
            {
                ExitApplication();
            }
            else if (choice == ExitMenuChoice.Tray)
            {
                UpdateColumnSettingsFromListView();
                SaveSettings();
                HideToTray();
            }
        }

        internal static void OpenPostLink(IWin32Window owner, string target)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = target;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
                MessageBox.Show(owner, "링크를 열 수 없습니다.", "링크 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private enum StatusKind
        {
            Ready,
            Normal,
            Error
        }

        private void SetStatus(string message, StatusKind kind)
        {
            lblStatus.Text = message;
            if (kind == StatusKind.Ready) lblStatus.ForeColor = Color.ForestGreen;
            else if (kind == StatusKind.Error) lblStatus.ForeColor = Color.Firebrick;
            else lblStatus.ForeColor = Color.RoyalBlue;
        }

        private enum FolderAddMode
        {
            MatchingPngFiles,
            AllPngAndSpriteFiles
        }

        private void AddFolderFromDialog()
        {
            string selectedPath;
            if (VistaFolderPicker.TryPickFolder(this, lastFolderPath, out selectedPath))
            {
                DialogResult result = MessageBox.Show(
                    this,
                    T("folder.matching_prompt", "Load only files with names matching PNG files?\r\n\r\nYes: load only matching .lowend.png/.sprite/.lowend.sprite.\r\nNo: load every PNG and .sprite file in the folder."),
                    T("folder.matching_title", "Add folder"),
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel)
                {
                    SetStatus("폴더 추가를 취소했습니다.", StatusKind.Normal);
                    return;
                }

                lastFolderPath = selectedPath;
                AddFolder(selectedPath, result == DialogResult.Yes ? FolderAddMode.MatchingPngFiles : FolderAddMode.AllPngAndSpriteFiles);
                SaveSettings();
            }
        }

        private void SelectFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = T("openfile.title", "Select PNG or Sprite files");
                dialog.Filter = T("openfile.filter", "PNG/Sprite files (*.png;*.sprite)|*.png;*.sprite|PNG files (*.png)|*.png|Sprite files (*.sprite)|*.sprite");
                dialog.Multiselect = true;
                string initial = NormalizeInitialFolder(lastFolderPath);
                if (Directory.Exists(initial)) dialog.InitialDirectory = initial;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(dialog.FileNames, false, true);
                }
            }
        }

        private void ClearList()
        {
            ClearListInternal();
            SetStatus("파일 목록을 비웠습니다.", StatusKind.Normal);
            UpdatePreview();
        }

        private void ReleaseLoadedFileResources()
        {
            try { previewPanel.SetImage(null); } catch { }
            try
            {
                if (previewViewer != null && !previewViewer.IsDisposed)
                {
                    previewViewer.Close();
                    previewViewer = null;
                }
            }
            catch { }
        }

        private void ClearListInternal()
        {
            ReleaseLoadedFileResources();
            entries.Clear();
            fileSet.Clear();
            nextOrder = 1;
            BuildColumns(false);
        }

        private void AddPaths(IEnumerable<string> paths, bool clearBeforeAdd, bool directAdd)
        {
            List<string> source = new List<string>();
            foreach (string path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path)) source.Add(path);
            }
            if (source.Count == 0) return;

            // .sprite만 드롭한 경우에는 기존 PNG 작업 목록을 보존한다.
            if (clearBeforeAdd && HasPngOrFolder(source)) ClearListInternal();

            int before = fileSet.Count;
            for (int i = 0; i < source.Count; i++)
            {
                string path = source[i];
                if (Directory.Exists(path))
                {
                    AddFolderEntries(path, directAdd ? FolderAddMode.AllPngAndSpriteFiles : FolderAddMode.MatchingPngFiles);
                }
                else if (IsTargetPng(path) || IsLowendPngFile(path) || IsSpriteFile(path))
                {
                    if (directAdd) AddFileDirect(path, true);
                    else AddFile(path);
                }
            }

            int added = fileSet.Count - before;
            RenumberEntries();
            RebuildListView(true);
            SetStatus(added + "개 항목을 추가했습니다. (직접 추가 파일은 동명 PNG 여부와 무관)", StatusKind.Normal);
            SelectFirstItemIfNothingSelected();
        }

        private bool HasPngOrFolder(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (Directory.Exists(path)) return true;
                if (IsTargetPng(path) || IsLowendPngFile(path)) return true;
            }
            return false;
        }

        private void AddFolder(string folder, FolderAddMode mode)
        {
            int added = AddFolderEntries(folder, mode);
            RebuildListView(true);
            string modeText = mode == FolderAddMode.MatchingPngFiles ? "PNG 동명 파일 기준" : "모든 PNG/.sprite";
            SetStatus(added + "개 항목을 폴더에서 추가했습니다. (" + modeText + ")", StatusKind.Normal);
            SelectFirstItemIfNothingSelected();
        }

        private int AddFolderEntries(string folder, FolderAddMode mode)
        {
            int before = fileSet.Count;

            if (mode == FolderAddMode.AllPngAndSpriteFiles)
            {
                foreach (string file in EnumeratePngAndSpriteFiles(folder, includeSubfolders))
                {
                    AddFileDirect(file, false);
                }
                return fileSet.Count - before;
            }

            foreach (string file in EnumeratePngFiles(folder, includeSubfolders))
            {
                if (IsTargetPng(file)) AddFileDirect(file, true);
                else if (IsLowendPngFile(file))
                {
                    AddEntry(file);
                    if (chkIncludeSpritesInList.Checked && chkIncludeLowendSpritesInList.Checked) AddMatchingLowendSpriteFileDirect(file);
                }
            }
            return fileSet.Count - before;
        }

        private void AddFile(string path)
        {
            if (IsTargetPng(path))
            {
                AddEntry(path);
                AddMatchingLowendPngFile(path);
                AddMatchingSpriteFiles(path);
            }
            else if (IsLowendPngFile(path))
            {
                if (ShouldShowLowendPngPath(path)) AddEntry(path);
            }
            else if (IsSpriteFile(path))
            {
                if (ShouldShowSpritePath(path)) AddEntry(path);
            }
        }

        private void AddFileDirect(string path, bool includeRelatedForTargetPng)
        {
            if (IsTargetPng(path))
            {
                AddEntry(path);
                if (includeRelatedForTargetPng)
                {
                    AddMatchingLowendPngFile(path);
                    AddMatchingSpriteFiles(path);
                }
            }
            else if (IsLowendPngFile(path))
            {
                AddEntry(path);
                if (includeRelatedForTargetPng && chkIncludeSpritesInList.Checked && chkIncludeLowendSpritesInList.Checked) AddMatchingLowendSpriteFileDirect(path);
            }
            else if (IsSpriteFile(path))
            {
                AddEntry(path);
            }
        }

        private bool AddEntry(string path)
        {
            return AddEntry(path, false);
        }

        private bool AddEntry(string path, bool autoReference)
        {
            string full = Path.GetFullPath(path);
            if (!fileSet.Add(full))
            {
                if (!autoReference)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (string.Equals(entries[i].FullPath, full, StringComparison.OrdinalIgnoreCase))
                        {
                            entries[i].AutoReference = false;
                            break;
                        }
                    }
                }
                return false;
            }

            FileEntry entry = new FileEntry(full, nextOrder++);
            entry.AutoReference = autoReference;
            entry.RefreshMetadata();
            entries.Add(entry);
            sortColumnId = "Order";
            sortAscending = true;
            return true;
        }

        private void AddMatchingSpriteFiles(string pngPath)
        {
            if (!loadMatchingSprites) return;

            bool includeLowendForCurrentView = includeLowendSprites && chkIncludeSpritesInList.Checked && chkIncludeLowendSpritesInList.Checked;
            foreach (string spritePath in GetMatchingSpritePathsForPng(pngPath, includeLowendForCurrentView))
            {
                if (IsSpriteFile(spritePath) && ShouldShowSpritePath(spritePath)) AddEntry(spritePath, true);
            }
        }

        private void AddMatchingSpriteFilesDirect(string pngPath, bool includeLowendSprite)
        {
            foreach (string spritePath in GetMatchingSpritePathsForPng(pngPath, includeLowendSprite))
            {
                if (IsSpriteFile(spritePath)) AddEntry(spritePath, true);
            }
        }

        private void AddMatchingLowendPngFile(string pngPath)
        {
            if (chkIncludeLowendPngInList == null || !chkIncludeLowendPngInList.Checked) return;
            string lowendPath = GetMatchingLowendPngPath(pngPath);
            if (IsLowendPngFile(lowendPath)) AddEntry(lowendPath, true);
        }

        private void AddMatchingLowendPngFileDirect(string pngPath)
        {
            string lowendPath = GetMatchingLowendPngPath(pngPath);
            if (IsLowendPngFile(lowendPath)) AddEntry(lowendPath, true);
        }

        private void AddMatchingLowendSpriteFileDirect(string lowendPngPath)
        {
            string dir = Path.GetDirectoryName(lowendPngPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(lowendPngPath) ?? string.Empty;
            string spritePath = Path.Combine(dir, baseName + ".sprite");
            if (IsSpriteFile(spritePath)) AddEntry(spritePath, true);
        }

        private static string GetMatchingLowendPngPath(string pngPath)
        {
            string dir = Path.GetDirectoryName(pngPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(pngPath) ?? string.Empty;
            return Path.Combine(dir, baseName + ".lowend.png");
        }

        private bool ShouldShowLowendPngPath(string path)
        {
            return chkIncludeLowendPngInList != null && chkIncludeLowendPngInList.Checked && IsLowendPngFile(path);
        }

        private bool ShouldShowSpritePath(string spritePath)
        {
            if (!IsSpriteFile(spritePath)) return true;
            if (IsLowendSpriteFile(spritePath)) return chkIncludeSpritesInList.Checked && chkIncludeLowendSpritesInList.Checked;
            return chkIncludeSpritesInList.Checked;
        }

        private IEnumerable<string> EnumeratePngFiles(string folder, bool recursive)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] files = new string[0];
                try { files = Directory.GetFiles(current, "*.png"); } catch { }
                foreach (string file in files) yield return file;

                if (!recursive) continue;

                string[] dirs = new string[0];
                try { dirs = Directory.GetDirectories(current); } catch { }
                foreach (string dir in dirs) stack.Push(dir);
            }
        }

        private IEnumerable<string> EnumeratePngAndSpriteFiles(string folder, bool recursive)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] files = new string[0];
                try { files = Directory.GetFiles(current); } catch { }

                foreach (string file in files)
                {
                    if (string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase) || IsSpriteFile(file))
                    {
                        yield return file;
                    }
                }

                if (!recursive) continue;

                string[] dirs = new string[0];
                try { dirs = Directory.GetDirectories(current); } catch { }
                foreach (string dir in dirs) stack.Push(dir);
            }
        }

        private bool IsTargetPng(string path)
        {
            if (!File.Exists(path)) return false;
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return false;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return !nameWithoutExt.EndsWith(".lowend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLowendPngFile(string path)
        {
            if (!File.Exists(path)) return false;
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return false;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return nameWithoutExt.EndsWith(".lowend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLowendPngEntry(FileEntry entry)
        {
            return entry != null && IsLowendPngFile(entry.FullPath);
        }

        private static bool IsSpriteFile(string path)
        {
            if (!File.Exists(path)) return false;
            string fileName = Path.GetFileName(path) ?? string.Empty;
            return fileName.EndsWith(".sprite", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLowendSpriteFile(string path)
        {
            string fileName = Path.GetFileName(path) ?? string.Empty;
            return fileName.EndsWith(".lowend.sprite", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpriteEntry(FileEntry entry)
        {
            return entry != null && IsSpriteFile(entry.FullPath);
        }

        private bool TryReadLoadedSpriteInfo(string path, out D2RSpriteInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            return D2RSpritePreview.TryReadSpriteInfo(path, out info);
        }

        private bool TryFindLoadedFrameTemplateForPng(string pngPath, out D2RSpriteInfo info)
        {
            string templatePath;
            return TryFindLoadedFrameTemplateForPng(pngPath, out info, out templatePath);
        }

        private bool TryFindLoadedFrameTemplateForPng(string pngPath, out D2RSpriteInfo info, out string templatePath)
        {
            info = null;
            templatePath = string.Empty;
            if (string.IsNullOrEmpty(pngPath)) return false;
            string dir = Path.GetDirectoryName(pngPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(pngPath) ?? string.Empty;
            templatePath = Path.GetFullPath(Path.Combine(dir, baseName + ".sprite"));
            if (!fileSet.Contains(templatePath)) return false;
            if (!TryReadLoadedSpriteInfo(templatePath, out info)) return false;
            return Math.Max(1, info.FrameCount) >= 2;
        }

        private bool IsFrameSpriteEntry(FileEntry entry)
        {
            D2RSpriteInfo info;
            return entry != null && IsSpriteFile(entry.FullPath) && TryReadLoadedSpriteInfo(entry.FullPath, out info) && Math.Max(1, info.FrameCount) >= 2;
        }

        private bool HasLoadedFrameTemplateForPng(FileEntry entry)
        {
            D2RSpriteInfo info;
            return entry != null && (IsTargetPng(entry.FullPath) || IsLowendPngFile(entry.FullPath)) && TryFindLoadedFrameTemplateForPng(entry.FullPath, out info);
        }

        private static IEnumerable<string> GetMatchingSpritePathsForPng(string pngPath, bool includeLowendSprite)
        {
            string dir = Path.GetDirectoryName(pngPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(pngPath) ?? string.Empty;
            yield return Path.Combine(dir, baseName + ".sprite");
            if (includeLowendSprite) yield return Path.Combine(dir, baseName + ".lowend.sprite");
        }

        private static string GetManagedFileSuffix(string fileName)
        {
            string name = fileName ?? string.Empty;
            if (name.EndsWith(".lowend.sprite", StringComparison.OrdinalIgnoreCase)) return ".lowend.sprite";
            if (name.EndsWith(".sprite", StringComparison.OrdinalIgnoreCase)) return ".sprite";
            if (name.EndsWith(".lowend.png", StringComparison.OrdinalIgnoreCase)) return ".lowend.png";
            string ext = Path.GetExtension(name);
            return string.IsNullOrEmpty(ext) ? ".png" : ext;
        }

        private static string GetEditableBaseName(string fileName)
        {
            string name = fileName ?? string.Empty;
            string suffix = GetManagedFileSuffix(name);
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - suffix.Length);
            }
            return Path.GetFileNameWithoutExtension(name);
        }

        private static string BuildFileNameWithSameSuffix(string referenceFileName, string baseName)
        {
            return baseName + GetManagedFileSuffix(referenceFileName);
        }

        private void ConvertFiles()
        {
            List<FileEntry> work = GetConvertibleEntries();
            if (work.Count == 0)
            {
                MessageBox.Show(this, "변환할 PNG 파일이 없습니다. .lowend.png와 .sprite 항목은 변환 대상에서 제외됩니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("변환할 PNG 파일이 없습니다.", StatusKind.Error);
                return;
            }

            DialogResult confirm = MessageBox.Show(this, "PNG " + work.Count + "개만 lowend PNG로 변환합니다. .lowend.png와 .sprite 항목은 건너뜁니다.\r\n" + GetConversionScopeNotice() + "\r\n변환하시겠습니까?", "확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
            {
                SetStatus("변환 취소됨", StatusKind.Normal);
                return;
            }

            string suffixError;
            if (!ValidateCustomOutputSuffix(out suffixError))
            {
                MessageBox.Show(this, suffixError, "출력 문자열 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(suffixError, StatusKind.Error);
                return;
            }

            SaveSettings();

            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            btnConvert.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                for (int i = 0; i < work.Count; i++)
                {
                    FileEntry entry = work[i];
                    string input = entry.FullPath;
                    try
                    {
                        if (!IsTargetPng(input)) continue;

                        using (Bitmap src = LoadSourceBitmap(input))
                        {
                            Size target = CalculateTargetSize(input, src.Width, src.Height);
                            using (Bitmap resized = ResizeTransparent(src, target.Width, target.Height))
                            {
                                string output = GetOutputPath(input);
                                SavePngSafely(resized, output);
                            }
                        }
                        entry.ConversionResult = BuildConversionResultText(true, "PNG → lowend", null);
                        entry.ConversionSucceeded = true;
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        entry.ConversionResult = BuildConversionResultText(false, "PNG → lowend", ex.Message);
                        entry.ConversionSucceeded = false;
                        fail++;
                        errors.Add(Path.GetFileName(input) + " : " + ex.Message);
                    }

                    SetStatus("변환 중: " + (i + 1) + " / " + work.Count, StatusKind.Normal);
                    Application.DoEvents();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                btnConvert.Enabled = true;
            }

            RefreshFileList();
            SetStatus("완료: 성공 " + ok + "개, 실패 " + fail + "개", fail > 0 ? StatusKind.Error : StatusKind.Normal);

            if (fail > 0)
            {
                string message = string.Join(Environment.NewLine, errors.ToArray());
                if (message.Length > 1800) message = message.Substring(0, 1800) + Environment.NewLine + "...";
                MessageBox.Show(this, message, "실패한 파일", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, "변환이 완료되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ConvertSpritesToPng()
        {
            List<FileEntry> work = GetSpriteEntries();
            if (work.Count == 0)
            {
                MessageBox.Show(this, "변환할 .sprite 파일이 없습니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("변환할 .sprite 파일이 없습니다.", StatusKind.Error);
                return;
            }

            DialogResult confirm = MessageBox.Show(this, ".sprite " + work.Count + "개를 PNG로 변환합니다.\r\n" + GetConversionScopeNotice() + "\r\n변환하시겠습니까?", "확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
            {
                SetStatus("sprite → PNG 변환 취소됨", StatusKind.Normal);
                return;
            }

            SaveSettings();

            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            btnSpriteToPng.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                for (int i = 0; i < work.Count; i++)
                {
                    FileEntry entry = work[i];
                    string input = entry.FullPath;
                    try
                    {
                        if (!IsSpriteFile(input)) continue;

                        Bitmap decoded;
                        D2RSpriteInfo info;
                        string error;
                        if (!D2RSpritePreview.TryLoadSpriteBitmap(input, out decoded, out info, out error))
                        {
                            throw new InvalidOperationException(error);
                        }

                        using (decoded)
                        {
                            string output = GetSpriteToPngOutputPath(input);
                            EnsureOutputDirectory(output);
                            SavePngSafely(decoded, output);
                        }
                        entry.ConversionResult = BuildConversionResultText(true, "sprite → PNG", null);
                        entry.ConversionSucceeded = true;
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        entry.ConversionResult = BuildConversionResultText(false, "sprite → PNG", ex.Message);
                        entry.ConversionSucceeded = false;
                        fail++;
                        errors.Add(Path.GetFileName(input) + " : " + ex.Message);
                    }

                    SetStatus("sprite → PNG 변환 중: " + (i + 1) + " / " + work.Count, StatusKind.Normal);
                    Application.DoEvents();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                btnSpriteToPng.Enabled = true;
            }

            RefreshFileList();
            SetStatus("sprite → PNG 완료: 성공 " + ok + "개, 실패 " + fail + "개", fail > 0 ? StatusKind.Error : StatusKind.Normal);

            ShowConversionResult(fail, errors, "sprite → PNG 변환이 완료되었습니다.");
        }

        private void ConvertPngsToSprite()
        {
            List<FileEntry> work = GetPngToSpriteEntries();
            if (work.Count == 0)
            {
                MessageBox.Show(this, "변환할 PNG 파일이 없습니다. .sprite 항목은 변환 대상에서 제외됩니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("변환할 PNG 파일이 없습니다.", StatusKind.Error);
                return;
            }

            int templateCount = CountPngEntriesWithFrameTemplate(work);
            string confirmMessage = BuildPngToSpriteConfirmMessage(work.Count, templateCount);
            DialogResult confirm = MessageBox.Show(this, confirmMessage, "확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
            {
                SetStatus("PNG → sprite 변환 취소됨", StatusKind.Normal);
                return;
            }

            SaveSettings();

            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            btnPngToSprite.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                for (int i = 0; i < work.Count; i++)
                {
                    FileEntry entry = work[i];
                    string input = entry.FullPath;
                    try
                    {
                        if (!IsTargetPng(input) && !IsLowendPngFile(input)) continue;

                        using (Bitmap src = LoadSourceBitmap(input))
                        {
                            string spriteOutput = GetPngToSpriteOutputPath(input);
                            EnsureOutputDirectory(spriteOutput);

                            D2RSpriteInfo templateInfo;
                            string templatePath;
                            if (TryFindLoadedFrameTemplateForPng(input, out templateInfo, out templatePath))
                            {
                                D2RSpriteCodec.SaveRgbaSpriteUsingTemplate(src, templatePath, spriteOutput);
                            }
                            else
                            {
                                D2RSpriteCodec.SaveStaticRgbaSprite(src, spriteOutput);
                            }
                        }
                        entry.ConversionResult = BuildConversionResultText(true, "PNG → sprite", null);
                        entry.ConversionSucceeded = true;
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        entry.ConversionResult = BuildConversionResultText(false, "PNG → sprite", ex.Message);
                        entry.ConversionSucceeded = false;
                        fail++;
                        errors.Add(Path.GetFileName(input) + " : " + ex.Message);
                    }

                    SetStatus("PNG → sprite 변환 중: " + (i + 1) + " / " + work.Count, StatusKind.Normal);
                    Application.DoEvents();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                btnPngToSprite.Enabled = true;
            }

            RefreshFileList();
            SetStatus("PNG → sprite 완료: 성공 " + ok + "개, 실패 " + fail + "개", fail > 0 ? StatusKind.Error : StatusKind.Normal);

            ShowConversionResult(fail, errors, "PNG → sprite 변환이 완료되었습니다.");
        }

        private int CountPngEntriesWithFrameTemplate(List<FileEntry> work)
        {
            int count = 0;
            if (work == null) return 0;
            for (int i = 0; i < work.Count; i++)
            {
                D2RSpriteInfo info;
                if (work[i] != null && TryFindLoadedFrameTemplateForPng(work[i].FullPath, out info)) count++;
            }
            return count;
        }

        private string BuildPngToSpriteConfirmMessage(int pngCount, int templateCount)
        {
            int staticCount = Math.Max(0, pngCount - templateCount);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PNG " + pngCount + "개를 스프라이트로 변환합니다.");
            sb.AppendLine(GetConversionScopeNotice());
            sb.AppendLine();

            if (templateCount <= 0)
            {
                sb.AppendLine("프레임 템플릿이 첨부된 PNG가 없습니다.");
                sb.AppendLine("모든 PNG는 정적 스프라이트로 변환됩니다.");
                sb.AppendLine();
                sb.AppendLine("애니메이션/커서 PNG는 같은 경로에 동일한 이름의 원본 .sprite 템플릿을 함께 첨부하면 헤더 정보를 유지해서 변환할 수 있습니다.");
            }
            else
            {
                sb.AppendLine("프레임 템플릿 첨부 : " + templateCount + "개");
                sb.AppendLine("정적 변환 : " + staticCount + "개");
                sb.AppendLine();
                sb.AppendLine("템플릿이 첨부된 PNG는 같은 경로/동일명의 sprite 헤더를 유지해서 변환합니다.");
                sb.AppendLine("템플릿이 없는 PNG는 정적 스프라이트로 변환합니다.");
            }

            sb.AppendLine();
            sb.AppendLine("변환하시겠습니까?");
            return sb.ToString().TrimEnd();
        }

        private string BuildConversionResultText(bool success, string action, string reason)
        {
            if (success) return T("result.success", "성공") + " (" + action + ")";
            string clean = (reason ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (clean.Length == 0) clean = T("result.unknown_reason", "사유 없음");
            if (clean.Length > 42) clean = clean.Substring(0, 42) + "...";
            return T("result.failure", "실패") + " (" + clean + ")";
        }

        private void ShowConversionResult(int fail, List<string> errors, string successMessage)
        {
            if (fail > 0)
            {
                string message = string.Join(Environment.NewLine, errors.ToArray());
                if (message.Length > 1800) message = message.Substring(0, 1800) + Environment.NewLine + "...";
                MessageBox.Show(this, message, "실패한 파일", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, successMessage, "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string GetSpriteToPngOutputPath(string input)
        {
            string dir = Path.GetDirectoryName(input) ?? string.Empty;
            if (chkOutputPngFolder.Checked) dir = Path.Combine(dir, "output_png");
            string name = Path.GetFileNameWithoutExtension(input) ?? "output";
            return Path.Combine(dir, name + ".png");
        }

        private string GetPngToSpriteOutputPath(string input)
        {
            string dir = Path.GetDirectoryName(input) ?? string.Empty;
            if (chkOutputSpriteFolder.Checked) dir = Path.Combine(dir, "output_sprite");
            string name = Path.GetFileNameWithoutExtension(input) ?? "output";
            return Path.Combine(dir, name + ".sprite");
        }

        private static void EnsureOutputDirectory(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            if (dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private Size CalculateTargetSize(string input, int originalWidth, int originalHeight)
        {
            int width;
            int height;

            if (chkForceSize.Checked)
            {
                width = (int)numForceWidth.Value;
                height = (int)numForceHeight.Value;
            }
            else
            {
                Size autoSize;
                if (autoSizeByLowend && TryGetExistingLowendSize(input, out autoSize))
                {
                    width = autoSize.Width;
                    height = autoSize.Height;
                }
                else
                {
                    width = Math.Max(1, originalWidth / 2);
                    height = Math.Max(1, originalHeight / 2);
                }

            }

            width = Math.Max(1, width);
            height = Math.Max(1, height);
            return new Size(width, height);
        }

        private static bool TryGetExistingLowendSize(string input, out Size size)
        {
            size = Size.Empty;
            string output = GetLowendOutputPath(input);
            if (!File.Exists(output)) return false;

            int width;
            int height;
            int bitDepth;
            if (ImageFileUtil.TryReadImageInfoNoLock(output, out width, out height, out bitDepth))
            {
                size = new Size(Math.Max(1, width), Math.Max(1, height));
                return true;
            }
            return false;
        }

        private static string GetLowendOutputPath(string input)
        {
            return BuildOutputPath(input, ".lowend");
        }

        private string GetOutputPath(string input)
        {
            return BuildOutputPath(input, GetOutputSuffix());
        }

        private string GetOutputSuffix()
        {
            if (chkCustomSuffix.Checked) return (txtCustomSuffix.Text ?? string.Empty).Trim();
            return ".lowend";
        }

        private bool ValidateCustomOutputSuffix(out string error)
        {
            error = null;
            if (!chkCustomSuffix.Checked) return true;

            string suffix = (txtCustomSuffix.Text ?? string.Empty).Trim();
            if (suffix.Length == 0)
            {
                error = "다른 문자열 입력칸이 비어 있습니다.";
                return false;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                if (suffix.IndexOf(invalid[i]) >= 0)
                {
                    error = "다른 문자열에 파일명으로 사용할 수 없는 문자가 포함되어 있습니다.";
                    return false;
                }
            }

            return true;
        }

        private static string BuildOutputPath(string input, string suffix)
        {
            string dir = Path.GetDirectoryName(input) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(input);
            return Path.Combine(dir, name + suffix + ".png");
        }

        private static Bitmap LoadSourceBitmap(string path)
        {
            using (Bitmap raw = ImageFileUtil.LoadBitmapNoLock(path))
            {
                Bitmap src = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(src))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                }
                return src;
            }
        }

        private static Bitmap ResizeTransparent(Bitmap source, int width, int height)
        {
            Bitmap dst = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dst))
            using (ImageAttributes attr = new ImageAttributes())
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY);
                Rectangle dest = new Rectangle(0, 0, width, height);
                g.DrawImage(source, dest, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attr);
            }
            return dst;
        }

        private static void SavePngSafely(Bitmap bitmap, string output)
        {
            string dir = Path.GetDirectoryName(output) ?? string.Empty;
            string temp = Path.Combine(dir, "__lowend_tmp_" + Guid.NewGuid().ToString("N") + ".png");

            try
            {
                using (Bitmap final = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(final))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
                    final.Save(temp, ImageFormat.Png);
                }

                if (File.Exists(output)) File.Delete(output);
                File.Move(temp, output);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private void UpdatePreview()
        {
            SetPlainPreviewInfo(string.Empty);
            previewPanel.SetImage(null);

            if (listFiles.SelectedItems.Count != 1)
            {
                if (listFiles.SelectedItems.Count > 1) SetPlainPreviewInfo(listFiles.SelectedItems.Count + "개 선택됨");
                return;
            }

            FileEntry entry = listFiles.SelectedItems[0].Tag as FileEntry;
            if (entry == null || !File.Exists(entry.FullPath)) return;

            if (IsLowendPngEntry(entry))
            {
                UpdateLowendPngPreview(entry);
                return;
            }

            if (IsSpriteEntry(entry))
            {
                UpdateSpritePreview(entry);
                return;
            }

            string fileTitle = FormatPreviewFileTitle(entry);
            string fileSizeLine = FormatPreviewFileSizeLine(entry);
            try
            {
                using (Bitmap src = LoadSourceBitmap(entry.FullPath))
                {
                    if (chkPreviewConverted.Checked)
                    {
                        Size target = CalculateTargetSize(entry.FullPath, src.Width, src.Height);
                        Bitmap resized = ResizeTransparent(src, target.Width, target.Height);
                        previewPanel.SetImage(resized);
                        double targetDisplayScale = previewPanel.GetDisplayScaleForImageSize(target.Width, target.Height);
                        SetPngPreviewInfo(entry, fileTitle, fileSizeLine, T("label.size", "Dimensions") + " : " + FormatDimension(target.Width, target.Height) + " (" + FormatPercent(targetDisplayScale) + ")", true, PngMetadataInfo.CreateMetadataText(entry.FullPath));
                    }
                    else
                    {
                        previewPanel.SetImage(new Bitmap(src));
                        double currentDisplayScale = previewPanel.GetDisplayScaleForImageSize(src.Width, src.Height);
                        SetPngPreviewInfo(entry, fileTitle, fileSizeLine, T("label.size", "Dimensions") + " : " + FormatDimension(src.Width, src.Height) + " (" + FormatPercent(currentDisplayScale) + ")", true, PngMetadataInfo.CreateMetadataText(entry.FullPath));
                    }
                }
            }
            catch
            {
                SetPngPreviewError(fileTitle, fileSizeLine, T("error.preview_failed", "Thumbnail creation failed"));
            }
        }

        private void UpdateLowendPngPreview(FileEntry entry)
        {
            string fileTitle = FormatPreviewFileTitle(entry);
            string fileSizeLine = FormatPreviewFileSizeLine(entry);
            try
            {
                using (Bitmap src = LoadSourceBitmap(entry.FullPath))
                {
                    previewPanel.SetImage(new Bitmap(src));
                    double displayScale = previewPanel.GetDisplayScaleForImageSize(src.Width, src.Height);
                    SetLowendPngPreviewInfo(entry, fileTitle, fileSizeLine, T("label.size", "Dimensions") + " : " + FormatDimension(src.Width, src.Height) + " (" + FormatPercent(displayScale) + ")", PngMetadataInfo.CreateMetadataText(entry.FullPath));
                }
            }
            catch
            {
                SetLowendPngPreviewInfo(entry, fileTitle, fileSizeLine, T("label.size", "Dimensions") + " : -", PngMetadataInfo.CreateMetadataText(entry.FullPath));
            }
        }

        private void UpdateSpritePreview(FileEntry entry)
        {
            string spriteName = FormatPreviewFileTitle(entry);
            string fileSizeLine = FormatPreviewFileSizeLine(entry);
            Bitmap spriteBitmap;
            D2RSpriteInfo spriteInfo;
            string error;

            if (!D2RSpritePreview.TryLoadSpriteBitmap(entry.FullPath, out spriteBitmap, out spriteInfo, out error))
            {
                string infoText = T("error.read_failed", "Read failed: {0}", error);
                SetSpritePreviewInfo(spriteName, fileSizeLine, spriteInfo, "-", spriteInfo == null ? string.Empty : spriteInfo.ToHeaderText());
                return;
            }

            try
            {
                previewPanel.SetImage(spriteBitmap);
                double displayScale = previewPanel.GetDisplayScaleForImageSize(spriteBitmap.Width, spriteBitmap.Height);
                SetSpritePreviewInfo(spriteName, fileSizeLine, spriteInfo, FormatPercent(displayScale), spriteInfo.ToHeaderText());
            }
            catch
            {
                if (spriteBitmap != null) spriteBitmap.Dispose();
                SetSpritePreviewInfo(spriteName, fileSizeLine, null, "-", string.Empty);
            }
        }

        private void OpenPreviewViewerForSelectedFile()
        {
            if (listFiles.SelectedItems.Count != 1) return;

            FileEntry entry = listFiles.SelectedItems[0].Tag as FileEntry;
            if (entry == null || !File.Exists(entry.FullPath)) return;

            List<string> viewerPaths = GetViewerPathList();
            int initialIndex = FindPathIndex(viewerPaths, entry.FullPath);
            if (initialIndex < 0) return;

            Bitmap testImage;
            string testTitle;
            string testPathLine;
            if (!TryCreateViewerImage(viewerPaths[initialIndex], out testImage, out testTitle, out testPathLine)) return;
            testImage.Dispose();

            if (previewViewer == null || previewViewer.IsDisposed)
            {
                previewViewer = new PreviewViewerForm(viewerPaths, initialIndex, TryCreateViewerImage, SelectFileInListForPreviewViewer, previewNavigationMode, previewZoomStepPercent, previewFixedZoomBasePercent);
                previewViewer.SetDisplaySettings(previewUseCanvasColor, previewCanvasColor, previewUseAlphaColor, previewAlphaColor);
                previewViewer.FormClosed += delegate { previewViewer = null; };
                previewViewer.Show(this);
            }
            else
            {
                previewViewer.SetInputSettings(previewNavigationMode, previewZoomStepPercent, previewFixedZoomBasePercent);
                previewViewer.LoadPaths(viewerPaths, initialIndex, false);
                if (previewViewer.WindowState == FormWindowState.Minimized) previewViewer.WindowState = FormWindowState.Normal;
                previewViewer.Show();
                previewViewer.BringToFront();
                previewViewer.Activate();
            }
        }

        private void ApplyPreviewViewerSettings()
        {
            if (previewViewer == null || previewViewer.IsDisposed) return;
            previewViewer.SetInputSettings(previewNavigationMode, previewZoomStepPercent, previewFixedZoomBasePercent);
            previewViewer.SetDisplaySettings(previewUseCanvasColor, previewCanvasColor, previewUseAlphaColor, previewAlphaColor);
        }


        private void LayoutListFooterControls()
        {
            if (panelList == null || listFiles == null || lblStatus == null || chkIncludeLowendPngInList == null || chkIncludeSpritesInList == null || chkIncludeLowendSpritesInList == null) return;

            // Y좌표는 v316의 안정 좌표를 그대로 쓴다.
            // 각 컨트롤은 Bottom Anchor가 있으므로, 리사이즈 때 WinForms가 같은 하단 간격을 유지한다.
            // 여기서는 X좌표/너비와 상태메시지 폭만 조정해서 하단으로 밀리는 문제를 피한다.
            int maxWidth = Math.Max(MeasureCheckBoxContentWidth(chkIncludeLowendPngInList), Math.Max(MeasureCheckBoxContentWidth(chkIncludeSpritesInList), MeasureCheckBoxContentWidth(chkIncludeLowendSpritesInList)));
            maxWidth = Math.Max(1, maxWidth);
            int left = Math.Max(0, listFiles.Right - maxWidth);

            chkIncludeLowendPngInList.Left = left;
            chkIncludeLowendPngInList.Width = maxWidth;

            chkIncludeSpritesInList.Left = left;
            chkIncludeSpritesInList.Width = maxWidth;

            chkIncludeLowendSpritesInList.Left = left;
            chkIncludeLowendSpritesInList.Width = maxWidth;

            lblStatus.Top = chkIncludeLowendSpritesInList.Top;
            lblStatus.Height = chkIncludeLowendSpritesInList.Height;
            lblStatus.Width = Math.Max(1, left - 8);
            lblStatus.BringToFront();
            chkIncludeLowendPngInList.BringToFront();
            chkIncludeSpritesInList.BringToFront();
            chkIncludeLowendSpritesInList.BringToFront();
        }

        private static int MeasureCheckBoxContentWidth(CheckBox checkBox)
        {
            if (checkBox == null) return 1;
            const int boxSize = 13;
            const int gap = 5;
            string text = checkBox.Text ?? string.Empty;
            Size textSize = text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(text, checkBox.Font, new Size(1000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return boxSize + (text.Length > 0 ? gap + textSize.Width : 0);
        }

        private void LayoutPreviewArea()
        {
            if (groupPreview == null || previewFrame == null || chkPreviewConverted == null || listFiles == null || btnSelectAll == null) return;

            int sideMargin = 12;
            int frameTop = Math.Max(28, listFiles.Top);
            int frameBottom = Math.Max(frameTop + 120, btnSelectAll.Bottom - 1);

            previewFrame.Left = sideMargin;
            previewFrame.Top = frameTop;
            previewFrame.Width = Math.Max(1, groupPreview.ClientSize.Width - sideMargin * 2);
            previewFrame.Height = Math.Max(1, frameBottom - frameTop);

            int infoLeft = previewFrame.Left + 1;
            int infoWidth = Math.Max(1, previewFrame.Width - 2);
            chkPreviewConverted.Width = infoWidth;
            chkPreviewConverted.Left = infoLeft;
            chkPreviewConverted.Top = Math.Max(frameBottom + 8, groupPreview.ClientSize.Height - (576 - 554));

            LayoutPreviewFrame();
        }

        private void ApplyFixedNoticePanels()
        {
            int spriteNoticeLeft = btnPngToSprite == null ? 558 : btnPngToSprite.Right + 12;
            int sharedNoticeWidth = Math.Max(1, ClientSize.Width - spriteNoticeLeft - 16);

            if (bottomNoticePanel != null)
            {
                bottomNoticePanel.Left = spriteNoticeLeft;
                bottomNoticePanel.Top = btnConvert == null ? 846 : btnConvert.Top;
                bottomNoticePanel.Width = sharedNoticeWidth;
                bottomNoticePanel.Height = 62;
                if (lblBottomNotice1 != null) lblBottomNotice1.Width = Math.Max(1, bottomNoticePanel.ClientSize.Width - lblBottomNotice1.Left - 12);
                if (lblBottomNotice2 != null) lblBottomNotice2.Width = Math.Max(1, bottomNoticePanel.ClientSize.Width - lblBottomNotice2.Left - 12);
            }

            if (spriteNoticePanel != null)
            {
                spriteNoticePanel.Left = spriteNoticeLeft;
                spriteNoticePanel.Top = btnPngToSprite == null ? 918 : btnPngToSprite.Top;
                spriteNoticePanel.Width = sharedNoticeWidth;
                spriteNoticePanel.Height = 62;
                if (lblSpriteNotice1 != null) lblSpriteNotice1.Width = Math.Max(1, spriteNoticePanel.ClientSize.Width - lblSpriteNotice1.Left - 12);
                if (lblSpriteNotice2 != null) lblSpriteNotice2.Width = Math.Max(1, spriteNoticePanel.ClientSize.Width - lblSpriteNotice2.Left - 12);
            }
        }

        private void LayoutPreviewFrame()
        {
            if (previewFrame == null || previewPanel == null || lblPreviewInfo == null) return;

            int border = 1;
            int innerWidth = Math.Max(1, previewFrame.ClientSize.Width - (border * 2));
            int baseImageHeight = listFiles == null ? 414 : Math.Max(1, listFiles.Height - 2);
            int imageHeight = Math.Max(1, (baseImageHeight * 80) / 100);
            int minInfoHeight = 220;
            int maxImageHeight = Math.Max(1, previewFrame.ClientSize.Height - (border * 2) - minInfoHeight);
            if (imageHeight > maxImageHeight) imageHeight = maxImageHeight;
            int dividerY = border + imageHeight;

            previewPanel.Left = border;
            previewPanel.Top = border;
            previewPanel.Width = innerWidth;
            previewPanel.Height = imageHeight;

            lblPreviewInfo.Left = border;
            lblPreviewInfo.Top = dividerY + 1;
            lblPreviewInfo.Width = innerWidth;
            lblPreviewInfo.Height = Math.Max(1, previewFrame.ClientSize.Height - border - lblPreviewInfo.Top);

            previewFrame.DividerY = dividerY;
        }

        private void ApplyPreviewDisplaySettings()
        {
            if (previewPanel != null) previewPanel.SetDisplaySettings(previewUseCanvasColor, previewCanvasColor, previewUseAlphaColor, previewAlphaColor);
            if (previewViewer != null && !previewViewer.IsDisposed) previewViewer.SetDisplaySettings(previewUseCanvasColor, previewCanvasColor, previewUseAlphaColor, previewAlphaColor);
        }

        private List<string> GetViewerPathList()
        {
            List<string> paths = new List<string>();
            for (int i = 0; i < listFiles.Items.Count; i++)
            {
                FileEntry itemEntry = listFiles.Items[i].Tag as FileEntry;
                if (itemEntry == null) continue;
                if (!File.Exists(itemEntry.FullPath)) continue;
                paths.Add(itemEntry.FullPath);
            }
            return paths;
        }

        private static int FindPathIndex(List<string> paths, string fullPath)
        {
            if (paths == null || string.IsNullOrEmpty(fullPath)) return -1;
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], fullPath, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private void SelectFileInListForPreviewViewer(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return;

            listFiles.BeginUpdate();
            try
            {
                for (int i = 0; i < listFiles.Items.Count; i++)
                {
                    FileEntry itemEntry = listFiles.Items[i].Tag as FileEntry;
                    bool matched = itemEntry != null && string.Equals(itemEntry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase);
                    listFiles.Items[i].Selected = matched;
                    listFiles.Items[i].Focused = matched;
                    if (matched) listFiles.Items[i].EnsureVisible();
                }
            }
            finally
            {
                listFiles.EndUpdate();
            }
        }

        private bool TryCreateViewerImage(string fullPath, out Bitmap viewerImage, out string title, out string pathLine)
        {
            viewerImage = null;
            title = Path.GetFileName(fullPath);
            pathLine = fullPath;

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return false;

            if (IsSpriteFile(fullPath))
            {
                D2RSpriteInfo spriteInfo;
                string error;
                if (!D2RSpritePreview.TryLoadSpriteBitmap(fullPath, out viewerImage, out spriteInfo, out error))
                {
                    if (viewerImage != null)
                    {
                        viewerImage.Dispose();
                        viewerImage = null;
                    }
                    return false;
                }
                return true;
            }

            if (!IsTargetPng(fullPath) && !IsLowendPngFile(fullPath)) return false;

            try
            {
                using (Bitmap src = LoadSourceBitmap(fullPath))
                {
                    if (chkPreviewConverted.Checked && IsTargetPng(fullPath))
                    {
                        Size target = CalculateTargetSize(fullPath, src.Width, src.Height);
                        viewerImage = ResizeTransparent(src, target.Width, target.Height);
                    }
                    else
                    {
                        viewerImage = new Bitmap(src);
                    }
                }
                return true;
            }
            catch
            {
                if (viewerImage != null)
                {
                    viewerImage.Dispose();
                    viewerImage = null;
                }
                return false;
            }
        }

        private void SetPlainPreviewInfo(string text)
        {
            currentHeaderInfoText = string.Empty;
            lblPreviewInfo.SetPlainText(text ?? string.Empty, SystemColors.ControlText);
        }

        private void SetPreviewInfoLines(string fileTitle, string fileSizeLine, Color fileColor, IEnumerable<PreviewInfoLine> infoLines)
        {
            List<PreviewInfoLine> lines = new List<PreviewInfoLine>();
            lines.Add(new PreviewInfoLine(fileTitle, fileColor, true));
            if (!string.IsNullOrEmpty(currentHeaderInfoText))
            {
                lines.Add(PreviewInfoLine.Link(T("label.metadata_view_text", "Metadata view") + " [🔍]"));
                lines.Add(new PreviewInfoLine(string.Empty, SystemColors.ControlText, false));
            }
            if (!string.IsNullOrEmpty(fileSizeLine)) lines.Add(new PreviewInfoLine(fileSizeLine, SystemColors.ControlText, false));
            if (infoLines != null)
            {
                foreach (PreviewInfoLine line in infoLines) lines.Add(line);
            }
            lblPreviewInfo.SetColoredLines(lines);
        }

        private void AddPngTemplateModeInfo(List<PreviewInfoLine> infoLines, FileEntry entry)
        {
            if (infoLines == null) return;
            D2RSpriteInfo info;
            if (entry != null && TryFindLoadedFrameTemplateForPng(entry.FullPath, out info))
            {
                infoLines.Add(new PreviewInfoLine(T("status.frame_template_attached", "(*) Frame template attached"), Color.ForestGreen, false));
                infoLines.Add(new PreviewInfoLine(T("status.convert_mode_label", "Convert mode :"), Color.Firebrick, false));
                infoLines.Add(new PreviewInfoLine(T("status.convert_mode_frame_value", "Frame sprite (keep header)"), Color.Firebrick, false));
            }
            else
            {
                infoLines.Add(new PreviewInfoLine(T("status.frame_template_none", "No frame template information"), ReferenceItemColor, false));
                infoLines.Add(new PreviewInfoLine(T("status.convert_mode_label", "Convert mode :"), ReferenceItemColor, false));
                infoLines.Add(new PreviewInfoLine(T("status.convert_mode_static_value", "Static sprite"), ReferenceItemColor, false));
            }
        }

        private void SetPngPreviewInfo(FileEntry entry, string fileTitle, string fileSizeLine, string sizeLine, bool canResize, string metadataText)
        {
            currentHeaderInfoText = metadataText ?? string.Empty;

            List<PreviewInfoLine> infoLines = new List<PreviewInfoLine>();
            infoLines.Add(new PreviewInfoLine(sizeLine, SystemColors.ControlText, false));
            infoLines.Add(new PreviewInfoLine(canResize ? T("status.resizable", "Resizable") : T("status.not_resizable", "Not resizable"), canResize ? Color.ForestGreen : Color.Firebrick, false));
            infoLines.Add(new PreviewInfoLine(string.Empty, SystemColors.ControlText, false));
            AddPngTemplateModeInfo(infoLines, entry);

            Color fileColor = IsLowendPngEntry(entry) ? LowendPreviewInfoColor : Color.ForestGreen;
            SetPreviewInfoLines(fileTitle, fileSizeLine, fileColor, infoLines);
        }

        private void SetPngPreviewError(string fileTitle, string fileSizeLine, string errorText)
        {
            SetPreviewInfoLines(fileTitle, fileSizeLine, Color.ForestGreen, new PreviewInfoLine[]
            {
                new PreviewInfoLine(errorText, Color.Firebrick, false)
            });
        }

        private void SetSpritePreviewInfo(string spriteName, string fileSizeLine, D2RSpriteInfo spriteInfo, string displayScaleText, string headerInfoText)
        {
            currentHeaderInfoText = headerInfoText ?? string.Empty;

            bool isFrameSprite = spriteInfo != null && Math.Max(1, spriteInfo.FrameCount) >= 2;
            Color fileColor = isFrameSprite ? Color.Firebrick : Color.RoyalBlue;
            List<PreviewInfoLine> infoLines = new List<PreviewInfoLine>();

            if (spriteInfo == null)
            {
                infoLines.Add(new PreviewInfoLine(T("error.read_failed", "Read failed: {0}", T("error.preview_failed", "Thumbnail creation failed")), Color.Firebrick, false));
            }
            else
            {
                infoLines.Add(new PreviewInfoLine(T("label.size", "Dimensions") + " : " + FormatDimension(spriteInfo.Width, spriteInfo.Height) + " (" + displayScaleText + ")", SystemColors.ControlText, false));
                infoLines.Add(new PreviewInfoLine(T("label.frame", "Frame") + " : " + spriteInfo.FrameWidth + "px * " + Math.Max(1, spriteInfo.FrameCount), SystemColors.ControlText, false));
                infoLines.Add(new PreviewInfoLine(T("label.magic", "Magic number") + " : " + SafeText(spriteInfo.Magic) + " (v" + spriteInfo.Version + ")", SystemColors.ControlText, false));
                infoLines.Add(new PreviewInfoLine(T("label.color_format", "Color format") + " : " + SafeText(spriteInfo.EncodingName), SystemColors.ControlText, false));
            }

            SetPreviewInfoLines(spriteName, fileSizeLine, fileColor, infoLines);
        }

        private void ShowCurrentMetadataInfoDialog()
        {
            if (string.IsNullOrEmpty(currentHeaderInfoText)) return;
            using (MetadataInfoDialog dialog = new MetadataInfoDialog(currentHeaderInfoText))
            {
                dialog.ShowDialog(this);
            }
        }

        private void SetLowendPngPreviewInfo(FileEntry entry, string fileName, string fileSizeLine, string infoLine, string metadataText)
        {
            currentHeaderInfoText = metadataText ?? string.Empty;

            List<PreviewInfoLine> infoLines = new List<PreviewInfoLine>();
            infoLines.Add(new PreviewInfoLine(infoLine, SystemColors.ControlText, false));
            infoLines.Add(new PreviewInfoLine(T("status.not_resizable", "Not resizable"), Color.Firebrick, false));
            infoLines.Add(new PreviewInfoLine(string.Empty, SystemColors.ControlText, false));
            AddPngTemplateModeInfo(infoLines, entry);

            SetPreviewInfoLines(fileName, fileSizeLine, LowendPreviewInfoColor, infoLines);
        }

        private static string FormatPreviewFileTitle(FileEntry entry)
        {
            if (entry == null) return string.Empty;
            return Path.GetFileName(entry.FullPath);
        }

        private static string FormatPreviewFileSizeLine(FileEntry entry)
        {
            if (entry == null) return string.Empty;
            return Localization.T("label.file_size", "File size") + " : " + FormatFileSize(entry.FileSize);
        }

        private static string FormatDimension(int width, int height)
        {
            return width + " * " + height + " px";
        }

        private static string SafeText(string text)
        {
            return string.IsNullOrEmpty(text) ? "?" : text;
        }

        private static string FormatPercent(double ratio)
        {
            if (ratio <= 0) return "-";
            return (ratio * 100.0).ToString("0.#") + "%";
        }

        private static int GetLabelPrefixLength(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            string[] labels = new string[]
            {
                Localization.T("label.file_size", "File size"),
                Localization.T("label.magic", "Magic number"),
                Localization.T("label.color_format", "Color format"),
                Localization.T("label.size", "Dimensions"),
                Localization.T("label.frame", "Frame"),
                Localization.T("label.payload", "Payload"),
                Localization.T("label.bpp", "BPP"),
                "magic", "Payload", "BPP", "크기", "프레임", "페이로드", "용량", "파일용량", "매직넘버", "색상형식"
            };
            for (int i = 0; i < labels.Length; i++)
            {
                string label = labels[i];
                if (string.IsNullOrEmpty(label)) continue;
                if (line.StartsWith(label + " :", StringComparison.CurrentCultureIgnoreCase)) return label.Length;
                if (line.StartsWith(label + " ", StringComparison.CurrentCultureIgnoreCase)) return label.Length;
            }
            return 0;
        }

        private void InitializeColumnSpecs()
        {
            columnSpecs.Add(new ColumnSpec("Order", T("column.order", "#"), 36, true, 0));
            columnSpecs.Add(new ColumnSpec("FileName", T("column.filename", "File name"), 190, true, 1));
            columnSpecs.Add(new ColumnSpec("NewName", T("column.newname", "New file name"), 190, false, 2));
            columnSpecs.Add(new ColumnSpec("Folder", T("column.folder", "Path"), 312, true, 3));
            columnSpecs.Add(new ColumnSpec("Type", T("column.type", "Type"), 140, true, 4));
            columnSpecs.Add(new ColumnSpec("ConversionResult", T("column.conversion_result", "Result"), 150, false, 5));
            columnSpecs.Add(new ColumnSpec("Created", T("column.created", "Created"), 145, false, 6));
            columnSpecs.Add(new ColumnSpec("Modified", T("column.modified", "Modified"), 145, false, 7));
            columnSpecs.Add(new ColumnSpec("Accessed", T("column.accessed", "Accessed"), 145, false, 8));
            columnSpecs.Add(new ColumnSpec("ImageSize", T("column.image_size", "Image size"), 100, false, 9));
            columnSpecs.Add(new ColumnSpec("Width", T("column.width", "Width"), 90, false, 10));
            columnSpecs.Add(new ColumnSpec("Height", T("column.height", "Height"), 90, false, 11));
            columnSpecs.Add(new ColumnSpec("BitDepth", T("column.bit_depth", "Bit depth"), 90, false, 12));
            columnSpecs.Add(new ColumnSpec("FileSize", T("column.file_size", "File size"), 110, false, 13));
        }

        private void BuildColumns(bool preserveSelection)
        {
            if (listFiles == null) return;

            buildingColumns = true;
            try
            {
                UpdateDynamicColumnVisibility();
                List<ColumnSpec> visible = GetVisibleColumnSpecs();
                int dynamicWidthForFolder = GetVisibleDynamicColumnWidthForFolder();

                listFiles.BeginUpdate();
                listFiles.Columns.Clear();
                for (int i = 0; i < visible.Count; i++)
                {
                    ColumnSpec spec = visible[i];
                    ColumnHeader header = new ColumnHeader();
                    header.Text = spec.Title;
                    header.Width = spec.Id == "Folder" ? Math.Max(80, spec.Width - dynamicWidthForFolder) : spec.Width;
                    header.Tag = spec;
                    listFiles.Columns.Add(header);
                }
                RebuildListView(preserveSelection);
                listFiles.EndUpdate();
            }
            finally
            {
                buildingColumns = false;
            }
        }

        private List<ColumnSpec> GetVisibleColumnSpecs()
        {
            List<ColumnSpec> list = new List<ColumnSpec>();
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                ColumnSpec spec = columnSpecs[i];
                if (spec.Visible) list.Add(spec);
            }
            list.Sort(delegate(ColumnSpec a, ColumnSpec b)
            {
                int c = a.DisplayOrder.CompareTo(b.DisplayOrder);
                if (c != 0) return c;
                return a.DefaultOrder.CompareTo(b.DefaultOrder);
            });
            if (list.Count == 0)
            {
                ColumnSpec order = GetColumnSpec("Order");
                order.Visible = true;
                list.Add(order);
            }
            return list;
        }

        private void UpdateDynamicColumnVisibility()
        {
            ColumnSpec newName = GetColumnSpec("NewName");
            if (newName != null) newName.Visible = HasPendingRenames();

            ColumnSpec result = GetColumnSpec("ConversionResult");
            if (result != null) result.Visible = HasConversionResults();
        }

        private int GetVisibleDynamicColumnWidthForFolder()
        {
            int width = 0;
            ColumnSpec newName = GetColumnSpec("NewName");
            if (newName != null && newName.Visible) width += newName.Width;
            ColumnSpec result = GetColumnSpec("ConversionResult");
            if (result != null && result.Visible) width += result.Width;
            return width;
        }

        private bool HasPendingRenames()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].PendingFileName)) return true;
            }
            return false;
        }

        private bool HasConversionResults()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].ConversionResult)) return true;
            }
            return false;
        }

        private ColumnSpec GetColumnSpec(string id)
        {
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                if (columnSpecs[i].Id == id) return columnSpecs[i];
            }
            return columnSpecs[0];
        }

        private void RebuildListView(bool preserveSelection)
        {
            if (listFiles == null) return;

            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (preserveSelection)
            {
                foreach (ListViewItem item in listFiles.SelectedItems)
                {
                    FileEntry entry = item.Tag as FileEntry;
                    if (entry != null) selected.Add(entry.FullPath);
                }
            }

            List<ColumnSpec> visible = GetVisibleColumnSpecs();
            List<FileEntry> sorted = GetSortedEntries();

            listFiles.BeginUpdate();
            listFiles.Items.Clear();
            for (int i = 0; i < sorted.Count; i++)
            {
                FileEntry entry = sorted[i];
                ListViewItem item = null;
                for (int c = 0; c < visible.Count; c++)
                {
                    string text = GetColumnText(entry, visible[c].Id);
                    if (c == 0)
                    {
                        item = new ListViewItem(text);
                        item.Tag = entry;
                    }
                    else
                    {
                        item.SubItems.Add(text);
                    }
                }
                if (item != null)
                {
                    Color? rowColor = null;
                    if (HasLoadedFrameTemplateForPng(entry)) rowColor = Color.ForestGreen;
                    else if (IsFrameSpriteEntry(entry)) rowColor = Color.Firebrick;
                    else if (IsSpriteEntry(entry)) rowColor = Color.RoyalBlue;
                    else if (IsLowendPngEntry(entry)) rowColor = LowendPreviewInfoColor;

                    if (rowColor.HasValue) ApplyListViewItemForeColor(item, rowColor.Value);
                    ApplyConversionResultSubItemStyle(item, visible, entry);

                    if (selected.Contains(entry.FullPath)) item.Selected = true;
                    listFiles.Items.Add(item);
                }
            }
            listFiles.EndUpdate();
        }

        private static void ApplyListViewItemForeColor(ListViewItem item, Color color)
        {
            if (item == null) return;
            item.UseItemStyleForSubItems = false;
            item.ForeColor = color;
            for (int i = 0; i < item.SubItems.Count; i++) item.SubItems[i].ForeColor = color;
        }

        private static void ApplyConversionResultSubItemStyle(ListViewItem item, List<ColumnSpec> visible, FileEntry entry)
        {
            if (item == null || visible == null || entry == null || string.IsNullOrEmpty(entry.ConversionResult)) return;
            for (int i = 0; i < visible.Count && i < item.SubItems.Count; i++)
            {
                if (visible[i].Id != "ConversionResult") continue;
                item.UseItemStyleForSubItems = false;
                item.SubItems[i].ForeColor = Color.Black;
                item.SubItems[i].BackColor = entry.ConversionSucceeded ? Color.Khaki : Color.LightCoral;
                return;
            }
        }

        private List<FileEntry> GetSortedEntries()
        {
            List<FileEntry> sorted = new List<FileEntry>(entries);
            sorted.Sort(CompareEntries);
            return sorted;
        }

        private List<FileEntry> GetVisualOrderedEntries()
        {
            return GetSortedEntries();
        }

        private List<FileEntry> GetConvertibleEntries()
        {
            return ApplyConversionScope(GetTargetPngEntries());
        }

        private List<FileEntry> GetTargetPngEntries()
        {
            List<FileEntry> list = new List<FileEntry>();
            List<FileEntry> sorted = GetVisualOrderedEntries();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (IsTargetPng(sorted[i].FullPath)) list.Add(sorted[i]);
            }
            return list;
        }

        private List<FileEntry> GetPngToSpriteEntries()
        {
            List<FileEntry> list = new List<FileEntry>();
            List<FileEntry> sorted = GetVisualOrderedEntries();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (IsTargetPng(sorted[i].FullPath) || IsLowendPngFile(sorted[i].FullPath)) list.Add(sorted[i]);
            }
            return ApplyConversionScope(list);
        }

        private List<FileEntry> GetSpriteEntries()
        {
            List<FileEntry> list = new List<FileEntry>();
            List<FileEntry> sorted = GetVisualOrderedEntries();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (IsSpriteFile(sorted[i].FullPath)) list.Add(sorted[i]);
            }
            return ApplyConversionScope(list);
        }

        private List<FileEntry> ApplyConversionScope(List<FileEntry> candidates)
        {
            if (!convertSelectedOnly || listFiles == null || listFiles.SelectedItems.Count == 0) return candidates;

            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem item in listFiles.SelectedItems)
            {
                FileEntry entry = item.Tag as FileEntry;
                if (entry != null) selected.Add(entry.FullPath);
            }

            List<FileEntry> scoped = new List<FileEntry>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (selected.Contains(candidates[i].FullPath)) scoped.Add(candidates[i]);
            }
            return scoped;
        }

        private string GetConversionScopeNotice()
        {
            if (convertSelectedOnly && listFiles != null && listFiles.SelectedItems.Count > 0) return "선택된 파일 중 변환 가능한 항목만 처리합니다.";
            return "목록의 모든 변환 가능 항목을 처리합니다.";
        }

        private int CompareEntries(FileEntry a, FileEntry b)
        {
            int result = 0;
            string id = sortColumnId;

            if (id == "Order") result = a.Order.CompareTo(b.Order);
            else if (id == "FileName") result = StringComparer.CurrentCultureIgnoreCase.Compare(a.FileName, b.FileName);
            else if (id == "NewName") result = StringComparer.CurrentCultureIgnoreCase.Compare(a.PendingFileName ?? string.Empty, b.PendingFileName ?? string.Empty);
            else if (id == "Folder") result = StringComparer.CurrentCultureIgnoreCase.Compare(a.Folder, b.Folder);
            else if (id == "Type") result = StringComparer.CurrentCultureIgnoreCase.Compare(GetEntryTypeText(a), GetEntryTypeText(b));
            else if (id == "ConversionResult") result = StringComparer.CurrentCultureIgnoreCase.Compare(a.ConversionResult ?? string.Empty, b.ConversionResult ?? string.Empty);
            else if (id == "Created") result = a.CreatedTime.CompareTo(b.CreatedTime);
            else if (id == "Modified") result = a.ModifiedTime.CompareTo(b.ModifiedTime);
            else if (id == "Accessed") result = a.AccessedTime.CompareTo(b.AccessedTime);
            else if (id == "ImageSize") result = ((long)a.ImageWidth * (long)a.ImageHeight).CompareTo((long)b.ImageWidth * (long)b.ImageHeight);
            else if (id == "Width") result = a.ImageWidth.CompareTo(b.ImageWidth);
            else if (id == "Height") result = a.ImageHeight.CompareTo(b.ImageHeight);
            else if (id == "BitDepth") result = a.BitDepth.CompareTo(b.BitDepth);
            else if (id == "FileSize") result = a.FileSize.CompareTo(b.FileSize);

            if (result == 0) result = a.Order.CompareTo(b.Order);
            return sortAscending ? result : -result;
        }

        private string GetColumnText(FileEntry entry, string id)
        {
            if (id == "Order") return entry.Order.ToString();
            if (id == "FileName") return entry.FileName;
            if (id == "NewName") return entry.PendingFileName ?? string.Empty;
            if (id == "Folder") return entry.Folder;
            if (id == "Type") return GetEntryTypeText(entry);
            if (id == "ConversionResult") return entry.ConversionResult ?? string.Empty;
            if (id == "Created") return FormatTime(entry.CreatedTime);
            if (id == "Modified") return FormatTime(entry.ModifiedTime);
            if (id == "Accessed") return FormatTime(entry.AccessedTime);
            if (id == "ImageSize") return entry.ImageWidth > 0 && entry.ImageHeight > 0 ? entry.ImageWidth + " * " + entry.ImageHeight : string.Empty;
            if (id == "Width") return entry.ImageWidth > 0 ? entry.ImageWidth.ToString() : string.Empty;
            if (id == "Height") return entry.ImageHeight > 0 ? entry.ImageHeight.ToString() : string.Empty;
            if (id == "BitDepth") return entry.BitDepth > 0 ? entry.BitDepth.ToString() : string.Empty;
            if (id == "FileSize") return FormatFileSize(entry.FileSize);
            return string.Empty;
        }

        private string GetEntryTypeText(FileEntry entry)
        {
            if (entry == null) return string.Empty;
            string path = entry.FullPath;
            D2RSpriteInfo info;
            if (IsLowendPngFile(path))
            {
                return T("filetype.lowend_png", "lowend PNG") + (TryFindLoadedFrameTemplateForPng(path, out info) ? "*" : string.Empty);
            }
            if (IsLowendSpriteFile(path))
            {
                return T("filetype.lowend_sprite", "lowend Sprite") + (TryReadLoadedSpriteInfo(path, out info) && Math.Max(1, info.FrameCount) >= 2 ? "*" : string.Empty);
            }
            if (IsSpriteFile(path))
            {
                return T("filetype.sprite", "Sprite") + (TryReadLoadedSpriteInfo(path, out info) && Math.Max(1, info.FrameCount) >= 2 ? "*" : string.Empty);
            }
            if (IsTargetPng(path))
            {
                return T("filetype.png", "PNG") + (TryFindLoadedFrameTemplateForPng(path, out info) ? "*" : string.Empty);
            }
            return string.Empty;
        }

        private static string FormatTime(DateTime value)
        {
            if (value == DateTime.MinValue) return string.Empty;
            return value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.##") + " KB";
            double mb = kb / 1024.0;
            return mb.ToString("0.##") + " MB";
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column < 0 || e.Column >= listFiles.Columns.Count) return;
            ColumnSpec spec = listFiles.Columns[e.Column].Tag as ColumnSpec;
            if (spec == null) return;

            if (sortColumnId == spec.Id) sortAscending = !sortAscending;
            else
            {
                sortColumnId = spec.Id;
                sortAscending = true;
            }

            SaveSettings();
            RebuildListView(true);
        }

        private void OnListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            ListViewHitTestInfo hit = listFiles.HitTest(e.Location);
            if (IsHeaderArea(e.Y) && hit.Item == null)
            {
                ShowColumnMenu(e.Location);
                return;
            }

            if (hit.Item == null) return;

            if (!hit.Item.Selected)
            {
                listFiles.SelectedItems.Clear();
                hit.Item.Selected = true;
            }

            if (listFiles.SelectedItems.Count == 1) ShowSingleFileMenu(e.Location);
            else if (listFiles.SelectedItems.Count > 1) ShowMultiFileMenu(e.Location);
        }

        private void OnListMouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewHitTestInfo hit = listFiles.HitTest(e.Location);
            int columnIndex = GetColumnIndexAtX(e.X);

            if (hit.Item != null)
            {
                listFiles.SelectedItems.Clear();
                hit.Item.Selected = true;
                hit.Item.Focused = true;
                hit.Item.EnsureVisible();
                OpenPreviewViewerForSelectedFile();
                return;
            }

            if (!IsHeaderArea(e.Y)) return;
            if (columnIndex < 0 || columnIndex >= listFiles.Columns.Count) return;

            listFiles.AutoResizeColumn(columnIndex, ColumnHeaderAutoResizeStyle.ColumnContent);
            int contentWidth = listFiles.Columns[columnIndex].Width;
            listFiles.AutoResizeColumn(columnIndex, ColumnHeaderAutoResizeStyle.HeaderSize);
            int headerWidth = listFiles.Columns[columnIndex].Width;
            listFiles.Columns[columnIndex].Width = Math.Max(contentWidth, headerWidth);
            UpdateColumnSettingsFromListView();
            SaveSettings();
        }

        private bool IsHeaderArea(int y)
        {
            return y >= 0 && y <= 28;
        }

        private int GetColumnIndexAtX(int x)
        {
            int left = 0;
            for (int i = 0; i < listFiles.Columns.Count; i++)
            {
                int width = listFiles.Columns[i].Width;
                if (x >= left && x <= left + width) return i;
                left += width;
            }
            return -1;
        }

        private void ShowColumnMenu(Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                ColumnSpec spec = columnSpecs[i];
                ToolStripMenuItem item = new ToolStripMenuItem(spec.Title);
                item.Checked = spec.Visible;
                item.Tag = spec;
                if (spec.Id == "NewName" || spec.Id == "ConversionResult") item.Enabled = false;
                item.Click += delegate(object sender, EventArgs e)
                {
                    ToolStripMenuItem clicked = sender as ToolStripMenuItem;
                    ColumnSpec target = clicked == null ? null : clicked.Tag as ColumnSpec;
                    if (target == null) return;
                    if (target.Visible && CountVisibleUserColumns() <= 1)
                    {
                        MessageBox.Show(this, "최소 1개 이상의 분류탭은 표시되어야 합니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    target.Visible = !target.Visible;
                    BuildColumns(true);
                    SaveSettings();
                };
                menu.Items.Add(item);
            }
            menu.Show(listFiles, location);
        }

        private int CountVisibleUserColumns()
        {
            int count = 0;
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                if (columnSpecs[i].Visible && columnSpecs[i].Id != "NewName" && columnSpecs[i].Id != "ConversionResult") count++;
            }
            return count;
        }

        private void ShowSingleFileMenu(Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem rename = new ToolStripMenuItem("파일명 변경");
            ToolStripMenuItem openFolder = new ToolStripMenuItem("경로 열기");
            ToolStripMenuItem remove = new ToolStripMenuItem("목록에서 제거");
            ToolStripMenuItem properties = new ToolStripMenuItem("파일 속성");

            rename.Click += delegate { RenameSelectedSingle(); };
            openFolder.Click += delegate { OpenSelectedFileLocation(); };
            remove.Click += delegate { RemoveSelectedEntries(); };
            properties.Click += delegate { ShowSelectedFileProperties(); };

            menu.Items.Add(rename);
            menu.Items.Add(openFolder);
            menu.Items.Add(remove);
            menu.Items.Add(properties);
            menu.Show(listFiles, location);
        }

        private void ShowMultiFileMenu(Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem prefix = new ToolStripMenuItem("파일명 앞에 추가");
            ToolStripMenuItem suffix = new ToolStripMenuItem("파일명 뒤에 추가");
            ToolStripMenuItem remove = new ToolStripMenuItem("목록에서 제거");

            prefix.Click += delegate { ApplyPrefixToSelected(); };
            suffix.Click += delegate { ApplySuffixToSelected(); };
            remove.Click += delegate { RemoveSelectedEntries(); };

            menu.Items.Add(prefix);
            menu.Items.Add(suffix);
            menu.Items.Add(remove);
            menu.Show(listFiles, location);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllEntries();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2 && listFiles.SelectedItems.Count == 1)
            {
                RenameSelectedSingle();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && listFiles.SelectedItems.Count > 0)
            {
                RemoveSelectedEntries();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5)
            {
                RefreshFileList();
                e.Handled = true;
            }
        }

        private List<FileEntry> GetSelectedEntries()
        {
            List<FileEntry> list = new List<FileEntry>();
            foreach (ListViewItem item in listFiles.SelectedItems)
            {
                FileEntry entry = item.Tag as FileEntry;
                if (entry != null) list.Add(entry);
            }
            return list;
        }

        private List<FileEntry> GetListedEntries()
        {
            List<FileEntry> list = new List<FileEntry>();
            foreach (ListViewItem item in listFiles.Items)
            {
                FileEntry entry = item.Tag as FileEntry;
                if (entry != null) list.Add(entry);
            }
            return list;
        }

        private void SelectAllEntries()
        {
            listFiles.BeginUpdate();
            foreach (ListViewItem item in listFiles.Items) item.Selected = true;
            listFiles.EndUpdate();
            SetStatus(listFiles.Items.Count + "개 파일을 선택했습니다.", StatusKind.Normal);
        }

        private void SelectNoneEntries()
        {
            listFiles.SelectedItems.Clear();
            SetStatus("선택을 해제했습니다.", StatusKind.Normal);
        }

        private void RenameSelectedSingle()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count != 1)
            {
                SetStatus("파일명 변경은 파일 1개를 선택했을 때 사용할 수 있습니다.", StatusKind.Error);
                return;
            }

            FileEntry entry = selected[0];
            string currentFileName = entry.PendingFileName ?? entry.FileName;
            string currentBase = GetEditableBaseName(currentFileName);
            using (StringInputDialog dialog = new StringInputDialog("파일명 변경", "새 파일명을 입력하세요. 확장자는 제외됩니다.", currentBase))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string baseName = NormalizeBaseName(dialog.InputText);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    MessageBox.Show(this, "파일명을 입력하세요.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                entry.PendingFileName = BuildFileNameWithSameSuffix(currentFileName, baseName);
            }

            BuildColumns(true);
            SetStatus("파일명 변경값을 임시 적용했습니다. 최종 반영은 [파일명 변경실행]을 누르세요.", StatusKind.Normal);
        }

        private void ApplyPrefixToSelected()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count == 0 && listFiles.Items.Count == 0)
            {
                SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                return;
            }

            using (StringInputDialog dialog = new StringInputDialog("파일명 앞에 추가", "파일명 앞에 추가할 문자열을 입력하세요.", renamePrefixText, true, new Action(SelectAllEntries)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                renamePrefixText = dialog.InputText ?? string.Empty;
                SaveSettings();
                if (dialog.SelectAllRequested) selected = GetListedEntries();
                if (selected.Count == 0)
                {
                    SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                    return;
                }

                string text = renamePrefixText;
                if (text.Length == 0) return;
                for (int i = 0; i < selected.Count; i++)
                {
                    FileEntry entry = selected[i];
                    string fileName = entry.PendingFileName ?? entry.FileName;
                    string baseName = GetEditableBaseName(fileName);
                    entry.PendingFileName = BuildFileNameWithSameSuffix(fileName, text + baseName);
                }
            }

            BuildColumns(true);
            SetStatus(selected.Count + "개 파일명에 앞 문자열을 임시 적용했습니다.", StatusKind.Normal);
        }

        private void ApplySuffixToSelected()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count == 0 && listFiles.Items.Count == 0)
            {
                SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                return;
            }

            using (StringInputDialog dialog = new StringInputDialog("파일명 뒤에 추가", "파일명과 확장자 사이에 추가할 문자열을 입력하세요.", renameSuffixText, true, new Action(SelectAllEntries)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                renameSuffixText = dialog.InputText ?? string.Empty;
                SaveSettings();
                if (dialog.SelectAllRequested) selected = GetListedEntries();
                if (selected.Count == 0)
                {
                    SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                    return;
                }

                string text = renameSuffixText;
                if (text.Length == 0) return;
                for (int i = 0; i < selected.Count; i++)
                {
                    FileEntry entry = selected[i];
                    string fileName = entry.PendingFileName ?? entry.FileName;
                    string baseName = GetEditableBaseName(fileName);
                    entry.PendingFileName = BuildFileNameWithSameSuffix(fileName, baseName + text);
                }
            }

            BuildColumns(true);
            SetStatus(selected.Count + "개 파일명에 뒤 문자열을 임시 적용했습니다.", StatusKind.Normal);
        }

        private void ApplyReplaceToSelected()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count == 0 && listFiles.Items.Count == 0)
            {
                SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                return;
            }

            using (ReplaceDialog dialog = new ReplaceDialog(renameReplaceFindText, renameReplaceText, new Action(SelectAllEntries)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                renameReplaceFindText = dialog.FindText ?? string.Empty;
                renameReplaceText = dialog.ReplaceText ?? string.Empty;
                SaveSettings();
                if (dialog.SelectAllRequested) selected = GetListedEntries();
                if (selected.Count == 0)
                {
                    SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                    return;
                }

                if (renameReplaceFindText.Length == 0)
                {
                    MessageBox.Show(this, "찾을 텍스트를 입력하세요.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                for (int i = 0; i < selected.Count; i++)
                {
                    FileEntry entry = selected[i];
                    string fileName = entry.PendingFileName ?? entry.FileName;
                    string baseName = GetEditableBaseName(fileName);
                    string changed = baseName.Replace(renameReplaceFindText, renameReplaceText);
                    entry.PendingFileName = BuildFileNameWithSameSuffix(fileName, changed);
                }
            }

            BuildColumns(true);
            SetStatus(selected.Count + "개 파일명의 문자열 치환을 임시 적용했습니다.", StatusKind.Normal);
        }

        private void ApplyRemoveSubstringFromSelected()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count == 0 && listFiles.Items.Count == 0)
            {
                SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                return;
            }

            using (RemoveSubstringDialog dialog = new RemoveSubstringDialog(renameRemoveStartText, renameRemoveEndText, renameRemoveFromEnd, new Action(SelectAllEntries)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                renameRemoveStartText = dialog.StartText;
                renameRemoveEndText = dialog.EndText;
                renameRemoveFromEnd = dialog.DeleteFromEnd;
                SaveSettings();
                if (dialog.SelectAllRequested) selected = GetListedEntries();
                if (selected.Count == 0)
                {
                    SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                    return;
                }

                int start = dialog.StartIndex;
                int end = dialog.EndIndex;
                if (start < 1 || end < 1 || start > end)
                {
                    MessageBox.Show(this, "삭제할 문자열 위치를 올바르게 입력하세요.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                for (int i = 0; i < selected.Count; i++)
                {
                    FileEntry entry = selected[i];
                    string fileName = entry.PendingFileName ?? entry.FileName;
                    string baseName = GetEditableBaseName(fileName);
                    string changed = RemoveSubstringByOneBasedRange(baseName, start, end, dialog.DeleteFromEnd);
                    entry.PendingFileName = BuildFileNameWithSameSuffix(fileName, changed);
                }
            }

            BuildColumns(true);
            SetStatus(selected.Count + "개 파일명의 일부 문자열 삭제를 임시 적용했습니다.", StatusKind.Normal);
        }

        private static string RemoveSubstringByOneBasedRange(string text, int start, int end, bool fromEnd)
        {
            string value = text ?? string.Empty;
            if (value.Length == 0) return value;

            int first;
            int count = end - start + 1;
            if (!fromEnd)
            {
                first = start - 1;
            }
            else
            {
                first = value.Length - end;
            }

            if (first < 0)
            {
                count += first;
                first = 0;
            }
            if (first >= value.Length || count <= 0) return value;
            if (first + count > value.Length) count = value.Length - first;
            return value.Remove(first, count);
        }

        private static string NormalizeBaseName(string input)
        {
            string text = (input ?? string.Empty).Trim();
            if (text.EndsWith(".lowend.sprite", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - ".lowend.sprite".Length);
            }
            else if (text.EndsWith(".sprite", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - ".sprite".Length);
            }
            else if (text.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                text = Path.GetFileNameWithoutExtension(text);
            }
            return text;
        }

        private void ExecutePendingRenames()
        {
            List<FileEntry> pending = new List<FileEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                FileEntry entry = entries[i];
                if (!string.IsNullOrEmpty(entry.PendingFileName) && !string.Equals(entry.PendingFileName, entry.FileName, StringComparison.OrdinalIgnoreCase)) pending.Add(entry);
            }

            if (pending.Count == 0)
            {
                SetStatus("반영할 파일명 변경값이 없습니다.", StatusKind.Error);
                return;
            }

            string validationError;
            if (!ValidatePendingRenames(pending, out validationError))
            {
                MessageBox.Show(this, validationError, "파일명 변경 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(this, "바꾸시겠습니까? 변경이 적용되면 되돌릴 수 없습니다.", "확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK) return;

            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            for (int i = 0; i < pending.Count; i++)
            {
                FileEntry entry = pending[i];
                string oldPath = entry.FullPath;
                string newPath = Path.Combine(entry.Folder, entry.PendingFileName);
                try
                {
                    File.Move(oldPath, newPath);
                    fileSet.Remove(oldPath);
                    fileSet.Add(newPath);
                    entry.FullPath = newPath;
                    entry.PendingFileName = null;
                    entry.RefreshMetadata();
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.Add(entry.FileName + " : " + ex.Message);
                }
            }

            for (int i = 0; i < entries.Count; i++) entries[i].PendingFileName = null;
            RefreshFileList();
            SetStatus("파일명 변경 완료: 성공 " + ok + "개, 실패 " + fail + "개", fail > 0 ? StatusKind.Error : StatusKind.Normal);

            if (fail > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.ToArray()), "실패한 파일", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ValidatePendingRenames(List<FileEntry> pending, out string error)
        {
            error = null;
            char[] invalid = Path.GetInvalidFileNameChars();
            HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pending.Count; i++)
            {
                FileEntry entry = pending[i];
                string newName = entry.PendingFileName ?? string.Empty;
                string baseName = GetEditableBaseName(newName);

                if (string.IsNullOrWhiteSpace(baseName))
                {
                    error = entry.FileName + " : 새 파일명이 비어 있습니다.";
                    return false;
                }

                for (int c = 0; c < invalid.Length; c++)
                {
                    if (newName.IndexOf(invalid[c]) >= 0)
                    {
                        error = newName + " : 파일명에 사용할 수 없는 문자가 포함되어 있습니다.";
                        return false;
                    }
                }

                string target = Path.Combine(entry.Folder, newName);
                if (!targets.Add(target))
                {
                    error = "일부 파일의 파일명이 중복되므로 파일명 변경을 실행하지 않았습니다.";
                    return false;
                }

                if (File.Exists(target) && !string.Equals(target, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    error = "일부 파일의 파일명이 중복되므로 파일명 변경을 실행하지 않았습니다.";
                    return false;
                }
            }

            return true;
        }

        private void ResetRenameDialogSettings()
        {
            renamePrefixText = string.Empty;
            renameSuffixText = string.Empty;
            renameReplaceFindText = string.Empty;
            renameReplaceText = string.Empty;
            renameRemoveStartText = "1";
            renameRemoveEndText = "1";
            renameRemoveFromEnd = false;
        }

        private void ResetPendingRenames()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].PendingFileName = null;
            ResetRenameDialogSettings();
            SaveSettings();
            BuildColumns(true);
            SetStatus("임시 파일명 변경값을 초기화했습니다.", StatusKind.Normal);
        }

        private void OpenSelectedFileLocation()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count != 1) return;
            try
            {
                Process.Start("explorer.exe", "/select,\"" + selected[0].FullPath + "\"");
            }
            catch
            {
                MessageBox.Show(this, "경로를 열 수 없습니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowSelectedFileProperties()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count != 1) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = selected[0].FullPath;
                psi.Verb = "properties";
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
                MessageBox.Show(this, "파일 속성을 열 수 없습니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ResetColumnLayoutToInitialState()
        {
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                ColumnSpec spec = columnSpecs[i];
                spec.Width = spec.DefaultWidth;
                spec.Visible = spec.DefaultVisible;
                spec.DisplayOrder = spec.DefaultOrder;
            }
            sortColumnId = "Order";
            sortAscending = true;
        }

        private void ResetMainWorkState()
        {
            DialogResult result = MessageBox.Show(
                T("reset.confirm_message", "Reset all work options and clear the file list?"),
                T("reset.confirm_title", "Reset"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            loadingSettings = true;
            try
            {
                chkForceSize.Checked = false;
                numForceWidth.Value = 20;
                numForceHeight.Value = 20;
                chkCustomSuffix.Checked = false;
                txtCustomSuffix.Text = string.Empty;
                chkPreviewConverted.Checked = false;
                chkIncludeLowendPngInList.Checked = true;
                chkIncludeSpritesInList.Checked = false;
                chkIncludeLowendSpritesInList.Checked = false;
                chkOutputPngFolder.Checked = true;
                chkOutputSpriteFolder.Checked = true;
                ResetRenameDialogSettings();
                ResetColumnLayoutToInitialState();
            }
            finally
            {
                loadingSettings = false;
            }

            ApplyOptionState();
            ClearListInternal();
            SaveSettings();
            UpdatePreview();
            SetStatus("작업 상태를 초기화하고 파일 목록을 비웠습니다.", StatusKind.Normal);
        }

        private void RemoveSelectedEntries()
        {
            List<FileEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                SetStatus("선택된 파일이 없습니다.", StatusKind.Error);
                return;
            }

            ReleaseLoadedFileResources();

            for (int i = 0; i < selected.Count; i++)
            {
                entries.Remove(selected[i]);
                fileSet.Remove(selected[i].FullPath);
            }

            BuildColumns(true);
            SetStatus(selected.Count + "개 항목을 목록에서 제거했습니다.", StatusKind.Normal);
            SelectFirstItemIfNothingSelected();
            UpdatePreview();
        }

        private int RemoveNonIncludedReferenceEntries()
        {
            int removed = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                bool shouldRemove = false;
                if (entries[i].AutoReference)
                {
                    if (IsSpriteEntry(entries[i])) shouldRemove = !ShouldShowSpritePath(entries[i].FullPath);
                    else if (IsLowendPngEntry(entries[i])) shouldRemove = !ShouldShowLowendPngPath(entries[i].FullPath);
                }
                if (!shouldRemove) continue;
                fileSet.Remove(entries[i].FullPath);
                entries.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private void RenumberEntries()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].Order = i + 1;
            nextOrder = entries.Count + 1;
        }

        private void SelectFirstItemIfNothingSelected()
        {
            if (listFiles.SelectedItems.Count == 0 && listFiles.Items.Count > 0)
            {
                listFiles.Items[0].Selected = true;
                listFiles.Items[0].Focused = true;
            }
        }

        private void RefreshFileList()
        {
            ReleaseLoadedFileResources();
            int missingRemoved = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (File.Exists(entries[i].FullPath)) continue;
                fileSet.Remove(entries[i].FullPath);
                entries.RemoveAt(i);
                missingRemoved++;
            }

            RefreshAllMetadata();

            int referenceRemoved = RemoveNonIncludedReferenceEntries();
            int before = fileSet.Count;
            List<FileEntry> snapshot = new List<FileEntry>(entries);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (IsTargetPng(snapshot[i].FullPath))
                {
                    AddMatchingLowendPngFile(snapshot[i].FullPath);
                    AddMatchingSpriteFiles(snapshot[i].FullPath);
                }
            }
            int referenceAdded = fileSet.Count - before;

            RenumberEntries();
            BuildColumns(false);
            if (listFiles != null)
            {
                listFiles.SelectedItems.Clear();
                if (listFiles.FocusedItem != null) listFiles.FocusedItem.Focused = false;
            }
            SetStatus("목록 새로고침: 사라진 파일 " + missingRemoved + "개 제거, 미표시 참조파일 " + referenceRemoved + "개 제거, " + referenceAdded + "개 추가", StatusKind.Normal);
            UpdatePreview();
        }

        private void RefreshAllMetadata()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].RefreshMetadata();
        }

        private void UpdateColumnSettingsFromListView()
        {
            if (listFiles == null) return;

            foreach (ColumnHeader header in listFiles.Columns)
            {
                ColumnSpec spec = header.Tag as ColumnSpec;
                if (spec == null) continue;
                if (header.Width > 0)
                {
                    spec.Width = spec.Id == "Folder" ? header.Width + GetVisibleDynamicColumnWidthForFolder() : header.Width;
                }
                spec.DisplayOrder = header.DisplayIndex;
            }
        }

        private void LoadSettings()
        {
            loadingSettings = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return;

                    string savedLanguage = Localization.NormalizeLanguage(Convert.ToString(key.GetValue("Language", currentLanguage)));
                    if (!string.Equals(savedLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        currentLanguage = savedLanguage;
                        Localization.Load(currentLanguage);
                    }

                    includeSubfolders = ReadBool(key, "IncludeSubfolders", true);
                    closeToTray = ReadBool(key, "CloseToTray", true);
                    clearBeforeDrop = ReadBool(key, "ClearBeforeDrop", true);
                    autoSizeByLowend = ReadBool(key, "AutoSizeByLowend", true);
                    loadMatchingSprites = ReadBool(key, "LoadMatchingSprites", true);
                    includeLowendSprites = ReadBool(key, "IncludeLowendSprites", true);
                    convertSelectedOnly = ReadBool(key, "ConvertSelectedOnly", false);
                    chkPreviewConverted.Checked = ReadBool(key, "PreviewConverted", false);
                    chkIncludeLowendPngInList.Checked = ReadBool(key, "IncludeLowendPngInList", true);
                    chkIncludeSpritesInList.Checked = ReadBool(key, "IncludeSpritesInList", false);
                    chkIncludeLowendSpritesInList.Checked = ReadBool(key, "IncludeLowendSpritesInList", false);
                    chkOutputPngFolder.Checked = ReadBool(key, "OutputPngFolder", true);
                    chkOutputSpriteFolder.Checked = ReadBool(key, "OutputSpriteFolder", true);
                    previewNavigationMode = ReadPreviewNavigationMode(key, "PreviewNavigationMode", PreviewNavigationInputMode.ArrowKeys);
                    previewZoomStepPercent = ClampInt(ReadInt(key, "PreviewZoomStepPercent", 30), 5, 50);
                    previewFixedZoomBasePercent = ClampInt(ReadInt(key, "PreviewFixedZoomBasePercent", 100), 50, 200);
                    previewUseCanvasColor = ReadBool(key, "PreviewUseCanvasColor", true);
                    previewCanvasColor = ReadColor(key, "PreviewCanvasColorArgb", Color.Black);
                    previewUseAlphaColor = ReadBool(key, "PreviewUseAlphaColor", false);
                    previewAlphaColor = ReadColor(key, "PreviewAlphaColorArgb", Color.White);

                    chkForceSize.Checked = ReadBool(key, "ForceSize", false);
                    numForceWidth.Value = ClampDecimal(ReadInt(key, "ForceWidth", 20), numForceWidth.Minimum, numForceWidth.Maximum);
                    numForceHeight.Value = ClampDecimal(ReadInt(key, "ForceHeight", 20), numForceHeight.Minimum, numForceHeight.Maximum);
                    chkCustomSuffix.Checked = ReadBool(key, "CustomSuffixChecked", false);
                    txtCustomSuffix.Text = Convert.ToString(key.GetValue("CustomSuffixText", string.Empty));
                    lastFolderPath = Convert.ToString(key.GetValue("LastFolder", string.Empty));
                    renamePrefixText = Convert.ToString(key.GetValue("RenamePrefixText", string.Empty));
                    renameSuffixText = Convert.ToString(key.GetValue("RenameSuffixText", string.Empty));
                    renameReplaceFindText = Convert.ToString(key.GetValue("RenameReplaceFindText", string.Empty));
                    renameReplaceText = Convert.ToString(key.GetValue("RenameReplaceText", string.Empty));
                    renameRemoveStartText = Convert.ToString(key.GetValue("RenameRemoveStartText", "1"));
                    renameRemoveEndText = Convert.ToString(key.GetValue("RenameRemoveEndText", "1"));
                    renameRemoveFromEnd = ReadBool(key, "RenameRemoveFromEnd", false);

                    sortColumnId = Convert.ToString(key.GetValue("SortColumn", "Order"));
                    sortAscending = ReadBool(key, "SortAscending", true);

                    LoadColumnSettings(key);
                }
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private void LoadColumnSettings(RegistryKey key)
        {
            for (int i = 0; i < columnSpecs.Count; i++)
            {
                ColumnSpec spec = columnSpecs[i];
                if (spec.Id != "NewName" && spec.Id != "ConversionResult") spec.Visible = ReadBool(key, "ColumnVisible_" + spec.Id, spec.DefaultVisible);
                spec.Width = spec.DefaultWidth;
            }

            string orderText = Convert.ToString(key.GetValue("ColumnOrder", string.Empty));
            if (!string.IsNullOrEmpty(orderText))
            {
                string[] ids = orderText.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < ids.Length; i++)
                {
                    ColumnSpec spec = GetColumnSpec(ids[i]);
                    if (spec != null) spec.DisplayOrder = i;
                }
            }
        }

        private void SaveSettings()
        {
            if (loadingSettings) return;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return;
                    key.SetValue("Language", currentLanguage ?? "en", RegistryValueKind.String);
                    key.SetValue("IncludeSubfolders", includeSubfolders ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("CloseToTray", closeToTray ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ClearBeforeDrop", clearBeforeDrop ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("AutoSizeByLowend", autoSizeByLowend ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("LoadMatchingSprites", loadMatchingSprites ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("IncludeLowendSprites", includeLowendSprites ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ConvertSelectedOnly", convertSelectedOnly ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PreviewConverted", chkPreviewConverted.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("IncludeLowendPngInList", chkIncludeLowendPngInList.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("IncludeSpritesInList", chkIncludeSpritesInList.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("IncludeLowendSpritesInList", chkIncludeLowendSpritesInList.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("OutputPngFolder", chkOutputPngFolder.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("OutputSpriteFolder", chkOutputSpriteFolder.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PreviewNavigationMode", ((int)previewNavigationMode).ToString(), RegistryValueKind.String);
                    key.SetValue("PreviewZoomStepPercent", previewZoomStepPercent.ToString(), RegistryValueKind.String);
                    key.SetValue("PreviewFixedZoomBasePercent", previewFixedZoomBasePercent.ToString(), RegistryValueKind.String);
                    key.SetValue("PreviewUseCanvasColor", previewUseCanvasColor ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PreviewCanvasColorArgb", previewCanvasColor.ToArgb().ToString(), RegistryValueKind.String);
                    key.SetValue("PreviewUseAlphaColor", previewUseAlphaColor ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PreviewAlphaColorArgb", previewAlphaColor.ToArgb().ToString(), RegistryValueKind.String);
                    key.SetValue("ForceSize", chkForceSize.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ForceWidth", ((int)numForceWidth.Value).ToString(), RegistryValueKind.String);
                    key.SetValue("ForceHeight", ((int)numForceHeight.Value).ToString(), RegistryValueKind.String);
                    key.SetValue("CustomSuffixChecked", chkCustomSuffix.Checked ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("CustomSuffixText", txtCustomSuffix.Text ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("LastFolder", lastFolderPath ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("RenamePrefixText", renamePrefixText ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("RenameSuffixText", renameSuffixText ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("RenameReplaceFindText", renameReplaceFindText ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("RenameReplaceText", renameReplaceText ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("RenameRemoveStartText", string.IsNullOrWhiteSpace(renameRemoveStartText) ? "1" : renameRemoveStartText, RegistryValueKind.String);
                    key.SetValue("RenameRemoveEndText", string.IsNullOrWhiteSpace(renameRemoveEndText) ? "1" : renameRemoveEndText, RegistryValueKind.String);
                    key.SetValue("RenameRemoveFromEnd", renameRemoveFromEnd ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("SortColumn", sortColumnId ?? "Order", RegistryValueKind.String);
                    key.SetValue("SortAscending", sortAscending ? 1 : 0, RegistryValueKind.DWord);
                    SaveColumnSettings(key);
                }
            }
            catch
            {
                // 설정 저장 실패는 변환 기능에 영향을 주지 않으므로 조용히 무시한다.
            }
        }

        private void SaveColumnSettings(RegistryKey key)
        {
            UpdateColumnSettingsFromListView();

            List<ColumnSpec> ordered = new List<ColumnSpec>(columnSpecs);
            ordered.Sort(delegate(ColumnSpec a, ColumnSpec b)
            {
                int c = a.DisplayOrder.CompareTo(b.DisplayOrder);
                if (c != 0) return c;
                return a.DefaultOrder.CompareTo(b.DefaultOrder);
            });

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ordered[i].Id);
            }
            key.SetValue("ColumnOrder", sb.ToString(), RegistryValueKind.String);

            for (int i = 0; i < columnSpecs.Count; i++)
            {
                ColumnSpec spec = columnSpecs[i];
                if (spec.Id != "NewName" && spec.Id != "ConversionResult") key.SetValue("ColumnVisible_" + spec.Id, spec.Visible ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static PreviewNavigationInputMode ReadPreviewNavigationMode(RegistryKey key, string name, PreviewNavigationInputMode fallback)
        {
            int value = ReadInt(key, name, (int)fallback);
            if (value == (int)PreviewNavigationInputMode.MouseWheel) return PreviewNavigationInputMode.MouseWheel;
            if (value == (int)PreviewNavigationInputMode.PageKeys) return PreviewNavigationInputMode.PageKeys;
            return PreviewNavigationInputMode.ArrowKeys;
        }

        private static bool ReadBool(RegistryKey key, string name, bool fallback)
        {
            object value = key.GetValue(name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value) != 0; } catch { return fallback; }
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            object value = key.GetValue(name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value); } catch { return fallback; }
        }

        private static Color ReadColor(RegistryKey key, string name, Color fallback)
        {
            object value = key.GetValue(name);
            if (value == null) return fallback;
            try { return Color.FromArgb(Convert.ToInt32(value)); } catch { return fallback; }
        }

        private static decimal ClampDecimal(int value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string NormalizeInitialFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string clean = path.Trim().Trim('"');
            if (Directory.Exists(clean)) return clean;

            try
            {
                string parent = Path.GetDirectoryName(clean);
                while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    parent = Path.GetDirectoryName(parent);
                }
                return parent ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal sealed class FileEntry
    {
        public string FullPath;
        public int Order;
        public string PendingFileName;
        public string ConversionResult;
        public bool ConversionSucceeded;
        public int ImageWidth;
        public int ImageHeight;
        public int BitDepth;
        public long FileSize;
        public bool AutoReference;
        public DateTime CreatedTime;
        public DateTime ModifiedTime;
        public DateTime AccessedTime;

        public FileEntry(string fullPath, int order)
        {
            FullPath = fullPath;
            Order = order;
        }

        public string FileName
        {
            get { return Path.GetFileName(FullPath); }
        }

        public string Folder
        {
            get { return Path.GetDirectoryName(FullPath) ?? string.Empty; }
        }

        public void RefreshMetadata()
        {
            ImageWidth = 0;
            ImageHeight = 0;
            BitDepth = 0;
            FileSize = 0;
            CreatedTime = DateTime.MinValue;
            ModifiedTime = DateTime.MinValue;
            AccessedTime = DateTime.MinValue;

            try
            {
                FileInfo info = new FileInfo(FullPath);
                if (info.Exists)
                {
                    FileSize = info.Length;
                    CreatedTime = info.CreationTime;
                    ModifiedTime = info.LastWriteTime;
                    AccessedTime = info.LastAccessTime;
                }
            }
            catch { }

            int width;
            int height;
            int bitDepth;
            if (ImageFileUtil.TryReadImageInfoNoLock(FullPath, out width, out height, out bitDepth))
            {
                ImageWidth = width;
                ImageHeight = height;
                BitDepth = bitDepth;
            }
        }
    }

    internal sealed class ColumnSpec
    {
        public readonly string Id;
        public string Title;
        public readonly int DefaultWidth;
        public readonly bool DefaultVisible;
        public readonly int DefaultOrder;
        public int Width;
        public bool Visible;
        public int DisplayOrder;

        public ColumnSpec(string id, string title, int defaultWidth, bool defaultVisible, int defaultOrder)
        {
            Id = id;
            Title = title;
            DefaultWidth = defaultWidth;
            DefaultVisible = defaultVisible;
            DefaultOrder = defaultOrder;
            Width = defaultWidth;
            Visible = defaultVisible;
            DisplayOrder = defaultOrder;
        }
    }

    internal sealed class SettingsDialog : Form
    {
        private readonly CheckBox chkAutoSize;
        private readonly CheckBox chkClearBeforeDrop;
        private readonly CheckBox chkLoadMatchingSprites;
        private readonly CheckBox chkIncludeLowendSprites;
        private readonly CheckBox chkIncludeSubfolders;
        private readonly CheckBox chkCloseToTray;
        private readonly CheckBox chkConvertSelectedOnly;
        private readonly ComboBox cmbNavigationMode;
        private readonly TrackBar trkZoomSpeed;
        private readonly Label lblZoomSpeed;
        private readonly TrackBar trkFixedZoomBase;
        private readonly Label lblFixedZoomBase;
        private readonly Label lblFixedZoom;
        private readonly CheckBox chkUseCanvasColor;
        private readonly Button btnCanvasColor;
        private readonly CheckBox chkUseAlphaColor;
        private readonly Button btnAlphaColor;

        private Color canvasColor;
        private Color alphaColor;

        public bool AutoSizeByLowend { get { return chkAutoSize.Checked; } }
        public bool ClearBeforeDrop { get { return chkClearBeforeDrop.Checked; } }
        public bool LoadMatchingSprites { get { return chkLoadMatchingSprites.Checked; } }
        public bool IncludeLowendSprites { get { return chkIncludeLowendSprites.Checked; } }
        public bool IncludeSubfolders { get { return chkIncludeSubfolders.Checked; } }
        public bool CloseToTray { get { return chkCloseToTray.Checked; } }
        public bool ConvertSelectedOnly { get { return chkConvertSelectedOnly.Checked; } }
        public PreviewNavigationInputMode PreviewNavigationMode { get { return GetSelectedNavigationMode(); } }
        public int PreviewZoomStepPercent { get { return trkZoomSpeed.Value; } }
        public int PreviewFixedZoomBasePercent { get { return trkFixedZoomBase.Value; } }
        public bool PreviewUseCanvasColor { get { return chkUseCanvasColor.Checked; } }
        public Color PreviewCanvasColor { get { return canvasColor; } }
        public bool PreviewUseAlphaColor { get { return chkUseAlphaColor.Checked; } }
        public Color PreviewAlphaColor { get { return alphaColor; } }

        public SettingsDialog(bool autoSizeByLowend, bool clearBeforeDrop, bool loadMatchingSprites, bool includeLowendSprites, bool includeSubfolders, PreviewNavigationInputMode navigationMode, int previewZoomStepPercent, int previewFixedZoomBasePercent, bool previewUseCanvasColor, Color previewCanvasColor, bool previewUseAlphaColor, Color previewAlphaColor, bool closeToTray, bool convertSelectedOnly)
        {
            Text = Localization.T("settings.title", "Settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 808);

            canvasColor = previewCanvasColor;
            alphaColor = previewAlphaColor;

            GroupBox groupAuto = new GroupBox { Text = Localization.T("settings.group_auto", "Automatic size correction"), Left = 12, Top = 12, Width = 516, Height = 96 };
            chkAutoSize = new CenteredCheckBox { Text = Localization.T("settings.auto_size", "Detect .lowend.png size and adjust each file automatically"), Left = 14, Top = 25, Width = 470, Height = 24, Checked = autoSizeByLowend };
            Label autoDesc = new Label
            {
                Text = Localization.T("settings.auto_desc", "If a matching .lowend.png exists in the same folder, its size is used.\r\nOtherwise the default 50% size is used."),
                Left = 14,
                Top = 53,
                Width = 480,
                Height = 36
            };
            groupAuto.Controls.Add(chkAutoSize);
            groupAuto.Controls.Add(autoDesc);

            GroupBox groupLoad = new GroupBox { Text = Localization.T("settings.group_load", "File loading"), Left = 12, Top = 118, Width = 516, Height = 146 };
            chkClearBeforeDrop = new CenteredCheckBox { Text = Localization.T("settings.clear_before_drop", "Clear previous file list when drag-and-dropping"), Left = 14, Top = 25, Width = 430, Height = 24, Checked = clearBeforeDrop };
            chkLoadMatchingSprites = new CenteredCheckBox { Text = Localization.T("settings.load_matching_sprite", "Load matching .sprite when loading PNG"), Left = 14, Top = 53, Width = 430, Height = 24, Checked = loadMatchingSprites };
            chkIncludeLowendSprites = new CenteredCheckBox { Text = Localization.T("settings.include_lowend_sprite", "Also load .lowend.sprite"), Left = 36, Top = 80, Width = 220, Height = 24, Checked = includeLowendSprites };
            chkConvertSelectedOnly = new CenteredCheckBox { Text = Localization.T("settings.convert_selected_only", "Convert selected files only when files are selected"), Left = 14, Top = 108, Width = 430, Height = 24, Checked = convertSelectedOnly };
            groupLoad.Controls.Add(chkClearBeforeDrop);
            groupLoad.Controls.Add(chkLoadMatchingSprites);
            groupLoad.Controls.Add(chkIncludeLowendSprites);
            groupLoad.Controls.Add(chkConvertSelectedOnly);

            GroupBox groupPreview = new GroupBox { Text = Localization.T("settings.group_preview", "Preview window"), Left = 12, Top = 274, Width = 516, Height = 334 };
            Label lblNavigationMode = new Label { Text = Localization.T("settings.navigation", "Previous/next file"), Left = 14, Top = 30, Width = 130, Height = 20 };
            cmbNavigationMode = new ComboBox { Left = 154, Top = 26, Width = 250, Height = 24, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbNavigationMode.Items.Add(Localization.T("settings.nav_arrows", "Arrow keys ← / →"));
            cmbNavigationMode.Items.Add(Localization.T("settings.nav_wheel", "Mouse wheel"));
            cmbNavigationMode.Items.Add(Localization.T("settings.nav_page", "PgUp / PgDn"));
            cmbNavigationMode.SelectedIndex = NavigationModeToIndex(navigationMode);

            lblZoomSpeed = new Label { Left = 14, Top = 78, Width = 180, Height = 20 };
            trkZoomSpeed = new TrackBar { Left = 190, Top = 64, Width = 300, Height = 48, Minimum = 5, Maximum = 50, TickFrequency = 5, SmallChange = 5, LargeChange = 10, Value = ClampTrackValue(previewZoomStepPercent, 5, 50) };

            lblFixedZoomBase = new Label { Left = 14, Top = 128, Width = 180, Height = 20 };
            trkFixedZoomBase = new TrackBar { Left = 190, Top = 114, Width = 300, Height = 48, Minimum = 50, Maximum = 200, TickFrequency = 10, SmallChange = 10, LargeChange = 10, Value = ClampTrackValue(previewFixedZoomBasePercent, 50, 200) };
            lblFixedZoom = new Label { Left = 14, Top = 170, Width = 470, Height = 34 };

            Label lblCanvasColor = new Label { Text = Localization.T("settings.canvas_outer", "Outer canvas"), Left = 14, Top = 222, Width = 130, Height = 22 };
            chkUseCanvasColor = new CenteredCheckBox { Text = Localization.T("settings.solid_color", "Solid color"), Left = 154, Top = 219, Width = 100, Height = 24, Checked = previewUseCanvasColor };
            btnCanvasColor = new Button { Left = 264, Top = 216, Width = 110, Height = 28 };
            Label lblCanvasDesc = new Label { Text = Localization.T("settings.checker_when_off", "Checkerboard when off"), Left = 382, Top = 222, Width = 110, Height = 22 };

            Label lblAlphaColor = new Label { Text = Localization.T("settings.alpha_area", "Alpha area"), Left = 14, Top = 268, Width = 130, Height = 22 };
            chkUseAlphaColor = new CenteredCheckBox { Text = Localization.T("settings.solid_color", "Solid color"), Left = 154, Top = 265, Width = 100, Height = 24, Checked = previewUseAlphaColor };
            btnAlphaColor = new Button { Left = 264, Top = 262, Width = 110, Height = 28 };
            Label lblAlphaDesc = new Label { Text = Localization.T("settings.checker_when_off", "Checkerboard when off"), Left = 382, Top = 268, Width = 110, Height = 22 };

            groupPreview.Controls.Add(lblNavigationMode);
            groupPreview.Controls.Add(cmbNavigationMode);
            groupPreview.Controls.Add(lblZoomSpeed);
            groupPreview.Controls.Add(trkZoomSpeed);
            groupPreview.Controls.Add(lblFixedZoomBase);
            groupPreview.Controls.Add(trkFixedZoomBase);
            groupPreview.Controls.Add(lblFixedZoom);
            groupPreview.Controls.Add(lblCanvasColor);
            groupPreview.Controls.Add(chkUseCanvasColor);
            groupPreview.Controls.Add(btnCanvasColor);
            groupPreview.Controls.Add(lblCanvasDesc);
            groupPreview.Controls.Add(lblAlphaColor);
            groupPreview.Controls.Add(chkUseAlphaColor);
            groupPreview.Controls.Add(btnAlphaColor);
            groupPreview.Controls.Add(lblAlphaDesc);

            GroupBox groupWindow = new GroupBox { Text = Localization.T("settings.group_window", "Window"), Left = 12, Top = 620, Width = 516, Height = 48 };
            chkCloseToTray = new CenteredCheckBox { Text = Localization.T("settings.close_to_tray", "Use system tray (send there on close)"), Left = 14, Top = 18, Width = 430, Height = 24, Checked = closeToTray };
            groupWindow.Controls.Add(chkCloseToTray);

            GroupBox groupFolder = new GroupBox { Text = Localization.T("settings.group_folder", "Add folder"), Left = 12, Top = 676, Width = 516, Height = 48 };
            chkIncludeSubfolders = new CenteredCheckBox { Text = Localization.T("settings.include_subfolders", "Include subfolders when adding folder"), Left = 14, Top = 18, Width = 300, Height = 24, Checked = includeSubfolders };
            groupFolder.Controls.Add(chkIncludeSubfolders);

            Button defaults = new Button { Text = Localization.T("settings.defaults", "Defaults"), Left = 274, Top = 758, Width = 78, Height = 26 };
            Button ok = new Button { Text = Localization.T("settings.ok", "OK"), Left = 362, Top = 758, Width = 78, Height = 26, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Localization.T("settings.cancel", "Cancel"), Left = 450, Top = 758, Width = 78, Height = 26, DialogResult = DialogResult.Cancel };

            chkLoadMatchingSprites.CheckedChanged += delegate { ApplyState(); };
            chkUseCanvasColor.CheckedChanged += delegate { ApplyState(); };
            chkUseAlphaColor.CheckedChanged += delegate { ApplyState(); };
            trkZoomSpeed.ValueChanged += delegate { UpdatePreviewLabels(); };
            trkFixedZoomBase.ValueChanged += delegate { UpdatePreviewLabels(); };
            btnCanvasColor.Click += delegate { ChooseColor(ref canvasColor, btnCanvasColor); };
            btnAlphaColor.Click += delegate { ChooseColor(ref alphaColor, btnAlphaColor); };
            defaults.Click += delegate { LoadDefaultSettingsWithConfirm(); };
            ApplyState();
            UpdatePreviewLabels();
            UpdateColorButton(btnCanvasColor, canvasColor);
            UpdateColorButton(btnAlphaColor, alphaColor);

            Controls.AddRange(new Control[] { groupAuto, groupLoad, groupPreview, groupWindow, groupFolder, defaults, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void ApplyState()
        {
            chkIncludeLowendSprites.Enabled = chkLoadMatchingSprites.Checked;
            btnCanvasColor.Enabled = chkUseCanvasColor.Checked;
            btnAlphaColor.Enabled = chkUseAlphaColor.Checked;
        }

        private void LoadDefaultSettingsWithConfirm()
        {
            DialogResult result = MessageBox.Show(this, Localization.T("settings.confirm_defaults", "Load default settings?"), Localization.T("settings.confirm", "Confirm"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result != DialogResult.OK) return;

            chkAutoSize.Checked = true;
            chkClearBeforeDrop.Checked = true;
            chkLoadMatchingSprites.Checked = true;
            chkIncludeLowendSprites.Checked = true;
            chkIncludeSubfolders.Checked = true;
            chkCloseToTray.Checked = true;
            chkConvertSelectedOnly.Checked = false;
            cmbNavigationMode.SelectedIndex = NavigationModeToIndex(PreviewNavigationInputMode.ArrowKeys);
            trkZoomSpeed.Value = 30;
            trkFixedZoomBase.Value = 100;
            chkUseCanvasColor.Checked = true;
            canvasColor = Color.Black;
            chkUseAlphaColor.Checked = false;
            alphaColor = Color.White;
            ApplyState();
            UpdatePreviewLabels();
            UpdateColorButton(btnCanvasColor, canvasColor);
            UpdateColorButton(btnAlphaColor, alphaColor);
        }

        private void UpdatePreviewLabels()
        {
            int step = trkFixedZoomBase.Value;
            lblZoomSpeed.Text = Localization.T("settings.zoom_speed", "Zoom speed : {0}%", trkZoomSpeed.Value);
            lblFixedZoomBase.Text = Localization.T("settings.fixed_zoom_interval", "Fixed zoom interval : {0}%", step);
            lblFixedZoom.Text = Localization.T("settings.fixed_zoom_desc", "Num1=100%, Num2~9 = 100% + interval*1~8\r\nRange: 100% ~ {0}% / Reset: wheel-click·Num0", 100 + step * 8);
        }

        private void ChooseColor(ref Color colorField, Button button)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = colorField;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                colorField = dialog.Color;
                UpdateColorButton(button, colorField);
            }
        }

        private static void UpdateColorButton(Button button, Color color)
        {
            if (button == null) return;
            button.UseVisualStyleBackColor = false;
            button.BackColor = color;
            button.Text = ColorToHex(color);
            int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
            button.ForeColor = brightness < 128 ? Color.White : Color.Black;
        }

        private static string ColorToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private PreviewNavigationInputMode GetSelectedNavigationMode()
        {
            if (cmbNavigationMode.SelectedIndex == 1) return PreviewNavigationInputMode.MouseWheel;
            if (cmbNavigationMode.SelectedIndex == 2) return PreviewNavigationInputMode.PageKeys;
            return PreviewNavigationInputMode.ArrowKeys;
        }

        private static int NavigationModeToIndex(PreviewNavigationInputMode mode)
        {
            if (mode == PreviewNavigationInputMode.MouseWheel) return 1;
            if (mode == PreviewNavigationInputMode.PageKeys) return 2;
            return 0;
        }

        private static int ClampTrackValue(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    internal enum ExitMenuChoice
    {
        Cancel,
        Exit,
        Tray
    }

    internal sealed class ExitConfirmDialog : Form
    {
        private ExitMenuChoice choice = ExitMenuChoice.Cancel;

        public static ExitMenuChoice ShowPrompt(IWin32Window owner)
        {
            using (ExitConfirmDialog dialog = new ExitConfirmDialog())
            {
                dialog.ShowDialog(owner);
                return dialog.choice;
            }
        }

        private ExitConfirmDialog()
        {
            Text = Localization.T("exit_menu.title", "Exit");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 150);

            Label message = new Label
            {
                Text = Localization.T("exit_menu.message", "The File > Exit command closes the program immediately without sending it to the system tray.\r\n\r\nWhat would you like to do?"),
                Left = 18,
                Top = 18,
                Width = ClientSize.Width - 36,
                Height = 62,
                TextAlign = ContentAlignment.MiddleLeft
            };

            const int buttonWidth = 118;
            const int buttonHeight = 30;
            const int gap = 10;
            int totalWidth = buttonWidth * 3 + gap * 2;
            int left = (ClientSize.Width - totalWidth) / 2;
            int top = ClientSize.Height - 48;

            Button btnExit = new Button
            {
                Text = Localization.T("exit_menu.confirm", "Exit"),
                Left = left,
                Top = top,
                Width = buttonWidth,
                Height = buttonHeight
            };
            btnExit.Click += delegate { choice = ExitMenuChoice.Exit; DialogResult = DialogResult.OK; Close(); };

            Button btnCancel = new Button
            {
                Text = Localization.T("exit_menu.cancel", "Cancel"),
                Left = left + buttonWidth + gap,
                Top = top,
                Width = buttonWidth,
                Height = buttonHeight
            };
            btnCancel.Click += delegate { choice = ExitMenuChoice.Cancel; DialogResult = DialogResult.Cancel; Close(); };

            Button btnTray = new Button
            {
                Text = Localization.T("exit_menu.tray", "Minimize to tray"),
                Left = left + (buttonWidth + gap) * 2,
                Top = top,
                Width = buttonWidth,
                Height = buttonHeight
            };
            btnTray.Click += delegate { choice = ExitMenuChoice.Tray; DialogResult = DialogResult.OK; Close(); };

            Controls.AddRange(new Control[] { message, btnExit, btnCancel, btnTray });
            AcceptButton = btnExit;
            CancelButton = btnCancel;
        }
    }

    internal sealed class CreditDialog : Form
    {
        public CreditDialog(string invenLinkUrl, string nexusLinkUrl, string version)
        {
            Text = Localization.T("info.title", "Info");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 360);

            const int margin = 18;
            const int rowHeight = 22;
            const int rowGap = 4;
            const int logoSize = 108;
            const int buttonWidth = 82;
            const int buttonHeight = 30;

            Button ok = new Button
            {
                Text = Localization.T("dialog.ok", "OK"),
                Left = (ClientSize.Width - buttonWidth) / 2,
                Top = ClientSize.Height - margin - buttonHeight,
                Width = buttonWidth,
                Height = buttonHeight,
                DialogResult = DialogResult.OK
            };

            PictureBox logo = new PictureBox
            {
                Left = ClientSize.Width - margin - logoSize,
                Top = margin,
                Width = logoSize,
                Height = logoSize,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };
            LoadLogoImage(logo);

            int infoWidth = logo.Left - (margin * 2);
            Label distribution = new Label
            {
                Text = Localization.T("info.distribution_label", "Distribution :"),
                Left = margin,
                Top = margin + 4,
                Width = infoWidth,
                Height = rowHeight,
                TextAlign = ContentAlignment.MiddleLeft
            };

            LinkLabel inven = CreateDistributionLink(
                Localization.T("info.inven", "Inven Mod Archive"),
                Localization.T("info.link_text", "Open"),
                invenLinkUrl,
                margin,
                distribution.Bottom + rowGap,
                infoWidth,
                rowHeight);

            LinkLabel nexus = CreateDistributionLink(
                Localization.T("info.nexus", "Nexus Mods"),
                Localization.T("info.link_text", "Open"),
                nexusLinkUrl,
                margin,
                inven.Bottom + rowGap,
                infoWidth,
                rowHeight);

            Label versionLabel = new Label
            {
                Text = Localization.T("info.version", "Version : {0}", version),
                Left = margin,
                Top = nexus.Bottom + rowGap + 4,
                Width = infoWidth,
                Height = rowHeight,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Label line = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Left = margin,
                Top = Math.Max(versionLabel.Bottom + 18, logo.Bottom + 18),
                Width = ClientSize.Width - margin * 2,
                Height = 2
            };

            Label notice = new Label
            {
                Text = Localization.T("info.notice", "This is an unofficial free fan-made tool for PNG / Sprite conversion work for Diablo II: Resurrected. It is not affiliated with, endorsed by, or sponsored by Blizzard Entertainment. The code was created with ChatGPT assistance.\r\n\r\nYou may modify and redistribute it under the included license. To avoid user confusion, credit the source and original base version, and clearly identify your changes. Commercial use is restricted.\r\n\r\nNo further feature updates are planned except fixes for critical errors or broken intended features.\r\n\r\nThank you."),
                Left = margin,
                Top = line.Bottom + 14,
                Width = ClientSize.Width - margin * 2,
                Height = ok.Top - (line.Bottom + 24),
                TextAlign = ContentAlignment.TopLeft
            };

            Controls.AddRange(new Control[] { distribution, inven, nexus, versionLabel, logo, line, notice, ok });
            AcceptButton = ok;
            CancelButton = ok;
        }

        private static LinkLabel CreateDistributionLink(string name, string linkText, string url, int left, int top, int width, int height)
        {
            string text = name + " (" + linkText + ")";
            LinkLabel link = new LinkLabel
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            int linkStart = text.LastIndexOf(linkText, StringComparison.Ordinal);
            if (linkStart >= 0)
            {
                link.Links.Clear();
                link.Links.Add(linkStart, linkText.Length, url);
            }
            link.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                string target = Convert.ToString(e.Link.LinkData);
                MainForm.OpenPostLink(link, target);
            };
            return link;
        }

        private static void LoadLogoImage(PictureBox logo)
        {
            try
            {
                using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("D2RSpriteToolkit.logo.png"))
                {
                    if (stream == null) return;

                    using (Image img = Image.FromStream(stream))
                    {
                        logo.Image = new Bitmap(img, new Size(90, 90));
                    }
                }
            }
            catch { }
        }
    }

    internal sealed class MetadataInfoDialog : Form
    {
        public MetadataInfoDialog(string headerText)
        {
            Text = Localization.T("metadata_dialog.title", "Metadata Information");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 340);
            MinimumSize = new Size(420, 260);

            TextBox textBox = new TextBox
            {
                Left = 16,
                Top = 16,
                Width = ClientSize.Width - 32,
                Height = ClientSize.Height - 66,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Text = headerText ?? string.Empty
            };

            Button ok = new Button
            {
                Text = Localization.T("dialog.ok", "OK"),
                Width = 86,
                Height = 30,
                Left = ClientSize.Width - 102,
                Top = ClientSize.Height - 42,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };

            Controls.Add(textBox);
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }

    internal sealed class StringInputDialog : Form
    {
        private readonly TextBox textBox;
        private bool selectAllRequested;

        public string InputText { get { return textBox.Text; } }
        public bool SelectAllRequested { get { return selectAllRequested; } }

        public StringInputDialog(string title, string message, string initialText)
            : this(title, message, initialText, false, null)
        {
        }

        public StringInputDialog(string title, string message, string initialText, bool showSelectAllButton, Action selectAllAction)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 130);

            Label label = new Label { Text = message, Left = 12, Top = 12, Width = 396, Height = 22 };
            textBox = new TextBox { Left = 12, Top = 42, Width = 396, Text = initialText ?? string.Empty };
            Button ok = new Button { Text = Localization.T("settings.ok", "OK"), Left = 246, Top = 86, Width = 78, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Localization.T("settings.cancel", "Cancel"), Left = 330, Top = 86, Width = 78, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { label, textBox, ok, cancel });

            if (showSelectAllButton)
            {
                Button selectAll = new Button { Text = DialogSelectAllText(), Left = 12, Top = 86, Width = 96, Height = 26 };
                selectAll.Click += delegate
                {
                    selectAllRequested = true;
                    if (selectAllAction != null) selectAllAction();
                };
                Controls.Add(selectAll);
            }

            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate
            {
                textBox.Focus();
                textBox.SelectAll();
            };
        }

        internal static string DialogSelectAllText()
        {
            return string.Equals(Localization.CurrentLanguage, "ko", StringComparison.OrdinalIgnoreCase) ? "모두 선택" : "Select all";
        }
    }

    internal sealed class RemoveSubstringDialog : Form
    {
        private readonly TextBox txtStart;
        private readonly TextBox txtEnd;
        private readonly CheckBox chkFromStart;
        private readonly CheckBox chkFromEnd;
        private bool updating;
        private bool selectAllRequested;

        public string StartText { get { return txtStart.Text.Trim(); } }
        public string EndText { get { return txtEnd.Text.Trim(); } }
        public bool SelectAllRequested { get { return selectAllRequested; } }

        public int StartIndex
        {
            get
            {
                int value;
                if (!int.TryParse(txtStart.Text.Trim(), out value)) return 0;
                return value;
            }
        }

        public int EndIndex
        {
            get
            {
                int value;
                if (!int.TryParse(txtEnd.Text.Trim(), out value)) return 0;
                return value;
            }
        }

        public bool DeleteFromEnd { get { return chkFromEnd.Checked; } }

        public RemoveSubstringDialog()
            : this("1", "1", false, null)
        {
        }

        public RemoveSubstringDialog(string initialStart, string initialEnd, bool initialFromEnd, Action selectAllAction)
        {
            Text = "파일명 일부 삭제";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(500, 210);

            txtStart = new TextBox { Left = 18, Top = 19, Width = 54, Text = string.IsNullOrWhiteSpace(initialStart) ? "1" : initialStart, TextAlign = HorizontalAlignment.Center };
            Label lblMid = new Label { Text = "번째부터", Left = 78, Top = 22, Width = 70, Height = 22 };
            txtEnd = new TextBox { Left = 150, Top = 19, Width = 54, Text = string.IsNullOrWhiteSpace(initialEnd) ? "1" : initialEnd, TextAlign = HorizontalAlignment.Center };
            Label lblEnd = new Label { Text = "번째까지의 문자열을 삭제합니다.", Left = 210, Top = 22, Width = 260, Height = 22 };

            chkFromStart = new CheckBox { Text = "앞에서부터 삭제", Left = 18, Top = 58, Width = 150, Height = 24, Checked = !initialFromEnd };
            chkFromEnd = new CheckBox { Text = "뒤에서부터 삭제", Left = 185, Top = 58, Width = 150, Height = 24, Checked = initialFromEnd };
            Label ex1 = new Label { Text = "앞에서 삭제 예시) 01_name.png → 1번째부터 3번째까지 삭제 → name.png", Left = 18, Top = 94, Width = 460, Height = 22 };
            Label ex2 = new Label { Text = "뒤에서 삭제 예시) name_01.png → 1번째부터 3번째까지 삭제 → name.png", Left = 18, Top = 120, Width = 460, Height = 22 };
            Button selectAll = new Button { Text = StringInputDialog.DialogSelectAllText(), Left = 18, Top = 166, Width = 96, Height = 26 };
            Button ok = new Button { Text = Localization.T("settings.ok", "OK"), Left = 322, Top = 166, Width = 78, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Localization.T("settings.cancel", "Cancel"), Left = 406, Top = 166, Width = 78, DialogResult = DialogResult.Cancel };

            selectAll.Click += delegate
            {
                selectAllRequested = true;
                if (selectAllAction != null) selectAllAction();
            };

            chkFromStart.CheckedChanged += delegate
            {
                if (updating) return;
                updating = true;
                if (chkFromStart.Checked) chkFromEnd.Checked = false;
                else if (!chkFromEnd.Checked) chkFromStart.Checked = true;
                updating = false;
            };
            chkFromEnd.CheckedChanged += delegate
            {
                if (updating) return;
                updating = true;
                if (chkFromEnd.Checked) chkFromStart.Checked = false;
                else if (!chkFromStart.Checked) chkFromEnd.Checked = true;
                updating = false;
            };

            Controls.AddRange(new Control[] { txtStart, lblMid, txtEnd, lblEnd, chkFromStart, chkFromEnd, ex1, ex2, selectAll, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { txtStart.Focus(); txtStart.SelectAll(); };
        }
    }

    internal sealed class ReplaceDialog : Form
    {
        private readonly TextBox txtFind;
        private readonly TextBox txtReplace;
        private bool selectAllRequested;

        public string FindText { get { return txtFind.Text; } }
        public string ReplaceText { get { return txtReplace.Text; } }
        public bool SelectAllRequested { get { return selectAllRequested; } }

        public ReplaceDialog()
            : this(string.Empty, string.Empty, null)
        {
        }

        public ReplaceDialog(string initialFind, string initialReplace, Action selectAllAction)
        {
            Text = "파일명 바꾸기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 165);

            Label lblFind = new Label { Text = "찾을 텍스트", Left = 12, Top = 18, Width = 90 };
            txtFind = new TextBox { Left = 105, Top = 15, Width = 310, Text = initialFind ?? string.Empty };
            Label lblReplace = new Label { Text = "바꿀 텍스트", Left = 12, Top = 56, Width = 90 };
            txtReplace = new TextBox { Left = 105, Top = 53, Width = 310, Text = initialReplace ?? string.Empty };
            Label desc = new Label { Text = "바꿀 텍스트를 비워두면 찾은 문자열을 삭제합니다.", Left = 105, Top = 82, Width = 310, Height = 20 };
            Button selectAll = new Button { Text = StringInputDialog.DialogSelectAllText(), Left = 12, Top = 118, Width = 96, Height = 26 };
            Button ok = new Button { Text = Localization.T("settings.ok", "OK"), Left = 253, Top = 118, Width = 78, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = Localization.T("settings.cancel", "Cancel"), Left = 337, Top = 118, Width = 78, DialogResult = DialogResult.Cancel };

            selectAll.Click += delegate
            {
                selectAllRequested = true;
                if (selectAllAction != null) selectAllAction();
            };

            Controls.AddRange(new Control[] { lblFind, txtFind, lblReplace, txtReplace, desc, selectAll, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate { txtFind.Focus(); txtFind.SelectAll(); };
        }
    }

    internal static class VistaFolderPicker
    {
        private const int S_OK = 0;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        [Flags]
        private enum FOS : uint
        {
            FOS_NOCHANGEDIR = 0x00000008,
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_PATHMUSTEXIST = 0x00000800
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        public static bool TryPickFolder(IWin32Window owner, string initialFolder, out string selectedPath)
        {
            selectedPath = null;
            IFileDialog dialog = null;
            IShellItem resultItem = null;

            try
            {
                dialog = (IFileDialog)new FileOpenDialog();

                FOS options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST | FOS.FOS_NOCHANGEDIR);
                dialog.SetTitle("폴더 선택");
                dialog.SetOkButtonLabel("폴더 선택");

                string initial = NormalizeInitialFolder(initialFolder);
                if (Directory.Exists(initial))
                {
                    IShellItem initialItem = null;
                    try
                    {
                        Guid shellItemGuid = typeof(IShellItem).GUID;
                        int hr = SHCreateItemFromParsingName(initial, IntPtr.Zero, ref shellItemGuid, out initialItem);
                        if (hr == S_OK && initialItem != null) dialog.SetFolder(initialItem);
                    }
                    finally
                    {
                        if (initialItem != null) Marshal.ReleaseComObject(initialItem);
                    }
                }

                IntPtr ownerHandle = owner == null ? IntPtr.Zero : owner.Handle;
                int showResult = dialog.Show(ownerHandle);
                if (showResult == ERROR_CANCELLED) return false;
                if (showResult != S_OK) Marshal.ThrowExceptionForHR(showResult);

                dialog.GetResult(out resultItem);
                IntPtr pathPtr = IntPtr.Zero;
                try
                {
                    resultItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out pathPtr);
                    selectedPath = Marshal.PtrToStringUni(pathPtr);
                }
                finally
                {
                    if (pathPtr != IntPtr.Zero) CoTaskMemFree(pathPtr);
                }

                return Directory.Exists(selectedPath);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (resultItem != null) Marshal.ReleaseComObject(resultItem);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        private static string NormalizeInitialFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string clean = path.Trim().Trim('"');
            if (Directory.Exists(clean)) return clean;

            try
            {
                string parent = Path.GetDirectoryName(clean);
                while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    parent = Path.GetDirectoryName(parent);
                }
                return parent ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal delegate bool PreviewViewerImageLoader(string fullPath, out Bitmap image, out string title, out string pathLine);
    internal delegate void PreviewViewerSelectionChanged(string fullPath);

    internal sealed class PreviewViewerForm : Form
    {
        private readonly PreviewViewerCanvas canvas;
        private List<string> paths;
        private readonly PreviewViewerImageLoader imageLoader;
        private readonly PreviewViewerSelectionChanged selectionChanged;
        private int currentIndex;
        private PreviewNavigationInputMode navigationMode;
        private int fixedZoomBasePercent;

        public PreviewViewerForm(List<string> paths, int initialIndex, PreviewViewerImageLoader imageLoader, PreviewViewerSelectionChanged selectionChanged, PreviewNavigationInputMode navigationMode, int zoomStepPercent, int fixedZoomBasePercent)
        {
            this.paths = paths == null ? new List<string>() : new List<string>(paths);
            this.imageLoader = imageLoader;
            this.selectionChanged = selectionChanged;
            this.navigationMode = navigationMode;
            this.fixedZoomBasePercent = fixedZoomBasePercent;
            currentIndex = initialIndex;

            Text = Localization.T("preview.title", "Preview");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 720);
            MinimumSize = new Size(480, 360);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowIcon = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            canvas = new PreviewViewerCanvas();
            canvas.Dock = DockStyle.Fill;
            canvas.NavigationRequested += delegate(int direction) { Navigate(direction); };
            Controls.Add(canvas);

            SetInputSettings(navigationMode, zoomStepPercent, fixedZoomBasePercent);
            LoadIndex(currentIndex, false);
        }

        public void SetInputSettings(PreviewNavigationInputMode navigationMode, int zoomStepPercent, int fixedZoomBasePercent)
        {
            this.navigationMode = navigationMode;
            this.fixedZoomBasePercent = ClampFixedZoomBase(fixedZoomBasePercent);
            if (canvas == null) return;
            canvas.SetInputSettings(navigationMode, zoomStepPercent, this.fixedZoomBasePercent);
        }

        public void SetDisplaySettings(bool useCanvasColor, Color canvasColor, bool useAlphaColor, Color alphaColor)
        {
            if (canvas == null) return;
            canvas.SetDisplaySettings(useCanvasColor, canvasColor, useAlphaColor, alphaColor);
        }

        private static int ClampFixedZoomBase(int value)
        {
            if (value < 50) return 50;
            if (value > 200) return 200;
            return value;
        }

        public void LoadPaths(List<string> newPaths, int initialIndex, bool notifySelection)
        {
            paths = newPaths == null ? new List<string>() : new List<string>(newPaths);
            if (paths.Count == 0) return;
            if (initialIndex < 0) initialIndex = 0;
            if (initialIndex >= paths.Count) initialIndex = paths.Count - 1;
            LoadIndex(initialIndex, notifySelection);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            canvas.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            if (keyData == Keys.NumPad0)
            {
                canvas.ResetViewToInitial();
                return true;
            }

            int fixedPercent = GetFixedZoomPercent(keyData);
            if (fixedPercent > 0)
            {
                canvas.SetAbsoluteZoomPercent(fixedPercent);
                return true;
            }

            if (keyData == Keys.Add)
            {
                canvas.ZoomFromKeyboard(true);
                return true;
            }

            if (keyData == Keys.Subtract)
            {
                canvas.ZoomFromKeyboard(false);
                return true;
            }

            if (navigationMode == PreviewNavigationInputMode.ArrowKeys)
            {
                if (keyData == Keys.Left)
                {
                    Navigate(-1);
                    return true;
                }

                if (keyData == Keys.Right)
                {
                    Navigate(1);
                    return true;
                }
            }
            else if (navigationMode == PreviewNavigationInputMode.PageKeys)
            {
                if (keyData == Keys.PageUp)
                {
                    Navigate(-1);
                    return true;
                }

                if (keyData == Keys.PageDown)
                {
                    Navigate(1);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private int GetFixedZoomPercent(Keys keyData)
        {
            int multiplier = 0;
            if (keyData == Keys.NumPad1) multiplier = 1;
            else if (keyData == Keys.NumPad2) multiplier = 2;
            else if (keyData == Keys.NumPad3) multiplier = 3;
            else if (keyData == Keys.NumPad4) multiplier = 4;
            else if (keyData == Keys.NumPad5) multiplier = 5;
            else if (keyData == Keys.NumPad6) multiplier = 6;
            else if (keyData == Keys.NumPad7) multiplier = 7;
            else if (keyData == Keys.NumPad8) multiplier = 8;
            else if (keyData == Keys.NumPad9) multiplier = 9;
            if (multiplier <= 0) return 0;
            return 100 + ((multiplier - 1) * ClampFixedZoomBase(fixedZoomBasePercent));
        }

        private void Navigate(int direction)
        {
            if (direction == 0 || paths.Count <= 1 || imageLoader == null) return;

            int stepDirection = direction > 0 ? 1 : -1;
            for (int offset = 1; offset <= paths.Count; offset++)
            {
                int nextIndex = currentIndex + (offset * stepDirection);
                while (nextIndex < 0) nextIndex += paths.Count;
                nextIndex = nextIndex % paths.Count;

                if (LoadIndex(nextIndex, true)) return;
            }
        }

        private bool LoadIndex(int index, bool notifySelection)
        {
            if (index < 0 || index >= paths.Count || imageLoader == null) return false;

            Bitmap image;
            string title;
            string pathLine;
            if (!imageLoader(paths[index], out image, out title, out pathLine)) return false;

            currentIndex = index;
            Text = Localization.T("preview.title_with_file", "Preview : {0}", string.IsNullOrEmpty(title) ? Path.GetFileName(paths[index]) : title);
            canvas.SetImage(image, pathLine);

            if (notifySelection && selectionChanged != null) selectionChanged(paths[index]);
            return true;
        }
    }

    internal sealed class PreviewViewerCanvas : Control
    {
        private Bitmap image;
        private string pathLine;
        private double scale = 1.0;
        private PointF imageOffset = new PointF(0, 0);
        private bool viewInitialized;
        private bool autoFitToWindow = true;
        private bool dragging;
        private Point dragStart;
        private PointF dragStartOffset;
        private PreviewNavigationInputMode navigationMode = PreviewNavigationInputMode.ArrowKeys;
        private double zoomFactor = 1.30;
        private int fixedZoomBasePercent = 100;
        private bool useCanvasColor = true;
        private Color canvasColor = Color.Black;
        private bool useAlphaColor = false;
        private Color alphaColor = Color.White;

        public event Action<int> NavigationRequested;

        public PreviewViewerCanvas()
        {
            pathLine = string.Empty;
            DoubleBuffered = true;
            BackColor = Color.Black;
            TabStop = true;
        }

        public void SetInputSettings(PreviewNavigationInputMode navigationMode, int zoomStepPercent, int fixedZoomBasePercent)
        {
            this.navigationMode = navigationMode;
            if (zoomStepPercent < 5) zoomStepPercent = 5;
            if (zoomStepPercent > 50) zoomStepPercent = 50;
            if (fixedZoomBasePercent < 50) fixedZoomBasePercent = 50;
            if (fixedZoomBasePercent > 200) fixedZoomBasePercent = 200;
            this.fixedZoomBasePercent = fixedZoomBasePercent;
            zoomFactor = 1.0 + (zoomStepPercent / 100.0);
            Invalidate();
        }

        public void SetDisplaySettings(bool useCanvasColor, Color canvasColor, bool useAlphaColor, Color alphaColor)
        {
            this.useCanvasColor = useCanvasColor;
            this.canvasColor = canvasColor;
            this.useAlphaColor = useAlphaColor;
            this.alphaColor = alphaColor;
            Invalidate();
        }

        public void SetImage(Bitmap newImage, string newPathLine)
        {
            if (image != null) image.Dispose();
            image = newImage;
            pathLine = newPathLine ?? string.Empty;
            dragging = false;
            autoFitToWindow = true;
            viewInitialized = false;
            Cursor = Cursors.Default;
            ResetView();
            Invalidate();
        }

        public void ZoomFromKeyboard(bool zoomIn)
        {
            ZoomAtCenter(zoomIn ? zoomFactor : 1.0 / zoomFactor);
        }

        public void ResetViewToInitial()
        {
            dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
            autoFitToWindow = true;
            viewInitialized = false;
            ResetView();
            Invalidate();
        }

        public void SetAbsoluteZoomPercent(int percent)
        {
            if (percent < 1) return;
            SetScaleAtCenter(percent / 100.0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) image.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (autoFitToWindow)
            {
                viewInitialized = false;
                ResetView();
            }
            else if (!viewInitialized) ResetView();
            else ClampImageOffset();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Middle)
            {
                ResetViewToInitial();
                return;
            }

            if (e.Button != MouseButtons.Left || !CanPan()) return;

            autoFitToWindow = false;
            dragging = true;
            dragStart = e.Location;
            dragStartOffset = imageOffset;
            Capture = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;

            imageOffset = new PointF(dragStartOffset.X + (e.X - dragStart.X), dragStartOffset.Y + (e.Y - dragStart.Y));
            ClampImageOffset();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Focus();

            bool ctrlDown = (ModifierKeys & Keys.Control) == Keys.Control;

            if (ctrlDown)
            {
                ZoomAtCenter(e.Delta > 0 ? zoomFactor : 1.0 / zoomFactor);
                return;
            }

            if (navigationMode == PreviewNavigationInputMode.MouseWheel && !ctrlDown && NavigationRequested != null)
            {
                int direction = e.Delta > 0 ? -1 : 1;
                NavigationRequested(direction);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (useCanvasColor) e.Graphics.Clear(canvasColor);
            else DrawChecker(e.Graphics, ClientRectangle);

            if (image != null && image.Width > 0 && image.Height > 0)
            {
                if (!viewInitialized) ResetView();

                Rectangle area = GetImageArea();
                if (area.Width > 0 && area.Height > 0)
                {
                    int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
                    Rectangle canvasRect = new Rectangle((int)Math.Round(imageOffset.X), (int)Math.Round(imageOffset.Y), drawW, drawH);

                    if (useAlphaColor)
                    {
                        using (Brush alphaBrush = new SolidBrush(alphaColor))
                        {
                            e.Graphics.FillRectangle(alphaBrush, canvasRect);
                        }
                    }
                    else
                    {
                        DrawChecker(e.Graphics, canvasRect);
                    }

                    using (Pen border = new Pen(Color.DimGray))
                    {
                        e.Graphics.DrawRectangle(border, canvasRect.Left, canvasRect.Top, canvasRect.Width - 1, canvasRect.Height - 1);
                    }

                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    e.Graphics.DrawImage(image, canvasRect);
                }
            }

            DrawOverlayText(e.Graphics);
        }

        private void DrawOverlayText(Graphics g)
        {
            using (Font textFont = new Font(Font.FontFamily, 9.0f, FontStyle.Regular))
            {
                Rectangle pathRect = new Rectangle(8, 6, Math.Max(1, ClientSize.Width - 16), 20);
                TextRenderer.DrawText(g, pathLine, textFont, pathRect, Color.Chartreuse, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

                string line1 = "이전/다음: " + GetNavigationHelp() + "    확대/축소: Ctrl + 휠, Num + / Num -";
                string line2 = "창 맞춤: 휠 클릭, Num 0    고정: Num 1-9 (100-" + (100 + fixedZoomBasePercent * 8) + "%)";
                Size line1Size = TextRenderer.MeasureText(line1, textFont);
                Size line2Size = TextRenderer.MeasureText(line2, textFont);
                int textWidth = Math.Max(line1Size.Width, line2Size.Width) + 4;
                int textHeight = line1Size.Height + line2Size.Height + 2;
                Rectangle helpRect = new Rectangle(
                    Math.Max(8, ClientSize.Width - textWidth - 12),
                    Math.Max(8, ClientSize.Height - textHeight - 8),
                    Math.Min(textWidth, Math.Max(1, ClientSize.Width - 16)),
                    textHeight);

                Rectangle line1Rect = new Rectangle(helpRect.Left, helpRect.Top, helpRect.Width, line1Size.Height + 1);
                Rectangle line2Rect = new Rectangle(helpRect.Left, helpRect.Top + line1Size.Height + 1, helpRect.Width, line2Size.Height + 1);
                TextRenderer.DrawText(g, line1, textFont, line1Rect, Color.LightGray, TextFormatFlags.NoPrefix | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, line2, textFont, line2Rect, Color.LightGray, TextFormatFlags.NoPrefix | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        private string GetNavigationHelp()
        {
            if (navigationMode == PreviewNavigationInputMode.MouseWheel) return "휠";
            if (navigationMode == PreviewNavigationInputMode.PageKeys) return "PgUp / PgDn";
            return "← / →";
        }

        private void ResetView()
        {
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                viewInitialized = true;
                scale = 1.0;
                imageOffset = new PointF(0, 0);
                return;
            }

            Rectangle area = GetImageArea();
            if (area.Width <= 0 || area.Height <= 0)
            {
                viewInitialized = true;
                return;
            }

            scale = GetInitialScale(area.Width, area.Height, image.Width, image.Height);
            CenterImageInArea(area);
            viewInitialized = true;
        }

        private Rectangle GetImageArea()
        {
            Rectangle area = ClientRectangle;
            area.Y += 28;
            area.Height -= 58;
            area.Inflate(-12, -12);
            return area;
        }

        private void CenterImageInArea(Rectangle area)
        {
            int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
            int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
            imageOffset = new PointF(area.Left + (area.Width - drawW) / 2.0f, area.Top + (area.Height - drawH) / 2.0f);
        }

        private void ZoomAtCenter(double factor)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0) return;
            SetScaleAtCenter(scale * factor);
        }

        private void SetScaleAtCenter(double newScale)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0) return;

            Rectangle area = GetImageArea();
            if (area.Width <= 0 || area.Height <= 0) return;

            if (!viewInitialized) ResetView();

            autoFitToWindow = false;
            double oldScale = scale;
            if (newScale < 0.05) newScale = 0.05;
            if (newScale > 32.0) newScale = 32.0;
            if (Math.Abs(newScale - oldScale) < 0.0001) return;

            PointF center = new PointF(area.Left + area.Width / 2.0f, area.Top + area.Height / 2.0f);
            double imageCenterX = (center.X - imageOffset.X) / oldScale;
            double imageCenterY = (center.Y - imageOffset.Y) / oldScale;

            scale = newScale;
            imageOffset = new PointF((float)(center.X - imageCenterX * scale), (float)(center.Y - imageCenterY * scale));
            ClampImageOffset();
            viewInitialized = true;
            Invalidate();
        }

        private bool CanPan()
        {
            if (image == null) return false;
            Rectangle area = GetImageArea();
            int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
            int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
            return drawW > area.Width || drawH > area.Height;
        }

        private void ClampImageOffset()
        {
            if (image == null) return;

            Rectangle area = GetImageArea();
            if (area.Width <= 0 || area.Height <= 0) return;

            int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
            int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
            float x = imageOffset.X;
            float y = imageOffset.Y;

            if (drawW <= area.Width) x = area.Left + (area.Width - drawW) / 2.0f;
            else
            {
                float minX = area.Right - drawW;
                float maxX = area.Left;
                if (x < minX) x = minX;
                if (x > maxX) x = maxX;
            }

            if (drawH <= area.Height) y = area.Top + (area.Height - drawH) / 2.0f;
            else
            {
                float minY = area.Bottom - drawH;
                float maxY = area.Top;
                if (y < minY) y = minY;
                if (y > maxY) y = maxY;
            }

            imageOffset = new PointF(x, y);
        }

        private static double GetInitialScale(int areaWidth, int areaHeight, int imageWidth, int imageHeight)
        {
            if (areaWidth <= 0 || areaHeight <= 0 || imageWidth <= 0 || imageHeight <= 0) return 1.0;
            double fitScale = Math.Min((double)areaWidth / imageWidth, (double)areaHeight / imageHeight);
            if (fitScale <= 0) return 1.0;
            return fitScale;
        }

        private static void DrawChecker(Graphics g, Rectangle rect)
        {
            const int size = 10;
            using (Brush b1 = new SolidBrush(Color.White))
            using (Brush b2 = new SolidBrush(Color.Gainsboro))
            {
                g.FillRectangle(b1, rect);
                Region oldClip = (Region)g.Clip.Clone();
                g.SetClip(rect, CombineMode.Replace);
                try
                {
                    for (int y = rect.Top; y < rect.Bottom; y += size)
                    {
                        for (int x = rect.Left; x < rect.Right; x += size)
                        {
                            bool alt = ((x / size) + (y / size)) % 2 == 0;
                            if (alt) g.FillRectangle(b2, x, y, size, size);
                        }
                    }
                }
                finally
                {
                    g.Clip = oldClip;
                    oldClip.Dispose();
                }
            }
        }
    }


    internal sealed class ColoredKeywordButton : Button
    {
        private bool pressed;

        public ColoredKeywordButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Standard;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            if (pressed)
            {
                pressed = false;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (pressed)
            {
                pressed = false;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            Rectangle rect = ClientRectangle;
            ButtonState state = !Enabled ? ButtonState.Inactive : (pressed ? ButtonState.Pushed : ButtonState.Normal);
            ControlPaint.DrawButton(g, rect, state);

            Rectangle textRect = new Rectangle(rect.Left + 6, rect.Top + 5, Math.Max(1, rect.Width - 12), Math.Max(1, rect.Height - 10));
            if (pressed) textRect.Offset(1, 1);
            DrawKeywordText(g, textRect, Text ?? string.Empty, Enabled);
        }

        private void DrawKeywordText(Graphics g, Rectangle area, string text, bool enabled)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] rawLines = normalized.Split(new char[] { '\n' });
            List<string> lines = new List<string>();
            for (int i = 0; i < rawLines.Length; i++)
            {
                if (rawLines[i].Length == 0) continue;
                lines.Add(rawLines[i]);
            }
            if (lines.Count == 0) return;

            using (Font regularFont = new Font(Font, FontStyle.Regular))
            using (Font boldFont = new Font(Font, FontStyle.Bold))
            {
                int lineHeight = Math.Max(regularFont.Height, boldFont.Height) + 3;
                int totalHeight = lineHeight * lines.Count;
                int y = area.Top + Math.Max(0, (area.Height - totalHeight) / 2);
                for (int i = 0; i < lines.Count; i++)
                {
                    DrawKeywordLine(g, lines[i], area.Left, y, area.Width, lineHeight, regularFont, boldFont, enabled);
                    y += lineHeight;
                }
            }
        }

        private void DrawKeywordLine(Graphics g, string line, int x, int y, int width, int height, Font regularFont, Font boldFont, bool enabled)
        {
            List<TextSegment> segments = BuildSegments(line, enabled);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int totalWidth = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                Font font = segments[i].Bold ? boldFont : regularFont;
                Size size = TextRenderer.MeasureText(g, segments[i].Text.Length == 0 ? " " : segments[i].Text, font, new Size(int.MaxValue, int.MaxValue), flags);
                segments[i] = new TextSegment(segments[i].Text, segments[i].Color, segments[i].Bold, size.Width);
                totalWidth += size.Width;
            }

            int drawX = x + Math.Max(0, (width - totalWidth) / 2);
            for (int i = 0; i < segments.Count; i++)
            {
                Font font = segments[i].Bold ? boldFont : regularFont;
                Color color = enabled ? segments[i].Color : SystemColors.GrayText;
                Rectangle segRect = new Rectangle(drawX, y, Math.Max(1, segments[i].Width), height);
                TextRenderer.DrawText(g, segments[i].Text, font, segRect, color, flags);
                drawX += segments[i].Width;
            }
        }

        private List<TextSegment> BuildSegments(string line, bool enabled)
        {
            List<TextSegment> segments = new List<TextSegment>();
            int i = 0;
            while (i < line.Length)
            {
                string keyword;
                Color color;
                if (TryMatchKeyword(line, i, out keyword, out color))
                {
                    segments.Add(new TextSegment(keyword, color, true, 0));
                    i += keyword.Length;
                    continue;
                }

                int start = i;
                i++;
                while (i < line.Length)
                {
                    string tmp;
                    Color tmpColor;
                    if (TryMatchKeyword(line, i, out tmp, out tmpColor)) break;
                    i++;
                }
                segments.Add(new TextSegment(line.Substring(start, i - start), SystemColors.ControlText, false, 0));
            }
            return segments;
        }

        private static bool TryMatchKeyword(string text, int index, out string keyword, out Color color)
        {
            if (Matches(text, index, "lowend"))
            {
                keyword = text.Substring(index, 6);
                color = Color.Purple;
                return true;
            }
            if (Matches(text, index, "sprite"))
            {
                keyword = text.Substring(index, 6);
                color = Color.RoyalBlue;
                return true;
            }
            if (Matches(text, index, "PNG"))
            {
                keyword = text.Substring(index, 3);
                color = Color.ForestGreen;
                return true;
            }

            keyword = string.Empty;
            color = SystemColors.ControlText;
            return false;
        }

        private static bool Matches(string text, int index, string keyword)
        {
            if (string.IsNullOrEmpty(text) || index < 0 || index + keyword.Length > text.Length) return false;
            return string.Compare(text, index, keyword, 0, keyword.Length, true) == 0;
        }

        private struct TextSegment
        {
            public string Text;
            public Color Color;
            public bool Bold;
            public int Width;

            public TextSegment(string text, Color color, bool bold, int width)
            {
                Text = text ?? string.Empty;
                Color = color;
                Bold = bold;
                Width = width;
            }
        }
    }

    internal sealed class LightBorderPanel : Panel
    {
        public Color BorderColor { get; set; }

        public LightBorderPanel()
        {
            BorderColor = Color.FromArgb(220, 220, 220);
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
            }
        }
    }

    internal sealed class DropHintControl : Control
    {
        private string primaryText = string.Empty;
        private string secondaryText = string.Empty;

        public string PrimaryText
        {
            get { return primaryText; }
            set { primaryText = value ?? string.Empty; Invalidate(); }
        }

        public string SecondaryText
        {
            get { return secondaryText; }
            set { secondaryText = value ?? string.Empty; Invalidate(); }
        }

        public DropHintControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Control;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            Font baseFont = Font ?? Control.DefaultFont;
            using (Font primaryFont = new Font(baseFont.FontFamily, baseFont.Size, FontStyle.Bold))
            {
                int firstHeight = Math.Max(16, TextRenderer.MeasureText(PrimaryText, primaryFont).Height);
                int secondHeight = Math.Max(15, TextRenderer.MeasureText(SecondaryText, baseFont).Height);
                int gap = 2;
                int totalHeight = firstHeight + gap + secondHeight;
                int y = Math.Max(0, (ClientSize.Height - totalHeight) / 2);

                Rectangle firstRect = new Rectangle(0, y, ClientSize.Width, firstHeight);
                TextRenderer.DrawText(e.Graphics, PrimaryText, primaryFont, firstRect, Color.Firebrick, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                Rectangle secondRect = new Rectangle(0, y + firstHeight + gap, ClientSize.Width, secondHeight);
                TextRenderer.DrawText(e.Graphics, SecondaryText, baseFont, secondRect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class CenteredBorderlessGroupBox : GroupBox
    {
        public CenteredBorderlessGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = SystemColors.Control;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back = Parent == null ? BackColor : Parent.BackColor;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            if (!string.IsNullOrEmpty(Text))
            {
                Rectangle textRect = new Rectangle(0, 4, ClientSize.Width, 32);
                TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal sealed class CenteredCheckBox : CheckBox
    {
        public bool CenterContent { get; set; }

        public CenteredCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Control;
            Cursor = Cursors.Default;
            AutoSize = false;
            Height = 24;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back = Parent == null ? BackColor : Parent.BackColor;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            const int boxSize = 13;
            const int gap = 5;
            string text = Text ?? string.Empty;
            bool hasText = text.Length > 0;
            Size textSize = hasText
                ? TextRenderer.MeasureText(text, Font, new Size(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix)
                : Size.Empty;
            int totalWidth = boxSize + (hasText ? gap + textSize.Width : 0);
            int startX = CenterContent ? Math.Max(0, (ClientSize.Width - totalWidth) / 2) : 0;
            int boxY = Math.Max(0, (ClientSize.Height - boxSize) / 2);
            Rectangle boxRect = new Rectangle(startX, boxY, boxSize, boxSize);
            ButtonState state = Checked ? ButtonState.Checked : ButtonState.Normal;
            if (!Enabled) state |= ButtonState.Inactive;
            ControlPaint.DrawCheckBox(e.Graphics, boxRect, state);

            if (hasText)
            {
                Rectangle textRect = new Rectangle(startX + boxSize + gap, 0, Math.Max(1, ClientSize.Width - (startX + boxSize + gap)), ClientSize.Height);
                Color textColor = Enabled ? ForeColor : SystemColors.GrayText;
                TextRenderer.DrawText(e.Graphics, text, Font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (Focused && ShowFocusCues)
            {
                Rectangle focusRect = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                ControlPaint.DrawFocusRectangle(e.Graphics, focusRect);
            }
        }
    }

    internal struct PreviewInfoLine
    {
        public readonly string Text;
        public readonly Color Color;
        public readonly bool Bold;
        public readonly int BoldPrefixLength;
        public readonly bool IsLink;
        public readonly bool FrameHighlight;

        public PreviewInfoLine(string text, Color color)
            : this(text, color, false, 0, false, false)
        {
        }

        public PreviewInfoLine(string text, Color color, bool bold)
            : this(text, color, bold, 0, false, false)
        {
        }

        public PreviewInfoLine(string text, Color color, int boldPrefixLength)
            : this(text, color, false, boldPrefixLength, false, false)
        {
        }

        public static PreviewInfoLine Link(string text)
        {
            return new PreviewInfoLine(text, Color.RoyalBlue, false, 0, true, false);
        }

        public static PreviewInfoLine Frame(string text, bool highlight)
        {
            return new PreviewInfoLine(text, SystemColors.ControlText, false, GetFramePrefixLength(text), false, highlight);
        }

        private static int GetFramePrefixLength(string text)
        {
            string label = Localization.T("label.frame", "Frame");
            if (!string.IsNullOrEmpty(text) && text.StartsWith(label, StringComparison.CurrentCultureIgnoreCase)) return label.Length;
            return 0;
        }

        private PreviewInfoLine(string text, Color color, bool bold, int boldPrefixLength, bool isLink, bool frameHighlight)
        {
            Text = text ?? string.Empty;
            Color = color;
            Bold = bold;
            BoldPrefixLength = Math.Max(0, boldPrefixLength);
            IsLink = isLink;
            FrameHighlight = frameHighlight;
        }
    }

    internal sealed class PreviewInfoPanel : Control
    {
        private readonly List<PreviewInfoLine> lines = new List<PreviewInfoLine>();
        private readonly List<Rectangle> linkRects = new List<Rectangle>();
        private readonly ToolTip linkToolTip;
        private bool linkToolTipVisible;

        public event EventHandler HeaderLinkClicked;

        public PreviewInfoPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Control;
            linkToolTip = new ToolTip();
            linkToolTip.AutoPopDelay = 30000;
            linkToolTip.InitialDelay = 0;
            linkToolTip.ReshowDelay = 0;
            linkToolTip.ShowAlways = true;
        }

        public void SetPlainText(string text, Color color)
        {
            HideLinkToolTip();
            lines.Clear();
            linkRects.Clear();
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] split = normalized.Length == 0 ? new string[0] : normalized.Split(new char[] { '\n' });
            foreach (string line in split)
            {
                lines.Add(new PreviewInfoLine(line, color));
            }
            Invalidate();
        }

        public void SetColoredLines(IEnumerable<PreviewInfoLine> newLines)
        {
            HideLinkToolTip();
            lines.Clear();
            linkRects.Clear();
            if (newLines != null)
            {
                foreach (PreviewInfoLine line in newLines)
                {
                    lines.Add(line);
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            linkRects.Clear();
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            if (lines.Count == 0) return;

            int x = 8;
            int y = 8;
            int width = Math.Max(1, ClientSize.Width - 16);
            TextFormatFlags centeredFlags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Text ?? string.Empty;
                if (lines[i].IsLink)
                {
                    int lineHeight = DrawLinkLine(e.Graphics, lines[i], x, y, width);
                    y += lineHeight;
                }
                else if (lines[i].FrameHighlight)
                {
                    int lineHeight = DrawFrameHighlightLine(e.Graphics, lines[i], x, y, width);
                    y += lineHeight;
                }
                else if (!lines[i].Bold && lines[i].BoldPrefixLength > 0 && lines[i].BoldPrefixLength < text.Length)
                {
                    int lineHeight = DrawMixedPrefixLine(e.Graphics, lines[i], x, y, width);
                    y += lineHeight;
                }
                else
                {
                    using (Font lineFont = lines[i].Bold ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular))
                    {
                        Size proposed = new Size(width, Math.Max(lineFont.Height + 6, ClientSize.Height));
                        Size measured = TextRenderer.MeasureText(e.Graphics, text.Length == 0 ? " " : text, lineFont, proposed, centeredFlags);
                        int lineHeight = Math.Max(lineFont.Height + 4, measured.Height + 2);
                        Rectangle rect = new Rectangle(x, y, width, lineHeight);
                        TextRenderer.DrawText(e.Graphics, text, lineFont, rect, lines[i].Color, centeredFlags);
                        y += lineHeight;
                    }
                }
                if (y >= ClientSize.Height - 2) break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overLink = IsOverLink(e.Location);
            Cursor = overLink ? Cursors.Hand : Cursors.Default;
            if (overLink)
            {
                if (!linkToolTipVisible)
                {
                    linkToolTip.Show(Localization.T("tooltip.metadata_view", "Metadata view"), this, e.X + 12, e.Y + 18, 30000);
                    linkToolTipVisible = true;
                }
            }
            else
            {
                HideLinkToolTip();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            HideLinkToolTip();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || !IsOverLink(e.Location)) return;
            EventHandler handler = HeaderLinkClicked;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void HideLinkToolTip()
        {
            if (!linkToolTipVisible) return;
            linkToolTip.Hide(this);
            linkToolTipVisible = false;
        }

        private bool IsOverLink(Point point)
        {
            for (int i = 0; i < linkRects.Count; i++)
            {
                if (linkRects[i].Contains(point)) return true;
            }
            return false;
        }

        private int DrawLinkLine(Graphics graphics, PreviewInfoLine line, int x, int y, int width)
        {
            string text = line.Text ?? string.Empty;
            string linkText = text;
            string iconText = string.Empty;
            int iconIndex = text.IndexOf(" [", StringComparison.Ordinal);
            if (iconIndex >= 0)
            {
                linkText = text.Substring(0, iconIndex);
                iconText = text.Substring(iconIndex);
            }

            using (Font linkFont = new Font(Font, FontStyle.Underline))
            using (Font iconFont = new Font(Font, FontStyle.Regular))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Size linkSize = TextRenderer.MeasureText(graphics, linkText.Length == 0 ? " " : linkText, linkFont, new Size(int.MaxValue, int.MaxValue), flags);
                Size iconSize = TextRenderer.MeasureText(graphics, iconText.Length == 0 ? " " : iconText, iconFont, new Size(int.MaxValue, int.MaxValue), flags);
                int lineHeight = Math.Max(Math.Max(linkFont.Height, iconFont.Height) + 4, Math.Max(linkSize.Height, iconSize.Height) + 2);
                int drawX = x;
                Rectangle linkRect = new Rectangle(drawX, y, Math.Max(1, linkSize.Width), lineHeight);
                TextRenderer.DrawText(graphics, linkText, linkFont, linkRect, line.Color, flags);
                drawX += linkSize.Width;
                if (iconText.Length > 0)
                {
                    Rectangle iconRect = new Rectangle(drawX, y, Math.Max(1, iconSize.Width), lineHeight);
                    TextRenderer.DrawText(graphics, iconText, iconFont, iconRect, line.Color, flags);
                }
                linkRects.Add(new Rectangle(x, y, Math.Max(1, linkSize.Width + (iconText.Length > 0 ? iconSize.Width : 0)), lineHeight));
                return lineHeight;
            }
        }

        private int DrawMixedPrefixLine(Graphics graphics, PreviewInfoLine line, int x, int y, int width)
        {
            string text = line.Text ?? string.Empty;
            int prefixLength = Math.Min(Math.Max(0, line.BoldPrefixLength), text.Length);

            string payloadLabel = Localization.T("label.payload", "Payload");
            string bppLabel = Localization.T("label.bpp", "BPP");
            string bppMarker = " / " + bppLabel + " ";
            if (text.StartsWith(payloadLabel + " ", StringComparison.CurrentCultureIgnoreCase) && text.IndexOf(bppMarker, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                return DrawPayloadBppLine(graphics, text, payloadLabel, bppLabel, x, y, width, line.Color);
            }

            string prefix = text.Substring(0, prefixLength);
            string rest = text.Substring(prefixLength);
            using (Font boldFont = new Font(Font, FontStyle.Bold))
            using (Font regularFont = new Font(Font, FontStyle.Regular))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Size prefixSize = TextRenderer.MeasureText(graphics, prefix, boldFont, new Size(int.MaxValue, int.MaxValue), flags);
                Size restSize = TextRenderer.MeasureText(graphics, rest.Length == 0 ? " " : rest, regularFont, new Size(int.MaxValue, int.MaxValue), flags);
                int totalWidth = prefixSize.Width + restSize.Width;
                int lineHeight = Math.Max(Math.Max(boldFont.Height, regularFont.Height) + 4, Math.Max(prefixSize.Height, restSize.Height) + 2);

                int drawX = x;
                Rectangle prefixRect = new Rectangle(drawX, y, prefixSize.Width, lineHeight);
                Rectangle restRect = new Rectangle(drawX + prefixSize.Width, y, Math.Max(1, width - (drawX - x) - prefixSize.Width), lineHeight);
                TextRenderer.DrawText(graphics, prefix, boldFont, prefixRect, line.Color, flags);
                TextRenderer.DrawText(graphics, rest, regularFont, restRect, line.Color, flags);
                return lineHeight;
            }
        }

        private int DrawFrameHighlightLine(Graphics graphics, PreviewInfoLine line, int x, int y, int width)
        {
            string text = line.Text ?? string.Empty;
            string label = Localization.T("label.frame", "Frame");
            int colonIndex = text.IndexOf(':');
            int starIndex = text.LastIndexOf('*');
            if (colonIndex < 0 || starIndex < 0 || starIndex >= text.Length - 1) return DrawMixedPrefixLine(graphics, line, x, y, width);

            string labelPart = text.Substring(0, colonIndex);
            string middlePart = text.Substring(colonIndex, starIndex - colonIndex);
            string frameCountPart = text.Substring(starIndex);

            using (Font boldFont = new Font(Font, FontStyle.Bold))
            using (Font regularFont = new Font(Font, FontStyle.Regular))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                string[] segments = new string[] { labelPart, middlePart, frameCountPart };
                Font[] fonts = new Font[] { boldFont, regularFont, regularFont };
                Color[] colors = new Color[] { Color.Firebrick, SystemColors.ControlText, Color.Firebrick };
                int lineHeight = Math.Max(boldFont.Height, regularFont.Height) + 4;
                int drawX = x;
                for (int i = 0; i < segments.Length; i++)
                {
                    Size size = TextRenderer.MeasureText(graphics, segments[i].Length == 0 ? " " : segments[i], fonts[i], new Size(int.MaxValue, int.MaxValue), flags);
                    lineHeight = Math.Max(lineHeight, size.Height + 2);
                    Rectangle rect = new Rectangle(drawX, y, Math.Max(1, size.Width), lineHeight);
                    TextRenderer.DrawText(graphics, segments[i], fonts[i], rect, colors[i], flags);
                    drawX += size.Width;
                }
                return lineHeight;
            }
        }

        private int DrawPayloadBppLine(Graphics graphics, string text, string payloadLabel, string bppLabel, int x, int y, int width, Color color)
        {
            string bppMarker = " / " + bppLabel + " ";
            int markerIndex = text.IndexOf(bppMarker, StringComparison.CurrentCultureIgnoreCase);
            if (markerIndex < 0) return DrawSingleLine(graphics, text, x, y, width, color, false);

            string payloadRest = text.Substring(payloadLabel.Length, markerIndex - payloadLabel.Length) + " / ";
            string bppRest = text.Substring(markerIndex + (" / " + bppLabel).Length);

            using (Font boldFont = new Font(Font, FontStyle.Bold))
            using (Font regularFont = new Font(Font, FontStyle.Regular))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                string[] segments = new string[] { payloadLabel, payloadRest, bppLabel, bppRest };
                Font[] fonts = new Font[] { boldFont, regularFont, boldFont, regularFont };
                Size[] sizes = new Size[segments.Length];
                int totalWidth = 0;
                int lineHeight = Math.Max(boldFont.Height, regularFont.Height) + 4;

                for (int i = 0; i < segments.Length; i++)
                {
                    sizes[i] = TextRenderer.MeasureText(graphics, segments[i].Length == 0 ? " " : segments[i], fonts[i], new Size(int.MaxValue, int.MaxValue), flags);
                    totalWidth += sizes[i].Width;
                    lineHeight = Math.Max(lineHeight, sizes[i].Height + 2);
                }

                int drawX = x;
                for (int i = 0; i < segments.Length; i++)
                {
                    Rectangle rect = new Rectangle(drawX, y, Math.Max(1, sizes[i].Width), lineHeight);
                    TextRenderer.DrawText(graphics, segments[i], fonts[i], rect, color, flags);
                    drawX += sizes[i].Width;
                }
                return lineHeight;
            }
        }

        private int DrawSingleLine(Graphics graphics, string text, int x, int y, int width, Color color, bool bold)
        {
            using (Font lineFont = new Font(Font, bold ? FontStyle.Bold : FontStyle.Regular))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Size textSize = TextRenderer.MeasureText(graphics, text.Length == 0 ? " " : text, lineFont, new Size(int.MaxValue, int.MaxValue), flags);
                int lineHeight = Math.Max(lineFont.Height + 4, textSize.Height + 2);
                int drawX = x;
                Rectangle rect = new Rectangle(drawX, y, Math.Max(1, width - (drawX - x)), lineHeight);
                TextRenderer.DrawText(graphics, text, lineFont, rect, color, flags);
                return lineHeight;
            }
        }
    }

    internal sealed class PngMetadataInfo
    {
        private static readonly byte[] PngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static string CreateMetadataText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;
                byte[] bytes = File.ReadAllBytes(path);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("File type : PNG");
                sb.AppendLine();

                if (!HasPngSignature(bytes))
                {
                    sb.AppendLine("Metadata :");
                    sb.AppendLine("Signature : invalid PNG signature");
                    sb.AppendLine("File size : " + FormatBytes(bytes.Length));
                    return sb.ToString().TrimEnd();
                }

                PngIhdr ihdr = ReadIhdr(bytes);
                sb.AppendLine("Metadata :");
                sb.AppendLine("0x00 Signature : " + FormatHexBytes(bytes, 0, 8));
                if (ihdr.Valid)
                {
                    sb.AppendLine("IHDR Width : " + ihdr.Width);
                    sb.AppendLine("IHDR Height : " + ihdr.Height);
                    sb.AppendLine("IHDR Bit depth : " + ihdr.BitDepth);
                    sb.AppendLine("IHDR Color type : " + ihdr.ColorType + " (" + ColorTypeName(ihdr.ColorType) + ")");
                    sb.AppendLine("IHDR Compression : " + ihdr.Compression);
                    sb.AppendLine("IHDR Filter : " + ihdr.Filter);
                    sb.AppendLine("IHDR Interlace : " + ihdr.Interlace + " (" + InterlaceName(ihdr.Interlace) + ")");
                }

                sb.AppendLine();
                sb.AppendLine("Derived :");
                sb.AppendLine("File size : " + FormatBytes(bytes.Length));
                if (ihdr.Valid)
                {
                    sb.AppendLine("Size : " + ihdr.Width + " * " + ihdr.Height + " px");
                    sb.AppendLine("Color format : " + ColorTypeName(ihdr.ColorType));
                    sb.AppendLine("Channels : " + ChannelCount(ihdr.ColorType));
                    int bitsPerPixel = ChannelCount(ihdr.ColorType) * ihdr.BitDepth;
                    if (bitsPerPixel > 0) sb.AppendLine("Bits per pixel : " + bitsPerPixel);
                }

                sb.AppendLine();
                sb.AppendLine("Chunks :");
                AppendChunkList(sb, bytes);
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "File type : PNG\r\n\r\nMetadata read failed : " + ex.Message;
            }
        }

        private static void AppendChunkList(StringBuilder sb, byte[] bytes)
        {
            int offset = 8;
            int index = 0;
            while (offset + 12 <= bytes.Length)
            {
                int chunkOffset = offset;
                int length = ReadInt32BigEndian(bytes, offset);
                string type = ReadAscii(bytes, offset + 4, 4);
                if (length < 0 || offset + 12L + length > bytes.Length)
                {
                    sb.AppendLine("0x" + chunkOffset.ToString("X8") + " Invalid chunk");
                    return;
                }

                int crcOffset = offset + 8 + length;
                uint crc = ReadUInt32BigEndian(bytes, crcOffset);
                sb.AppendLine("0x" + chunkOffset.ToString("X8") + " " + type + " Length : " + length + " CRC : 0x" + crc.ToString("X8"));

                if (IsTextChunk(type) && length > 0)
                {
                    string text = ReadTextChunk(bytes, offset + 8, length, type);
                    if (!string.IsNullOrEmpty(text)) sb.AppendLine("  Text : " + text);
                }

                offset += 12 + length;
                index++;
                if (type == "IEND") return;
                if (index > 10000)
                {
                    sb.AppendLine("Chunk list stopped : too many chunks");
                    return;
                }
            }
        }

        private static PngIhdr ReadIhdr(byte[] bytes)
        {
            PngIhdr ihdr = new PngIhdr();
            if (bytes == null || bytes.Length < 33) return ihdr;
            int length = ReadInt32BigEndian(bytes, 8);
            string type = ReadAscii(bytes, 12, 4);
            if (length != 13 || type != "IHDR") return ihdr;
            ihdr.Width = ReadInt32BigEndian(bytes, 16);
            ihdr.Height = ReadInt32BigEndian(bytes, 20);
            ihdr.BitDepth = bytes[24];
            ihdr.ColorType = bytes[25];
            ihdr.Compression = bytes[26];
            ihdr.Filter = bytes[27];
            ihdr.Interlace = bytes[28];
            ihdr.Valid = true;
            return ihdr;
        }

        private static bool HasPngSignature(byte[] bytes)
        {
            if (bytes == null || bytes.Length < PngSignature.Length) return false;
            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (bytes[i] != PngSignature[i]) return false;
            }
            return true;
        }

        private static int ReadInt32BigEndian(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset + 4 > bytes.Length) return 0;
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset + 4 > bytes.Length) return 0;
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static string ReadAscii(byte[] bytes, int offset, int length)
        {
            if (bytes == null || offset < 0 || length <= 0 || offset + length > bytes.Length) return string.Empty;
            return Encoding.ASCII.GetString(bytes, offset, length);
        }

        private static string FormatHexBytes(byte[] bytes, int offset, int length)
        {
            if (bytes == null || offset < 0 || length <= 0 || offset >= bytes.Length) return string.Empty;
            int end = Math.Min(bytes.Length, offset + length);
            StringBuilder sb = new StringBuilder();
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static bool IsTextChunk(string type)
        {
            return type == "tEXt" || type == "zTXt" || type == "iTXt";
        }

        private static string ReadTextChunk(byte[] bytes, int offset, int length, string type)
        {
            if (bytes == null || length <= 0 || offset < 0 || offset + length > bytes.Length) return string.Empty;
            int maxLength = Math.Min(length, 300);
            string text = Encoding.UTF8.GetString(bytes, offset, maxLength).Replace('\0', ' ');
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (length > maxLength) text += " ...";
            return text;
        }

        private static string ColorTypeName(int colorType)
        {
            switch (colorType)
            {
                case 0: return "Grayscale";
                case 2: return "Truecolor RGB";
                case 3: return "Indexed color";
                case 4: return "Grayscale + alpha";
                case 6: return "Truecolor RGBA";
                default: return "Unknown";
            }
        }

        private static string InterlaceName(int interlace)
        {
            if (interlace == 0) return "None";
            if (interlace == 1) return "Adam7";
            return "Unknown";
        }

        private static int ChannelCount(int colorType)
        {
            switch (colorType)
            {
                case 0: return 1;
                case 2: return 3;
                case 3: return 1;
                case 4: return 2;
                case 6: return 4;
                default: return 0;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.##") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.##") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        private struct PngIhdr
        {
            public bool Valid;
            public int Width;
            public int Height;
            public int BitDepth;
            public int ColorType;
            public int Compression;
            public int Filter;
            public int Interlace;
        }
    }

    internal sealed class D2RSpriteInfo
    {
        public string Magic;
        public int Version;
        public int Width;
        public int Height;
        public int FrameWidth;
        public int FrameCount;
        public int HeaderValue10;
        public int HeaderValue18;
        public int HeaderValue1C;
        public int PayloadSize;
        public int BytesPerPixel;
        public string EncodingName;
        public int FileSize;
        public string RawHeaderHex;

        public string ToInfoText(string displayScaleText)
        {
            return Localization.T("label.size", "Dimensions") + " : " + FormatDimension(Width, Height) + " (" + displayScaleText + ")" + Environment.NewLine
                + Localization.T("label.magic", "Magic number") + " : " + SafeText(Magic) + " (v" + Version + ")" + Environment.NewLine
                + Localization.T("label.color_format", "Color format") + " : " + SafeText(EncodingName) + Environment.NewLine
                + Localization.T("label.frame", "Frame") + " : " + FrameWidth + "px * " + Math.Max(1, FrameCount);
        }

        public string ToHeaderText()
        {
            string encodingText = string.IsNullOrEmpty(EncodingName) ? "unknown" : EncodingName;
            int safeFrameCount = Math.Max(1, FrameCount);
            int frameHeight = Height;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("File type : Sprite");
            sb.AppendLine();
            sb.AppendLine("Metadata :");
            sb.AppendLine("0x00 Magic : " + SafeText(Magic));
            sb.AppendLine("0x04 Version : " + Version);
            sb.AppendLine("0x06 Frame width : " + FrameWidth);
            sb.AppendLine("0x08 Width : " + Width);
            sb.AppendLine("0x0C Height : " + Height);
            sb.AppendLine("0x10 Unknown 1 : " + FormatHex(HeaderValue10));
            sb.AppendLine("0x14 Frame count : " + safeFrameCount);
            sb.AppendLine("0x18 Unknown 2 : " + FormatHex(HeaderValue18));
            sb.AppendLine("0x1C Unknown 3 : " + FormatHex(HeaderValue1C));
            sb.AppendLine("0x20 Payload size : " + PayloadSize);
            sb.AppendLine("0x24 Bytes per pixel : " + BytesPerPixel);
            sb.AppendLine();
            sb.AppendLine("Derived :");
            sb.AppendLine("Color format : " + encodingText);
            sb.AppendLine("Frame size : " + FrameWidth + " * " + frameHeight + " px");
            sb.AppendLine("File size : " + FormatBytes(FileSize));
            sb.AppendLine("Payload offset : 0x28");
            sb.AppendLine("Payload size : " + FormatBytes(PayloadSize));
            if (!string.IsNullOrEmpty(RawHeaderHex))
            {
                sb.AppendLine();
                sb.AppendLine("Raw header :");
                sb.AppendLine(RawHeaderHex);
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatDimension(int width, int height)
        {
            return width + " * " + height + " px";
        }

        private static string SafeText(string text)
        {
            return string.IsNullOrEmpty(text) ? "?" : text;
        }

        private static string FormatBytes(int bytes)
        {
            if (bytes <= 0) return "?";
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024.0) return kb.ToString("0.0") + " KB";
            return (kb / 1024.0).ToString("0.00") + " MB";
        }

        private static string FormatHex(int value)
        {
            return "0x" + value.ToString("X8");
        }
    }

    internal static class D2RSpriteCodec
    {
        private const int HeaderSize = 0x28;
        private const int MaxDimension = 32768;
        private const long MaxPixelCount = 134217728L;

        public static void SaveStaticRgbaSprite(Bitmap source, string output)
        {
            if (source == null) throw new ArgumentNullException("source");
            int width = source.Width;
            int height = source.Height;

            if (!IsSafeDimension(width, height)) throw new InvalidOperationException("비정상 크기 " + width + " * " + height);
            if (width > ushort.MaxValue) throw new InvalidOperationException("정적 sprite frameWidth가 너무 큽니다: " + width);

            long payloadSizeLong = (long)width * (long)height * 4L;
            if (payloadSizeLong > int.MaxValue) throw new InvalidOperationException("RGBA 데이터가 너무 큽니다.");

            byte[] payload = ExtractRgbaPayload(source);
            if (payload.Length != (int)payloadSizeLong) throw new InvalidOperationException("RGBA 데이터 크기가 맞지 않습니다.");

            string dir = Path.GetDirectoryName(output) ?? string.Empty;
            string temp = Path.Combine(dir, "__sprite_tmp_" + Guid.NewGuid().ToString("N") + ".sprite");

            try
            {
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(new byte[] { (byte)'S', (byte)'p', (byte)'A', (byte)'1' });
                    bw.Write((ushort)31);
                    bw.Write((ushort)width);      // frameWidth for static sprite
                    bw.Write((int)width);
                    bw.Write((int)height);
                    bw.Write((int)0);             // unknown / cursor offset area, static item default
                    bw.Write((int)1);             // frameCount
                    bw.Write((int)0);
                    bw.Write((int)0);
                    bw.Write((int)payload.Length);
                    bw.Write((int)4);             // bytes per pixel
                    bw.Write(payload);
                }

                if (File.Exists(output)) File.Delete(output);
                File.Move(temp, output);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static void SaveRgbaSpriteUsingTemplate(Bitmap source, string templateSpritePath, string output)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (string.IsNullOrEmpty(templateSpritePath) || !File.Exists(templateSpritePath)) throw new InvalidOperationException("프레임 템플릿 sprite 파일을 찾을 수 없습니다.");

            byte[] templateBytes = File.ReadAllBytes(templateSpritePath);
            if (templateBytes.Length < HeaderSize) throw new InvalidOperationException("프레임 템플릿 sprite 헤더가 너무 짧습니다.");

            int frameCount = BitConverter.ToInt32(templateBytes, 0x14);
            int templateBytesPerPixel = BitConverter.ToInt32(templateBytes, 0x24);
            int width = source.Width;
            int height = source.Height;

            if (frameCount <= 0) throw new InvalidOperationException("프레임 템플릿 sprite의 프레임 개수가 비정상입니다: " + frameCount);
            if (templateBytesPerPixel != 4) throw new InvalidOperationException("현재는 RGBA/BPP 4 sprite 템플릿만 지원합니다.");
            if (!IsSafeDimension(width, height)) throw new InvalidOperationException("PNG 크기가 비정상입니다: " + width + " * " + height);
            if (width % frameCount != 0)
            {
                throw new InvalidOperationException("PNG 너비 " + width + "px를 템플릿의 " + frameCount + "개 프레임으로 균등하게 나눌 수 없습니다. PNG 너비를 프레임 개수의 배수로 조정하십시오.");
            }

            int frameWidth = width / frameCount;
            if (frameWidth <= 0 || frameWidth > ushort.MaxValue)
            {
                throw new InvalidOperationException("계산된 프레임 너비가 지원 범위를 벗어났습니다: " + frameWidth + "px");
            }

            long payloadSizeLong = (long)width * (long)height * 4L;
            if (payloadSizeLong > int.MaxValue) throw new InvalidOperationException("PNG RGBA 데이터가 너무 큽니다.");

            byte[] payload = ExtractRgbaPayload(source);
            if (payload.Length != (int)payloadSizeLong) throw new InvalidOperationException("PNG RGBA 데이터 크기가 계산된 sprite 페이로드 크기와 맞지 않습니다.");

            byte[] header = new byte[HeaderSize];
            Buffer.BlockCopy(templateBytes, 0, header, 0, HeaderSize);

            Buffer.BlockCopy(BitConverter.GetBytes((ushort)frameWidth), 0, header, 0x06, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, header, 0x08, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, header, 0x0C, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(frameCount), 0, header, 0x14, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 0x20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, header, 0x24, 4);

            string dir = Path.GetDirectoryName(output) ?? string.Empty;
            string temp = Path.Combine(dir, "__sprite_tmp_" + Guid.NewGuid().ToString("N") + ".sprite");

            try
            {
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(header, 0, header.Length);
                    fs.Write(payload, 0, payload.Length);
                }

                if (File.Exists(output)) File.Delete(output);
                File.Move(temp, output);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static byte[] ExtractRgbaPayload(Bitmap source)
        {
            using (Bitmap argb = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(argb))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }

                Rectangle rect = new Rectangle(0, 0, argb.Width, argb.Height);
                BitmapData data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = argb.Width * 4;
                    byte[] bgra = new byte[rowBytes * argb.Height];
                    int stride = data.Stride;

                    if (stride == rowBytes)
                    {
                        Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
                    }
                    else
                    {
                        for (int y = 0; y < argb.Height; y++)
                        {
                            IntPtr srcRow = new IntPtr(data.Scan0.ToInt64() + (long)y * stride);
                            Marshal.Copy(srcRow, bgra, y * rowBytes, rowBytes);
                        }
                    }

                    byte[] rgba = new byte[bgra.Length];
                    for (int i = 0; i < bgra.Length; i += 4)
                    {
                        // Bitmap Format32bppArgb memory order: B, G, R, A
                        // .sprite v31 payload: R, G, B, A
                        rgba[i + 0] = bgra[i + 2];
                        rgba[i + 1] = bgra[i + 1];
                        rgba[i + 2] = bgra[i + 0];
                        rgba[i + 3] = bgra[i + 3];
                    }
                    return rgba;
                }
                finally
                {
                    argb.UnlockBits(data);
                }
            }
        }

        private static bool IsSafeDimension(int width, int height)
        {
            if (width <= 0 || height <= 0) return false;
            if (width > MaxDimension || height > MaxDimension) return false;
            return (long)width * (long)height <= MaxPixelCount;
        }
    }

    internal static class D2RSpritePreview
    {
        private const int HeaderSize = 0x28;
        private const int MaxDimension = 32768;
        private const long MaxPixelCount = 134217728L;

        public static bool TryReadSpriteInfo(string path, out D2RSpriteInfo info)
        {
            info = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length < HeaderSize || !IsKnownSpriteMagic(bytes)) return false;
                info = CreateHeaderInfo(bytes);
                return true;
            }
            catch
            {
                info = null;
                return false;
            }
        }

        public static bool TryLoadSpriteBitmap(string path, out Bitmap bitmap, out D2RSpriteInfo info, out string error)
        {
            bitmap = null;
            info = null;
            error = string.Empty;

            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    error = "파일 없음";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                info = CreateHeaderInfo(bytes);

                if (bytes.Length < HeaderSize)
                {
                    error = "헤더 부족";
                    return false;
                }

                if (!IsKnownSpriteMagic(bytes))
                {
                    error = "미지원 sprite magic";
                    return false;
                }

                int version = info.Version;
                int frameWidth = info.FrameWidth;
                int width = info.Width;
                int height = info.Height;
                int frameCount = info.FrameCount;

                if (!IsSafeDimension(width, height))
                {
                    error = "비정상 크기 " + width + " * " + height;
                    return false;
                }
                if (frameCount <= 0) frameCount = 1;

                Bitmap decoded;
                string encodingName;
                if (version == 31)
                {
                    if (!TryDecodeRgba(bytes, width, height, out decoded, out error)) return false;
                    encodingName = "RGBA";
                }
                else if (version == 61)
                {
                    if (!TryDecodeDxt5(bytes, width, height, out decoded, out error)) return false;
                    encodingName = "DXT5";
                }
                else
                {
                    error = "미지원 버전 v" + version;
                    return false;
                }

                info.EncodingName = encodingName;
                info.FrameCount = frameCount;
                bitmap = decoded;
                return true;
            }
            catch (Exception ex)
            {
                if (bitmap != null)
                {
                    bitmap.Dispose();
                    bitmap = null;
                }
                error = ex.Message;
                return false;
            }
        }

        private static D2RSpriteInfo CreateHeaderInfo(byte[] bytes)
        {
            D2RSpriteInfo info = new D2RSpriteInfo();
            info.FileSize = bytes == null ? 0 : bytes.Length;
            info.Magic = ReadMagicText(bytes);
            info.Version = ReadUInt16Safe(bytes, 0x04);
            info.FrameWidth = ReadUInt16Safe(bytes, 0x06);
            info.Width = ReadInt32Safe(bytes, 0x08);
            info.Height = ReadInt32Safe(bytes, 0x0C);
            info.HeaderValue10 = ReadInt32Safe(bytes, 0x10);
            info.FrameCount = ReadInt32Safe(bytes, 0x14);
            info.HeaderValue18 = ReadInt32Safe(bytes, 0x18);
            info.HeaderValue1C = ReadInt32Safe(bytes, 0x1C);
            info.PayloadSize = ReadInt32Safe(bytes, 0x20);
            info.BytesPerPixel = ReadInt32Safe(bytes, 0x24);
            info.EncodingName = GuessEncodingName(info.Version);
            info.RawHeaderHex = FormatRawHeader(bytes, HeaderSize);
            return info;
        }

        private static string FormatRawHeader(byte[] bytes, int maxLength)
        {
            if (bytes == null || bytes.Length == 0 || maxLength <= 0) return string.Empty;
            int length = Math.Min(bytes.Length, maxLength);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                {
                    if (i % 16 == 0) sb.AppendLine();
                    else sb.Append(' ');
                }
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string ReadMagicText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "?";

            int count = Math.Min(4, bytes.Length);
            bool printable = true;
            for (int i = 0; i < count; i++)
            {
                if (bytes[i] < 32 || bytes[i] > 126)
                {
                    printable = false;
                    break;
                }
            }

            if (printable) return Encoding.ASCII.GetString(bytes, 0, count);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static int ReadUInt16Safe(byte[] bytes, int offset)
        {
            if (bytes == null || bytes.Length < offset + 2) return 0;
            return BitConverter.ToUInt16(bytes, offset);
        }

        private static int ReadInt32Safe(byte[] bytes, int offset)
        {
            if (bytes == null || bytes.Length < offset + 4) return 0;
            return BitConverter.ToInt32(bytes, offset);
        }

        private static string GuessEncodingName(int version)
        {
            if (version == 31) return "RGBA";
            if (version == 61) return "DXT5";
            return "unknown";
        }

        private static bool IsKnownSpriteMagic(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return false;

            // D2R sprite 계열에서 확인한 magic 변형:
            // 일반: SpA1
            // 일부 상태 아이콘/면역 아이콘: SPa1
            // 둘 다 같은 0x28 헤더 + v31/v61 payload 구조를 사용한다.
            return bytes[0] == (byte)'S'
                && (bytes[1] == (byte)'p' || bytes[1] == (byte)'P')
                && (bytes[2] == (byte)'A' || bytes[2] == (byte)'a')
                && bytes[3] == (byte)'1';
        }

        private static bool IsSafeDimension(int width, int height)
        {
            if (width <= 0 || height <= 0) return false;
            if (width > MaxDimension || height > MaxDimension) return false;
            return (long)width * (long)height <= MaxPixelCount;
        }

        private static bool TryDecodeRgba(byte[] bytes, int width, int height, out Bitmap bitmap, out string error)
        {
            bitmap = null;
            error = string.Empty;

            long rawSize = (long)width * (long)height * 4L;
            if (rawSize > int.MaxValue || bytes.Length < HeaderSize + rawSize)
            {
                error = "RGBA 데이터 부족";
                return false;
            }

            byte[] bgra = new byte[(int)rawSize];
            int src = HeaderSize;
            for (int dst = 0; dst < bgra.Length; dst += 4, src += 4)
            {
                // .sprite v31 payload: R, G, B, A
                // Bitmap Format32bppArgb memory order: B, G, R, A
                bgra[dst + 0] = bytes[src + 2];
                bgra[dst + 1] = bytes[src + 1];
                bgra[dst + 2] = bytes[src + 0];
                bgra[dst + 3] = bytes[src + 3];
            }

            bitmap = CreateBitmapFromBgra(width, height, bgra);
            return true;
        }

        private static bool TryDecodeDxt5(byte[] bytes, int width, int height, out Bitmap bitmap, out string error)
        {
            bitmap = null;
            error = string.Empty;

            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            long dxtSizeLong = (long)blockWidth * (long)blockHeight * 16L;
            long rawSizeLong = (long)width * (long)height * 4L;
            if (dxtSizeLong > int.MaxValue || rawSizeLong > int.MaxValue)
            {
                error = "DXT5 데이터가 너무 큼";
                return false;
            }

            int dxtSize = (int)dxtSizeLong;
            int payloadOffset = HeaderSize;
            if (bytes.Length < payloadOffset + dxtSize)
            {
                error = "DXT5 데이터 부족";
                return false;
            }

            byte[] dxt = new byte[dxtSize];
            Buffer.BlockCopy(bytes, payloadOffset, dxt, 0, dxtSize);
            byte[] bgra = new byte[(int)rawSizeLong];
            DxtDecoder.DecompressDXT5(dxt, width, height, bgra);
            bitmap = CreateBitmapFromBgra(width, height, bgra);
            return true;
        }

        private static Bitmap CreateBitmapFromBgra(int width, int height, byte[] bgra)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int rowBytes = width * 4;
                if (stride == rowBytes)
                {
                    Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr dst = new IntPtr(data.Scan0.ToInt64() + (long)y * stride);
                        Marshal.Copy(bgra, y * rowBytes, dst, rowBytes);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
    }

    internal static class DxtDecoder
    {
        public static void DecompressDXT5(byte[] input, int width, int height, byte[] output)
        {
            int offset = 0;
            int bcw = (width + 3) / 4;
            int bch = (height + 3) / 4;
            int clenLast = ((width + 3) % 4 + 1) * 4;
            uint[] buffer = new uint[16];
            int[] colors = new int[4];
            int[] alphas = new int[8];

            for (int t = 0; t < bch; t++)
            {
                for (int s = 0; s < bcw; s++, offset += 16)
                {
                    alphas[0] = input[offset + 0];
                    alphas[1] = input[offset + 1];
                    if (alphas[0] > alphas[1])
                    {
                        alphas[2] = (alphas[0] * 6 + alphas[1]) / 7;
                        alphas[3] = (alphas[0] * 5 + alphas[1] * 2) / 7;
                        alphas[4] = (alphas[0] * 4 + alphas[1] * 3) / 7;
                        alphas[5] = (alphas[0] * 3 + alphas[1] * 4) / 7;
                        alphas[6] = (alphas[0] * 2 + alphas[1] * 5) / 7;
                        alphas[7] = (alphas[0] + alphas[1] * 6) / 7;
                    }
                    else
                    {
                        alphas[2] = (alphas[0] * 4 + alphas[1]) / 5;
                        alphas[3] = (alphas[0] * 3 + alphas[1] * 2) / 5;
                        alphas[4] = (alphas[0] * 2 + alphas[1] * 3) / 5;
                        alphas[5] = (alphas[0] + alphas[1] * 4) / 5;
                        alphas[6] = 0;
                        alphas[7] = 255;
                    }

                    for (int i = 0; i < 8; i++) alphas[i] <<= 24;

                    int r0, g0, b0, r1, g1, b1;
                    int q0 = input[offset + 8] | input[offset + 9] << 8;
                    int q1 = input[offset + 10] | input[offset + 11] << 8;
                    Rgb565(q0, out r0, out g0, out b0);
                    Rgb565(q1, out r1, out g1, out b1);

                    colors[0] = Color(r0, g0, b0, 0);
                    colors[1] = Color(r1, g1, b1, 0);
                    if (q0 > q1)
                    {
                        colors[2] = Color((r0 * 2 + r1) / 3, (g0 * 2 + g1) / 3, (b0 * 2 + b1) / 3, 0);
                        colors[3] = Color((r0 + r1 * 2) / 3, (g0 + g1 * 2) / 3, (b0 + b1 * 2) / 3, 0);
                    }
                    else
                    {
                        colors[2] = Color((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 0);
                        colors[3] = Color(0, 0, 0, 0);
                    }

                    ulong da = BitConverter.ToUInt64(input, offset) >> 16;
                    uint dc = BitConverter.ToUInt32(input, offset + 12);
                    for (int i = 0; i < 16; i++, da >>= 3, dc >>= 2)
                    {
                        buffer[i] = unchecked((uint)(alphas[(int)(da & 7)] | colors[(int)(dc & 3)]));
                    }

                    int copyBytes = s < bcw - 1 ? 16 : clenLast;
                    for (int row = 0, y = t * 4; row < 4 && y < height; row++, y++)
                    {
                        Buffer.BlockCopy(buffer, row * 16, output, (y * width + s * 4) * 4, copyBytes);
                    }
                }
            }
        }

        private static void Rgb565(int c, out int r, out int g, out int b)
        {
            r = (c & 0xf800) >> 8;
            g = (c & 0x07e0) >> 3;
            b = (c & 0x001f) << 3;
            r |= r >> 5;
            g |= g >> 6;
            b |= b >> 5;
        }

        private static int Color(int r, int g, int b, int a)
        {
            return r << 16 | g << 8 | b | a << 24;
        }
    }

    internal sealed class PreviewFramePanel : Panel
    {
        private int dividerY;

        public int DividerY
        {
            get { return dividerY; }
            set
            {
                if (dividerY == value) return;
                dividerY = value;
                Invalidate();
            }
        }

        public PreviewFramePanel()
        {
            DoubleBuffered = true;
            BackColor = SystemColors.Control;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(SystemColors.ControlDark))
            {
                Rectangle rect = ClientRectangle;
                if (rect.Width > 0 && rect.Height > 0)
                {
                    e.Graphics.DrawRectangle(pen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
                }

                if (dividerY > 0 && dividerY < ClientSize.Height - 1)
                {
                    e.Graphics.DrawLine(pen, 0, dividerY, ClientSize.Width - 1, dividerY);
                }
            }
        }
    }

    internal sealed class TransparentPreviewPanel : Panel
    {
        private Bitmap image;
        private bool useCanvasColor = true;
        private Color canvasColor = Color.Black;
        private bool useAlphaColor = false;
        private Color alphaColor = Color.White;

        public TransparentPreviewPanel()
        {
            DoubleBuffered = true;
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void SetImage(Bitmap newImage)
        {
            if (image != null) image.Dispose();
            image = newImage;
            Invalidate();
        }

        public void SetDisplaySettings(bool useCanvasColor, Color canvasColor, bool useAlphaColor, Color alphaColor)
        {
            this.useCanvasColor = useCanvasColor;
            this.canvasColor = canvasColor;
            this.useAlphaColor = useAlphaColor;
            this.alphaColor = alphaColor;
            Invalidate();
        }

        public double GetDisplayScaleForImageSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return 0;

            Rectangle area = ClientRectangle;
            area.Inflate(-8, -8);
            if (area.Width <= 0 || area.Height <= 0) return 0;

            double scale = Math.Min((double)area.Width / width, (double)area.Height / height);
            if (scale > 1.0) scale = 1.0;
            return scale;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) image.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (image == null)
            {
                e.Graphics.Clear(Color.White);
                return;
            }

            if (useCanvasColor) e.Graphics.Clear(canvasColor);
            else DrawChecker(e.Graphics, ClientRectangle);

            Rectangle area = ClientRectangle;
            area.Inflate(-8, -8);
            if (area.Width <= 0 || area.Height <= 0) return;

            double scale = GetDisplayScaleForImageSize(image.Width, image.Height);
            int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
            int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
            int x = area.Left + (area.Width - drawW) / 2;
            int y = area.Top + (area.Height - drawH) / 2;
            Rectangle canvas = new Rectangle(x, y, drawW, drawH);

            if (useAlphaColor)
            {
                using (Brush alphaBrush = new SolidBrush(alphaColor))
                {
                    e.Graphics.FillRectangle(alphaBrush, canvas);
                }
            }
            else
            {
                DrawChecker(e.Graphics, canvas);
            }

            using (Pen border = new Pen(Color.DimGray))
            {
                e.Graphics.DrawRectangle(border, canvas.Left, canvas.Top, canvas.Width - 1, canvas.Height - 1);
            }

            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, canvas);
        }

        private static void DrawChecker(Graphics g, Rectangle rect)
        {
            const int size = 10;
            using (Brush b1 = new SolidBrush(Color.White))
            using (Brush b2 = new SolidBrush(Color.Gainsboro))
            {
                g.FillRectangle(b1, rect);
                Region oldClip = (Region)g.Clip.Clone();
                g.SetClip(rect, CombineMode.Replace);
                try
                {
                    for (int y = rect.Top; y < rect.Bottom; y += size)
                    {
                        for (int x = rect.Left; x < rect.Right; x += size)
                        {
                            bool alt = ((x / size) + (y / size)) % 2 == 0;
                            if (alt) g.FillRectangle(b2, x, y, size, size);
                        }
                    }
                }
                finally
                {
                    g.Clip = oldClip;
                    oldClip.Dispose();
                }
            }
        }
    }
}
