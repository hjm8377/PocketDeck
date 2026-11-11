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
        Save(new DeckLayout {
            columns = 3, rows = 2, buttons = new List<DeckButton> {
                Button("재생 / 일시정지", "⏯", "#365CE8", "play"),
                Button("이전 곡", "⏮", "#39455D", "previous"),
                Button("다음 곡", "⏭", "#39455D", "next"),
                Button("볼륨 내리기", "−", "#275D59", "volumeDown"),
                Button("볼륨 올리기", "+", "#275D59", "volumeUp"),
                Button("음소거", "♪", "#744669", "mute")
            }
        });
    }

    public static DeckButton Button(string label, string icon, string color, string action)
    {
        return new DeckButton { label = label, icon = icon, color = color, action = action, target = "" };
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
            deck.buttons.Count > 20 || deck.buttons.Contains(null))
            throw new InvalidDataException("저장된 버튼 배치를 읽을 수 없습니다.");
    }
}

internal static class DeckActions
{
    public static readonly string[] Names = {
        "동작 없음", "재생 / 일시정지", "이전 곡", "다음 곡", "볼륨 내리기",
        "볼륨 올리기", "음소거", "프로그램 열기", "키보드 단축키"
    };
    public static readonly string[] Ids = {
        "none", "play", "previous", "next", "volumeDown", "volumeUp", "mute", "open", "shortcut"
    };
    private static readonly Keys[] Media = {
        Keys.MediaPlayPause, Keys.MediaPreviousTrack, Keys.MediaNextTrack,
        Keys.VolumeDown, Keys.VolumeUp, Keys.VolumeMute
    };

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scan, uint flags, UIntPtr extra);

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

    public PhoneServer(DeckStore store, string web, int preferredPort = Port)
    {
        this.store = store;
        this.web = web;
        byte[] random = new byte[8];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        StringBuilder code = new StringBuilder();
        foreach (byte value in random) code.Append(alphabet[value & 31]);
        Code = code.ToString();
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
        result.Add(Url("127.0.0.1", port));
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
            var buttons = new List<object>();
            for (int i = 0; i < deck.columns * deck.rows; i++)
            {
                DeckButton button = deck.buttons[i];
                buttons.Add(new { label = button.label, icon = button.icon, color = button.color,
                    enabled = button.action != "none" });
            }
            Reply(stream, "200 OK", "application/json", new JavaScriptSerializer().Serialize(
                new { version = deck.version, columns = deck.columns, rows = deck.rows, buttons = buttons }));
            return;
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
                if (deck.buttons[index].action == "none")
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

    private void Asset(NetworkStream stream, string name, string type)
    {
        string path = Path.Combine(web, name);
        string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8)
            : InstallPaths.ReadResource("Phone." + name);
        Reply(stream, "200 OK", type, text);
    }

    private static void Reply(NetworkStream stream, string status, string type, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string length = status.StartsWith("204") ? "" : "Content-Length: " + bytes.Length + "\r\n";
        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
            "\r\nContent-Type: " + type + "; charset=utf-8\r\n" + length +
            "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        stream.Write(header, 0, header.Length);
        if (bytes.Length > 0) stream.Write(bytes, 0, bytes.Length);
    }
}

internal sealed class DragGhost : Form
{
    public DragGhost(Button tile)
    {
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
            e.Graphics.DrawString(Text, Font, text,
                new RectangleF(5, 5, ClientSize.Width - 10, ClientSize.Height - 10), format);
        }
    }
}

