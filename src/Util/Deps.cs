using System;
using System.Linq;
using ColossalFramework.Plugins;

namespace CSM.TmpeSync.Util
{
    internal static class Deps
    {
        internal static bool IsCsmEnabled(){
            try{
                foreach (var p in PluginManager.instance.GetPluginsInfo()){
                    if (!p.isEnabled) continue;
                    var name = SafeName(p);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase)>=0) return true;
                    var inst = p.userModInstance;
                    if (inst!=null){
                        var asm = inst.GetType().Assembly.FullName ?? "";
                        var ns = inst.GetType().Namespace ?? "";
                        if (ns.StartsWith("CSM", StringComparison.OrdinalIgnoreCase) || asm.StartsWith("CSM", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }catch(Exception ex){ Log.Warn("CSM dep check: {0}", ex); }
            return false;
        }
        internal static bool IsHarmonyAvailable(){
            try{
                var t = Type.GetType("CitiesHarmony.API.HarmonyHelper, CitiesHarmony.API");
                if (t!=null){
                    var mi = t.GetMethod("IsHarmonyInstalled", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
                    if (mi!=null && (bool)mi.Invoke(null,null)) return true;
                }
                var any = AppDomain.CurrentDomain.GetAssemblies().Any(a=>{
                    var n=a.GetName().Name; return n=="0Harmony"||n=="HarmonyLib";
                });
                if (any) return true;
                foreach (var p in PluginManager.instance.GetPluginsInfo()){
                    if (!p.isEnabled) continue;
                    var name = SafeName(p);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("harmony", StringComparison.OrdinalIgnoreCase)>=0) return true;
                }
            }catch(Exception ex){ Log.Warn("Harmony dep check: {0}", ex); }
            return false;
        }
        private static string SafeName(PluginInfo p){ try{ return p.name ?? p.modPath ?? ""; }catch{ return ""; } }
    }
}
