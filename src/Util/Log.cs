using System;
namespace CSM.TmpeSync.Util
{
    internal static class Log
    {
        private const string P = "[CSM.TmpeSync] ";
        internal static void Info(string m, params object[] a){ try{ UnityEngine.Debug.Log(P+string.Format(m,a)); }catch{ Console.WriteLine(P+string.Format(m,a)); } }
        internal static void Warn(string m, params object[] a){ try{ UnityEngine.Debug.LogWarning(P+string.Format(m,a)); }catch{ Console.WriteLine("WARN "+P+string.Format(m,a)); } }
        internal static void Error(string m, params object[] a){ try{ UnityEngine.Debug.LogError(P+string.Format(m,a)); }catch{ Console.WriteLine("ERR  "+P+string.Format(m,a)); } }
    }
}
