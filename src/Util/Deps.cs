using System;
using System.Collections.Generic;
using System.Linq;
#if GAME
using ColossalFramework.Plugins;
#endif

namespace CSM.TmpeSync.Util
{
    internal static class Deps
    {
#if GAME
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
        internal static string[] GetMissingDependencies(){
            var missing = new List<string>();
            if (!IsCsmEnabled()) missing.Add("CSM");
            if (!IsHarmonyAvailable()) missing.Add("Harmony");
            return missing.ToArray();
        }
        internal static void DisableSelf(object modInstance){
            try{
                foreach (var p in PluginManager.instance.GetPluginsInfo()){
                    if (p.userModInstance == modInstance){
                        Log.Warn("Disabling plugin '{0}' due to missing dependencies.", SafeName(p));
                        var pluginName = p.name;
                        if (!string.IsNullOrEmpty(pluginName)){
                            PluginManager.instance.SetPluginEnabled(pluginName, false);
                        }else{
                            p.isEnabled = false;
                        }
                        break;
                    }
                }
            }catch(Exception ex){ Log.Warn("Failed to disable plugin: {0}", ex); }
        }
        private static string SafeName(PluginInfo p){ try{ return p.name ?? p.modPath ?? ""; }catch{ return ""; } }
#else
        internal static bool IsCsmEnabled(){ return false; }
        internal static bool IsHarmonyAvailable(){ return false; }
        internal static string[] GetMissingDependencies(){ return new[]{"CSM","Harmony"}; }
        internal static void DisableSelf(object _){ }
        private static string SafeName(object _){ return string.Empty; }
#endif
    }
}
