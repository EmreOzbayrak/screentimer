using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace ScreenTime {
    public class Snapshot {
        public string App="", Title="", Domain="";
        public IntPtr Handle;
        public bool Idle;
        public BrowserMode BrowserMode=BrowserMode.Unknown;
        public bool PrivacyBlocked;
        public DateTime ReadAt;
    }
    public class Tracker {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint processId);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int count);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size; public uint Tick; }
        private Task<Snapshot> lookup;
        private DateTime nextLookup=DateTime.MinValue;
        private Snapshot cached;
        private bool lastCapture,lastPrivate;
        public void Reset() { cached=null; lookup=null; nextLookup=DateTime.MinValue; }
        public static bool IsIdle(int timeoutSeconds,uint idleMilliseconds,bool inputAvailable) {
            return timeoutSeconds!=0 && (!inputAvailable || idleMilliseconds>=(long)timeoutSeconds*1000);
        }
        public Snapshot Sample(State state) {
            bool captureTitles=state.CaptureTitles, includePrivate=state.IncludePrivateWindows;
            if(lastCapture!=captureTitles || lastPrivate!=includePrivate) { Reset(); lastCapture=captureTitles; lastPrivate=includePrivate; }
            var input=new LastInput { Size=(uint)Marshal.SizeOf(typeof(LastInput)) };
            bool inputAvailable=state.IdleSeconds==0 || GetLastInputInfo(ref input);
            uint idle=unchecked((uint)Environment.TickCount-input.Tick);
            if(IsIdle(state.IdleSeconds,idle,inputAvailable)) return new Snapshot { Idle=true };
            var handle=GetForegroundWindow();
            if(handle==IntPtr.Zero) return new Snapshot { Idle=true };
            uint pid; GetWindowThreadProcessId(handle,out pid);
            try {
                using(var process=Process.GetProcessById((int)pid)) {
                    string app=process.ProcessName;
                    if(app=="ScreenTime" || app=="LockApp" || app=="LogonUI") return new Snapshot { Idle=true };
                    if(state.Excluded.Any(x=>String.Equals(x,app,StringComparison.OrdinalIgnoreCase))) return new Snapshot { Idle=true };
                    var result=new Snapshot { App=app,Handle=handle };
                    if(lookup!=null && lookup.IsCompleted) {
                        if(lookup.Status==TaskStatus.RanToCompletion) cached=lookup.Result;
                        lookup=null;
                    }
                    if(Logic.IsBrowser(app)) {
                        if(cached!=null && cached.Handle==handle && cached.App==app && DateTime.UtcNow-cached.ReadAt<TimeSpan.FromSeconds(4)) result=cached;
                        else result.PrivacyBlocked=!includePrivate;
                        if(lookup==null && DateTime.UtcNow>=nextLookup) {
                            var copy=new Snapshot { App=app,Handle=handle };
                            nextLookup=DateTime.UtcNow.AddSeconds(2);
                            lookup=Task.Run(()=>ReadBrowser(copy,captureTitles,includePrivate));
                        }
                    }
                    return result;
                }
            } catch { return new Snapshot { Idle=true }; }
        }
        private static Snapshot ReadBrowser(Snapshot snapshot,bool captureTitles,bool includePrivate) {
            snapshot.PrivacyBlocked=!includePrivate;
            try {
                var controls=BrowserPrivacy.NativeControls(snapshot.Handle);
                snapshot.BrowserMode=BrowserPrivacy.Inspect(snapshot.App.ToLowerInvariant(),controls);
                snapshot.PrivacyBlocked=!BrowserPrivacy.MayRecord(snapshot.BrowserMode,includePrivate);
                if(snapshot.PrivacyBlocked || !captureTitles) return snapshot;
                var title=new StringBuilder(1024); GetWindowText(snapshot.Handle,title,title.Capacity); snapshot.Title=title.ToString();
                // Only native address controls, never descendants of page documents.
                var bar=controls.FirstOrDefault(c=>c.Current.ControlType==ControlType.Edit &&
                    (c.Current.AutomationId=="urlbar-input" || c.Current.Name=="Address and search bar" || c.Current.Name=="Search or enter address" || c.Current.Name=="Address bar"));
                object pattern;
                if(bar!=null && bar.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)) {
                    string value=((ValuePattern)pattern).Current.Value;
                    Uri url;
                    if(!value.Contains(" ") && Uri.TryCreate(value.Contains("://")?value:"https://"+value,UriKind.Absolute,out url) && (url.Scheme=="http" || url.Scheme=="https") && url.Host.Contains("."))
                        snapshot.Domain=url.Host.ToLowerInvariant().StartsWith("www.")?url.Host.ToLowerInvariant().Substring(4):url.Host.ToLowerInvariant();
                }
            } catch { snapshot.PrivacyBlocked=!includePrivate; snapshot.Title=""; snapshot.Domain=""; }
            finally { snapshot.ReadAt=DateTime.UtcNow; }
            return snapshot;
        }
    }
}
