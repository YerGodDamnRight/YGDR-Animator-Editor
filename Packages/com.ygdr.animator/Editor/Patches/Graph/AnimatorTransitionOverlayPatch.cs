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
    // ── Transition line color + animated arrow ────────────────────────────────

    [HarmonyPatch]
    internal static class PatchDrawEdge
    {
        const float LabelOffsetAbove = 10f;
        const float LabelOffsetBelow = -25f;
        const float LabelOffsetSelfTransition = 40f;


        static FastInvokeHandler _drawArrowInvoker;

        internal static Vector3 GetAnimatedArrowPosition(Vector3 source, Vector3 midpoint, Vector3 destination)
        {
            float progress = (float)(EditorApplication.timeSinceStartup * 0.5 % 1.0);
            return progress < 0.5f
                ? Vector3.Lerp(midpoint, destination, progress * 2f)
                : Vector3.Lerp(source, midpoint, (progress - 0.5f) * 2f);
        }
        static FastInvokeHandler _edgeSizeMultiplierInvoker;
        static FastInvokeHandler _fromSlotInvoker;
        static FastInvokeHandler _toSlotInvoker;
        static FastInvokeHandler _slotNodeInvoker;
        static FieldInfo         _labelTransitionsField;
        static FieldInfo         _labelTransitionContextField;
        static EditorWindow      _cachedAnimatorWindow;
        static Func<Rect>        _getVisibleRect;
        static bool              _showExpandedBox;
        static (object info, Vector2 midPoint, Vector2 direction)? _pendingExpandedBox;

        internal static void DrawExpandedBox()  => _showExpandedBox = true;
        internal static void ClearExpandedBox() => _showExpandedBox = false;

        internal static void FlushExpandedBox()
        {
            if (_pendingExpandedBox == null) return;
            var (info, midPoint, direction) = _pendingExpandedBox.Value;
            _pendingExpandedBox = null;
            DrawExpandedConditionsBox(info, midPoint, direction);
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DrawEdgeMethod;

        // __state: 0 = entry (skip postfix), 1 = selected, 2 = normal
        [HarmonyPrefix]
        static void Prefix(object edge, ref Color color, object info, ref int __state)
        {
            __state = 0;
            try
            {
                if (AnimatorGraphAnalyzer.HighlightedTransitions.Count > 0 && IsEdgeHighlightedForAnalysis(info))
                {
                    color = AnimatorGraphAnalyzer.HighlightColor;
                    return;
                }
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.transitionOverlayEnabled) return;
                if (IsEntryEdge(edge)) return;
                bool selected = color.b > color.r + 0.15f;
                __state = selected ? 1 : 2;
                if (!selected)
                {
                    var inOutColor = ResolveInOutColor(edge, settings);
                    color = inOutColor ?? GetTagColorFromInfo(info, settings) ?? settings.transitionOverlayColor;
                }
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge prefix error: {e}"); }
        }

        static bool IsEdgeHighlightedForAnalysis(object info)
        {
            if (info == null) return false;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return false;
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                if (_labelTransitionContextField?.GetValue(transitionContext) is AnimatorStateTransition stateTransition
                    && AnimatorGraphAnalyzer.HighlightedTransitions.Contains(stateTransition))
                    return true;
            }
            return false;
        }

        /* Returns the incoming or outgoing highlight color when exactly one state node matching the current selection is on either end of edge, or null to use the default line color. */
        static Color? ResolveInOutColor(object edge, AnimatorDefaultSettings settings)
        {
            if (!settings.transitionSelectionColorEnabled) return null;
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length != 1) return null;

            if (_fromSlotInvoker == null)
                _fromSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "fromSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_fromSlot"));
            if (_toSlotInvoker == null)
                _toSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "toSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_toSlot"));

            var fromSlot = _fromSlotInvoker?.Invoke(edge);
            var toSlot   = _toSlotInvoker?.Invoke(edge);
            if (fromSlot == null || toSlot == null) return null;

            if (_slotNodeInvoker == null)
                _slotNodeInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(fromSlot.GetType(), "node") ?? AccessTools.Method(fromSlot.GetType(), "get_node"));

            var fromNode = _slotNodeInvoker?.Invoke(fromSlot);
            var toNode   = _slotNodeInvoker?.Invoke(toSlot);

            if (IsNodeMatchingSelection(fromNode, selectedObjects)) return settings.transitionOutgoingColor;
            if (IsNodeMatchingSelection(toNode, selectedObjects))   return settings.transitionIncomingColor;
            return null;
        }

        /* Returns true if node is a StateNode or StateMachineNode whose underlying asset is present in selectedObjects. */
        static bool IsNodeMatchingSelection(object node, UnityEngine.Object[] selectedObjects)
        {
            if (node == null) return false;
            if (AnimatorEditorInit.StateNodeType.IsInstanceOfType(node))
            {
                var state = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                return state != null && System.Array.IndexOf(selectedObjects, state) >= 0;
            }
            if (AnimatorEditorInit.StateMachineNodeType.IsInstanceOfType(node))
            {
                var stateMachine = AnimatorEditorInit.SMNodeStateMachineField?.GetValue(node) as AnimatorStateMachine;
                return stateMachine != null && System.Array.IndexOf(selectedObjects, stateMachine) >= 0;
            }
            // AnyState/Exit/Entry nodes have no underlying asset — Unity puts the node ScriptableObject itself in Selection.objects
            if (node is UnityEngine.Object nodeObject)
                return System.Array.IndexOf(selectedObjects, nodeObject) >= 0;
            return false;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, object edge, object info, int __state)
        {
            if (__state == 0) return;
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                bool animate = settings.transitionAnimateSelected && (__state == 1 || IsNodeSelected(edge));
                if (!settings.transitionShowLabel && !animate && !_showExpandedBox) return;

                var args = new object[] { edge, Vector3.zero };
                var points = GraphPatchReflection.GetEdgePointsMethod?.Invoke(__instance, args) as Vector3[];
                if (points == null || points.Length < 2) return;
                var cross = (Vector3)args[1];

                var sourcePoint      = points[0];
                var destinationPoint = points[points.Length - 1];
                var midPoint         = Vector3.Lerp(sourcePoint, destinationPoint, 0.5f);
                var direction        = (destinationPoint - sourcePoint).normalized;

                if (__state == 1 && _showExpandedBox)
                    _pendingExpandedBox = (info, (Vector2)midPoint, (Vector2)direction);
                else if (settings.transitionShowLabel)
                {
                    var label = BuildLabel(info);
                    if (label != null) DrawLabel((Vector2)midPoint, (Vector2)direction, label);
                }

                if (!animate) return;

                 _edgeSizeMultiplierInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.EdgeSizeMultiplierGetter);
                 float mult         = _edgeSizeMultiplierInvoker != null ? (float)_edgeSizeMultiplierInvoker(__instance) : 1f;
                 float arrowSize    = 5f * mult;
                 float outlineWidth = 2f * mult;

                 var arrowColor = settings.transitionIndicatorArrowsEnabled
                     ? PatchDrawArrows.GetOrResolveArrowColor(info, settings) ?? settings.transitionOverlayColor
                     : settings.transitionOverlayColor;

                 var animatedPosition = GetAnimatedArrowPosition(sourcePoint, midPoint, destinationPoint);

                 _drawArrowInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.DrawArrowMethod);
                 _drawArrowInvoker?.Invoke(null, arrowColor, cross, direction, animatedPosition, arrowSize, outlineWidth);

                 if (_cachedAnimatorWindow == null)
                     _cachedAnimatorWindow = Resources
                         .FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType)
                         .FirstOrDefault() as EditorWindow;
                 _cachedAnimatorWindow?.Repaint();
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge postfix error: {e}"); }
        }

        /* Reads the transitions list from the edge info object and returns a one-line label: condition summary, "N Conditions", "Invalid", or null to show nothing. */
        static string BuildLabel(object info)
        {
            if (info == null) return null;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;

            var stateTransitions = new List<AnimatorStateTransition>();
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                if (_labelTransitionContextField?.GetValue(transitionContext) is AnimatorStateTransition stateTransition)
                    stateTransitions.Add(stateTransition);
            }
            if (stateTransitions.Count == 0) return null;

            if (stateTransitions.Any(x => !x.hasExitTime && (x.conditions == null || x.conditions.Length == 0)))
                return L10n.Get("transition_overlay.invalid");

            if (stateTransitions.Count == 1 && stateTransitions[0].conditions?.Length == 1)
                return FormatCondition(stateTransitions[0].conditions[0]);

            int total = stateTransitions.Sum(x => x.conditions?.Length ?? 0);
            return L10n.Get("transition_overlay.n_conditions").Replace("{n}", total.ToString());
        }

        static readonly string[] GestureNames =
        {
            "Neutral", "Fist", "OpenHand", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp"
        };

        /* Returns a short human-readable string for a single condition (e.g. "Param > 0.5", "Flag = True"), truncating parameter names over 16 chars. */
        static string FormatCondition(AnimatorCondition animatorCondition, bool truncate = true)
        {
            var parameterLabel = truncate && animatorCondition.parameter.Length > 16 ? animatorCondition.parameter[..16] + "…" : animatorCondition.parameter;
            return animatorCondition.mode switch
            {
                AnimatorConditionMode.If       => $"{parameterLabel} = True",
                AnimatorConditionMode.IfNot    => $"{parameterLabel} = False",
                AnimatorConditionMode.Greater  => $"{parameterLabel} > {animatorCondition.threshold:0.##}",
                AnimatorConditionMode.Less     => $"{parameterLabel} < {animatorCondition.threshold:0.##}",
                AnimatorConditionMode.Equals   => $"{parameterLabel} = {FormatIntThreshold(animatorCondition)}",
                AnimatorConditionMode.NotEqual => $"{parameterLabel} ≠ {FormatIntThreshold(animatorCondition)}",
                _ => parameterLabel
            };
        }

        /* Returns the integer threshold as a string, appending the gesture name in parentheses when the parameter is GestureLeft or GestureRight. */
        static string FormatIntThreshold(AnimatorCondition animatorCondition)
        {
            int intValue = (int)animatorCondition.threshold;
            if ((animatorCondition.parameter == "GestureLeft" || animatorCondition.parameter == "GestureRight")
                && intValue >= 0 && intValue < GestureNames.Length)
                return $"{intValue} ({GestureNames[intValue]})";
            return intValue.ToString();
        }

        /* Draws text rotated to follow the edge direction at mid-point, offsetting above or below the line based on the horizontal component of dir. Self-transitions (zero dir) use LabelOffsetBelow to place the label clear of the node. */
        static void DrawLabel(Vector2 mid, Vector2 dir, string text)
        {
            bool isSelfTransition = dir.sqrMagnitude < 0.001f;
            float yOffset = isSelfTransition ? LabelOffsetSelfTransition : (dir.x >= 0f ? LabelOffsetAbove : LabelOffsetBelow);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle > 90f)  angle -= 180f;
            if (angle < -90f) angle += 180f;

            if (_getVisibleRect == null)
            {
                var guiClipType = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                var prop = guiClipType?.GetProperty("visibleRect",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                _getVisibleRect = prop != null
                    ? (Func<Rect>)Delegate.CreateDelegate(typeof(Func<Rect>), prop.GetGetMethod(nonPublic: true))
                    : static () => new Rect(0, 0, 9999, 9999);
            }
            var clipRect = _getVisibleRect();

            var localMid = mid - clipRect.position;
            var matrix = GUI.matrix;
            GUI.BeginClip(clipRect);
            GUIUtility.RotateAroundPivot(angle, localMid);
            GUI.Label(new Rect(localMid.x - 75f, localMid.y + yOffset, 150f, 14f), text, AnimatorStyles.TransitionEdgeLabelStyle);
            GUI.matrix = matrix;
            GUI.EndClip();
        }

        static void DrawExpandedConditionsBox(object info, Vector2 midPoint, Vector2 direction)
        {
            if (info == null) return;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return;

            var conditionLines = new List<string>();
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                if (_labelTransitionContextField?.GetValue(transitionContext) is not AnimatorStateTransition stateTransition) continue;

                if (!stateTransition.hasExitTime && (stateTransition.conditions == null || stateTransition.conditions.Length == 0))
                {
                    conditionLines.Add(L10n.Get("transition_overlay.invalid"));
                    continue;
                }
                if (stateTransition.conditions != null)
                {
                    foreach (var condition in stateTransition.conditions)
                        conditionLines.Add(FormatCondition(condition, truncate: false));
                }
            }

            if (conditionLines.Count == 0) return;

            const float boxWidth   = 200f;
            const float lineHeight = 16f;
            const float padding    = 5f;
            float boxHeight = conditionLines.Count * lineHeight + padding * 2f;
            bool isSelfTransition = direction.sqrMagnitude < 0.001f;
            float yOffset = isSelfTransition ? 60f : -(boxHeight + 14f);

            if (_getVisibleRect == null)
            {
                var guiClipType = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                var prop = guiClipType?.GetProperty("visibleRect",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                _getVisibleRect = prop != null
                    ? (Func<Rect>)Delegate.CreateDelegate(typeof(Func<Rect>), prop.GetGetMethod(nonPublic: true))
                    : static () => new Rect(0, 0, 9999, 9999);
            }
            var clipRect = _getVisibleRect();
            var localMid = midPoint - clipRect.position;
            var boxRect  = new Rect(localMid.x - boxWidth * 0.5f, localMid.y + yOffset, boxWidth, boxHeight);

            GUI.BeginClip(clipRect);
            EditorGUI.DrawRect(boxRect, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            for (int i = 0; i < conditionLines.Count; i++)
            {
                var lineRect = new Rect(boxRect.x + padding, boxRect.y + padding + i * lineHeight, boxWidth - padding * 2f, lineHeight);
                GUI.Label(lineRect, conditionLines[i], AnimatorStyles.TransitionEdgeLabelStyle);
            }
            GUI.EndClip();
        }

        /* Returns true if the source slot of edge belongs to an EntryNode, used to skip entry transitions that should not be re-coloured. */
        static bool IsEntryEdge(object edge)
        {
            if (_fromSlotInvoker == null)
                _fromSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "fromSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_fromSlot"));
            var slot = _fromSlotInvoker?.Invoke(edge);
            if (slot == null) return false;
            if (_slotNodeInvoker == null)
                _slotNodeInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(slot.GetType(), "node") ?? AccessTools.Method(slot.GetType(), "get_node"));
            var node = _slotNodeInvoker?.Invoke(slot);
            return node != null && AnimatorEditorInit.EntryNodeType.IsInstanceOfType(node);
        }

        /* Returns true if either the source or destination StateNode of edge contains a state that is in the current selection, used to trigger animated arrow drawing. */
        static bool IsNodeSelected(object edge)
        {
            try
            {
                if (_fromSlotInvoker == null)
                    _fromSlotInvoker = MethodInvoker.GetHandler(
                        AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "fromSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_fromSlot"));
                if (_toSlotInvoker == null)
                    _toSlotInvoker = MethodInvoker.GetHandler(
                        AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "toSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_toSlot"));

                var fromSlotForType = _fromSlotInvoker?.Invoke(edge);
                if (_slotNodeInvoker == null && fromSlotForType != null)
                    _slotNodeInvoker = MethodInvoker.GetHandler(
                        AccessTools.PropertyGetter(fromSlotForType.GetType(), "node")
                        ?? AccessTools.Method(fromSlotForType.GetType(), "get_node"));

                var selected = Selection.objects;
                foreach (var slot in new[] { fromSlotForType, _toSlotInvoker?.Invoke(edge) })
                {
                    if (slot == null) continue;
                    var node = _slotNodeInvoker?.Invoke(slot);
                    if (IsNodeMatchingSelection(node, selected)) return true;
                }
            }
            catch { }
            return false;
        }

        // Layer 2: swallow exceptions from conflicting transpilers on this hot path to prevent GUI lockup
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Debug.LogError($"[AnimatorTools] Exception in DrawEdge — disable conflicting feature in Compatibility settings: {__exception.Message}");
            return null;
        }

        static Color? GetTagColorFromInfo(object info, AnimatorDefaultSettings settings)
        {
            if (info == null || settings.colorTags.Count == 0) return null;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                if (_labelTransitionContextField?.GetValue(transitionContext) is AnimatorStateTransition stateTransition)
                    return AnimatorDefaultSettings.GetTagColor(stateTransition.name, settings);
            }
            return null;
        }
    }

    /* ── Transition arrow color ────────────────────────────────────────────────
     Intercepts DrawArrows to apply condition-based arrow color independently
     from the line color. Reflects into EdgeInfo.transitions to read each
     AnimatorStateTransition — entry edges (AnimatorTransition only) are skipped
     naturally. Color persists through selection.
       anyInvalid   — any transition has no conditions AND no exit time
       allInstant — any transition has duration == 0
       Default — transitionOverlayArrowColor
    */

    [HarmonyPatch]
    internal static class PatchDrawArrows
    {
        static readonly Dictionary<Type, FieldInfo> _transitionsFields = new();
        static readonly Dictionary<Type, FieldInfo> _transitionFields = new();

        // Frame-level cache: ResolveArrowColor called twice per edge per repaint (DrawArrows.Prefix + DrawEdge.Postfix)
        // info objects are stable within a repaint pass; color is selection-independent so safe to cache
        static readonly Dictionary<object, Color?> _arrowColorCache = new();
        static int _arrowColorCacheFrame = -1;

        internal static Color? GetOrResolveArrowColor(object info, AnimatorDefaultSettings settings)
        {
            if (info == null) return null;
            int currentFrame = Time.frameCount;
            if (_arrowColorCacheFrame != currentFrame)
            {
                _arrowColorCache.Clear();
                _arrowColorCacheFrame = currentFrame;
            }
            if (_arrowColorCache.TryGetValue(info, out var cached)) return cached;
            var result = ResolveArrowColor(info, settings);
            _arrowColorCache[info] = result;
            return result;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DrawArrowsMethod;

        [HarmonyPrefix]
        static void Prefix(ref Color color, object info)
        {
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.transitionOverlayEnabled || !settings.transitionIndicatorArrowsEnabled || info == null) return;
                var resolved = GetOrResolveArrowColor(info, settings);
                if (resolved.HasValue)
                    color = resolved.Value;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawArrows prefix error: {e}"); }
        }

        /* Inspects all AnimatorStateTransitions in info to determine arrow color: red for any invalid transition, green when all transitions are instant, default arrow color otherwise. */
        internal static Color? ResolveArrowColor(object info, AnimatorDefaultSettings settings)
        {
            if (info == null) return null;
            var infoType = info.GetType();
            if (!_transitionsFields.TryGetValue(infoType, out var transitionsField))
                _transitionsFields[infoType] = transitionsField = AccessTools.Field(infoType, "transitions");
            var transitions = transitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;

            bool anyArrowInvalid  = false;
            bool allArrowInstant = true;
            bool hasStateTransition = false;
            string firstTagName = null;

            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                var transitionContextType = transitionContext.GetType();
                if (!_transitionFields.TryGetValue(transitionContextType, out var transitionField))
                    _transitionFields[transitionContextType] = transitionField = AccessTools.Field(transitionContextType, "transition");
                if (transitionField?.GetValue(transitionContext) is not AnimatorStateTransition stateTransition) continue;

                hasStateTransition = true;
                firstTagName ??= stateTransition.name;
                bool hasConditions = stateTransition.conditions != null && stateTransition.conditions.Length > 0;
                bool isValid = stateTransition.hasExitTime || hasConditions;
                if (!isValid) anyArrowInvalid = true;
                if (stateTransition.duration != 0f) allArrowInstant = false;
            }

            if (!hasStateTransition) return null;
            if (anyArrowInvalid) return settings.transitionArrowNoConditionColor;
            if (allArrowInstant) return settings.transitionArrowInstantColor;
            return AnimatorDefaultSettings.GetTagColor(firstTagName, settings) ?? settings.transitionOverlayArrowColor;
        }
    }
}
#endif
