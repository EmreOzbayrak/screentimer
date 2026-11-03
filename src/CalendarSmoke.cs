using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ScreenTime {
    public partial class Program {
        private void CalendarSmoke() {
            var original=state.Activities;
            try {
                state.Activities=new List<Activity>();
                for(int day=1;day<=31;day++) {
                    if(day%6==0) continue;
                    string key=new DateTime(2026,10,day).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                    state.Activities.Add(new Activity {Day=key,App="code",Category="Work",Seconds=(45+day*3)*60,Title="Project",Domain=""});
                    state.Activities.Add(new Activity {Day=key,App="chrome",Category="Fun",Seconds=(20+day%5*18)*60,Title="Videos",Domain="example.com"});
                }
                state.Activities.Add(new Activity {Day="2026-09-01",App="code",Category="Work",Seconds=72000});
                state.Activities.Add(new Activity {Day="2025-12-15",App="code",Category="Work",Seconds=5400});
                ShowCalendar(new DateTime(2026,10,1)); window.UpdateLayout(); RenderPreview("preview-calendar-overview.png");
                DateTime selected=new DateTime(2026,10,17);
                if(calendarDays.Count!=31 || calendarDays[selected].Time.Text!="2h 32m") throw new Exception("Calendar day squares did not show recorded hours.");
                var dayButton=calendarDays[selected].Button;
                state.Activities.First(a=>a.Day=="2026-10-17").Seconds+=60;
                Refresh();
                if(calendarDays[selected].Button!=dayButton || calendarDays[selected].Time.Text!="2h 33m") throw new Exception("Calendar refresh lost controls or did not update time.");
                dayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(showingCalendar || selectedDate!=selected || Find<ComboBox>("RangeBox").SelectedIndex!=0 || Find<TextBlock>("TotalTime").Text!="2h 33m" || Find<Button>("CalendarReturnButton").Visibility!=Visibility.Visible) throw new Exception("Calendar day click did not open the matching daily breakdown.");
                window.UpdateLayout(); RenderPreview("preview-calendar-day.png");
                Find<ListBox>("ActivityList").SelectedIndex=0;
                if(detailApp==null) throw new Exception("Calendar day did not allow app inspection.");
                CloseEditor();
                Find<Button>("CalendarReturnButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!showingCalendar || calendarMonth!=new DateTime(2026,10,1)) throw new Exception("Day inspection did not return to its calendar month.");
                calendarInspectMonth.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(Find<ComboBox>("RangeBox").SelectedIndex!=2 || selectedDate!=new DateTime(2026,10,1) || showingCalendar) throw new Exception("Month total did not open the monthly breakdown.");
                Find<Button>("CalendarReturnButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Find<ScrollViewer>("EditorScroll").ScrollToBottom(); window.UpdateLayout(); RenderPreview("preview-calendar-saved-months.png");
                calendarMonths[new DateTime(2026,9,1)].Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(calendarMonth!=new DateTime(2026,9,1) || calendarDays.Count!=30 || calendarTotal.Text!="20h 0m") throw new Exception("Saved-month navigation or monthly total failed.");
                calendarPrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(calendarMonth!=new DateTime(2026,8,1) || calendarDays.Values.Any(c=>c.Time.Text!="—")) throw new Exception("An empty month leaked another month's hours.");
                ChangeCalendarMonth(new DateTime(2026,11,1)); window.UpdateLayout(); RenderPreview("preview-calendar-empty.png");
                if(calendarGrid.Rows!=6 || calendarDays.Count!=30) throw new Exception("Six-week calendar layout failed.");
                Settings(); Refresh();
                if(showingCalendar || !((StackPanel)Find<ContentControl>("EditorContent").Content).Children.OfType<TextBox>().Any(t=>t.Name=="MinimumLogInput")) throw new Exception("Calendar refresh overwrote Settings.");
            } finally {
                state.Activities=original; showingCalendar=false; calendarReturnMonth=null;
                selectedDate=DateTime.Today; followToday=true; Find<ComboBox>("RangeBox").SelectedIndex=0; CloseEditor();
            }
        }
    }
}
