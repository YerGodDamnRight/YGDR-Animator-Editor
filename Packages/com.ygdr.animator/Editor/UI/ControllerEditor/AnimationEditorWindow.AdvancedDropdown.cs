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
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        /* Opens an AdvancedDropdown listing all controller parameters below rect, invoking onSelected with the chosen name. */
        void ShowParameterDropdown(Rect rect, string currentParam, Action<string> onSelected)
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            new ParameterDropdown(_controller.parameters, currentParam, onSelected).ShowWithCheckmark(rect);
        }

        /* Opens an AdvancedDropdown listing controller parameters of a single type below rect, invoking onSelected with the chosen name. */
        void ShowParameterDropdown(Rect rect, string currentParam, AnimatorControllerParameterType filterType, Action<string> onSelected)
        {
            if (_controller == null) return;
            var filtered = _controller.parameters.Where(x => x.type == filterType).ToArray();
            if (filtered.Length == 0) return;
            new ParameterDropdown(filtered, currentParam, onSelected).ShowWithCheckmark(rect);
        }

        /* Opens an AdvancedDropdown listing layer names below rect, invoking onSelected with the chosen index. */
        void ShowLayerDropdown(Rect rect, string[] layerNames, int currentIndex, Action<int> onSelected)
        {
            string current = currentIndex >= 0 && currentIndex < layerNames.Length ? layerNames[currentIndex] : "";
            new ParameterDropdown(layerNames, current, name => onSelected(Array.IndexOf(layerNames, name)))
                .ShowWithCheckmark(rect);
        }

        class ParameterDropdown : AdvancedDropdown
        {
            static readonly FieldInfo    ItemIdField         = AccessTools.Field(typeof(AdvancedDropdownItem), "m_Id");
            static readonly FieldInfo    DataSourceField     = AccessTools.Field(typeof(AdvancedDropdown), "m_DataSource");
            static readonly PropertyInfo MaximumSizeProperty = AccessTools.Property(typeof(AdvancedDropdown), "maximumSize");
            static FieldInfo             _selectedIDsField;

            readonly AnimatorControllerParameter[] _parameters;
            readonly string[] _items;
            readonly string _currentParam;
            readonly Action<string> _onSelected;
            readonly float _maxHeight;
            ParameterItem _currentItem;

            internal ParameterDropdown(AnimatorControllerParameter[] parameters, string currentParam, Action<string> onSelected, float maxHeight = 350f)
                : base(new AdvancedDropdownState())
            {
                _parameters = parameters;
                _currentParam = currentParam;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
                minimumSize = new Vector2(200, 250);
            }

            internal ParameterDropdown(string[] items, string current, Action<string> onSelected, float maxHeight = 250f)
                : base(new AdvancedDropdownState())
            {
                _items = items;
                _currentParam = current;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
                minimumSize = new Vector2(200, 150);
            }

            internal void ShowWithCheckmark(Rect rect)
            {
                MaximumSizeProperty?.SetValue(this, new Vector2(10000f, _maxHeight));
                Show(rect);

                if (_currentItem == null || ItemIdField == null || DataSourceField == null) return;
                try
                {
                    var dataSource = DataSourceField.GetValue(this);
                    if (dataSource == null) return;
                    _selectedIDsField ??= AccessTools.Field(dataSource.GetType(), "m_SelectedIDs");
                    if (_selectedIDsField == null) return;
                    var selectedIDs = (List<int>)_selectedIDsField.GetValue(dataSource);
                    selectedIDs.Clear();
                    selectedIDs.Add((int)ItemIdField.GetValue(_currentItem));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AnimatorTools] ParameterDropdown checkmark: {e.Message}");
                }
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                _currentItem = null;
                if (_items != null)
                {
                    var root = new AdvancedDropdownItem("Layers");
                    foreach (var item in _items)
                    {
                        var dropdownItem = new ParameterItem(item, item);
                        if (item == _currentParam) _currentItem = dropdownItem;
                        root.AddChild(dropdownItem);
                    }
                    return root;
                }
                var parametersRoot = new AdvancedDropdownItem("Parameters");
                var groups = new Dictionary<string, AdvancedDropdownItem>();
                foreach (var param in _parameters)
                {
                    var parts = param.name.Split('/');
                    bool isCurrent = param.name == _currentParam;
                    if (parts.Length == 1)
                    {
                        var item = new ParameterItem(param.name, param.name);
                        if (isCurrent) _currentItem = item;
                        parametersRoot.AddChild(item);
                        continue;
                    }
                    var parent = parametersRoot;
                    string runningPath = null;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string groupPath = runningPath == null ? parts[i] : runningPath + "/" + parts[i];
                        runningPath = groupPath;
                        if (!groups.TryGetValue(groupPath, out var group))
                        {
                            group = new AdvancedDropdownItem(parts[i]);
                            parent.AddChild(group);
                            groups[groupPath] = group;
                        }
                        parent = group;
                    }
                    var leafItem = new ParameterItem(parts[parts.Length - 1], param.name);
                    if (isCurrent) _currentItem = leafItem;
                    parent.AddChild(leafItem);
                }
                return parametersRoot;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
                => _onSelected?.Invoke(item is ParameterItem parameterItem ? parameterItem.fullName : item.name);

            class ParameterItem : AdvancedDropdownItem
            {
                internal readonly string fullName;
                internal ParameterItem(string displayName, string fullName) : base(displayName)
                    => this.fullName = fullName;
            }
        }
    }
    internal class ClipDropdown : AdvancedDropdown
    {
        static readonly FieldInfo    ItemIdField         = AccessTools.Field(typeof(AdvancedDropdownItem), "m_Id");
        static readonly FieldInfo    DataSourceField     = AccessTools.Field(typeof(AdvancedDropdown), "m_DataSource");
        static readonly PropertyInfo MaximumSizeProperty = AccessTools.Property(typeof(AdvancedDropdown), "maximumSize");
        static FieldInfo             _selectedIDsField;

        readonly AnimatorController          _controller;
        readonly AnimatorControllerParameter _parameter;
        readonly HashSet<AnimationClip>      _alreadyLinked = new();
        readonly List<ClipItem>              _leafItems     = new();
        bool                                 _removeMode;

        internal static ClipDropdown ForRemove(AnimatorController controller, AnimatorControllerParameter parameter)
        {
            var dropdown = new ClipDropdown(controller, parameter);
            dropdown._removeMode = true;
            return dropdown;
        }

        internal ClipDropdown(AnimatorController controller, AnimatorControllerParameter parameter)
            : base(new AdvancedDropdownState())
        {
            _controller = controller;
            _parameter  = parameter;
            minimumSize = new Vector2(220, 300);

            foreach (var clip in CollectAllClips(controller))
            {
                if (AnimationUtility.GetCurveBindings(clip)
                    .Any(binding => binding.type == typeof(Animator) && binding.propertyName == parameter.name))
                    _alreadyLinked.Add(clip);
            }
        }

        internal void ShowWithCheckmarks(Rect rect)
        {
            MaximumSizeProperty?.SetValue(this, new Vector2(10000f, 400f));
            Show(rect);

            if (ItemIdField == null || DataSourceField == null || _leafItems.Count == 0) return;
            try
            {
                var dataSource = DataSourceField.GetValue(this);
                if (dataSource == null) return;
                _selectedIDsField ??= AccessTools.Field(dataSource.GetType(), "m_SelectedIDs");
                if (_selectedIDsField == null) return;
                var selectedIDs = (List<int>)_selectedIDsField.GetValue(dataSource);
                selectedIDs.Clear();
                foreach (var leafItem in _leafItems)
                    if (_alreadyLinked.Contains(leafItem.clip))
                        selectedIDs.Add((int)ItemIdField.GetValue(leafItem));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimatorTools] ClipDropdown checkmarks: {e.Message}");
            }
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            _leafItems.Clear();
            var root   = new AdvancedDropdownItem("Create AAP");
            var groups = new Dictionary<string, AdvancedDropdownItem>();

            foreach (var clip in CollectAllClips(_controller).OrderBy(c => c.name))
            {
                bool linked = _alreadyLinked.Contains(clip);
                if (_removeMode && !linked) continue;

                var    parts  = clip.name.Split('.');
                var    parent      = root;
                string runningPath = null;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string groupPath = runningPath == null ? parts[i] : runningPath + "." + parts[i];
                    runningPath = groupPath;
                    if (!groups.TryGetValue(groupPath, out var group))
                    {
                        group = new AdvancedDropdownItem(parts[i]);
                        parent.AddChild(group);
                        groups[groupPath] = group;
                    }
                    parent = group;
                }

                string leafLabel = !_removeMode && linked ? $"✓ {parts[parts.Length - 1]}" : parts[parts.Length - 1];
                var    clipItem  = new ClipItem(leafLabel, clip);
                if (!_removeMode && linked) clipItem.enabled = false;
                _leafItems.Add(clipItem);
                parent.AddChild(clipItem);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is not ClipItem clipItem) return;
            var binding = new EditorCurveBinding { type = typeof(Animator), path = "", propertyName = _parameter.name };
            if (_removeMode)
            {
                if (!_alreadyLinked.Contains(clipItem.clip)) return;
                Undo.RecordObject(clipItem.clip, "Remove AAP");
                AnimationUtility.SetEditorCurve(clipItem.clip, binding, null);
                EditorUtility.SetDirty(clipItem.clip);
            }
            else
            {
                if (_alreadyLinked.Contains(clipItem.clip)) return;
                var curve = AnimationCurve.Constant(0f, 0f, _parameter.defaultFloat);
                Undo.RecordObject(clipItem.clip, "Add AAP");
                AnimationUtility.SetEditorCurve(clipItem.clip, binding, curve);
                EditorUtility.SetDirty(clipItem.clip);
            }
        }

        static HashSet<AnimationClip> CollectAllClips(AnimatorController controller)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var layer in controller.layers)
                CollectClipsFromSM(layer.stateMachine, clips);
            return clips;
        }

        static void CollectClipsFromSM(AnimatorStateMachine sm, HashSet<AnimationClip> clips)
        {
            foreach (var childState in sm.states)
                CollectClipsFromMotion(childState.state.motion, clips);
            foreach (var childStateMachine in sm.stateMachines)
                CollectClipsFromSM(childStateMachine.stateMachine, clips);
        }

        static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion is AnimationClip clip && clip != null)
                clips.Add(clip);
            else if (motion is BlendTree blendTree)
                foreach (var child in blendTree.children)
                    CollectClipsFromMotion(child.motion, clips);
        }

        class ClipItem : AdvancedDropdownItem
        {
            internal readonly AnimationClip clip;
            internal ClipItem(string label, AnimationClip clip) : base(label) => this.clip = clip;
        }
    }
}
#endif
