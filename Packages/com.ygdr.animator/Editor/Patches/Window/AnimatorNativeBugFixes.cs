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
using System.Collections.Generic;
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

    // Bug: unticking a state's speed/time/mirror/cycleOffset "Parameter" toggle leaves the linked
    // parameter name on the state (only the *ParameterActive flag goes false). Deleting that parameter
    // from the native Parameters list still blocks/warns "is used by" that state, because
    // AnimatorController.CollectObjectsUsingParameter (native) matches the field regardless of the
    // Active flag — the same gap our own unused-parameter indicators already account for.
    // Fix: OnRemoveParameter (managed) is replaced with a filtered copy that drops AnimatorState
    // matches whose corresponding *ParameterActive flag is off, so an inactive link no longer blocks
    // deletion. Genuine uses (transitions, behaviours, active-driven states) still block as before.
    [HarmonyPatch]
    internal static class PatchParameterDeleteInactiveLinkedState
    {
        static readonly MethodInfo ResetTextFieldsMethod =
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "ResetTextFields");
        static readonly MethodInfo GrabKeyboardFocusMethod =
            AccessTools.Method(typeof(UnityEditorInternal.ReorderableList), "GrabKeyboardFocus");
        static readonly MethodInfo CollectObjectsUsingParameterMethod =
            AccessTools.Method(typeof(AnimatorController), "CollectObjectsUsingParameter");

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnRemoveParameter");

        [HarmonyPrefix]
        static bool Prefix(object __instance, int index)
        {
            try
            {
                var parameterList = WindowPatchReflection.ParameterListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                if (parameterList == null || index >= parameterList.list.Count) return false;

                var host = WindowPatchReflection.ParameterViewHostField?.GetValue(__instance);
                if (host == null) return true;
                bool liveLink = WindowPatchReflection.AnimatorControllerToolLiveLinkGetter?.Invoke(host, null) is true;
                if (liveLink) return false;

                var controller = WindowPatchReflection.AnimatorControllerGetter.Invoke(host, null) as AnimatorController;
                if (controller == null) return true;

                var element = parameterList.list[index];
                var parameter = Traverse.Create(element).Field("m_Parameter").GetValue<AnimatorControllerParameter>();
                if (parameter == null) return true;
                if (CollectObjectsUsingParameterMethod == null) return true;

                var rawUsages = CollectObjectsUsingParameterMethod.Invoke(controller, new object[] { parameter.name })
                    as System.Collections.IEnumerable;
                if (rawUsages == null) return true;

                var usages = rawUsages.Cast<UnityEngine.Object>()
                    .Where(obj => obj is not AnimatorState state || IsActiveLink(state, parameter.name))
                    .ToList();

                bool proceed = usages.Count == 0;
                if (!proceed)
                {
                    string text = "It is used by : \n" + string.Concat(usages.Select(DescribeUsage));
                    proceed = EditorUtility.DisplayDialog($"Delete parameter {parameter.name}?", text, "Delete", "Cancel");
                }

                if (proceed)
                {
                    var undoTargets = usages.Cast<UnityEngine.Object>().Append(controller).ToArray();
                    Undo.RegisterCompleteObjectUndo(undoTargets, "Parameter removed");
                    controller.RemoveParameter(parameter);
                    ResetTextFieldsMethod?.Invoke(__instance, null);
                    WindowPatchReflection.ParameterRebuildListMethod?.Invoke(__instance, null);
                    GrabKeyboardFocusMethod?.Invoke(parameterList, null);
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchParameterDeleteInactiveLinkedState: {e}");
                return true;
            }
        }

        static bool IsActiveLink(AnimatorState state, string paramName) =>
            (state.speedParameterActive && state.speedParameter == paramName) ||
            (state.timeParameterActive && state.timeParameter == paramName) ||
            (state.mirrorParameterActive && state.mirrorParameter == paramName) ||
            (state.cycleOffsetParameterActive && state.cycleOffsetParameter == paramName);

        static string DescribeUsage(UnityEngine.Object item) => item switch
        {
            AnimatorTransitionBase { destinationState: { } dest } => $"Transition to {dest.name}\n",
            AnimatorTransitionBase { destinationStateMachine: { } destSM } => $"Transition to {destSM.name}\n",
            _ => $"{item.name}\n"
        };
    }

    // Bug: multi-selecting states with mixed speed/time/mirror/cycleOffset *ParameterActive flags
    // collapses all selected states to one flag value (usually all off), without any click.
    // Root cause: native StateEditor.OnParametrizedValueGUI(Override) unconditionally writes
    // `valueParameterActive.boolValue = EditorGUILayout.ToggleLeft(..., valueParameterActive.boolValue, ...)`
    // every repaint, with no showMixedValue/BeginChangeCheck guard. SerializedProperty.boolValue on a
    // mixed-value multi-edit reads target[0]'s value, so it gets rewritten to every selected object each
    // frame. Default state "wins" only because it happens to be target[0] when it's part of the selection.
    // Fix: replace both methods with a copy that shows the mixed-value dash and only writes the toggle
    // back when the user actually clicks it (BeginChangeCheck-gated), matching normal Unity multi-edit UX.
    static class ParametrizedValueGuiShared
    {
        internal static readonly Type StateEditorType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateEditor");
        internal static readonly MethodInfo ControllerContextGetter =
            AccessTools.PropertyGetter(StateEditorType, "controllerContext");
        static readonly MethodInfo CollectParametersMethod =
            AccessTools.Method(StateEditorType, "CollectParameters");
        static readonly MethodInfo TextFieldDropDownMethod =
            AccessTools.Method(typeof(EditorGUILayout), "TextFieldDropDown", new[] { typeof(GUIContent), typeof(string), typeof(string[]) });

        internal static string TextFieldDropDown(GUIContent content, string value, string[] options) =>
            TextFieldDropDownMethod?.Invoke(null, new object[] { content, value, options }) as string ?? value;

        internal static List<string> CollectParameters(object instance, AnimatorController controller, AnimatorControllerParameterType type) =>
            (List<string>)CollectParametersMethod.Invoke(instance, new object[] { controller, type });

        internal static void DrawValueOrLabel(SerializedProperty value, string name)
        {
            if (value != null) EditorGUILayout.PropertyField(value);
            else EditorGUILayout.LabelField(new GUIContent(name));
        }

        internal static void ApplyToggleFix(SerializedProperty valueParameterActive, string tooltip)
        {
            EditorGUI.showMixedValue = valueParameterActive.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool newActive = EditorGUILayout.ToggleLeft(
                EditorGUIUtility.TrTextContent("Parameter", tooltip),
                valueParameterActive.boolValue, GUILayout.MaxWidth(100f));
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck()) valueParameterActive.boolValue = newActive;
        }
    }

    [HarmonyPatch]
    internal static class PatchStateSpeedParameterToggle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(ParametrizedValueGuiShared.StateEditorType, "OnParametrizedValueGUI");

        [HarmonyPrefix]
        static bool Prefix(object __instance, SerializedProperty value, SerializedProperty valueParameter,
            SerializedProperty valueParameterActive, AnimatorControllerParameterType parameterType)
        {
            try
            {
                if (value != null) EditorGUILayout.PropertyField(value);

                var controller = ParametrizedValueGuiShared.ControllerContextGetter?.Invoke(__instance, null) as AnimatorController;
                if (controller == null) return false;

                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();

                var parameters = ParametrizedValueGuiShared.CollectParameters(__instance, controller, parameterType);
                if (parameters.Count == 0 && valueParameterActive.boolValue)
                {
                    EditorGUILayout.HelpBox($"Must have at least one Parameter of type {parameterType} in the AnimatorController", MessageType.Error);
                }
                else
                {
                    if (valueParameterActive.boolValue && valueParameter.stringValue == "" && parameters.Count > 0)
                        valueParameter.stringValue = parameters[0];

                    using (new EditorGUI.DisabledScope(!valueParameterActive.boolValue))
                    {
                        EditorGUI.BeginChangeCheck();
                        string dropdownValue = ParametrizedValueGuiShared.TextFieldDropDown(
                            EditorGUIUtility.TrTextContent("Multiplier", "Parameter used as multiplier for speed."),
                            valueParameter.stringValue, parameters.ToArray());
                        if (EditorGUI.EndChangeCheck()) valueParameter.stringValue = dropdownValue;
                    }
                }

                EditorGUI.indentLevel--;
                ParametrizedValueGuiShared.ApplyToggleFix(valueParameterActive,
                    "Use an AnimatorController's parameter to modulate this property at runtime.");
                EditorGUILayout.EndHorizontal();
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchStateSpeedParameterToggle: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class PatchStateOverrideParameterToggle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(ParametrizedValueGuiShared.StateEditorType, "OnParametrizedValueGUIOverride");

        [HarmonyPrefix]
        static bool Prefix(object __instance, string name, SerializedProperty value, SerializedProperty valueParameter,
            SerializedProperty valueParameterActive, AnimatorControllerParameterType parameterType)
        {
            try
            {
                var controller = ParametrizedValueGuiShared.ControllerContextGetter?.Invoke(__instance, null) as AnimatorController;
                if (controller == null)
                {
                    ParametrizedValueGuiShared.DrawValueOrLabel(value, name);
                    return false;
                }

                EditorGUILayout.BeginHorizontal();

                if (!valueParameterActive.boolValue)
                {
                    ParametrizedValueGuiShared.DrawValueOrLabel(value, name);
                }
                else
                {
                    var parameters = ParametrizedValueGuiShared.CollectParameters(__instance, controller, parameterType);
                    if (parameters.Count == 0)
                    {
                        EditorGUILayout.HelpBox($"Must have at least one Parameter of type {parameterType} in the AnimatorController", MessageType.Error);
                    }
                    else
                    {
                        if (valueParameter.stringValue == "") valueParameter.stringValue = parameters[0];
                        EditorGUI.BeginChangeCheck();
                        string dropdownValue = ParametrizedValueGuiShared.TextFieldDropDown(
                            new GUIContent(name), valueParameter.stringValue, parameters.ToArray());
                        if (EditorGUI.EndChangeCheck()) valueParameter.stringValue = dropdownValue;
                    }
                }

                ParametrizedValueGuiShared.ApplyToggleFix(valueParameterActive,
                    "Override this constant value with an AnimatorController's parameter to animate this property at runtime.");
                EditorGUILayout.EndHorizontal();
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchStateOverrideParameterToggle: {e}");
                return true;
            }
        }
    }
}
#endif
