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
    // Preserve layer scroll position when reordering or editing layers
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollReset
    {
        static Vector2 _scrollCache;
        static bool _refocusSelectedLayer;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "ResetUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(__instance);
                if (!proxy.IsValid) return;
                _scrollCache = proxy.LayerScroll;
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerScrollReset.Prefix: {e}"); }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                if (AnimatorDefaultSettings.Load().preventLayerScroll) return;
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(__instance);
                if (!proxy.IsValid) return;
                if (proxy.LayerScroll.y == 0)
                    proxy.LayerScroll = _scrollCache;
                _refocusSelectedLayer = true;
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerScrollReset.Postfix: {e}"); }
        }

        internal static bool ConsumeRefocus()
        {
            if (!_refocusSelectedLayer) return false;
            _refocusSelectedLayer = false;
            return true;
        }
    }

    // Scroll to keep selected layer visible after ResetUI
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollRefocus
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect)
        {
            try
            {
                if (!PatchLayerScrollReset.ConsumeRefocus()) return;
                if (AnimatorDefaultSettings.Load().preventLayerScroll) return;
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(__instance);
                if (!proxy.IsValid) return;
                var reorderableList = proxy.LayerList;
                if (reorderableList == null) return;
                var currentScroll = proxy.LayerScroll;
                float elementHeight = (float)WindowPatchReflection.GetElementHeightMethod.Invoke(reorderableList, new object[] { reorderableList.index }) + 20;
                float elementOffset = (float)WindowPatchReflection.GetElementYOffsetMethod.Invoke(reorderableList, new object[] { reorderableList.index });
                if (elementOffset < currentScroll.y)
                    proxy.LayerScroll = new Vector2(currentScroll.x, elementOffset);
                else if (elementOffset + elementHeight > currentScroll.y + rect.height)
                    proxy.LayerScroll = new Vector2(currentScroll.x, elementOffset + elementHeight - rect.height);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerScrollRefocus.Prefix: {e}"); }
        }
    }

    // Default weight of newly added layers to 1
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWeightDefault
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(AnimatorController), "AddLayer",
                new Type[] { typeof(AnimatorControllerLayer) });

        [HarmonyPrefix]
        static void Prefix(ref AnimatorControllerLayer layer)
        {
            try
            {
                if (!AnimatorDefaultSettings.Load().newLayerWeightOne) return;
                layer.defaultWeight = 1.0f;
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerWeightDefault.Prefix: {e}"); }
        }
    }

    // Layer copy/paste via right-click context menu on each layer row
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCopyPaste
    {
        internal static AnimatorControllerLayer _layerClipboard;
        static AnimatorController _controllerClipboard;

        static AnimatorController GetController(object layerView) =>
            Traverse.Create(layerView).Field("m_Host").Property("animatorController")
                .GetValue<AnimatorController>();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            try
            {
            var evt = Event.current;
            if (evt.type != EventType.MouseUp || evt.button != 1 || !rect.Contains(evt.mousePosition)) return;

            evt.Use();
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(L10n.Get("layer_menu.copy")), false,
                static data => CopyLayer(data), __instance);

            if (_layerClipboard != null)
            {
                menu.AddItem(new GUIContent(L10n.Get("layer_menu.paste")), false,
                    static data => PasteLayer(data), __instance);
                menu.AddItem(new GUIContent(L10n.Get("layer_menu.paste_settings")), false,
                    static data => PasteLayerSettings(data), __instance);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(L10n.Get("layer_menu.paste")));
                menu.AddDisabledItem(new GUIContent(L10n.Get("layer_menu.paste_settings")));
            }

            menu.AddItem(new GUIContent(L10n.Get("layer_menu.delete")), false,
                static data => Traverse.Create(data).Method("DeleteLayer").GetValue(null), __instance);

            if (AnimatorDefaultSettings.Load().layerTemplateButtonEnabled)
            {
                var capturedController = GetController(__instance);
                int capturedIndex      = index;
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(L10n.Get("layer_menu.create_template")), false, () =>
                    AnimatorTemplateParameterWindow.OpenCreate(capturedController, capturedIndex));
            }

            menu.ShowAsContext();
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchLayerCopyPaste.Prefix: {e}"); }
        }

        internal static void CopyLayer(object layerView)
        {
            PatchGuard.Run("CopyLayer", () =>
            {
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(layerView);
                if (!proxy.IsValid) return;
                var reorderableList = proxy.LayerList;
                if (reorderableList == null) return;
                var controller = GetController(layerView);
                _layerClipboard = reorderableList.list[reorderableList.index] as AnimatorControllerLayer;
                _controllerClipboard = controller;
                Unsupported.CopyStateMachineDataToPasteboard(_layerClipboard.stateMachine, controller, reorderableList.index);
            });
        }

        internal static void PasteLayer(object layerView, bool appendToBottom = false)
        {
            try
            {
            if (_layerClipboard == null) return;

            var proxy = new WindowPatchReflection.LayerControllerViewProxy(layerView);
            if (!proxy.IsValid) return;
            var reorderableList = proxy.LayerList;
            if (reorderableList == null) return;
            var controller = GetController(layerView);
            int targetIndex = appendToBottom ? controller.layers.Length : reorderableList.index + 1;
            string newName = controller.MakeUniqueLayerName(_layerClipboard.name);

            if (!appendToBottom) Undo.FlushUndoRecordObjects();

            controller.AddLayer(newName);
            var layers = controller.layers;
            int pastedIndex = layers.Length - 1;
            var pastedLayer = layers[pastedIndex];
            Unsupported.PasteToStateMachineFromPasteboard(pastedLayer.stateMachine, controller, pastedIndex, Vector3.zero);

            // Promote pasted SM from child wrapper to top-level
            var pastedSM = pastedLayer.stateMachine.stateMachines[0].stateMachine;
            pastedSM.name = newName;
            pastedLayer.stateMachine.stateMachines = new ChildAnimatorStateMachine[0];
            UnityEngine.Object.DestroyImmediate(pastedLayer.stateMachine, true);
            pastedLayer.stateMachine = pastedSM;
            PasteLayerProperties(pastedLayer, _layerClipboard);
            CopyLayerFrames(_layerClipboard.stateMachine, pastedSM, _controllerClipboard, controller);

            if (!appendToBottom)
            {
                // Move to just below source layer
                for (int i = layers.Length - 1; i > targetIndex; i--)
                    layers[i] = layers[i - 1];
                layers[targetIndex] = pastedLayer;
            }
            controller.layers = layers;

            if (!appendToBottom)
            {
                // Prevent undo from leaving dangling sub-assets
                Undo.ClearUndo(controller);
            }

            // Cross-controller paste: sync referenced parameters
            if (controller != _controllerClipboard)
            {
                if (!appendToBottom) Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                SyncCrossControllerParams(controller, pastedSM);
                if (!appendToBottom) Undo.CollapseUndoOperations(group);
            }

            EditorUtility.SetDirty(controller);
            Traverse.Create(layerView).Property("selectedLayerIndex").SetValue(targetIndex);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PasteLayer: {e}"); }
        }

        internal static void PasteLayerSettings(object layerView)
        {
            PatchGuard.Run("PasteLayerSettings", () =>
            {
                if (_layerClipboard == null) return;
                var proxy = new WindowPatchReflection.LayerControllerViewProxy(layerView);
                if (!proxy.IsValid) return;
                var reorderableList = proxy.LayerList;
                if (reorderableList == null) return;
                var controller = GetController(layerView);
                var layers = controller.layers;
                PasteLayerProperties(layers[reorderableList.index], _layerClipboard);
                controller.layers = layers;
            });
        }

        static void PasteLayerProperties(AnimatorControllerLayer destinationLayer, AnimatorControllerLayer sourceLayer)
        {
            destinationLayer.avatarMask               = sourceLayer.avatarMask;
            destinationLayer.blendingMode             = sourceLayer.blendingMode;
            destinationLayer.defaultWeight            = sourceLayer.defaultWeight;
            destinationLayer.iKPass                   = sourceLayer.iKPass;
            destinationLayer.syncedLayerAffectsTiming = sourceLayer.syncedLayerAffectsTiming;
            destinationLayer.syncedLayerIndex         = sourceLayer.syncedLayerIndex;
        }

        static void GatherSmParams(AnimatorStateMachine sm,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.mirrorParameterActive      && src.ContainsKey(state.mirrorParameter))      queued[state.mirrorParameter]      = src[state.mirrorParameter];
                if (state.speedParameterActive       && src.ContainsKey(state.speedParameter))       queued[state.speedParameter]       = src[state.speedParameter];
                if (state.timeParameterActive        && src.ContainsKey(state.timeParameter))        queued[state.timeParameter]        = src[state.timeParameter];
                if (state.cycleOffsetParameterActive && src.ContainsKey(state.cycleOffsetParameter)) queued[state.cycleOffsetParameter] = src[state.cycleOffsetParameter];

                if (state.motion is BlendTree blendTree)
                    GatherBtParams(blendTree, ref src, ref queued);
            }

            var transitions = new List<AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var childState in sm.states)
                transitions.AddRange(childState.state.transitions);
            foreach (var transition in transitions)
                foreach (var cond in transition.conditions)
                    if (src.ContainsKey(cond.parameter))
                        queued[cond.parameter] = src[cond.parameter];

            foreach (var childStateMachine in sm.stateMachines)
                GatherSmParams(childStateMachine.stateMachine, ref src, ref queued);
        }

        static void GatherBtParams(BlendTree blendTree,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            if (src.ContainsKey(blendTree.blendParameter))  queued[blendTree.blendParameter]  = src[blendTree.blendParameter];
            if (src.ContainsKey(blendTree.blendParameterY)) queued[blendTree.blendParameterY] = src[blendTree.blendParameterY];

            foreach (var childMotion in blendTree.children)
            {
                if (src.ContainsKey(childMotion.directBlendParameter))
                    queued[childMotion.directBlendParameter] = src[childMotion.directBlendParameter];
                if (childMotion.motion is BlendTree childBlendTree)
                    GatherBtParams(childBlendTree, ref src, ref queued);
            }
        }

        static void SyncCrossControllerParams(AnimatorController controller, AnimatorStateMachine pastedSM)
        {
            Undo.RecordObject(controller, "Sync pasted layer parameters");

            var destParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(controller.parameters.Length);
            foreach (var parameter in controller.parameters) destParams[parameter.name] = parameter;

            var srcParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
            foreach (var parameter in _controllerClipboard.parameters) srcParams[parameter.name] = parameter;

            var queued = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
            GatherSmParams(pastedSM, ref srcParams, ref queued);

            foreach (var parameter in queued.Values)
                if (!destParams.ContainsKey(parameter.name))
                    controller.AddParameter(parameter);
        }

        internal static void BuildSMMap(
            AnimatorStateMachine source,
            AnimatorStateMachine destination,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> map)
        {
            map[source] = destination;
            foreach (var sourceChild in source.stateMachines)
            {
                var destinationChild = destination.stateMachines
                    .FirstOrDefault(c => c.stateMachine.name == sourceChild.stateMachine.name);
                if (destinationChild.stateMachine != null)
                    BuildSMMap(sourceChild.stateMachine, destinationChild.stateMachine, map);
            }
        }

        static void CopyLayerFrames(
            AnimatorStateMachine sourceSM,
            AnimatorStateMachine destinationSM,
            AnimatorController sourceController,
            AnimatorController destinationController)
        {
            var sourceData = FrameLayoutData.GetOrCreate(sourceController);
            if (!sourceData.frames.Any(frame => frame.layerStateMachine == sourceSM)) return;

            var smMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            BuildSMMap(sourceSM, destinationSM, smMap);

            var destinationData = FrameLayoutData.GetOrCreate(destinationController);
            bool dirty = false;

            foreach (var frame in sourceData.frames.ToArray())
            {
                if (frame.layerStateMachine != sourceSM) continue;
                if (!smMap.TryGetValue(frame.activeSM, out var mappedActiveSM)) continue;

                destinationData.frames.Add(new FrameRect
                {
                    title             = frame.title,
                    layerStateMachine = destinationSM,
                    activeSM          = mappedActiveSM,
                    bounds            = frame.bounds,
                    color             = frame.color,
                    locked            = frame.locked,
                });
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(destinationData);
                AssetDatabase.SaveAssets();
            }
        }

        internal static void ImportAllLayersFromTemplate(
            AnimatorController templateController, object targetLayerView)
        {
            Undo.SetCurrentGroupName("Import Template Layers");
            int undoGroup = Undo.GetCurrentGroup();
            Undo.FlushUndoRecordObjects();

            var layers = templateController.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                _layerClipboard = layers[i];
                _controllerClipboard = templateController;
                Unsupported.CopyStateMachineDataToPasteboard(layers[i].stateMachine, templateController, i);
                PasteLayer(targetLayerView, appendToBottom: true);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    // Layer list: WD indicator if all states have Write Defaults on, ! if empty
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWDIndicator
    {
        static GUIStyle _labelStyle;
        internal static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 9, alignment = TextAnchor.MiddleRight };

        internal static readonly GUIContent WdContent = new GUIContent("WD");
        internal static readonly GUIContent EmptyContent = new GUIContent("empty");
        internal static readonly Color EmptyColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        static GUIContent _frameIcon;
        internal static GUIContent FrameIcon => _frameIcon ??= EditorGUIUtility.IconContent("animationdopesheetkeyframe");

        // Cache 1: FrameLayoutData — LoadAllAssetsAtPath is expensive; load once per frame per controller
        static AnimatorController _frameDataCachedController;
        static FrameLayoutData    _frameDataCache;
        static int                _frameDataCachedFrame = -1;

        // Cache 2: WD counts — recursive SM traversal; recompute only on undo/asset change
        static readonly Dictionary<(AnimatorStateMachine sm, bool includeBT), (int on, int off)> _wdCache = new();

        // Cache 3: HasFrameData per SM — LINQ + ActiveSMReachable traversal; recompute only on undo/asset change
        static readonly Dictionary<AnimatorStateMachine, bool> _hasFrameDataCache = new();

        // Cache 4: m_AnimatorController FieldInfo — avoids Traverse allocation + string field lookup per layer
        static FieldInfo _animatorControllerField;

        // Cache 5: GUIStyle CalcSize results — IconContent + CalcSize per layer is not free
        static float _cachedGearWidth   = -1f;
        static float _cachedWdWidth     = -1f;
        static float _cachedEmptyWidth  = -1f;

        static PatchLayerWDIndicator()
        {
            Undo.undoRedoPerformed += InvalidateCaches;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => InvalidateCaches();
        }

        static void InvalidateCaches()
        {
            _wdCache.Clear();
            _hasFrameDataCache.Clear();
        }

        internal static bool IsEmpty(AnimatorStateMachine sm) =>
            sm.states.Length == 0 && sm.stateMachines.Length == 0;

        static FrameLayoutData GetCachedFrameLayoutData(AnimatorController controller)
        {
            int currentFrame = Time.frameCount;
            if (_frameDataCachedFrame == currentFrame && _frameDataCachedController == controller)
                return _frameDataCache;
            _frameDataCachedController = controller;
            _frameDataCachedFrame      = currentFrame;
            _frameDataCache            = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller))
                .OfType<FrameLayoutData>().FirstOrDefault();
            return _frameDataCache;
        }

        internal static bool HasFrameData(AnimatorStateMachine sm, AnimatorController controller)
        {
            if (_hasFrameDataCache.TryGetValue(sm, out bool cached)) return cached;
            var frameLayoutData = GetCachedFrameLayoutData(controller);
            bool result = frameLayoutData != null && frameLayoutData.frames.Any(frame =>
                frame.layerStateMachine == sm && FrameRenderer.ActiveSMReachable(sm, frame.activeSM));
            _hasFrameDataCache[sm] = result;
            return result;
        }

        internal static (int on, int off) GetOrComputeWD(AnimatorStateMachine sm, bool includeBlendTrees)
        {
            var key = (sm, includeBlendTrees);
            if (_wdCache.TryGetValue(key, out var cached)) return cached;
            int on = 0, off = 0;
            CountWD(sm, ref on, ref off, includeBlendTrees);
            _wdCache[key] = (on, off);
            return (on, off);
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            if (EditorApplication.isPlaying) return;
            if (Event.current.type != EventType.Repaint) return;

            var settings = AnimatorDefaultSettings.Load();
            if (!settings.showLayerWDIndicator) return;

            try
            {
                var layerViewHost = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                _animatorControllerField ??= AccessTools.Field(layerViewHost.GetType(), "m_AnimatorController");
                var controller = _animatorControllerField?.GetValue(layerViewHost) as AnimatorController;
                if (controller == null || index >= controller.layers.Length) return;

                var stateMachine = controller.layers[index].stateMachine;
                if (_cachedGearWidth < 0f)
                    _cachedGearWidth = EditorStyles.iconButton.CalcSize(EditorGUIUtility.IconContent("d_SettingsIcon")).x;
                float gearWidth = _cachedGearWidth;
                float maskOffset = controller.layers[index].avatarMask != null ? 15f : 0f;
                float cursorX = rect.xMax - gearWidth - 8f - maskOffset;

                bool isEmpty = IsEmpty(stateMachine);
                bool hasFrameData = HasFrameData(stateMachine, controller);

                int writeDefaultsOnCount = 0, writeDefaultsOffCount = 0;
                if (!isEmpty)
                    (writeDefaultsOnCount, writeDefaultsOffCount) = GetOrComputeWD(stateMachine, settings.wdIncludeBlendTreeStates);
                bool showWD = !isEmpty && writeDefaultsOnCount > 0;

                if (showWD)
                {
                    if (_cachedWdWidth < 0f) _cachedWdWidth = LabelStyle.CalcSize(WdContent).x;
                    float wdWidth = _cachedWdWidth;
                    var wdRect = new Rect(cursorX - wdWidth, rect.yMin + 5f, wdWidth, 16f);
                    LabelStyle.normal.textColor = writeDefaultsOffCount == 0 ? settings.layerWDColor : Color.cyan;
                    EditorGUI.LabelField(wdRect, "WD", LabelStyle);
                    cursorX = wdRect.x - 2f;
                }

                if (hasFrameData)
                {
                    var frameIconRect = new Rect(cursorX - 16f, rect.yMin + 5f, 16f, 16f);
                    GUI.Label(frameIconRect, FrameIcon);
                    cursorX = frameIconRect.x - 4f;
                }

                if (isEmpty)
                {
                    if (_cachedEmptyWidth < 0f) _cachedEmptyWidth = LabelStyle.CalcSize(EmptyContent).x;
                    float emptyWidth = _cachedEmptyWidth;
                    var emptyRect = new Rect(cursorX - emptyWidth, rect.yMin + 5f, emptyWidth, 16f);
                    LabelStyle.normal.textColor = EmptyColor;
                    EditorGUI.LabelField(emptyRect, "empty", LabelStyle);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Layer WD indicator error: {e}");
            }
        }

        static void CountWD(AnimatorStateMachine sm, ref int writeDefaultsOnCount, ref int writeDefaultsOffCount, bool includeBlendTrees)
        {
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is BlendTree) continue;
                if (childState.state.writeDefaultValues) writeDefaultsOnCount++;
                else writeDefaultsOffCount++;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CountWD(childStateMachine.stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount, includeBlendTrees);
        }
    }

}
#endif
