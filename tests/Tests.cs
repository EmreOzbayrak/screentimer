using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenTime;

class Tests {
    static int count;
    static State Fixture() { return new State { CaptureTitles=true, IncludePrivateWindows=true, CollectBrowserResources=true, PrivacyVersion=1 }; }
    static void Check(bool condition,string name) { if(!condition) throw new Exception(name); count++; Console.WriteLine("PASS "+name); }
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Contains("--live")) { Live(); return 0; }
            NewFeatures();
            var state=Fixture();
            Check(Logic.Classify(state,"chrome","Python tutorial - YouTube","youtube.com")=="Education","Education intent takes priority over entertainment site");
            Check(Logic.Classify(state,"chrome","Video","youtube.com")=="Fun","Entertainment categorization");
            state.Rules[Logic.RuleKey("chrome","youtube.com")]="Work";
            Check(Logic.Classify(state,"chrome","Video","youtube.com")=="Work","User rule wins");
            var midnight=new DateTime(2026,10,13,0,0,1);
            Logic.Record(state,midnight,2,"chrome","Video","youtube.com");
            Check(state.Activities.Count==2 && state.Activities.All(a=>a.Seconds==1),"Midnight splits activity between dates");
            Logic.Record(state,midnight.AddSeconds(1),1,"chrome","Video","youtube.com");
            Check(state.Activities.Sum(a=>a.Seconds)==3,"Repeated activity aggregates");
            Logic.Record(state,midnight,100,"chrome","Video","youtube.com");
            Check(state.Activities.Sum(a=>a.Seconds)==3,"Suspend gaps are never counted");
            state.Excluded.Add("CHROME"); Logic.Record(state,midnight,1,"chrome","Video","youtube.com");
            Check(state.Activities.Sum(a=>a.Seconds)==3,"Exclusions are case insensitive");
            state.CaptureTitles=false; Logic.Record(state,midnight,1,"firefox","Private title","private.example");
            Check(state.Activities.Last().Title=="" && state.Activities.Last().Domain=="","App-only mode strips page data before storage");
            Check(Logic.Project(new DateTime(2026,10,12),20,30)==new DateTime(2026,11,20),"Goal completion includes today's session");
            Check(Logic.Project(new DateTime(2026,10,12),0.5,30)==new DateTime(2026,10,12),"One-session goal completes today");
            Check(Logic.Duration(3660)=="1h 1m","Duration formatting");
            var filtered=Fixture(); var recorder=new VisitRecorder();
            var page=new Snapshot { App="chrome",Title="A page",Domain="example.com",Handle=new IntPtr(1) };
            var stamp=new DateTime(2026,10,12,12,0,0);
            for(int i=1;i<=9;i++) recorder.Add(filtered,stamp.AddSeconds(i),1,page);
            Check(filtered.Activities.Count==0,"Nine-second page visit never enters saved history");
            recorder.Add(filtered,stamp.AddSeconds(10),1,page);
            Check(filtered.Activities.Single().Seconds==10,"Ten-second visit includes all initial seconds");
            recorder.Add(filtered,stamp.AddSeconds(11),1,page);
            Check(filtered.Activities.Single().Seconds==11,"Qualified visits keep accumulating");
            recorder.Reset(); recorder.Add(filtered,stamp.AddSeconds(12),1,page); recorder.Reset();
            Check(filtered.Activities.Single().Seconds==11,"Brief return to an already recorded page is still discarded");
            filtered=Fixture(); recorder=new VisitRecorder();
            for(int i=0;i<3;i++) { recorder.Add(filtered,stamp.AddSeconds(i*5+4),4,page); recorder.Reset(); }
            Check(filtered.Activities.Count==0,"Separate brief visits do not combine to meet the threshold");
            recorder.Add(filtered,stamp.AddSeconds(20),9,page);
            recorder.Add(filtered,stamp.AddSeconds(21),1,new Snapshot {App="chrome",Title="Another page",Domain="example.com",Handle=new IntPtr(1)});
            Check(filtered.Activities.Count==0,"Changing pages on the same website resets the threshold");
            recorder.Reset();
            recorder.Add(filtered,stamp.AddSeconds(30),5,new Snapshot {App="chrome",Title="A page",Handle=new IntPtr(1)});
            recorder.Add(filtered,stamp.AddSeconds(35),5,page);
            Check(filtered.Activities.Single().Seconds==10 && filtered.Activities[0].Domain=="example.com","Late hostname discovery preserves and combines the pending visit");
            filtered=Fixture(); recorder=new VisitRecorder();
            recorder.Add(filtered,midnight.AddSeconds(-2),5,page); recorder.Add(filtered,midnight.AddSeconds(3),5,page);
            Check(filtered.Activities.Count==2 && filtered.Activities.Sum(a=>a.Seconds)==10,"Qualified visit keeps its midnight split");
            filtered.BrowserVisitFilterVersion=1; VisitRecorder.CleanLegacyHistory(filtered);
            Check(filtered.Activities.Count==2,"Future startup preserves short midnight pieces of a qualified visit");
            filtered=Fixture(); recorder=new VisitRecorder();
            recorder.Add(filtered,stamp,9,new Snapshot {App="explorer",Title="Folder A"});
            Check(filtered.Activities.Count==0,"Desktop apps also require ten continuous seconds");
            recorder.Add(filtered,stamp.AddSeconds(1),1,new Snapshot {App="explorer",Title="Folder B"});
            Check(filtered.Activities.Single().Seconds==10,"Changing folders within one app preserves its minimum threshold");
            filtered=new State {CaptureTitles=false,IncludePrivateWindows=true,PrivacyVersion=1}; recorder=new VisitRecorder(); recorder.Add(filtered,stamp,1,page);
            Check(filtered.Activities.Count==0,"App-only mode also filters short sessions");
            recorder.Add(filtered,stamp.AddSeconds(9),9,page);
            Check(filtered.Activities.Single().Seconds==10 && filtered.Activities[0].Title=="","App-only mode qualifies without storing page information");
            filtered=Fixture(); recorder=new VisitRecorder();
            recorder.Add(filtered,stamp,9,page); recorder.Add(filtered,stamp.AddSeconds(100),100,page); recorder.Add(filtered,stamp.AddSeconds(101),1,page);
            Check(filtered.Activities.Count==0,"Suspend gaps discard pending page visits");
            filtered.Activities.Add(new Activity {App="chrome",Title="Quick",Seconds=1});
            filtered.Activities.Add(new Activity {App="chrome",Title="Long",Seconds=10});
            filtered.Activities.Add(new Activity {App="code",Title="Project",Seconds=1});
            Check(VisitRecorder.CleanLegacyHistory(filtered)==1 && filtered.Activities.Count==2,"Migration removes only legacy short browser rows");
            Check(VisitRecorder.CleanLegacyApps(filtered)==1 && filtered.Activities.Count==1,"Desktop history cleanup removes short app totals");
            var resourceState=Fixture(); var monitor=new ResourceMonitor();
            var r0=ResourceSample(stamp,0,"code",0,0);
            var r1=ResourceSample(stamp.AddSeconds(5),5,"code",2,1);
            var r2=ResourceSample(stamp.AddSeconds(10),10,"code",4,2);
            var compared=ResourceMonitor.Compare(r0,r1).Single();
            Check(compared.Seconds==5 && compared.CpuSeconds==3 && compared.MemoryMb==300,"Browser child processes combine CPU/RAM without multiplying runtime");
            Check(compared.CpuCapacitySeconds==40 && compared.Background,"CPU normalization uses logical processors and background focus");
            monitor.Accept(resourceState,r0); monitor.Accept(resourceState,r1);
            Check(resourceState.Resources.Count==0,"Background runs shorter than ten seconds stay out of history");
            monitor.Accept(resourceState,r2);
            Check(resourceState.Resources.Single().RunningSeconds==10 && resourceState.Resources.Single().BackgroundSeconds==10,"Background run qualifies with its first ten seconds");
            Check(resourceState.Resources.Single().CpuSeconds==6 && resourceState.Resources.Single().MemoryMbSeconds==3000,"Resource measurements persist CPU seconds and time-weighted memory");
            var focus=ResourceSample(stamp.AddSeconds(15),15,"chrome",6,3);
            monitor.Accept(resourceState,focus);
            Check(resourceState.Resources.Single().RunningSeconds==15 && resourceState.Resources.Single().BackgroundSeconds==10,"Foreground transition does not count as confirmed background time");
            monitor.Reset(); monitor.Accept(resourceState,r0); monitor.Accept(resourceState,r1); monitor.Reset();
            Check(resourceState.Resources.Single().RunningSeconds==15,"Pause discards short pending resource intervals");
            Check(ResourceMonitor.Compare(r0,ResourceSample(stamp.AddSeconds(100),100,"code",50,50)).Count==0,"Suspended resource time is not recorded");
            var reused=ResourceSample(stamp.AddSeconds(5),5,"code",2,1); reused.Processes.ForEach(p=>p.Identity+="new-start");
            Check(ResourceMonitor.Compare(r0,reused).Count==0,"Restarted process identities do not inherit CPU time");
            var inaccessible=ResourceSample(stamp.AddSeconds(5),5,"code",2,1); inaccessible.Processes[0].CpuSeconds=null;
            Check(ResourceMonitor.Compare(r0,inaccessible).Single().CpuCapacitySeconds==0,"Unavailable CPU stays unavailable rather than appearing as zero usage");
            var excludedResources=Fixture(); excludedResources.Excluded.Add("chrome"); var excludedMonitor=new ResourceMonitor();
            excludedMonitor.Accept(excludedResources,r0); excludedMonitor.Accept(excludedResources,r1); excludedMonitor.Accept(excludedResources,r2);
            Check(excludedResources.Resources.Count==0,"Excluded apps are not stored in resource history");
            var overnightResources=Fixture(); ResourceMonitor.Accumulate(overnightResources,new ResourceInterval {App="chrome",End=midnight,Seconds=5,MemoryMb=100,CpuSeconds=2,CpuCapacitySeconds=40,Background=true});
            Check(overnightResources.Resources.Count==2 && overnightResources.Resources.Sum(r=>r.CpuSeconds)==2 && overnightResources.Resources.Sum(r=>r.RunningSeconds)==5,"Resource history splits across midnight without changing totals");
            var chartRows=new[] {
                new Activity {App="chrome",Title="Lesson",Domain="learn.example",Category="Education",Seconds=3600},
                new Activity {App="chrome",Title="Video",Domain="video.example",Category="Fun",Seconds=1800},
                new Activity {App="code",Title="Project",Domain="",Category="Work",Seconds=900},
                new Activity {App="chrome",Title="Lesson",Domain="learn.example",Category="Education",Seconds=600}
            };
            foreach(int mode in new[]{0,1,2}) Check(Chart.Summarize(chartRows,mode).Sum(s=>s.Seconds)==6900,"Ring preserves total time in view "+mode);
            Check(ActivityDetails.Rank(chartRows,"chrome").First().Name=="learn.example" && ActivityDetails.Rank(chartRows,"chrome").First().Seconds==4200,"App details rank websites by total active time");
            Check(Chart.Summarize(chartRows,1).Count(s=>s.Name=="chrome")==2,"Browser activities retain distinct category colors");
            Check(Chart.Summarize(chartRows,2).Single(s=>s.Name=="Lesson").Seconds==4200,"Repeated page time aggregates across sessions");
            Check(Chart.Summarize(new Activity[0],0).Count==0,"Empty activity creates an empty ring");
            Check(Chart.Arc(-90,360) is System.Windows.Media.EllipseGeometry,"Single activity draws a complete circle");
            var arc=(System.Windows.Media.PathGeometry)Chart.Arc(-90,270);
            Check(((System.Windows.Media.ArcSegment)arc.Figures[0].Segments[0]).IsLargeArc,"Majority activity uses the long arc");
            string directory=Path.Combine(Path.GetTempPath(),"ScreenTime-tests-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var store=new Store(Path.Combine(directory,"test.json")); store.Save(state);
                Check(store.Load().Activities.Count==state.Activities.Count,"Activity survives restart");
                store.Save(state); File.WriteAllText(store.Path,"invalid");
                Check(store.Load().Activities.Count==state.Activities.Count && store.Warning!=null,"Corrupt storage recovers from backup");
                Check(Directory.GetFiles(directory,"*.unreadable-*").Length==1,"Unreadable data preserved for recovery");
                store.Save(resourceState); Check(store.Load().Resources.Single().CpuSeconds==9,"CPU and memory history survives restart");
                Check(Store.IsEncrypted(File.ReadAllBytes(store.Path)) && Store.IsEncrypted(File.ReadAllBytes(store.Path+".bak")),"History and rolling backup are encrypted at rest");
                Check(!Encoding.UTF8.GetString(File.ReadAllBytes(store.Path)).Contains("chrome"),"App names are not plaintext in encrypted files");
                Check(store.Load().Sessions.Count==resourceState.Sessions.Count && store.Load().Sessions.Single().SessionId==resourceState.Sessions.Single().SessionId,"Session identity and measurements survive restart");
                var legacy=new Store(Path.Combine(directory,"legacy.json"));
                using(var file=File.Create(legacy.Path)) new DataContractJsonSerializer(typeof(State)).WriteObject(file,resourceState);
                File.Copy(legacy.Path,legacy.Path+".bak");
                Check(legacy.Load().Resources.Count==1 && Store.IsEncrypted(File.ReadAllBytes(legacy.Path)) && Store.IsEncrypted(File.ReadAllBytes(legacy.Path+".bak")),"Plaintext history and backup migrate without losing records");
                File.WriteAllText(legacy.Path,"broken main"); File.WriteAllText(legacy.Path+".bak","broken backup");
                bool rejected=false; try { legacy.Load(); } catch(InvalidDataException) { rejected=true; }
                Check(rejected && File.ReadAllText(legacy.Path)=="broken main","Unrecoverable history fails closed instead of overwriting with an empty day");
            } finally { foreach(var path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory); }
            Console.WriteLine(count+" checks passed."); return 0;
        } catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex); return 1; }
    }
    static ResourceSnapshot ResourceSample(DateTime at,double monotonic,string foreground,double cpu1,double cpu2) {
        var result=new ResourceSnapshot { At=at,MonotonicSeconds=monotonic,Cores=8,Foreground=foreground };
        result.Processes.Add(new ProcessReading {App="chrome",Identity="1:start",CpuSeconds=cpu1,MemoryMb=100});
        result.Processes.Add(new ProcessReading {App="chrome",Identity="2:start",CpuSeconds=cpu2,MemoryMb=200}); return result;
    }
    static void NewFeatures() {
        var fresh=new State();
        Check(!fresh.IncludePrivateWindows && !fresh.CaptureTitles && !fresh.CollectBrowserResources,"Privacy-sensitive capture defaults off");
        fresh.CaptureTitles=true; fresh.IncludePrivateWindows=true; fresh.Normalize();
        Check(!fresh.CaptureTitles && !fresh.IncludePrivateWindows,"Legacy settings migrate to private-by-default behavior");
        Check(BrowserPrivacy.Classify("chrome",new[]{"Profile: Person 1"})==BrowserMode.Normal,"Supported normal browser requires explicit profile evidence");
        Check(BrowserPrivacy.Classify("chrome",new[]{"Profile","Incognito"})==BrowserMode.Private,"Private indicator overrides normal-looking controls");
        Check(BrowserPrivacy.Classify("firefox",new[]{"Profile"})==BrowserMode.Unknown && !BrowserPrivacy.MayRecord(BrowserMode.Unknown,false),"Unverified browsers fail closed, including Firefox always-private mode");
        var recorder=new VisitRecorder(); var stamp=new DateTime(2026,10,12,12,0,0);
        recorder.Add(fresh,stamp,10,new Snapshot {App="chrome",Title="secret",Domain="private.example",BrowserMode=BrowserMode.Private});
        Check(fresh.Activities.Count==0,"Private foreground apps and page metadata are not stored while opt-out is active");
        fresh.IncludePrivateWindows=true;
        recorder.Add(fresh,stamp,10,new Snapshot {App="chrome",BrowserMode=BrowserMode.Private});
        Check(fresh.Activities.Count==1,"Explicit private-recording opt-in permits private activity");
        var r0=ResourceSample(stamp,0,"code",0,0); var r1=ResourceSample(stamp.AddSeconds(5),5,"code",2,1); var r2=ResourceSample(stamp.AddSeconds(10),10,"code",4,2);
        var resourcePrivate=new State(); var monitor=new ResourceMonitor();
        monitor.Accept(resourcePrivate,r0); monitor.Accept(resourcePrivate,r1); monitor.Accept(resourcePrivate,r2);
        Check(resourcePrivate.Resources.Count==0 && resourcePrivate.Sessions.Count==0,"Browser background totals and sessions remain off by default");
        Check(CalendarPeriod.Start(new DateTime(2026,10,18),1)==new DateTime(2026,10,12) && CalendarPeriod.End(new DateTime(2026,10,18),1)==new DateTime(2026,10,19),"Calendar weeks start Monday and end before next Monday");
        Check(CalendarPeriod.Contains("2024-02-29",new DateTime(2024,2,1),2) && !CalendarPeriod.Contains("2024-03-01",new DateTime(2024,2,1),2),"Leap-year calendar months retain exactly their own days");
        Check(CalendarPeriod.Contains("2025-12-31",new DateTime(2026,1,1),1),"Weeks across year boundaries include prior-year history");
        Check(CalendarPeriod.Move(new DateTime(2026,3,31),2,-1)==new DateTime(2026,2,28),"Month navigation handles shorter months");
        Check(!CalendarPeriod.Contains("invalid",stamp,0),"Malformed dates cannot leak into selected history");
        Check(StartupManager.Command(@"C:\App Folder\ScreenTime.exe")=="\"C:\\App Folder\\ScreenTime.exe\" --background","Startup command quotes paths with spaces");
        var state=Fixture(); monitor=new ResourceMonitor(); monitor.Accept(state,r0); monitor.Accept(state,r1); monitor.Accept(state,r2);
        Check(state.Sessions.Single().RunningSeconds==10 && state.Sessions.Single().BackgroundSeconds==10,"Ten-second minimum creates one app-running session");
        string first=state.Sessions.Single().SessionId;
        monitor.Reset(); monitor.Accept(state,r0); monitor.Accept(state,r1); monitor.Accept(state,r2);
        Check(state.Sessions.Count==2 && state.Sessions.Last().SessionId!=first,"New observation after pause or restart gets a separate session");
        var midnight=new DateTime(2026,10,13,0,0,5); var night=Fixture();
        ResourceMonitor.Accumulate(night,new ResourceInterval { App="overwatch",SessionId="session",End=midnight,Seconds=10,PrivateMemoryMb=2048,Gpu=60,SystemCpu=35,SystemMemoryMb=9000,Foreground=true });
        ResourceMonitor.Accumulate(night,new ResourceInterval { App="overwatch",SessionId="session",End=midnight.AddSeconds(5),Seconds=5,PrivateMemoryMb=1024,Gpu=null,Foreground=true });
        var combined=ActivityDetails.Combine(night.Sessions);
        Check(night.Sessions.Count==2 && night.Sessions.Select(r=>r.SessionId).Distinct().Count()==1 && combined.RunningSeconds==15,"Midnight divides a session into days without duplicating elapsed time");
        Check(Math.Abs(combined.Metric("PrivateRam").Average-1706.666666)<0.001 && combined.Metric("PrivateRam").Peak==2048,"RAM averages are duration weighted and peaks preserved");
        Check(combined.Metric("Gpu").Average==60 && combined.Metric("Gpu").Seconds==10,"Missing GPU samples do not pull averages toward zero");
        Check(combined.Metric("Fan").Seconds==0 && combined.Metric("SystemCpu").Average==35,"Unsupported fans stay unavailable and PC metrics stay separate");
        r1.Processes[0].PrivateMemoryMb=25; r1.Processes[1].PrivateMemoryMb=50;
        Check(ResourceMonitor.Compare(r0,r1).Single().PrivateMemoryMb==75,"Private working set stays separate from shared-inclusive working set");
        r1.Processes[1].PrivateMemoryMb=null;
        Check(!ResourceMonitor.Compare(r0,r1).Single().PrivateMemoryMb.HasValue,"Partial process memory is unavailable instead of an incomplete total");
        var shifted=ResourceSample(stamp.AddHours(1),5,"code",2,1);
        Check(ResourceMonitor.Compare(r0,shifted).Count==0,"Clock changes cannot fabricate a long session");
        var gpu0=new ResourceSnapshot {GpuAvailable=true}; var gpu1=new ResourceSnapshot {GpuAvailable=true};
        string a="pid_1_luid_0x0000_0x0001_phys_0_eng_0_engtype_3D", b="pid_2_luid_0x0000_0x0001_phys_0_eng_0_engtype_3D", c="pid_1_luid_0x0000_0x0001_phys_0_eng_1_engtype_Copy";
        foreach(string key in new[]{a,b,c}) gpu0.GpuSamples[key]=GpuSample(0,10000000);
        gpu1.GpuSamples[a]=GpuSample(2000000,20000000); gpu1.GpuSamples[b]=GpuSample(3000000,20000000); gpu1.GpuSamples[c]=GpuSample(4000000,20000000);
        Check(Math.Abs(ResourceMonitor.BusiestGpu(gpu0,gpu1,new HashSet<int>{1,2}).Value-50)<0.01,"GPU sums child processes per engine then selects the busiest engine");
        Check(Math.Abs(ResourceMonitor.BusiestGpu(gpu0,gpu1,new HashSet<int>{1}).Value-40)<0.01,"Per-app GPU excludes other processes");
        gpu0.GpuSamples.Remove(a);
        Check(!ResourceMonitor.BusiestGpu(gpu0,gpu1,new HashSet<int>{1}).HasValue,"New GPU counters wait for a second sample");
    }
    static CounterSample GpuSample(long raw,long time) { return new CounterSample(raw,0,10000000,10000000,time,time,PerformanceCounterType.Timer100Ns); }
    static void Live() {
        // Known allocation and single-core load; no browser data or real history is saved.
        var allocation=new byte[128*1024*1024]; for(int i=0;i<allocation.Length;i+=4096) allocation[i]=1;
        var first=ResourceMonitor.Capture();
        var watch=Stopwatch.StartNew(); long iterations=0;
        var work=Task.Run(()=> { while(watch.Elapsed.TotalSeconds<4) Interlocked.Increment(ref iterations); });
        work.Wait(); var second=ResourceMonitor.Capture();
        int pid=Process.GetCurrentProcess().Id;
        var process=second.Processes.Single(p=>p.Pid==pid);
        var usage=ResourceMonitor.Compare(first,second).Single(r=>r.App==process.App);
        Check(process.PrivateMemoryMb.HasValue && process.PrivateMemoryMb.Value>=120,"Native private RAM detects the controlled 128 MiB allocation");
        double? perfMemory=null;
        var counters=new PerformanceCounterCategory("Process").ReadCategory();
        foreach(InstanceData entry in counters["ID Process"].Values) if(entry.RawValue==pid) perfMemory=counters["Working Set - Private"][entry.InstanceName].RawValue/1048576.0;
        Check(perfMemory.HasValue && Math.Abs(perfMemory.Value-process.PrivateMemoryMb.Value)<32,"Private RAM agrees with independent Windows performance counter within sampling tolerance");
        double cpu=100*usage.CpuSeconds/usage.CpuCapacitySeconds;
        Check(cpu>30.0/Environment.ProcessorCount && cpu<=100,"Known CPU load yields a positive normalized process reading");
        Check(usage.SystemCpu.HasValue && usage.SystemMemoryMb>128,"Whole-PC CPU and used physical RAM are readable");
        Console.WriteLine("Live check: private RAM="+process.PrivateMemoryMb.Value.ToString("0.0")+" MiB; Windows counter="+perfMemory.Value.ToString("0.0")+" MiB; CPU="+cpu.ToString("0.00")+"%; GPU counters="+second.GpuAvailable+"; RAM firmware MHz="+second.RamClock+"; fan RPM unavailable.");
        GC.KeepAlive(allocation);
    }
}
