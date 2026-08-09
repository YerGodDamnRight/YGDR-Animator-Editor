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
using UnityEditor.IMGUI.Controls;
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
            WarnIfNull(AnimationWindowStateProperty, "AnimationWindow.state");
            WarnIfNull(ActiveAnimationClipProperty, "AnimationWindowState.activeAnimationClip");
            WarnIfNull(ActiveRootGameObjectProperty, "AnimationWindowState.activeRootGameObject");
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
        internal static readonly PropertyInfo AnimatorControllerProperty =
            AccessTools.Property(AnimatorEditorInit.AnimatorControllerToolType, "animatorController");
        internal static readonly MethodInfo AnimatorControllerToolLiveLinkGetter =
            AccessTools.PropertyGetter(AnimatorEditorInit.AnimatorControllerToolType, "liveLink");
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

        // InspectorWindow (for scoped tracker rebuilds)
        internal static readonly Type InspectorWindowType =
            AccessTools.TypeByName("UnityEditor.InspectorWindow");
        internal static readonly PropertyInfo InspectorTrackerProperty =
            AccessTools.Property(InspectorWindowType, "tracker");

        // Rebuilds only the inspector(s) currently showing `target`, avoiding
        // a global ForceRebuild that tears down and re-inits unrelated editors.
        internal static void RebuildInspectorsShowing(UnityEngine.Object target)
        {
            if (InspectorWindowType == null) return;
            foreach (var win in Resources.FindObjectsOfTypeAll(InspectorWindowType))
            {
                var tracker = InspectorTrackerProperty?.GetValue(win) as ActiveEditorTracker;
                if (tracker == null) continue;
                if (tracker.activeEditors.Any(editor => editor.target == target))
                    tracker.ForceRebuild();
            }
        }

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
        // AnimationWindow.state internals (shared)
        internal static readonly PropertyInfo AnimationWindowStateProperty =
            AccessTools.Property(typeof(AnimationWindow), "state");
        internal static readonly Type AnimationWindowStateType =
            AnimationWindowStateProperty?.PropertyType;
        internal static readonly PropertyInfo ActiveAnimationClipProperty =
            AccessTools.Property(AnimationWindowStateType, "activeAnimationClip");
        internal static readonly PropertyInfo ActiveRootGameObjectProperty =
            AccessTools.Property(AnimationWindowStateType, "activeRootGameObject");
        internal static readonly PropertyInfo SelectionProperty =
            AccessTools.Property(AnimationWindowStateType, "selection");
        internal static readonly PropertyInfo CanChangeAnimationClipProperty =
            AccessTools.Property(AccessTools.TypeByName("UnityEditorInternal.AnimationWindowSelectionItem"), "canChangeAnimationClip");
        internal static readonly PropertyInfo CurrentTimeProperty =
            AccessTools.Property(AnimationWindowStateType, "currentTime");

        // AnimationWindowHierarchyGUI / AnimationWindowHierarchyNode / AnimationWindowCurve
        // internals (shared) — the dopesheet's row hierarchy panel and its per-row curve list.
        internal static readonly Type AnimationWindowHierarchyGUIType =
            AccessTools.TypeByName("UnityEditorInternal.AnimationWindowHierarchyGUI");
        internal static readonly PropertyInfo AnimationWindowHierarchyGUIStateProperty =
            AccessTools.Property(AnimationWindowHierarchyGUIType, "state");
        internal static readonly Type AnimationWindowHierarchyNodeType =
            AccessTools.TypeByName("UnityEditorInternal.AnimationWindowHierarchyNode");
        internal static readonly Type AnimationWindowHierarchyNodeListType =
            AnimationWindowHierarchyNodeType != null ? typeof(List<>).MakeGenericType(AnimationWindowHierarchyNodeType) : null;
        internal static readonly FieldInfo AnimationWindowHierarchyNodeCurvesField =
            AccessTools.Field(AnimationWindowHierarchyNodeType, "curves");
        internal static readonly Type AnimationWindowCurveType =
            AccessTools.TypeByName("UnityEditorInternal.AnimationWindowCurve");
        internal static readonly PropertyInfo AnimationWindowCurveClipProperty =
            AccessTools.Property(AnimationWindowCurveType, "clip");
        internal static readonly PropertyInfo AnimationWindowCurveBindingProperty =
            AccessTools.Property(AnimationWindowCurveType, "binding");

        // The popup doesn't reference its owning AnimationWindow directly, only the shared state object.
        internal static readonly FieldInfo AnimationWindowClipPopupStateField =
            AccessTools.Field(AnimationWindowClipPopupType, "state");

        // Finds the AnimationWindow whose `state` is the same object the popup was constructed with.
        internal static AnimationWindow FindWindowOwningState(object state)
        {
            if (state == null) return null;
            foreach (var window in Resources.FindObjectsOfTypeAll<AnimationWindow>())
                if (ReferenceEquals(AnimationWindowStateProperty?.GetValue(window), state))
                    return window;
            return null;
        }

        // AdvancedDropdown internals (shared)
        internal static readonly PropertyInfo AdvancedDropdownMaximumSizeProperty =
            AccessTools.Property(typeof(AdvancedDropdown), "maximumSize");
        internal static readonly FieldInfo AdvancedDropdownDataSourceField =
            AccessTools.Field(typeof(AdvancedDropdown), "m_DataSource");
        internal static readonly FieldInfo AdvancedDropdownItemIdField =
            AccessTools.Field(typeof(AdvancedDropdownItem), "m_Id");
        static FieldInfo _advancedDropdownSelectedIDsField;

        /* Marks item as the checked row on dropdown's data source, so the current value is pre-highlighted when shown. */
        internal static void PreselectItem(AdvancedDropdown dropdown, AdvancedDropdownItem item)
        {
            if (item == null) return;
            PreselectItems(dropdown, new[] { item });
        }

        /* Marks items as the checked rows on dropdown's data source (multi-select variant). */
        internal static void PreselectItems(AdvancedDropdown dropdown, System.Collections.Generic.IEnumerable<AdvancedDropdownItem> items)
        {
            if (AdvancedDropdownItemIdField == null || AdvancedDropdownDataSourceField == null) return;
            try
            {
                var dataSource = AdvancedDropdownDataSourceField.GetValue(dropdown);
                if (dataSource == null) return;
                _advancedDropdownSelectedIDsField ??= AccessTools.Field(dataSource.GetType(), "m_SelectedIDs");
                if (_advancedDropdownSelectedIDsField == null) return;
                var selectedIDs = (System.Collections.Generic.List<int>)_advancedDropdownSelectedIDsField.GetValue(dataSource);
                selectedIDs.Clear();
                foreach (var item in items)
                    selectedIDs.Add((int)AdvancedDropdownItemIdField.GetValue(item));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimatorTools] AdvancedDropdown checkmark: {e.Message}");
            }
        }

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


        internal readonly struct AnimationWindowStateProxy
        {
            readonly object _state;

            internal AnimationWindowStateProxy(AnimationWindow window)
                : this(AnimationWindowStateProperty?.GetValue(window)) { }

            AnimationWindowStateProxy(object state) => _state = state;

            internal static AnimationWindowStateProxy FromState(object state) => new(state);

            internal AnimationClip ActiveAnimationClip
            {
                get => ActiveAnimationClipProperty?.GetValue(_state) as AnimationClip;
                set => ActiveAnimationClipProperty?.SetValue(_state, value);
            }

            internal GameObject ActiveRootGameObject =>
                ActiveRootGameObjectProperty?.GetValue(_state) as GameObject;

            // The activeAnimationClip setter silently no-ops unless the current selection item owns a
            // root GameObject (AnimationClipSelectionItem hardcodes this to false).
            internal bool CanChangeAnimationClip
            {
                get
                {
                    var selection = SelectionProperty?.GetValue(_state);
                    return selection != null && CanChangeAnimationClipProperty?.GetValue(selection) is true;
                }
            }
        }

        static FieldInfo FindReorderableListField(Type type) => FindFieldOfType(type, typeof(ReorderableList));

        static FieldInfo FindFieldOfType(Type type, Type fieldType)
        {
            if (type == null) return null;
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                if (field.FieldType == fieldType) return field;
            return null;
        }


        // Cached window ref - Resources.FindObjectsOfTypeAll is a full-heap scan and this getter runs on
        // every OnGUI of the clip popup (PatchClipMenuAdvancedDropdown), so re-scanning every call caused
        // visible lag while scrolling/searching that dropdown.
        static UnityEngine.Object _cachedToolWindow;

        internal static UnityEditor.Animations.AnimatorController GetOpenController()
        {
            if (_cachedToolWindow != null)
            {
                var cachedController = AnimatorControllerGetter?.Invoke(_cachedToolWindow, null)
                    as UnityEditor.Animations.AnimatorController;
                if (cachedController != null) return cachedController;
            }

            // FindObjectsOfTypeAll also returns tool windows that were never shown and so never resolved
            // a controller, so pick one that actually has it rather than whichever comes back first.
            foreach (var window in Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType))
            {
                var controller = AnimatorControllerGetter?.Invoke(window, null)
                    as UnityEditor.Animations.AnimatorController;
                if (controller == null) continue;
                _cachedToolWindow = window;
                return controller;
            }
            return null;
        }
    }
}
#endif
