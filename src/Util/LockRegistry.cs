using System.Collections.Generic;

namespace CSM.TmpeSync.Util
{
    internal static class LockRegistry
    {
        internal class LockInfo { public int Owner; public int Ttl; }
        private static readonly Dictionary<string,LockInfo> _locks=new Dictionary<string,LockInfo>();
        internal static string Key(byte k, uint id)=>k+":"+id;

        internal static void Apply(byte k,uint id,int owner,int ttl){ _locks[Key(k,id)]=new LockInfo{Owner=owner,Ttl=ttl}; }
        internal static void Clear(byte k,uint id){ _locks.Remove(Key(k,id)); }
        internal static bool IsLocked(byte k,uint id){ LockInfo li; return _locks.TryGetValue(Key(k,id), out li) && li.Ttl>0; }
        internal static void Tick(){
            var rm=new List<string>(_locks.Keys);
            foreach(var key in rm){ var li=_locks[key]; li.Ttl--; if(li.Ttl<=0) _locks.Remove(key); else _locks[key]=li; }
        }
    }
}
