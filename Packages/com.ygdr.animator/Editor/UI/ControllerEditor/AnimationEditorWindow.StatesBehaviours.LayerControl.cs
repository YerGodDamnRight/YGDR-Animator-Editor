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


#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Animator Layer Control + Playable Layer Control sections (native, multi-instance) ─────

        readonly Dictionary<string, bool> _layerControlFoldoutExpanded = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> _playableLayerFoldoutExpanded = new Dictionary<string, bool>();

        VisualElement _layerControlSection, _playableLayerSection;
        Button _layerControlRemoveButton;
        VisualElement _layerControlRows;
        Button _playableLayerRemoveButton;
        VisualElement _playableLayerRows;

        VisualElement BuildLayerControlBody()
        {
            var root = new VisualElement();
            root.AddToClassList("ygdr-behavior-group");

            _layerControlSection = BuildBehaviorSectionShell(L10n.Get("vrc.layer_control"),
                out _layerControlRemoveButton, out _layerControlRows);
            _layerControlRemoveButton.clicked += () =>
            {
                RemoveLayerControlFromAll();
                RefreshLayerControlSection();
            };
            root.Add(_layerControlSection);

            _playableLayerSection = BuildBehaviorSectionShell(L10n.Get("vrc.playable_layer"),
                out _playableLayerRemoveButton, out _playableLayerRows);
            _playableLayerRemoveButton.clicked += () =>
            {
                RemovePlayableLayerFromAll();
                RefreshPlayableLayerSection();
            };
            root.Add(_playableLayerSection);

            return root;
        }

        void RefreshLayerControlBody()
        {
            RefreshLayerControlSection();
            RefreshPlayableLayerSection();
        }

        /* Entry points for the top-level Add Behavior dropdown — always available since both allow duplicates. */
        void AddLayerControlBehaviorToSelected()
        {
            foreach (var state in _selectedStates) AddInstance<VRCAnimatorLayerControl>(state, "Layer Control");
            RefreshLayerControlSection();
        }

        void AddPlayableLayerBehaviorToSelected()
        {
            foreach (var state in _selectedStates) AddInstance<VRCPlayableLayerControl>(state, "Playable Layer");
            RefreshPlayableLayerSection();
        }

        void RefreshLayerControlSection()
        {
            if (_layerControlRows == null) return;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAnimatorLayerControl>(state));
            _layerControlSection.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _layerControlRemoveButton.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _layerControlRows.Clear();
            if (maxCount == 0) return;

            var groups = GroupInstancesByName<VRCAnimatorLayerControl>(_selectedStates);
            for (int i = 0; i < groups.Count; i++)
                _layerControlRows.Add(BuildLayerControlFoldout(groups[i].name, groups[i].states, i == 0, i == groups.Count - 1));
        }

        VisualElement BuildLayerControlFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-behavior-instance-container");

            var body = BuildLayerControlInstanceBody(statesWithName, state => FindInstance<VRCAnimatorLayerControl>(state, name));
            body.style.display = IsExpandedByDefault(_layerControlFoldoutExpanded, name) ? DisplayStyle.Flex : DisplayStyle.None;

            var header = BuildInstanceFoldoutHeader<VRCAnimatorLayerControl>(name, statesWithName, _layerControlFoldoutExpanded,
                isFirst, isLast, out _, expandedNow => body.style.display = expandedNow ? DisplayStyle.Flex : DisplayStyle.None,
                RefreshLayerControlSection);

            container.Add(header);
            container.Add(body);
            return container;
        }

        static bool IsExpandedByDefault(Dictionary<string, bool> expandedByName, string name)
            => !expandedByName.TryGetValue(name, out var stored) || stored;

        VisualElement BuildLayerControlInstanceBody(AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorLayerControl> resolver)
        {
            var body = new VisualElement();
            body.AddToClassList("ygdr-behavior-instance-body");
            body.style.backgroundColor = SharedWindowStyles.SecondaryColor;

            var statesWithControl = statesWithName.Where(state => resolver(state) != null).ToArray();
            if (statesWithControl.Length == 0) return body;
            var first = resolver(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            var playableField = new EnumField(first.playable) { showMixedValue = multi && statesWithControl.Any(state => resolver(state).playable != first.playable) };
            StyleAccentPopupField(playableField);
            playableField.RegisterValueChangedCallback(evt =>
            {
                var newPlayable = (VRC_AnimatorLayerControl.BlendableLayer)evt.newValue;
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreateLayerControl(state, resolver);
                    Undo.RecordObject(control, "Edit Layer Control Playable");
                    control.playable = newPlayable;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.layer_control.playable"), L10n.Get("vrc.tooltip.playable_layer"), playableField));

            var layerField = new IntegerField { value = first.layer, showMixedValue = multi && statesWithControl.Any(state => resolver(state).layer != first.layer) };
            layerField.RegisterValueChangedCallback(evt =>
            {
                int newLayer = Mathf.Max(0, evt.newValue);
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreateLayerControl(state, resolver);
                    Undo.RecordObject(control, "Edit Layer Control Layer");
                    control.layer = newLayer;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.sub_layer_index"), layerField));

            var goalWeightField = new Slider(0f, 1f) { value = first.goalWeight, showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(resolver(state).goalWeight, first.goalWeight)) };
            goalWeightField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreateLayerControl(state, resolver);
                    Undo.RecordObject(control, "Edit Layer Control Goal Weight");
                    control.goalWeight = evt.newValue;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight"), goalWeightField));

            var blendDurationField = new FloatField { value = first.blendDuration, showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(resolver(state).blendDuration, first.blendDuration)) };
            blendDurationField.RegisterValueChangedCallback(evt =>
            {
                float newBlendDuration = Mathf.Max(0f, evt.newValue);
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreateLayerControl(state, resolver);
                    Undo.RecordObject(control, "Edit Layer Control Blend Duration");
                    control.blendDuration = newBlendDuration;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration_layer"), blendDurationField));

            var debugStringField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithControl.Any(state => resolver(state).debugString != first.debugString) };
            debugStringField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreateLayerControl(state, resolver);
                    Undo.RecordObject(control, "Edit Debug String");
                    control.debugString = evt.newValue;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string"), debugStringField));

            return body;
        }

        static VRCAnimatorLayerControl GetOrCreateLayerControl(AnimatorState state, Func<AnimatorState, VRCAnimatorLayerControl> resolver)
        {
            var resolved = resolver(state);
            if (resolved != null) return resolved;
            var existing = InstanceAt<VRCAnimatorLayerControl>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCAnimatorLayerControl>(state, "Layer Control");
        }

        void RemoveLayerControlFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var controls = Instances<VRCAnimatorLayerControl>(state);
                if (controls.Count == 0) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Animator Layer Control");
                state.behaviours = state.behaviours.Where(b => !(b is VRCAnimatorLayerControl)).ToArray();
                foreach (var control in controls) Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
            _layerControlFoldoutExpanded.Clear();
        }

        // ── Playable Layer ───────────────────────────────────────────────────

        void RefreshPlayableLayerSection()
        {
            if (_playableLayerRows == null) return;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCPlayableLayerControl>(state));
            _playableLayerSection.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _playableLayerRemoveButton.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _playableLayerRows.Clear();
            if (maxCount == 0) return;

            var groups = GroupInstancesByName<VRCPlayableLayerControl>(_selectedStates);
            for (int i = 0; i < groups.Count; i++)
                _playableLayerRows.Add(BuildPlayableLayerFoldout(groups[i].name, groups[i].states, i == 0, i == groups.Count - 1));
        }

        VisualElement BuildPlayableLayerFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-behavior-instance-container");

            var body = BuildPlayableLayerInstanceBody(statesWithName, state => FindInstance<VRCPlayableLayerControl>(state, name));
            body.style.display = IsExpandedByDefault(_playableLayerFoldoutExpanded, name) ? DisplayStyle.Flex : DisplayStyle.None;

            var header = BuildInstanceFoldoutHeader<VRCPlayableLayerControl>(name, statesWithName, _playableLayerFoldoutExpanded,
                isFirst, isLast, out _, expandedNow => body.style.display = expandedNow ? DisplayStyle.Flex : DisplayStyle.None,
                RefreshPlayableLayerSection);

            container.Add(header);
            container.Add(body);
            return container;
        }

        VisualElement BuildPlayableLayerInstanceBody(AnimatorState[] statesWithName, Func<AnimatorState, VRCPlayableLayerControl> resolver)
        {
            var body = new VisualElement();
            body.AddToClassList("ygdr-behavior-instance-body");
            body.style.backgroundColor = SharedWindowStyles.SecondaryColor;

            var statesWithControl = statesWithName.Where(state => resolver(state) != null).ToArray();
            if (statesWithControl.Length == 0) return body;
            var first = resolver(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            var layerField = new EnumField(first.layer) { showMixedValue = multi && statesWithControl.Any(state => resolver(state).layer != first.layer) };
            StyleAccentPopupField(layerField);
            layerField.RegisterValueChangedCallback(evt =>
            {
                var newLayer = (VRC_PlayableLayerControl.BlendableLayer)evt.newValue;
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreatePlayableLayer(state, resolver);
                    Undo.RecordObject(control, "Edit Playable Layer");
                    control.layer = newLayer;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.layer"), layerField));

            var goalWeightField = new Slider(0f, 1f) { value = first.goalWeight, showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(resolver(state).goalWeight, first.goalWeight)) };
            goalWeightField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreatePlayableLayer(state, resolver);
                    Undo.RecordObject(control, "Edit Playable Layer Goal Weight");
                    control.goalWeight = evt.newValue;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight"), goalWeightField));

            var blendDurationField = new FloatField { value = first.blendDuration, showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(resolver(state).blendDuration, first.blendDuration)) };
            blendDurationField.RegisterValueChangedCallback(evt =>
            {
                float newBlendDuration = Mathf.Max(0f, evt.newValue);
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreatePlayableLayer(state, resolver);
                    Undo.RecordObject(control, "Edit Playable Layer Blend Duration");
                    control.blendDuration = newBlendDuration;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration"), blendDurationField));

            var debugStringField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithControl.Any(state => resolver(state).debugString != first.debugString) };
            debugStringField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in statesWithName)
                {
                    var control = GetOrCreatePlayableLayer(state, resolver);
                    Undo.RecordObject(control, "Edit Debug String");
                    control.debugString = evt.newValue;
                    EditorUtility.SetDirty(control);
                }
            });
            body.Add(BuildBehaviorFieldRow(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string"), debugStringField));

            return body;
        }

        static VRCPlayableLayerControl GetOrCreatePlayableLayer(AnimatorState state, Func<AnimatorState, VRCPlayableLayerControl> resolver)
        {
            var resolved = resolver(state);
            if (resolved != null) return resolved;
            var existing = InstanceAt<VRCPlayableLayerControl>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCPlayableLayerControl>(state, "Playable Layer");
        }

        void RemovePlayableLayerFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var controls = Instances<VRCPlayableLayerControl>(state);
                if (controls.Count == 0) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Playable Layer Control");
                state.behaviours = state.behaviours.Where(b => !(b is VRCPlayableLayerControl)).ToArray();
                foreach (var control in controls) Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
            _playableLayerFoldoutExpanded.Clear();
        }
    }
}
#endif
