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
    internal static class BlendTreePatchReflection
    {
        static BlendTreePatchReflection()
        {
            if (BlendTreeGraphGUIType == null)
                Debug.LogWarning("[AnimatorTools] AnimationBlendTree.GraphGUI not found — Unity version mismatch?");
            if (BlendTreeNodeType == null)
                Debug.LogWarning("[AnimatorTools] AnimationBlendTree.Node not found — Unity version mismatch?");
            if (NodeMotionField == null)
                Debug.LogWarning("[AnimatorTools] AnimationBlendTree.Node.motion not found — Unity version mismatch?");
            if (NodePositionField == null)
                Debug.LogWarning("[AnimatorTools] AnimationBlendTree.Node.position not found — Unity version mismatch?");
            if (BlendTreeParameterGUIMethod == null)
                Debug.LogWarning("[AnimatorTools] BlendTreeInspector.ParameterGUI not found — Unity version mismatch?");
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

            internal Vector2 Position
            {
                get => NodePositionField != null ? (Vector2)(NodePositionField.GetValue(_instance) ?? Vector2.zero) : Vector2.zero;
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
