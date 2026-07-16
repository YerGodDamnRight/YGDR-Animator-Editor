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
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
#endif

namespace YGDR.Editor.Animation
{
    // Scroll parameter list to bottom when adding a new parameter
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchNewParameterScroll
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "AddParameterMenu");

        [HarmonyPostfix]
        static void Postfix(object __instance, object value)
        {
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.scrollToNewParameter || settings.preventParameterScroll) return;
                Traverse.Create(__instance).Field("m_ScrollPosition").SetValue(new Vector2(0, 9001));
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchNewParameterScroll.Postfix: {e}"); }
        }
    }

    // Parameter row: type label overlay + VRC sync icon + right-click convert menu
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterRow
    {
        static GUIStyle _typeStyle;
        static GUIStyle TypeStyle => _typeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            richText = true
        };

        static GUIContent _syncedIcon;
        static GUIContent _unsyncedIcon;
        static GUIContent SyncedIcon   => _syncedIcon   ??= EditorGUIUtility.IconContent("soloon");
        static GUIContent UnsyncedIcon => _unsyncedIcon ??= EditorGUIUtility.IconContent("solonormal");

        static GUIContent _savedIcon;
        static GUIContent _unsavedIcon;
        static GUIContent SavedIcon   => _savedIcon   ??= EditorGUIUtility.IconContent("bypasson");
        static GUIContent UnsavedIcon => _unsavedIcon ??= EditorGUIUtility.IconContent("bypassnormal");

        static GUIStyle _vrcBuiltinStyle;
        static GUIStyle VrcBuiltinStyle => _vrcBuiltinStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.cyan }
        };

        static GUIContent _clipUsesIcon;
        static GUIContent ClipUsesIcon => _clipUsesIcon ??= EditorGUIUtility.IconContent("d_unityeditor.graphs.animatorcontrollertool@2x");

        static GUIContent _unusedParamIcon;
        static GUIContent UnusedParamIcon => _unusedParamIcon ??= EditorGUIUtility.IconContent("d_p4_local@2x");

        static GUIContent _vrcComponentIcon;
        static GUIContent VrcComponentIcon => _vrcComponentIcon ??= EditorGUIUtility.IconContent("templatecontainer@2x");

#if VRC_SDK_VRCSDK3
        static bool _vrcComponentNeedsRebuild = true;
        static HashSet<string> _vrcComponentUsedParams;
#endif

        internal static readonly HashSet<string> VrcBuiltinNames =
            new HashSet<string>(PatchParameterContextMenu.VrcParameters.Select(tuple => tuple.name));

        static int _clipCacheControllerId = -1;
        static HashSet<string> _clipUsedParams;

        internal static int _conditionCacheControllerId = -1;
        static HashSet<string> _conditionUsedParams;
        static AnimatorController _subscribedConditionController;

        internal static AnimatorController ViewFrameController;
        internal static HashSet<string> ViewFrameClipUsedParams;
        internal static AnimatorDefaultSettings ViewFrameSettings;

        static readonly Dictionary<string, float> _typeWidthCache = new();

        static PatchParameterRow()
        {
            Undo.undoRedoPerformed += () => { _clipCacheControllerId = -1; _conditionCacheControllerId = -1; };
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                _clipCacheControllerId = -1;
                InvalidateConditionCache();
                PruneStaleDefaultCache();
            };
#if VRC_SDK_VRCSDK3
            EditorApplication.hierarchyChanged += () => _vrcComponentNeedsRebuild = true;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                for (int i = 0; i < stream.length; i++)
                {
                    if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
                    var obj = EditorUtility.InstanceIDToObject(data.instanceId);
                    if (obj is ContactReceiver || obj is VRCPhysBone || obj is VRCRaycast)
                    {
                        _vrcComponentNeedsRebuild = true;
                        EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
                        return;
                    }
                }
            };
#endif
        }

#if VRC_SDK_VRCSDK3
        internal static void InvalidateVrcComponentCache() => _vrcComponentNeedsRebuild = true;

        internal static HashSet<string> GetVrcComponentUsedParams()
        {
            if (!_vrcComponentNeedsRebuild && _vrcComponentUsedParams != null) return _vrcComponentUsedParams;
            _vrcComponentUsedParams = AnimatorFindUsageWindow.BuildAllEffectingParamNames();
            _vrcComponentNeedsRebuild = false;
            return _vrcComponentUsedParams;
        }
