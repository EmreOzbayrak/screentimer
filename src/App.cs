using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace ScreenTime {
    public class ActivityView {
        public string Name {get;set;} public string Detail {get;set;} public string Category {get;set;} public string Time {get;set;}
        public string Color {get;set;} public string Tooltip {get;set;}
        public Activity Source;
        public double Seconds;
    }
    public partial class Program {
        private Window window;
        private State state;
        private Store store;
        private Tracker tracker=new Tracker();
        private VisitRecorder visits=new VisitRecorder();
        private ResourceMonitor resources=new ResourceMonitor();
        private Task<ResourceSnapshot> resourceTask;
        private int resourceGeneration, taskGeneration;
        private DateTime nextResourceSample=DateTime.MinValue;
        private ResourceSnapshot latestResources;
        private List<ResourceInterval> liveResources=new List<ResourceInterval>();
        private string detailApp;
        private bool showingBackground;
        private int backgroundSort;
        private Forms.NotifyIcon tray;
        private DispatcherTimer timer;
        private bool paused, exiting, locked;
        private Snapshot previous;
        private Stopwatch elapsed=Stopwatch.StartNew();
        private DateTime lastSave=DateTime.UtcNow;
        private string lastError;
        private int refreshTick;
        private int viewMode=1, goalIndex;
        private DateTime selectedDate=DateTime.Today;
        private bool followToday=true;
        private bool refreshing, keepOpen, smokeMode;
        private static Mutex mutex;
        private static EventWaitHandle activate;
        private static EventWaitHandle stop;
        private readonly Dictionary<string,string> colors=new Dictionary<string,string> { {"Work","#4CC2FF"},{"Education","#80D5AA"},{"Fun","#F4BA76"},{"Social","#C5A2EF"},{"Other","#A5ADB8"} };
        [STAThread] public static void Main(string[] args) {
            bool first;
            string scope=args.Contains("--smoke-test")?".Smoke":"";
            mutex=new Mutex(true,"Local\\ScreenTime.Desktop.v1"+scope,out first);
            activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ScreenTime.Desktop.Activate.v1"+scope);
            stop=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ScreenTime.Desktop.Stop.v1"+scope);
            if(args.Contains("--quit")) { if(!first) stop.Set(); return; }
            if(!first) { activate.Set(); return; }
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            try { new Program().Run(args); app.Run(); }
            catch(Exception ex) {
                Environment.ExitCode=1;
                if(args.Contains("--smoke-test")) File.WriteAllText(Path.Combine(Path.GetTempPath(),"ScreenTime-smoke-error.txt"),ex.ToString());
                else MessageBox.Show("ScreenTime could not start:\n"+ex.Message,"ScreenTime");
            }
            finally { activate.Dispose(); stop.Dispose(); mutex.ReleaseMutex(); mutex.Dispose(); }
        }
        private T Find<T>(string name) where T:class { return window.FindName(name) as T; }
        private void Click(string name,RoutedEventHandler handler) { Find<Button>(name).Click+=handler; }
        private static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private void Run(string[] args) {
            smokeMode=args.Contains("--smoke-test");
            store=new Store(args.Contains("--smoke-test") ? Path.Combine(Path.GetTempPath(),"ScreenTime-smoke","activity.json") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenTime","activity.json"));
            state=store.Load();
            string startupWarning=null;
            if(!smokeMode) try {
                if(args.Contains("--enable-startup")) { state.StartupEnabled=true; state.StartupVersion=1; StartupManager.Set(true,true); }
                if(state.StartupVersion<1) { state.StartupEnabled=StartupManager.Registered(); state.StartupVersion=1; }
                if(state.StartupEnabled && !StartupManager.Healthy()) StartupManager.Set(true,false);
                if(state.StartupEnabled && StartupManager.DisabledByWindows()) startupWarning="Windows has disabled startup. Enable ScreenTime in Task Manager → Startup apps, or save the enabled setting here.";
            } catch(Exception ex) { startupWarning="Startup needs repair: "+ex.Message; }
            // One-time cleanup of old short page rows; later midnight fragments
            // from qualified visits must never be filtered by their row duration.
            bool migrateVisits=state.BrowserVisitFilterVersion<1 || state.AppVisitFilterVersion<1;
            VisitRecorder.CleanLegacyHistory(state);
            VisitRecorder.CleanLegacyApps(state);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dashboard.xaml")) window=(Window)XamlReader.Load(stream);
            window.Closing+=(s,e)=>{ if(!exiting) { e.Cancel=true; window.Hide(); } };
            window.Deactivated+=(s,e)=>{ if(!keepOpen && !smokeMode) window.Hide(); };
            window.PreviewKeyDown+=(s,e)=>{ if(e.Key==System.Windows.Input.Key.Escape) { if(Find<Grid>("EditorPanel").Visibility==Visibility.Visible) CloseEditor(); else window.Hide(); e.Handled=true; } };
            Click("CloseButton",(s,e)=>window.Hide());
            Click("PauseButton",(s,e)=>TogglePause());
            Click("CategoriesButton",(s,e)=>SetView(0));
            Click("AppsButton",(s,e)=>SetView(1));
            Click("PagesButton",(s,e)=>SetView(2));
            Click("SettingsButton",(s,e)=>Settings());
            Click("BackgroundButton",(s,e)=>ShowBackground());
            Click("EditGoalsButton",(s,e)=>EditGoals());
            Click("NextGoalButton",(s,e)=>{ goalIndex++; Refresh(); });
            Click("BackButton",(s,e)=>CloseEditor());
            Click("ApplyCategory",(s,e)=>ApplyCategory());
            Click("CancelCategory",(s,e)=>{ Find<ListBox>("ActivityList").SelectedItem=null; });
            Find<ComboBox>("CategoryChoice").ItemsSource=Logic.Categories;
            Find<ComboBox>("CategoryChoice").SelectedIndex=0;
            Find<ComboBox>("RangeBox").SelectionChanged+=(s,e)=>Refresh();
            Find<DatePicker>("HistoryDate").SelectedDate=selectedDate;
            Find<DatePicker>("HistoryDate").SelectedDateChanged+=(s,e)=> {
                if(!Find<DatePicker>("HistoryDate").SelectedDate.HasValue) return;
                selectedDate=Find<DatePicker>("HistoryDate").SelectedDate.Value.Date; followToday=selectedDate==DateTime.Today; Refresh();
            };
            Click("PreviousPeriod",(s,e)=>MovePeriod(-1));
            Click("NextPeriod",(s,e)=>MovePeriod(1));
            Click("TodayButton",(s,e)=>{ followToday=true; selectedDate=DateTime.Today; Refresh(); });
            Find<ListBox>("ActivityList").SelectionChanged+=(s,e)=> {
                if(refreshing) return;
                var row=Find<ListBox>("ActivityList").SelectedItem as ActivityView;
                if(row!=null && viewMode==1) { ShowAppDetails(row.Source.App); return; }
                Find<StackPanel>("CategoryEditor").Visibility=row!=null && viewMode!=0?Visibility.Visible:Visibility.Collapsed;
                if(row!=null && viewMode!=0) {
                    Find<ComboBox>("CategoryChoice").SelectedItem=row.Category;
                    Find<TextBlock>("CategoryTarget").Text="Category for "+(viewMode==2 && !String.IsNullOrEmpty(row.Source.Domain)?row.Source.Domain:Friendly(row.Source.App));
                }
            };
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("Open ScreenTime",null,(s,e)=>Show());
            menu.Items.Add("Pause / resume tracking",null,(s,e)=>TogglePause());
            menu.Items.Add("Settings",null,(s,e)=>{ Show(); Settings(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit ScreenTime",null,(s,e)=>Quit());
            tray=new Forms.NotifyIcon { Text="ScreenTime • Tracking", Icon=CreateIcon(),ContextMenuStrip=menu,Visible=true };
            tray.MouseClick+=(s,e)=>{ if(e.Button==Forms.MouseButtons.Left) { if(window.IsVisible) window.Hide(); else Show(); } };
            SystemEvents.SessionSwitch+=SessionSwitch;
            SystemEvents.PowerModeChanged+=PowerChanged;
            SystemEvents.SessionEnding+=SessionEnding;
            timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
            timer.Tick+=(s,e)=>Tick(); timer.Start();
            if(store.Warning!=null) Notice(store.Warning);
            if(startupWarning!=null) Notice(startupWarning);
            if(migrateVisits) { Save(); if(lastError==null) Save(); }
            Refresh();
            if(!args.Contains("--background")) Show();
            if(args.Contains("--smoke-test")) {
                var smoke=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };
                smoke.Tick+=(s,e)=>{
                    smoke.Stop();
                    try {
                        state.Activities.Clear(); state.Resources.Clear(); state.Sessions.Clear(); previous=null; paused=true; UpdateStatus(); Refresh(); window.UpdateLayout(); RenderPreview("preview-empty.png");
                        string day=DateTime.Today.ToString("yyyy-MM-dd");
                        state.Activities.Add(new Activity { Day=day,App="Code",Title="My personal project",Domain="",Category="Work",Seconds=5400 });
                        state.Activities.Add(new Activity { Day=day,App="msedge",Title="Introduction to design - Course",Domain="coursera.org",Category="Education",Seconds=2700 });
                        state.Activities.Add(new Activity { Day=day,App="chrome",Title="My favourite videos - YouTube",Domain="youtube.com",Category="Fun",Seconds=4800 });
                        state.Activities.Add(new Activity { Day=day,App="Discord",Title="Friends",Domain="",Category="Social",Seconds=1200 });
                        state.Activities.Add(new Activity { Day=day,App="chrome",Title="A JavaScript tutorial",Domain="learn.example.com",Category="Education",Seconds=600 });
                        Refresh(); window.UpdateLayout(); RenderPreview("preview-sample.png");
                        var datePicker=Find<DatePicker>("HistoryDate");
                        ((Button)datePicker.Template.FindName("PART_Button",datePicker)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                        var calendarPopup=(System.Windows.Controls.Primitives.Popup)datePicker.Template.FindName("PART_Popup",datePicker);
                        var calendar=(System.Windows.Controls.Calendar)calendarPopup.Child;
                        if(!datePicker.IsDropDownOpen || !calendarPopup.IsOpen) throw new Exception("Date selector did not open.");
                        calendar.UpdateLayout(); RenderPreview("preview-calendar.png",calendar);
                        var calendarItem=(System.Windows.Controls.Primitives.CalendarItem)calendar.Template.FindName("PART_CalendarItem",calendar);
                        ((Button)calendarItem.Template.FindName("PART_HeaderButton",calendarItem)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        calendar.UpdateLayout(); RenderPreview("preview-calendar-months.png",calendar);
                        ((Button)calendarItem.Template.FindName("PART_HeaderButton",calendarItem)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        calendar.UpdateLayout(); RenderPreview("preview-calendar-years.png",calendar);
                        calendar.DisplayMode=CalendarMode.Month; calendar.SelectedDate=DateTime.Today.AddDays(-1);
                        if(selectedDate!=DateTime.Today.AddDays(-1)) throw new Exception("Calendar selection did not update history.");
                        datePicker.IsDropDownOpen=false; datePicker.SelectedDate=DateTime.Today;
                        SetView(0); window.UpdateLayout(); RenderPreview("preview-categories.png");
                        SetView(2); window.UpdateLayout(); RenderPreview("preview-pages.png");
                        Find<ListBox>("ActivityList").SelectedIndex=0; window.UpdateLayout(); RenderPreview("preview-edit-category.png");
                        Settings(); window.UpdateLayout(); RenderPreview("preview-settings.png"); CloseEditor();
                        EditGoals(); window.UpdateLayout(); RenderPreview("preview-goals.png"); CloseEditor();
                        ResourceMonitor.Accumulate(state,new ResourceInterval { App="chrome",SessionId="sample-session",End=DateTime.Now,Seconds=3600,CpuSeconds=150,CpuCapacitySeconds=28800,MemoryMb=1450,PrivateMemoryMb=920,Gpu=7,SystemCpu=24,SystemMemoryMb=8900,SystemGpu=19,RamClock=3200,Background=true });
                        ResourceMonitor.Accumulate(state,new ResourceInterval { App="Discord",End=DateTime.Now,Seconds=7200,CpuSeconds=90,CpuCapacitySeconds=57600,MemoryMb=340,Background=true });
                        SetView(1);
                        Find<ListBox>("ActivityList").SelectedItem=Find<ListBox>("ActivityList").Items.Cast<ActivityView>().First(v=>v.Source.App=="chrome");
                        if(detailApp!="chrome") throw new Exception("Clicking an app did not open details.");
                        window.UpdateLayout(); RenderPreview("preview-app-details.png");
                        var detailPanel=(StackPanel)Find<ContentControl>("EditorContent").Content;
                        var sessionPanel=detailPanel.Children.OfType<Expander>().First(); sessionPanel.IsExpanded=true;
                        detailCategory.SelectedItem="Education";
                        paused=false;
                        latestResources=new ResourceSnapshot { At=DateTime.Now };
                        liveResources.Add(new ResourceInterval { App="chrome",End=DateTime.Now,Seconds=5,CpuSeconds=14,CpuCapacitySeconds=140,PrivateMemoryMb=6144,Gpu=42 });
                        Refresh();
                        detailPanel=(StackPanel)Find<ContentControl>("EditorContent").Content;
                        sessionPanel=detailPanel.Children.OfType<Expander>().First();
                        if(!sessionPanel.IsExpanded || (string)detailCategory.SelectedItem!="Education") throw new Exception("Auto-refresh lost expanded sessions or an unsaved category.");
                        var liveCard=detailPanel.Children.OfType<Border>().First();
                        var livePanel=(StackPanel)liveCard.Child;
                        if(!livePanel.Children.OfType<Grid>().Any(g=>g.Children.OfType<TextBlock>().Any(t=>t.Text=="6.00 GiB"))) throw new Exception("Live usage did not refresh independently of historical averages.");
                        window.UpdateLayout(); RenderPreview("preview-live-resources.png");
                        ResetResources();
                        paused=true;
                        sessionPanel.BringIntoView(); window.UpdateLayout(); RenderPreview("preview-session.png"); CloseEditor();
                        ShowBackground(); window.UpdateLayout(); RenderPreview("preview-background.png");
                        ShowAppDetails("chrome"); CloseEditor();
                        if(!showingBackground || detailApp!=null) throw new Exception("Resource detail back navigation failed.");
                        CloseEditor();
                        state.Activities.Add(new Activity {Day=DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"),App="overwatch",Category="Fun",Seconds=10800,Title="",Domain=""});
                        MovePeriod(-1);
                        if(Find<TextBlock>("TotalTime").Text!="3h 0m") throw new Exception("Previous-day history navigation failed.");
                        window.UpdateLayout(); RenderPreview("preview-history.png");
                        Find<ComboBox>("RangeBox").SelectedIndex=2;
                        if(!InRange(DateTime.Today.ToString("yyyy-MM-dd"))) throw new Exception("Calendar month selection failed.");
                        Find<ComboBox>("RangeBox").SelectedIndex=0; followToday=true; Refresh();
                        state.Activities.RemoveRange(1,state.Activities.Count-1); SetView(0); window.UpdateLayout(); RenderPreview("preview-single.png");
                        window.Hide(); Show();
                        if(window.ShowInTaskbar || !window.Topmost || !window.IsVisible) throw new Exception("Tray popup visibility contract failed.");
                        Find<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(paused) throw new Exception("Resume control failed.");
                        Find<Button>("PauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(!paused) throw new Exception("Pause control failed.");
                        SetView(2); Find<ListBox>("ActivityList").SelectedIndex=0;
                        Find<ComboBox>("CategoryChoice").SelectedItem="Education";
                        Find<Button>("ApplyCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(state.Activities[0].Category!="Education") throw new Exception("Inline category editing failed.");
                        smokeMode=false;
                        var focusTest=new Window { Width=1,Height=1,Left=window.Left,Top=window.Top,ShowInTaskbar=false,WindowStyle=WindowStyle.None };
                        focusTest.Show(); focusTest.Activate();
                        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                        if(window.IsVisible) throw new Exception("Click-away dismissal failed.");
                        focusTest.Close(); smokeMode=true;
                    } catch(Exception ex) { Environment.ExitCode=1; File.WriteAllText(Path.Combine(Path.GetTempPath(),"ScreenTime-smoke-error.txt"),ex.ToString()); }
                    Quit();
                }; smoke.Start();
            }
        }
        private void RenderPreview(string filename,FrameworkElement target=null) {
            var visual=target??(FrameworkElement)window.Content;
            visual.InvalidateVisual(); visual.UpdateLayout();
            window.Dispatcher.Invoke(DispatcherPriority.Render,new Action(()=>{}));
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),filename))) encoder.Save(file);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        private System.Drawing.Icon CreateIcon() {
            using(var bitmap=new System.Drawing.Bitmap(32,32)) {
                using(var g=System.Drawing.Graphics.FromImage(bitmap)) {
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using(var fill=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(168,234,203))) g.FillEllipse(fill,2,2,28,28);
                    using(var pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(21,44,50),2.5f)) { g.DrawLine(pen,16,8,16,17); g.DrawLine(pen,16,17,22,20); }
                }
                var handle=bitmap.GetHicon();
                var icon=(System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); DestroyIcon(handle); return icon;
            }
        }
        private void Show() {
            // Use the tray/cursor monitor's working area, which excludes its taskbar.
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var source=PresentationSource.FromVisual(window);
            var transform=source!=null && source.CompositionTarget!=null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var area=Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
            Point origin=transform.Transform(new Point(area.Left,area.Top));
            Point corner=transform.Transform(new Point(area.Right,area.Bottom));
            window.Width=Math.Min(414,corner.X-origin.X-20);
            window.Height=Math.Min(756,corner.Y-origin.Y-20);
            window.Left=Math.Max(origin.X+10,corner.X-window.Width-12);
            window.Top=Math.Max(origin.Y+10,corner.Y-window.Height-12);
            window.Show(); window.WindowState=WindowState.Normal; window.Activate(); Refresh();
        }
        private void SetView(int mode) {
            viewMode=mode; Find<ListBox>("ActivityList").SelectedItem=null;
            Find<StackPanel>("CategoryEditor").Visibility=Visibility.Collapsed;
            Refresh();
        }
        private void Notice(string message) { Find<TextBlock>("Notice").Text=message; Find<TextBlock>("Notice").Visibility=Visibility.Visible; }
        private void TogglePause() { paused=!paused; previous=null; visits.Reset(); ResetResources(); elapsed.Restart(); UpdateStatus(); }
        private void UpdateStatus() {
            string status=paused?"Paused":locked?"Away":previous==null || previous.Idle?"Ready":"Tracking";
            Find<TextBlock>("TrackingStatus").Text="● "+status;
            Find<Button>("PauseButton").Content=paused?"Resume":"Pause";
            tray.Text="ScreenTime • "+status;
        }
        private void Tick() {
            if(stop.WaitOne(0)) { Quit(); return; }
            if(activate.WaitOne(0)) Show();
            double seconds=elapsed.Elapsed.TotalSeconds; elapsed.Restart();
            var current=paused || locked ? new Snapshot { Idle=true } : tracker.Sample(state);
            if(previous!=null && !previous.Idle && !current.Idle && !previous.PrivacyBlocked && !current.PrivacyBlocked && !paused && !locked)
                visits.Add(state,DateTime.Now,seconds,previous);
            else visits.Reset();
            previous=current;
            PollResources();
            UpdateStatus();
            if(DateTime.UtcNow-lastSave>TimeSpan.FromSeconds(15)) Save();
            if(window.IsVisible && ++refreshTick%5==0) Refresh();
        }
        private void Save() {
            try { store.Save(state); lastSave=DateTime.UtcNow; lastError=null; }
            catch(Exception ex) { if(lastError!=ex.Message) Notice("Activity could not be saved: "+ex.Message); lastError=ex.Message; lastSave=DateTime.UtcNow; }
        }
        private void ResetResources() { resourceGeneration++; resources.Reset(); latestResources=null; liveResources.Clear(); nextResourceSample=DateTime.MinValue; }
        private void PollResources() {
            if(resourceTask!=null && resourceTask.IsCompleted) {
                if(resourceTask.Status==TaskStatus.RanToCompletion && taskGeneration==resourceGeneration && !paused && !locked) {
                    var sample=resourceTask.Result;
                    liveResources=latestResources==null?new List<ResourceInterval>():ResourceMonitor.Compare(latestResources,sample);
                    latestResources=sample; resources.Accept(state,latestResources);
                }
                resourceTask=null;
            }
            if(!smokeMode && !paused && !locked && resourceTask==null && DateTime.UtcNow>=nextResourceSample) {
                taskGeneration=resourceGeneration; nextResourceSample=DateTime.UtcNow.AddSeconds(5);
                bool browserResources=state.CollectBrowserResources; var excluded=state.Excluded.ToArray();
                resourceTask=Task.Run(()=>ResourceMonitor.Capture(browserResources,excluded));
            }
        }
        private void SessionSwitch(object sender,SessionSwitchEventArgs e) {
            window.Dispatcher.BeginInvoke(new Action(()=> { locked=e.Reason!=SessionSwitchReason.SessionUnlock && e.Reason!=SessionSwitchReason.ConsoleConnect && e.Reason!=SessionSwitchReason.RemoteConnect; previous=null; visits.Reset(); ResetResources(); elapsed.Restart(); Save(); }));
        }
        private void PowerChanged(object sender,PowerModeChangedEventArgs e) {
            window.Dispatcher.BeginInvoke(new Action(()=> { previous=null; visits.Reset(); ResetResources(); elapsed.Restart(); if(e.Mode==PowerModes.Suspend) { locked=true; Save(); } else if(e.Mode==PowerModes.Resume) locked=false; }));
        }
        private void SessionEnding(object sender,SessionEndingEventArgs e) { window.Dispatcher.Invoke(new Action(Save)); }
        private void Quit() {
            exiting=true; timer.Stop(); Save();
            SystemEvents.SessionSwitch-=SessionSwitch; SystemEvents.PowerModeChanged-=PowerChanged; SystemEvents.SessionEnding-=SessionEnding;
            tray.Visible=false; var icon=tray.Icon; tray.Dispose(); icon.Dispose(); window.Close(); Application.Current.Shutdown();
        }
        private void ApplyCategory() {
            var row=Find<ListBox>("ActivityList").SelectedItem as ActivityView;
            if(row==null) { Notice("Select an activity row first, then choose its category."); return; }
            string category=(string)Find<ComboBox>("CategoryChoice").SelectedItem;
            string domain=viewMode==2?row.Source.Domain:"";
            string key=Logic.RuleKey(row.Source.App,domain); state.Rules[key]=category;
            foreach(var activity in state.Activities) activity.Category=Logic.Classify(state,activity.App,activity.Title,activity.Domain);
            Find<ListBox>("ActivityList").SelectedItem=null;
            Save(); Refresh();
            Notice("Category saved for "+(String.IsNullOrEmpty(domain)?Friendly(row.Source.App):domain)+".");
        }
        private TextBlock Label(string text) { return new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,7),Foreground=Brush("#BBBBBB"),FontSize=11 }; }
        private CheckBox Check(string text,bool selected) { return new CheckBox { Content=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap },IsChecked=selected,Margin=new Thickness(0,13,8,5) }; }
        private void Settings() {
            showingBackground=false; detailApp=null;
            var panel=new StackPanel { Margin=new Thickness(0,0,8,0) };
            panel.Children.Add(new TextBlock { Text="Settings",FontSize=22,FontWeight=FontWeights.SemiBold });
            panel.Children.Add(Label("History is encrypted for your Windows account and stays on this PC. Days, weeks and months are kept until you delete them."));
            var titles=Check("Record browser page titles and website names",state.CaptureTitles); panel.Children.Add(titles);
            var privateWindows=Check("Record incognito / private browser windows",state.IncludePrivateWindows); panel.Children.Add(privateWindows);
            panel.Children.Add(Label("Both are off by default. With private recording off, unverified browser windows are skipped too. Detection currently verifies supported English Chrome / Edge profile controls; other browsers may be skipped. Titles may contain personal information. Full URLs are never saved."));
            var browserResources=Check("Collect browser CPU, RAM and GPU (includes private processes)",state.CollectBrowserResources); panel.Children.Add(browserResources);
            panel.Children.Add(Label("Browser resource totals cannot separate private and normal windows. This separate option is off by default."));
            var startup=Check("Start in the tray when I sign in to Windows",state.StartupEnabled); panel.Children.Add(startup);
            panel.Children.Add(Label("Startup uses a stable installed copy in your Windows user folder."));
            panel.Children.Add(Label("Stop counting after this many seconds without mouse / keyboard input"));
            var idle=new TextBox { Text=state.IdleSeconds.ToString() }; panel.Children.Add(idle);
            panel.Children.Add(Label("Excluded app process names, separated by commas (for example: chrome, msedge)"));
            var exclusions=new TextBox { Text=String.Join(", ",state.Excluded) }; panel.Children.Add(exclusions);
            panel.Children.Add(Label("Data file: "+store.Path));
            var save=new Button { Content="Save preferences",Background=Brush("#4CC2FF"),Foreground=Brush("#10242E"),Margin=new Thickness(0,17,0,8) }; panel.Children.Add(save);
            var clear=new Button { Content="Delete recorded history",Foreground=Brush("#F1A0A0"),Margin=new Thickness(0,3,0,0) }; panel.Children.Add(clear);
            save.Click+=(s,e)=> {
                int seconds;
                if(!Int32.TryParse(idle.Text,out seconds)||seconds<30||seconds>3600) { Prompt("Choose an idle timeout between 30 and 3600 seconds.","Settings",MessageBoxButton.OK); return; }
                try { if(!smokeMode) StartupManager.Set(startup.IsChecked==true,true); } catch(Exception ex) { Prompt("Could not update Windows startup: "+ex.Message,"Settings",MessageBoxButton.OK); return; }
                state.StartupEnabled=startup.IsChecked==true; state.StartupVersion=1;
                state.IdleSeconds=seconds; state.CaptureTitles=titles.IsChecked==true;
                state.IncludePrivateWindows=privateWindows.IsChecked==true; state.CollectBrowserResources=browserResources.IsChecked==true; state.PrivacyVersion=1;
                state.Excluded=exclusions.Text.Split(',').Select(x=>x.Trim().Replace(".exe","")).Where(x=>x.Length>0).ToList();
                previous=null; tracker.Reset(); visits.Reset(); ResetResources(); Save(); CloseEditor();
            };
            clear.Click+=(s,e)=> {
                if(Prompt("Delete all activity, sessions, resource history and backups? Your preferences and goals will remain.","Delete history",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                state.Activities.Clear(); state.Resources.Clear(); state.Sessions.Clear(); previous=null; visits.Reset(); ResetResources();
                try {
                    store.Save(state);
                    // Replace the rolling backup as well so cleared activity cannot return on recovery.
                    store.Save(state);
                    foreach(var backup in Directory.GetFiles(Path.GetDirectoryName(store.Path),"activity.json.unreadable-*")) File.Delete(backup);
                    Refresh(); Prompt("Recorded history deleted.","ScreenTime",MessageBoxButton.OK);
                } catch(Exception ex) { Prompt("Could not fully delete saved history: "+ex.Message,"ScreenTime",MessageBoxButton.OK); }
            };
            OpenEditor(panel);
        }
        private void EditGoals() {
            showingBackground=false; detailApp=null;
            var panel=new StackPanel { Margin=new Thickness(0,0,8,0) };
            panel.Children.Add(new TextBlock { Text="A little every day",FontSize=22,FontWeight=FontWeights.SemiBold });
            panel.Children.Add(Label("Set a total effort estimate and the minutes you could spend each day. Dates are projections, not promises."));
            var fields=new List<TextBox[]>();
            foreach(var goal in state.Goals) {
                panel.Children.Add(Label("Goal")); var name=new TextBox { Text=goal.Name }; panel.Children.Add(name);
                var row=new StackPanel { Orientation=Orientation.Horizontal };
                var hours=new TextBox { Text=goal.Hours.ToString(),Width=95 }; var minutes=new TextBox { Text=goal.Minutes.ToString(),Width=95,Margin=new Thickness(16,0,0,0) };
                panel.Children.Add(Label("Total hours                         Minutes each day")); row.Children.Add(hours); row.Children.Add(minutes); panel.Children.Add(row);
                fields.Add(new[]{name,hours,minutes});
            }
            var save=new Button { Content="Update my possibilities",Background=Brush("#4CC2FF"),Foreground=Brush("#10242E"),Margin=new Thickness(0,22,0,0) }; panel.Children.Add(save);
            save.Click+=(s,e)=> {
                var goals=new List<Goal>();
                foreach(var fieldsForGoal in fields) {
                    double hours; int minutes;
                    if(String.IsNullOrWhiteSpace(fieldsForGoal[0].Text)||!Double.TryParse(fieldsForGoal[1].Text,out hours)||Double.IsNaN(hours)||Double.IsInfinity(hours)||hours<0.5||hours>10000||!Int32.TryParse(fieldsForGoal[2].Text,out minutes)||minutes<1||minutes>720) { Prompt("Enter a goal name, 0.5–10,000 hours, and 1–720 minutes per day.","Goal",MessageBoxButton.OK); return; }
                    goals.Add(new Goal { Name=fieldsForGoal[0].Text.Trim(),Hours=hours,Minutes=minutes });
                }
                state.Goals=goals; Save(); CloseEditor();
            }; OpenEditor(panel);
        }
    }
}
