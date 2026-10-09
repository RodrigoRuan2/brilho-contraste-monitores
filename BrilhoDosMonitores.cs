using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace BrilhoDosMonitores
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }

        internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        internal delegate bool GetValue(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
        internal delegate bool SetValue(IntPtr monitor, uint value);

        [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice display, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetMonitorContrast(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetMonitorContrast(IntPtr monitor, uint value);
    }

    internal sealed class MonitorState
    {
        internal IntPtr Handle;
        internal string Name, DeviceId;
        internal int Left, Top;
        internal uint BrightMin, BrightCurrent, BrightMax;
        internal uint ContrastMin, ContrastCurrent, ContrastMax;
        internal bool BrightAvailable, ContrastAvailable;

        internal void Read()
        {
            BrightAvailable = ReadValue(Native.GetMonitorBrightness, out BrightMin, out BrightCurrent, out BrightMax);
            ContrastAvailable = ReadValue(Native.GetMonitorContrast, out ContrastMin, out ContrastCurrent, out ContrastMax);
        }

        private bool ReadValue(Native.GetValue getter, out uint minimum, out uint current, out uint maximum)
        {
            minimum = current = maximum = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (getter(Handle, out minimum, out current, out maximum) && maximum > minimum) return true;
                if (attempt < 2) Thread.Sleep(200);
            }
            return false;
        }

        internal int Percent(bool brightness)
        {
            uint minimum = brightness ? BrightMin : ContrastMin;
            uint current = brightness ? BrightCurrent : ContrastCurrent;
            uint maximum = brightness ? BrightMax : ContrastMax;
            return maximum > minimum ? (int)Math.Round(100.0 * (current - minimum) / (maximum - minimum)) : 0;
        }

        internal void SetPercent(bool brightness, int percent)
        {
            uint minimum = brightness ? BrightMin : ContrastMin;
            uint maximum = brightness ? BrightMax : ContrastMax;
            uint value = (uint)Math.Round(minimum + (maximum - minimum) * Math.Max(0, Math.Min(100, percent)) / 100.0);
            Native.SetValue setter = brightness ? new Native.SetValue(Native.SetMonitorBrightness) : new Native.SetValue(Native.SetMonitorContrast);
            int error = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (setter(Handle, value))
                {
                    if (brightness) BrightCurrent = value; else ContrastCurrent = value;
                    return;
                }
                error = Marshal.GetLastWin32Error();
                if (attempt < 2) Thread.Sleep(200);
            }
            throw new Win32Exception(error);
        }

        internal void Dispose()
        {
            if (Handle != IntPtr.Zero) Native.DestroyPhysicalMonitor(Handle);
            Handle = IntPtr.Zero;
        }
    }

    internal static class MonitorFinder
    {
        internal static List<MonitorState> Discover()
        {
            List<MonitorState> found = new List<MonitorState>();
            Native.MonitorEnumProc callback = delegate(IntPtr handle, IntPtr dc, ref Native.Rect rect, IntPtr data)
            {
                Native.MonitorInfoEx info = new Native.MonitorInfoEx();
                info.Size = Marshal.SizeOf(typeof(Native.MonitorInfoEx));
                if (!Native.GetMonitorInfo(handle, ref info)) return true;
                Native.DisplayDevice display = new Native.DisplayDevice();
                display.Size = Marshal.SizeOf(typeof(Native.DisplayDevice));
                Native.EnumDisplayDevices(info.Device, 0, ref display, 0);
                string name = FriendlyName(display.DeviceId);
                uint count;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out count) || count == 0) return true;
                Native.PhysicalMonitor[] physical = new Native.PhysicalMonitor[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(handle, count, physical)) return true;
                for (int i = 0; i < physical.Length; i++)
                {
                    MonitorState state = new MonitorState();
                    state.Handle = physical[i].Handle;
                    state.Name = name;
                    state.DeviceId = display.DeviceId + "#" + i;
                    state.Left = rect.Left;
                    state.Top = rect.Top;
                    state.Read();
                    found.Add(state);
                }
                return true;
            };
            if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            found.Sort(delegate(MonitorState a, MonitorState b)
            {
                int result = a.Left.CompareTo(b.Left);
                return result != 0 ? result : a.Top.CompareTo(b.Top);
            });
            return found;
        }

        private static string FriendlyName(string deviceId)
        {
            string[] parts = (deviceId ?? "").Split('\\');
            if (parts.Length < 2) return "Monitor externo";
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1]))
                {
                    if (root == null) return "Monitor externo";
                    foreach (string instance in root.GetSubKeyNames())
                    {
                        using (RegistryKey parameters = root.OpenSubKey(instance + @"\Device Parameters"))
                        {
                            byte[] edid = parameters == null ? null : parameters.GetValue("EDID") as byte[];
                            if (edid == null) continue;
                            for (int offset = 54; offset <= 108; offset += 18)
                            {
                                if (edid.Length < offset + 18) break;
                                if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 0xFC && edid[offset + 4] == 0)
                                {
                                    string name = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim(' ', '\r', '\n', '\0');
                                    if (name.Length > 0) return name;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            return "Monitor externo";
        }
    }

    internal sealed class SliderControl
    {
        internal MonitorState Monitor;
        internal bool Brightness;
        internal TrackBar Slider;
        internal Label Value;
        internal System.Windows.Forms.Timer Timer;
    }

    internal sealed class MainForm : Form
    {
        private readonly List<MonitorState> monitors = new List<MonitorState>();
        private readonly List<SliderControl> sliders = new List<SliderControl>();
        private readonly Dictionary<string, Dictionary<string, int>> settings;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly NotifyIcon tray;
        private readonly FlowLayoutPanel cards;
        private readonly Label status;
        private readonly System.Windows.Forms.Timer reconnectTimer;
        private bool exiting;
        private bool balloonShown;
        private int reconnectAttempts;
        private int knownMonitorCount;
        private bool lastRefreshFailed;
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "BrilhoDosMonitores";
        private const string TaskName = "BrilhoDosMonitores";

        private static string InstalledPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BrilhoDosMonitores", "BrilhoDosMonitores.exe"); }
        }

        private static string SettingsPath
        {
            get
            {
                string testDirectory = Environment.GetEnvironmentVariable("BRILHO_TEST_SETTINGS_DIR");
                string directory = String.IsNullOrEmpty(testDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrilhoDosMonitores")
                    : testDirectory;
                return Path.Combine(directory, "settings.json");
            }
        }

        internal MainForm(bool startInTray)
        {
            Text = "Brilho e Contraste";
            ClientSize = new Size(560, 530);
            MinimumSize = new Size(460, 500);
            StartPosition = FormStartPosition.CenterScreen;
            if (startInTray)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                Opacity = 0;
            }
            BackColor = Color.FromArgb(16, 24, 39);
            ForeColor = Color.FromArgb(242, 247, 255);
            Font = new Font("Segoe UI", 10);
            settings = LoadSettings();
            MigrateStartup();
            reconnectTimer = new System.Windows.Forms.Timer();
            reconnectTimer.Interval = 1800;
            reconnectTimer.Tick += delegate
            {
                reconnectTimer.Stop();
                RefreshMonitors();
                if (reconnectAttempts > 0 && MonitorsNeedRetry())
                {
                    reconnectAttempts--;
                    reconnectTimer.Interval = 5000;
                    reconnectTimer.Start();
                }
            };

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 87;
            Controls.Add(header);
            Label heading = NewLabel("Brilho e contraste", 21, FontStyle.Bold);
            heading.SetBounds(25, 14, 380, 32);
            header.Controls.Add(heading);
            Label subtitle = NewLabel("Ajuste cada tela. Os valores ficam salvos.", 10, FontStyle.Regular);
            subtitle.ForeColor = Color.FromArgb(168, 185, 210);
            subtitle.SetBounds(27, 51, 450, 24);
            header.Controls.Add(subtitle);
            Button refresh = new Button();
            refresh.Text = "Atualizar";
            refresh.SetBounds(438, 20, 95, 32);
            refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refresh.FlatStyle = FlatStyle.Flat;
            refresh.FlatAppearance.BorderSize = 0;
            refresh.BackColor = Color.FromArgb(35, 55, 77);
            refresh.ForeColor = ForeColor;
            refresh.Click += delegate { RefreshMonitors(); };
            header.Controls.Add(refresh);

            status = NewLabel("Detectando monitores...", 9, FontStyle.Regular);
            status.ForeColor = Color.FromArgb(168, 185, 210);
            status.Dock = DockStyle.Bottom;
            status.Height = 43;
            status.Padding = new Padding(27, 9, 12, 0);
            Controls.Add(status);

            cards = new FlowLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.FlowDirection = FlowDirection.TopDown;
            cards.WrapContents = false;
            cards.AutoScroll = true;
            cards.Padding = new Padding(25, 6, 10, 0);
            cards.BackColor = BackColor;
            cards.Resize += delegate { ResizeCards(); };
            Controls.Add(cards);
            cards.BringToFront();

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, delegate { ShowWindow(); });
            ToolStripMenuItem startup = new ToolStripMenuItem("Iniciar com o Windows");
            startup.Checked = AutoStartEnabled();
            startup.Click += delegate { ToggleAutoStart(startup); };
            menu.Items.Add(startup);
            menu.Items.Add("Sair", null, delegate { ExitApp(); });
            tray = new NotifyIcon();
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            Icon = tray.Icon;
            tray.Text = "Brilho e Contraste";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };

            Shown += delegate
            {
                RefreshMonitors();
                if (MonitorsNeedRetry()) ScheduleReconnect(5000);
                if (startInTray)
                {
                    Hide();
                    WindowState = FormWindowState.Normal;
                    Opacity = 1;
                }
            };
            FormClosing += OnClosing;
            FormClosed += delegate { reconnectTimer.Dispose(); tray.Visible = false; tray.Dispose(); };
        }

        private Label NewLabel(string text, float size, FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style);
            label.ForeColor = ForeColor;
            label.BackColor = Color.Transparent;
            return label;
        }

        private Dictionary<string, Dictionary<string, int>> LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return json.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(SettingsPath, Encoding.UTF8));
            }
            catch (Exception) { }
            return new Dictionary<string, Dictionary<string, int>>();
        }

        private void SaveSettings()
        {
            string path = SettingsPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, json.Serialize(settings), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception ex)
            {
                status.Text = "Ajuste aplicado, mas não foi possível salvar: " + ex.Message;
            }
        }

        private static bool LegacyRunEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    string value = key == null ? null : key.GetValue(RunValueName) as string;
                    return String.Equals(value, "\"" + InstalledPath + "\" --tray", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception) { return false; }
        }

        private static void RemoveLegacyRun()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                if (key != null) key.DeleteValue(RunValueName, false);
        }

        private static int RunSchtasks(string arguments, out string output)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            start.Arguments = arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static bool ScheduledTaskExists()
        {
            string output;
            return RunSchtasks("/Query /TN \"" + TaskName + "\" /XML", out output) == 0;
        }

        private static bool ScheduledTaskEnabled()
        {
            try
            {
                string output;
                if (RunSchtasks("/Query /TN \"" + TaskName + "\" /XML", out output) != 0) return false;
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(output);
                XmlNode enabled = xml.SelectSingleNode("//*[local-name()='Settings']/*[local-name()='Enabled']");
                XmlNode command = xml.SelectSingleNode("//*[local-name()='Actions']/*[local-name()='Exec']/*[local-name()='Command']");
                if (enabled != null && String.Equals(enabled.InnerText, "false", StringComparison.OrdinalIgnoreCase)) return false;
                return command != null && String.Equals(Path.GetFullPath(command.InnerText.Trim('"')), Path.GetFullPath(InstalledPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool AutoStartEnabled()
        {
            return ScheduledTaskEnabled() || LegacyRunEnabled();
        }

        private static void RegisterScheduledTask()
        {
            string sid = SecurityElement.Escape(WindowsIdentity.GetCurrent().User.Value);
            string executable = SecurityElement.Escape(InstalledPath);
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>Abre Brilho e Contraste na bandeja.</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + sid + "</UserId><Delay>PT10S</Delay></LogonTrigger>" +
                "<SessionStateChangeTrigger><Enabled>true</Enabled><UserId>" + sid + "</UserId><StateChange>SessionUnlock</StateChange><Delay>PT5S</Delay></SessionStateChangeTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + executable + "</Command><Arguments>--tray</Arguments></Exec></Actions></Task>";
            string temp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(temp, xml, Encoding.Unicode);
                string output;
                if (RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + temp + "\" /F", out output) != 0)
                    throw new InvalidOperationException(output.Trim());
            }
            finally { File.Delete(temp); }
        }

        private static void MigrateStartup()
        {
            if (!LegacyRunEnabled()) return;
            try
            {
                if (!ScheduledTaskEnabled()) RegisterScheduledTask();
                RemoveLegacyRun();
            }
            catch (Exception) { /* A entrada antiga continua funcionando se a migração falhar. */ }
        }

        private void ToggleAutoStart(ToolStripMenuItem item)
        {
            try
            {
                if (item.Checked)
                {
                    if (ScheduledTaskExists())
                    {
                        string output;
                        if (RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", out output) != 0)
                            throw new InvalidOperationException(output.Trim());
                    }
                    RemoveLegacyRun();
                    item.Checked = false;
                    status.Text = "Inicialização com o Windows desativada.";
                }
                else
                {
                    string target = InstalledPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (!String.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        File.Copy(Application.ExecutablePath, target, true);
                    RegisterScheduledTask();
                    RemoveLegacyRun();
                    item.Checked = true;
                    status.Text = "O app iniciará na bandeja ao entrar ou desbloquear o Windows.";
                }
            }
            catch (Exception ex) { status.Text = "Não foi possível alterar a inicialização: " + ex.Message; }
        }

        private void SaveMonitor(MonitorState monitor)
        {
            Dictionary<string, int> values;
            if (!settings.TryGetValue(monitor.DeviceId, out values) || values == null)
            {
                values = new Dictionary<string, int>();
                settings[monitor.DeviceId] = values;
            }
            if (monitor.BrightAvailable) values["brightness"] = monitor.Percent(true);
            if (monitor.ContrastAvailable) values["contrast"] = monitor.Percent(false);
            SaveSettings();
        }

        private bool ApplySaved(MonitorState monitor)
        {
            Dictionary<string, int> values;
            if (!settings.TryGetValue(monitor.DeviceId, out values) || values == null)
            {
                SaveMonitor(monitor);
                return true;
            }
            int target;
            bool success = true;
            try
            {
                if (monitor.BrightAvailable && values.TryGetValue("brightness", out target) && target != monitor.Percent(true))
                    monitor.SetPercent(true, target);
            }
            catch (Exception ex) { status.Text = "Falha ao restaurar brilho de " + monitor.Name + ": " + ex.Message; success = false; }
            try
            {
                if (monitor.ContrastAvailable && values.TryGetValue("contrast", out target) && target != monitor.Percent(false))
                    monitor.SetPercent(false, target);
            }
            catch (Exception ex) { status.Text = "Falha ao restaurar contraste de " + monitor.Name + ": " + ex.Message; success = false; }
            return success;
        }

        private void RefreshMonitors()
        {
            lastRefreshFailed = false;
            FlushPending();
            foreach (SliderControl slider in sliders) slider.Timer.Dispose();
            sliders.Clear();
            cards.Controls.Clear();
            foreach (MonitorState monitor in monitors) monitor.Dispose();
            monitors.Clear();
            try
            {
                monitors.AddRange(MonitorFinder.Discover());
                knownMonitorCount = Math.Max(knownMonitorCount, monitors.Count);
                for (int i = 0; i < monitors.Count; i++)
                {
                    if (!ApplySaved(monitors[i])) lastRefreshFailed = true;
                    AddCard(monitors[i], i + 1);
                }
                ResizeCards();
                if (monitors.Count == 0) status.Text = "Nenhum monitor externo encontrado.";
                else if (lastRefreshFailed) status.Text = "Monitores detectados; alguns ajustes não foram restaurados.";
                else status.Text = monitors.Count + " monitores detectados. Ajustes salvos automaticamente.";
            }
            catch (Exception ex) { lastRefreshFailed = true; status.Text = "Erro ao detectar monitores: " + ex.Message; }
        }

        private bool MonitorsNeedRetry()
        {
            if (lastRefreshFailed || monitors.Count == 0 || monitors.Count < knownMonitorCount) return true;
            foreach (MonitorState monitor in monitors)
                if (!monitor.BrightAvailable && !monitor.ContrastAvailable) return true;
            return false;
        }

        private void ScheduleReconnect(int delay)
        {
            if (exiting || reconnectTimer == null) return;
            reconnectAttempts = 3;
            reconnectTimer.Stop();
            reconnectTimer.Interval = delay;
            reconnectTimer.Start();
        }

        private void AddCard(MonitorState monitor, int number)
        {
            Panel card = new Panel();
            card.Height = 188;
            card.Width = Math.Max(350, cards.ClientSize.Width - 45);
            card.BackColor = Color.FromArgb(27, 41, 64);
            card.Margin = new Padding(0, 0, 0, 11);
            Label name = NewLabel(number + ". " + monitor.Name, 12, FontStyle.Bold);
            name.SetBounds(17, 10, 360, 25);
            card.Controls.Add(name);
            string position = monitors.Count == 2 ? (number == 1 ? "esquerda" : "direita") : "tela " + number;
            Label side = NewLabel("Tela " + number + " · " + position, 9, FontStyle.Regular);
            side.ForeColor = Color.FromArgb(168, 185, 210);
            side.SetBounds(18, 35, 320, 20);
            card.Controls.Add(side);
            AddSlider(card, monitor, true, "Brilho", 59);
            AddSlider(card, monitor, false, "Contraste", 120);
            cards.Controls.Add(card);
        }

        private void AddSlider(Panel card, MonitorState monitor, bool brightness, string title, int top)
        {
            bool available = brightness ? monitor.BrightAvailable : monitor.ContrastAvailable;
            Label caption = NewLabel(title, 10, FontStyle.Regular);
            caption.ForeColor = Color.FromArgb(168, 185, 210);
            caption.SetBounds(18, top, 150, 24);
            card.Controls.Add(caption);
            Label value = NewLabel(available ? monitor.Percent(brightness) + "%" : "Indisponível", 10, FontStyle.Bold);
            value.ForeColor = Color.FromArgb(95, 211, 192);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.SetBounds(card.Width - 120, top, 100, 24);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(value);
            TrackBar track = new TrackBar();
            track.Minimum = 0;
            track.Maximum = 100;
            track.TickStyle = TickStyle.None;
            track.AutoSize = false;
            track.Height = 32;
            track.SetBounds(12, top + 23, card.Width - 30, 32);
            track.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            track.BackColor = card.BackColor;
            track.Enabled = available;
            track.Value = available ? monitor.Percent(brightness) : 0;
            card.Controls.Add(track);
            SliderControl control = new SliderControl();
            control.Monitor = monitor;
            control.Brightness = brightness;
            control.Slider = track;
            control.Value = value;
            control.Timer = new System.Windows.Forms.Timer();
            control.Timer.Interval = 220;
            control.Timer.Tick += delegate { control.Timer.Stop(); ApplyControl(control); };
            track.ValueChanged += delegate
            {
                value.Text = track.Value + "%";
                control.Timer.Stop();
                control.Timer.Start();
            };
            sliders.Add(control);
        }

        private void ResizeCards()
        {
            foreach (Control card in cards.Controls)
                card.Width = Math.Max(350, cards.ClientSize.Width - 45);
        }

        private void ApplyControl(SliderControl control)
        {
            try
            {
                control.Monitor.SetPercent(control.Brightness, control.Slider.Value);
                SaveMonitor(control.Monitor);
                status.Text = control.Monitor.Name + ": ajuste aplicado e salvo.";
            }
            catch (Exception ex)
            {
                control.Slider.Value = control.Monitor.Percent(control.Brightness);
                control.Timer.Stop();
                status.Text = "Não foi possível ajustar " + control.Monitor.Name + ": " + ex.Message;
            }
        }

        private void FlushPending()
        {
            foreach (SliderControl control in sliders)
            {
                if (!control.Timer.Enabled) continue;
                control.Timer.Stop();
                ApplyControl(control);
            }
        }

        internal void ShowWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        private void OnClosing(object sender, FormClosingEventArgs args)
        {
            FlushPending();
            if (!exiting)
            {
                args.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                if (!balloonShown)
                {
                    tray.ShowBalloonTip(2500, "Brilho e Contraste", "O app continua aberto na bandeja. Clique duas vezes no ícone para reabrir.", ToolTipIcon.Info);
                    balloonShown = true;
                }
                return;
            }
            foreach (SliderControl slider in sliders) slider.Timer.Dispose();
            foreach (MonitorState monitor in monitors) monitor.Dispose();
        }

        private void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x11) exiting = true; // WM_QUERYENDSESSION
            if (message.Msg == 0x16 && message.WParam == IntPtr.Zero) exiting = false; // canceled shutdown
            if (message.Msg == 0x7E || // WM_DISPLAYCHANGE
                (message.Msg == 0x219 && message.WParam.ToInt64() == 0x7) || // WM_DEVICECHANGE / DBT_DEVNODES_CHANGED
                (message.Msg == 0x218 && (message.WParam.ToInt64() == 0x7 || message.WParam.ToInt64() == 0x12))) // resume
                ScheduleReconnect(message.Msg == 0x218 ? 3000 : 1800);
            base.WndProc(ref message);
        }
    }

    internal static class Program
    {
        private const string MutexName = @"Local\BrilhoDosMonitores-SingleInstance";
        private const string ShowEventName = @"Local\BrilhoDosMonitores-Show";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--read-test")) return SelfTest(args);
            bool startInTray = args.Length > 0 && args[0] == "--tray";
            bool first;
            using (Mutex mutex = new Mutex(true, MutexName, out first))
            {
                if (!first)
                {
                    if (!startInTray)
                    {
                        using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                        {
                            signal.Set();
                        }
                    }
                    return 0;
                }
                using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    MainForm form = new MainForm(startInTray);
                    Thread listener = new Thread(delegate()
                    {
                        while (true)
                        {
                            try
                            {
                                signal.WaitOne();
                                if (form.IsDisposed) return;
                                if (form.IsHandleCreated) form.BeginInvoke(new Action(form.ShowWindow));
                            }
                            catch (ObjectDisposedException) { return; }
                            catch (InvalidOperationException) { return; }
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                    Application.Run(form);
                    return 0;
                }
            }
        }

        private static int SelfTest(string[] args)
        {
            int result = 0;
            string message = "";
            List<MonitorState> found = new List<MonitorState>();
            try
            {
                found = MonitorFinder.Discover();
                if (found.Count != 2) throw new Exception("Esperados 2 monitores; encontrados " + found.Count);
                foreach (MonitorState monitor in found)
                {
                    if (!monitor.BrightAvailable || !monitor.ContrastAvailable) throw new Exception(monitor.Name + " indisponível");
                    int brightness = monitor.Percent(true), contrast = monitor.Percent(false);
                    if (args[0] == "--self-test")
                    {
                        monitor.SetPercent(true, brightness);
                        monitor.SetPercent(false, contrast);
                        monitor.Read();
                    }
                    message += monitor.Name + ": brilho " + monitor.Percent(true) + "%, contraste " + monitor.Percent(false) + "%\n";
                }
            }
            catch (Exception ex) { result = 1; message += ex.ToString(); }
            finally { foreach (MonitorState monitor in found) monitor.Dispose(); }
            if (args.Length > 1) File.WriteAllText(args[1], "exit=" + result + "\n" + message, Encoding.UTF8);
            return result;
        }
    }
}