#endif

        internal static HashSet<string> GetClipUsedParams(AnimatorController controller)
        {
            int controllerId = controller.GetInstanceID();
            if (_clipCacheControllerId == controllerId && _clipUsedParams != null) return _clipUsedParams;
            _clipCacheControllerId = controllerId;
            _clipUsedParams = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectClipParams(layer.stateMachine, _clipUsedParams);
            return _clipUsedParams;
        }

        static void CollectClipParams(AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            foreach (var childState in stateMachine.states)
                CollectMotionParams(childState.state.motion, result);
            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectClipParams(childStateMachine.stateMachine, result);
        }

        static void CollectMotionParams(UnityEngine.Motion motion, HashSet<string> result)
        {
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(UnityEngine.Animator))
                        result.Add(binding.propertyName);
                return;
            }
            if (motion is BlendTree blendTree)
                foreach (var childMotion in blendTree.children)
                    CollectMotionParams(childMotion.motion, result);
        }

        internal static HashSet<string> GetConditionUsedParams(AnimatorController controller)
        {
            if (_subscribedConditionController != controller)
            {
                var dirtyField = WindowPatchReflection.AnimatorControllerDirtyField;
                if (_subscribedConditionController != null && dirtyField != null)
                {
                    var existing = (Action)dirtyField.GetValue(_subscribedConditionController);
                    dirtyField.SetValue(_subscribedConditionController, (Action)Delegate.Remove(existing, (Action)InvalidateConditionCache));
                }
                _subscribedConditionController = controller;
                if (dirtyField != null)
                {
                    var existing = (Action)dirtyField.GetValue(controller);
                    dirtyField.SetValue(controller, (Action)Delegate.Combine(existing, (Action)InvalidateConditionCache));
                }
            }

            int controllerId = controller.GetInstanceID();
            if (_conditionCacheControllerId == controllerId && _conditionUsedParams != null) return _conditionUsedParams;
            _conditionCacheControllerId = controllerId;
            _conditionUsedParams = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectConditionParams(layer.stateMachine, _conditionUsedParams);
#if VRC_SDK_VRCSDK3
            AnimatorParameterOps.CollectParameterDriverNames(controller, _conditionUsedParams);
#endif
            return _conditionUsedParams;
        }

        internal static void InvalidateConditionCache()
        {
            _conditionCacheControllerId = -1;
        }

        static void PruneStaleDefaultCache()
        {
            if (_lastKnownDefaultByName.Count == 0) return;
            var controller = ViewFrameController;
            if (controller == null) return;
            var currentNames = new HashSet<string>(controller.parameters.Select(p => p.name));
            var staleKeys = _lastKnownDefaultByName.Keys.Where(k => !currentNames.Contains(k)).ToList();
            foreach (var key in staleKeys)
                _lastKnownDefaultByName.Remove(key);
        }

        static void CollectConditionParams(AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                foreach (var condition in transition.conditions)
                    result.Add(condition.parameter);
            foreach (var transition in stateMachine.entryTransitions)
                foreach (var condition in transition.conditions)
                    result.Add(condition.parameter);
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                foreach (var transition in state.transitions)
                    foreach (var condition in transition.conditions)
                        result.Add(condition.parameter);
                if (state.speedParameterActive && !string.IsNullOrEmpty(state.speedParameter))
                    result.Add(state.speedParameter);
                if (state.timeParameterActive && !string.IsNullOrEmpty(state.timeParameter))
                    result.Add(state.timeParameter);
                if (state.mirrorParameterActive && !string.IsNullOrEmpty(state.mirrorParameter))
                    result.Add(state.mirrorParameter);
                if (state.cycleOffsetParameterActive && !string.IsNullOrEmpty(state.cycleOffsetParameter))
                    result.Add(state.cycleOffsetParameter);
                CollectBlendTreeConditionParams(state.motion, result);
            }
            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectConditionParams(childStateMachine.stateMachine, result);
        }

        static void CollectBlendTreeConditionParams(Motion motion, HashSet<string> result)
        {
            if (motion is BlendTree blendTree)
            {
                if (!string.IsNullOrEmpty(blendTree.blendParameter))
                    result.Add(blendTree.blendParameter);
                if (!string.IsNullOrEmpty(blendTree.blendParameterY))
                    result.Add(blendTree.blendParameterY);
                foreach (var childMotion in blendTree.children)
                {
                    if (!string.IsNullOrEmpty(childMotion.directBlendParameter))
                        result.Add(childMotion.directBlendParameter);
                    CollectBlendTreeConditionParams(childMotion.motion, result);
                }
            }
        }

#if VRC_SDK_VRCSDK3
        static bool VrcTypesMatch(AnimatorControllerParameterType animType, VRCExpressionParameters.ValueType vrcType) =>
            animType switch
            {
                AnimatorControllerParameterType.Float   => vrcType == VRCExpressionParameters.ValueType.Float,
                AnimatorControllerParameterType.Int     => vrcType == VRCExpressionParameters.ValueType.Int,
                AnimatorControllerParameterType.Bool    => vrcType == VRCExpressionParameters.ValueType.Bool,
                AnimatorControllerParameterType.Trigger => vrcType == VRCExpressionParameters.ValueType.Bool,
                _ => true
            };
#endif

#if VRC_SDK_VRCSDK3
        // Click a row icon to flip it and start a drag; dragging over other rows' same icon paints them to the same value.
        class DragToggleState
        {
            internal bool Active;
            internal bool Value;
            internal readonly HashSet<string> Visited = new();
        }

        static readonly DragToggleState _syncDrag = new();
        static readonly DragToggleState _savedDrag = new();
        static readonly DragToggleState _eligibleDrag = new();

        static void ResetDragStates()
        {
            _syncDrag.Active = false;
            _savedDrag.Active = false;
            _eligibleDrag.Active = false;
        }

        static void DragToggleIcon(Rect iconRect, bool currentValue, DragToggleState drag, string paramName, Action<bool> apply)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && iconRect.Contains(evt.mousePosition))
            {
                evt.Use();
                bool newValue = !currentValue;
                apply(newValue);
                drag.Active = true;
                drag.Value = newValue;
                drag.Visited.Clear();
                drag.Visited.Add(paramName);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }
            else if (evt.type == EventType.MouseDrag && drag.Active && iconRect.Contains(evt.mousePosition) && !drag.Visited.Contains(paramName))
            {
                evt.Use();
                apply(drag.Value);
                drag.Visited.Add(paramName);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }
        }

        static GUIContent _syncEligibleIcon;
        static GUIContent _syncIneligibleIcon;
        static GUIContent SyncEligibleIcon   => _syncEligibleIcon   ??= EditorGUIUtility.IconContent("muteon");
        static GUIContent SyncIneligibleIcon => _syncIneligibleIcon ??= EditorGUIUtility.IconContent("mutenormal");

        // Session-only: not persisted on the parameter or the VRCExpressionParameters asset.
        // Marks whether a controller parameter qualifies for the bulk "Sync VRC Parameters Asset" action.
        static readonly Dictionary<string, bool> _syncEligible = new();

        internal static bool IsSyncEligible(string paramName)
        {
            if (_syncEligible.TryGetValue(paramName, out bool value)) return value;
            bool defaultValue = VRCSyncCache.TryGetParameter(paramName, out _);
            _syncEligible[paramName] = defaultValue;
            return defaultValue;
        }

        internal static void SetSyncEligible(string paramName, bool value) => _syncEligible[paramName] = value;

        internal static HashSet<string> GetSyncIneligibleNames(IEnumerable<string> paramNames) =>
            paramNames.Where(name => !IsSyncEligible(name)).ToHashSet();
