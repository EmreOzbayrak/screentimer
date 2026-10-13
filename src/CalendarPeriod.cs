using System;
using System.Globalization;

namespace ScreenTime {
    public static class CalendarPeriod {
        public static DateTime Start(DateTime selected,int mode) {
            var date=selected.Date;
            if(mode==1) return date.AddDays(-((int)date.DayOfWeek+6)%7);
            if(mode==2) return new DateTime(date.Year,date.Month,1);
            return date;
        }
        public static DateTime End(DateTime selected,int mode) {
            var start=Start(selected,mode);
            return mode==2?start.AddMonths(1):start.AddDays(mode==1?7:1);
        }
        public static DateTime Move(DateTime selected,int mode,int direction) {
            return mode==2?selected.AddMonths(direction):selected.AddDays((mode==1?7:1)*direction);
        }
        public static bool Contains(string day,DateTime selected,int mode) {
            DateTime date;
            return DateTime.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date) && date>=Start(selected,mode) && date<End(selected,mode);
        }
        public static string Label(DateTime selected,int mode) {
            var start=Start(selected,mode);
            if(mode==2) return start.ToString("MMMM yyyy");
            if(mode==1) return start.ToString("d MMM")+" – "+End(selected,mode).AddDays(-1).ToString("d MMM yyyy");
            return selected.ToString("ddd, d MMM yyyy");
        }
    }
}
