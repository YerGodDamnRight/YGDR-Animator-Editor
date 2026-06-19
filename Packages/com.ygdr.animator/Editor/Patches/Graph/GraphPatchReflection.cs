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
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class GraphPatchReflection
    {
        static GraphPatchReflection()
        {
            if (GraphGUIType == null)
                Debug.LogWarning("[AnimatorTools] AnimationStateMachine.GraphGUI not found — Unity version mismatch?");
            if (EdgeGUIType == null)
                Debug.LogWarning("[AnimatorTools] AnimationStateMachine.EdgeGUI not found — Unity version mismatch?");
            if (OnGraphGUIMethod == null)
                Debug.LogWarning("[AnimatorTools] GraphGUI.OnGraphGUI not found — Unity version mismatch?");
            if (DrawEdgeMethod == null)
                Debug.LogWarning("[AnimatorTools] EdgeGUI.DrawEdge not found — Unity version mismatch?");
            if (RebuildGraphMethod == null)
                Debug.LogWarning("[AnimatorTools] AnimatorControllerTool.RebuildGraph not found — Unity version mismatch?");
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
        internal static readonly MethodInfo EdgeSizeMultiplierGetter =
            AccessTools.PropertyGetter(EdgeGUIType, "edgeSizeMultiplier");

        // ── AnimatorControllerTool methods ───────────────────────────────────
        internal static readonly MethodInfo RebuildGraphMethod =
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "RebuildGraph",
                new[] { typeof(bool) });

        // ── Node fields ──────────────────────────────────────────────────────
        internal static readonly FieldInfo StateNodeStateField =
            AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
        internal static readonly FieldInfo StateMachineNodeStateMachineField =
            AccessTools.Field(AnimatorEditorInit.StateMachineNodeType, "stateMachine");

        // ── StateNode / AnyStateNode methods ─────────────────────────────────
        internal static readonly MethodInfo MakeTransitionCallbackMethod =
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "MakeTransitionCallback");
        internal static readonly MethodInfo AnyStateMakeTransitionCallbackMethod =
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "MakeTransitionCallback");
    }
}
#endif
