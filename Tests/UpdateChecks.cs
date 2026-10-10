using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchHere
{
    internal static class UpdateChecks
    {
        private static int count;
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        private static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, name);
        }
        private sealed class Progress : IProgress<UpdateProgress>
        {
            internal string Last;
            public void Report(UpdateProgress value) { Last = value.Message; }
        }
        private sealed class Handler : HttpMessageHandler
        {
            internal int Requests;
            internal byte[] Package;
            internal ReleaseInfo Release;
            internal string Redirect;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Requests++;
                if (request.RequestUri.AbsoluteUri == UpdateClient.LatestUrl)
                {
                    using (var memory = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(ReleaseInfo)).WriteObject(memory, Release);
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(memory.ToArray()) });
                    }
                }
                if (Redirect != null && request.RequestUri.Host == "github.com")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new Uri(Redirect);
                    return Task.FromResult(response);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) });
            }
        }
        private static string Hash(byte[] data)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
        private static ReleaseInfo Release(byte[] package, bool portable)
        {
            string name = "LaunchHere-1.3.0" + (portable ? "-Windows-x64.zip" : "-Setup-x64.exe");
            return new ReleaseInfo { Tag = "v1.3.0", Assets = new List<ReleaseAsset> { new ReleaseAsset {
                Name = name, Url = "https://github.com/jaxels1/LaunchHere/releases/download/v1.3.0/" + name,
                Size = package.Length, Digest = "sha256:" + Hash(package) } } };
        }
        private static DownloadedUpdate Download(Handler handler, string folder, bool portable, Version current, CancellationToken cancellation)
        {
            return new UpdateClient(delegate { return new HttpClient(handler); }).CheckAndDownloadAsync(current, portable, folder, new Progress(), cancellation).GetAwaiter().GetResult();
        }
        private static void Add(ZipArchive zip, string name, byte[] bytes)
        {
            using (var stream = zip.CreateEntry(name).Open()) stream.Write(bytes, 0, bytes.Length);
        }
        private static string Zip(string folder, string sourceRoot, string extra)
        {
            string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                foreach (string name in UpdateFiles.PortableNames) Add(zip, name, File.ReadAllBytes(Path.Combine(sourceRoot, name)));
                if (extra != null) Add(zip, extra, new byte[] { 1 });
            }
            return file;
        }
        private static int Main(string[] args)
        {
            try
            {
                string folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
                string sourceRoot = Path.GetFullPath(args[1]);
                string builtVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(sourceRoot, "LaunchHere.exe")).FileVersion;
                byte[] bytes = new byte[] { 1, 2, 3, 4, 5 };
                var handler = new Handler { Package = bytes, Release = Release(bytes, false) };
                var offline = new UpdateClient(delegate { return new HttpClient(handler); });
                Check(handler.Requests == 0, "constructing updater makes no network requests");
                var result = Download(handler, folder, false, new Version(1, 3, 0, 0), CancellationToken.None);
                Check(result == null && handler.Requests == 1, "current release does not download or apply anything");
                Check(UpdateClient.SelectAsset(Release(bytes, false), new Version(9, 0, 0, 0), false) == null, "older release cannot downgrade app");
                Check(UpdateClient.ParseVersion("v1.3.0") == new Version(1, 3, 0, 0), "three-part release version normalized");
                Check(UpdateClient.ParseVersion("v2.0") == new Version(2, 0, 0, 0), "2.0 release tag normalized");
                foreach (bool portable in new[] { false, true })
                {
                    var twoPart = Release(bytes, portable);
                    twoPart.Tag = "v2.0";
                    twoPart.Assets[0].Name = "LaunchHere-2.0" + (portable ? "-Windows-x64.zip" : "-Setup-x64.exe");
                    twoPart.Assets[0].Url = "https://github.com/jaxels1/LaunchHere/releases/download/v2.0/" + twoPart.Assets[0].Name;
                    Check(UpdateClient.SelectAsset(twoPart, new Version(1, 3, 0, 0), portable) == twoPart.Assets[0], "2.0 " + (portable ? "portable" : "installer") + " asset selected");
                }
                Check(UpdateClient.DisplayVersion(new Version(2, 0, 0, 0)) == "2.0" &&
                    UpdateClient.DisplayVersion(new Version(2, 0, 1, 0)) == "2.0.1" &&
                    UpdateClient.DisplayVersion(new Version(2, 0, 0, 1)) == "2.0.0.1", "display version preserves nonzero patch and revision");
                Reject(delegate { UpdateClient.ParseVersion("v1.3.0-beta"); }, "prerelease tag rejected");
                var invalid = Release(bytes, false); invalid.Prerelease = true;
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "prerelease metadata rejected");
                invalid = Release(bytes, false); invalid.Draft = true;
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "draft release rejected");
                invalid = Release(bytes, false); invalid.Assets[0].Url = "https://evil.example/update.exe";
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "foreign repository or download host rejected");
                invalid = Release(bytes, false); invalid.Assets[0].Digest = null;
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "unverifiable update rejected");
                invalid = Release(bytes, false); invalid.Assets[0].Size = UpdateClient.MaximumDownload + 1;
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "oversized release rejected");
                invalid = Release(bytes, false); invalid.Assets.Add(invalid.Assets[0]);
                Reject(delegate { UpdateClient.SelectAsset(invalid, new Version(1, 2, 2, 0), false); }, "ambiguous duplicate update assets rejected");
                Reject(delegate { UpdateClient.SelectAsset(Release(bytes, false), new Version(1, 2, 2, 0), true); }, "portable mode requires the portable asset");

                handler = new Handler { Package = bytes, Release = Release(bytes, false), Redirect = "https://release-assets.githubusercontent.com/test" };
                result = Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None);
                Check(handler.Requests == 3 && File.ReadAllBytes(result.Package).Length == bytes.Length, "explicit check downloads through allowed GitHub redirect");
                UpdateFiles.VerifyHash(result.Package, Hash(bytes));
                Check(result.Version == new Version(1, 3, 0, 0) && !result.Portable, "verified download produces correct apply plan");
                UpdateClient.DeleteDownload(result.Folder);
                int directoryCount = Directory.GetDirectories(folder).Length;
                handler = new Handler { Package = bytes, Release = Release(bytes, false), Redirect = "http://github.com/insecure" };
                Reject(delegate { Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None); }, "insecure redirect rejected");
                Check(Directory.GetDirectories(folder).Length == directoryCount, "failed redirect removes partial download");
                handler = new Handler { Package = bytes, Release = Release(bytes, false), Redirect = "https://evil.example/update" };
                Reject(delegate { Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None); }, "foreign redirect rejected before request");
                handler = new Handler { Package = bytes, Release = Release(bytes, false) }; handler.Release.Assets[0].Digest = "sha256:" + new string('0', 64);
                Reject(delegate { Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None); }, "checksum mismatch blocks installation");
                Check(Directory.GetDirectories(folder).Length == directoryCount, "checksum failure removes downloaded payload");
                handler = new Handler { Package = bytes, Release = Release(bytes, false) }; handler.Release.Assets[0].Size--;
                Reject(delegate { Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None); }, "oversized download stream blocked");
                handler = new Handler { Package = bytes, Release = Release(bytes, false) }; handler.Release.Assets[0].Size++;
                Reject(delegate { Download(handler, folder, false, new Version(1, 2, 2, 0), CancellationToken.None); }, "truncated download stream blocked");
                handler = new Handler { Package = bytes, Release = Release(bytes, false) };
                bool cancelled = false;
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try { Download(handler, folder, false, new Version(1, 2, 2, 0), cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                }
                Check(cancelled && handler.Requests == 0, "cancelled check makes no request and applies nothing");

                string app = Path.Combine(folder, "portable-app"); Directory.CreateDirectory(app);
                string package = Zip(folder, sourceRoot, null);
                File.WriteAllText(Path.Combine(app, "settings.json"), "keep preferences");
                UpdateFiles.ApplyPortable(package, app, Path.Combine(folder, "staged-valid"), builtVersion);
                foreach (string name in UpdateFiles.PortableNames)
                    Check(Hash(File.ReadAllBytes(Path.Combine(app, name))) == Hash(File.ReadAllBytes(Path.Combine(sourceRoot, name))), "portable updater applies " + name);
                Check(File.ReadAllText(Path.Combine(app, "settings.json")) == "keep preferences", "portable update preserves unrelated preferences");
                Reject(delegate { UpdateFiles.ApplyPortable(Zip(folder, sourceRoot, "../escape.exe"), app, Path.Combine(folder, "staged-traversal"), builtVersion); }, "ZIP path traversal rejected");
                Reject(delegate { UpdateFiles.ApplyPortable(Zip(folder, sourceRoot, "extra.exe"), app, Path.Combine(folder, "staged-extra"), builtVersion); }, "unexpected executable rejected");
                Reject(delegate { UpdateFiles.ApplyPortable(Zip(folder, sourceRoot, "LaunchHere.exe"), app, Path.Combine(folder, "staged-duplicate"), builtVersion); }, "duplicate ZIP entries rejected");
                Reject(delegate { UpdateFiles.ApplyPortable(package, app, Path.Combine(folder, "staged-version"), "9.0.0.0"); }, "ZIP executable version must match release");
                File.WriteAllText(Path.Combine(app, "LaunchHere.exe"), "old app");
                File.WriteAllText(Path.Combine(app, "LaunchHere.exe.config"), "old config");
                bool locked = false;
                using (var held = new FileStream(Path.Combine(app, "README.md"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    try { UpdateFiles.ApplyPortable(package, app, Path.Combine(folder, "staged-rollback"), builtVersion); } catch (IOException) { locked = true; }
                }
                Check(locked && File.ReadAllText(Path.Combine(app, "LaunchHere.exe")) == "old app" && File.ReadAllText(Path.Combine(app, "LaunchHere.exe.config")) == "old config", "failed portable overwrite restores original binaries");
                Console.WriteLine(count + " update checks passed. No real network requests were made.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
