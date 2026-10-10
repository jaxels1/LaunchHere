using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LaunchHere
{
    [DataContract] internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<ReleaseAsset> Assets { get; set; }
    }
    [DataContract] internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        [DataMember(Name = "digest")] public string Digest { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }
    internal sealed class UpdateProgress
    {
        internal string Message;
        internal int Percent;
        internal UpdateProgress(string message, int percent) { Message = message; Percent = percent; }
    }
    internal sealed class DownloadedUpdate
    {
        internal string Folder, Package, Sha256;
        internal Version Version;
        internal bool Portable;
    }
    internal sealed class UpdateClient
    {
        internal const string LatestUrl = "https://api.github.com/repos/jaxels1/LaunchHere/releases/latest";
        internal const long MaximumDownload = 96L * 1024 * 1024;
        private readonly Func<HttpClient> createClient;
        // Construction is offline. Requests occur only inside the explicitly
        // invoked check method, never at startup or on a timer.
        internal UpdateClient(Func<HttpClient> factory = null)
        {
            createClient = factory ?? delegate {
                return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
            };
        }
        internal static Version CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        internal static string DisplayVersion(Version version)
        {
            return version.ToString(version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2);
        }
        internal static Version ParseVersion(string tag)
        {
            if (tag == null || !Regex.IsMatch(tag, @"^v?\d+\.\d+(\.\d+)?(\.\d+)?$"))
                throw new InvalidDataException("The release has an unsupported version number.");
            Version value;
            if (!Version.TryParse(tag.TrimStart('v'), out value)) throw new InvalidDataException("Invalid release version.");
            return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        }
        internal static ReleaseAsset SelectAsset(ReleaseInfo release, Version current, bool portable)
        {
            if (release == null || release.Draft || release.Prerelease) throw new InvalidDataException("No stable release is available.");
            Version next = ParseVersion(release.Tag);
            if (next <= current) return null;
            string version = release.Tag.TrimStart('v');
            string name = "LaunchHere-" + version + (portable ? "-Windows-x64.zip" : "-Setup-x64.exe");
            ReleaseAsset selected = null;
            foreach (var asset in release.Assets ?? new List<ReleaseAsset>())
            {
                if (asset.Name != name) continue;
                if (selected != null) throw new InvalidDataException("The release contains duplicate update files.");
                selected = asset;
            }
            if (selected == null) throw new InvalidDataException("The release does not include the required Windows " + (portable ? "ZIP." : "installer."));
            Uri url;
            string expectedUrl = "https://github.com/jaxels1/LaunchHere/releases/download/" + Uri.EscapeDataString(release.Tag) + "/" + Uri.EscapeDataString(name);
            if (!Uri.TryCreate(selected.Url, UriKind.Absolute, out url) || selected.Url != expectedUrl)
                throw new InvalidDataException("The update download is not from the LaunchHere repository.");
            if (selected.Size <= 0 || selected.Size > MaximumDownload) throw new InvalidDataException("Unexpected update file size.");
            if (selected.Digest == null || !Regex.IsMatch(selected.Digest, "^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("GitHub has not provided a checksum for this release. Automatic installation was stopped.");
            return selected;
        }
        internal async Task<DownloadedUpdate> CheckAndDownloadAsync(Version current, bool portable, string updatesRoot,
            IProgress<UpdateProgress> progress, CancellationToken cancellation)
        {
            string folder = null;
            try
            {
                using (var client = createClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("LaunchHere/" + DisplayVersion(current));
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                    progress.Report(new UpdateProgress("Checking the latest release…", 0));
                    ReleaseInfo release;
                    using (var response = await GetAsync(client, new Uri(LatestUrl), true, cancellation).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("No published release is available yet.");
                        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                            throw new InvalidDataException("GitHub is limiting requests. Try again later.");
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var metadata = new MemoryStream())
                        {
                            await CopyLimited(stream, metadata, 4 * 1024 * 1024, null, cancellation).ConfigureAwait(false);
                            metadata.Position = 0;
                            release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(metadata);
                        }
                    }
                    var asset = SelectAsset(release, current, portable);
                    if (asset == null) { progress.Report(new UpdateProgress("You're up to date.", 100)); return null; }
                    folder = Path.Combine(updatesRoot, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    string package = Path.Combine(folder, asset.Name);
                    using (var response = await GetAsync(client, new Uri(asset.Url), false, cancellation).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var destination = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            long actual = await CopyLimited(source, destination, asset.Size,
                                delegate(long count) { progress.Report(new UpdateProgress("Downloading " + release.Tag + "…", (int)(count * 100 / asset.Size))); }, cancellation).ConfigureAwait(false);
                            if (actual != asset.Size) throw new InvalidDataException("The download was incomplete.");
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    progress.Report(new UpdateProgress("Verifying the download…", 100));
                    string digest = asset.Digest.Substring(7);
                    UpdateFiles.VerifyHash(package, digest);
                    return new DownloadedUpdate { Folder = folder, Package = package, Sha256 = digest, Version = ParseVersion(release.Tag), Portable = portable };
                }
            }
            catch
            {
                if (folder != null) DeleteDownload(folder);
                throw;
            }
        }
        private static bool AllowedHost(Uri uri)
        {
            return uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host == "api.github.com" || uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }
        private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri url, bool metadata, CancellationToken cancellation)
        {
            for (int redirect = 0; redirect < 6; redirect++)
            {
                if (!AllowedHost(url)) throw new InvalidDataException("The download redirected outside GitHub.");
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Accept.ParseAdd(metadata ? "application/vnd.github+json" : "application/octet-stream");
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) return response;
                    var location = response.Headers.Location;
                    response.Dispose();
                    if (location == null) throw new InvalidDataException("Missing download redirect.");
                    url = location.IsAbsoluteUri ? location : new Uri(url, location);
                }
            }
            throw new InvalidDataException("Too many download redirects.");
        }
        private static async Task<long> CopyLimited(Stream source, Stream destination, long limit, Action<long> progress, CancellationToken cancellation)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > limit) throw new InvalidDataException("The download exceeded its expected size.");
                await destination.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false);
                if (progress != null) progress(total);
            }
            return total;
        }
        internal static void DeleteDownload(string folder)
        {
            // Only files created in this one download directory are removed.
            try { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal static bool IsInstalled(string appFolder)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6BA2C7B6-6790-4311-B7ED-3C1CFAE67A87}_is1"))
                {
                    string location = key == null ? null : key.GetValue("InstallLocation") as string;
                    return location != null && String.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(appFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal static void Apply(DownloadedUpdate update, string appFolder)
        {
            string runner = Path.Combine(update.Folder, "LaunchHere.exe");
            File.Copy(ApplicationPath(appFolder), runner, false);
            File.Copy(Path.Combine(appFolder, "LaunchHere.exe.config"), runner + ".config", false);
            var process = Process.GetCurrentProcess();
            var plan = new UpdatePlan { Package = update.Package, Sha256 = update.Sha256, AppFolder = appFolder,
                ParentPid = process.Id, ParentStartTicks = process.StartTime.ToUniversalTime().Ticks,
                Portable = update.Portable, Version = update.Version.ToString() };
            string planPath = Path.Combine(update.Folder, "update.json");
            using (var file = File.Create(planPath)) new DataContractJsonSerializer(typeof(UpdatePlan)).WriteObject(file, plan);
            Process.Start(new ProcessStartInfo(runner, "--apply-update \"" + planPath + "\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        private static string ApplicationPath(string folder) { return Path.Combine(folder, "LaunchHere.exe"); }
    }
}
