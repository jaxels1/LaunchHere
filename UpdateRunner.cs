using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace LaunchHere
{
    internal static class UpdateRunner
    {
        internal static int Run(string[] args)
        {
            string appFolder = null;
            try
            {
                if (args.Length != 1) return 2;
                string planPath = Path.GetFullPath(args[0]);
                string folder = Path.GetDirectoryName(Application.ExecutablePath);
                if (planPath != Path.Combine(folder, "update.json")) throw new InvalidDataException("Invalid update plan location.");
                UpdatePlan plan;
                using (var stream = File.OpenRead(planPath)) plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(stream);
                if (plan == null || Path.GetDirectoryName(Path.GetFullPath(plan.Package)) != folder)
                    throw new InvalidDataException("Invalid package location.");
                appFolder = Path.GetFullPath(plan.AppFolder);
                if (!File.Exists(Path.Combine(appFolder, "LaunchHere.exe"))) throw new InvalidDataException("LaunchHere installation was not found.");
                UpdateFiles.VerifyHash(plan.Package, plan.Sha256);
                try
                {
                    using (var parent = Process.GetProcessById(plan.ParentPid))
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks && !parent.WaitForExit(30000))
                            throw new IOException("LaunchHere did not close in time. Please quit it and try again.");
                    }
                }
                catch (ArgumentException) { } // The previous app has already exited.
                if (plan.Portable) UpdateFiles.ApplyPortable(plan.Package, appFolder, Path.Combine(folder, "staged"), plan.Version);
                else
                {
                    if (!plan.Package.EndsWith("-Setup-x64.exe", StringComparison.Ordinal)) throw new InvalidDataException("Invalid installer filename.");
                    Version expected, actual;
                    if (!Version.TryParse(plan.Version, out expected) || !Version.TryParse(FileVersionInfo.GetVersionInfo(plan.Package).FileVersion, out actual) || expected != actual)
                        throw new InvalidDataException("The installer version does not match the release.");
                    string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=\"" + appFolder + "\" /LOG=\"" + Path.Combine(folder, "install.log") + "\"";
                    using (var installer = Process.Start(new ProcessStartInfo(plan.Package, arguments) { UseShellExecute = false }))
                    {
                        installer.WaitForExit();
                        if (installer.ExitCode != 0) throw new IOException("The installer could not finish (code " + installer.ExitCode + "). Your existing settings are preserved.");
                    }
                }
                Process.Start(new ProcessStartInfo(Path.Combine(appFolder, "LaunchHere.exe"), "--settings") { UseShellExecute = false });
                // Retain a failed update's log for support; successful payloads
                // can be removed. The helper itself is in use until we exit.
                try { File.Delete(plan.Package); File.Delete(planPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("The update could not be applied.\n\n" + ex.Message + "\n\nYou can download a release manually from github.com/jaxels1/LaunchHere/releases.",
                    "LaunchHere update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (appFolder != null && File.Exists(Path.Combine(appFolder, "LaunchHere.exe")))
                    try { Process.Start(new ProcessStartInfo(Path.Combine(appFolder, "LaunchHere.exe"), "--settings") { UseShellExecute = false }); } catch { }
                return 1;
            }
        }
    }
}
