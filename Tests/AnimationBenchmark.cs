using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace LaunchHere {
internal static class AnimationBenchmark {
    static void Pump(int ms) { var s=Stopwatch.StartNew(); while(s.ElapsedMilliseconds<ms) { Application.DoEvents(); Thread.Sleep(2); } }
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(args[0]);
        using(var request=new EventWaitHandle(false,EventResetMode.AutoReset))
        using(var app=new TrayApp(true,request,Path.Combine(args[0],"settings.json"))) {
            ((System.Windows.Forms.Timer)typeof(TrayApp).GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(app)).Stop();
            var window=app.ShowSettings(); Pump(200);
            var tabs=(SettingsPages)typeof(SettingsWindow).GetField("tabs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
            var activate=typeof(Form).GetMethod("OnActivated",BindingFlags.NonPublic|BindingFlags.Instance);
            var deactivate=typeof(Form).GetMethod("OnDeactivate",BindingFlags.NonPublic|BindingFlags.Instance);
            var tick=typeof(ChevronScene).GetMethod("Tick",BindingFlags.NonPublic|BindingFlags.Instance);
            activate.Invoke(window,new object[]{EventArgs.Empty});
            using(var image=new Bitmap(window.ClientSize.Width,window.ClientSize.Height))
            using(var g=Graphics.FromImage(image)) {
                window.Scene.Draw(g,false); window.Scene.Ripple(new Point(100,120));
                var s=Stopwatch.StartNew();
                for(int i=0;i<60;i++) { window.Scene.MovePointer(new Point(80+i*8,120)); tick.Invoke(window.Scene,new object[]{null,EventArgs.Empty}); window.Scene.Draw(g,false); }
                Console.WriteLine("Scene render mean ms: "+(s.Elapsed.TotalMilliseconds/60).ToString("F2"));
            }
            deactivate.Invoke(window,new object[]{EventArgs.Empty}); Pump(100);
            using(var image=new Bitmap(window.Width,window.Height)) {
                window.DrawToBitmap(image,new Rectangle(Point.Empty,window.Size));
                var s=Stopwatch.StartNew();
                for(int i=0;i<3;i++) window.DrawToBitmap(image,new Rectangle(Point.Empty,window.Size));
                Console.WriteLine("Full settings capture mean ms: "+(s.Elapsed.TotalMilliseconds/3).ToString("F2"));
                image.Save(Path.Combine(args[0],"settings.png"));
            }
            var t=Stopwatch.StartNew();
            for(int i=0;i<6;i++) tabs.SelectedIndex=(i+1)%3;
            Console.WriteLine("Tab selection mean ms: "+(t.Elapsed.TotalMilliseconds/6).ToString("F2"));
            window.Hide();
        }
        return 0;
    }
}
}