#endif

        static readonly Dictionary<int, string> _elementParamNameCache = new();
        static readonly Dictionary<string, float> _lastKnownDefaultByName = new();
        static readonly GUIContent _tempContent = new GUIContent();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewElementType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
            if (parameter != null)
                _elementParamNameCache[__instance.GetHashCode()] = parameter.name;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            try
            {
                if (Event.current.type == EventType.Repaint && focused)
                    PatchParameterContextMenu._hasFocus = true;

#if VRC_SDK_VRCSDK3
                if (Event.current.type == EventType.MouseUp)
                    ResetDragStates();
#endif

                var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
                if (parameter == null) return;

                if (_elementParamNameCache.TryGetValue(__instance.GetHashCode(), out var oldName) && oldName != parameter.name)
                {
                    var controller = ViewFrameController;
                    if (controller != null)
                        AnimatorParameterOps.RemapParameterReferences(controller, oldName, parameter.name);
                    _elementParamNameCache[__instance.GetHashCode()] = parameter.name;
                    EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
                }

#if VRC_SDK_VRCSDK3
                if (parameter.type != AnimatorControllerParameterType.Trigger)
                {
                    float currentDefault = parameter.type switch
                    {
                        AnimatorControllerParameterType.Float => parameter.defaultFloat,
                        AnimatorControllerParameterType.Int   => parameter.defaultInt,
                        AnimatorControllerParameterType.Bool  => parameter.defaultBool ? 1f : 0f,
                        _ => 0f
                    };

                    if (VRCSyncCache.TryGetParameter(parameter.name, out var vrcParamForDefault))
                    {
                        bool hasLastKnown = _lastKnownDefaultByName.TryGetValue(parameter.name, out float lastKnown);
                        if (!hasLastKnown)
                        {
                            _lastKnownDefaultByName[parameter.name] = currentDefault;
                        }
                        else if (vrcParamForDefault.defaultValue != lastKnown)
                        {
                            var controller = ViewFrameController;
                            if (controller != null)
                            {
                                var allParams = controller.parameters;
                                int paramIndex = System.Array.FindIndex(allParams, p => p.name == parameter.name);
                                if (paramIndex >= 0)
                                {
                                    Undo.RecordObject(controller, "Sync Parameter Default From VRC");
                                    switch (parameter.type)
                                    {
                                        case AnimatorControllerParameterType.Float: allParams[paramIndex].defaultFloat = vrcParamForDefault.defaultValue; break;
                                        case AnimatorControllerParameterType.Int:   allParams[paramIndex].defaultInt   = (int)vrcParamForDefault.defaultValue; break;
                                        case AnimatorControllerParameterType.Bool:  allParams[paramIndex].defaultBool  = vrcParamForDefault.defaultValue != 0f; break;
                                    }
                                    controller.parameters = allParams;
                                    EditorUtility.SetDirty(controller);
                                }
                            }
                            _lastKnownDefaultByName[parameter.name] = vrcParamForDefault.defaultValue;
                        }
                        else if (currentDefault != lastKnown)
                        {
                            var expParamsForDefault = VRCSyncCache.GetExpressionParameters();
                            Undo.RecordObject(expParamsForDefault, "Sync VRC Parameter Default");
                            vrcParamForDefault.defaultValue = currentDefault;
                            EditorUtility.SetDirty(expParamsForDefault);
                            EditorApplication.delayCall += () => WindowPatchReflection.RebuildInspectorsShowing(expParamsForDefault);
                            _lastKnownDefaultByName[parameter.name] = currentDefault;
                        }
                    }
                }
#endif

                var settings = ViewFrameSettings ?? AnimatorDefaultSettings.Load();
                bool showType = settings.showParamTypeIcons;
                bool showVrc  = settings.showParamVrcIcons;
                bool showAap  = settings.showParamAapIcons;
                bool showVrcComponent = settings.showParamVrcComponentIcons;
                bool showUnused = settings.showParamUnusedIcon;

#if VRC_SDK_VRCSDK3
                bool hasSyncData = VRCSyncCache.TryGetSync(parameter.name, out bool isSynced);
#else
                const bool hasSyncData = false;
#endif
                if (!hasSyncData && !showType && !showVrc && !showAap && !showVrcComponent && !showUnused) return;

#if VRC_SDK_VRCSDK3
                VRCExpressionParameters.ValueType vrcValueType = default;
                bool hasMismatch = showType
                    && VRCSyncCache.TryGetVrcValueType(parameter.name, out vrcValueType)
                    && !VrcTypesMatch(parameter.type, vrcValueType);
#else
                const bool hasMismatch = false;
#endif

                const float iconPadding = 2f;
                const float clipIconSize = 20f;
                const float vrcLabelWidth = 23f;

                float cursorX = rect.xMax - 72f;

#if VRC_SDK_VRCSDK3
                const float iconSize = 14f;
                var expParams = VRCSyncCache.GetExpressionParameters();

                if (showVrcComponent && expParams != null)
                {
                    bool eligible = IsSyncEligible(parameter.name);
                    var eligibleIconRect = new Rect(rect.xMin - 18f, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
                    GUI.Label(eligibleIconRect, eligible ? SyncEligibleIcon : SyncIneligibleIcon);
                    DragToggleIcon(eligibleIconRect, eligible, _eligibleDrag, parameter.name,
                        newValue => SetSyncEligible(parameter.name, newValue));
                }

                if (hasSyncData && showVrcComponent)
                {
                    VRCExpressionParameters.Parameter vrcParam = null;
                    if (expParams != null) VRCSyncCache.TryGetParameter(parameter.name, out vrcParam);

                    cursorX -= iconSize + iconPadding;
                    var syncIconRect = new Rect(cursorX, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
                    GUI.Label(syncIconRect, isSynced ? SyncedIcon : UnsyncedIcon);
                    DragToggleIcon(syncIconRect, isSynced, _syncDrag, parameter.name, newValue =>
                    {
                        if (vrcParam != null) AnimatorParameterOps.SetVrcSynced(expParams, parameter.name, newValue);
                    });
                    cursorX -= iconPadding;

                    if (vrcParam != null)
                    {
                        cursorX -= iconSize;
                        var savedIconRect = new Rect(cursorX, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
                        GUI.Label(savedIconRect, vrcParam.saved ? SavedIcon : UnsavedIcon);
                        DragToggleIcon(savedIconRect, vrcParam.saved, _savedDrag, parameter.name, newValue =>
                        {
                            Undo.RecordObject(expParams, "Toggle VRC Parameter Saved");
                            vrcParam.saved = newValue;
                            EditorUtility.SetDirty(expParams);
                        });
                        cursorX -= iconPadding;
                    }
                }
#endif

                if (showType)
                {
                    var typeColor = hasMismatch ? new Color(0.5f, 0.5f, 0.5f) : parameter.type switch
                    {
                        AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                        AnimatorControllerParameterType.Int     => settings.paramColorInt,
                        AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                        AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                        _ => Color.white
                    };

                    string typeText = parameter.type.ToString();
                    if (!_typeWidthCache.TryGetValue(typeText, out float typeTextWidth))
                    {
                        _tempContent.text = typeText;
                        typeTextWidth = TypeStyle.CalcSize(_tempContent).x;
                        _typeWidthCache[typeText] = typeTextWidth;
                    }

#if VRC_SDK_VRCSDK3
                    if (hasMismatch)
                    {
                        var vrcColor = vrcValueType switch
                        {
                            VRCExpressionParameters.ValueType.Float => settings.paramColorFloat,
                            VRCExpressionParameters.ValueType.Int   => settings.paramColorInt,
                            _                                        => settings.paramColorBool,
                        };

                        string vrcTypeText = vrcValueType.ToString();
                        if (!_typeWidthCache.TryGetValue(vrcTypeText, out float vrcTypeWidth))
                        {
                            _tempContent.text = vrcTypeText;
                            vrcTypeWidth = TypeStyle.CalcSize(_tempContent).x;
                            _typeWidthCache[vrcTypeText] = vrcTypeWidth;
                        }
                        var prevColor = GUI.color;
                        GUI.color = vrcColor;
                        cursorX -= vrcTypeWidth;
                        GUI.Label(new Rect(cursorX, rect.y, vrcTypeWidth, rect.height), vrcTypeText, TypeStyle);

                        if (!_typeWidthCache.TryGetValue("/", out float sepWidth))
                        {
                            _tempContent.text = "/";
                            sepWidth = TypeStyle.CalcSize(_tempContent).x;
                            _typeWidthCache["/"] = sepWidth;
                        }
                        cursorX -= sepWidth;
                        GUI.color = new Color(0.5f, 0.5f, 0.5f);
                        GUI.Label(new Rect(cursorX, rect.y, sepWidth, rect.height), "/", TypeStyle);
                        GUI.color = prevColor;
                    }
#endif

                    var savedColor = GUI.color;
                    GUI.color = typeColor;
                    cursorX -= typeTextWidth;
                    GUI.Label(new Rect(cursorX, rect.y, typeTextWidth, rect.height), typeText, TypeStyle);
                    GUI.color = savedColor;

                    cursorX -= iconPadding;
                }

                if (showVrc && VrcBuiltinNames.Contains(parameter.name))
                {
                    cursorX -= vrcLabelWidth;
                    var prevVrcColor = GUI.color;
                    GUI.color = settings.paramColorVrcLabel;
                    GUI.Label(new Rect(cursorX, rect.y, vrcLabelWidth, rect.height), "VRC", VrcBuiltinStyle);
                    GUI.color = prevVrcColor;
                    cursorX -= iconPadding;
                }

#if VRC_SDK_VRCSDK3
                if (showVrcComponent && GetVrcComponentUsedParams().Contains(parameter.name))
                {
                    cursorX -= clipIconSize;
                    var vrcComponentIconRect = new Rect(cursorX, rect.y + (rect.height - clipIconSize) * 0.5f, clipIconSize, clipIconSize);
                    GUI.Label(vrcComponentIconRect, VrcComponentIcon);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && vrcComponentIconRect.Contains(Event.current.mousePosition))
                    {
                        Event.current.Use();
                        AnimatorFindUsageWindow.Open(parameter, ViewFrameController);
                    }
                    cursorX -= iconPadding;
                }
#endif

                if (showAap && ViewFrameClipUsedParams != null && ViewFrameClipUsedParams.Contains(parameter.name))
                {
                    cursorX -= clipIconSize;
                    var aapIconRect = new Rect(cursorX, rect.y + (rect.height - clipIconSize) * 0.5f, clipIconSize, clipIconSize);
                    GUI.Label(aapIconRect, ClipUsesIcon);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && aapIconRect.Contains(Event.current.mousePosition))
                    {
                        Event.current.Use();
                        AnimatorFindUsageWindow.Open(parameter, ViewFrameController);
                    }
                }

                if (showUnused && ViewFrameController != null)
                {
                    var conditionUsedParams = GetConditionUsedParams(ViewFrameController);
                    bool usedAsAap = ViewFrameClipUsedParams != null && ViewFrameClipUsedParams.Contains(parameter.name);
                    if (!conditionUsedParams.Contains(parameter.name) && !usedAsAap)
                    {
                        cursorX -= clipIconSize;
                        GUI.Label(new Rect(cursorX, rect.y + (rect.height - clipIconSize) * 0.5f, clipIconSize, clipIconSize), UnusedParamIcon);
                        cursorX -= iconPadding;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter row error: {e}");
            }
        }
    }

    // Replaces the "+" dropdown to insert below selected param, plus VRC parameter presets
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterAddMenu
    {
        static readonly Type _viewType = WindowPatchReflection.ParameterControllerViewType;
        internal static readonly FieldInfo ParamListField =
            _viewType?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType == typeof(UnityEditorInternal.ReorderableList));

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(_viewType, "OnAddParameter");

        [HarmonyPrefix]
        static bool Prefix(object __instance, Rect buttonRect)
        {
            try
            {
                if (!AnimatorDefaultSettings.Load().parameterAddMenuEnabled) return true;
                var controller = WindowPatchReflection.GetOpenController();
                if (controller == null) return true;

                var reorderableList = ParamListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                int insertIndex = (reorderableList != null && reorderableList.index >= 0)
                    ? reorderableList.index + 1
                    : controller.parameters.Length;

                var menu = new GenericMenu();
                var capturedInstance = __instance;
                foreach (AnimatorControllerParameterType type in
                         Enum.GetValues(typeof(AnimatorControllerParameterType)))
                {
                    var capturedType = type;
                    menu.AddItem(new GUIContent(type.ToString()), false, () =>
                        InsertWithUniqueName(capturedInstance, controller, insertIndex, capturedType));
                }

#if VRC_SDK_VRCSDK3
                var existingParamNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
                menu.AddSeparator("");
                foreach (var (category, vrcParamName, vrcParamType) in PatchParameterContextMenu.VrcParameters)
                {
                    var content = new GUIContent($"VRC/{category}/{vrcParamName}");
                    if (existingParamNames.Contains(vrcParamName))
                    {
                        menu.AddDisabledItem(content, true);
                    }
                    else
                    {
                        var capturedName = vrcParamName;
                        var capturedType = vrcParamType;
                        menu.AddItem(content, false, () =>
                            AnimatorParameterOps.InsertParameterAtIndex(controller, insertIndex, capturedName, capturedType));
                    }
                }
#endif

                menu.DropDown(buttonRect);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter add menu error: {e}");
                return true;
            }
        }

        internal static void InsertWithUniqueName(object instance, AnimatorController controller,
            int index, AnimatorControllerParameterType type)
        {
            string baseName = type.ToString();
            string paramName = baseName;
            var existingNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
            int counter = 1;
            while (existingNames.Contains(paramName))
                paramName = $"{baseName} {counter++}";
            AnimatorParameterOps.InsertParameterAtIndex(controller, index, paramName, type);

            WindowPatchReflection.ParameterRebuildListMethod?.Invoke(instance, null);
            var paramList = ParamListField?.GetValue(instance) as UnityEditorInternal.ReorderableList;
            if (paramList != null) paramList.index = index;
            var renameOverlay = WindowPatchReflection.ParameterRenameOverlayField?.GetValue(instance);
            if (renameOverlay == null) return;
            if (WindowPatchReflection.RenameOverlayIsRenamingMethod?.Invoke(renameOverlay, null) is true)
                WindowPatchReflection.ParameterRenameEndMethod?.Invoke(instance, null);
            WindowPatchReflection.RenameOverlayBeginRenameMethod?.Invoke(renameOverlay, new object[] { paramName, index, 0.1f });
        }
    }

    // Right-click convert menu on ParameterControllerView.OnGUI (Element.OnGUI is Repaint-only)
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterContextMenu
    {
        static AnimatorControllerParameter _parameterClipboard;
        internal static bool _hasFocus;

        static void PasteParameter(object instance, AnimatorController controller, string selectedParamName)
        {
            if (_parameterClipboard == null) return;
            var existingNames = new HashSet<string>(controller.parameters.Select(p => p.name));
            string uniqueName = _parameterClipboard.name;
            int counter = 1;
            while (existingNames.Contains(uniqueName))
                uniqueName = $"{_parameterClipboard.name} {counter++}";

            int actualIndex = Array.FindIndex(controller.parameters, p => p.name == selectedParamName);
            int insertIndex = actualIndex >= 0 ? actualIndex + 1 : controller.parameters.Length;

            AnimatorParameterOps.InsertParameterAtIndex(controller, insertIndex, uniqueName, _parameterClipboard.type);

            var allParams = controller.parameters;
            if (insertIndex < allParams.Length && allParams[insertIndex].name == uniqueName)
            {
                allParams[insertIndex].defaultFloat = _parameterClipboard.defaultFloat;
                allParams[insertIndex].defaultInt   = _parameterClipboard.defaultInt;
                allParams[insertIndex].defaultBool  = _parameterClipboard.defaultBool;
                controller.parameters = allParams;
            }
            EditorUtility.SetDirty(controller);

            WindowPatchReflection.ParameterRebuildListMethod?.Invoke(instance, null);
            var paramList = PatchParameterAddMenu.ParamListField?.GetValue(instance) as UnityEditorInternal.ReorderableList;
            if (paramList != null) paramList.index = insertIndex;
        }
#if VRC_SDK_VRCSDK3
        static void ConfirmAndSyncVrcParameters(VRCExpressionParameters expressionParameters, AnimatorController controller)
        {
            var ineligibleNames = PatchParameterRow.GetSyncIneligibleNames(
                controller.parameters.Select(parameter => parameter.name));
            var (toAdd, toRemove) = AnimatorParameterOps.PreviewVrcParameterSync(expressionParameters, controller, ineligibleNames);
            if (toAdd.Count == 0 && toRemove.Count == 0) return;

            string body = string.Format(L10n.Get("params_menu.sync_vrc_asset_body"),
                toAdd.Count == 0 ? L10n.Get("params_menu.sync_vrc_asset_none") : string.Join("\n", toAdd),
                toRemove.Count == 0 ? L10n.Get("params_menu.sync_vrc_asset_none") : string.Join("\n", toRemove));

            if (!EditorUtility.DisplayDialog(L10n.Get("params_menu.sync_vrc_asset_title"), body,
                    L10n.Get("params_menu.sync_vrc_asset_ok"), L10n.Get("params_menu.sync_vrc_asset_cancel")))
                return;

            AnimatorParameterOps.SyncVrcParameters(expressionParameters, controller, ineligibleNames);
            EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
        }
#endif

        internal static readonly (string category, string name, AnimatorControllerParameterType type)[] VrcParameters =
        {
            ("Local",    "IsLocal",              AnimatorControllerParameterType.Bool),
            ("Local",    "PreviewMode",          AnimatorControllerParameterType.Int),
            ("Speech",   "Viseme",               AnimatorControllerParameterType.Int),
            ("Speech",   "Voice",                AnimatorControllerParameterType.Float),
            ("IK",       "GestureLeft",          AnimatorControllerParameterType.Int),
            ("IK",       "GestureRight",         AnimatorControllerParameterType.Int),
            ("IK",       "AngularY",             AnimatorControllerParameterType.Float),
            ("IK",       "VelocityX",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityY",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityZ",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityMagnitude",    AnimatorControllerParameterType.Float),
            ("IK",       "Upright",              AnimatorControllerParameterType.Float),
            ("IK",       "Grounded",             AnimatorControllerParameterType.Bool),
            ("IK",       "Seated",               AnimatorControllerParameterType.Bool),
            ("IK",       "AFK",                  AnimatorControllerParameterType.Bool),
            ("IK",       "VRMode",               AnimatorControllerParameterType.Int),
            ("IK",       "InStation",            AnimatorControllerParameterType.Bool),
            ("IK",       "AvatarVersion",        AnimatorControllerParameterType.Int),
            ("Playable", "GestureLeftWeight",    AnimatorControllerParameterType.Float),
            ("Playable", "GestureRightWeight",   AnimatorControllerParameterType.Float),
            ("Playable", "TrackingType",         AnimatorControllerParameterType.Int),
            ("Playable", "MuteSelf",             AnimatorControllerParameterType.Bool),
            ("Playable", "Earmuffs",             AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleModified",        AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleFactor",          AnimatorControllerParameterType.Float),
            ("Playable", "ScaleFactorInverse",   AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsMeters",    AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsPercent",   AnimatorControllerParameterType.Float),
            ("Social",   "IsOnFriendsList",      AnimatorControllerParameterType.Bool),
            ("System",   "IsAnimatorEnabled",    AnimatorControllerParameterType.Bool),
        };

        static readonly Dictionary<int, string[]> _paramNameCache = new();
        static bool _isProcessingSiblingRenames;

        static PatchParameterContextMenu()
        {
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                for (int i = 0; i < stream.length; i++)
                {
                    if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var eventData);
                    if (EditorUtility.InstanceIDToObject(eventData.instanceId) is not AnimatorController controller) continue;
                    if (!_paramNameCache.TryGetValue(controller.GetInstanceID(), out var oldNames)) continue;
                    var newNames = controller.parameters.Select(parameter => parameter.name).ToArray();
                    if (oldNames.Length != newNames.Length) { _paramNameCache[controller.GetInstanceID()] = newNames; continue; }
                    // Pure reorder: same set of names, different positions — don't treat as rename
                    if (oldNames.OrderBy(n => n).SequenceEqual(newNames.OrderBy(n => n))) { _paramNameCache[controller.GetInstanceID()] = newNames; continue; }
                    for (int j = 0; j < newNames.Length; j++)
                    {
                        if (newNames[j] == oldNames[j]) continue;
                        bool shouldRemap = true;
#if VRC_SDK_VRCSDK3
                        if (!_isProcessingSiblingRenames)
                            shouldRemap = TryRenameSiblingVariants(controller, newNames, oldNames[j], newNames[j]);
#endif
                        if (shouldRemap)
                        {
                            AnimatorParameterOps.RemapParameterReferences(controller, oldNames[j], newNames[j]);
#if VRC_SDK_VRCSDK3
                            if (PatchParameterRow.GetVrcComponentUsedParams().Contains(oldNames[j]))
                                AnimatorFindUsageWindow.RemapVrcComponentParameters(oldNames[j], newNames[j]);
#endif
                            EditorUtility.SetDirty(controller);
                        }
                    }
                    _paramNameCache[controller.GetInstanceID()] = newNames;
                }
            };
        }

        internal static UnityEditorInternal.ReorderableList FindParamList(object instance) =>
            PatchParameterAddMenu.ParamListField?.GetValue(instance) as UnityEditorInternal.ReorderableList;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
            var viewController = WindowPatchReflection.GetOpenController();
            PatchParameterRow.ViewFrameController = viewController;
            PatchParameterRow.ViewFrameClipUsedParams = viewController != null
                ? PatchParameterRow.GetClipUsedParams(viewController)
                : null;
            PatchParameterRow.ViewFrameSettings = AnimatorDefaultSettings.Load();

            if (viewController != null && !_paramNameCache.ContainsKey(viewController.GetInstanceID()))
                _paramNameCache[viewController.GetInstanceID()] = viewController.parameters.Select(parameter => parameter.name).ToArray();

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.Repaint)
                _hasFocus = false;

            if (currentEvent.type == EventType.KeyDown && viewController != null && _hasFocus)
            {
                var kbReorderableList = FindParamList(__instance);
                if (kbReorderableList != null && kbReorderableList.index >= 0)
                {
                    var kbListItem = kbReorderableList.index < kbReorderableList.list.Count
                        ? kbReorderableList.list[kbReorderableList.index] : null;
                    var kbParam = kbListItem != null
                        ? Traverse.Create(kbListItem).Field("m_Parameter").GetValue<AnimatorControllerParameter>()
                        : null;
                    if (kbParam != null)
                    {
                        var kbSettings = AnimatorDefaultSettings.Load();
                        if (kbSettings.kbCopy.Matches(currentEvent))
                        {
                            _parameterClipboard = kbParam;
                            currentEvent.Use();
                            return;
                        }
                        if (kbSettings.kbPaste.Matches(currentEvent) && _parameterClipboard != null)
                        {
                            PasteParameter(__instance, viewController, kbParam.name);
                            currentEvent.Use();
                            return;
                        }
                        if (kbSettings.kbDuplicate.Matches(currentEvent))
                        {
                            _parameterClipboard = kbParam;
                            PasteParameter(__instance, viewController, kbParam.name);
                            currentEvent.Use();
                            return;
                        }
                    }
                }
            }

            if (!AnimatorDefaultSettings.Load().parameterAddMenuEnabled) return;
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;

            var reorderableList = FindParamList(__instance);
            if (reorderableList == null || reorderableList.index < 0) return;

            var controller = WindowPatchReflection.GetOpenController();
            if (controller == null) return;

            // reorderableList.index is the visual (filtered) index — derive actual parameter from the list item
            var listItem = reorderableList.index < reorderableList.list.Count
                ? reorderableList.list[reorderableList.index]
                : null;
            var parameter = listItem != null
                ? Traverse.Create(listItem).Field("m_Parameter").GetValue<AnimatorControllerParameter>()
                : null;
            if (parameter == null) return;

            // actual index in the unfiltered controller.parameters array
            var capturedIndex = Array.FindIndex(controller.parameters, p => p.name == parameter.name);
            if (capturedIndex < 0) return;
            var capturedInstance = __instance;

            var capturedScreenPos = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            currentEvent.Use();
            var menu = new GenericMenu();
            foreach (AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(AnimatorControllerParameterType)))
            {
                var capturedAddType = type;
                menu.AddItem(new GUIContent($"{L10n.Get("params_menu.add_below")}/{type}"), false, () =>
                    PatchParameterAddMenu.InsertWithUniqueName(capturedInstance, controller, capturedIndex + 1, capturedAddType));
            }
            menu.AddSeparator("");
