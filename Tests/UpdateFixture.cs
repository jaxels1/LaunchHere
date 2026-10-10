using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

class UpdateFixture
{
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--parent") { Thread.Sleep(1800); return 0; }
        if (args.Length == 1 && args[0] == "--settings") {
            File.WriteAllText(Path.Combine(Application.StartupPath, "restart.marker"), FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion);
        }
        return 0;
    }
}
