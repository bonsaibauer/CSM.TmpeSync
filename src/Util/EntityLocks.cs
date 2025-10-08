using System;
using System.Collections.Generic;

namespace CSM.TmpeSync.Util
{
    internal static class EntityLocks
    {
        private static readonly Dictionary<uint,object> LaneLocks=new Dictionary<uint,object>();
        private static readonly object Guard=new object();

        internal struct Releaser: IDisposable{
            private readonly object _o; internal Releaser(object o){ _o=o; System.Threading.Monitor.Enter(_o); }
            public void Dispose(){ System.Threading.Monitor.Exit(_o); }
        }
        private static object GetLaneLock(uint laneId){
            lock(Guard){ object o; if(!LaneLocks.TryGetValue(laneId,out o)){ o=new object(); LaneLocks[laneId]=o; } return o; }
        }
        internal static Releaser AcquireLane(uint laneId){ return new Releaser(GetLaneLock(laneId)); }
    }
}