internal sealed class DeckEditor : Form
{
    private readonly DeckStore store;
    private readonly string connectionCode;
    private readonly int port;
    private readonly DeckLayout draft;
    private readonly TableLayoutPanel preview = new TableLayoutPanel();
    private readonly NumericUpDown columns = new NumericUpDown();
    private readonly NumericUpDown rows = new NumericUpDown();
    private readonly TextBox name = new TextBox();
    private readonly TextBox icon = new TextBox();
    private readonly TextBox target = new TextBox();
    private readonly ComboBox action = new ComboBox();
    private readonly Button color = new Button();
    private readonly Button browse = new Button();
    private readonly Label status = new Label();
    private readonly Label selection = new Label();
    private readonly ComboBox address = new ComboBox();
    private readonly List<Button> tiles = new List<Button>();
    private readonly ToolStripMenuItem appMenu = new ToolStripMenuItem("Pocket Deck");
    private readonly ToolStripMenuItem updateMenu = new ToolStripMenuItem("업데이트 확인");
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
        this.connectionCode = connectionCode;
        this.port = port;
        draft = store.Read();
        Text = "Pocket Deck " + BuildInfo.Version;
        ClientSize = new Size(930, 520);
        MinimumSize = new Size(760, 545);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Malgun Gothic", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 247, 251);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(root);
        var menu = new MenuStrip();
        menu.Items.Add(appMenu);
        appMenu.DropDownItems.Add(updateMenu);
        updateMenu.Click += async (s, e) => await CheckUpdates(true);
        appMenu.DropDownItems.Add("GitHub 업데이트 저장소…", null, (s, e) => ConfigureRepository());
        appMenu.DropDownItems.Add("아이폰 디자인 폴더 열기", null, (s, e) => OpenDesignFolder());
        if (!InstallPaths.IsInstalled)
        {
            updateMenu.Enabled = false;
            appMenu.DropDownItems[1].Enabled = false;
        }
        else Shown += async (s, e) => await CheckUpdates(false);
        MainMenuStrip = menu;
        Controls.Add(menu);

