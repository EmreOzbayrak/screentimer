using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenTime {
    // Short foreground sessions stay in memory until they qualify.
    public class VisitRecorder {
        public const double MinimumSeconds=10;
        private Snapshot visit;
        private double duration;
        private bool qualified;
        private readonly List<Tuple<DateTime,double>> pending=new List<Tuple<DateTime,double>>();

        public void Reset() { visit=null; duration=0; qualified=false; pending.Clear(); }
        private bool SameVisit(Snapshot sample,bool pageMode) {
            if(visit==null || visit.App!=sample.App) return false;
            if(!pageMode) return true;
            return visit.Handle==sample.Handle && visit.Title==sample.Title &&
                (String.IsNullOrEmpty(visit.Domain) || String.IsNullOrEmpty(sample.Domain) || visit.Domain==sample.Domain);
        }
        public void Add(State state,DateTime end,double seconds,Snapshot sample) {
            if(sample==null || sample.Idle || sample.PrivacyBlocked || (Logic.IsBrowser(sample.App) && !BrowserPrivacy.MayRecord(sample.BrowserMode,state.IncludePrivateWindows)) || seconds<=0 || seconds>10 || state.Excluded.Any(x=>String.Equals(x,sample.App,StringComparison.OrdinalIgnoreCase))) { Reset(); return; }
            bool pageMode=state.CaptureTitles && Logic.IsBrowser(sample.App);
            if(!SameVisit(sample,pageMode)) {
                Reset();
                visit=new Snapshot { App=sample.App,Title=pageMode?sample.Title:"",Domain=pageMode?sample.Domain:"",Handle=sample.Handle };
            }
            // Address-bar lookup is asynchronous: learning the hostname must not
            // restart the same page's threshold or create a second pending visit.
            if(pageMode && !String.IsNullOrEmpty(sample.Domain)) visit.Domain=sample.Domain;
            duration+=seconds;
            if(qualified) { Logic.Record(state,end,seconds,visit.App,visit.Title,visit.Domain); return; }
            pending.Add(Tuple.Create(end,seconds));
            if(duration<MinimumSeconds) return;
            qualified=true;
            foreach(var interval in pending) Logic.Record(state,interval.Item1,interval.Item2,visit.App,visit.Title,visit.Domain);
            pending.Clear();
        }
        public static int CleanLegacyHistory(State state) {
            if(state.BrowserVisitFilterVersion>=1) return 0;
            int removed=state.Activities.RemoveAll(a=>Logic.IsBrowser(a.App) && a.Seconds<MinimumSeconds && (!String.IsNullOrEmpty(a.Title) || !String.IsNullOrEmpty(a.Domain)));
            state.BrowserVisitFilterVersion=1;
            return removed;
        }
        public static int CleanLegacyApps(State state) {
            if(state.AppVisitFilterVersion>=1) return 0;
            var shortApps=state.Activities.GroupBy(a=>new { a.Day,a.App }).Where(g=>g.Sum(a=>a.Seconds)<MinimumSeconds).SelectMany(g=>g).ToList();
            int removed=state.Activities.RemoveAll(a=>shortApps.Contains(a));
            state.AppVisitFilterVersion=1;
            return removed;
        }
    }
}
