using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScreenTime {
    public static class UserStorage {
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenTime"); } }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint size,uint flags);
        public static bool IsRedirected(string requested,string resolved) {
            // Packaged launchers can redirect AppData without giving the child
            // package identity, or return the private folder directly.
            return IsPackageFolder(requested) || IsPackageFolder(resolved);
        }
        private static bool IsPackageFolder(string path) {
            if(path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) path=@"\\"+path.Substring(8);
            else if(path.StartsWith(@"\\?\")) path=path.Substring(4);
            path=Path.GetFullPath(path).Replace('/','\\');
            int packages=path.IndexOf(@"\Packages\",StringComparison.OrdinalIgnoreCase);
            if(packages<0) return false;
            int packageEnd=path.IndexOf('\\',packages+10);
            if(packageEnd<0) return false;
            string suffix=path.Substring(packageEnd);
            return suffix.StartsWith(@"\LocalCache\",StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith(@"\LocalState\",StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith(@"\AC\",StringComparison.OrdinalIgnoreCase);
        }
        public static void Verify() {
            string directory=DirectoryPath;
            if(IsRedirected(directory,directory)) throw RedirectedStorage();
            Directory.CreateDirectory(directory);
            // Directory handles can resolve normally while files are overlaid.
            foreach(string path in new[]{Path.Combine(directory,"activity.json"),Path.Combine(directory,"app","ScreenTime.exe")}) {
                if(File.Exists(path)) using(var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) VerifyHandle(path,file.SafeFileHandle);
            }
            string probe=Path.Combine(directory,".storage-"+Guid.NewGuid().ToString("N")+".probe");
            using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.DeleteOnClose)) VerifyHandle(probe,file.SafeFileHandle);
        }
        private static void VerifyHandle(string requested,SafeFileHandle handle) {
            var resolved=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(handle,resolved,(uint)resolved.Capacity,0);
            if(length==0 || length>=resolved.Capacity) throw new IOException("Could not verify the history folder.");
            if(IsRedirected(requested,resolved.ToString())) throw RedirectedStorage();
        }
        private static IOException RedirectedStorage() { return new IOException("Windows redirected ScreenTime's storage into another app's private folder. To keep one history and one set of settings, close this copy and open ScreenTime from the Windows Start menu or File Explorer. Existing files have not been changed."); }
    }
}
