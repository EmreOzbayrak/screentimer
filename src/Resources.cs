using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace ScreenTime {
    [DataContract] public class MetricSummary {
        [DataMember] public double Total, Seconds, Peak;
        public double Average { get { return Seconds>0?Total/Seconds:0; } }
        public void Add(double value,double seconds) { if(Double.IsNaN(value) || Double.IsInfinity(value) || value<0 || seconds<=0) return; Total+=value*seconds; Seconds+=seconds; Peak=Math.Max(Peak,value); }
    }
    [DataContract] public class ResourceUsage {
        [DataMember] public string Day, App, SessionId;
        [DataMember] public double RunningSeconds, BackgroundSeconds, ForegroundSeconds, CpuSeconds, CpuCapacitySeconds;
        [DataMember] public double BackgroundCpuSeconds, BackgroundCpuCapacitySeconds;
        // Legacy full working-set values stay separate from new private RAM measurements.
        [DataMember] public double MemoryMbSeconds, PeakMemoryMb, BackgroundMemoryMbSeconds;
        [DataMember] public DateTime FirstSeen, LastSeen;
        [DataMember] public Dictionary<string,MetricSummary> Metrics=new Dictionary<string,MetricSummary>();
        public MetricSummary Metric(string name) { MetricSummary value; return Metrics!=null && Metrics.TryGetValue(name,out value)?value:new MetricSummary(); }
        public void Measure(string name,double? value,double seconds) {
            if(!value.HasValue) return;
            if(Metrics==null) Metrics=new Dictionary<string,MetricSummary>();
            if(!Metrics.ContainsKey(name)) Metrics[name]=new MetricSummary();
            Metrics[name].Add(value.Value,seconds);
        }
    }
    public class ProcessReading {
        public string App, Identity;
        public int Pid;
        public double MemoryMb;
        public double? PrivateMemoryMb, CpuSeconds;
    }
    public class ResourceSnapshot {
        public DateTime At;
        public double MonotonicSeconds;
        public int Cores;
        public string Foreground;
        public int Inaccessible;
        public double? SystemIdle, SystemTotal, SystemMemoryMb, RamClock;
        public bool GpuAvailable;
        public Dictionary<string,CounterSample> GpuSamples=new Dictionary<string,CounterSample>();
        public List<ProcessReading> Processes=new List<ProcessReading>();
    }
    public class ResourceInterval {
        public DateTime End;
        public string App, SessionId;
        public double Seconds, CpuSeconds, CpuCapacitySeconds, MemoryMb;
        public double? PrivateMemoryMb, Gpu, SystemCpu, SystemMemoryMb, SystemGpu, RamClock;
        public bool Background, Foreground;
    }
    public class ResourceMonitor {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
        private ResourceSnapshot previous;
        private class PendingApp { public double Seconds; public bool Qualified; public string Id=Guid.NewGuid().ToString("N"); public List<ResourceInterval> Intervals=new List<ResourceInterval>(); }
        private Dictionary<string,PendingApp> pending=new Dictionary<string,PendingApp>();
        public void Reset() { previous=null; pending.Clear(); }
        public static ResourceSnapshot Capture(bool browserResources=false,IEnumerable<string> excluded=null) {
            var result=new ResourceSnapshot { At=DateTime.Now,MonotonicSeconds=(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,Cores=Environment.ProcessorCount };
            var exclusions=new HashSet<string>(excluded??new string[0],StringComparer.OrdinalIgnoreCase);
            uint foregroundPid; GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);
            int session; using(var self=Process.GetCurrentProcess()) session=self.SessionId;
            foreach(var process in Process.GetProcesses()) {
                using(process) {
                    try {
                        if(process.Id==0 || process.SessionId!=session) continue;
                        string name=process.ProcessName;
                        if(process.Id==foregroundPid) result.Foreground=name;
                        if(name.Equals("ScreenTime",StringComparison.OrdinalIgnoreCase) || exclusions.Contains(name) || (!browserResources && Logic.IsBrowser(name))) continue;
                        var reading=new ProcessReading { App=name,Pid=process.Id,Identity=process.Id+":"+process.StartTime.ToUniversalTime().Ticks,MemoryMb=process.WorkingSet64/1048576.0 };
                        reading.PrivateMemoryMb=Hardware.ReadPrivateMemory(process);
                        try { reading.CpuSeconds=process.TotalProcessorTime.TotalSeconds; } catch { result.Inaccessible++; }
                        result.Processes.Add(reading);
                    } catch { result.Inaccessible++; }
                }
            }
            Hardware.ReadSystem(result);
            try {
                var data=new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
                if(data!=null) {
                    foreach(InstanceData entry in data.Values) result.GpuSamples[entry.InstanceName]=entry.Sample;
                    result.GpuAvailable=true;
                }
            } catch { /* Unsupported drivers or unavailable/localized counters stay unavailable. */ }
            return result;
        }
        public static double? BusiestGpu(ResourceSnapshot before,ResourceSnapshot after,HashSet<int> pids) {
            if(!before.GpuAvailable || !after.GpuAvailable) return null;
            var engines=new Dictionary<string,double>();
            foreach(var entry in after.GpuSamples) {
                var match=Regex.Match(entry.Key,@"^pid_(\d+)_(luid_.+)$",RegexOptions.IgnoreCase); int pid;
                if(!match.Success || !Int32.TryParse(match.Groups[1].Value,out pid) || (pids!=null && !pids.Contains(pid))) continue;
                CounterSample old;
                if(!before.GpuSamples.TryGetValue(entry.Key,out old)) return null;
                if(entry.Value.RawValue<old.RawValue || entry.Value.TimeStamp<=old.TimeStamp) return null;
                double value=CounterSample.Calculate(old,entry.Value);
                if(Double.IsNaN(value) || Double.IsInfinity(value) || value<0) return null;
                string engine=match.Groups[2].Value;
                if(!engines.ContainsKey(engine)) engines[engine]=0;
                engines[engine]+=value;
            }
            foreach(string name in before.GpuSamples.Keys.Where(k=>!after.GpuSamples.ContainsKey(k))) {
                var match=Regex.Match(name,@"^pid_(\d+)_",RegexOptions.IgnoreCase); int pid;
                if(match.Success && Int32.TryParse(match.Groups[1].Value,out pid) && (pids==null || pids.Contains(pid))) return null;
            }
            return engines.Count==0?0:Math.Min(100,engines.Values.Max());
        }
        public static List<ResourceInterval> Compare(ResourceSnapshot before,ResourceSnapshot after) {
            var rows=new List<ResourceInterval>();
            double seconds=after.MonotonicSeconds-before.MonotonicSeconds;
            if(seconds<=0 || seconds>15 || Math.Abs((after.At-before.At).TotalSeconds-seconds)>2) return rows;
            var old=before.Processes.ToDictionary(p=>p.Identity);
            double? systemCpu=null;
            if(after.SystemTotal.HasValue && before.SystemTotal.HasValue && after.SystemIdle.HasValue && before.SystemIdle.HasValue) {
                double total=after.SystemTotal.Value-before.SystemTotal.Value, idle=after.SystemIdle.Value-before.SystemIdle.Value;
                if(total>0 && idle>=0 && idle<=total) systemCpu=100*(total-idle)/total;
            }
            double? systemGpu=BusiestGpu(before,after,null);
            foreach(var group in after.Processes.GroupBy(p=>p.App)) {
                var matched=group.Where(p=>old.ContainsKey(p.Identity) && old[p.Identity].App==p.App).ToList();
                if(matched.Count==0) continue;
                bool cpuAvailable=matched.Count==group.Count() && matched.Count==before.Processes.Count(p=>p.App==group.Key) && matched.All(p=>p.CpuSeconds.HasValue && old[p.Identity].CpuSeconds.HasValue && p.CpuSeconds.Value>=old[p.Identity].CpuSeconds.Value);
                rows.Add(new ResourceInterval {
                    App=group.Key,End=after.At,Seconds=seconds,
                    CpuSeconds=cpuAvailable?matched.Sum(p=>p.CpuSeconds.Value-old[p.Identity].CpuSeconds.Value):0,
                    CpuCapacitySeconds=cpuAvailable?seconds*Math.Max(1,after.Cores):0,
                    MemoryMb=group.Sum(p=>p.MemoryMb),
                    PrivateMemoryMb=group.All(p=>p.PrivateMemoryMb.HasValue)?(double?)group.Sum(p=>p.PrivateMemoryMb.Value):null,
                    Gpu=BusiestGpu(before,after,new HashSet<int>(group.Select(p=>p.Pid))),
                    SystemCpu=systemCpu,SystemMemoryMb=after.SystemMemoryMb,SystemGpu=systemGpu,RamClock=after.RamClock,
                    Foreground=before.Foreground==group.Key && after.Foreground==group.Key,
                    Background=before.Foreground!=null && after.Foreground!=null && before.Foreground!=group.Key && after.Foreground!=group.Key
                });
            }
            return rows;
        }
        public void Accept(State state,ResourceSnapshot snapshot) {
            if(previous==null) { previous=snapshot; return; }
            var intervals=Compare(previous,snapshot).Where(r=>(state.CollectBrowserResources || !Logic.IsBrowser(r.App)) && !state.Excluded.Any(x=>String.Equals(x,r.App,StringComparison.OrdinalIgnoreCase))).ToList();
            previous=snapshot;
            var present=new HashSet<string>(intervals.Select(r=>r.App));
            foreach(string gone in pending.Keys.Where(k=>!present.Contains(k)).ToList()) pending.Remove(gone);
            foreach(var interval in intervals) {
                PendingApp app;
                if(!pending.TryGetValue(interval.App,out app)) { app=new PendingApp(); pending[interval.App]=app; }
                interval.SessionId=app.Id;
                if(app.Qualified) { Accumulate(state,interval); continue; }
                app.Seconds+=interval.Seconds; app.Intervals.Add(interval);
                if(app.Seconds<VisitRecorder.MinimumSeconds) continue;
                app.Qualified=true;
                foreach(var held in app.Intervals) Accumulate(state,held);
                app.Intervals.Clear();
            }
        }
        public static void Accumulate(State state,ResourceInterval interval) {
            DateTime start=interval.End.AddSeconds(-interval.Seconds);
            while(start<interval.End) {
                DateTime end=interval.End<start.Date.AddDays(1)?interval.End:start.Date.AddDays(1);
                AddRow(state.Resources,interval,start,end,false);
                if(!String.IsNullOrEmpty(interval.SessionId)) AddRow(state.Sessions,interval,start,end,true);
                start=end;
            }
        }
        private static void AddRow(List<ResourceUsage> rows,ResourceInterval interval,DateTime start,DateTime end,bool session) {
            double seconds=(end-start).TotalSeconds, ratio=seconds/interval.Seconds;
            string day=start.ToString("yyyy-MM-dd");
            var row=rows.LastOrDefault(r=>r.Day==day && r.App==interval.App && (!session || r.SessionId==interval.SessionId));
            if(row==null) { row=new ResourceUsage { Day=day,App=interval.App,SessionId=session?interval.SessionId:null,FirstSeen=start }; rows.Add(row); }
            row.LastSeen=end; row.RunningSeconds+=seconds;
            row.CpuSeconds+=interval.CpuSeconds*ratio; row.CpuCapacitySeconds+=interval.CpuCapacitySeconds*ratio;
            row.MemoryMbSeconds+=interval.MemoryMb*seconds; row.PeakMemoryMb=Math.Max(row.PeakMemoryMb,interval.MemoryMb);
            if(interval.Foreground) row.ForegroundSeconds+=seconds;
            row.Measure("PrivateRam",interval.PrivateMemoryMb,seconds); row.Measure("Gpu",interval.Gpu,seconds);
            row.Measure("SystemCpu",interval.SystemCpu,seconds); row.Measure("SystemRam",interval.SystemMemoryMb,seconds);
            row.Measure("SystemGpu",interval.SystemGpu,seconds); row.Measure("RamClock",interval.RamClock,seconds);
            if(interval.CpuCapacitySeconds>0) row.Measure("Cpu",100*interval.CpuSeconds/interval.CpuCapacitySeconds,seconds);
            if(interval.Background) {
                row.BackgroundSeconds+=seconds; row.BackgroundCpuSeconds+=interval.CpuSeconds*ratio;
                row.BackgroundCpuCapacitySeconds+=interval.CpuCapacitySeconds*ratio;
                row.BackgroundMemoryMbSeconds+=interval.MemoryMb*seconds;
                row.Measure("BackgroundPrivateRam",interval.PrivateMemoryMb,seconds);
            }
        }
    }
}
