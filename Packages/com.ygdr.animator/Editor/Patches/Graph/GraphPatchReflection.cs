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
using System.Reflection;
using HarmonyLib;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class GraphPatchReflection
    {
        // Controller for the currently-drawing graph, refreshed each repaint by AnimatorTransitionPathPatch
        // (which resolves it from the active state machine) before per-edge draws run — lets edge-level code
        // resolve a controller without an AnimatorState to anchor an asset-path lookup off of (e.g. AnyState -> Exit).
        internal static AnimatorController LastActiveController;

        static GraphPatchReflection()
        {
            WarnIfNull(GraphGUIType, "AnimationStateMachine.GraphGUI");
            WarnIfNull(EdgeGUIType, "AnimationStateMachine.EdgeGUI");
            WarnIfNull(OnGraphGUIMethod, "GraphGUI.OnGraphGUI");
            WarnIfNull(DrawEdgeMethod, "EdgeGUI.DrawEdge");
            WarnIfNull(RebuildGraphMethod, "AnimatorControllerTool.RebuildGraph");
            WarnIfNull(NodePositionField, "Node.position");
        }

        static void WarnIfNull(object value, string label)
        {
            if (value == null)
                Debug.LogWarning($"[AnimatorTools] {label} not found — Unity version mismatch?");
        }

        // ── Graph types ──────────────────────────────────────────────────────
        internal static readonly System.Type GraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI");
        internal static readonly System.Type GraphGUIBaseType =
            AccessTools.TypeByName("UnityEditor.Graphs.GraphGUI");
        internal static readonly System.Type EdgeGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EdgeGUI");
        internal static readonly System.Type EdgeType =
            AccessTools.TypeByName("UnityEditor.Graphs.Edge");
        internal static readonly System.Type StylesType =
            AccessTools.TypeByName("UnityEditor.Graphs.Styles");

        // ── GraphGUI methods ─────────────────────────────────────────────────
        internal static readonly MethodInfo OnGraphGUIMethod =
            AccessTools.Method(GraphGUIType, "OnGraphGUI");
        internal static readonly MethodInfo HandleContextMenuMethod =
            AccessTools.Method(GraphGUIType, "HandleContextMenu");
        internal static readonly MethodInfo CopySelectionToPasteboardMethod =
            AccessTools.Method(GraphGUIType, "CopySelectionToPasteboard");

        // ── GraphGUI base methods ────────────────────────────────────────────
        internal static readonly MethodInfo DrawGridMethod =
            AccessTools.Method(GraphGUIBaseType, "DrawGrid");

        // ── EdgeGUI methods ──────────────────────────────────────────────────
        internal static readonly MethodInfo DrawEdgeMethod =
            AccessTools.Method(EdgeGUIType, "DrawEdge");
        internal static readonly MethodInfo DrawArrowsMethod =
            AccessTools.Method(EdgeGUIType, "DrawArrows");
        internal static readonly MethodInfo DrawArrowMethod =
            AccessTools.Method(EdgeGUIType, "DrawArrow");
        internal static readonly MethodInfo DoEdgesMethod =
            AccessTools.Method(EdgeGUIType, "DoEdges");
        internal static readonly MethodInfo GetEdgePointsMethod =
            AccessTools.Method(EdgeGUIType, "GetEdgePoints",
                new[] { EdgeType, typeof(Vector3).MakeByRefType() });
        internal static readonly MethodInfo FindClosestEdgeMethod =
            AccessTools.Method(EdgeGUIType, "FindClosestEdge");
        internal static readonly MethodInfo EdgeSizeMultiplierGetter =
            AccessTools.PropertyGetter(EdgeGUIType, "edgeSizeMultiplier");

        // ── Shared math ──────────────────────────────────────────────────────
        internal static float DistancePointToSegment(Vector2 point, Vector2 segA, Vector2 segB)
        {
            var ab = segB - segA;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 0.0001f) return (point - segA).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - segA, ab) / sqrLen);
            return (point - (segA + t * ab)).magnitude;
        }

        // ── AnimatorControllerTool methods ───────────────────────────────────
        internal static readonly MethodInfo RebuildGraphMethod =
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "RebuildGraph",
                new[] { typeof(bool) });

        // ── Node fields ──────────────────────────────────────────────────────
        internal static readonly System.Type NodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.Node");
        internal static readonly FieldInfo NodePositionField =
            AccessTools.Field(NodeType, "position");
        internal static readonly FieldInfo StateNodeStateField =
            AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
        internal static readonly FieldInfo StateMachineNodeStateMachineField =
            AccessTools.Field(AnimatorEditorInit.StateMachineNodeType, "stateMachine");

        // ── StateNode / AnyStateNode methods ─────────────────────────────────
        internal static readonly MethodInfo MakeTransitionCallbackMethod =
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "MakeTransitionCallback");
        internal static readonly MethodInfo AnyStateMakeTransitionCallbackMethod =
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "MakeTransitionCallback");
        internal static readonly MethodInfo EntryMakeTransitionCallbackMethod =
            AccessTools.Method(AnimatorEditorInit.EntryNodeType, "MakeTransitionCallback");
    }
}
#endif
