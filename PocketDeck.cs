using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class DeckButton
{
    public string label { get; set; }
    public string icon { get; set; }
    public string color { get; set; }
    public string action { get; set; }
    public string target { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

internal static class DeckGrid
{
    public static int Width(DeckButton button) { return DeckActions.IsWidget(button) ? Math.Max(1, button.width) : 1; }
    public static int Height(DeckButton button) { return DeckActions.IsWidget(button) ? Math.Max(1, button.height) : 1; }

    public static int[] Coverage(DeckLayout deck)
    {
        int count = deck.columns * deck.rows;
        var cells = new int[count];
        for (int i = 0; i < count; i++) cells[i] = i;
        for (int i = 0; i < deck.buttons.Count; i++)
        {
            DeckButton button = deck.buttons[i];
            if (!DeckActions.IsWidget(button)) continue;
            int column = i % deck.columns, row = i / deck.columns;
            int width = Width(button), height = Height(button);
            if (width > deck.columns - column || height > deck.rows - row) return null;
            for (int y = row; y < row + height; y++)
            for (int x = column; x < column + width; x++)
            {
                int cell = y * deck.columns + x;
                if (cell == i) continue;
                if (cells[cell] != cell || deck.buttons[cell].action != "none") return null;
                cells[cell] = i;
            }
        }
        return cells;
    }
}

public sealed class DeckLayout
{
    public string version { get; set; }
    public int columns { get; set; }
    public int rows { get; set; }
    public List<DeckButton> buttons { get; set; }
}

internal sealed class DeckStore
{
    private readonly string path;
    private volatile string applied;

    public DeckStore(string path)
    {
        this.path = path;
        if (File.Exists(path))
        {
            applied = File.ReadAllText(path, Encoding.UTF8);
            Validate(Read());
            return;
        }
        var buttons = new List<DeckButton>();
        for (int i = 1; i <= 6; i++) buttons.Add(DeckActions.DefaultButton(i));
        Save(new DeckLayout { columns = 3, rows = 2, buttons = buttons });
    }

    public DeckLayout Read()
    {
        return new JavaScriptSerializer().Deserialize<DeckLayout>(applied);
    }

    public void Save(DeckLayout deck)
    {
        Validate(deck);
        deck.version = Guid.NewGuid().ToString("N");
        string json = new JavaScriptSerializer().Serialize(deck);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
        applied = json;
    }

    private static void Validate(DeckLayout deck)
    {
        if (deck == null || deck.columns < 1 || deck.columns > 5 || deck.rows < 1 || deck.rows > 4 ||
            deck.buttons == null || deck.buttons.Count < deck.columns * deck.rows ||
            deck.buttons.Count > 20 || deck.buttons.Contains(null) || DeckGrid.Coverage(deck) == null)
            throw new InvalidDataException("저장된 버튼 배치를 읽을 수 없습니다.");
    }
}

internal static class DeckActions
{
    public static readonly string[] Names = {
        "동작 없음", "재생 / 일시정지", "이전 곡", "다음 곡", "볼륨 내리기",
        "볼륨 올리기", "음소거", "프로그램 열기", "키보드 단축키", "시계", "날씨", "볼륨 믹서"
    };
    public static readonly string[] Ids = {
        "none", "play", "previous", "next", "volumeDown", "volumeUp", "mute", "open", "shortcut", "clock", "weather", "mixer"
    };
    private static readonly string[] Icons = { "", "⏯", "⏮", "⏭", "−", "+", "♪", "", "⌨", "◷", "☀", "" };
    private static readonly string[] Colors = {
        "#39455D", "#365CE8", "#39455D", "#39455D", "#275D59", "#275D59", "#744669", "#28547D", "#684997",
        "#263867", "#215362", "#42376E"
    };
    private static readonly Keys[] Media = {
        Keys.MediaPlayPause, Keys.MediaPreviousTrack, Keys.MediaNextTrack,
        Keys.VolumeDown, Keys.VolumeUp, Keys.VolumeMute
    };

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scan, uint flags, UIntPtr extra);

    public static DeckButton DefaultButton(int action)
    {
        return new DeckButton {
            label = action == 0 ? "" : Names[action], icon = Icons[action],
            color = Colors[action], action = Ids[action], target = ""
        };
    }

    public static bool UsesProgramIcon(DeckButton button)
    {
        return button.action == "open" && (String.IsNullOrWhiteSpace(button.icon) || button.icon == "↗");
    }

    public static bool IsWidget(DeckButton button)
    {
        return button.action == "clock" || button.action == "weather" || button.action == "mixer";
    }

    public static Bitmap ReadProgramIcon(string path)
    {
        try
        {
            if (!File.Exists(path) || !String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            using (var icon = Icon.ExtractAssociatedIcon(path)) return icon == null ? null : icon.ToBitmap();
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static void Run(DeckButton button)
    {
        int kind = Array.IndexOf(Ids, button.action);
        if (kind >= 1 && kind <= Media.Length)
        {
            Tap(new[] { Media[kind - 1] });
        }
        else if (kind == 7)
        {
            string path = button.target ?? "";
            if (!Path.IsPathRooted(path) || !File.Exists(path) ||
                !String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PC 앱에서 실행할 .exe 파일을 선택해 주세요.");
            using (Process.Start(new ProcessStartInfo(path) {
                WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true
            })) { }
        }
        else if (kind == 8)
        {
            Tap(ParseShortcut(button.target));
        }
    }

    public static Keys[] ParseShortcut(string value)
    {
        string[] parts = (value ?? "").Split('+');
        List<Keys> keys = new List<Keys>();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            Keys key;
            if (i < parts.Length - 1)
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL": key = Keys.ControlKey; break;
                    case "ALT": key = Keys.Menu; break;
                    case "SHIFT": key = Keys.ShiftKey; break;
                    case "WIN": key = Keys.LWin; break;
                    default: throw ShortcutError();
                }
                if (keys.Contains(key)) throw ShortcutError();
            }
            else
            {
                if (part.Length == 1 && part[0] >= '0' && part[0] <= '9') part = "D" + part;
                if (part.Equals("Esc", StringComparison.OrdinalIgnoreCase)) part = "Escape";
                if (part.Contains(",") || !Enum.TryParse<Keys>(part, true, out key) ||
                    !Enum.IsDefined(typeof(Keys), key) || key < Keys.Back || (int)key > 254 ||
                    Char.IsDigit(part[0])) throw ShortcutError();
            }
            keys.Add(key);
        }
        return keys.ToArray();
    }

    private static Exception ShortcutError()
    {
        return new InvalidOperationException("단축키는 Ctrl+Shift+M, Alt+F4, Win+1 같은 형식으로 입력해 주세요.");
    }

    private static void Tap(Keys[] keys)
    {
        foreach (Keys key in keys) SendKey(key, 0);
        for (int i = keys.Length - 1; i >= 0; i--)
            SendKey(keys[i], 2);
    }

    private static void SendKey(Keys key, uint flags)
    {
        if ((key >= Keys.PageUp && key <= Keys.Down) || key == Keys.Insert || key == Keys.Delete ||
            key == Keys.LWin || key == Keys.RWin || key == Keys.Divide ||
            (key >= Keys.VolumeMute && key <= Keys.MediaPlayPause)) flags |= 1;
        keybd_event((byte)key, 0, flags, UIntPtr.Zero);
    }
}

internal sealed class DevicePairing
{
    private const int CodeLength = 8;
    private readonly string path;
    public readonly string Code;
    public bool Completed { get; private set; }

    public DevicePairing(string path, bool previouslyLaunched)
    {
        this.path = path;
        bool exists = File.Exists(path);
        bool completed = !exists && previouslyLaunched;
        if (exists)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 2 && Boolean.TryParse(lines[1], out completed) && ValidCode(lines[0]))
            {
                Code = lines[0];
                Completed = completed;
                return;
            }
        }
        Code = CreateCode();
        Save(completed);
    }

    private static bool ValidCode(string code)
    {
        if (code.Length != CodeLength) return false;
        foreach (char character in code)
            if (character < '0' || character > '9') return false;
        return true;
    }

    public static string CreateCode()
    {
        byte[] random = new byte[16];
        var code = new StringBuilder(CodeLength);
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            while (code.Length < CodeLength)
            {
                rng.GetBytes(random);
                foreach (byte value in random)
                {
                    if (value >= 250) continue;
                    code.Append((char)('0' + value % 10));
                    if (code.Length == CodeLength) break;
                }
            }
        }
        return code.ToString();
    }

    public void Complete() { Save(true); }

    private void Save(bool completed)
    {
        using (var contents = new MemoryStream(Encoding.UTF8.GetBytes(Code + "\n" + completed)))
            InstallPaths.WriteAtomic(path, contents);
        Completed = completed;
    }
}

