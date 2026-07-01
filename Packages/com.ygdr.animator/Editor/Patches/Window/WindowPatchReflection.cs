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
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class WindowPatchReflection
    {
        static WindowPatchReflection()
        {
            WarnIfNull(LayerControllerViewType, "LayerControllerView type");
            WarnIfNull(LayerScrollField, "LayerControllerView.m_LayerScroll");
            WarnIfNull(LayerListField, "LayerControllerView.m_LayerList");
            WarnIfNull(LayerSelectedIndexField, "LayerControllerView.m_SelectedLayerIndex");
            WarnIfNull(AnimatorControllerDirtyField, "AnimatorController.OnAnimatorControllerDirty");
            WarnIfNull(AnimatorControllerGetter, "AnimatorControllerTool.animatorController");
        }

        static void WarnIfNull(object value, string label)
        {
            if (value == null)
                Debug.LogWarning($"[AnimatorTools] {label} not found — Unity version mismatch?");
        }

        // Layer view
        internal static readonly Type LayerControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.LayerControllerView");
        internal static readonly FieldInfo LayerScrollField =
            AccessTools.Field(LayerControllerViewType, "m_LayerScroll");
        internal static readonly FieldInfo LayerListField =
            AccessTools.Field(LayerControllerViewType, "m_LayerList");
        internal static readonly FieldInfo LayerViewHostField =
            AccessTools.Field(LayerControllerViewType, "m_Host");

        // Parameter view
        internal static readonly Type ParameterControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.ParameterControllerView");
        internal static readonly Type ParameterControllerViewElementType =
            AccessTools.Inner(ParameterControllerViewType, "Element");

        // ReorderableList scroll helpers
        internal static readonly MethodInfo GetElementHeightMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementHeight", new Type[] { typeof(int) });
        internal static readonly MethodInfo GetElementYOffsetMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementYOffset", new Type[] { typeof(int) });

        // AnimatorController internals
        internal static readonly FieldInfo AnimatorControllerDirtyField =
            AccessTools.Field(typeof(UnityEditor.Animations.AnimatorController), "OnAnimatorControllerDirty");

        // AnimatorControllerTool access
        internal static readonly MethodInfo AnimatorControllerGetter =
            AccessTools.PropertyGetter(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimatorControllerTool"),
                "animatorController");
        internal static readonly MethodInfo AddNewLayerMethod =
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "AddNewLayer");
        internal static readonly PropertyInfo SelectedLayerIndexProperty =
            AccessTools.Property(AnimatorEditorInit.AnimatorControllerToolType, "selectedLayerIndex");

        // LayerControllerView rename
        internal static readonly FieldInfo LayerSelectedIndexField =
            AccessTools.Field(LayerControllerViewType, "m_SelectedLayerIndex");
        internal static readonly PropertyInfo LayerRenameOverlayProperty =
            AccessTools.Property(LayerControllerViewType, "renameOverlay");
        internal static readonly MethodInfo LayerRenameEndMethod =
            AccessTools.Method(LayerControllerViewType, "RenameEnd");

        // ParameterControllerView rename
        internal static readonly FieldInfo ParameterRenameOverlayField =
            AccessTools.Field(ParameterControllerViewType, "m_RenameOverlay");
        internal static readonly MethodInfo ParameterRebuildListMethod =
            AccessTools.Method(ParameterControllerViewType, "RebuildList");
        internal static readonly MethodInfo ParameterRenameEndMethod =
            AccessTools.Method(ParameterControllerViewType, "RenameEnd");
        internal static readonly FieldInfo ParameterViewHostField =
            AccessTools.Field(ParameterControllerViewType, "m_Host");
        internal static readonly FieldInfo ParameterListField = FindReorderableListField(ParameterControllerViewType);

        // AnimationWindow internals (shared)
        internal static readonly Type AnimationWindowClipPopupType =
            AccessTools.TypeByName("UnityEditor.AnimationWindowClipPopup");
        internal static readonly MethodInfo AnimationWindowEditAnimationClipMethod =
            AccessTools.Method(typeof(AnimationWindow), "EditAnimationClip", new[] { typeof(AnimationClip) });

        // RenameOverlay (shared)
        internal static readonly Type RenameOverlayType =
            AccessTools.TypeByName("UnityEditor.RenameOverlay");
        internal static readonly MethodInfo RenameOverlayIsRenamingMethod =
            AccessTools.Method(AccessTools.TypeByName("UnityEditor.RenameOverlay"), "IsRenaming");
        internal static readonly MethodInfo RenameOverlayBeginRenameMethod =
            AccessTools.Method(AccessTools.TypeByName("UnityEditor.RenameOverlay"), "BeginRename",
                new[] { typeof(string), typeof(int), typeof(float) });

        internal readonly struct LayerControllerViewProxy
        {
            readonly object _instance;

            internal LayerControllerViewProxy(object instance) => _instance = instance;

            internal bool IsValid => _instance != null && LayerScrollField != null && LayerListField != null;

            internal Vector2 LayerScroll
            {
                get => LayerScrollField != null ? (Vector2)(LayerScrollField.GetValue(_instance) ?? Vector2.zero) : Vector2.zero;
                set => LayerScrollField?.SetValue(_instance, value);
            }

            internal ReorderableList LayerList =>
                LayerListField?.GetValue(_instance) as ReorderableList;

            internal int LayerSelectedIndex =>
                LayerSelectedIndexField != null ? (int)(LayerSelectedIndexField.GetValue(_instance) ?? -1) : -1;
        }


        static FieldInfo FindReorderableListField(Type type)
        {
            if (type == null) return null;
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                if (field.FieldType == typeof(ReorderableList)) return field;
            return null;
        }

        internal static UnityEditor.Animations.AnimatorController GetOpenController()
        {
            var windows = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
            if (windows.Length == 0) return null;
            return AnimatorControllerGetter?.Invoke(windows[0], null)
                as UnityEditor.Animations.AnimatorController;
        }
    }
}
#endif
