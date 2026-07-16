/*
    YGDR Animator Editor - A custom editor for managing complex animator controllers
    Copyright (C) 2026  YerGodDamnRight

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/


#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Bug: after layer reorder, graph shows the layer previously at that index.
    // Root cause: selectedLayerIndex setter fires before animatorController.layers = array,
    // caching the wrong active state machine from the old layer order.
    // Fix: postfix re-triggers setter after layers are updated, with backing-field reset
    // to bypass any equality guard.
    [HarmonyPatch]
    internal static class PatchLayerReorderSelection
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnReorderLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, out string __state)
        {
            __state = null;
            try
            {
                var host = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                int selectedIndex = (int)(WindowPatchReflection.LayerSelectedIndexField.GetValue(__instance) ?? -1);
                if (controller != null && selectedIndex >= 0 && selectedIndex < controller.layers.Length)
                    __state = controller.layers[selectedIndex].name;
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerReorderSelection prefix: {e}"); }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, string __state)
        {
            if (__state == null) return;
            try
            {
                var host = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                if (controller == null) return;
                int newIndex = Array.FindIndex(controller.layers, layer => layer.name == __state);
                if (newIndex < 0) return;
                // Reset backing field so setter doesn't short-circuit on equality, then re-trigger with correct layer order
                WindowPatchReflection.LayerSelectedIndexField.SetValue(__instance, -1);
                AccessTools.PropertySetter(WindowPatchReflection.LayerControllerViewType, "selectedLayerIndex")
                    ?.Invoke(__instance, new object[] { newIndex });
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerReorderSelection postfix: {e}"); }
        }
    }

    // Bug: undo parameter rename → transitions show "parameter does not exist in controller".
    // Root cause: RenameParameter is a native extern — Harmony can't prefix it.
    // Instead patch RenameEnd (managed C#), register all affected transitions before the rename.
    [HarmonyPatch]
    internal static class PatchParameterRenameUndo
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => WindowPatchReflection.ParameterRenameEndMethod;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var renameOverlay = WindowPatchReflection.ParameterRenameOverlayField.GetValue(__instance);
                if (renameOverlay == null) return;

                var overlayType = renameOverlay.GetType();
                bool userAccepted = (bool)(AccessTools.Property(overlayType, "userAcceptedRename")?.GetValue(renameOverlay) ?? false);
                if (!userAccepted) return;

                string originalName = (string)AccessTools.Property(overlayType, "originalName")?.GetValue(renameOverlay);
                string newName = (string)AccessTools.Property(overlayType, "name")?.GetValue(renameOverlay);
                if (string.IsNullOrEmpty(newName)) newName = originalName;
                if (newName == originalName) return;

                var host = AccessTools.Field(__instance.GetType(), "m_Host").GetValue(__instance);
                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                if (controller == null) return;

                foreach (var layer in controller.layers)
                    RegisterTransitionsInSM(layer.stateMachine, originalName);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterRenameUndo: {e}"); }
        }

        static void RegisterTransitionsInSM(AnimatorStateMachine stateMachine, string paramName)
        {
            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    if (transition.conditions.Any(x => x.parameter == paramName))
                        Undo.RegisterCompleteObjectUndo(transition, "Parameter renamed");

            foreach (var transition in stateMachine.anyStateTransitions)
                if (transition.conditions.Any(x => x.parameter == paramName))
                    Undo.RegisterCompleteObjectUndo(transition, "Parameter renamed");

            foreach (var transition in stateMachine.entryTransitions)
                if (transition.conditions.Any(x => x.parameter == paramName))
                    Undo.RegisterCompleteObjectUndo(transition, "Parameter renamed");

            foreach (var childStateMachine in stateMachine.stateMachines)
                RegisterTransitionsInSM(childStateMachine.stateMachine, paramName);
        }
    }

    // Pressing F2 with a layer selected triggers the native layer rename overlay.
    [HarmonyPatch]
    internal static class PatchLayerF2Rename
    {
        internal static bool IsRootLayerStateMachine(AnimatorStateMachine stateMachine)
        {
            var path = AssetDatabase.GetAssetPath(stateMachine);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return false;
            foreach (var layer in controller.layers)
                if (layer.stateMachine == stateMachine) return true;
            return false;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI", new[] { typeof(Rect) })
            ?? AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                if (Event.current.type != EventType.KeyDown || Event.current.keyCode != KeyCode.F2) return;
                var layerList = WindowPatchReflection.LayerListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                if (layerList == null || !layerList.HasKeyboardControl()) return;
                if (Selection.activeObject is AnimatorState) return;
                if (Selection.activeObject is AnimatorStateMachine selectedSM && !IsRootLayerStateMachine(selectedSM)) return;
                if (FrameRenderer.SingleSelected != null) return;
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(__instance);
                if (!proxy.IsValid) return;
                int selectedIndex = proxy.LayerSelectedIndex;
                if (selectedIndex < 0) return;
                var host = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                if (controller == null || selectedIndex >= controller.layers.Length) return;
                string layerName = controller.layers[selectedIndex].name;
                var renameOverlay = WindowPatchReflection.LayerRenameOverlayProperty.GetValue(__instance);
                if (renameOverlay == null) return;
                WindowPatchReflection.RenameOverlayBeginRenameMethod.Invoke(renameOverlay, new object[] { layerName, selectedIndex, 0f });
                Event.current.Use();
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerF2Rename: {e}"); }
        }
    }

    // Pressing F2 with a parameter selected triggers the native parameter rename overlay.
    [HarmonyPatch]
    internal static class PatchParameterF2Rename
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI", new[] { typeof(Rect) })
            ?? AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                if (Event.current.type != EventType.KeyDown || Event.current.keyCode != KeyCode.F2) return;
                var parameterList = WindowPatchReflection.ParameterListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                if (parameterList == null || !parameterList.HasKeyboardControl()) return;
                if (Selection.activeObject is AnimatorState) return;
                if (Selection.activeObject is AnimatorStateMachine selectedSM && !PatchLayerF2Rename.IsRootLayerStateMachine(selectedSM)) return;
                if (FrameRenderer.SingleSelected != null) return;
                int selectedIndex = parameterList.index;
                if (selectedIndex < 0) return;
                var host = WindowPatchReflection.ParameterViewHostField?.GetValue(__instance);
                if (host == null) return;
                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                if (controller == null || selectedIndex >= controller.parameters.Length) return;
                string paramName = controller.parameters[selectedIndex].name;
                var renameOverlay = WindowPatchReflection.ParameterRenameOverlayField?.GetValue(__instance);
                if (renameOverlay == null) return;
                WindowPatchReflection.RenameOverlayBeginRenameMethod.Invoke(renameOverlay, new object[] { paramName, selectedIndex, 0f });
                Event.current.Use();
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterF2Rename: {e}"); }
        }
    }
}
#endif