internal sealed class PhoneServer : IDisposable
{
    public const int Port = 8765;
    private readonly TcpListener listener;
    private readonly DeckStore store;
    private readonly string web;
    private readonly Semaphore clients = new Semaphore(16, 16);
    private readonly object actionLock = new object();
    public readonly string Code;
    public readonly int PortNumber;
    private volatile bool remoteConnected;
    public bool RemoteConnected { get { return remoteConnected; } }

    public PhoneServer(DeckStore store, string web, int preferredPort = Port, string code = null)
    {
        this.store = store;
        this.web = web;
        Code = code ?? DevicePairing.CreateCode();
        listener = new TcpListener(IPAddress.Any, preferredPort);
        try { listener.Start(); }
        catch (SocketException error)
        {
            if (error.SocketErrorCode != SocketError.AddressAlreadyInUse) throw;
            listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
        }
        PortNumber = ((IPEndPoint)listener.LocalEndpoint).Port;
        new Thread(Accept) { IsBackground = true }.Start();
    }

    public void Dispose() { listener.Stop(); }

    public static List<string> Addresses(int port)
    {
        List<string> result = new List<string>();
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            var config = adapter.GetIPProperties();
            bool hasGateway = false;
            foreach (var gateway in config.GatewayAddresses)
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any)) hasGateway = true;
            foreach (var info in config.UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string address = Url(info.Address.ToString(), port);
                if (hasGateway) result.Insert(0, address);
                else result.Add(address);
            }
        }
        return result;
    }

    private static string Url(string address, int port) { return "http://" + address + ":" + port + "/"; }

    private void Accept()
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (SocketException) { return; }
            catch (InvalidOperationException) { return; }
            if (!clients.WaitOne(0)) { client.Close(); continue; }
            ThreadPool.QueueUserWorkItem(_ => {
                try { using (client) Serve(client); }
                catch (IOException) { }
                catch (SocketException) { }
                finally { clients.Release(); }
            });
        }
    }

    private void Serve(TcpClient client)
    {
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;
        var stream = client.GetStream();
        var local = (IPEndPoint)client.Client.LocalEndPoint;
        string expectedHost = local.Address + ":" + PortNumber;
        byte[] header = new byte[8192];
        int length = 0;
        var deadline = Stopwatch.StartNew();
        while (length < header.Length && deadline.ElapsedMilliseconds < 3000)
        {
            int b = stream.ReadByte();
            if (b < 0) return;
            header[length++] = (byte)b;
            if (length >= 4 && header[length - 4] == 13 && header[length - 3] == 10 &&
                header[length - 2] == 13 && header[length - 1] == 10) break;
        }
        if (length == header.Length || deadline.ElapsedMilliseconds >= 3000) return;
        string[] lines = Encoding.ASCII.GetString(header, 0, length).Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] request = lines[0].Split(' ');
        if (request.Length != 3) return;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length - 2; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) return;
            string key = lines[i].Substring(0, colon);
            if (headers.ContainsKey(key)) return;
            headers[key] = lines[i].Substring(colon + 1).Trim();
        }
        string host, origin, token;
        if (!headers.TryGetValue("Host", out host) ||
            !(host == expectedHost || (IPAddress.IsLoopback(local.Address) && host == "localhost:" + PortNumber)) ||
            (headers.TryGetValue("Origin", out origin) && origin != "http://" + host))
        {
            Reply(stream, "403 Forbidden", "text/plain", "Forbidden");
            return;
        }
        string method = request[0], route = request[1];
        if (method == "GET")
        {
            switch (route)
            {
                case "/": Asset(stream, "index.html", "text/html"); return;
                case "/phone.css": Asset(stream, "phone.css", "text/css"); return;
                case "/phone.js": Asset(stream, "phone.js", "text/javascript"); return;
            }
        }
        if (!headers.TryGetValue("X-Deck-Code", out token) || token != Code)
        {
            Reply(stream, "401 Unauthorized", "text/plain", "Unauthorized");
            return;
        }
        if (method == "GET" && route == "/api/layout")
        {
            DeckLayout deck = store.Read();
            int[] cells = DeckGrid.Coverage(deck);
            var buttons = new List<object>();
            for (int i = 0; i < deck.columns * deck.rows; i++)
            {
                DeckButton button = deck.buttons[i];
                bool widget = DeckActions.IsWidget(button);
                buttons.Add(new { label = button.label, icon = button.icon, color = button.color,
                    enabled = button.action != "none" && !widget, appIcon = DeckActions.UsesProgramIcon(button),
                    covered = cells[i] != i, width = DeckGrid.Width(button), height = DeckGrid.Height(button),
                    widget = widget ? button.action : null, location = button.action == "weather" ? button.target : null });
            }
            Reply(stream, "200 OK", "application/json", new JavaScriptSerializer().Serialize(
                new { version = deck.version, columns = deck.columns, rows = deck.rows, buttons = buttons }));
            ObserveLayoutRequest(((IPEndPoint)client.Client.RemoteEndPoint).Address, local.Address);
            return;
        }
        if ((method == "GET" && route == "/api/mixer") ||
            (method == "POST" && route.StartsWith("/api/mixer/", StringComparison.Ordinal)))
        {
            ServeMixer(stream, method, route);
            return;
        }
        const string iconRoute = "/api/icon/";
        if (method == "GET" && route.StartsWith(iconRoute, StringComparison.Ordinal))
        {
            string[] parts = route.Substring(iconRoute.Length).Split('/');
            DeckLayout deck = store.Read();
            int index;
            if (parts.Length == 2 && parts[0] == deck.version && Int32.TryParse(parts[1], out index) &&
                index >= 0 && index < deck.columns * deck.rows && DeckActions.UsesProgramIcon(deck.buttons[index]))
            {
                using (var icon = DeckActions.ReadProgramIcon(deck.buttons[index].target))
                using (var png = new MemoryStream())
                {
                    if (icon != null)
                    {
                        icon.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        ReplyBytes(stream, "200 OK", "image/png", png.ToArray());
                        return;
                    }
                }
            }
        }
        const string press = "/api/press/";
        if (method == "POST" && route.StartsWith(press, StringComparison.Ordinal))
        {
            string[] parts = route.Substring(press.Length).Split('/');
            DeckLayout deck = store.Read();
            int index;
            if (parts.Length == 2 && Int32.TryParse(parts[1], out index))
            {
                if (parts[0] != deck.version)
                {
                    Reply(stream, "409 Conflict", "text/plain", "배치가 바뀌었어요. 새 버튼을 확인해 주세요.");
                    return;
                }
                if (index < 0 || index >= deck.columns * deck.rows)
                {
                    Reply(stream, "404 Not Found", "text/plain", "Not found");
                    return;
                }
                if (deck.buttons[index].action == "none" || DeckActions.IsWidget(deck.buttons[index]))
                {
                    Reply(stream, "422 Unprocessable Content", "text/plain", "이 버튼에는 실행 동작이 없어요.");
                    return;
                }
                try { lock (actionLock) DeckActions.Run(deck.buttons[index]); }
                catch (Exception error)
                {
                    Reply(stream, "422 Unprocessable Content", "text/plain", error.Message);
                    return;
                }
                Reply(stream, "204 No Content", "text/plain", "");
                return;
            }
        }
        Reply(stream, "404 Not Found", "text/plain", "Not found");
    }

    private void ServeMixer(NetworkStream stream, string method, string route)
    {
        DeckLayout deck = store.Read();
        int first = deck.buttons.FindIndex(button => button.action == "mixer");
        if (first < 0 || first >= deck.columns * deck.rows)
        {
            Reply(stream, "404 Not Found", "text/plain", "볼륨 믹서 위젯을 적용해 주세요.");
            return;
        }
        try
        {
            if (method == "GET")
            {
                List<MixerChannel> channels;
                lock (actionLock) channels = AudioMixer.Read();
                Reply(stream, "200 OK", "application/json", new JavaScriptSerializer().Serialize(channels));
                return;
            }
            string[] parts = route.Substring("/api/mixer/".Length).Split('/');
            int volume;
            if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Length > 512 ||
                !Int32.TryParse(parts[1], out volume) || volume < 0 || volume > 100)
            {
                Reply(stream, "400 Bad Request", "text/plain", "볼륨은 0부터 100까지 설정할 수 있어요.");
                return;
            }
            lock (actionLock) AudioMixer.SetVolume(Uri.UnescapeDataString(parts[0]), volume);
            Reply(stream, "204 No Content", "text/plain", "");
        }
        catch (InvalidOperationException error)
        {
            Reply(stream, "422 Unprocessable Content", "text/plain", error.Message);
        }
        catch (COMException)
        {
            Reply(stream, "503 Service Unavailable", "text/plain", "Windows 오디오 출력 장치를 확인해 주세요.");
        }
    }

    private void ObserveLayoutRequest(IPAddress client, IPAddress host)
    {
        if (!IPAddress.IsLoopback(client) && !client.Equals(host)) remoteConnected = true;
    }

    private void Asset(NetworkStream stream, string name, string type)
    {
        string path = Path.Combine(web, name);
        string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8)
            : InstallPaths.ReadResource("Phone." + name);
        Reply(stream, "200 OK", type, text);
    }

    private static void Reply(NetworkStream stream, string status, string type, string body)
    {
        ReplyBytes(stream, status, type + "; charset=utf-8", Encoding.UTF8.GetBytes(body));
    }

    private static void ReplyBytes(NetworkStream stream, string status, string type, byte[] bytes)
    {
        string length = status.StartsWith("204") ? "" : "Content-Length: " + bytes.Length + "\r\n";
        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
            "\r\nContent-Type: " + type + "\r\n" + length +
            "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        stream.Write(header, 0, header.Length);
        if (bytes.Length > 0) stream.Write(bytes, 0, bytes.Length);
    }
}

