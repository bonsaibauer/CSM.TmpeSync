using UnityEngine;
using ColossalFramework;

using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tool
{
    internal static class LockOverlay
    {
        // Called once per frame from the tool context.
        internal static void OnRenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            // Example: draw a red outline around locked segments.
            // RenderManager.instance.OverlayEffect.DrawSegment(cameraInfo, Color.red, segmentId, 0, 100f, false, true);
        }

        // Tick once per frame to update the lock TTL.
        internal static void Tick()
        {
            LockRegistry.Tick();
        }

        // Example guards before applying tool input.
        internal static bool IsLockedSegment(ushort segmentId) => LockRegistry.IsLocked(1, segmentId);

        internal static bool IsLockedNode(ushort nodeId) => LockRegistry.IsLocked(2, nodeId);

        internal static bool IsLockedLane(uint laneId) => LockRegistry.IsLocked(3, laneId);
    }
}
