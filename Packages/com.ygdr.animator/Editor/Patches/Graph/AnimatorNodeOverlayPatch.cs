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
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;


namespace YGDR.Editor.Animation
{
    // ──── State nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorStateNodeOverlayPatch
    {
        internal static readonly Dictionary<AnimatorState, Rect> NodeRects = new();
        internal static readonly Dictionary<AnimatorState, Vector2> NodeScreenCenters = new();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var state = GetState(__instance);
                if (state == null) return;
                var stateRect    = GUILayoutUtility.GetLastRect();
                var currentEvent = Event.current;
                bool isRenaming = StateRenameState.RenameTarget == state;
                bool isRenamingMotion = MotionRenameState.RenameTargetState == state;

                if (currentEvent.type != EventType.Repaint)
                {
                    if (isRenaming) DrawRenameField(state, stateRect);
                    if (isRenamingMotion) DrawMotionRenameField(state, stateRect);
                    return;
                }

                NodeRects[state] = stateRect;
                NodeScreenCenters[state] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
                if (AnimatorGraphAnalyzer.HighlightedStates.Contains(state))
                {
                    var highlightColor = AnimatorGraphAnalyzer.HighlightColor;
                    highlightColor.a = 0.45f;
                    EditorGUI.DrawRect(stateRect, highlightColor);
                }
                var settings = AnimatorDefaultSettings.Load();
                if (!string.IsNullOrEmpty(state.tag))
                {
                    var nodeTagColor = AnimatorDefaultSettings.GetTagColor(state.tag, settings);
                    if (nodeTagColor.HasValue)
                    {
                        var drawColor = nodeTagColor.Value;
                        drawColor.a = 0.85f;
                        var stripRect = new Rect(
                            stateRect.x + stateRect.width * 0.25f,
                            stateRect.y - 29f,
                            stateRect.width * 0.5f,
                            4f);
                        EditorGUI.DrawRect(stripRect, drawColor);
                    }
                }
                if (!isRenaming)
                    DrawNodeNameLabel(state, stateRect, settings);
                var graphPosition = Vector2.zero;
                if (settings.overlayEnabled && settings.overlayShowCoords)
                {
                    if (_nodeGraphInvoker == null)
                        _nodeGraphInvoker = MethodInvoker.GetHandler(AccessTools.Method(__instance.GetType(), "get_graph"));
                    var graph = _nodeGraphInvoker?.Invoke(__instance);
                    if (graph != null)
                    {
                        if (_activeStateMachineInvoker == null)
                            _activeStateMachineInvoker = MethodInvoker.GetHandler(AccessTools.Method(graph.GetType(), "get_activeStateMachine"));
                        var activeSM = _activeStateMachineInvoker?.Invoke(graph) as AnimatorStateMachine;
                        if (activeSM != null)
                        {
                            bool stale = activeSM != _positionCacheSM
                                      || EditorApplication.timeSinceStartup - _positionCacheTime > 0.02;
                            if (stale)
                            {
                                _positionCacheSM   = activeSM;
                                _positionCacheTime = EditorApplication.timeSinceStartup;
                                _positionCache.Clear();
                                foreach (var childState in activeSM.states)
                                    _positionCache[childState.state] = new Vector2(childState.position.x, childState.position.y);
                            }
                            _positionCache.TryGetValue(state, out graphPosition);
                        }
                    }
                }
                if (settings.overlayEnabled)
                    DrawIndicators(state, stateRect, settings, graphPosition);
                if (isRenaming)
                    DrawRenameField(state, stateRect);
                if (isRenamingMotion)
                    DrawMotionRenameField(state, stateRect);
            }
            catch (Exception e) { Debug.LogError($"[YGDR] State node overlay error: {e}"); }
        }

        static void DrawIndicators(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings, Vector2 graphPosition)
        {
            var previousContentColor = GUI.contentColor;

            // Left-anchored  Rect(nodeRect.x + offsetX, nodeRect.y + offsetY, width, height)
            bool hasMotion = state.motion != null;

            if (settings.overlayShowLoopEmpty)
            {
                var loopRect = new Rect(nodeRect.x + 2f, nodeRect.y + -26f, 16f, 15f);
                if (hasMotion)
                {
                    if (state.motion is BlendTree)
                    {
                        GUI.contentColor = settings.overlayActiveColor;
                        GUI.Label(loopRect, BlendTreeIcon, AnimatorStyles.LoopStyle);
                    }
                    else
                    {
                        GUI.contentColor = IsLooping(state.motion) ? settings.overlayActiveColor : settings.overlayInactiveColor;
                        GUI.Label(loopRect, LoopIcon, AnimatorStyles.LoopStyle);
                    }
                }
                else
                {
                    GUI.contentColor = settings.overlayActiveColor;
                    GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.y + -28f, 14f, 15f), "!", AnimatorStyles.IndicatorStyle);
                }
            }

            if (settings.overlayShowClipTime && hasMotion && state.motion is AnimationClip clipForTime)
            {
                var hasBindings = AnimationUtility.GetCurveBindings(clipForTime).Length > 0 || AnimationUtility.GetObjectReferenceCurveBindings(clipForTime).Length > 0;
                var totalSeconds = hasBindings ? clipForTime.length : 0f;
                var minutes = (int)(totalSeconds / 60f);
                var remainingSeconds = totalSeconds - minutes * 60f;
                var tenths = Mathf.RoundToInt(remainingSeconds * 10f);
                var clipTimeText = tenths % 10 == 0 ? $"{minutes}m{tenths / 10}s" : $"{minutes}m{tenths / 10}.{tenths % 10}s";
                GUI.contentColor = settings.overlayActiveColor;
                GUI.Label(new Rect(nodeRect.x + 20f, nodeRect.y + -27f, 50f, 15f), clipTimeText, AnimatorStyles.ClipTimeStyle);
            }

            // Right-anchored  Rect(nodeRect.x + nodeRect.width + offsetX, nodeRect.y + offsetY, width, height)  (offsetX is negative)
            if (settings.overlayShowB)
            {
                GUI.contentColor = state.behaviours.Length > 0 ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -28f, 13f, 15f), "B",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowWD)
            {
                GUI.contentColor = state.writeDefaultValues ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -28f, 22f, 15f), "WD", AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowSpeed)
            {
                GUI.contentColor = state.speedParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -5f, 13f, 15f), "S",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowMotion)
            {
                GUI.contentColor = state.timeParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -5f, 22f, 15f), "M",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowMotionName && MotionRenameState.RenameTargetState != state)
            {
                string label = state.motion != null ? $"[{state.motion.name}]" : "[none]";
                GUI.contentColor = state.motion != null ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x, nodeRect.y + -6f, nodeRect.width, 13f), label, AnimatorStyles.MotionNameStyle);
            }

            if (settings.overlayShowCoords)
            {
                GUI.contentColor = settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.yMax - 13f, nodeRect.width - 4f, 13f),
                    $"({(int)graphPosition.x},{(int)graphPosition.y})", AnimatorStyles.CoordsStyle);
            }

            GUI.contentColor = previousContentColor;
        }

        static bool IsLooping(Motion motion) => motion is AnimationClip clip && clip.isLooping;

        static FastInvokeHandler   _nodeGraphInvoker;
        static FastInvokeHandler   _activeStateMachineInvoker;
        static GUIContent          _blendTreeIcon;
        static GUIContent          _loopIcon;
        static GUIContent BlendTreeIcon => _blendTreeIcon ??= EditorGUIUtility.IconContent("d_BlendTree Icon");
        static GUIContent LoopIcon      => _loopIcon      ??= EditorGUIUtility.IconContent("d_preaudioloopoff@2x");

        static AnimatorStateMachine                        _positionCacheSM;
        static double                                      _positionCacheTime;
        static readonly Dictionary<AnimatorState, Vector2> _positionCache = new();

        static AnimatorState GetState(object node) =>
            GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;

        static void DrawNodeNameLabel(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings)
        {
            var previousContentColor = GUI.contentColor;
            GUI.contentColor = settings.overlayActiveColor;
            GUI.Label(new Rect(nodeRect.x, nodeRect.y - 25f, nodeRect.width, 20f), state.name, AnimatorStyles.NodeNameStyle);
            GUI.contentColor = previousContentColor;
        }

        static bool _renameFieldHadFocus;

        static void DrawRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "StateRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 24f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            if (StateRenameState.JustStarted)
            {
                GUI.SetNextControlName(controlName);
                EditorGUI.TextField(fieldRect, StateRenameState.RenameText, AnimatorStyles.RenameFieldStyle);
                EditorGUI.FocusTextInControl(controlName);
                StateRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            // Check Enter/Escape before TextField so Unity's internal handling can't consume them
            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    StateRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    StateRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            StateRenameState.RenameText = EditorGUI.TextField(fieldRect, StateRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (!_renameFieldHadFocus && hasFocus)
            {
                var textEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                if (textEditor != null)
                {
                    textEditor.SelectAll();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    _renameFieldHadFocus = true;
                }
                // else: keep false, retry next frame when keyboardControl is set
            }
            else
            {
                if (_renameFieldHadFocus && !hasFocus)
                    StateRenameState.Apply();
                _renameFieldHadFocus = hasFocus;
            }
        }

        static bool _motionRenameFieldHadFocus;

        static void DrawMotionRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "MotionRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 6f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            if (MotionRenameState.JustStarted)
            {
                GUI.SetNextControlName(controlName);
                EditorGUI.TextField(fieldRect, MotionRenameState.RenameText, AnimatorStyles.RenameFieldStyle);
                EditorGUI.FocusTextInControl(controlName);
                MotionRenameState.JustStarted = false;
                _motionRenameFieldHadFocus = false;
                return;
            }

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    MotionRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    MotionRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            MotionRenameState.RenameText = EditorGUI.TextField(fieldRect, MotionRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            bool hasFocusMotion = GUI.GetNameOfFocusedControl() == controlName;
            if (_motionRenameFieldHadFocus && !hasFocusMotion)
                MotionRenameState.Apply();
            _motionRenameFieldHadFocus = hasFocusMotion;
        }

        // Layer 2: swallow exceptions from conflicting transpilers on this hot path to prevent GUI lockup
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Debug.LogError($"[AnimatorTools] Exception in NodeUI — disable conflicting feature in Compatibility settings: {__exception.Message}");
            return null;
        }
    }

    // ─── Special node rect storage (for transition overlay) ─────────────────────────────────
    internal static class SpecialNodeRects
    {
        internal static Rect AnyState;
        internal static Rect Entry;
        internal static Rect Exit;
        internal static readonly Dictionary<AnimatorStateMachine, Rect> SubSMs = new();

        internal static Vector2 AnyStateScreen;
        internal static Vector2 EntryScreen;
        internal static Vector2 ExitScreen;
        internal static readonly Dictionary<AnimatorStateMachine, Vector2> SubSMScreens = new();
    }

    // ─── Entry / Exit / Any State nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorEntryNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.EntryNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Entry = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.EntryScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }
    }

    [HarmonyPatch]
    internal static class AnimatorExitNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.ExitNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Exit = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.ExitScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }
    }

    [HarmonyPatch]
    internal static class AnimatorAnyStateNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.AnyState = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.AnyStateScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }
    }

    // ─── Sub state machine nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorSubSMNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateMachineNodeType, "NodeUI");

        static bool _renameFieldHadFocus;

        const float HighlightWidth   = 170f;
        const float HighlightHeight  = 10f;
        const float HighlightOffsetX = 15f;
        const float HighlightOffsetY = 30f;

        static AnimatorStateMachine GetStateMachine(object node) =>
            GraphPatchReflection.StateMachineNodeStateMachineField?.GetValue(node) as AnimatorStateMachine;

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var sm = GetStateMachine(__instance);
                if (sm == null) return;

                var nodeLocalRect = new Rect(HighlightOffsetX, HighlightOffsetY, HighlightWidth, HighlightHeight);
                SpecialNodeRects.SubSMs[sm] = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.Repaint)
                {
                    SpecialNodeRects.SubSMScreens[sm] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
                    if (AnimatorGraphAnalyzer.HighlightedSubStateMachines.Contains(sm))
                    {
                        var highlightColor = AnimatorGraphAnalyzer.HighlightColor;
                        highlightColor.a = 0.45f;
                        EditorGUI.DrawRect(nodeLocalRect, highlightColor);
                    }
                }

                if (SubSMRenameState.RenameTarget != sm) return;
                DrawRenameField();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] AnimatorSubSMNodeOverlayPatch.Postfix: {e}"); }
        }

        static void DrawRenameField()
        {
            const string controlName = "SubSMRenameField";
            // NodeUI has no GUILayout content, draw in local window coords, title bar area at y < 0, content at y >= 0
            var fieldRect = new Rect(2f, 10f, 196f, 17f);
            var currentEvent = Event.current;

            if (SubSMRenameState.JustStarted)
            {
                GUI.SetNextControlName(controlName);
                EditorGUI.TextField(fieldRect, SubSMRenameState.RenameText, AnimatorStyles.RenameFieldStyle);
                EditorGUI.FocusTextInControl(controlName);
                SubSMRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    SubSMRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    SubSMRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            SubSMRenameState.RenameText = EditorGUI.TextField(fieldRect, SubSMRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                SubSMRenameState.Apply();
            _renameFieldHadFocus = hasFocus;
        }
    }


    // ──── State rename state ────────────────────────────────────────────────────────────────────

    internal static class StateRenameState
    {
        internal static AnimatorState RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;
        static AnimatorState[] _additionalTargets;
        static AnimatorStateMachine _activeSM;

        internal static void Begin(AnimatorState state, AnimatorState[] additionalTargets = null, AnimatorStateMachine activeSM = null)
        {
            RenameTarget        = state;
            RenameText          = state.name;
            JustStarted         = true;
            _additionalTargets  = additionalTargets;
            _activeSM           = activeSM;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            string baseName = RenameText?.Trim() ?? "";

            if (_additionalTargets == null || _additionalTargets.Length == 0)
            {
                AnimatorStateOps.RenameState(RenameTarget, baseName);
            }
            else
            {
                var allSelected = new HashSet<AnimatorState>(_additionalTargets) { RenameTarget };
                var existingNames = new HashSet<string>();
                if (_activeSM != null)
                    foreach (var childState in _activeSM.states)
                        if (!allSelected.Contains(childState.state))
                            existingNames.Add(childState.state.name);

                existingNames.Add(baseName);
                AnimatorStateOps.RenameState(RenameTarget, baseName);

                int n = 1;
                foreach (var additionalState in _additionalTargets)
                {
                    string candidate;
                    do { candidate = baseName + " " + n++; } while (existingNames.Contains(candidate));
                    existingNames.Add(candidate);
                    AnimatorStateOps.RenameState(additionalState, candidate);
                }
            }

            RenameTarget       = null;
            RenameText         = null;
            _additionalTargets = null;
            _activeSM          = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget       = null;
            RenameText         = null;
            _additionalTargets = null;
            _activeSM          = null;
        }
    }

    internal static class SubSMRenameState
    {
        internal static AnimatorStateMachine RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Starts an inline rename session for stateMachine, seeding the text field with the current name. */
        internal static void Begin(AnimatorStateMachine stateMachine)
        {
            RenameTarget   = stateMachine;
            RenameText     = stateMachine.name;
            JustStarted    = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameStateMachine(RenameTarget, RenameText);
            RenameTarget = null;
            RenameText   = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget   = null;
            RenameText     = null;
        }
    }

    internal static class MotionRenameState
    {
        internal static Motion RenameTarget;
        internal static AnimatorState RenameTargetState;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Starts an inline rename session for motion associated with state, seeding the text field with the current motion name. */
        internal static void Begin(Motion motion, AnimatorState state)
        {
            RenameTarget      = motion;
            RenameTargetState = state;
            RenameText        = motion.name;
            JustStarted       = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameMotion(RenameTarget, RenameText);
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }
    }

    // ─── Suppress built-in title label ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class PatchStateNodeTitle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "get_title");

        [HarmonyPostfix]
        static void Postfix(ref string __result) => __result = "";
    }

}
#endif
