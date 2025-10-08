using System;
using System.Collections.Generic;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Util
{
    internal static class DeferredApply
    {
        private const int MAX_RETRIES=20, DELAY_FRAMES=8;
        private static readonly List<Entry> _queue=new List<Entry>();
        private static bool _running;

        internal static void Enqueue(IDeferredOp op){
            lock(_queue){
                for(int i=_queue.Count-1;i>=0;i--) if(_queue[i].Op.Key==op.Key){ _queue[i]=new Entry(op); Start(); return; }
                _queue.Add(new Entry(op)); Start();
            }
        }
        private static void Start(){ if(_running) return; _running=true; NetUtil.StartSimulationCoroutine(Worker()); }
        private static System.Collections.IEnumerator Worker(){
            int wait=0;
            while(true){
                if(wait>0){ wait--; yield return 0; continue; }
                Entry[] work; lock(_queue) work=_queue.ToArray();
                if(work.Length==0){ _running=false; yield break; }
                foreach(var e in work){
                    bool done=false, drop=false;
                    try{
                        if(e.Op.Exists()){
                            CSM.API.IgnoreHelper.Instance.StartIgnore();
                            try{ done=e.Op.TryApply(); } finally{ CSM.API.IgnoreHelper.Instance.EndIgnore(); }
                        }else{
                            e.Retries++; if(e.Retries>=MAX_RETRIES) drop=true;
                        }
                    }catch(Exception ex){ Log.Error("DeferredApply err {0}: {1}", e.Op.Key, ex); drop=true; }

                    if(done||drop){
                        lock(_queue){ for(int i=0;i<_queue.Count;i++) if(object.ReferenceEquals(_queue[i].Op,e.Op)){ _queue.RemoveAt(i); break; } }
                        if(done) Log.Info("DeferredApply applied {0} after {1} retries", e.Op.Key, e.Retries);
                        else Log.Warn("DeferredApply dropped {0} after {1} retries", e.Op.Key, e.Retries);
                    }
                }
                wait=DELAY_FRAMES; yield return 0;
            }
        }
        private struct Entry{ internal readonly IDeferredOp Op; internal int Retries; internal Entry(IDeferredOp op){ Op=op; Retries=0; } }
    }

    internal interface IDeferredOp { string Key { get; } bool Exists(); bool TryApply(); }
}
