using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ScreenTime {
    public static class StartupManager {
        private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public static string InstalledPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenTime","app","ScreenTime.exe"); } }
        public static string Command(string path) { return "\""+path+"\" --background"; }
        public static bool Registered() { using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && key.GetValue("ScreenTime")!=null; }
        public static bool DisabledByWindows() {
            using(var key=Registry.CurrentUser.OpenSubKey(ApprovedKey)) {
                var value=key==null?null:key.GetValue("ScreenTime") as byte[];
                return value!=null && value.Length>0 && (value[0]==3 || value[0]==7);
            }
        }
        public static bool Healthy() {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && (string)key.GetValue("ScreenTime","")==Command(InstalledPath) && File.Exists(InstalledPath) && !DisabledByWindows();
        }
        public static void Set(bool enabled,bool explicitChange) {
            if(enabled) {
                string source=Assembly.GetExecutingAssembly().Location;
                Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath));
                if(!String.Equals(Path.GetFullPath(source),InstalledPath,StringComparison.OrdinalIgnoreCase)) {
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
