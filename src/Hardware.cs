using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace ScreenTime {
    public static class Hardware {
        [StructLayout(LayoutKind.Sequential)] private struct MemoryCounters {
            public uint Size, PageFaults;
            public UIntPtr PeakWorking, Working, PeakPaged, Paged, PeakNonPaged, NonPaged, PageFile, PeakPageFile, PrivateUsage, PrivateWorking;
            public ulong SharedCommit;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus {
            public uint Size, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
        }
        [DllImport("psapi.dll",SetLastError=true)] private static extern bool GetProcessMemoryInfo(IntPtr process,ref MemoryCounters counters,uint size);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        private static bool readClock;
        private static double? clock;
        public static double? ReadPrivateMemory(Process process) {
            IntPtr handle=OpenProcess(0x0400|0x0010,false,process.Id);
            if(handle==IntPtr.Zero) return null;
            try {
                var counters=new MemoryCounters { Size=(uint)Marshal.SizeOf(typeof(MemoryCounters)),PrivateWorking=UIntPtr.Size==8?new UIntPtr(UInt64.MaxValue):new UIntPtr(UInt32.MaxValue) };
                if(!GetProcessMemoryInfo(handle,ref counters,counters.Size)) return null;
                ulong size=counters.PrivateWorking.ToUInt64();
                if(size>counters.Working.ToUInt64()) return null;
                return size/1048576.0;
            } finally { CloseHandle(handle); }
        }
        public static void ReadSystem(ResourceSnapshot result) {
            long idle,kernel,user;
            if(Environment.ProcessorCount<=64 && GetSystemTimes(out idle,out kernel,out user)) { result.SystemIdle=idle; result.SystemTotal=(double)kernel+user; }
            var memory=new MemoryStatus { Size=(uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if(GlobalMemoryStatusEx(ref memory)) result.SystemMemoryMb=(memory.TotalPhysical-memory.AvailablePhysical)/1048576.0;
            if(!readClock) {
                readClock=true;
                try {
                    using(var search=new ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory")) {
                        search.Options.Timeout=TimeSpan.FromSeconds(2);
                        using(var values=search.Get()) foreach(ManagementObject row in values) using(row) {
                            double value=Convert.ToDouble(row["ConfiguredClockSpeed"]);
                            if(value>0) clock=clock.HasValue?Math.Min(clock.Value,value):value;
                        }
                    }
                } catch { }
            }
            result.RamClock=clock;
            // Win32_Fan.DesiredSpeed is a requested target, not measured RPM.
            // No fan values are invented and no privileged sensor driver is installed.
        }
    }
}
