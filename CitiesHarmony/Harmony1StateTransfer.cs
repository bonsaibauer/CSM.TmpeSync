﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CitiesHarmony {
    /// <summary>
    /// 1. Reverts Harmony 1.x patches that were applied before this mod was loaded.<br/>
    /// 2. Resets the Harmony shared state so that Harmony 2.x runs without exceptions.<br/>
    /// 3. Self-patches the Harmony 1.2 assembly so that it redirects all calls to Harmony 2.x.<br/>
    /// 4. Re-applies the patches using Harmony 2.x
    /// </summary>
    public class Harmony1StateTransfer {
        private Harmony harmony;
        private Assembly assembly;

        private MethodInfo HarmonySharedState_GetPatchedMethods;
        private MethodInfo HarmonySharedState_GetPatchInfo;

        private FieldInfo PatchInfo_prefixed;
        private FieldInfo PatchInfo_postfixes;
        private FieldInfo PatchInfo_transpilers;

        private FieldInfo Patch_owner;
        private FieldInfo Patch_priority;
        private FieldInfo Patch_before;
        private FieldInfo Patch_after;
        private FieldInfo Patch_patch;

        private Type harmonyInstanceType;
        private MethodInfo HarmonyInstance_Create;
        private MethodInfo HarmonyInstance_Unpatch;

        private object HarmonyPatchType_All;

        public Harmony1StateTransfer(Harmony harmony, Assembly assembly) {
            this.harmony = harmony;
            this.assembly = assembly;

            UnityEngine.Debug.Log($"Transferring Harmony {assembly.GetName().Version} state ({assembly.FullName})");

            var sharedStateType = assembly.GetType("Harmony.HarmonySharedState");
            HarmonySharedState_GetPatchedMethods = sharedStateType.GetMethodOrThrow("GetPatchedMethods", BindingFlags.NonPublic | BindingFlags.Static);
            HarmonySharedState_GetPatchInfo = sharedStateType.GetMethodOrThrow("GetPatchInfo", BindingFlags.NonPublic | BindingFlags.Static);

            var patchInfoType = assembly.GetType("Harmony.PatchInfo");
            PatchInfo_prefixed = patchInfoType.GetFieldOrThrow("prefixes");
            PatchInfo_postfixes = patchInfoType.GetFieldOrThrow("postfixes");
            PatchInfo_transpilers = patchInfoType.GetFieldOrThrow("transpilers");

            var patchType = assembly.GetType("Harmony.Patch");
            Patch_owner = patchType.GetFieldOrThrow("owner");
            Patch_priority = patchType.GetFieldOrThrow("priority");
            Patch_before = patchType.GetFieldOrThrow("before");
            Patch_after = patchType.GetFieldOrThrow("after");
            Patch_patch = patchType.GetFieldOrThrow("patch");

            harmonyInstanceType = assembly.GetType("Harmony.HarmonyInstance") ?? throw new Exception("HarmonyInstance type not found");
            HarmonyInstance_Create = harmonyInstanceType.GetMethodOrThrow("Create", BindingFlags.Public | BindingFlags.Static);

            var harmonyPatchTypeType = assembly.GetType("Harmony.HarmonyPatchType") ?? throw new Exception("HarmonyPatchType type not found");

            var unpatchArgTypes = new Type[] { typeof(MethodBase), harmonyPatchTypeType, typeof(string) };
            HarmonyInstance_Unpatch = HarmonyInstance_Unpatch = harmonyInstanceType.GetMethod("RemovePatch", unpatchArgTypes) // Harmony 1.1.0.0
                ?? harmonyInstanceType.GetMethodOrThrow("Unpatch", unpatchArgTypes); // Harmony 1.2.0.1

            HarmonyPatchType_All = Enum.ToObject(harmonyPatchTypeType, 0);
        }

        public void Patch() {
            var patchedMethods = new List<MethodBase>((HarmonySharedState_GetPatchedMethods.Invoke(null, new object[0]) as IEnumerable<MethodBase>));

            UnityEngine.Debug.Log($"{patchedMethods.Count} patched methods found.");

            var processors = new List<ProcessorInfo>();

            foreach (var method in patchedMethods) {
                var patchInfo = HarmonySharedState_GetPatchInfo.Invoke(null, new object[] { method });
                if (patchInfo == null) continue;

                var prefixes = (object[])PatchInfo_prefixed.GetValue(patchInfo);
                foreach (var patch in prefixes) {
                    processors.Add(ProcessorInfo
                        .Create(CreateHarmony(patch), method)
                        .AddPrefix(CreateHarmonyMethod(patch)));
                }

                var postfixes = (object[])PatchInfo_postfixes.GetValue(patchInfo);
                foreach (var patch in postfixes) {
                    processors.Add(ProcessorInfo
                        .Create(CreateHarmony(patch), method)
                        .AddPostfix(CreateHarmonyMethod(patch)));
                }

                var transpilers = (object[])PatchInfo_transpilers.GetValue(patchInfo);
                foreach (var patch in transpilers) {
                    processors.Add(ProcessorInfo
                        .Create(CreateHarmony(patch), method)
                        .AddTranspiler(CreateHarmonyMethod(patch)));
                }
            }

            UnityEngine.Debug.Log($"Reverting patches...");
            var oldInstance = HarmonyInstance_Create.Invoke(null, new object[] { "CitiesHarmony" }); 
            foreach (var method in patchedMethods.ToList()) {
                HarmonyInstance_Unpatch.Invoke(oldInstance, new object[] { method, HarmonyPatchType_All, null });
            }

            // Reset is not needed while we are using a Harmony 2 fork that uses a different assembly name!
            /*
            // Reset shared state
            var sharedStateAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name.Contains("HarmonySharedState"));
            if (sharedStateAssembly != null) {
                var stateField = sharedStateAssembly.GetType("HarmonySharedState")?.GetField("state");
                if (stateField != null) {
                    UnityEngine.Debug.Log("Resetting HarmonySharedState...");
                    stateField.SetValue(null, null);
                }
            }
            */

            // Apply patches to old Harmony
            Harmony1SelfPatcher.Apply(harmony, assembly);

            // Apply patches using Harmony 2.x
            foreach (var processor in processors) {
                try {
                    UnityEngine.Debug.Log($"Applying patch for {processor.original.FullDescription()} ({processor.instance.Id})");
                    processor.Patch();
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogError($"Exception while transferring Harmony 1 patch in {processor.original.FullDescription()} ({processor.instance.Id})");
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        private Harmony CreateHarmony(object patch) {
            var owner = (string)Patch_owner.GetValue(patch);
            return new Harmony(owner);
        }

        private HarmonyMethod CreateHarmonyMethod(object patch) {
            var priority = (int)Patch_priority.GetValue(patch);
            var before = (string[])Patch_before.GetValue(patch);
            var after = (string[])Patch_after.GetValue(patch);
            var method = (MethodInfo)Patch_patch.GetValue(patch);

            if (!method.IsDeclaredMember()) {
                UnityEngine.Debug.Log($"Attempting to patch non-declared member {method.FullDescription()} (forbidden in Harmony 2.x)! Getting closest declared member for backwards compatibility...");
            }

            return new HarmonyMethod(method.GetDeclaredMember(), priority, before, after);
        }

        private class ProcessorInfo {
            public Harmony instance;
            public MethodBase original;
            public PatchProcessor processor;

            public static ProcessorInfo Create(Harmony instance, MethodBase original) {
                return new ProcessorInfo {
                    instance = instance,
                    original = original,
                    processor = instance.CreateProcessor(original)
                };
            }

            public void Patch() {
                processor.Patch();
            }

            public ProcessorInfo AddPrefix(HarmonyMethod prefix) {
                processor.AddPrefix(prefix);
                return this;
            }

            public ProcessorInfo AddPostfix(HarmonyMethod postfix) {
                processor.AddPostfix(postfix);
                return this;
            }

            public ProcessorInfo AddTranspiler(HarmonyMethod transpiler) {
                processor.AddTranspiler(transpiler);
                return this;
            }
        }
    }
}