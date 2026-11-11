using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class UpdateAsset
{
    public Version Version;
    public Uri Download;
    public string Digest;
    public int Size;
}

internal static class UpdateClient
{
    public const string AssetName = "PocketDeckSetup.exe";
    private const int MaxDownload = 64 * 1024 * 1024;

    private sealed class Release
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public List<Asset> assets { get; set; }
    }

    private sealed class Asset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public int size { get; set; }
    }

    public static bool IsRepository(string value)
    {
        return Regex.IsMatch(value ?? "", @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}\z") &&
            !value.EndsWith("/.") && !value.EndsWith("/..");
    }

    public static UpdateAsset Latest(string repository)
    {
        if (!IsRepository(repository)) throw new InvalidOperationException("GitHub 저장소를 먼저 설정해 주세요.");
        var url = new Uri("https://api.github.com/repos/" + repository + "/releases/latest");
        try
        {
            using (var response = Request(url, "application/vnd.github+json"))
            using (var reader = new StreamReader(response.GetResponseStream()))
                return ParseRelease(repository, reader.ReadToEnd(), new Version(BuildInfo.Version));
        }
        catch (WebException error)
        {
            var response = error.Response as HttpWebResponse;
            if (response == null) throw;
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("공개 저장소와 게시된 GitHub Release가 있는지 확인해 주세요.");
            }
            throw;
        }
    }

    public static UpdateAsset ParseRelease(string repository, string json, Version current)
    {
        var release = new JavaScriptSerializer().Deserialize<Release>(json);
        Version version;
        if (release == null || release.draft || release.prerelease ||
            !Regex.IsMatch(release.tag_name ?? "", @"\Av[0-9]+\.[0-9]+\.[0-9]+\z") ||
            !Version.TryParse(release.tag_name.Substring(1), out version) || version <= current) return null;

        string expected = "https://github.com/" + repository + "/releases/download/" + release.tag_name + "/" + AssetName;
        if (release.assets != null)
        {
            foreach (Asset asset in release.assets)
            {
                if (asset.name != AssetName) continue;
                if (!String.Equals(expected, asset.browser_download_url, StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(asset.digest ?? "", @"\Asha256:[a-fA-F0-9]{64}\z") ||
                    asset.size < 1 || asset.size > MaxDownload)
                    throw new InvalidDataException("설치 파일의 주소나 SHA-256 정보를 확인할 수 없습니다.");
                return new UpdateAsset { Version = version, Download = new Uri(expected),
                    Digest = asset.digest.Substring(7), Size = asset.size };
            }
        }
        throw new InvalidDataException("새 릴리스에 " + AssetName + " 파일이 없습니다.");
    }

    public static string Download(UpdateAsset asset, string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "PocketDeckSetup-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using (HttpWebResponse response = DownloadResponse(asset.Download))
            using (Stream input = response.GetResponseStream())
                SaveVerified(input, path, asset);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public static void SaveVerified(Stream input, string path, UpdateAsset asset)
    {
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var sha = SHA256.Create())
        {
            byte[] buffer = new byte[8192];
            int total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > asset.Size) throw new InvalidDataException("설치 파일 크기가 릴리스 정보와 다릅니다.");
                sha.TransformBlock(buffer, 0, read, null, 0);
                output.Write(buffer, 0, read);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            string actual = BitConverter.ToString(sha.Hash).Replace("-", "");
            if (total != asset.Size || !String.Equals(actual, asset.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("설치 파일의 SHA-256이 릴리스 정보와 다릅니다.");
        }
    }

    private static HttpWebResponse DownloadResponse(Uri uri)
    {
        for (int i = 0; i < 5; i++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort ||
                (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" &&
                 uri.Host != "objects.githubusercontent.com"))
                throw new InvalidDataException("GitHub 다운로드 주소를 확인할 수 없습니다.");
            var response = Request(uri, "application/octet-stream");
            if (response.StatusCode == HttpStatusCode.OK) return response;
            using (response)
            {
                string location = response.Headers["Location"];
                if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 ||
                    String.IsNullOrWhiteSpace(location)) break;
                uri = new Uri(uri, location);
            }
        }
        throw new InvalidDataException("GitHub에서 설치 파일을 내려받을 수 없습니다.");
    }

    private static HttpWebResponse Request(Uri uri, string accept)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.UserAgent = "PocketDeck/" + BuildInfo.Version;
        request.Accept = accept;
        request.Timeout = 12000;
        request.ReadWriteTimeout = 12000;
        request.AllowAutoRedirect = false;
        return (HttpWebResponse)request.GetResponse();
    }
}
