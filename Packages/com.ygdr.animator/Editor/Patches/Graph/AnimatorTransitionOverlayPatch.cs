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
        static FastInvokeHandler _drawArrowsInvoker;

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
        static bool              _wasAltHeld;
        static (object info, Vector2 midPoint, Vector2 direction)? _pendingExpandedBox;

        internal static void FlushExpandedBox()
        {
            if (_pendingExpandedBox == null) return;
            var (info, midPoint, direction) = _pendingExpandedBox.Value;
            _pendingExpandedBox = null;
            DrawExpandedConditionsBox(info, midPoint, direction);
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DrawEdgeMethod;

        // __state: 0 = overlay disabled (skip postfix), 1 = selected, 2 = normal; bit 4 = gradient color applied this edge; bit 8 = manual path edge
        const int GradientAppliedFlag = 4;
        const int ManualPathFlag = 8;

        static TransitionPathData _pathDataCache;
        static int _pathDataCacheFrame = -1;
        static TransitionPathEntry _pendingManualEntry;
        static Vector3 _pendingSourceCenter;
        static Vector3 _pendingDestCenter;
        static Vector3[] _scratchPoints = new Vector3[8];
        static readonly Color HoveredHandleColor = new Color(0.239f, 0.502f, 0.878f); // Unity accent blue

        [HarmonyPrefix]
        static bool Prefix(object edge, ref Color color, object info, ref int __state)
        {
            __state = 0;
            try
            {
                if (AnimatorGraphAnalyzer.HighlightedTransitions.Count > 0 && IsEdgeHighlightedForAnalysis(info))
                {
                    color = AnimatorGraphAnalyzer.HighlightColor;
                    return TryApplyManualPath(edge, ref __state);
                }
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.transitionOverlayEnabled) return TryApplyManualPath(edge, ref __state);
                if (IsEntryEdge(edge)) { __state = color.b > color.r + 0.15f ? 1 : 2; return TryApplyManualPath(edge, ref __state); }
                bool selected = color.b > color.r + 0.15f;
                __state = selected ? 1 : 2;
                if (!selected)
                {
                    var direction = ResolveInOutDirection(edge, settings);
                    color = direction switch
                    {
                        InOutDirection.Outgoing => settings.transitionGradientEnabled
                            ? ResolveGradientColor(edge, settings.transitionGradientOutColorA, settings.transitionGradientOutColorB, settings.transitionGradientOutSpeed)
                            : settings.transitionOutgoingColor,
                        InOutDirection.Incoming => settings.transitionGradientEnabled
                            ? ResolveGradientColor(edge, settings.transitionGradientInColorA, settings.transitionGradientInColorB, settings.transitionGradientInSpeed)
                            : settings.transitionIncomingColor,
                        _ => GetTagColorFromInfo(info, settings) ?? settings.transitionOverlayColor
                    };
                    if (settings.transitionGradientEnabled && direction != InOutDirection.None)
                        __state |= GradientAppliedFlag;
                }
                return TryApplyManualPath(edge, ref __state);
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge prefix error: {e}"); }
            return true;
        }

        /* Used by PatchFindClosestEdge to discard native's click-selection pick for a manual-path edge — native's
           FindClosestEdge hit-tests distance to the (now invisible) straight source-dest line via GetEdgePoints,
           so it can never see our bent geometry and must not be trusted for these edges. */
        internal static bool IsManualPathEdge(object edge)
        {
            var (fromState, fromSpecial, toState, toSpecial, _, _) = ResolveEdgeStates(edge);
            var pathData = GetPathDataForFrame(fromState ?? toState);
            return pathData != null && pathData.TryGetEntry(fromState, toState, fromSpecial, toSpecial) != null;
        }

        /* Resolves the edge's (fromState, toState) pair and, if a manual path entry exists for it, sets
           ManualPathFlag on __state, stashes the entry for the immediately-following Postfix (draws are
           sequential/non-reentrant within one repaint), and returns false to skip the native line draw. */
        static bool TryApplyManualPath(object edge, ref int __state)
        {
            var (fromState, fromSpecial, toState, toSpecial, fromCenter, toCenter) = ResolveEdgeStates(edge);
            var pathData = GetPathDataForFrame(fromState ?? toState);
            if (pathData == null || pathData.entries.Count == 0) { _pendingManualEntry = null; return true; }
            var entry = pathData.TryGetEntry(fromState, toState, fromSpecial, toSpecial);
            if (entry == null) { _pendingManualEntry = null; return true; }
            __state |= ManualPathFlag;
            _pendingManualEntry = entry;
            _pendingSourceCenter = fromCenter;
            _pendingDestCenter = toCenter;
            return false;
        }

        /* Resolves the AnimatorState (and, for AnyState/Entry/Exit, the SpecialNode kind) on each side of edge via
           fromSlot/toSlot -> node -> state field reflection, plus each side's node-box center in graph space — used
           instead of the native angled boundary anchor so a manual path's endpoints can sit at the node's exact
           center (needed to get a perfectly straight horizontal run). */
        static (AnimatorState fromState, SpecialNode fromSpecial, AnimatorState toState, SpecialNode toSpecial, Vector3 fromCenter, Vector3 toCenter) ResolveEdgeStates(object edge)
        {
            if (_fromSlotInvoker == null)
                _fromSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "fromSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_fromSlot"));
            if (_toSlotInvoker == null)
                _toSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "toSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_toSlot"));

            var fromSlot = _fromSlotInvoker?.Invoke(edge);
            var toSlot   = _toSlotInvoker?.Invoke(edge);
            if (fromSlot == null || toSlot == null) return (null, SpecialNode.None, null, SpecialNode.None, default, default);

            if (_slotNodeInvoker == null)
                _slotNodeInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(fromSlot.GetType(), "node") ?? AccessTools.Method(fromSlot.GetType(), "get_node"));

            var fromNode = _slotNodeInvoker?.Invoke(fromSlot);
            var toNode   = _slotNodeInvoker?.Invoke(toSlot);

            var fromState = AnimatorEditorInit.StateNodeType.IsInstanceOfType(fromNode)
                ? GraphPatchReflection.StateNodeStateField?.GetValue(fromNode) as AnimatorState : null;
            var toState = AnimatorEditorInit.StateNodeType.IsInstanceOfType(toNode)
                ? GraphPatchReflection.StateNodeStateField?.GetValue(toNode) as AnimatorState : null;

            var fromSpecial = AnimatorEditorInit.AnyStateNodeType.IsInstanceOfType(fromNode) ? SpecialNode.AnyState
                : AnimatorEditorInit.EntryNodeType.IsInstanceOfType(fromNode) ? SpecialNode.Entry
                : SpecialNode.None;
            var toSpecial = AnimatorEditorInit.ExitNodeType.IsInstanceOfType(toNode) ? SpecialNode.Exit
                : SpecialNode.None;

            var fromCenter = NodeCenter(fromNode);
            var toCenter = NodeCenter(toNode);
            // Special nodes render shorter than state nodes — match AnimatorTransitionPathPatch's node-center
            // calc so the drawn line and the drag/click hit-test agree on where the endpoint actually sits.
            if (fromSpecial != SpecialNode.None) fromCenter.y += AnimatorTransitionPathPatch.SpecialVerticalOffset;
            if (toSpecial != SpecialNode.None) toCenter.y += AnimatorTransitionPathPatch.SpecialVerticalOffset;
            return (fromState, fromSpecial, toState, toSpecial, fromCenter, toCenter);
        }

        static Vector3 NodeCenter(object node)
        {
            if (node == null || GraphPatchReflection.NodePositionField == null) return default;
            if (GraphPatchReflection.NodePositionField.GetValue(node) is not Rect rect) return default;
            return new Vector3(rect.center.x, rect.center.y, 0f);
        }

        /* Frame-memoized TransitionPathData resolution — resolved once per repaint pass via the first edge's
           state, not once per edge (that would be hundreds of AssetDatabase.LoadAllAssetsAtPath calls/repaint). */
        static TransitionPathData GetPathDataForFrame(AnimatorState anyState)
        {
            int frame = Time.frameCount;
            if (_pathDataCacheFrame == frame) return _pathDataCache;
            // Stateless edges (e.g. AnyState -> Exit) have no AnimatorState to resolve a controller path from —
            // fall back to the controller AnimatorTransitionPathPatch resolved for the graph this repaint.
            AnimatorController controller = null;
            if (anyState != null)
            {
                var path = AssetDatabase.GetAssetPath(anyState);
                controller = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            controller ??= GraphPatchReflection.LastActiveController;
            if (controller == null) return null; // retry on the next edge this repaint rather than caching a miss
            _pathDataCache = TransitionPathData.Get(controller);
            _pathDataCacheFrame = frame;
            return _pathDataCache;
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

        /* True if any currently-selected object is one of this edge's transitions — used to gate the Alt-held expanded conditions box on the actual selected transition(s) rather than a color heuristic. */
        static bool IsEdgeInSelection(object info)
        {
            if (info == null) return false;
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0) return false;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return false;
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                var transObj = _labelTransitionContextField?.GetValue(transitionContext) as UnityEngine.Object;
                if (transObj != null && System.Array.IndexOf(selectedObjects, transObj) >= 0) return true;
            }
            return false;
        }

        enum InOutDirection { None, Outgoing, Incoming }

        /* Returns whether edge leaves (Outgoing) or enters (Incoming) the single currently selected node, or None to use the default line color. */
        static InOutDirection ResolveInOutDirection(object edge, AnimatorDefaultSettings settings)
        {
            if (!settings.transitionSelectionColorEnabled) return InOutDirection.None;
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length != 1) return InOutDirection.None;

            if (_fromSlotInvoker == null)
                _fromSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "fromSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_fromSlot"));
            if (_toSlotInvoker == null)
                _toSlotInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(GraphPatchReflection.EdgeType, "toSlot") ?? AccessTools.Method(GraphPatchReflection.EdgeType, "get_toSlot"));

            var fromSlot = _fromSlotInvoker?.Invoke(edge);
            var toSlot   = _toSlotInvoker?.Invoke(edge);
            if (fromSlot == null || toSlot == null) return InOutDirection.None;

            if (_slotNodeInvoker == null)
                _slotNodeInvoker = MethodInvoker.GetHandler(
                    AccessTools.PropertyGetter(fromSlot.GetType(), "node") ?? AccessTools.Method(fromSlot.GetType(), "get_node"));

            var fromNode = _slotNodeInvoker?.Invoke(fromSlot);
            var toNode   = _slotNodeInvoker?.Invoke(toSlot);

            if (IsNodeMatchingSelection(fromNode, selectedObjects)) return InOutDirection.Outgoing;
            if (IsNodeMatchingSelection(toNode, selectedObjects))   return InOutDirection.Incoming;
            return InOutDirection.None;
        }

        /* Lerps between the two given gradient colors over time (ping-pong). Each edge gets a stable phase offset from its hash so lines drift out of sync instead of pulsing together. */
        static Color ResolveGradientColor(object edge, Color colorA, Color colorB, float speed)
        {
            float phaseOffset = ((edge?.GetHashCode() ?? 0) & 0x3FF) / 1024f;
            float t = Mathf.PingPong((float)(EditorApplication.timeSinceStartup * speed) + phaseOffset, 1f);
            return Color.Lerp(colorA, colorB, t);
        }

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
        static void Postfix(object __instance, object edge, ref Color color, object info, int __state)
        {
            if (__state == 0) return;
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if ((__state & GradientAppliedFlag) != 0) RequestRepaint();
                bool isManualPath = (__state & ManualPathFlag) != 0;
                bool animate = settings.transitionAnimateSelected && ((__state & 1) != 0 || IsNodeSelected(edge));

                var args = new object[] { edge, Vector3.zero };
                var points = GraphPatchReflection.GetEdgePointsMethod?.Invoke(__instance, args) as Vector3[];
                if (points == null || points.Length < 2) return;
                var cross = (Vector3)args[1];

                var sourcePoint      = points[0];
                var destinationPoint = points[points.Length - 1];
                Vector3 midPoint;
                Vector3 direction;

                if (isManualPath)
                {
                    var entry = _pendingManualEntry;
                    if (entry == null) return;

                    int pointCount = entry.points.Count + 2;
                    EnsureScratchCapacity(pointCount);
                    _scratchPoints[0] = _pendingSourceCenter + new Vector3(entry.sourceOffset.x, entry.sourceOffset.y, 0f);
                    for (int i = 0; i < entry.points.Count; i++)
                    {
                        var waypoint = entry.points[i];
                        _scratchPoints[i + 1] = new Vector3(waypoint.x, waypoint.y, 0f);
                    }
                    _scratchPoints[pointCount - 1] = _pendingDestCenter + new Vector3(entry.destOffset.x, entry.destOffset.y, 0f);

                    if (Event.current.type == EventType.Repaint && ChainIntersectsRect(_scratchPoints, pointCount, VisibleRect()))
                    {
                        var previousColor  = Handles.color;
                        var previousMatrix = Handles.matrix;
                        try
                        {
                            _edgeSizeMultiplierInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.EdgeSizeMultiplierGetter);
                            float manualMult = _edgeSizeMultiplierInvoker != null ? (float)_edgeSizeMultiplierInvoker(__instance) : 1f;

                            // 0.8 alpha + 4f*mult width matches PatchEdgeGUIDoEdges's chain-preview AA polyline —
                            // a full-alpha AA line reads brighter/thinner than native at the same nominal width.
                            var lineColor = color;
                            lineColor.a *= 0.8f;
                            Handles.color = lineColor;
                            Handles.DrawAAPolyLine(4f * manualMult, pointCount, _scratchPoints);

                            float handleRadius = 3f * manualMult;
                            for (int i = 0; i < pointCount; i++)
                            {
                                int waypointIndex = i - 1; // source/dest anchors (i == 0 / pointCount-1) aren't interactive waypoints
                                bool isHovered = waypointIndex >= 0 && entry == AnimatorTransitionPathPatch.HoveredEntry &&
                                    waypointIndex == AnimatorTransitionPathPatch.HoveredPointIndex;
                                Handles.color = isHovered ? HoveredHandleColor : Color.white;
                                Handles.DrawSolidDisc(_scratchPoints[i], Vector3.forward, handleRadius);
                            }

                            if (settings.transitionIndicatorArrowsEnabled)
                            {
                                var manualArrowColor = PatchDrawArrows.GetOrResolveArrowColor(info, settings) ?? settings.transitionOverlayArrowColor;
                                _drawArrowsInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.DrawArrowsMethod);

                                bool isSelfTransition = entry.fromState == entry.toState && entry.fromSpecial == entry.toSpecial;
                                float arrowLength = 13f * manualMult;

                                // one arrow per bent segment, not just the segment the overall midpoint falls on
                                for (int segmentIndex = 0; segmentIndex < pointCount - 1; segmentIndex++)
                                {
                                    var segmentStart = _scratchPoints[segmentIndex];
                                    var segmentEnd = _scratchPoints[segmentIndex + 1];
                                    var segmentDirection = (segmentEnd - segmentStart).normalized;
                                    var segmentPerpendicular = new Vector3(-segmentDirection.y, segmentDirection.x, 0f);

                                    // Pull the node-touching end out to the box boundary so DrawArrows' own midpoint
                                    // calc centers within the visible line, not the hidden half inside the node.
                                    if (pointCount > 2)
                                    {
                                        if (segmentIndex == 0) segmentStart = PullToNodeBoundary(segmentStart, segmentEnd, entry.fromSpecial);
                                        if (segmentIndex == pointCount - 2) segmentEnd = PullToNodeBoundary(segmentEnd, segmentStart, entry.toSpecial);
                                    }

                                    var segmentPoints = new[] { segmentStart, segmentEnd };
                                    _drawArrowsInvoker?.Invoke(null, manualArrowColor, segmentPerpendicular, segmentPoints, info, isSelfTransition, 5f * manualMult, 2f * manualMult, arrowLength);
                                }

                                if (animate)
                                {
                                    _drawArrowInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.DrawArrowMethod);
                                    var (manualAnimatedPosition, animatedDirection) = GetAnimatedArrowPositionMultiSegment(_scratchPoints, pointCount);
                                    var animatedPerpendicular = new Vector3(-animatedDirection.y, animatedDirection.x, 0f);
                                    _drawArrowInvoker?.Invoke(null, manualArrowColor, animatedPerpendicular, animatedDirection, manualAnimatedPosition, 5f * manualMult, 2f * manualMult);
                                    RequestRepaint();
                                }
                            }
                        }
                        finally
                        {
                            Handles.color  = previousColor;
                            Handles.matrix = previousMatrix;
                        }
                    }

                    (midPoint, direction) = GetMiddleSegmentCenter(_scratchPoints, pointCount);
                }
                else
                {
                    midPoint  = Vector3.Lerp(sourcePoint, destinationPoint, 0.5f);
                    direction = (destinationPoint - sourcePoint).normalized;
                }

                bool altHeld = Event.current.alt;
                if (altHeld != _wasAltHeld) { _wasAltHeld = altHeld; RequestRepaint(); }
                bool showExpandedBox = altHeld && IsEdgeInSelection(info);

                if (!settings.transitionShowLabel && !animate && !showExpandedBox) return;

                if (showExpandedBox)
                    _pendingExpandedBox = (info, (Vector2)midPoint, (Vector2)direction);
                else if (settings.transitionShowLabel)
                {
                    var label = BuildLabel(info);
                    if (label != null) DrawLabel((Vector2)midPoint, (Vector2)direction, label);
                }

                if (!animate || isManualPath) return; // manual edges already drew their arrow above

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

                RequestRepaint();
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge postfix error: {e}"); }
        }

        static void EnsureScratchCapacity(int required)
        {
            if (_scratchPoints.Length >= required) return;
            int newSize = _scratchPoints.Length;
            while (newSize < required) newSize *= 2;
            _scratchPoints = new Vector3[newSize];
        }

        /* Bounding-box overlap test — cheap whole-chain cull before any per-segment draw work. */
        static bool ChainIntersectsRect(Vector3[] chain, int count, Rect rect)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (chain[i].x < minX) minX = chain[i].x;
                if (chain[i].x > maxX) maxX = chain[i].x;
                if (chain[i].y < minY) minY = chain[i].y;
                if (chain[i].y > maxY) maxY = chain[i].y;
            }
            return maxX >= rect.xMin && minX <= rect.xMax && maxY >= rect.yMin && minY <= rect.yMax;
        }

        static Vector3 SegmentMidpoint(Vector3[] chain, int segmentIndex) =>
            Vector3.Lerp(chain[segmentIndex], chain[segmentIndex + 1], 0.5f);

        // Ray/box exit distance from a node center toward `to`, capped at half the segment length — box size
        // depends on whether `from` is a state node or an AnyState/Entry/Exit special node (smaller box).
        static Vector3 PullToNodeBoundary(Vector3 from, Vector3 to, SpecialNode special)
        {
            var full = to - from;
            float length = full.magnitude;
            if (length <= 0.0001f) return from;
            var direction = full / length;
            float halfWidth  = special != SpecialNode.None ? AnimatorTransitionPathPatch.SpecialWidth * 0.5f : AnimatorTransitionPathPatch.NodeWidth * 0.5f;
            float halfHeight = special != SpecialNode.None ? AnimatorTransitionPathPatch.SpecialHeight * 0.5f : AnimatorTransitionPathPatch.NodeHeight * 0.5f;
            float exitX = direction.x != 0f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
            float exitY = direction.y != 0f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;
            float clearance = Mathf.Min(Mathf.Min(exitX, exitY), length * 0.5f);

            return from + direction * clearance;
        }

        // Label anchor: center of the middle segment by index, not the length-weighted midpoint of the whole chain.
        static (Vector3 position, Vector3 direction) GetMiddleSegmentCenter(Vector3[] chain, int count)
        {
            int segmentCount = count - 1;
            int segmentIndex = Mathf.Clamp(segmentCount / 2, 0, segmentCount - 1);
            var direction = (chain[segmentIndex + 1] - chain[segmentIndex]).normalized;
            return (SegmentMidpoint(chain, segmentIndex), direction);
        }

        /* Walks cumulative segment lengths and lerps within the segment containing t * totalLength (t in [0,1]). */
        static (Vector3 position, Vector3 direction, int segmentIndex) GetPositionAlongChain(Vector3[] chain, int count, float t)
        {
            if (count < 2) return (chain[0], Vector3.zero, 0);
            float totalLength = 0f;
            for (int i = 0; i < count - 1; i++) totalLength += Vector3.Distance(chain[i], chain[i + 1]);
            if (totalLength <= 0.0001f) return (chain[0], Vector3.zero, 0);

            float target = Mathf.Clamp01(t) * totalLength;
            float accumulated = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                float segmentLength = Vector3.Distance(chain[i], chain[i + 1]);
                if (accumulated + segmentLength >= target || i == count - 2)
                {
                    float segmentT = segmentLength > 0.0001f ? Mathf.Clamp01((target - accumulated) / segmentLength) : 0f;
                    var segmentDirection = segmentLength > 0.0001f ? (chain[i + 1] - chain[i]).normalized : Vector3.zero;
                    return (Vector3.Lerp(chain[i], chain[i + 1], segmentT), segmentDirection, i);
                }
                accumulated += segmentLength;
            }
            return (chain[count - 1], Vector3.zero, count - 2);
        }

        internal static (Vector3 position, Vector3 direction) GetAnimatedArrowPositionMultiSegment(Vector3[] chain, int count)
        {
            float progress = (float)(EditorApplication.timeSinceStartup * 0.5 % 1.0);
            // Match native GetAnimatedArrowPosition's phase: it runs midpoint->dest then source->midpoint
            // (a 0.5 phase shift from a plain source->dest sweep), so manual arrows stay in sync with it.
            float chainT = (progress + 0.5f) % 1f;
            var (position, direction, _) = GetPositionAlongChain(chain, count, chainT);
            return (position, direction);
        }

        static void RequestRepaint()
        {
            if (_cachedAnimatorWindow == null)
                _cachedAnimatorWindow = Resources
                    .FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType)
                    .FirstOrDefault() as EditorWindow;
            _cachedAnimatorWindow?.Repaint();
        }

        /* Reads the transitions list from the edge info object and returns a one-line label: condition summary, "N Conditions", "Invalid", or null to show nothing. */
        static string BuildLabel(object info)
        {
            if (info == null) return null;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;

            var stateTransitions = new List<AnimatorStateTransition>();
            var entryTransitions = new List<AnimatorTransition>();
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                var transObj = _labelTransitionContextField?.GetValue(transitionContext);
                if ((transObj as object) is AnimatorStateTransition stateTrans)
                    stateTransitions.Add(stateTrans);
                else if ((transObj as object) is AnimatorTransition entryTrans)
                    entryTransitions.Add(entryTrans);
            }

            if (stateTransitions.Count > 0)
            {
                if (stateTransitions.Any(x => !x.hasExitTime && (x.conditions == null || x.conditions.Length == 0)))
                    return L10n.Get("transition_overlay.invalid");
                if (stateTransitions.Count == 1 && stateTransitions[0].conditions?.Length == 1)
                    return FormatCondition(stateTransitions[0].conditions[0]);
                int stateTotal = stateTransitions.Sum(x => x.conditions?.Length ?? 0);
                return L10n.Get("transition_overlay.n_conditions").Replace("{n}", stateTotal.ToString());
            }

            if (entryTransitions.Count > 0)
            {
                if (entryTransitions.Count == 1 && entryTransitions[0].conditions?.Length == 1)
                    return FormatCondition(entryTransitions[0].conditions[0]);
                int entryTotal = entryTransitions.Sum(x => x.conditions?.Length ?? 0);
                return entryTotal == 0 ? null : L10n.Get("transition_overlay.n_conditions").Replace("{n}", entryTotal.ToString());
            }

            return null;
        }

        static readonly string[] GestureNames =
        {
            "Neutral", "Fist", "OpenHand", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp"
        };

        /* Returns a short human-readable string for a single condition (e.g. "Param > 0.5", "Flag = True"), truncating parameter names over 16 chars from the front. */
        static string FormatCondition(AnimatorCondition animatorCondition, bool truncate = true)
        {
            var parameterLabel = truncate && animatorCondition.parameter.Length > 16 ? "…" + animatorCondition.parameter[^16..] : animatorCondition.parameter;
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

        static Rect VisibleRect()
        {
            if (_getVisibleRect == null)
            {
                var guiClipType = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                var prop = guiClipType?.GetProperty("visibleRect",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                _getVisibleRect = prop != null
                    ? (Func<Rect>)Delegate.CreateDelegate(typeof(Func<Rect>), prop.GetGetMethod(nonPublic: true))
                    : static () => new Rect(0, 0, 9999, 9999);
            }
            return _getVisibleRect();
        }

        /* Draws text rotated to follow the edge direction at mid-point, offsetting above or below the line based on the horizontal component of dir. Self-transitions (zero dir) use LabelOffsetBelow to place the label clear of the node. */
        static void DrawLabel(Vector2 mid, Vector2 dir, string text)
        {
            bool isSelfTransition = dir.sqrMagnitude < 0.001f;
            float yOffset = isSelfTransition ? LabelOffsetSelfTransition : (dir.x >= 0f ? LabelOffsetAbove : LabelOffsetBelow);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle > 90f)  angle -= 180f;
            if (angle < -90f) angle += 180f;

            var clipRect = VisibleRect();
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
                var transObj = _labelTransitionContextField?.GetValue(transitionContext);

                AnimatorCondition[] conditions;
                if ((transObj as object) is AnimatorStateTransition stateTransition)
                {
                    if (!stateTransition.hasExitTime && (stateTransition.conditions == null || stateTransition.conditions.Length == 0))
                    {
                        conditionLines.Add(L10n.Get("transition_overlay.invalid"));
                        continue;
                    }
                    conditions = stateTransition.conditions;
                }
                else if ((transObj as object) is AnimatorTransition entryTransition)
                    conditions = entryTransition.conditions;
                else
                    continue;

                if (conditions != null)
                {
                    foreach (var condition in conditions)
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

            var clipRect = VisibleRect();
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

    /* Draws the Alt-held expanded conditions box after the whole graph frame (nodes+edges) has drawn —
       edges draw before nodes in Unity's native pipeline, so anything queued in PatchDrawEdge's postfix
       must be flushed from a hook that fires after OnGraphGUI's full body returns, or the box renders
       underneath the node boxes. Kept in this file/feature so the whole box feature is self-contained. */
    [HarmonyPatch]
    internal static class PatchTransitionExpandedBoxFlush
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.OnGraphGUIMethod;

        [HarmonyPostfix]
        static void Postfix() => PatchDrawEdge.FlushExpandedBox();
    }

    /* ── Edge click-selection override ───────────────────────────────────────
       EdgeGUI.FindClosestEdge hit-tests distance to GetEdgePoints' straight source-dest line — it has no idea
       our manual-path edges are bent, so it keeps "finding" the invisible straight line and letting native
       selection (GraphGUI.DragSelection) pick a transition the user never actually clicked on. Discarding its
       result for manual-path edges here forces that click through as a miss, so AnimatorTransitionPathPatch's
       own bent-geometry hit test (which runs earlier, as a Prefix on OnGraphGUI) is the only thing that can
       select these edges. */
    [HarmonyPatch]
    internal static class PatchFindClosestEdge
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.FindClosestEdgeMethod;

        [HarmonyPostfix]
        static void Postfix(ref object __result)
        {
            try
            {
                if (__result != null && PatchDrawEdge.IsManualPathEdge(__result))
                    __result = null;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] FindClosestEdge postfix error: {e}"); }
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