internal sealed class DragGhost : Form
{
    private readonly Image image;

    public DragGhost(Button tile)
    {
        if (tile.Image != null) image = (Image)tile.Image.Clone();
        Text = tile.Text;
        Font = tile.Font;
        BackColor = tile.BackColor;
        ForeColor = tile.ForeColor;
        ClientSize = new Size(120, 72);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.76;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            const int noActivate = 0x08000000;
            const int mouseTransparent = 0x20;
            var parameters = base.CreateParams;
            parameters.ExStyle |= noActivate | mouseTransparent;
            return parameters;
        }
    }

    public void FollowPointer()
    {
        Point pointer = Cursor.Position;
        var bounds = Screen.FromPoint(pointer).WorkingArea;
        int x = pointer.X + 16;
        int y = pointer.Y + 16;
        if (x + Width > bounds.Right) x = pointer.X - Width - 14;
        if (y + Height > bounds.Bottom) y = pointer.Y - Height - 14;
        Location = new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var border = new Pen(Color.FromArgb(185, 213, 255), 2F))
        using (var text = new SolidBrush(ForeColor))
        using (var format = new StringFormat {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        })
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            e.Graphics.DrawRectangle(border, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            int textTop = 5;
            if (image != null)
            {
                e.Graphics.DrawImage(image, (ClientSize.Width - 26) / 2, 8, 26, 26);
                textTop = 35;
            }
            e.Graphics.DrawString(Text, Font, text,
                new RectangleF(5, textTop, ClientSize.Width - 10, ClientSize.Height - textTop - 5), format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && image != null) image.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DeviceAddressPicker : ComboBox
{
    private readonly int port;
    public DeviceAddressPicker(int port)
    {
        this.port = port;
        DropDownStyle = ComboBoxStyle.DropDownList;
        Width = 203;
        RefreshAddresses();
    }

    public void RefreshAddresses()
    {
        string previous = Text;
        Items.Clear();
        foreach (string url in PhoneServer.Addresses(port)) Items.Add(url);
        if (Items.Count == 0) return;
        int index = Items.IndexOf(previous);
        SelectedIndex = index >= 0 ? index : 0;
    }
}

internal sealed class PairingWindow : Form
{
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 };

    public PairingWindow(PhoneServer server) : this(server.Code, server.PortNumber, server) { }

    public PairingWindow(string code, int port, PhoneServer pending = null)
    {
        Text = "Pocket Deck · 기기 연결";
        ClientSize = new Size(530, 270);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Malgun Gothic", 9F);
        BackColor = Color.FromArgb(245, 247, 251);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
            ColumnCount = 1, RowCount = 6 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 39, 46, 24, 39, 35, 30 })
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(content);

        var heading = new Label { Text = "기기 연결", Font = new Font("Malgun Gothic", 17F, FontStyle.Bold),
            AutoSize = true };
        content.Controls.Add(heading, 0, 0);
        content.Controls.Add(new Label {
            Text = "PC와 기기를 같은 공유기에 연결하세요.\n기기 브라우저에서 주소를 열고 8자리 숫자를 입력하세요.",
            AutoSize = true
        }, 0, 1);
        content.Controls.Add(new Label { Text = "접속 주소", AutoSize = true }, 0, 2);

        var connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        content.Controls.Add(connection, 0, 3);
        var address = new DeviceAddressPicker(port) { Width = 235 };
        var refresh = new Button { Text = "새로고침", AutoSize = true };
        var copy = new Button { Text = "연결 링크 복사", AutoSize = true, Enabled = address.SelectedIndex >= 0 };
        connection.Controls.AddRange(new Control[] { address, refresh, copy });
        copy.Click += (s, e) => Clipboard.SetText(address.Text + "#" + code);
        content.Controls.Add(new Label { Text = "연결 코드  " + code, AutoSize = true,
            Font = new Font(Font, FontStyle.Bold) }, 0, 4);
        var status = new Label { AutoSize = true, ForeColor = Color.FromArgb(57, 69, 93) };
        content.Controls.Add(status, 0, 5);
        Action showStatus = () => {
            copy.Enabled = address.SelectedIndex >= 0;
            status.Text = !copy.Enabled ? "PC 네트워크를 확인한 뒤 주소를 새로고침하세요."
                : pending != null ? "연결을 기다리는 중 · 연결되면 버튼 설정이 열려요." : "";
        };
        address.SelectedIndexChanged += (s, e) => showStatus();
        refresh.Click += (s, e) => { address.RefreshAddresses(); showStatus(); };
        showStatus();

        if (pending != null)
        {
            timer.Tick += (s, e) => {
                if (!pending.RemoteConnected) return;
                timer.Stop();
                DialogResult = DialogResult.OK;
            };
            Shown += (s, e) => timer.Start();
        }
        else
        {
            var preview = new Button { Text = "PC에서 미리 열기", AutoSize = true };
            preview.Click += (s, e) => {
                using (Process.Start(new ProcessStartInfo("http://127.0.0.1:" + port + "/#" + code) {
                    UseShellExecute = true
                })) { }
            };
            content.RowCount = 7;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            content.SetRow(status, 6);
            content.Controls.Add(preview, 0, 5);
            ClientSize = new Size(530, 295);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DeckEditor : Form
{
    private const string ActionFormat = "PocketDeck.Action";
    private readonly DeckStore store;
    private readonly DeckLayout draft;
    private readonly TableLayoutPanel preview = new TableLayoutPanel();
    private readonly NumericUpDown columns = new NumericUpDown();
    private readonly NumericUpDown rows = new NumericUpDown();
    private readonly NumericUpDown widgetWidth = new NumericUpDown { Minimum = 1, Width = 39 };
    private readonly NumericUpDown widgetHeight = new NumericUpDown { Minimum = 1, Width = 39 };
    private readonly Label widgetSizeLabel = new Label { Text = "가로 × 세로 (칸)" };
    private readonly FlowLayoutPanel widgetSize = new FlowLayoutPanel { WrapContents = false };
    private readonly TextBox name = new TextBox();
    private readonly TextBox icon = new TextBox();
    private readonly Label iconLabel = new Label();
    private readonly TextBox target = new TextBox();
    private readonly Label targetLabel = new Label();
    private readonly ComboBox action = new ComboBox();
    private readonly Button color = new Button();
    private readonly Button browse = new Button();
    private readonly Label status = new Label();
    private readonly Label selection = new Label();
    private readonly List<Button> tiles = new List<Button>();
    private readonly ToolStripMenuItem appMenu = new ToolStripMenuItem("Pocket Deck");
    private readonly ToolStripMenuItem updateMenu = new ToolStripMenuItem("업데이트 확인");
    private readonly ToolStripMenuItem repositoryMenu = new ToolStripMenuItem("GitHub 업데이트 저장소…");
    private UpdateAsset availableUpdate;
    private bool checkingUpdate;
    private int selected;
    private int dragSource = -1;
    private int dropTarget = -1;
    private bool loading;
    private bool dirty;

    public DeckEditor(DeckStore store, string connectionCode, int port)
    {
        this.store = store;
        draft = store.Read();
        Text = "Pocket Deck " + BuildInfo.Version;
        ClientSize = new Size(960, 600);
        MinimumSize = new Size(765, 575);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Malgun Gothic", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 247, 251);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(root);
        var menu = new MenuStrip();
        menu.Items.Add(appMenu);
        appMenu.DropDownItems.Add(updateMenu);
        updateMenu.Click += async (s, e) => await CheckUpdates(true);
        appMenu.DropDownItems.Add(repositoryMenu);
        repositoryMenu.Click += (s, e) => ConfigureRepository();
        appMenu.DropDownItems.Add("기기 화면 디자인 폴더 열기", null, (s, e) => OpenDesignFolder());
        var settings = new ToolStripMenuItem("설정");
        menu.Items.Add(settings);
        settings.DropDownItems.Add("기기 연결…", null, (s, e) => {
            using (var window = new PairingWindow(connectionCode, port)) window.ShowDialog(this);
        });
        var startWithWindows = new ToolStripMenuItem("Windows 시작 시 실행") {
            Enabled = InstallPaths.IsInstalled
        };
        settings.DropDownItems.Add(startWithWindows);
        settings.DropDownOpening += (s, e) => startWithWindows.Checked = InstallPaths.StartsWithWindows;
        startWithWindows.Click += (s, e) => {
            try { InstallPaths.SetStartsWithWindows(!startWithWindows.Checked); }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text); }
            startWithWindows.Checked = InstallPaths.StartsWithWindows;
        };
        if (!InstallPaths.IsInstalled)
        {
            updateMenu.Enabled = false;
            repositoryMenu.Enabled = false;
        }
        else Shown += async (s, e) => await CheckUpdates(false);
        MainMenuStrip = menu;
        Controls.Add(menu);

        var canvas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = new Padding(0, 0, 16, 0) };
        canvas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.Controls.Add(canvas, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        canvas.Controls.Add(toolbar, 0, 0);
        AddSize(toolbar, "열", columns, draft.columns, 5);
        AddSize(toolbar, "행", rows, draft.rows, 4);
        toolbar.Controls.Add(new Label { Text = "오른쪽 동작을 원하는 칸에 끌어놓으세요.", AutoSize = true,
            Margin = new Padding(12, 8, 0, 0) });
        preview.Dock = DockStyle.Fill;
        preview.Margin = Padding.Empty;
        preview.Padding = new Padding(8);
        preview.BackColor = Color.FromArgb(14, 19, 32);
        canvas.Controls.Add(preview, 0, 1);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5,
            Margin = new Padding(0, 10, 0, 0) };
        foreach (int width in new[] { 36, 42, 22 })
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        foreach (int height in new[] { 28, 22, 35, 22, 31 })
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        canvas.Controls.Add(fields, 0, 2);
        selection.Font = new Font(Font, FontStyle.Bold);
        selection.AutoSize = true;
        selection.Margin = new Padding(3, 4, 3, 3);
        fields.Controls.Add(selection, 0, 0);
        fields.SetColumnSpan(selection, 3);
        name.MaxLength = 32;
        icon.MaxLength = 8;
        target.MaxLength = 1024;
        AddField(fields, new Label { Text = "버튼 이름" }, name, 0, 1);
        AddField(fields, iconLabel, icon, 1, 1);
        color.Height = 27;
        color.FlatStyle = FlatStyle.Flat;
        AddField(fields, new Label { Text = "배경색" }, color, 2, 1);
        action.DropDownStyle = ComboBoxStyle.DropDownList;
        action.Items.AddRange(DeckActions.Names);
        AddField(fields, new Label { Text = "동작 / 위젯" }, action, 0, 3);
        AddField(fields, targetLabel, target, 1, 3);
        browse.Text = "프로그램 선택…";
        browse.Height = 28;
        browse.Click += BrowseProgram;
        fields.Controls.Add(browse, 2, 4);
        browse.Dock = DockStyle.Top;
        widgetWidth.AccessibleName = "위젯 가로 칸 수";
        widgetHeight.AccessibleName = "위젯 세로 칸 수";
        widgetWidth.Margin = widgetHeight.Margin = Padding.Empty;
        widgetSize.Controls.AddRange(new Control[] { widgetWidth,
            new Label { Text = "×", AutoSize = true, Margin = new Padding(3, 3, 3, 0) }, widgetHeight });
        AddField(fields, widgetSizeLabel, widgetSize, 2, 3);
        root.Controls.Add(CreateActionPanel(), 1, 0);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(0, 9, 0, 0) };
        root.Controls.Add(footer, 0, 1);
        root.SetColumnSpan(footer, 2);
        var apply = MakeButton("기기에 적용", (s, e) => Apply());
        apply.BackColor = Color.FromArgb(40, 73, 197);
        apply.ForeColor = Color.White;
        apply.FlatStyle = FlatStyle.Flat;
        footer.Controls.Add(apply);
        status.AutoSize = true;
        status.Margin = new Padding(10, 8, 0, 0);
        footer.Controls.Add(status);

        name.TextChanged += (s, e) => UpdateButton();
        icon.TextChanged += (s, e) => UpdateButton();
        target.TextChanged += (s, e) => UpdateButton();
        action.SelectedIndexChanged += (s, e) => {
            if (!loading && action.SelectedIndex >= 0) AssignAction(selected, action.SelectedIndex);
        };
        color.Click += (s, e) => {
            using (var picker = new ColorDialog { Color = color.BackColor, FullOpen = true })
                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    color.BackColor = picker.Color;
                    UpdateButton();
                }
        };
        columns.ValueChanged += (s, e) => ResizeGrid();
        rows.ValueChanged += (s, e) => ResizeGrid();
        widgetWidth.ValueChanged += (s, e) => ResizeWidget();
        widgetHeight.ValueChanged += (s, e) => ResizeWidget();
        FormClosing += (s, e) => {
            if (dirty && MessageBox.Show(this, "적용하지 않은 변경이 있어요. 닫을까요?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
        };
        Rebuild();
        SelectTile(0);
    }

    private Control CreateActionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "동작", AutoSize = true, Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(4, 7, 0, 0) }, 0, 0);
        var actions = new TreeView { Dock = DockStyle.Fill, HideSelection = false,
            BorderStyle = BorderStyle.FixedSingle, ShowLines = false, ItemHeight = 27, Margin = Padding.Empty };
        panel.Controls.Add(actions, 0, 1);
        var system = actions.Nodes.Add("시스템");
        AddAction(system, 7);
        AddAction(system, 8);
        var media = actions.Nodes.Add("미디어");
        for (int i = 1; i <= 6; i++) AddAction(media, i);
        var widgets = actions.Nodes.Add("위젯");
        AddAction(widgets, 9);
        AddAction(widgets, 10);
        AddAction(widgets, 11);
        actions.ExpandAll();
        actions.ItemDrag += (s, e) => {
            var node = (TreeNode)e.Item;
            if (!(node.Tag is int) || e.Button != MouseButtons.Left) return;
            var data = new DataObject(ActionFormat, DeckActions.Ids[(int)node.Tag]);
            try { actions.DoDragDrop(data, DragDropEffects.Copy); }
            finally { HighlightDropTarget(-1); }
        };
        actions.NodeMouseDoubleClick += (s, e) => {
            if (e.Node.Tag is int) AssignAction(selected, (int)e.Node.Tag);
        };
        actions.KeyDown += (s, e) => {
            if (e.KeyCode != Keys.Enter || actions.SelectedNode == null || !(actions.SelectedNode.Tag is int)) return;
            AssignAction(selected, (int)actions.SelectedNode.Tag);
            e.SuppressKeyPress = true;
        };
        return panel;
    }

    private static void AddAction(TreeNode category, int index)
    {
        category.Nodes.Add(new TreeNode(DeckActions.Names[index]) { Tag = index });
    }

    private static string ReadRepository()
    {
        return File.Exists(InstallPaths.RepositoryFile)
            ? File.ReadAllText(InstallPaths.RepositoryFile, Encoding.UTF8).Trim()
            : InstallPaths.ReadResource("PocketDeck.Repository").Trim();
    }

    private void ConfigureRepository()
    {
        using (var dialog = new Form { Text = "GitHub 업데이트 저장소", ClientSize = new Size(405, 120),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = Font })
        {
            var input = new TextBox { Text = ReadRepository(), Left = 14, Top = 36, Width = 375 };
            var label = new Label { Text = "공개 저장소의 소유자/저장소 이름을 입력하세요.", Left = 14, Top = 12,
                AutoSize = true };
            var save = new Button { Text = "저장", Left = 308, Top = 76, Width = 80 };
            dialog.Controls.AddRange(new Control[] { label, input, save });
            dialog.AcceptButton = save;
            save.Click += (s, e) => {
                string repository = input.Text.Trim();
                if (repository.Length != 0 && !UpdateClient.IsRepository(repository))
                {
                    MessageBox.Show(dialog, "예시 형식은 owner/PocketDeck입니다.", dialog.Text);
                    return;
                }
                try
                {
                    using (var contents = new MemoryStream(Encoding.UTF8.GetBytes(repository)))
                        InstallPaths.WriteAtomic(InstallPaths.RepositoryFile, contents);
                    availableUpdate = null;
                    SetUpdateLabel();
                    dialog.DialogResult = DialogResult.OK;
                }
                catch (Exception error) { MessageBox.Show(dialog, error.Message, dialog.Text); }
            };
            dialog.ShowDialog(this);
        }
    }

    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        updateMenu.Enabled = false;
        repositoryMenu.Enabled = false;
        try
        {
            string repository = ReadRepository();
            if (repository.Length == 0)
            {
                if (manual) ConfigureRepository();
                repository = ReadRepository();
                if (repository.Length == 0) return;
            }
            availableUpdate = await Task.Run(() => UpdateClient.Latest(repository));
            SetUpdateLabel();
            if (!manual) return;
            if (availableUpdate == null)
            {
                MessageBox.Show(this, "현재 최신 버전입니다.", Text);
                return;
            }
            if (MessageBox.Show(this, "v" + availableUpdate.Version + "을 설치하고 앱을 다시 열까요?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var asset = availableUpdate;
            status.Text = "GitHub에서 업데이트를 받는 중…";
            string setup = await Task.Run(() => UpdateClient.Download(asset, InstallPaths.Updates));
            if (IsDisposed) return;
            bool closing = false;
            FormClosedEventHandler applyUpdate = (s, e) => {
                closing = true;
                try
                {
                    using (var process = Process.GetCurrentProcess())
                        InstallPaths.Start(setup, "--update " + process.Id);
                }
                catch (Exception error) { MessageBox.Show(error.Message, "Pocket Deck 업데이트"); }
            };
            FormClosed += applyUpdate;
            Close();
            if (!closing)
            {
                FormClosed -= applyUpdate;
                File.Delete(setup);
                status.Text = "업데이트 설치를 취소했어요.";
            }
        }
        catch (Exception error)
        {
            if (manual && !IsDisposed)
            {
                status.Text = "업데이트를 완료하지 못했어요.";
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            checkingUpdate = false;
            if (!IsDisposed)
            {
                updateMenu.Enabled = true;
                repositoryMenu.Enabled = true;
            }
        }
    }

    private void SetUpdateLabel()
    {
        appMenu.Text = availableUpdate == null ? "Pocket Deck" : "Pocket Deck · 업데이트 있음";
        updateMenu.Text = availableUpdate == null ? "업데이트 확인" : "v" + availableUpdate.Version + " 설치…";
    }

    private void OpenDesignFolder()
    {
        try
        {
            string folder = InstallPaths.IsInstalled ? Path.Combine(InstallPaths.Data, "phone")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "phone");
            Directory.CreateDirectory(folder);
            foreach (string name in new[] { "index.html", "phone.css", "phone.js" })
            {
                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                    using (var source = InstallPaths.Resource("Phone." + name))
                    using (var output = new FileStream(path, FileMode.CreateNew))
                        source.CopyTo(output);
            }
            InstallPaths.Start(folder, "");
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, Text); }
    }

    private static Button MakeButton(string title, EventHandler onClick)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 29 };
        button.Click += onClick;
        return button;
    }

    private static void AddSize(FlowLayoutPanel bar, string text, NumericUpDown control, int value, int max)
    {
        bar.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(4, 8, 3, 0) });
        control.Minimum = 1;
        control.Maximum = max;
        control.Value = value;
        control.Width = 44;
        bar.Controls.Add(control);
    }

    private static void AddField(TableLayoutPanel fields, Label label, Control control, int column, int row)
    {
        label.AutoSize = true;
        label.Margin = new Padding(3, 4, 3, 1);
        fields.Controls.Add(label, column, row);
        fields.Controls.Add(control, column, row + 1);
        control.Dock = DockStyle.Top;
    }

    private void BrowseProgram(object sender, EventArgs args)
    {
        using (var dialog = new OpenFileDialog { Filter = "Windows 프로그램 (*.exe)|*.exe" })
            if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void ResizeGrid()
    {
        if (loading) return;
        int oldColumns = draft.columns, oldRows = draft.rows;
        draft.columns = (int)columns.Value;
        draft.rows = (int)rows.Value;
        int total = draft.columns * draft.rows;
        while (draft.buttons.Count < total)
            draft.buttons.Add(DeckActions.DefaultButton(0));
        int[] coverage = DeckGrid.Coverage(draft);
        if (coverage == null)
        {
            draft.columns = oldColumns;
            draft.rows = oldRows;
            loading = true;
            columns.Value = oldColumns;
            rows.Value = oldRows;
            loading = false;
            status.Text = "위젯이 격자 밖으로 나가거나 다른 타일과 겹쳐요.";
            return;
        }
        selected = coverage[Math.Min(selected, total - 1)];
        Rebuild();
        SelectTile(selected);
        MarkDirty();
    }

    private void ResizeWidget()
    {
        if (loading) return;
        DeckButton button = draft.buttons[selected];
        int oldWidth = DeckGrid.Width(button), oldHeight = DeckGrid.Height(button);
        button.width = (int)widgetWidth.Value;
        button.height = (int)widgetHeight.Value;
        if (DeckGrid.Coverage(draft) == null)
        {
            button.width = oldWidth;
            button.height = oldHeight;
            loading = true;
            widgetWidth.Value = oldWidth;
            widgetHeight.Value = oldHeight;
            loading = false;
            status.Text = "위젯을 늘리려면 오른쪽과 아래쪽에 빈 칸이 필요해요.";
            return;
        }
        Rebuild();
        SelectTile(selected);
        MarkDirty();
    }

    private void Rebuild()
    {
        preview.SuspendLayout();
        foreach (Button tile in tiles)
            if (tile != null) tile.Dispose();
        tiles.Clear();
        preview.ColumnStyles.Clear();
        preview.RowStyles.Clear();
        preview.ColumnCount = draft.columns;
        preview.RowCount = draft.rows;
        for (int i = 0; i < draft.columns; i++)
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / draft.columns));
        for (int i = 0; i < draft.rows; i++)
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / draft.rows));
        int[] coverage = DeckGrid.Coverage(draft);
        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i] != i) { tiles.Add(null); continue; }
            int index = i;
            var tile = new Button { Dock = DockStyle.Fill, Margin = new Padding(5), FlatStyle = FlatStyle.Flat,
                AllowDrop = true, UseMnemonic = false, Cursor = Cursors.Hand,
                TextImageRelation = TextImageRelation.ImageAboveText };
            tile.Disposed += (s, e) => { if (tile.Image != null) tile.Image.Dispose(); };
            tile.Click += (s, e) => SelectTile(index);
            Point? start = null;
            tile.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) start = e.Location; };
            tile.MouseUp += (s, e) => start = null;
            tile.MouseMove += (s, e) => {
                if (start.HasValue && e.Button == MouseButtons.Left &&
                    (Math.Abs(e.X - start.Value.X) >= SystemInformation.DragSize.Width ||
                     Math.Abs(e.Y - start.Value.Y) >= SystemInformation.DragSize.Height))
                {
                    start = null;
                    DragTile(tile, index);
                }
            };
            tile.DragEnter += (s, e) => UpdateDropTarget(e, index);
            tile.DragOver += (s, e) => UpdateDropTarget(e, index);
            tile.DragLeave += (s, e) => {
                if (dropTarget == index) HighlightDropTarget(-1);
            };
            tile.DragDrop += (s, e) => {
                HighlightDropTarget(-1);
                int pickedAction = ActionFromDrop(e.Data);
                if (pickedAction > 0)
                {
                    BeginInvoke((Action)(() => AssignAction(index, pickedAction)));
                    return;
                }
                object data = e.Data.GetData(typeof(int));
                if (!(data is int)) return;
                int from = (int)data;
                if (from == index || from < 0 || from >= tiles.Count) return;
                BeginInvoke((Action)(() => MoveTile(from, index)));
            };
            tiles.Add(tile);
            preview.Controls.Add(tile, i % draft.columns, i / draft.columns);
            preview.SetColumnSpan(tile, DeckGrid.Width(draft.buttons[i]));
            preview.SetRowSpan(tile, DeckGrid.Height(draft.buttons[i]));
        }
        preview.ResumeLayout();
    }

    private void Swap(int first, int second)
    {
        DeckButton button = draft.buttons[first];
        draft.buttons[first] = draft.buttons[second];
        draft.buttons[second] = button;
    }

    private bool CanMoveTile(int from, int to)
    {
        Swap(from, to);
        bool fits = DeckGrid.Coverage(draft) != null;
        Swap(from, to);
        return fits;
    }

    private void MoveTile(int from, int to)
    {
        if (!CanMoveTile(from, to)) return;
        Swap(from, to);
        Rebuild();
        SelectTile(to);
        MarkDirty();
    }

    private void DragTile(Button tile, int index)
    {
        using (var ghost = new DragGhost(tile))
        {
            GiveFeedbackEventHandler feedback = (s, e) => ghost.FollowPointer();
            dragSource = index;
            tile.GiveFeedback += feedback;
            try
            {
                DrawTile(index);
                ghost.FollowPointer();
                ghost.Show(this);
                tile.DoDragDrop(index, DragDropEffects.Move);
            }
            finally
            {
                ghost.Hide();
                tile.GiveFeedback -= feedback;
                dragSource = -1;
                HighlightDropTarget(-1);
                DrawTile(index);
            }
        }
    }

    private void UpdateDropTarget(DragEventArgs args, int index)
    {
        if (ActionFromDrop(args.Data) > 0) args.Effect = DragDropEffects.Copy;
        else if (dragSource >= 0 && args.Data.GetDataPresent(typeof(int)))
            args.Effect = index != dragSource && CanMoveTile(dragSource, index) ? DragDropEffects.Move : DragDropEffects.None;
        else return;
        HighlightDropTarget(args.Effect == DragDropEffects.None ? -1 : index);
    }

    private static int ActionFromDrop(IDataObject data)
    {
        return Array.IndexOf(DeckActions.Ids, data.GetData(ActionFormat, false) as string);
    }

    private void HighlightDropTarget(int index)
    {
        if (index == dropTarget) return;
        int previous = dropTarget;
        dropTarget = index;
        if (previous >= 0) DrawTile(previous);
        if (index >= 0) DrawTile(index);
    }

    private void SelectTile(int index)
    {
        selected = index;
        DeckButton button = draft.buttons[index];
        loading = true;
        selection.Text = "버튼 설정 · " + (index / draft.columns + 1) + "행 " + (index % draft.columns + 1) + "열";
        name.Text = button.label;
        icon.Text = button.icon;
        color.BackColor = ParseColor(button.color);
        action.SelectedIndex = Math.Max(0, Array.IndexOf(DeckActions.Ids, button.action));
        target.Text = button.target;
        widgetWidth.Maximum = draft.columns - index % draft.columns;
        widgetHeight.Maximum = draft.rows - index / draft.columns;
        widgetWidth.Value = DeckGrid.Width(button);
        widgetHeight.Value = DeckGrid.Height(button);
        UpdateTarget();
        loading = false;
        for (int i = 0; i < tiles.Count; i++) DrawTile(i);
    }

    private void UpdateTarget()
    {
        bool opensProgram = action.SelectedIndex == 7;
        bool weather = action.SelectedIndex == 10;
        bool widget = action.SelectedIndex >= 9;
        iconLabel.Text = opensProgram ? "아이콘 · 비워두면 프로그램 아이콘" : "아이콘 · 문자 또는 이모지";
        targetLabel.Text = opensProgram ? "프로그램 경로" : weather ? "도시 이름" : "단축키";
        targetLabel.Visible = target.Visible = opensProgram || weather || action.SelectedIndex == 8;
        browse.Visible = opensProgram;
        widgetSizeLabel.Visible = widgetSize.Visible = widget;
        iconLabel.Visible = icon.Visible = !widget;
    }

    private void AssignAction(int index, int next)
    {
        DeckButton button = draft.buttons[index];
        if (button.action != DeckActions.Ids[next])
        {
            int previous = Math.Max(0, Array.IndexOf(DeckActions.Ids, button.action));
            bool wasWidget = DeckActions.IsWidget(button);
            DeckButton oldDefault = DeckActions.DefaultButton(previous);
            DeckButton newDefault = DeckActions.DefaultButton(next);
            if (String.IsNullOrWhiteSpace(button.label) || button.label == oldDefault.label)
                button.label = newDefault.label;
            if (String.IsNullOrWhiteSpace(button.icon) || button.icon == oldDefault.icon ||
                DeckActions.UsesProgramIcon(button))
                button.icon = newDefault.icon;
            if (String.Equals(button.color, oldDefault.color, StringComparison.OrdinalIgnoreCase))
                button.color = newDefault.color;
            button.action = newDefault.action;
            button.target = "";
            if (!DeckActions.IsWidget(button))
            {
                button.width = 0;
                button.height = 0;
            }
            if (wasWidget) Rebuild();
            MarkDirty();
        }
        SelectTile(index);
    }

    private void UpdateButton()
    {
        if (loading || action.SelectedIndex < 0) return;
        DeckButton button = draft.buttons[selected];
        button.label = name.Text;
        button.icon = icon.Text;
        button.color = ColorTranslator.ToHtml(Color.FromArgb(color.BackColor.R, color.BackColor.G, color.BackColor.B));
        button.action = DeckActions.Ids[action.SelectedIndex];
        button.target = target.Text.Trim();
        DrawTile(selected);
        MarkDirty();
    }

    private static Color ParseColor(string value)
    {
        try { return ColorTranslator.FromHtml(value); }
        catch (ArgumentException) { return Color.FromArgb(57, 69, 93); }
    }

    private void DrawTile(int index)
    {
        Button tile = tiles[index];
        if (tile == null) return;
        DeckButton button = draft.buttons[index];
        bool programIcon = DeckActions.UsesProgramIcon(button);
        string imagePath = programIcon ? button.target : null;
        if (!String.Equals(tile.Tag as string, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            Image oldImage = tile.Image;
            tile.Image = DeckActions.ReadProgramIcon(imagePath);
            tile.Tag = imagePath;
            if (oldImage != null) oldImage.Dispose();
        }
        string caption = tile.Image != null ? button.label :
            ((programIcon ? "↗" : button.icon) + "\n" + button.label).Trim();
        if (DeckActions.IsWidget(button))
            caption = button.label + "\n" + DeckGrid.Width(button) + " × " + DeckGrid.Height(button);
        tile.Text = String.IsNullOrWhiteSpace(caption) ? "빈 버튼" : caption;
        Color background = ParseColor(button.color);
        if (index == dragSource)
            background = Color.FromArgb((background.R + 14) / 2, (background.G + 19) / 2, (background.B + 32) / 2);
        tile.BackColor = background;
        tile.ForeColor = background.R * 0.299 + background.G * 0.587 + background.B * 0.114 > 158
            ? Color.FromArgb(17, 24, 39) : Color.White;
        tile.FlatAppearance.BorderSize = index == dropTarget ? 3 : index == selected ? 2 : 1;
        tile.FlatAppearance.BorderColor = index == dropTarget ? Color.FromArgb(128, 186, 255)
            : index == selected ? Color.White : background;
    }

    private void MarkDirty()
    {
        dirty = true;
        status.Text = "편집 중 · 적용하면 연결한 기기에 반영돼요.";
    }

    private void Apply()
    {
        try
        {
            for (int i = 0; i < draft.columns * draft.rows; i++)
            {
                DeckButton button = draft.buttons[i];
                try
                {
                    if (button.action == "shortcut") DeckActions.ParseShortcut(button.target);
                    if (button.action == "weather" && (String.IsNullOrWhiteSpace(button.target) || button.target.Length > 80))
                        throw new InvalidOperationException("날씨를 표시할 도시 이름을 입력해 주세요.");
                    if (button.action == "open" && (!Path.IsPathRooted(button.target) ||
                        !File.Exists(button.target) ||
                        !String.Equals(Path.GetExtension(button.target), ".exe", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("실행할 .exe 파일을 선택해 주세요.");
                }
                catch (InvalidOperationException error)
                {
                    SelectTile(i);
                    MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            store.Save(draft);
            dirty = false;
            status.Text = "적용 완료 · 연결한 기기가 배치를 자동으로 불러와요.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (HandleUninstall(args)) return;
        bool owner;
        using (var instance = new Mutex(true, InstallPaths.AppMutex, out owner))
        {
            if (!owner)
            {
                MessageBox.Show("Pocket Deck이 이미 실행 중이에요.", "Pocket Deck");
                return;
            }
            try
            {
                string folder = InstallPaths.IsInstalled ? InstallPaths.Data : AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(folder);
                string web = Path.Combine(folder, "phone");
                RunDeck(folder, web);
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Pocket Deck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void RunDeck(string folder, string web)
    {
        string layout = Path.Combine(folder, "layout.json");
        var pairing = new DevicePairing(Path.Combine(folder, "device-connection.txt"), File.Exists(layout));
        var store = new DeckStore(layout);
        using (var server = new PhoneServer(store, web, code: pairing.Code))
        {
            bool newlyConnected = !pairing.Completed;
            if (newlyConnected)
            {
                using (var window = new PairingWindow(server))
                    if (window.ShowDialog() != DialogResult.OK) return;
                pairing.Complete();
            }
            Application.Run(new DeckEditor(store, server.Code, server.PortNumber));
        }
    }

    private static bool HandleUninstall(string[] args)
    {
        if (args.Length == 0) return false;
        try
        {
            if (args.Length == 1 && args[0] == "--uninstall" && InstallPaths.IsInstalled)
            {
                if (MessageBox.Show("Pocket Deck을 제거할까요? 저장된 버튼과 디자인은 유지됩니다.",
                    "Pocket Deck 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Directory.CreateDirectory(InstallPaths.Updates);
                    string worker = Path.Combine(InstallPaths.Updates, "Remove-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(InstallPaths.ProgramPath, worker, false);
                    using (var process = Process.GetCurrentProcess())
                        InstallPaths.Start(worker, "--remove-installed " + process.Id);
                }
                return true;
            }
            int pid;
            if (args.Length == 2 && args[0] == "--remove-installed" && Int32.TryParse(args[1], out pid))
            {
                InstallPaths.WaitFor(pid);
                InstallPaths.Uninstall();
                MessageBox.Show("Pocket Deck을 제거했어요.", "Pocket Deck 제거");
                return true;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Pocket Deck 제거", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return true;
        }
        return false;
    }
}
