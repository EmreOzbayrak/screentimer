using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ScreenTime {
    public class CalendarDayTotal {
        public DateTime Date;
        public double Seconds;
        public Dictionary<string,double> Categories=new Dictionary<string,double>();
    }
    public class CalendarMonthTotal {
        public DateTime Month;
        public double Seconds;
        public int TrackedDays;
    }
    public class CalendarOverview {
        public DateTime Month;
        public int MondayOffset;
        public List<CalendarDayTotal> Days;
        public List<CalendarMonthTotal> History;
        public double Seconds { get { return Days.Sum(d=>d.Seconds); } }
        public int TrackedDays { get { return Days.Count(d=>d.Seconds>0); } }
        public static CalendarOverview Create(IEnumerable<Activity> activities,DateTime selected) {
            var byDate=new Dictionary<DateTime,CalendarDayTotal>();
            foreach(var activity in activities) {
                DateTime date;
                if(activity==null || Double.IsNaN(activity.Seconds) || Double.IsInfinity(activity.Seconds) || activity.Seconds<=0 ||
                    !DateTime.TryParseExact(activity.Day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) continue;
                CalendarDayTotal day;
                if(!byDate.TryGetValue(date,out day)) { day=new CalendarDayTotal { Date=date }; byDate.Add(date,day); }
                day.Seconds+=activity.Seconds;
                string category=Logic.Categories.Contains(activity.Category)?activity.Category:"Other";
                if(!day.Categories.ContainsKey(category)) day.Categories[category]=0;
                day.Categories[category]+=activity.Seconds;
            }
            var month=new DateTime(selected.Year,selected.Month,1);
            var result=new CalendarOverview { Month=month,MondayOffset=((int)month.DayOfWeek+6)%7,Days=new List<CalendarDayTotal>() };
            for(int number=1;number<=DateTime.DaysInMonth(month.Year,month.Month);number++) {
                DateTime date=new DateTime(month.Year,month.Month,number); CalendarDayTotal day;
                result.Days.Add(byDate.TryGetValue(date,out day)?day:new CalendarDayTotal { Date=date });
            }
            result.History=byDate.Values.GroupBy(d=>new DateTime(d.Date.Year,d.Date.Month,1))
                .Select(g=>new CalendarMonthTotal { Month=g.Key,Seconds=g.Sum(d=>d.Seconds),TrackedDays=g.Count() })
                .OrderByDescending(m=>m.Month).ToList();
            return result;
        }
    }
}
