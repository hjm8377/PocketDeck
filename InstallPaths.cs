using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class InstallPaths
{
    public const string AppMutex = "Local\\PocketDeck-8765";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PocketDeck";
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pocket Deck");
    public static readonly string ProgramDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pocket Deck");
    public static readonly string ProgramPath = Path.Combine(ProgramDirectory, "PocketDeck.exe");
    public static readonly string Shortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pocket Deck.lnk");
    public static readonly string Updates = Path.Combine(Data, "Updates");
    public static readonly string RepositoryFile = Path.Combine(Data, "update-repository.txt");

    public static bool IsInstalled
    {
        get
        {
            return String.Equals(Assembly.GetExecutingAssembly().Location, ProgramPath,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ReadResource(string name)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null) throw new FileNotFoundException(name);
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
    }

    public static Stream Resource(string name)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) throw new FileNotFoundException(name);
        return stream;
    }

    public static void WriteAtomic(string path, Stream input)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temp)) input.CopyTo(output);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Install()
    {
        WithAppClosed(() => {
            InstallFiles(ProgramPath, Data);
            CreateShortcut();
            using (RegistryKey entry = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                entry.SetValue("DisplayName", "Pocket Deck");
                entry.SetValue("DisplayVersion", BuildInfo.Version);
                entry.SetValue("InstallLocation", ProgramDirectory);
                entry.SetValue("DisplayIcon", ProgramPath);
                entry.SetValue("UninstallString", Quote(ProgramPath) + " --uninstall");
                entry.SetValue("NoModify", 1);
                entry.SetValue("NoRepair", 1);
            }
        });
    }

    public static void InstallFiles(string program, string data)
    {
        if (File.Exists(program))
        {
            FileVersionInfo existing = FileVersionInfo.GetVersionInfo(program);
            if (new Version(existing.FileMajorPart, existing.FileMinorPart, existing.FileBuildPart) >
                new Version(BuildInfo.Version))
                throw new InvalidOperationException("이미 더 새로운 Pocket Deck이 설치되어 있습니다.");
        }
        Directory.CreateDirectory(data);
        using (var payload = Resource("PocketDeck.Payload")) WriteAtomic(program, payload);
    }

    private static void CreateShortcut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Shortcut));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(Shortcut);
        shortcut.TargetPath = ProgramPath;
        shortcut.WorkingDirectory = ProgramDirectory;
        shortcut.Save();
    }

    public static void Uninstall()
    {
        WithAppClosed(() => {
            File.Delete(ProgramPath);
            File.Delete(Shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            if (Directory.Exists(ProgramDirectory) && Directory.GetFileSystemEntries(ProgramDirectory).Length == 0)
                Directory.Delete(ProgramDirectory);
        });
    }

    private static void WithAppClosed(Action action)
    {
        using (var app = new Mutex(false, AppMutex))
        {
            bool locked;
            try { locked = app.WaitOne(0); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new InvalidOperationException("열려 있는 Pocket Deck을 닫고 다시 시도해 주세요.");
            try { action(); }
            finally { app.ReleaseMutex(); }
        }
    }

    public static void WaitFor(int pid)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
            if (!process.WaitForExit(30000))
                throw new InvalidOperationException("Pocket Deck이 종료되면 설치를 다시 시도해 주세요.");
    }

    public static void Start(string path, string arguments)
    {
        using (Process.Start(new ProcessStartInfo(path, arguments) {
            WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true
        })) { }
    }

    private static string Quote(string text) { return "\"" + text + "\""; }
}
