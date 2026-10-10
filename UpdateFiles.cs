using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LaunchHere
{
    [DataContract] internal sealed class UpdatePlan
    {
        [DataMember] public string Package { get; set; }
        [DataMember] public string Sha256 { get; set; }
        [DataMember] public string AppFolder { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public int ParentPid { get; set; }
        [DataMember] public long ParentStartTicks { get; set; }
        [DataMember] public bool Portable { get; set; }
    }
    internal static class UpdateFiles
    {
        internal static readonly string[] PortableNames = { "LaunchHere.exe", "LaunchHere.exe.config", "README.md" };
        internal static void VerifyHash(string file, string expected)
        {
            if (expected == null || !Regex.IsMatch(expected, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Missing update checksum.");
            using (var stream = File.OpenRead(file))
            using (var hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum did not match. Installation was stopped.");
            }
        }
        internal static void ApplyPortable(string package, string appFolder, string staging, string version)
        {
            Directory.CreateDirectory(staging);
            string backup = Path.Combine(staging, "backup");
            Directory.CreateDirectory(backup);
            using (var zip = ZipFile.OpenRead(package))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long size = 0;
                foreach (var entry in zip.Entries)
                {
                    // Whitelist exact root paths. Reject directories, duplicates,
                    // traversal and extra executables before touching the app.
                    if (Array.IndexOf(PortableNames, entry.FullName) < 0 || !names.Add(entry.FullName))
                        throw new InvalidDataException("Unexpected file in the portable update.");
                    size += entry.Length;
                    if (entry.Length <= 0 || size > 96L * 1024 * 1024) throw new InvalidDataException("Unexpected extracted update size.");
                    entry.ExtractToFile(Path.Combine(staging, entry.FullName), false);
                }
                foreach (string name in PortableNames) if (!names.Contains(name)) throw new InvalidDataException("The portable update is incomplete.");
            }
            string executable = Path.Combine(staging, "LaunchHere.exe");
            Version expected, actual;
            if (!System.Version.TryParse(version, out expected) ||
                !System.Version.TryParse(System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion, out actual) || actual != expected)
                throw new InvalidDataException("The downloaded app version does not match the release.");
            var replaced = new List<string>();
            var originals = new HashSet<string>();
            try
            {
                foreach (string name in PortableNames)
                {
                    string target = Path.Combine(appFolder, name);
                    if (File.Exists(target)) { File.Copy(target, Path.Combine(backup, name), false); originals.Add(name); }
                    // Include this file in rollback even if overwrite fails partway.
                    replaced.Add(name);
                    File.Copy(Path.Combine(staging, name), target, true);
                }
            }
            catch
            {
                foreach (string name in replaced)
                {
                    string target = Path.Combine(appFolder, name);
                    if (originals.Contains(name)) File.Copy(Path.Combine(backup, name), target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }
                throw;
            }
        }
    }
}
