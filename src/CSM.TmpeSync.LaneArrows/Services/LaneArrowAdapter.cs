using System;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;
using ExtLaneArrows = TrafficManager.API.Traffic.Enums.LaneArrows;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowAdapter
    {
        internal static bool TryGetLaneArrows(uint laneId, out int arrows)
        {
            arrows = 0;
            try
            {
                var mgr = Implementations.ManagerFactory?.LaneArrowManager;
                if (mgr == null)
                    return false;

                var ext = mgr.GetFinalLaneArrows(laneId);
                arrows = MapToOur(ext);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneArrows TryGet failed | laneId={0} error={1}", laneId, ex);
                return false;
            }
        }

        internal static bool ApplyLaneArrows(uint laneId, int arrows)
        {
            try
            {
                var mgr = Implementations.ManagerFactory?.LaneArrowManager;
                if (mgr == null)
                    return false;

                var mgrType = mgr.GetType();
                var method = mgrType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "SetLaneArrows" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(uint));

                if (method == null)
                    return false;

                var paramTypes = method.GetParameters();
                var enumType = paramTypes[1].ParameterType;
                var value = Enum.ToObject(enumType, MapToExt((LaneArrowFlags)arrows));

                object result;
                if (paramTypes.Length == 3)
                    result = method.Invoke(mgr, new object[] { laneId, value, true });
                else
                    result = method.Invoke(mgr, new object[] { laneId, value });

                if (method.ReturnType == typeof(bool))
                    return result is bool b && b;

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneArrows Apply failed | laneId={0} arrows={1} error={2}", laneId, arrows, ex);
                return false;
            }
        }

        private static int MapToExt(LaneArrowFlags arrows)
        {
            int v = 0;
            if ((arrows & LaneArrowFlags.Left) != 0) v |= (int)ExtLaneArrows.Left;
            if ((arrows & LaneArrowFlags.Forward) != 0) v |= (int)ExtLaneArrows.Forward;
            if ((arrows & LaneArrowFlags.Right) != 0) v |= (int)ExtLaneArrows.Right;
            return v;
        }

        private static int MapToOur(ExtLaneArrows ext)
        {
            int v = 0;
            if ((ext & ExtLaneArrows.Left) != 0) v |= (int)LaneArrowFlags.Left;
            if ((ext & ExtLaneArrows.Forward) != 0) v |= (int)LaneArrowFlags.Forward;
            if ((ext & ExtLaneArrows.Right) != 0) v |= (int)LaneArrowFlags.Right;
            return v;
        }
    }
}
