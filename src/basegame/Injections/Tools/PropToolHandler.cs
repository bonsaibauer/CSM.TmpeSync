using System;
using ColossalFramework;
using ColossalFramework.Math;
using CSM.API;
using CSM.API.Commands;
using CSM.API.Helpers;
using HarmonyLib;
using ProtoBuf;
using UnityEngine;

namespace CSM.BaseGame.Injections.Tools
{
    [HarmonyPatch(typeof(PropTool))]
    [HarmonyPatch("OnToolLateUpdate")]
    public class PropToolHandler {

        private static PlayerPropToolCommand _lastCommand;

        public static void Postfix(PropTool __instance, ToolController ___m_toolController, PropInfo ___m_propInfo, Vector3 ___m_cachedPosition, float ___m_cachedAngle, Randomizer ___m_randomizer)
        {
            if (Command.CurrentRole != MultiplayerRole.None) {

                if (___m_toolController != null && ___m_toolController.IsInsideUI) {
                    return;
                }

                // Send info to all clients
                PlayerPropToolCommand newCommand = new PlayerPropToolCommand
                {                    
                    Prop = (uint)  ___m_propInfo.m_prefabDataIndex,
                    Mode = (int) __instance.m_mode,
                    Position = ___m_cachedPosition,
                    Angle = ___m_cachedAngle,
                    RandomizerSeed = ___m_randomizer.seed,
                    BrushSize = __instance.m_brushSize,
                    CursorWorldPosition = ___m_cachedPosition,
                    PlayerName = Chat.Instance.GetCurrentUsername()
                };
                if (!newCommand.Equals(_lastCommand)) {
                    _lastCommand = newCommand;
                    Command.SendToAll(newCommand);
                }
            }
        }    
    }
    
    [ProtoContract]
    public class PlayerPropToolCommand : ToolCommandBase, IEquatable<PlayerPropToolCommand>
    {
        [ProtoMember(1)]
        public uint Prop { get; set; }
        [ProtoMember(2)]
        public int Mode { get; set; }
        [ProtoMember(3)]
        public Vector3 Position { get; set; }
        [ProtoMember(4)]
        public float Angle { get; set; }
        [ProtoMember(5)]
        public ulong RandomizerSeed { get; set; }
        [ProtoMember(6)]
        public float BrushSize { get; set; }
        // TODO: Transmit placement errors

        public bool Equals(PlayerPropToolCommand other)
        {
            return base.Equals(other) &&
                   Equals(this.Prop, other.Prop) &&
                   Equals(this.Mode, other.Mode) &&
                   Equals(this.Position, other.Position) &&
                   Equals(this.Angle, other.Angle) &&
                   Equals(this.RandomizerSeed, other.RandomizerSeed) &&
                   Equals(this.BrushSize, other.BrushSize);
        }
            
    }

    public class PlayerPropToolCommandHandler : BaseToolCommandHandler<PlayerPropToolCommand, PropTool>
    {
        protected override void Configure(PropTool tool, ToolController toolController, PlayerPropToolCommand command) {
            // Note: Some private fields are already initialised by the ToolSimulator
            // These fields here are the important ones to transmit between game sessions
            
            ReflectionHelper.SetAttr(tool, "m_propInfo", PrefabCollection<PropInfo>.GetPrefab(command.Prop));
            tool.m_mode = (PropTool.Mode) command.Mode;
            ReflectionHelper.SetAttr(tool, "m_cachedPosition", command.Position);
            ReflectionHelper.SetAttr(tool, "m_cachedAngle", command.Angle);
            ReflectionHelper.SetAttr(tool, "m_randomizer", new Randomizer(command.RandomizerSeed));

            if (tool.m_mode == PropTool.Mode.Brush)
            {
                toolController.SetBrush(tool.m_brush, command.Position, command.BrushSize);
            }
            else
            {
                toolController.SetBrush(null, Vector3.zero, 1f);
            }
        }

        protected override CursorInfo GetCursorInfo(PropTool tool)
        {
            return tool.m_buildCursor;
        }
    }
}
