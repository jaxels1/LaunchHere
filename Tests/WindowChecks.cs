using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
class TestForm : Form { protected override bool ShowWithoutActivation { get { return true; } } }
class WindowChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    static void Pump() { for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(20); } }
    [STAThread] static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(args[0]);
        var type = assembly.GetType("LaunchHere.TrayApp");
        var move = type.GetMethod("Move", BindingFlags.NonPublic | BindingFlags.Static);
        Console.WriteLine("Connected monitors: " + Screen.AllScreens.Length);
        foreach (var screen in Screen.AllScreens) Console.WriteLine(screen.DeviceName + " " + screen.WorkingArea);
        if (Screen.AllScreens.Length < 2) { Console.WriteLine("Cross-monitor native movement unavailable: only one display exposed to test session."); return 0; }
        foreach (var source in Screen.AllScreens)
        foreach (var target in Screen.AllScreens)
        {
            if (target.DeviceName == source.DeviceName) continue;
            using (var form = new TestForm())
            {
                form.Text = "LaunchHere temporary movement test";
                form.StartPosition = FormStartPosition.Manual;
                form.Bounds = new Rectangle(source.WorkingArea.Left + 80, source.WorkingArea.Top + 80, 640, 480);
                form.Show(); Pump();
                move.Invoke(null, new object[] { form.Handle, target }); Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName) { Console.WriteLine("FAIL normal window move"); return 1; }
                move.Invoke(null, new object[] { form.Handle, source }); Pump();
                form.WindowState = FormWindowState.Maximized; Pump();
                move.Invoke(null, new object[] { form.Handle, target }); Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName || form.WindowState != FormWindowState.Maximized) { Console.WriteLine("FAIL maximized window move"); return 2; }
                form.WindowState = FormWindowState.Normal; Pump();
                if (Screen.FromHandle(form.Handle).DeviceName != target.DeviceName) { Console.WriteLine("FAIL restored location"); return 3; }
                Console.WriteLine("PASS normal, maximized, restored: " + target.DeviceName);
            }
        }
        return 0;
    }
}
