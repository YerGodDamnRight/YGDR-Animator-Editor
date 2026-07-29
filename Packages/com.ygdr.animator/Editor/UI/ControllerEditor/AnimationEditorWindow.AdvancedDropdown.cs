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
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        /* Opens an AdvancedDropdown listing all controller parameters below rect, invoking onSelected with the chosen name. */
        void ShowParameterDropdown(Rect rect, string currentParam, Action<string> onSelected, bool includeNone = false)
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            new ParameterDropdown(_controller.parameters, currentParam, onSelected, includeNone: includeNone).ShowWithCheckmark(rect);
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

        class ParameterDropdown : YgdrAdvancedDropdownBase
        {
            readonly AnimatorControllerParameter[] _parameters;
            readonly string[] _items;
            readonly string _currentParam;
            readonly Action<string> _onSelected;
            readonly float _maxHeight;
            readonly bool _includeNone;
            ParameterItem _currentItem;

            internal ParameterDropdown(AnimatorControllerParameter[] parameters, string currentParam, Action<string> onSelected, float maxHeight = 350f, bool includeNone = false)
                : base(new Vector2(200, 250))
            {
                _parameters = parameters;
                _currentParam = currentParam;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
                _includeNone = includeNone;
            }

            internal ParameterDropdown(string[] items, string current, Action<string> onSelected, float maxHeight = 250f)
                : base(new Vector2(200, 150))
            {
                _items = items;
                _currentParam = current;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
            }

            internal void ShowWithCheckmark(Rect rect) => ShowCapped(rect, _maxHeight, getPreselect: () => _currentItem);

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
                if (_includeNone)
                {
                    var noneItem = new ParameterItem("[None]", "");
                    if (string.IsNullOrEmpty(_currentParam)) _currentItem = noneItem;
                    parametersRoot.AddChild(noneItem);
                }
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

    /* Shared base for the package's AdvancedDropdown subclasses (parameter/layer/clip/blendshape pickers).
       AdvancedDropdown has no public API to raise its built-in width/height ceiling, so every subclass needs
       the same reflection call before Show() - this centralizes it instead of repeating it per dropdown. */
    internal abstract class YgdrAdvancedDropdownBase : AdvancedDropdown
    {
        protected YgdrAdvancedDropdownBase(Vector2 minSize) : base(new AdvancedDropdownState())
        {
            minimumSize = minSize;
        }

        /* Uncaps AdvancedDropdown's internal maximumSize via reflection, shows it, and optionally pre-highlights
           the current selection. getPreselect is resolved AFTER Show() runs BuildRoot() - subclasses that track
           "current item" only populate it inside BuildRoot, so reading it any earlier would see a stale value. */
        protected void ShowCapped(Rect rect, float maxHeight, float maxWidth = 10000f, Func<AdvancedDropdownItem> getPreselect = null)
        {
            WindowPatchReflection.AdvancedDropdownMaximumSizeProperty?.SetValue(this, new Vector2(maxWidth, maxHeight));
            Show(rect);
            if (getPreselect != null) WindowPatchReflection.PreselectItem(this, getPreselect());
        }

        /* Multi-select variant - same reflection uncap, but pre-highlights every item in getPreselects()
           (e.g. all clips already linked to a parameter) instead of a single current value. */
        protected void ShowCapped(Rect rect, float maxHeight, Func<IEnumerable<AdvancedDropdownItem>> getPreselects)
        {
            WindowPatchReflection.AdvancedDropdownMaximumSizeProperty?.SetValue(this, new Vector2(10000f, maxHeight));
            Show(rect);
            var preselects = getPreselects?.Invoke();
            if (preselects != null) WindowPatchReflection.PreselectItems(this, preselects);
        }
    }

    internal class ClipDropdown : YgdrAdvancedDropdownBase
    {
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
            : base(new Vector2(220, 300))
        {
            _controller = controller;
            _parameter  = parameter;

            foreach (var clip in CollectAllClips(controller))
            {
                if (AnimationUtility.GetCurveBindings(clip)
                    .Any(binding => binding.type == typeof(Animator) && binding.propertyName == parameter.name))
                    _alreadyLinked.Add(clip);
            }
        }

        internal void ShowWithCheckmarks(Rect rect) => ShowCapped(rect, 400f,
            getPreselects: () => _leafItems.Where(leafItem => _alreadyLinked.Contains(leafItem.clip)));

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
