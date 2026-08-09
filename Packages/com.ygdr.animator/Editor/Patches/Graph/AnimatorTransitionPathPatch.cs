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
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Ctrl+Click a manual-path edge to insert a waypoint, Alt+Click an existing waypoint to delete it, drag a
    // waypoint to move it. Source/destination anchors are fixed to their node and not interactive.
    // AnyState/Entry-sourced and Exit-targeted edges are fully interactive too; only sub-state-machine
    // targets stay unsupported (not modeled by TransitionPathEntry).
    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class AnimatorTransitionPathPatch
    {
        const float HitDistance = 10f;
        const float HandleSize  = 8f;
        internal const float NodeWidth   = 200f;
        internal const float NodeHeight  = 44f;
        internal const float SpecialWidth  = 160f;
        internal const float SpecialHeight = 30f;
        // Special nodes render shorter than state nodes — nudge their center down by half the height
        // difference so a manual path's horizontal run lines up level with a same-row state node.
        internal const float SpecialVerticalOffset = (NodeHeight - SpecialHeight) * 0.5f;

        static TransitionPathEntry _draggedEntry;
        static int _draggedPointIndex = int.MinValue;

        // Per-entry last-seen endpoint centers, so a repaint can tell "both nodes moved by the same amount"
        // (rigid drag — translate the waypoints with them) apart from "only one endpoint moved" (leave the
        // manual route as-is so the user can re-route by hand). Keyed by entry identity, not id, so a deleted
        // entry's tracking state is reclaimed by GC instead of leaking for the rest of the session.
        sealed class EndpointCenters { public Vector2 From, To; }
        static readonly ConditionalWeakTable<TransitionPathEntry, EndpointCenters> _lastEndpointCenters = new();

        static void TrackRigidMove(AnimatorStateMachine sm, TransitionPathData data)
        {
            foreach (var entry in data.entries)
            {
                if (!TryGetNodeCenter(sm, entry.fromState, entry.fromSpecial, out var fromCenter)) continue;
                if (!TryGetNodeCenter(sm, entry.toState, entry.toSpecial, out var toCenter)) continue;

                if (!_lastEndpointCenters.TryGetValue(entry, out var previous))
                {
                    _lastEndpointCenters.Add(entry, new EndpointCenters { From = fromCenter, To = toCenter });
                    continue;
                }

                var fromDelta = fromCenter - previous.From;
                var toDelta = toCenter - previous.To;
                if (fromDelta.sqrMagnitude > 0.0001f && (fromDelta - toDelta).sqrMagnitude < 0.01f)
                {
                    for (int i = 0; i < entry.points.Count; i++) entry.points[i] += fromDelta;
                    data.BumpVersion();
                    EditorUtility.SetDirty(data);
                }
                previous.From = fromCenter;
                previous.To = toCenter;
            }
        }

        internal static TransitionPathEntry HoveredEntry { get; private set; }
        internal static int HoveredPointIndex { get; private set; } = int.MinValue;

        static TransitionPathData _pathDataCache;
        static AnimatorController _pathDataCacheController;
        static int _pathDataCacheFrame = -1;

        // MouseMove fires once per pixel of mouse travel — TransitionPathData.Get() hits AssetDatabase, so this
        // must not run more than once per repaint frame.
        static TransitionPathData GetCachedPathData(AnimatorController controller)
        {
            int frame = Time.frameCount;
            if (_pathDataCacheFrame == frame && _pathDataCacheController == controller) return _pathDataCache;
            _pathDataCache = TransitionPathData.Get(controller);
            _pathDataCacheController = controller;
            _pathDataCacheFrame = frame;
            return _pathDataCache;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.OnGraphGUIMethod;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;

                // Every GUI event (Layout/Repaint/MouseMove/etc.) hits this prefix, but only these event types
                // ever do anything — bail before the AssetDatabase/reflection lookups below run for free.
                bool isDragEvent = _draggedEntry != null &&
                    (currentEvent.type == EventType.MouseDrag || currentEvent.type == EventType.MouseUp);
                bool isClickEvent = _draggedEntry == null &&
                    currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentEvent.clickCount == 1;
                bool isCursorEvent = _draggedEntry == null &&
                    (currentEvent.type == EventType.Repaint || currentEvent.type == EventType.MouseMove);
                if (!isDragEvent && !isClickEvent && !isCursorEvent) return;

                var activeSM = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeSM == null) return;

                var controllerPath = AssetDatabase.GetAssetPath(activeSM);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null) return;
                GraphPatchReflection.LastActiveController = controller;

                // Drag continuation takes priority so an in-progress drag always finishes cleanly.
                if (_draggedEntry != null)
                {
                    if (currentEvent.type == EventType.MouseDrag)
                    {
                        var scrollPosition = FrameRenderer.LastScrollPosition;
                        var graphPoint = SnapToGrid(currentEvent.mousePosition + scrollPosition);
                        _draggedEntry.points[_draggedPointIndex] = graphPoint;
                        currentEvent.Use();
                        return;
                    }
                    // Only MouseDrag/MouseUp reach here (isDragEvent gate above) — this is the MouseUp case.
                    var pathData = GetCachedPathData(controller);
                    if (pathData != null)
                    {
                        pathData.BumpVersion();
                        EditorUtility.SetDirty(pathData);
                    }
                    _draggedEntry = null;
                    _draggedPointIndex = int.MinValue;
                    currentEvent.Use();
                    return;
                }

                var data = GetCachedPathData(controller);
                if (data == null || data.entries.Count == 0) return;

                // Repaint fires on every step of a native node drag — piggyback here to translate a manual
                // path's waypoints along with its endpoints when both ends of the edge move together.
                if (currentEvent.type == EventType.Repaint) TrackRigidMove(activeSM, data);

                var scroll = FrameRenderer.LastScrollPosition;
                var mouse = currentEvent.mousePosition;
                var graphMouse = mouse + scroll;
                bool mouseOverNode = IsMouseOverNode(activeSM, graphMouse);

                // Resolve each entry's screen-space chain once, shared by both passes below.
                var chains = data.entries
                    .Where(entry => (entry.fromState != null || entry.fromSpecial != SpecialNode.None)
                        && (entry.toState != null || entry.toSpecial != SpecialNode.None))
                    .Select(entry => (entry, chain: TryBuildChain(activeSM, entry, scroll)))
                    .Where(pair => pair.chain != null)
                    .ToList();

                if (isCursorEvent)
                {
                    // MouseMove events only fire once the graph window opts in — flip that on the first frame
                    // we know there's manual-path data worth hovering, never bothering when there is none.
                    if (PatchGraphInputHandler.AnimWindow != null) PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;

                    bool isRepaint = currentEvent.type == EventType.Repaint;
                    TransitionPathEntry hoveredEntry = null;
                    int hoveredIndex = int.MinValue;
                    foreach (var (entry, chain) in chains)
                    {
                        for (int i = 0; i < entry.points.Count; i++)
                        {
                            if (Vector2.Distance(mouse, chain[i + 1]) <= HandleSize)
                            {
                                hoveredEntry = entry;
                                hoveredIndex = i;
                            }
                        }
                    }

                    if (!isRepaint && (hoveredEntry != HoveredEntry || hoveredIndex != HoveredPointIndex))
                        PatchGraphInputHandler.AnimWindow?.Repaint();

                    HoveredEntry = hoveredEntry;
                    HoveredPointIndex = hoveredIndex;
                    return;
                }

                // Pass 1: handle dots always win, across ALL entries, before any line-segment test runs — otherwise
                // an overlapping transition's line can steal a click meant for another entry's waypoint dot.
                foreach (var (entry, chain) in chains)
                {
                    int handleSlot = int.MinValue;
                    for (int i = 0; i < entry.points.Count; i++)
                    {
                        if (Vector2.Distance(mouse, chain[i + 1]) > HandleSize) continue;
                        handleSlot = i;
                        break;
                    }

                    if (handleSlot == int.MinValue) continue;

                    if (currentEvent.alt)
                    {
                        Undo.RegisterCompleteObjectUndo(data, "Delete Transition Path Point");
                        entry.points.RemoveAt(handleSlot);
                        data.BumpVersion();
                        EditorUtility.SetDirty(data);
                    }
                    else
                    {
                        Undo.RegisterCompleteObjectUndo(data, "Move Transition Path Point");
                        _draggedEntry = entry;
                        _draggedPointIndex = handleSlot;
                    }
                    currentEvent.Use();
                    return;
                }

                if (mouseOverNode) return; // a node sits under the click — let native node handling win
                if (currentEvent.alt) return; // Alt+Click only deletes/resets handles, never inserts

                // Pass 2: no handle anywhere matched — fall back to line-segment hit test (insert/select).
                foreach (var (entry, chain) in chains)
                {
                    for (int i = 0; i < chain.Length - 1; i++)
                    {
                        if (GraphPatchReflection.DistancePointToSegment(mouse, chain[i], chain[i + 1]) > HitDistance) continue;

                        if (currentEvent.control)
                        {
                            Undo.RegisterCompleteObjectUndo(data, "Add Transition Path Point");
                            entry.points.Insert(i, SnapToGrid(graphMouse));
                            data.BumpVersion();
                            EditorUtility.SetDirty(data);
                            currentEvent.Use();
                            return;
                        }

                        // Plain click on the bent line — native click hit-testing uses the straight-line geometry
                        // (now suppressed for manual edges by PatchFindClosestEdge) and would miss this, so
                        // select the transition(s) ourselves.
                        if (!TrySelectEdge(activeSM, entry, currentEvent.shift)) break;
                        currentEvent.Use();
                        return;
                    }
                }
            }
            catch (Exception e) { Debug.LogError($"[YGDR] Transition path interaction error: {e}"); }
        }

        static Vector2[] TryBuildChain(AnimatorStateMachine sm, TransitionPathEntry entry, Vector2 scroll)
        {
            if (!TryGetNodeCenter(sm, entry.fromState, entry.fromSpecial, out var fromCenter)) return null;
            if (!TryGetNodeCenter(sm, entry.toState, entry.toSpecial, out var toCenter)) return null;
            return BuildScreenChain(entry, fromCenter, toCenter, scroll);
        }

        static bool TrySelectEdge(AnimatorStateMachine sm, TransitionPathEntry entry, bool addToSelection)
        {
            var sourceTransitions = entry.fromSpecial switch
            {
                SpecialNode.AnyState => sm.anyStateTransitions.Cast<AnimatorTransitionBase>(),
                SpecialNode.Entry => sm.entryTransitions.Cast<AnimatorTransitionBase>(),
                _ => entry.fromState.transitions.Cast<AnimatorTransitionBase>()
            };
            var transitions = sourceTransitions
                .Where(t => entry.toSpecial == SpecialNode.Exit ? t.isExit : t.destinationState == entry.toState)
                .Cast<UnityEngine.Object>()
                .ToArray();
            if (transitions.Length == 0) return false;
            Selection.objects = addToSelection
                ? Selection.objects.Concat(transitions).Distinct().ToArray()
                : transitions;
            return true;
        }

        static Vector2 SnapToGrid(Vector2 point) =>
            new Vector2(Mathf.Round(point.x / 10f) * 10f, Mathf.Round(point.y / 10f) * 10f);

        // Top-left position of a special node's box in sm, or null for SpecialNode.None.
        static Vector2? SpecialNodePosition(AnimatorStateMachine sm, SpecialNode special) => special switch
        {
            SpecialNode.AnyState => sm.anyStatePosition,
            SpecialNode.Entry => sm.entryPosition,
            SpecialNode.Exit => sm.exitPosition,
            _ => null
        };

        static bool TryGetNodeCenter(AnimatorStateMachine sm, AnimatorState state, SpecialNode special, out Vector2 center)
        {
            var specialPosition = SpecialNodePosition(sm, special);
            if (specialPosition != null)
            {
                center = new Vector2(specialPosition.Value.x + SpecialWidth * 0.5f, specialPosition.Value.y + SpecialHeight * 0.5f + SpecialVerticalOffset);
                return true;
            }
            var childState = sm.states.FirstOrDefault(x => x.state == state);
            if (childState.state == null) { center = default; return false; }
            center = new Vector2(childState.position.x + NodeWidth * 0.5f, childState.position.y + NodeHeight * 0.5f);
            return true;
        }

        /* Checks graphMouse against every node/special-node box in sm, so line/segment interaction can yield to
           node clicks when the two overlap — otherwise our broad segment-proximity hit test can unintentionally
           swallow clicks meant for a node sitting near or under a bent line. */
        static bool IsMouseOverNode(AnimatorStateMachine sm, Vector2 graphMouse)
        {
            foreach (var childState in sm.states)
                if (new Rect(childState.position.x, childState.position.y, NodeWidth, NodeHeight).Contains(graphMouse))
                    return true;
            foreach (var childSM in sm.stateMachines)
                if (new Rect(childSM.position.x, childSM.position.y, NodeWidth, NodeHeight).Contains(graphMouse))
                    return true;
            foreach (var special in new[] { SpecialNode.AnyState, SpecialNode.Entry, SpecialNode.Exit })
            {
                var position = SpecialNodePosition(sm, special)!.Value;
                if (new Rect(position.x, position.y, SpecialWidth, SpecialHeight).Contains(graphMouse)) return true;
            }
            return false;
        }

        static Vector2[] BuildScreenChain(TransitionPathEntry entry, Vector2 fromCenter, Vector2 toCenter, Vector2 scroll)
        {
            var chain = new Vector2[entry.points.Count + 2];
            chain[0] = fromCenter + entry.sourceOffset - scroll;
            for (int i = 0; i < entry.points.Count; i++)
                chain[i + 1] = entry.points[i] - scroll;
            chain[chain.Length - 1] = toCenter + entry.destOffset - scroll;
            return chain;
        }
    }
}
#endif
