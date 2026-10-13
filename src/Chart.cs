using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ScreenTime {
    public class ActivitySlice {
        public string Name;
        public string Category;
        public double Seconds;
        public Activity Source;
    }
    public static class Chart {
        // Keep a browser's different categories separate so every arc has its real color.
        public static List<ActivitySlice> Summarize(IEnumerable<Activity> rows,int mode) {
            return rows.Where(a=>a.Seconds>0).GroupBy(a=>new {
                Name=mode==0?a.Category:mode==1?a.App:(String.IsNullOrEmpty(a.Title)?(String.IsNullOrEmpty(a.Domain)?a.App:a.Domain):a.Title),
                Category=a.Category,
                App=mode==0?"":a.App,
                Domain=mode==2?a.Domain:""
            }).Select(g=>new ActivitySlice { Name=g.Key.Name,Category=g.Key.Category,Seconds=g.Sum(a=>a.Seconds),Source=g.First() })
              .OrderBy(s=>Array.IndexOf(Logic.Categories,s.Category)).ThenByDescending(s=>s.Seconds).ToList();
        }
        public static Geometry Arc(double startDegrees,double sweepDegrees) {
            if(sweepDegrees>=359.999) return new EllipseGeometry(new Point(90,90),76,76);
            double start=startDegrees*Math.PI/180, end=(startDegrees+sweepDegrees)*Math.PI/180;
            var figure=new PathFigure { StartPoint=new Point(90+76*Math.Cos(start),90+76*Math.Sin(start)),IsClosed=false };
            figure.Segments.Add(new ArcSegment(new Point(90+76*Math.Cos(end),90+76*Math.Sin(end)),new Size(76,76),0,sweepDegrees>180,SweepDirection.Clockwise,true));
            return new PathGeometry(new[]{figure});
        }
    }
}
