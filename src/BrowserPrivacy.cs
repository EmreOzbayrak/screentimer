using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

namespace ScreenTime {
    public enum BrowserMode { Unknown, Normal, Private }
    public static class BrowserPrivacy {
        // A missing private marker never counts as proof of a normal window.
        public static BrowserMode Classify(string app,IEnumerable<string> toolbarLabels) {
            string[] labels=toolbarLabels.Where(s=>s!=null).Select(s=>s.Trim().ToLowerInvariant()).ToArray();
            if(labels.Any(s=>s.Contains("incognito") || s.Contains("inprivate") || s.Contains("private browsing") || s=="guest" || s.StartsWith("guest "))) return BrowserMode.Private;
            if(app!="chrome" && app!="msedge") return BrowserMode.Unknown;
            if(labels.Any(s=>s=="profile" || s=="profiles" || s.StartsWith("profile:") || s.StartsWith("profile ") || s.StartsWith("your profile"))) return BrowserMode.Normal;
            return BrowserMode.Unknown;
        }
        public static bool MayRecord(BrowserMode mode,bool includePrivate) { return includePrivate || mode==BrowserMode.Normal; }
        public static List<AutomationElement> NativeControls(IntPtr handle) {
            var found=new List<AutomationElement>();
            var root=AutomationElement.FromHandle(handle);
            Walk(root,found,0);
            return found;
        }
        private static void Walk(AutomationElement node,List<AutomationElement> found,int depth) {
            if(depth>12 || found.Count>=300) return;
            var kind=node.Current.ControlType;
            // Never descend into page content (where a website could spoof a profile button).
            if(kind==ControlType.Document) return;
            found.Add(node);
            var walker=TreeWalker.ControlViewWalker;
            for(var child=walker.GetFirstChild(node);child!=null && found.Count<300;child=walker.GetNextSibling(child)) Walk(child,found,depth+1);
        }
        public static BrowserMode Inspect(string app,List<AutomationElement> controls) {
            var labels=new List<string>();
            foreach(var control in controls) {
                if(control.Current.ControlType!=ControlType.Button) continue;
                // Browser toolbars, not menu items or tabs containing website text.
                bool inToolbar=false; var parent=TreeWalker.ControlViewWalker.GetParent(control);
                for(int i=0;i<5 && parent!=null;i++,parent=TreeWalker.ControlViewWalker.GetParent(parent)) {
                    if(parent.Current.ControlType==ControlType.Document) break;
                    if(parent.Current.ControlType==ControlType.ToolBar) { inToolbar=true; break; }
                }
                if(!inToolbar) continue;
                labels.Add(control.Current.Name); labels.Add(control.Current.HelpText);
            }
            return Classify(app,labels);
        }
    }
}