#if VRC_SDK_VRCSDK3
            var expressionParameters = VRCSyncCache.GetExpressionParameters();
            bool hasVrcParams = expressionParameters?.parameters != null;
#else
            bool hasVrcParams = false;
#endif
            if (hasVrcParams)
            {
#if VRC_SDK_VRCSDK3
                bool existsInVrc = VRCSyncCache.TryGetVrcValueType(parameter.name, out var currentVrcType);
                var capturedConvertExpParams = expressionParameters;
                var capturedConvertParamName = parameter.name;
                foreach (AnimatorControllerParameterType type in
                         Enum.GetValues(typeof(AnimatorControllerParameterType)))
                {
                    var capturedType = type;
                    string convertToLabel = $"{L10n.Get("params_menu.convert_to")} {type}";
                    bool controllerAlreadyMatches = type == parameter.type;
                    if (controllerAlreadyMatches)
                        menu.AddDisabledItem(new GUIContent($"{convertToLabel}/{L10n.Get("params_menu.convert_controller")}"), false);
                    else
                        menu.AddItem(new GUIContent($"{convertToLabel}/{L10n.Get("params_menu.convert_controller")}"), false, () =>
                            AnimatorParameterOps.ConvertParameter(controller, capturedIndex, capturedType));
                    bool vrcAlreadyMatches = existsInVrc && (
                        type == AnimatorControllerParameterType.Float ? currentVrcType == VRCExpressionParameters.ValueType.Float :
                        type == AnimatorControllerParameterType.Int   ? currentVrcType == VRCExpressionParameters.ValueType.Int   :
                        currentVrcType == VRCExpressionParameters.ValueType.Bool);
                    if (existsInVrc && !vrcAlreadyMatches)
                        menu.AddItem(new GUIContent($"{convertToLabel}/{L10n.Get("params_menu.convert_vrc_params")}"), false, () =>
                            AnimatorParameterOps.ConvertVrcParameter(capturedConvertExpParams, capturedConvertParamName, capturedType));
                    else
                        menu.AddDisabledItem(new GUIContent($"{convertToLabel}/{L10n.Get("params_menu.convert_vrc_params")}"));
                }
#endif
            }
            else
            {
                foreach (AnimatorControllerParameterType type in
                         Enum.GetValues(typeof(AnimatorControllerParameterType)))
                {
                    var capturedType = type;
                    if (type == parameter.type)
                        menu.AddDisabledItem(new GUIContent($"{L10n.Get("params_menu.convert_to")} {type}"), false);
                    else
                        menu.AddItem(new GUIContent($"{L10n.Get("params_menu.convert_to")} {type}"), false, () =>
                            AnimatorParameterOps.ConvertParameter(controller, capturedIndex, capturedType));
                }
            }

