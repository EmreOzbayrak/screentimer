using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ScreenTime {
    public class RankedActivity { public string Name, Note, Category; public double Seconds; }
    public static class ActivityDetails {
        public static List<RankedActivity> Rank(IEnumerable<Activity> rows,string app) {
            return rows.Where(a=>a.App==app).GroupBy(a=>!String.IsNullOrEmpty(a.Domain)?"site:"+a.Domain:!String.IsNullOrEmpty(a.Title)?"title:"+a.Title:"app:"+app)
                .Select(g=>new RankedActivity {
                    Name=g.Key.Substring(g.Key.IndexOf(':')+1),
                    Note=g.Key.StartsWith("site:")?"Website":g.Key.StartsWith("title:")?(Logic.IsBrowser(app)?"Page title · website unavailable":"Window"):"App activity",
                    Category=g.GroupBy(a=>a.Category).OrderByDescending(c=>c.Sum(a=>a.Seconds)).First().Key,
                    Seconds=g.Sum(a=>a.Seconds)
                }).OrderByDescending(a=>a.Seconds).ToList();
        }
        public static ResourceUsage Combine(IEnumerable<ResourceUsage> source) {
            var rows=source.ToList();
            var combined=new ResourceUsage {
                RunningSeconds=rows.Sum(r=>r.RunningSeconds),BackgroundSeconds=rows.Sum(r=>r.BackgroundSeconds),
                ForegroundSeconds=rows.Sum(r=>r.ForegroundSeconds),
                CpuSeconds=rows.Sum(r=>r.CpuSeconds),CpuCapacitySeconds=rows.Sum(r=>r.CpuCapacitySeconds),
                BackgroundCpuSeconds=rows.Sum(r=>r.BackgroundCpuSeconds),BackgroundCpuCapacitySeconds=rows.Sum(r=>r.BackgroundCpuCapacitySeconds),
                MemoryMbSeconds=rows.Sum(r=>r.MemoryMbSeconds),PeakMemoryMb=rows.Count==0?0:rows.Max(r=>r.PeakMemoryMb),
                BackgroundMemoryMbSeconds=rows.Sum(r=>r.BackgroundMemoryMbSeconds),
                FirstSeen=rows.Count==0?DateTime.MinValue:rows.Min(r=>r.FirstSeen),LastSeen=rows.Count==0?DateTime.MinValue:rows.Max(r=>r.LastSeen)
            };
            foreach(var row in rows) if(row.Metrics!=null) foreach(var metric in row.Metrics) {
                if(!combined.Metrics.ContainsKey(metric.Key)) combined.Metrics[metric.Key]=new MetricSummary();
                var value=combined.Metrics[metric.Key]; value.Total+=metric.Value.Total; value.Seconds+=metric.Value.Seconds; value.Peak=Math.Max(value.Peak,metric.Value.Peak);
            }
            return combined;
        }
    }
    public partial class Program {
        private bool InRange(string day) {
            return CalendarPeriod.Contains(day,selectedDate,Find<ComboBox>("RangeBox").SelectedIndex);
        }
        private string RangeName() { return CalendarPeriod.Label(selectedDate,Find<ComboBox>("RangeBox").SelectedIndex); }
        private static string Memory(double mb) { return mb>=1024?(mb/1024).ToString("0.00")+" GiB":mb.ToString("0")+" MiB"; }
        private static string Reading(ResourceUsage row,string metric,bool memory=false,bool peak=false) {
            var value=row.Metric(metric); if(value.Seconds<=0) return "Unavailable";
            return memory?Memory(peak?value.Peak:value.Average):(peak?value.Peak:value.Average).ToString("0.0")+"%";
        }
        private static string Cpu(double seconds,double capacity) { return capacity>0?(seconds/capacity*100).ToString("0.0")+"%":"Unavailable"; }
        private static string CpuTime(double seconds) { return seconds<60?seconds.ToString("0.0")+"s":Logic.Duration(seconds)+" "+((int)seconds%60)+"s"; }
        private TextBlock Heading(string text) { return new TextBlock { Text=text,FontSize=21,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) }; }
        private Border Card(StackPanel content) { return new Border { Background=Brush("#333333"),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Margin=new Thickness(0,9,0,3),Child=content }; }
        private Grid Metric(string label,string value) {
            var grid=new Grid { Margin=new Thickness(0,5,0,5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            grid.Children.Add(new TextBlock { Text=label,FontSize=11,Foreground=Brush("#BBBBBB") });
            var number=new TextBlock { Text=value,FontSize=11,FontWeight=FontWeights.SemiBold }; Grid.SetColumn(number,1); grid.Children.Add(number); return grid;
        }
        private ComboBox detailCategory;
        private void ShowAppDetails(string app,bool refresh=false) {
            if(refresh && detailCategory!=null && detailCategory.IsDropDownOpen) return;
            double offset=refresh?Find<ScrollViewer>("EditorScroll").VerticalOffset:0;
            object selectedCategory=refresh && detailCategory!=null?detailCategory.SelectedItem:null;
            var oldPanel=refresh?Find<ContentControl>("EditorContent").Content as StackPanel:null;
            var expanded=new HashSet<object>(oldPanel==null?new object[0]:oldPanel.Children.OfType<Expander>().Where(e=>e.IsExpanded).Select(e=>e.Tag));
            detailApp=app;
            var panel=new StackPanel { Margin=new Thickness(0,0,8,8) };
            panel.Children.Add(Heading(Friendly(app)));
            var activities=state.Activities.Where(a=>InRange(a.Day) && a.App==app).ToList();
            panel.Children.Add(Label(RangeName()+" · "+Logic.Duration(activities.Sum(a=>a.Seconds))+" actively used"));
            var live=new StackPanel();
            live.Children.Add(new TextBlock { Text="Live now · last sample",FontWeight=FontWeights.SemiBold,FontSize=13 });
            var current=liveResources.FirstOrDefault(r=>r.App==app);
            bool fresh=latestResources!=null && (DateTime.Now-latestResources.At).TotalSeconds>=0 && (DateTime.Now-latestResources.At).TotalSeconds<=15;
            if(!paused && !locked && fresh && current!=null) {
                live.Children.Add(Metric("App CPU · all processors",Cpu(current.CpuSeconds,current.CpuCapacitySeconds)));
                live.Children.Add(Metric("App private RAM",current.PrivateMemoryMb.HasValue?Memory(current.PrivateMemoryMb.Value):"Unavailable"));
                live.Children.Add(Metric("App GPU · busiest engine",current.Gpu.HasValue?current.Gpu.Value.ToString("0.0")+"%":"Unavailable"));
                live.Children.Add(Label("Updated "+current.End.ToString("HH:mm:ss")+" · CPU/GPU sampled over "+current.Seconds.ToString("0.0")+"s"));
            } else live.Children.Add(Label(paused || locked?"Sampling paused":!fresh?"Waiting for a fresh sample…":latestResources.Processes.Any(p=>p.App==app)?"Collecting the next sample…":"App is not currently running"));
            panel.Children.Add(Card(live));
            var usage=ActivityDetails.Combine(state.Resources.Where(r=>InRange(r.Day) && r.App==app));
            if(usage.RunningSeconds>0) {
                var stats=new StackPanel();
                stats.Children.Add(new TextBlock { Text="Selected period · averages & peaks",FontWeight=FontWeights.SemiBold,FontSize=13,Margin=new Thickness(0,0,0,5) });
                stats.Children.Add(Label("Includes menus, gameplay and background time. These are historical averages, not live readings."));
                stats.Children.Add(Metric("Observed running",Logic.Duration(usage.RunningSeconds)));
                stats.Children.Add(Metric("In background",Logic.Duration(usage.BackgroundSeconds)));
                stats.Children.Add(Metric("App CPU avg · all processors",Cpu(usage.CpuSeconds,usage.CpuCapacitySeconds)));
                stats.Children.Add(Metric("CPU time consumed",CpuTime(usage.CpuSeconds)));
                stats.Children.Add(Metric("Private RAM avg / peak",Reading(usage,"PrivateRam",true)+" / "+Reading(usage,"PrivateRam",true,true)));
                stats.Children.Add(Metric("GPU avg / peak",Reading(usage,"Gpu")+" / "+Reading(usage,"Gpu",false,true)));
                stats.Children.Add(Metric("Background CPU avg",Cpu(usage.BackgroundCpuSeconds,usage.BackgroundCpuCapacitySeconds)));
                stats.Children.Add(Metric("Background private RAM",Reading(usage,"BackgroundPrivateRam",true)));
                if(usage.Metric("PrivateRam").Seconds==0) stats.Children.Add(Label("Older RAM records include shared memory: "+Memory(usage.MemoryMbSeconds/usage.RunningSeconds)+" average working set. Private RAM was not measured then, or is unavailable."));
                stats.Children.Add(Label("Last observed: "+usage.LastSeen.ToString("d MMM, HH:mm")));
                panel.Children.Add(Card(stats));
            } else panel.Children.Add(Label("No resource history in this period. Current minimum before logging: "+state.MinimumLogSeconds+" seconds. Resource samples arrive approximately every 5 seconds."));
            AddSessions(panel,app);
            foreach(var session in panel.Children.OfType<Expander>()) session.IsExpanded=expanded.Contains(session.Tag);
            panel.Children.Add(new TextBlock { Text=Logic.IsBrowser(app)?"Most-used websites & pages":"Activity details",FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,5) });
            var ranked=ActivityDetails.Rank(activities,app);
            if(ranked.Count==0) panel.Children.Add(Label("No qualifying foreground visits in this period. Current minimum before logging: "+state.MinimumLogSeconds+" seconds."));
            foreach(var row in ranked) {
                var content=new StackPanel();
                var line=Metric(row.Name,Logic.Duration(row.Seconds));
                ((TextBlock)line.Children[0]).TextTrimming=TextTrimming.CharacterEllipsis;
                ((TextBlock)line.Children[0]).Margin=new Thickness(0,0,12,0);
                line.ToolTip=row.Name; content.Children.Add(line);
                content.Children.Add(new TextBlock { Text="● "+row.Category+" · "+row.Note,Foreground=Brush(CategoryColor(row.Category)),FontSize=10,TextWrapping=TextWrapping.Wrap });
                panel.Children.Add(Card(content));
            }
            var controls=new StackPanel();
            controls.Children.Add(Label("Default category for this app (website overrides keep priority)"));
            var categories=new ComboBox { ItemsSource=Logic.Categories,SelectedItem=selectedCategory??Logic.Classify(state,app,"","") };
            detailCategory=categories;
            controls.Children.Add(categories);
            var save=new Button { Content="Save app category",Margin=new Thickness(0,8,0,0) };
            save.Click+=(s,e)=> {
                state.Rules[Logic.RuleKey(app,"")]=(string)categories.SelectedItem;
                foreach(var row in state.Activities.Where(a=>a.App==app)) row.Category=Logic.Classify(state,row.App,row.Title,row.Domain);
                Save(); ShowAppDetails(app);
            };
            controls.Children.Add(save); panel.Children.Add(controls);
            var refreshButton=new Button { Content="Refresh details",Margin=new Thickness(0,12,0,0) };
            refreshButton.Click+=(s,e)=>ShowAppDetails(app,true); panel.Children.Add(refreshButton);
            OpenEditor(panel); Find<ScrollViewer>("EditorScroll").ScrollToVerticalOffset(offset);
        }
        private ComboBox backgroundSortBox;
        private void ShowBackground(bool refresh=false) {
            if(refresh && backgroundSortBox!=null && backgroundSortBox.IsDropDownOpen) return;
            showingBackground=true; detailApp=null;
            double offset=refresh?Find<ScrollViewer>("EditorScroll").VerticalOffset:0;
            var panel=new StackPanel { Margin=new Thickness(0,0,8,8) };
            panel.Children.Add(Heading("Background & resources"));
            panel.Children.Add(Label(RangeName()+" · current logging minimum: "+state.MinimumLogSeconds+" seconds"));
            panel.Children.Add(Label("Runtime includes foreground and background. Click an app for sessions, measured averages and whole-PC usage."));
            if(!state.CollectBrowserResources) panel.Children.Add(Label("Browser resource collection is off for privacy. You can opt in in Settings."));
            var sort=new ComboBox { ItemsSource=new[]{"Most background time","Most CPU time","Highest average RAM"},SelectedIndex=backgroundSort,Margin=new Thickness(0,8,0,5) };
            backgroundSortBox=sort;
            sort.SelectionChanged+=(s,e)=> { backgroundSort=sort.SelectedIndex; ShowBackground(); }; panel.Children.Add(sort);
            var rows=state.Resources.Where(r=>InRange(r.Day)).GroupBy(r=>r.App).Select(g=>new { App=g.Key,Usage=ActivityDetails.Combine(g) });
            var ordered=rows.OrderByDescending(r=>backgroundSort==1?r.Usage.CpuSeconds:backgroundSort==2?r.Usage.Metric("PrivateRam").Average:r.Usage.BackgroundSeconds).ToList();
            if(ordered.Count==0) panel.Children.Add(Label("Collecting resource history… Apps appear after the configured logging minimum and a complete sample. Sampling continues while you are idle, and stops when paused, locked, or asleep."));
            foreach(var item in ordered) {
                var usage=item.Usage; string app=item.App;
                var content=new StackPanel();
                content.Children.Add(new TextBlock { Text=Friendly(app)+"  ›",FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis });
                content.Children.Add(Metric("Running / background",Logic.Duration(usage.RunningSeconds)+" / "+Logic.Duration(usage.BackgroundSeconds)));
                content.Children.Add(Metric("CPU avg / CPU time",Cpu(usage.CpuSeconds,usage.CpuCapacitySeconds)+" / "+CpuTime(usage.CpuSeconds)));
                content.Children.Add(Metric("Private RAM avg",Reading(usage,"PrivateRam",true)));
                content.Children.Add(Metric("GPU avg",Reading(usage,"Gpu")));
                string status=paused || locked?"Sampling paused":latestResources==null || (DateTime.Now-latestResources.At).TotalSeconds>15?"Waiting for a fresh sample":latestResources.Processes.Any(p=>p.App==app)?"Running now":"Not currently observed";
                content.Children.Add(new TextBlock { Text=status+" · last seen "+usage.LastSeen.ToString("d MMM HH:mm"),Foreground=Brush("#A6B5BD"),FontSize=10 });
                var button=new Button { Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,7,0,0),Padding=new Thickness(12) };
                button.Click+=(s,e)=>ShowAppDetails(app); panel.Children.Add(button);
            }
            panel.Children.Add(Label("Sampled every 5 seconds. RAM uses private working set; CPU uses execution time divided by all logical processors. GPU uses the busiest engine, not a sum of engines. Averages cover measured intervals and may differ from Task Manager's live values. Missing readings are unavailable, not zero."));
            if(latestResources!=null && latestResources.Inaccessible>0) panel.Children.Add(Label("Some protected or exiting processes could not be measured."));
            OpenEditor(panel); Find<ScrollViewer>("EditorScroll").ScrollToVerticalOffset(offset);
        }
        private void AddSessions(StackPanel panel,string app) {
            panel.Children.Add(Heading("Sessions"));
            panel.Children.Add(Label("One observed app run, including background time. Pause, lock, sleep, exit or a sampling gap ends it. Times below show the portion within this calendar period."));
            var sessions=state.Sessions.Where(r=>r.App==app && InRange(r.Day)).GroupBy(r=>r.SessionId).Select(g=>ActivityDetails.Combine(g)).OrderByDescending(r=>r.FirstSeen).ToList();
            if(sessions.Count==0) panel.Children.Add(Label("No sessions recorded in this period. Earlier daily totals are preserved; session details begin with this update."));
            foreach(var session in sessions) {
                var stats=new StackPanel();
                stats.Children.Add(Metric("Observed running",Logic.Duration(session.RunningSeconds)));
                stats.Children.Add(Metric("Foreground / background",Logic.Duration(session.ForegroundSeconds)+" / "+Logic.Duration(session.BackgroundSeconds)));
                stats.Children.Add(Metric("App CPU avg / peak",Reading(session,"Cpu")+" / "+Reading(session,"Cpu",false,true)));
                stats.Children.Add(Metric("App private RAM avg",Reading(session,"PrivateRam",true)));
                stats.Children.Add(Metric("App private RAM peak",Reading(session,"PrivateRam",true,true)));
                stats.Children.Add(Metric("App GPU avg / peak",Reading(session,"Gpu")+" / "+Reading(session,"Gpu",false,true)));
                stats.Children.Add(Label("Whole PC during this session (all apps combined)"));
                stats.Children.Add(Metric("PC CPU avg / peak",Reading(session,"SystemCpu")+" / "+Reading(session,"SystemCpu",false,true)));
                stats.Children.Add(Metric("PC used RAM avg / peak",Reading(session,"SystemRam",true)+" / "+Reading(session,"SystemRam",true,true)));
                stats.Children.Add(Metric("PC GPU avg / peak",Reading(session,"SystemGpu")+" / "+Reading(session,"SystemGpu",false,true)));
                var speed=session.Metric("RamClock");
                stats.Children.Add(Metric("RAM speed · firmware",speed.Seconds>0?speed.Average.ToString("0")+" MHz":"Unavailable"));
                stats.Children.Add(Metric("Fan speed", "Sensor unavailable"));
                stats.Children.Add(Label("Measured coverage: CPU "+Logic.Duration(session.Metric("Cpu").Seconds)+", RAM "+Logic.Duration(session.Metric("PrivateRam").Seconds)+", GPU "+Logic.Duration(session.Metric("Gpu").Seconds)+". Peaks are sampled; focus transitions may be neither foreground nor background."));
                var expander=new Expander { Header=session.FirstSeen.ToString("d MMM HH:mm")+" – "+session.LastSeen.ToString("HH:mm")+" · "+Logic.Duration(session.RunningSeconds),Foreground=Brush("#EEEEEE"),FontSize=11,Content=stats,Margin=new Thickness(0,10,0,5) };
                expander.Tag=session.FirstSeen;
                panel.Children.Add(expander);
            }
        }
    }
}
