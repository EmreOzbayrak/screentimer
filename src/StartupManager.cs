using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace ScreenTime {
    public static class StartupManager {
        private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public static string InstalledPath { get { return Path.Combine(UserStorage.DirectoryPath,"app","ScreenTime.exe"); } }
        public static string Command(string path) { return "\""+path+"\" --background"; }
        public static bool Registered() { using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && key.GetValue("ScreenTime")!=null; }
        public static bool DisabledByWindows() {
            using(var key=Registry.CurrentUser.OpenSubKey(ApprovedKey)) {
                var value=key==null?null:key.GetValue("ScreenTime") as byte[];
                return value!=null && value.Length>0 && (value[0]==3 || value[0]==7);
            }
        }
        public static bool Healthy() {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && (string)key.GetValue("ScreenTime","")==Command(InstalledPath) && UpToDate(Assembly.GetExecutingAssembly().Location,InstalledPath) && !DisabledByWindows();
        }
        public static bool UpToDate(string source,string installed) {
            if(!File.Exists(installed)) return false;
            if(String.Equals(Path.GetFullPath(source),Path.GetFullPath(installed),StringComparison.OrdinalIgnoreCase)) return true;
            Version expected,actual;
            if(!Version.TryParse(FileVersionInfo.GetVersionInfo(source).FileVersion,out expected)) expected=new Version(0,0);
            if(!Version.TryParse(FileVersionInfo.GetVersionInfo(installed).FileVersion,out actual)) actual=new Version(0,0);
            // Opening an older download must not replace a newer startup build.
            if(actual>expected) return true;
            if(actual<expected) return false;
            using(var hash=SHA256.Create()) using(var current=File.OpenRead(source)) using(var existing=File.OpenRead(installed))
                return hash.ComputeHash(current).SequenceEqual(hash.ComputeHash(existing));
        }
        public static void Set(bool enabled,bool explicitChange) {
            UserStorage.Verify();
            if(enabled) {
                string source=Assembly.GetExecutingAssembly().Location;
                Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath));
                if(!UpToDate(source,InstalledPath)) {
                    File.Copy(source,InstalledPath,true);
                    if(File.Exists(source+".config")) File.Copy(source+".config",InstalledPath+".config",true);
                }
            }
            using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
                if(enabled) key.SetValue("ScreenTime",Command(InstalledPath),RegistryValueKind.String);
                else key.DeleteValue("ScreenTime",false);
            }
            // Only a user's explicit enable action reverses a Windows startup disable.
            if(enabled && explicitChange) using(var key=Registry.CurrentUser.OpenSubKey(ApprovedKey,true)) { if(key!=null) key.DeleteValue("ScreenTime",false); }
        }
    }
}
