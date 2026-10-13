using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace ScreenTime {
    [DataContract] public class Activity {
        [DataMember] public string Day;
        [DataMember] public string App;
        [DataMember] public string Title;
        [DataMember] public string Domain;
        [DataMember] public string Category;
        [DataMember] public double Seconds;
    }
    [DataContract] public class Goal {
        [DataMember] public string Name;
        [DataMember] public double Hours;
        [DataMember] public int Minutes;
    }
    [DataContract] public class State {
        [DataMember] public List<Activity> Activities = new List<Activity>();
        [DataMember] public Dictionary<string,string> Rules = new Dictionary<string,string>();
        [DataMember] public List<string> Excluded = new List<string>();
        [DataMember] public List<Goal> Goals = new List<Goal> {
            new Goal { Name="Finish a short course", Hours=20, Minutes=30 },
            new Goal { Name="Build a personal project", Hours=40, Minutes=45 },
            new Goal { Name="Start learning a language", Hours=60, Minutes=20 }
        };
        [DataMember] public int IdleSeconds = 60;
        [DataMember] public bool CaptureTitles = false;
        [DataMember] public bool IncludePrivateWindows = false;
        [DataMember] public bool CollectBrowserResources = false;
        [DataMember] public int PrivacyVersion;
        [DataMember] public int BrowserVisitFilterVersion;
        [DataMember] public int AppVisitFilterVersion;
        [DataMember] public List<ResourceUsage> Resources = new List<ResourceUsage>();
        [DataMember] public List<ResourceUsage> Sessions = new List<ResourceUsage>();
        [DataMember] public bool StartupEnabled;
        [DataMember] public int StartupVersion;
        public void Normalize() {
            if (Activities == null) Activities = new List<Activity>();
            if (Resources == null) Resources = new List<ResourceUsage>();
            if (Sessions == null) Sessions = new List<ResourceUsage>();
            if (Rules == null) Rules = new Dictionary<string,string>();
            if (Excluded == null) Excluded = new List<string>();
            if (Goals == null) Goals = new List<Goal>();
            if (IdleSeconds < 30) IdleSeconds = 60;
            IdleSeconds=Math.Min(3600,IdleSeconds);
            Activities.RemoveAll(a=>a==null || String.IsNullOrEmpty(a.App) || String.IsNullOrEmpty(a.Day) || Double.IsNaN(a.Seconds) || Double.IsInfinity(a.Seconds) || a.Seconds<0);
            Resources.RemoveAll(r=>r==null || String.IsNullOrEmpty(r.App) || String.IsNullOrEmpty(r.Day));
            Sessions.RemoveAll(r=>r==null || String.IsNullOrEmpty(r.App) || String.IsNullOrEmpty(r.Day) || String.IsNullOrEmpty(r.SessionId));
            Goals.RemoveAll(g=>g==null || String.IsNullOrWhiteSpace(g.Name) || Double.IsNaN(g.Hours) || Double.IsInfinity(g.Hours) || g.Hours<0.5 || g.Hours>10000 || g.Minutes<1 || g.Minutes>720);
            if(PrivacyVersion<1) { CaptureTitles=false; IncludePrivateWindows=false; CollectBrowserResources=false; PrivacyVersion=1; }
        }
    }
    public class Store {
        public readonly string Path;
        public string Warning;
        private static readonly byte[] Header=Encoding.ASCII.GetBytes("SCREEN-TIME-DPAPI-1\n");
        public Store(string path) { Path=path; }
        public static bool IsEncrypted(byte[] bytes) { return bytes.Length>=Header.Length && bytes.Take(Header.Length).SequenceEqual(Header); }
        private static byte[] Protect(byte[] bytes) { return Header.Concat(ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser)).ToArray(); }
        private static byte[] Unprotect(byte[] bytes) { return ProtectedData.Unprotect(bytes.Skip(Header.Length).ToArray(),null,DataProtectionScope.CurrentUser); }
        public State Load() {
            if (!File.Exists(Path) && !File.Exists(Path+".bak")) { var fresh=new State(); fresh.PrivacyVersion=1; return fresh; }
            State result;
            try { result=Read(Path); }
            catch {
                Warning="The history file could not be read. Recovered the backup; any unreadable data is preserved in encrypted form.";
                if(File.Exists(Path)) {
                    byte[] bytes=File.ReadAllBytes(Path);
                    File.WriteAllBytes(Path+".unreadable-"+Guid.NewGuid().ToString("N"),IsEncrypted(bytes)?bytes:Protect(bytes));
                }
                try { result=Read(Path+".bak"); }
                catch(Exception ex) { throw new InvalidDataException("History could not be decrypted or recovered. Sign in with the Windows account that created it. Original files were not overwritten.",ex); }
            }
            SecureExistingFiles();
            return result;
        }
        private State Read(string path) {
            byte[] bytes=File.ReadAllBytes(path);
            using (var stream=new MemoryStream(IsEncrypted(bytes)?Unprotect(bytes):bytes)) {
                var data=(State)new DataContractJsonSerializer(typeof(State)).ReadObject(stream);
                if (data==null) throw new InvalidDataException();
                data.Normalize(); return data;
            }
        }
        public void Save(State state) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var temp=Path+".tmp";
            using(var stream=new MemoryStream()) {
                new DataContractJsonSerializer(typeof(State)).WriteObject(stream,state);
                byte[] encrypted=Protect(stream.ToArray());
                using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { file.Write(encrypted,0,encrypted.Length); file.Flush(true); }
            }
            if(File.Exists(Path)) File.Replace(temp,Path,Path+".bak");
            else File.Move(temp,Path);
            SecureExistingFiles();
        }
        private void SecureExistingFiles() {
            string directory=System.IO.Path.GetDirectoryName(Path), name=System.IO.Path.GetFileName(Path);
            foreach(string file in Directory.GetFiles(directory,name+"*")) {
                string suffix=file.Substring(Path.Length);
                if(suffix!="" && suffix!=".bak" && suffix!=".tmp" && !suffix.StartsWith(".unreadable-")) continue;
                byte[] data=File.ReadAllBytes(file);
                if(IsEncrypted(data)) continue;
                string encryptedTemp=file+".encrypting";
                File.WriteAllBytes(encryptedTemp,Protect(data));
                File.Replace(encryptedTemp,file,null);
            }
        }
    }
    public static class Logic {
        public static readonly string[] Categories = { "Work", "Education", "Fun", "Social", "Other" };
        public static string RuleKey(string app,string domain) { return String.IsNullOrEmpty(domain) ? "app:"+app.ToLowerInvariant() : "site:"+domain.ToLowerInvariant(); }
        public static string Classify(State state,string app,string title,string domain) {
            string category;
            if(state.Rules.TryGetValue(RuleKey(app,domain),out category)) return category;
            if(state.Rules.TryGetValue(RuleKey(app,""),out category)) return category;
            string text=(app+" "+title+" "+domain).ToLowerInvariant();
            if(Contains(text,"coursera","udemy","khanacademy","edx.org","duolingo","wikipedia","tutorial","lecture","learn.microsoft","documentation","stackoverflow")) return "Education";
            if(Contains(text,"youtube","netflix","twitch","spotify","steam","epicgames","roblox","minecraft","overwatch","disneyplus","primevideo")) return "Fun";
            if(Contains(text,"discord","whatsapp","telegram","instagram","facebook","reddit","tiktok","messenger","twitter","x.com")) return "Social";
            if(Contains(text,"code","devenv","rider","pycharm","excel","winword","powerpnt","outlook","notion","figma","slack","teams","github","docs.google","sheets.google","codex")) return "Work";
            return "Other";
        }
        private static bool Contains(string text,params string[] terms) { return terms.Any(text.Contains); }
        public static bool IsBrowser(string app) { return new[]{"chrome","msedge","firefox","brave","opera","vivaldi","arc"}.Contains(app.ToLowerInvariant()); }
        public static void Record(State state,DateTime end,double seconds,string app,string title,string domain) {
            if(seconds<=0 || seconds>10 || state.Excluded.Any(x=>String.Equals(x,app,StringComparison.OrdinalIgnoreCase))) return;
            if(!state.CaptureTitles) { title=""; domain=""; }
            // Split intervals at midnight rather than assigning a whole interval to the next day.
            DateTime start=end.AddSeconds(-seconds);
            while(start<end) {
                var stop=end<start.Date.AddDays(1) ? end : start.Date.AddDays(1);
                string day=start.ToString("yyyy-MM-dd");
                string category=Classify(state,app,title,domain);
                var row=state.Activities.LastOrDefault(a=>a.Day==day && a.App==app && a.Title==title && a.Domain==domain && a.Category==category);
                if(row==null) { row=new Activity { Day=day,App=app,Title=title,Domain=domain,Category=category }; state.Activities.Add(row); }
                row.Seconds+=(stop-start).TotalSeconds;
                start=stop;
            }
        }
        public static DateTime Project(DateTime today,double hours,int minutes) {
            if(hours<=0 || minutes<=0) throw new ArgumentOutOfRangeException();
            return today.Date.AddDays(Math.Ceiling(hours*60/minutes)-1);
        }
        public static string Duration(double seconds) {
            var minutes=(int)(seconds/60);
            if(minutes<1) return ((int)seconds)+"s";
            return minutes<60 ? minutes+"m" : (minutes/60)+"h "+(minutes%60)+"m";
        }
    }
}
