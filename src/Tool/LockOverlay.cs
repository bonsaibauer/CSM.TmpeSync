#if GAME
using UnityEngine;
using ColossalFramework;
#endif

using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tool
{
    internal static class LockOverlay
    {
        // call pro Frame aus deinem Tool-Kontext:
        internal static void OnRenderOverlay(
#if GAME
            RenderManager.CameraInfo cameraInfo
#else
            object cameraInfo
#endif
        )
        {
#if GAME
            // Beispiel: grob alle gelockten Segmente rot umranden (Pseudo: du brauchst die Keys -> parse TargetKind==1)
            // Hier nur Demonstration – in der Praxis speicherst du parallel eine Liste gelockter Segmente/Nodes.
            // RenderManager.instance.OverlayEffect.DrawSegment(cameraInfo, Color.red, segId, 0, 100f, false, true);
#endif
        }

        // einmal pro Frame → TTL runterzählen
        internal static void Tick(){ LockRegistry.Tick(); }

        // Beispiel-Check vor Eingabe:
        internal static bool IsLockedSegment(ushort segmentId){ return LockRegistry.IsLocked(1, segmentId); }
        internal static bool IsLockedNode(ushort nodeId){ return LockRegistry.IsLocked(2, nodeId); }
        internal static bool IsLockedLane(uint laneId){ return LockRegistry.IsLocked(3, laneId); }
    }
}