        var connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        root.Controls.Add(connection, 0, 0);
        root.SetColumnSpan(connection, 2);
        connection.Controls.Add(new Label { Text = "아이폰 주소", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        address.DropDownStyle = ComboBoxStyle.DropDownList;
        address.Width = 203;
        connection.Controls.Add(address);
        RefreshAddresses();
        connection.Controls.Add(MakeButton("새로고침", (s, e) => RefreshAddresses()));
        connection.Controls.Add(new Label { Text = "코드  " + connectionCode, AutoSize = true,
            Margin = new Padding(12, 8, 8, 0) });
        connection.Controls.Add(MakeButton("링크 복사", (s, e) => Clipboard.SetText(PairUrl())));
        connection.Controls.Add(MakeButton("PC에서 미리 열기", (s, e) =>
            Process.Start(new ProcessStartInfo("http://127.0.0.1:" + port + "/#" + connectionCode) {
                UseShellExecute = true
            })));

        var canvas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 0, 16, 0) };
        canvas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(canvas, 0, 1);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        canvas.Controls.Add(toolbar, 0, 0);
        AddSize(toolbar, "열", columns, draft.columns, 5);
        AddSize(toolbar, "행", rows, draft.rows, 4);
        toolbar.Controls.Add(new Label { Text = "버튼을 끌어서 위치를 바꿔보세요.", AutoSize = true,
            Margin = new Padding(12, 8, 0, 0) });
        preview.Dock = DockStyle.Fill;
        preview.Margin = Padding.Empty;
        preview.Padding = new Padding(8);
        preview.BackColor = Color.FromArgb(14, 19, 32);
        canvas.Controls.Add(preview, 0, 1);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            ColumnCount = 1, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(fields, 1, 1);
        selection.Font = new Font(Font, FontStyle.Bold);
        selection.AutoSize = true;
        selection.Margin = new Padding(3, 4, 3, 3);
        fields.Controls.Add(selection);
        name.MaxLength = 32;
        icon.MaxLength = 8;
        target.MaxLength = 1024;
        AddField(fields, "버튼 이름", name);
        AddField(fields, "아이콘 · 문자 또는 이모지", icon);
        color.Height = 27;
        color.FlatStyle = FlatStyle.Flat;
        AddField(fields, "배경색", color);
        action.DropDownStyle = ComboBoxStyle.DropDownList;
        action.Items.AddRange(DeckActions.Names);
        AddField(fields, "실행 동작", action);
        AddField(fields, "프로그램 경로 / 단축키", target);
        browse.Text = "프로그램 선택…";
        browse.Height = 28;
        browse.Click += BrowseProgram;
        fields.Controls.Add(browse);
        browse.Dock = DockStyle.Top;

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(0, 9, 0, 0) };
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);
        var apply = MakeButton("아이폰에 적용", (s, e) => Apply());
        apply.BackColor = Color.FromArgb(40, 73, 197);
        apply.ForeColor = Color.White;
        apply.FlatStyle = FlatStyle.Flat;
        footer.Controls.Add(apply);
        status.AutoSize = true;
        status.Margin = new Padding(10, 8, 0, 0);
        footer.Controls.Add(status);
        status.Text = "아이폰 Safari에서 위 주소를 열고 코드를 입력하세요.";

        name.TextChanged += (s, e) => UpdateButton();
        icon.TextChanged += (s, e) => UpdateButton();
        target.TextChanged += (s, e) => UpdateButton();
        action.SelectedIndexChanged += (s, e) => {
            if (!loading && action.SelectedIndex >= 0)
            {
                loading = true;
                target.Clear();
                loading = false;
            }
            UpdateTarget();
            UpdateButton();
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
        FormClosing += (s, e) => {
            if (dirty && MessageBox.Show(this, "적용하지 않은 변경이 있어요. 닫을까요?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
        };
        Rebuild();
        SelectTile(0);
    }

    private void RefreshAddresses()
    {
        string previous = address.Text;
        address.Items.Clear();
        foreach (string url in PhoneServer.Addresses(port)) address.Items.Add(url);
        int index = address.Items.IndexOf(previous);
        address.SelectedIndex = index >= 0 ? index : 0;
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
        appMenu.DropDownItems[1].Enabled = false;
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
                appMenu.DropDownItems[1].Enabled = true;
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

    private string PairUrl() { return address.Text + "#" + connectionCode; }

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

    private static void AddField(TableLayoutPanel fields, string label, Control control)
    {
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 1) });
        fields.Controls.Add(control);
        control.Dock = DockStyle.Top;
    }

    private void BrowseProgram(object sender, EventArgs args)
    {
        using (var dialog = new OpenFileDialog { Filter = "Windows 프로그램 (*.exe)|*.exe" })
            if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void ResizeGrid()
    {
        draft.columns = (int)columns.Value;
        draft.rows = (int)rows.Value;
        int total = draft.columns * draft.rows;
        while (draft.buttons.Count < total)
            draft.buttons.Add(DeckStore.Button("", "", "#39455D", "none"));
        selected = Math.Min(selected, total - 1);
        Rebuild();
        SelectTile(selected);
        MarkDirty();
    }

    private void Rebuild()
    {
        preview.SuspendLayout();
        foreach (Button tile in tiles) tile.Dispose();
        tiles.Clear();
        preview.ColumnStyles.Clear();
        preview.RowStyles.Clear();
        preview.ColumnCount = draft.columns;
        preview.RowCount = draft.rows;
        for (int i = 0; i < draft.columns; i++)
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / draft.columns));
        for (int i = 0; i < draft.rows; i++)
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / draft.rows));
        for (int i = 0; i < draft.columns * draft.rows; i++)
        {
            int index = i;
            var tile = new Button { Dock = DockStyle.Fill, Margin = new Padding(5), FlatStyle = FlatStyle.Flat,
                AllowDrop = true, UseMnemonic = false, Cursor = Cursors.Hand };
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
                object data = e.Data.GetData(typeof(int));
                if (!(data is int)) return;
                int from = (int)data;
                if (from == index || from < 0 || from >= tiles.Count) return;
                DeckButton moved = draft.buttons[from];
                draft.buttons[from] = draft.buttons[index];
                draft.buttons[index] = moved;
                SelectTile(index);
                MarkDirty();
            };
            tiles.Add(tile);
            preview.Controls.Add(tile, i % draft.columns, i / draft.columns);
        }
        preview.ResumeLayout();
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
        if (dragSource < 0 || !args.Data.GetDataPresent(typeof(int))) return;
        args.Effect = DragDropEffects.Move;
        HighlightDropTarget(index == dragSource ? -1 : index);
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
        selection.Text = (index / draft.columns + 1) + "행 " + (index % draft.columns + 1) + "열";
        name.Text = button.label;
        icon.Text = button.icon;
        color.BackColor = ParseColor(button.color);
        action.SelectedIndex = Math.Max(0, Array.IndexOf(DeckActions.Ids, button.action));
        target.Text = button.target;
        UpdateTarget();
        loading = false;
        for (int i = 0; i < tiles.Count; i++) DrawTile(i);
    }

    private void UpdateTarget()
    {
        target.Enabled = action.SelectedIndex >= 7;
        browse.Enabled = action.SelectedIndex == 7;
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
        DeckButton button = draft.buttons[index];
        tile.Text = String.IsNullOrWhiteSpace(button.label + button.icon)
            ? "빈 버튼" : (button.icon + "\n" + button.label).Trim();
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
        status.Text = "편집 중 · 적용하면 아이폰에 반영돼요.";
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
            status.Text = "적용 완료 · 아이폰이 배치를 자동으로 불러와요.";
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
                var store = new DeckStore(Path.Combine(folder, "layout.json"));
                using (var server = new PhoneServer(store, web))
                    Application.Run(new DeckEditor(store, server.Code, server.PortNumber));
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Pocket Deck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
