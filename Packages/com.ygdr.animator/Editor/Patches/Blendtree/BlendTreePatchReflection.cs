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
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class BlendTreePatchReflection
    {
        static BlendTreePatchReflection()
        {
            WarnIfNull(BlendTreeGraphGUIType, "AnimationBlendTree.GraphGUI");
            WarnIfNull(BlendTreeNodeType, "AnimationBlendTree.Node");
            WarnIfNull(NodeMotionField, "AnimationBlendTree.Node.motion");
            WarnIfNull(NodePositionField, "AnimationBlendTree.Node.position");
            WarnIfNull(BlendTreeParameterGUIMethod, "BlendTreeInspector.ParameterGUI");
            WarnIfNull(RecursiveBlendParameterCountGetter, "BlendTree.recursiveBlendParameterCount");
            WarnIfNull(GetRecursiveBlendParameterMethod, "BlendTree.GetRecursiveBlendParameter");
            WarnIfNull(GetRecursiveBlendParameterMinMethod, "BlendTree.GetRecursiveBlendParameterMin");
            WarnIfNull(GetRecursiveBlendParameterMaxMethod, "BlendTree.GetRecursiveBlendParameterMax");
            WarnIfNull(GraphGetParameterValueMethod, "Graph.GetParameterValue");
            WarnIfNull(GraphSetParameterValueMethod, "Graph.SetParameterValue");
            WarnIfNull(GraphPopulateParameterValuesMethod, "Graph.PopulateParameterValues");
            WarnIfNull(GraphParameterValuesField, "Graph.m_ParameterValues");
            WarnIfNull(BlendTreeGetInputBlendValueMethod, "BlendTree.GetInputBlendValue");
            WarnIfNull(HostGetter, "GraphGUI.m_Host");
            WarnIfNull(HostBeginWindowsMethod, "EditorWindow.BeginWindows");
            WarnIfNull(HostEndWindowsMethod, "EditorWindow.EndWindows");
            WarnIfNull(EdgeGUIGetter, "GraphGUI.edgeGUI");
            WarnIfNull(EdgeGUIDoEdgesMethod, "EdgeGUI.DoEdges");
            WarnIfNull(DragSelectionMethod, "GraphGUI.DragSelection");
            WarnIfNull(ShowContextMenuMethod, "GraphGUI.ShowContextMenu");
            WarnIfNull(HandleMenuEventsMethod, "GraphGUI.HandleMenuEvents");
            WarnIfNull(GetNodeStyleMethod, "Styles.GetNodeStyle");
            WarnIfNull(NodeInputSlotsGetter, "Node.inputSlots");
            WarnIfNull(NodeOutputSlotsGetter, "Node.outputSlots");
            WarnIfNull(SlotTitleField, "Slot.m_Title");
            WarnIfNull(SlotPositionField, "Slot.m_Position");
            WarnIfNull(GUIClipUnclipMethod, "GUIClip.Unclip");
        }

        static void WarnIfNull(object value, string label)
        {
            if (value == null)
                Debug.LogWarning($"[AnimatorTools] {label} not found — Unity version mismatch?");
        }

        // Resolves name as a field or property (whichever exists) and returns a uniform getter.
        // Used for base-class members whose exact declaration kind isn't confirmed via decompile.
        internal static Func<object, object> ResolveGetter(System.Type type, string name)
        {
            var field = AccessTools.Field(type, name);
            if (field != null) return field.GetValue;
            var prop = AccessTools.PropertyGetter(type, name);
            if (prop != null) return obj => prop.Invoke(obj, null);
            return null;
        }

        // ── Blend tree types ─────────────────────────────────────────────────
        internal static readonly System.Type BlendTreeGraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
        internal static readonly System.Type BlendTreeGraphType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.Graph");
        internal static readonly System.Type BlendTreeNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.Node");

        // ── GraphGUI methods ─────────────────────────────────────────────────
        internal static readonly MethodInfo NodeGUIMethod =
            AccessTools.Method(BlendTreeGraphGUIType, "NodeGUI");
        internal static readonly MethodInfo OnGraphGUIMethod =
            AccessTools.Method(BlendTreeGraphGUIType, "OnGraphGUI");
        internal static readonly MethodInfo HandleNodeInputMethod =
            AccessTools.Method(BlendTreeGraphGUIType, "HandleNodeInput");

        // ── GraphGUI properties ──────────────────────────────────────────────
        internal static readonly MethodInfo GraphGUIGraphGetter =
            AccessTools.PropertyGetter(BlendTreeGraphGUIType, "graph");

        // ── Graph methods ────────────────────────────────────────────────────
        internal static readonly MethodInfo BuildFromBlendTreeMethod =
            AccessTools.Method(BlendTreeGraphType, "BuildFromBlendTree",
                new[] { typeof(BlendTree) });

        // ── Graph properties ─────────────────────────────────────────────────
        internal static readonly MethodInfo GraphRootBlendTreeGetter =
            AccessTools.PropertyGetter(BlendTreeGraphType, "rootBlendTree");
        internal static readonly MethodInfo GraphGetParameterValueMethod =
            AccessTools.Method(BlendTreeGraphType, "GetParameterValue", new[] { typeof(string) });
        internal static readonly MethodInfo GraphSetParameterValueMethod =
            AccessTools.Method(BlendTreeGraphType, "SetParameterValue", new[] { typeof(string), typeof(float) });
        // Native OnGraphGUI calls this unconditionally as its first line, every single call — refreshing
        // m_ParameterValues from m_RootBlendTree's own recursive walk. The lightweight path fully replaces
        // OnGraphGUI, so this never runs on its own; call it ourselves to match.
        internal static readonly MethodInfo GraphPopulateParameterValuesMethod =
            AccessTools.Method(BlendTreeGraphType, "PopulateParameterValues");
        // Graph.GetParameterValue logs "parameter name does not exist." and returns 0f for a missing key
        // rather than throwing — read m_ParameterValues directly so we can check/seed without spamming
        // the console (native PopulateParameterValues only re-derives this dict on graph rebuild, e.g.
        // breadcrumb navigation, from m_RootBlendTree's own recursive set, which a nested Direct-type
        // child's own recursive set doesn't always match).
        internal static readonly FieldInfo GraphParameterValuesField =
            AccessTools.Field(BlendTreeGraphType, "m_ParameterValues");
        // Compiled field accessor — called per-parameter-per-node on the hot lightweight-draw path,
        // so this avoids paying FieldInfo.GetValue's per-call reflection cost there.
        internal static readonly AccessTools.FieldRef<object, Dictionary<string, float>> GraphParameterValuesRef =
            GraphParameterValuesField != null
                ? AccessTools.FieldRefAccess<Dictionary<string, float>>(BlendTreeGraphType, "m_ParameterValues")
                : null;
        internal static readonly MethodInfo BlendTreeGetInputBlendValueMethod =
            AccessTools.Method(typeof(BlendTree), "GetInputBlendValue", new[] { typeof(string) });

        // ── Node fields ──────────────────────────────────────────────────────
        internal static readonly FieldInfo NodeMotionField =
            AccessTools.Field(BlendTreeNodeType, "motion");
        internal static readonly FieldInfo NodePositionField =
            AccessTools.Field(BlendTreeNodeType, "position");

        // ── Node properties ──────────────────────────────────────────────────
        internal static readonly MethodInfo NodeParentGetter =
            AccessTools.PropertyGetter(BlendTreeNodeType, "parent");
        internal static readonly MethodInfo NodeChildIndexGetter =
            AccessTools.PropertyGetter(BlendTreeNodeType, "childIndex");

        internal readonly struct BlendTreeNodeProxy
        {
            readonly object _instance;

            internal BlendTreeNodeProxy(object instance) => _instance = instance;

            internal bool IsValid => _instance != null && NodeMotionField != null;

            internal Motion Motion
            {
                get => NodeMotionField?.GetValue(_instance) as Motion;
                set => NodeMotionField?.SetValue(_instance, value);
            }

            internal Rect Position
            {
                get => NodePositionField != null ? (Rect)(NodePositionField.GetValue(_instance) ?? default(Rect)) : default;
                set => NodePositionField?.SetValue(_instance, value);
            }

            internal object Parent => NodeParentGetter?.Invoke(_instance, null);
            internal int ChildIndex => NodeChildIndexGetter != null ? (int)(NodeChildIndexGetter.Invoke(_instance, null) ?? 0) : 0;
        }

        // ── BlendTreeInspector ───────────────────────────────────────────────
        internal static readonly System.Type BlendTreeInspectorType =
            AccessTools.TypeByName("UnityEditor.BlendTreeInspector");
        internal static readonly MethodInfo BlendTreeParameterGUIMethod =
            AccessTools.Method(BlendTreeInspectorType, "ParameterGUI");

        // ── Recursive blend parameter members (NodeGUI transpiler call-site match) ─
        // Public API on BlendTree, but resolved via AccessTools for MethodInfo identity
        // matching against the decompiled NodeGUI IL — never invoked directly.
        internal static readonly MethodInfo RecursiveBlendParameterCountGetter =
            AccessTools.PropertyGetter(typeof(BlendTree), "recursiveBlendParameterCount");
        internal static readonly MethodInfo GetRecursiveBlendParameterMethod =
            AccessTools.Method(typeof(BlendTree), "GetRecursiveBlendParameter", new[] { typeof(int) });
        internal static readonly MethodInfo GetRecursiveBlendParameterMinMethod =
            AccessTools.Method(typeof(BlendTree), "GetRecursiveBlendParameterMin", new[] { typeof(int) });
        internal static readonly MethodInfo GetRecursiveBlendParameterMaxMethod =
            AccessTools.Method(typeof(BlendTree), "GetRecursiveBlendParameterMax", new[] { typeof(int) });

        // ── Base GraphGUI (shared node-loop internals for lightweight draw) ────
        internal static readonly System.Type BaseGraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.GraphGUI");
        internal static readonly System.Type BaseNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.Node");
        internal static readonly System.Type EdgeGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.EdgeGUI");

        internal static readonly Func<object, object> HostGetter =
            ResolveGetter(BaseGraphGUIType, "m_Host");
        internal static readonly MethodInfo HostBeginWindowsMethod =
            AccessTools.Method(typeof(UnityEditor.EditorWindow), "BeginWindows");
        internal static readonly MethodInfo HostEndWindowsMethod =
            AccessTools.Method(typeof(UnityEditor.EditorWindow), "EndWindows");

        internal static readonly Func<object, object> EdgeGUIGetter =
            ResolveGetter(BaseGraphGUIType, "edgeGUI");
        internal static readonly MethodInfo EdgeGUIDoEdgesMethod =
            AccessTools.Method(EdgeGUIType, "DoEdges");
        internal static readonly MethodInfo EdgeGUIDoDraggedEdgeMethod =
            AccessTools.Method(EdgeGUIType, "DoDraggedEdge");
        internal static readonly MethodInfo DragSelectionMethod =
            AccessTools.Method(BaseGraphGUIType, "DragSelection");
        internal static readonly MethodInfo ShowContextMenuMethod =
            AccessTools.Method(BaseGraphGUIType, "ShowContextMenu");
        internal static readonly MethodInfo HandleMenuEventsMethod =
            AccessTools.Method(BaseGraphGUIType, "HandleMenuEvents");

        internal static readonly Func<object, object> NodeStyleGetter =
            ResolveGetter(BaseNodeType, "style");
        internal static readonly Func<object, object> NodeColorGetter =
            ResolveGetter(BaseNodeType, "color");
        internal static readonly Func<object, object> NodeIsInvalidGetter =
            ResolveGetter(BaseNodeType, "nodeIsInvalid");
        internal static readonly MethodInfo GetNodeStyleMethod =
            AccessTools.Method(AccessTools.TypeByName("UnityEditor.Graphs.Styles"), "GetNodeStyle");

        internal static readonly System.Type SlotType =
            AccessTools.TypeByName("UnityEditor.Graphs.Slot");
        internal static readonly Func<object, object> NodeInputSlotsGetter =
            ResolveGetter(BaseNodeType, "inputSlots");
        internal static readonly Func<object, object> NodeOutputSlotsGetter =
            ResolveGetter(BaseNodeType, "outputSlots");
        // Slot.title is a trivial `=> m_Title` pass-through (confirmed via decompile) — read the field
        // directly instead of going through the property getter, called per-slot-per-idle-node-per-repaint.
        static readonly FieldInfo SlotTitleField = AccessTools.Field(SlotType, "m_Title");
        internal static readonly AccessTools.FieldRef<object, string> SlotTitleRef =
            SlotTitleField != null ? AccessTools.FieldRefAccess<string>(SlotType, "m_Title") : null;
        internal static readonly FieldInfo SlotPositionField =
            AccessTools.Field(SlotType, "m_Position");
        // Compiled field accessor — written per-slot-per-idle-node-per-repaint, see DrawIdleSlotRows.
        internal static readonly AccessTools.FieldRef<object, Rect> SlotPositionRef =
            SlotPositionField != null ? AccessTools.FieldRefAccess<Rect>(SlotType, "m_Position") : null;

        // Native DoSlot writes slot.m_Position = GUIClip.Unclip(rect) so EdgeGUI.GetEdgeEndPoints can
        // read absolute screen-space endpoints later. Idle lightweight nodes never run DoSlot, so their
        // slots keep whatever m_Position was set to before the graph was last rebuilt (Rect.zero on a
        // fresh BuildFromBlendTree, e.g. after breadcrumb navigation) — edges from those slots draw from
        // the corner until we write this ourselves.
        internal static readonly MethodInfo GUIClipUnclipMethod =
            AccessTools.Method(AccessTools.TypeByName("UnityEngine.GUIClip"), "Unclip", new[] { typeof(Rect) });
        internal static readonly Func<Rect, Rect> GUIClipUnclip =
            GUIClipUnclipMethod != null
                ? (Func<Rect, Rect>)Delegate.CreateDelegate(typeof(Func<Rect, Rect>), GUIClipUnclipMethod)
                : null;

        // ── Styles fields ─────────────────────────────────────────────────────
        internal static readonly System.Type StylesType =
            AccessTools.TypeByName("UnityEditor.Graphs.Styles");
        internal static readonly FieldInfo VarPinInField =
            AccessTools.Field(AccessTools.TypeByName("UnityEditor.Graphs.Styles"), "varPinIn");
        internal static readonly FieldInfo VarPinOutField =
            AccessTools.Field(AccessTools.TypeByName("UnityEditor.Graphs.Styles"), "varPinOut");
    }
}
#endif
