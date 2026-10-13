using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTime {
    public partial class Program {
        private static string Friendly(string app) {
            switch((app??"").ToLowerInvariant()) {
                case "chrome": return "Google Chrome";
                case "msedge": return "Microsoft Edge";
                case "code": return "Visual Studio Code";
                case "firefox": return "Firefox";
                case "explorer": return "File Explorer";
                case "winword": return "Word";
                case "powerpnt": return "PowerPoint";
                default: return app;
            }
        }
        private string CategoryColor(string category) { return colors.ContainsKey(category)?colors[category]:colors["Other"]; }
        private void MovePeriod(int direction) {
            selectedDate=CalendarPeriod.Move(selectedDate,Find<ComboBox>("RangeBox").SelectedIndex,direction);
            followToday=selectedDate==DateTime.Today; Refresh();
        }
        private void Refresh() {
            int range=Find<ComboBox>("RangeBox").SelectedIndex;
            if(followToday) selectedDate=DateTime.Today;
            if(Find<DatePicker>("HistoryDate").SelectedDate!=selectedDate) Find<DatePicker>("HistoryDate").SelectedDate=selectedDate;
            var rows=state.Activities.Where(a=>InRange(a.Day)).ToList();
            double total=rows.Sum(a=>a.Seconds);
            Find<TextBlock>("DateLabel").Text=RangeName();
            Find<TextBlock>("TotalTime").Text=Logic.Duration(total);
            Find<TextBlock>("ChartCaption").Text=range==0?"day's active time":range==1?"week's active time":"month's active time";
            var legend=Find<StackPanel>("CategoryLegend"); legend.Children.Clear();
            foreach(string category in Logic.Categories) {
                double amount=rows.Where(a=>a.Category==category).Sum(a=>a.Seconds);
                var item=new Grid { Margin=new Thickness(0,6,0,6),ToolTip=category+": "+Logic.Duration(amount)+(total>0?" · "+Math.Round(100*amount/total)+"%":"") };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(13) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
                item.Children.Add(new Border { Width=7,Height=7,CornerRadius=new CornerRadius(4),Background=Brush(CategoryColor(category)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center });
                var label=new TextBlock { Text=category,FontSize=11,Foreground=Brush("#C3C3C3"),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(label,1); item.Children.Add(label);
                var time=new TextBlock { Text=Logic.Duration(amount),FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,0,0,0),VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(time,2); item.Children.Add(time);
                legend.Children.Add(item);
            }
            string[] tabs={"CategoriesButton","AppsButton","PagesButton"};
            for(int i=0;i<tabs.Length;i++) {
                Find<Button>(tabs[i]).Background=Brush(i==viewMode?"#414141":"#202020");
                Find<Button>(tabs[i]).Foreground=Brush(i==viewMode?"#74D0FF":"#B5B5B5");
            }
            var slices=Chart.Summarize(rows,viewMode);
            DrawDonut(slices,total);
            var list=Find<ListBox>("ActivityList");
            var selected=list.SelectedItem as ActivityView;
            var views=slices.OrderByDescending(s=>s.Seconds).Select(s=>new ActivityView {
                Name=viewMode==1?Friendly(s.Name):s.Name,
                Category=s.Category,Seconds=s.Seconds,Time=Logic.Duration(s.Seconds),Color=CategoryColor(s.Category),Source=s.Source,
                Detail=viewMode==0?(total>0?Math.Round(100*s.Seconds/total)+"% of active time":"No activity"):
                    viewMode==1?s.Category:s.Category+" · "+Friendly(s.Source.App),
                Tooltip=(viewMode==1?Friendly(s.Name):s.Name)+"\n"+s.Category+" · "+Logic.Duration(s.Seconds)+(String.IsNullOrEmpty(s.Source.Domain)?"":"\n"+s.Source.Domain)
            }).ToList();
            if(viewMode==1) views=rows.GroupBy(a=>a.App).Select(g=> {
                var dominant=g.GroupBy(a=>a.Category).OrderByDescending(c=>c.Sum(a=>a.Seconds)).First().Key;
                return new ActivityView { Name=Friendly(g.Key),Category=dominant,Source=g.First(),Seconds=g.Sum(a=>a.Seconds),Time=Logic.Duration(g.Sum(a=>a.Seconds)),Color=CategoryColor(dominant),Detail="View activity & resources  ›",Tooltip="Click to see "+Friendly(g.Key)+" details" };
            }).OrderByDescending(v=>v.Seconds).ToList();
            refreshing=true;
            list.ItemsSource=views;
            if(selected!=null) list.SelectedItem=views.FirstOrDefault(v=>v.Name==selected.Name && v.Category==selected.Category && v.Source.App==selected.Source.App && (viewMode!=2 || v.Source.Domain==selected.Source.Domain));
            refreshing=false;
            if(list.SelectedItem==null) Find<StackPanel>("CategoryEditor").Visibility=Visibility.Collapsed;
            Find<TextBlock>("EmptyLabel").Visibility=views.Count==0?Visibility.Visible:Visibility.Collapsed;
            Find<TextBlock>("CurrentLabel").Text=paused?"Only on this PC":previous!=null && previous.PrivacyBlocked?"Browser skipped · privacy setting":previous!=null && !previous.Idle?Friendly(previous.App):"Only on this PC";
            Find<TextBlock>("EmptyLabel").Text="No activity in this period.\nUse the arrows or calendar to view saved history.";
            if(state.Goals.Count>0) {
                var goal=state.Goals[goalIndex%state.Goals.Count];
                Find<TextBlock>("GoalTitle").Text=goal.Name;
                Find<TextBlock>("GoalProjection").Text=goal.Minutes+" min / day  →  "+Logic.Project(DateTime.Today,goal.Hours,goal.Minutes).ToString("d MMM yyyy");
                Find<TextBlock>("GoalEstimate").Text=goal.Hours+"h estimate · starting today";
            } else {
                Find<TextBlock>("GoalTitle").Text="Make time for something you love";
                Find<TextBlock>("GoalProjection").Text="Add a goal to see a completion estimate.";
                Find<TextBlock>("GoalEstimate").Text="";
            }
            if(showingBackground && detailApp==null) ShowBackground(true);
        }
        private void DrawDonut(List<ActivitySlice> slices,double total) {
            var canvas=Find<Canvas>("Donut"); canvas.Children.Clear();
            canvas.Children.Add(new System.Windows.Shapes.Path { Data=Chart.Arc(-90,360),Stroke=Brush("#3D3D3D"),StrokeThickness=20 });
            double angle=-90;
            foreach(var slice in slices) {
                double sweep=360*slice.Seconds/total;
                double gap=slices.Count>1?Math.Min(2.5,sweep*0.12):0;
                var segment=new System.Windows.Shapes.Path {
                    Data=Chart.Arc(angle+gap/2,sweep-gap),Stroke=Brush(CategoryColor(slice.Category)),StrokeThickness=20,
                    ToolTip=(viewMode==1?Friendly(slice.Name):slice.Name)+"\n"+slice.Category+" · "+Logic.Duration(slice.Seconds)+" · "+Math.Round(100*slice.Seconds/total)+"%",
                    Cursor=System.Windows.Input.Cursors.Hand
                };
                var captured=slice;
                segment.MouseEnter+=(s,e)=> {
                    Find<TextBlock>("TotalTime").Text=Logic.Duration(captured.Seconds);
                    Find<TextBlock>("ChartCaption").Text=viewMode==1?Friendly(captured.Name):captured.Name;
                    segment.StrokeThickness=24;
                };
                segment.MouseLeave+=(s,e)=> {
                    Find<TextBlock>("TotalTime").Text=Logic.Duration(total);
                    Find<TextBlock>("ChartCaption").Text="total active time"; segment.StrokeThickness=20;
                };
                segment.MouseLeftButtonUp+=(s,e)=> {
                    var list=Find<ListBox>("ActivityList");
                    var row=list.Items.Cast<ActivityView>().FirstOrDefault(v=>viewMode==1?v.Source.App==captured.Source.App:v.Source==captured.Source && v.Category==captured.Category);
                    if(row!=null) { list.SelectedItem=row; list.ScrollIntoView(row); }
                };
                canvas.Children.Add(segment); angle+=sweep;
            }
        }
        private void OpenEditor(StackPanel panel) {
            Find<ContentControl>("EditorContent").Content=panel;
            Find<Grid>("EditorPanel").Visibility=Visibility.Visible;
            Find<ScrollViewer>("MainScroll").Visibility=Visibility.Collapsed;
        }
        private void CloseEditor() {
            if(detailApp!=null && showingBackground) { detailApp=null; ShowBackground(); return; }
            detailApp=null; showingBackground=false;
            Find<ListBox>("ActivityList").SelectedItem=null;
            Find<Grid>("EditorPanel").Visibility=Visibility.Collapsed;
            Find<ContentControl>("EditorContent").Content=null;
            Find<ScrollViewer>("MainScroll").Visibility=Visibility.Visible;
            Refresh();
        }
        private MessageBoxResult Prompt(string text,string title,MessageBoxButton buttons) {
            keepOpen=true;
            try { return MessageBox.Show(window,text,title,buttons); }
            finally { keepOpen=false; }
        }
    }
}
