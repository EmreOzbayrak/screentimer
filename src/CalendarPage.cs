using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ScreenTime
{
    public partial class Program
    {
        private bool showingCalendar;
        private DateTime calendarMonth = CalendarPeriod.Start(DateTime.Today, 2);
        private DateTime? calendarReturnMonth;
        private UniformGrid calendarGrid;
        private StackPanel calendarHistoryList;
        private TextBlock calendarMonthLabel,
            calendarTotal,
            calendarSummary,
            calendarEmpty;
        private Button calendarPrevious,
            calendarNext,
            calendarInspectMonth;

        private class DayCell
        {
            public Button Button;
            public TextBlock Number,
                Time;
            public Grid Categories;
        }

        private class MonthCell
        {
            public Button Button;
            public TextBlock Time;
        }

        private Dictionary<DateTime, DayCell> calendarDays = new Dictionary<DateTime, DayCell>();
        private Dictionary<DateTime, MonthCell> calendarMonths =
            new Dictionary<DateTime, MonthCell>();

        private void ShowCalendar(DateTime month)
        {
            calendarMonth = CalendarPeriod.Start(month, 2);
            showingBackground = false;
            detailApp = null;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
            var heading = new Grid();
            heading.Children.Add(Heading("Calendar"));
            var today = new Button
            {
                Content = "This month",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 10,
                Background = Brush("#293D47"),
                Foreground = Brush("#8DD6FF"),
            };
            today.Click += (s, e) => ChangeCalendarMonth(DateTime.Today);
            heading.Children.Add(today);
            panel.Children.Add(heading);
            var navigation = new Grid { Margin = new Thickness(0, 10, 0, 6) };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition());
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            calendarPrevious = new Button
            {
                Content = "‹",
                FontSize = 20,
                Padding = new Thickness(4),
                ToolTip = "Previous month",
            };
            calendarPrevious.Click += (s, e) =>
                ChangeCalendarMonth(CalendarPeriod.Move(calendarMonth, 2, -1));
            navigation.Children.Add(calendarPrevious);
            calendarMonthLabel = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(calendarMonthLabel, 1);
            navigation.Children.Add(calendarMonthLabel);
            calendarNext = new Button
            {
                Content = "›",
                FontSize = 20,
                Padding = new Thickness(4),
                ToolTip = "Next month",
            };
            calendarNext.Click += (s, e) =>
                ChangeCalendarMonth(CalendarPeriod.Move(calendarMonth, 2, 1));
            Grid.SetColumn(calendarNext, 2);
            navigation.Children.Add(calendarNext);
            panel.Children.Add(navigation);
            var totalPanel = new StackPanel();
            totalPanel.Children.Add(
                new TextBlock
                {
                    Text = "MONTH TOTAL",
                    FontSize = 10,
                    Foreground = Brush("#9BD9F7"),
                }
            );
            calendarTotal = new TextBlock
            {
                FontSize = 27,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 0, 3),
            };
            totalPanel.Children.Add(calendarTotal);
            calendarSummary = new TextBlock
            {
                FontSize = 11,
                Foreground = Brush("#BCBCBC"),
                TextWrapping = TextWrapping.Wrap,
            };
            totalPanel.Children.Add(calendarSummary);
            calendarInspectMonth = new Button
            {
                Content = totalPanel,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brush("#29383F"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 5, 0, 12),
                ToolTip = "Inspect this month's apps, websites and categories",
            };
            calendarInspectMonth.Click += (s, e) => InspectCalendar(calendarMonth, 2);
            panel.Children.Add(calendarInspectMonth);
            var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 5) };
            foreach (string day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" })
                weekdays.Children.Add(
                    new TextBlock
                    {
                        Text = day,
                        FontSize = 10,
                        Foreground = Brush("#969FA5"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    }
                );
            panel.Children.Add(weekdays);
            calendarGrid = new UniformGrid { Columns = 7 };
            panel.Children.Add(calendarGrid);
            panel.Children.Add(
                Label(
                    "Click a day to inspect it. The blue outline marks today; — means no recorded activity."
                )
            );
            var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            foreach (string category in Logic.Categories)
                legend.Children.Add(
                    new TextBlock
                    {
                        Text = "● " + category,
                        FontSize = 10,
                        Foreground = Brush(CategoryColor(category)),
                        Margin = new Thickness(0, 0, 10, 5),
                    }
                );
            panel.Children.Add(legend);
            panel.Children.Add(
                new TextBlock
                {
                    Text = "Saved months",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 9, 0, 3),
                }
            );
            panel.Children.Add(Label("Total active time for every month with recorded activity."));
            calendarEmpty = Label("No saved months yet. Your calendar fills as you use apps.");
            panel.Children.Add(calendarEmpty);
            calendarHistoryList = new StackPanel();
            panel.Children.Add(calendarHistoryList);
            calendarDays.Clear();
            calendarMonths.Clear();
            OpenEditor(panel);
            showingCalendar = true;
            RefreshCalendar();
        }

        private void ChangeCalendarMonth(DateTime date)
        {
            calendarMonth = CalendarPeriod.Start(date, 2);
            calendarDays.Clear();
            RefreshCalendar();
            Find<ScrollViewer>("EditorScroll").ScrollToTop();
        }

        private void InspectCalendar(DateTime date, int mode)
        {
            calendarReturnMonth = calendarMonth;
            showingCalendar = false;
            showingBackground = false;
            detailApp = null;
            selectedDate = date.Date;
            followToday = mode == 0 && selectedDate == DateTime.Today;
            Find<ComboBox>("RangeBox").SelectedIndex = mode;
            viewMode = 1;
            CloseEditor();
            Find<ScrollViewer>("MainScroll").ScrollToTop();
        }

        private void RefreshCalendar()
        {
            var overview = CalendarOverview.Create(state.Activities, calendarMonth);
            calendarMonthLabel.Text = calendarMonth.ToString("MMMM yyyy");
            Find<TextBlock>("DateLabel").Text = calendarMonthLabel.Text;
            calendarTotal.Text = Logic.Duration(overview.Seconds);
            calendarSummary.Text =
                overview.TrackedDays == 0
                    ? "No recorded activity this month"
                    : "Active time · "
                        + overview.TrackedDays
                        + " tracked "
                        + (overview.TrackedDays == 1 ? "day" : "days")
                        + " · inspect  ›";
            calendarPrevious.IsEnabled = calendarMonth > DateTime.MinValue;
            calendarNext.IsEnabled = calendarMonth < new DateTime(9999, 12, 1);
            AutomationProperties.SetName(
                calendarInspectMonth,
                calendarMonthLabel.Text + ", " + calendarTotal.Text + ", inspect month"
            );
            if (
                calendarDays.Count != overview.Days.Count
                || !calendarDays.ContainsKey(calendarMonth)
            )
            {
                calendarDays.Clear();
                calendarGrid.Children.Clear();
                int rows = (overview.MondayOffset + overview.Days.Count + 6) / 7;
                calendarGrid.Rows = rows;
                for (int index = 0; index < rows * 7; index++)
                {
                    int number = index - overview.MondayOffset;
                    if (number < 0 || number >= overview.Days.Count)
                    {
                        calendarGrid.Children.Add(new Border { Height = 52 });
                        continue;
                    }
                    DateTime date = overview.Days[number].Date;
                    var content = new Grid();
                    content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    content.RowDefinitions.Add(new RowDefinition());
                    content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
                    var cell = new DayCell
                    {
                        Number = new TextBlock
                        {
                            Text = date.Day.ToString(),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                        },
                        Time = new TextBlock
                        {
                            FontSize = 9,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                        Categories = new Grid(),
                    };
                    content.Children.Add(cell.Number);
                    Grid.SetRow(cell.Time, 1);
                    content.Children.Add(cell.Time);
                    Grid.SetRow(cell.Categories, 2);
                    content.Children.Add(cell.Categories);
                    for (int i = 0; i < Logic.Categories.Length; i++)
                    {
                        cell.Categories.ColumnDefinitions.Add(new ColumnDefinition());
                        var stripe = new Border
                        {
                            Background = Brush(CategoryColor(Logic.Categories[i])),
                        };
                        Grid.SetColumn(stripe, i);
                        cell.Categories.Children.Add(stripe);
                    }
                    cell.Button = new Button
                    {
                        Name =
                            "CalendarDay_"
                            + date.ToString(
                                "yyyyMMdd",
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                        Tag = date,
                        Content = content,
                        Height = 52,
                        Margin = new Thickness(2),
                        Padding = new Thickness(4),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        BorderThickness = new Thickness(1),
                    };
                    cell.Button.Click += (s, e) => InspectCalendar(date, 0);
                    calendarDays.Add(date, cell);
                    calendarGrid.Children.Add(cell.Button);
                }
            }
            foreach (var day in overview.Days)
            {
                var cell = calendarDays[day.Date];
                bool today = day.Date == DateTime.Today;
                cell.Button.Background = Brush(day.Seconds > 0 ? "#334047" : "#303030");
                cell.Button.BorderBrush = Brush(today ? "#4CC2FF" : "#424242");
                cell.Number.Foreground = Brush(
                    today ? "#8DD6FF"
                    : day.Date > DateTime.Today ? "#8F969B"
                    : "#E7E7E7"
                );
                cell.Time.Text = day.Seconds > 0 ? Logic.Duration(day.Seconds) : "—";
                cell.Time.Foreground = Brush(day.Seconds > 0 ? "#EEEEEE" : "#777F84");
                cell.Categories.Visibility =
                    day.Seconds > 0 ? Visibility.Visible : Visibility.Hidden;
                for (int i = 0; i < Logic.Categories.Length; i++)
                {
                    double seconds;
                    day.Categories.TryGetValue(Logic.Categories[i], out seconds);
                    cell.Categories.ColumnDefinitions[i].Width = new GridLength(
                        seconds,
                        GridUnitType.Star
                    );
                }
                string description =
                    day.Date.ToString("D")
                    + (today ? " · Today" : "")
                    + "\n"
                    + (
                        day.Seconds > 0
                            ? Logic.Duration(day.Seconds) + " active time"
                            : "No recorded activity"
                    );
                cell.Button.ToolTip =
                    description
                    + String.Concat(
                        Logic
                            .Categories.Where(day.Categories.ContainsKey)
                            .Select(c => "\n" + c + ": " + Logic.Duration(day.Categories[c]))
                    );
                AutomationProperties.SetName(
                    cell.Button,
                    description.Replace('\n', ',') + ", inspect day"
                );
            }
            if (
                !overview
                    .History.Select(m => m.Month)
                    .SequenceEqual(calendarMonths.Keys.OrderByDescending(m => m))
            )
            {
                calendarMonths.Clear();
                calendarHistoryList.Children.Clear();
                foreach (var month in overview.History)
                {
                    DateTime date = month.Month;
                    var line = Metric(date.ToString("MMMM yyyy"), "");
                    var cell = new MonthCell
                    {
                        Time = (TextBlock)line.Children[1],
                        Button = new Button
                        {
                            Tag = date,
                            Content = line,
                            Padding = new Thickness(10, 6, 10, 6),
                            Margin = new Thickness(0, 3, 0, 3),
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        },
                    };
                    cell.Button.Click += (s, e) => ChangeCalendarMonth(date);
                    calendarMonths.Add(date, cell);
                    calendarHistoryList.Children.Add(cell.Button);
                }
            }
            foreach (var month in overview.History)
            {
                var cell = calendarMonths[month.Month];
                cell.Time.Text = Logic.Duration(month.Seconds);
                cell.Button.Background = Brush(
                    month.Month == calendarMonth ? "#293D47" : "#333333"
                );
                AutomationProperties.SetName(
                    cell.Button,
                    month.Month.ToString("MMMM yyyy") + ", " + cell.Time.Text + ", open calendar"
                );
            }
            calendarEmpty.Visibility =
                overview.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