#if VRC_SDK_VRCSDK3
            if (hasVrcParams)
            {
                VRCExpressionParameters.Parameter vrcParam = null;
                foreach (var expressionParameter in expressionParameters.parameters)
                    if (expressionParameter.name == parameter.name) { vrcParam = expressionParameter; break; }

                var capturedExpressionParameters = expressionParameters;
                var capturedParamName = parameter.name;
                var capturedParamType = parameter.type;
                menu.AddSeparator("");

                if (vrcParam == null)
                    menu.AddItem(new GUIContent(L10n.Get("params_menu.add_to_vrc")), false,
                        () => AnimatorParameterOps.AddToVrcParameters(capturedExpressionParameters, capturedParamName, capturedParamType));

                var capturedController = controller;
                menu.AddItem(new GUIContent(L10n.Get("params_menu.sync_vrc_asset")), false,
                    () => ConfirmAndSyncVrcParameters(capturedExpressionParameters, capturedController));
            }
#endif

            menu.AddSeparator("");
            var capturedFindParameter = parameter;
            var capturedFindController = controller;
            menu.AddItem(new GUIContent(L10n.Get("params_menu.find_uses")), false,
                () => AnimatorFindUsageWindow.Open(capturedFindParameter, capturedFindController));

            if (parameter.type == AnimatorControllerParameterType.Float)
            {
                var  clipUsedParams   = PatchParameterRow.ViewFrameClipUsedParams;
                bool parameterIsAAPLinked = clipUsedParams != null && clipUsedParams.Contains(parameter.name);

                var capturedAAPParam = parameter;
                menu.AddItem(new GUIContent(L10n.Get("params_menu.create_aap")), false, static data =>
                {
                    var (aapController, aapParam, screenPos) = ((AnimatorController, AnimatorControllerParameter, Vector2))data;
                    EditorApplication.delayCall += () =>
                        new ClipDropdown(aapController, aapParam).ShowWithCheckmarks(new Rect(screenPos, Vector2.zero));
                }, (capturedFindController, capturedAAPParam, capturedScreenPos));

                if (parameterIsAAPLinked)
                    menu.AddItem(new GUIContent(L10n.Get("params_menu.remove_aap")), false, static data =>
                    {
                        var (aapController, aapParam, screenPos) = ((AnimatorController, AnimatorControllerParameter, Vector2))data;
                        EditorApplication.delayCall += () =>
                            ClipDropdown.ForRemove(aapController, aapParam).ShowWithCheckmarks(new Rect(screenPos, Vector2.zero));
                    }, (capturedFindController, capturedAAPParam, capturedScreenPos));
                else
                    menu.AddDisabledItem(new GUIContent(L10n.Get("params_menu.remove_aap")));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L10n.Get("params_menu.remap_to")), false, static data =>
            {
                var (remapController, fromParamName, screenPos) = ((AnimatorController, string, Vector2))data;
                EditorApplication.delayCall += () =>
                    new ParameterRemapDropdown(remapController, fromParamName).ShowCapped(new Rect(screenPos, Vector2.zero));
            }, (capturedFindController, capturedFindParameter.name, capturedScreenPos));
            menu.AddItem(new GUIContent(L10n.Get("params_menu.delete_and_clean")), false, static data =>
            {
                var (deleteController, deleteParamName) = ((AnimatorController, string))data;
                AnimatorParameterOps.DeleteParameterAndClean(deleteController, deleteParamName);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }, (capturedFindController, capturedFindParameter.name));
            menu.AddItem(new GUIContent(L10n.Get("params_menu.remove_unused")), false, static data =>
            {
                AnimatorParameterOps.RemoveUnusedParameters((AnimatorController)data);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }, capturedFindController);

            menu.ShowAsContext();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterContextMenu.Prefix: {e}"); }
        }

        static bool TryRenameSiblingVariants(AnimatorController controller, string[] newNames, string oldName, string newName)
        {
#if VRC_SDK_VRCSDK3
            string[] suffixes = null;
            string componentTypeName = null;
            string matchedSuffix = null;

            foreach (var suffix in AnimatorFindUsageWindow.PhysBoneSuffixes)
            {
                if (oldName.EndsWith(suffix, StringComparison.Ordinal) && newName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    suffixes = AnimatorFindUsageWindow.PhysBoneSuffixes;
                    componentTypeName = "PhysBone";
                    matchedSuffix = suffix;
                    break;
                }
            }
            if (suffixes == null)
            {
                foreach (var suffix in AnimatorFindUsageWindow.RaycastSuffixes)
                {
                    if (oldName.EndsWith(suffix, StringComparison.Ordinal) && newName.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        suffixes = AnimatorFindUsageWindow.RaycastSuffixes;
                        componentTypeName = "Raycast";
                        matchedSuffix = suffix;
                        break;
                    }
                }
            }
            if (suffixes == null) return true;

            string oldBase = oldName.Substring(0, oldName.Length - matchedSuffix.Length);
            string newBase = newName.Substring(0, newName.Length - matchedSuffix.Length);

            var siblings = new List<(int paramIndex, string oldSiblingName, string newSiblingName)>();
            for (int k = 0; k < newNames.Length; k++)
            {
                if (newNames[k] == oldName) continue;
                foreach (var suffix in suffixes)
                {
                    if (newNames[k] == oldBase + suffix)
                    {
                        siblings.Add((k, oldBase + suffix, newBase + suffix));
                        break;
                    }
                }
            }
            if (siblings.Count == 0) return true;

            string siblingList = string.Join("\n", siblings.Select(sibling => $"{sibling.oldSiblingName}  →  {sibling.newSiblingName}"));
            string paramWord = siblings.Count == 1
                ? L10n.Get("params_menu.rename_sibling_param")
                : L10n.Get("params_menu.rename_sibling_params");
            // 0 = Rename All, 1 = Cancel (revert original rename), 2 = Skip
            int dialogResult = EditorUtility.DisplayDialogComplex(
                L10n.Get("params_menu.rename_sibling_title"),
                string.Format(L10n.Get("params_menu.rename_sibling_body"),
                    oldBase, newBase, siblings.Count, componentTypeName, paramWord, siblingList),
                L10n.Get("params_menu.rename_sibling_ok"),
                L10n.Get("params_menu.rename_sibling_cancel"),
                L10n.Get("params_menu.rename_sibling_skip"));

            if (dialogResult == 2) return true;

            if (dialogResult == 1)
            {
                var serializedControllerForRevert = new SerializedObject(controller);
                var parametersPropertyForRevert = serializedControllerForRevert.FindProperty("m_AnimatorParameters");
                for (int k = 0; k < parametersPropertyForRevert.arraySize; k++)
                {
                    var nameProperty = parametersPropertyForRevert.GetArrayElementAtIndex(k).FindPropertyRelative("m_Name");
                    if (nameProperty.stringValue == newName)
                    {
                        nameProperty.stringValue = oldName;
                        break;
                    }
                }
                serializedControllerForRevert.ApplyModifiedProperties();

                AnimatorParameterOps.RemapParameter(controller, newName, oldName);
                if (PatchParameterRow.GetVrcComponentUsedParams().Contains(newName))
                    AnimatorFindUsageWindow.RemapVrcComponentParameters(newName, oldName);

                int revertIndex = Array.IndexOf(newNames, newName);
                if (revertIndex >= 0) newNames[revertIndex] = oldName;

                EditorUtility.SetDirty(controller);
                return false;
            }

            foreach (var (paramIndex, _, newSiblingName) in siblings)
                newNames[paramIndex] = newSiblingName;
            _paramNameCache[controller.GetInstanceID()] = newNames;

            _isProcessingSiblingRenames = true;
            try
            {
                var serializedController = new SerializedObject(controller);
                var parametersProperty = serializedController.FindProperty("m_AnimatorParameters");
                foreach (var (_, oldSiblingName, newSiblingName) in siblings)
                {
                    for (int k = 0; k < parametersProperty.arraySize; k++)
                    {
                        var nameProperty = parametersProperty.GetArrayElementAtIndex(k).FindPropertyRelative("m_Name");
                        if (nameProperty.stringValue == oldSiblingName)
                        {
                            nameProperty.stringValue = newSiblingName;
                            break;
                        }
                    }
                }
                serializedController.ApplyModifiedProperties();

                foreach (var (_, oldSiblingName, newSiblingName) in siblings)
                {
                    AnimatorParameterOps.RemapParameter(controller, oldSiblingName, newSiblingName);
#if VRC_SDK_VRCSDK3
                    if (PatchParameterRow.GetVrcComponentUsedParams().Contains(oldSiblingName))
                        AnimatorFindUsageWindow.RemapVrcComponentParameters(oldSiblingName, newSiblingName);
#endif
                }

                EditorUtility.SetDirty(controller);
            }
            finally
            {
                _isProcessingSiblingRenames = false;
            }
            return true;
#else
            return true;
#endif
        }

        class ParameterRemapDropdown : AdvancedDropdown
        {
            readonly AnimatorController _controller;
            readonly string _fromParam;

            internal ParameterRemapDropdown(AnimatorController controller, string fromParam)
                : base(new AdvancedDropdownState())
            {
                _controller = controller;
                _fromParam = fromParam;
                minimumSize = new Vector2(200, 250);
            }

            internal void ShowCapped(Rect rect)
            {
                WindowPatchReflection.AdvancedDropdownMaximumSizeProperty?.SetValue(this, new Vector2(10000f, 350f));
                Show(rect);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(L10n.Get("params_menu.remap_to"));
                foreach (var parameter in _controller.parameters)
                {
                    if (parameter.name == _fromParam) continue;
                    root.AddChild(new AdvancedDropdownItem(parameter.name));
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
                => AnimatorParameterOps.RemapParameter(_controller, _fromParam, item.name);
        }
    }
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterBudget
    {
        static GUIStyle _style;
        static GUIStyle Style => _style ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            richText   = true
        };

        static AnimatorController _controller;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnToolbarGUI");

        [HarmonyPrefix]
        static void Prefix() => _controller = WindowPatchReflection.GetOpenController();

        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
            if (Event.current.type != EventType.Repaint) return;
            if (!AnimatorDefaultSettings.Load().showParamBudget) return;
            if (_controller == null) return;

            int controllerBits = 0;
#if VRC_SDK_VRCSDK3
            int syncedBits     = 0;
#endif
            var builtins       = PatchParameterRow.VrcBuiltinNames;

            foreach (var parameter in _controller.parameters)
            {
                if (builtins.Contains(parameter.name)) continue;
                int cost = parameter.type switch
                {
                    AnimatorControllerParameterType.Float   => 8,
                    AnimatorControllerParameterType.Int     => 8,
                    AnimatorControllerParameterType.Bool    => 1,
                    AnimatorControllerParameterType.Trigger => 1,
                    _ => 0
                };
                controllerBits += cost;
#if VRC_SDK_VRCSDK3
                if (VRCSyncCache.TryGetSync(parameter.name, out bool isSynced) && isSynced)
                {
                    int syncedCost = VRCSyncCache.TryGetVrcValueType(parameter.name, out VRCExpressionParameters.ValueType vrcType)
                        ? vrcType switch
                        {
                            VRCExpressionParameters.ValueType.Float => 8,
                            VRCExpressionParameters.ValueType.Int   => 8,
                            VRCExpressionParameters.ValueType.Bool  => 1,
                            _ => 0
                        }
                        : cost;
                    syncedBits += syncedCost;
                }
#endif
            }

            string text;
            float textWidth;
#if VRC_SDK_VRCSDK3
            bool hasSyncData = VRCSyncCache.GetExpressionParameters() != null;
#endif

            text      = controllerBits > 256
                ? $"<color=#ff4444>{controllerBits}/256</color>"
                : $"{controllerBits}/256";
            textWidth = 64f;
#if VRC_SDK_VRCSDK3
            if (hasSyncData)
            {
                string syncedPart = syncedBits > 256
                    ? $"<color=#ff4444>{syncedBits}/256</color>"
                    : $"{syncedBits}/256";
                text      = $"{controllerBits} | {syncedPart}";
                textWidth = 110f;
            }
#endif

            var plusRect = GUILayoutUtility.GetLastRect();
            GUI.Label(new Rect(plusRect.x - textWidth - 18f, plusRect.y, textWidth, plusRect.height), text, Style);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterBudget.Postfix: {e}"); }
        }
    }

    // Invalidate condition cache when blend tree parameter dropdown changes in the inspector
    [HarmonyPatch]
    internal static class PatchBlendTreeParameterGUI
    {
        static MethodBase TargetMethod() => BlendTreePatchReflection.BlendTreeParameterGUIMethod;

        static void Postfix() => PatchParameterRow.InvalidateConditionCache();
    }
}
#endif
