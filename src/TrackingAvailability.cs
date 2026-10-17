using Microsoft.Win32;

namespace ScreenTime {
    // Lock, disconnection and sleep are independent; a resume must not unlock a PC.
    public class TrackingAvailability {
        private bool sessionLocked, disconnected, suspended;
        public bool Blocked { get { return sessionLocked || disconnected || suspended; } }
        public bool Apply(SessionSwitchReason reason) {
            bool before=Blocked;
            switch(reason) {
                case SessionSwitchReason.SessionLock: sessionLocked=true; break;
                case SessionSwitchReason.SessionUnlock: sessionLocked=false; break;
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                case SessionSwitchReason.SessionLogoff: disconnected=true; break;
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon: disconnected=false; break;
            }
            return before!=Blocked;
        }
        public bool Apply(PowerModes mode) {
            if(mode==PowerModes.StatusChange) return false;
            bool before=Blocked;
            suspended=mode==PowerModes.Suspend;
            return before!=Blocked;
        }
    }
}
