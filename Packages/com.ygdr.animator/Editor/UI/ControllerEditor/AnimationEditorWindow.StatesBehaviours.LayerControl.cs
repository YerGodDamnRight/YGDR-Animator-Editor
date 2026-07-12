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
using ReorderableList = UnityEditorInternal.ReorderableList;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Animator Layer Control section (multi-instance) ────────────────

        readonly Dictionary<string, bool> _layerControlFoldoutExpanded = new Dictionary<string, bool>();
        AnimatorState[] _activeLayerControlStates;
        Func<AnimatorState, VRCAnimatorLayerControl> _activeLayerControlResolver;

        void DrawVRCLayerControlSection()
        {
            _activeLayerControlResolver = null;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAnimatorLayerControl>(state));

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.layer_control"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    foreach (var state in _selectedStates) AddInstance<VRCAnimatorLayerControl>(state, "Layer Control");
                    return; // maxCount below is stale after this mutation — redraw fresh next repaint.
                }
                if (maxCount > 0 && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemoveLayerControlFromAll();
                    return;
                }
            }

            if (maxCount == 0) return;

            var layerControlGroups = GroupInstancesByName<VRCAnimatorLayerControl>(_selectedStates);
            for (int i = 0; i < layerControlGroups.Count; i++)
                DrawLayerControlFoldout(layerControlGroups[i].name, layerControlGroups[i].states, i == 0, i == layerControlGroups.Count - 1);

            _activeLayerControlResolver = null;
        }

        void DrawLayerControlFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            bool removeRequested = DrawInstanceFoldoutHeader<VRCAnimatorLayerControl>(name, statesWithName, _layerControlFoldoutExpanded, isFirst, isLast, out bool expanded, out bool moveUp, out bool moveDown);

            if (moveUp || moveDown)
            {
                MoveNamedInstance<VRCAnimatorLayerControl>(name, statesWithName, moveUp ? -1 : 1);
                return; // order changed — redraw fresh next repaint.
            }

            if (removeRequested)
            {
                RemoveNamedInstance<VRCAnimatorLayerControl>(name, statesWithName);
                return;
            }

            if (!expanded) return;

            _activeLayerControlStates = statesWithName;
            _activeLayerControlResolver = state => FindInstance<VRCAnimatorLayerControl>(state, name);
            // Header may have just renamed this instance this frame — bail rather than index an empty
            // statesWithControl array below; next repaint recomputes namesUnion with the new name.
            if (!_activeLayerControlStates.Any(state => _activeLayerControlResolver(state) != null)) return;
            DrawLayerControlInstanceBody();
        }

        void DrawLayerControlInstanceBody()
        {
            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLayerControlFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLayerControlFields()
        {
            var statesWithControl = _activeLayerControlStates.Where(state => GetLayerControlForState(state) != null).ToArray();
            var first = GetLayerControlForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer_control.playable"), L10n.Get("vrc.tooltip.playable_layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).playable != first.playable);
                EditorGUI.BeginChangeCheck();
                var newPlayable = (VRC_AnimatorLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.playable);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activeLayerControlStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Playable");
                        control.playable = newPlayable;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.sub_layer_index")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                int newLayer = Mathf.Max(0, EditorGUILayout.IntField(first.layer));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activeLayerControlStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activeLayerControlStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration_layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activeLayerControlStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activeLayerControlStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        VRCAnimatorLayerControl GetLayerControlForState(AnimatorState state)
            => _activeLayerControlResolver != null ? _activeLayerControlResolver(state) : InstanceAt<VRCAnimatorLayerControl>(state, 0);

        static bool HasAnyLayerControl(AnimatorState state) => HasInstance<VRCAnimatorLayerControl>(state);

        VRCAnimatorLayerControl GetOrCreateLayerControl(AnimatorState state)
        {
            if (_activeLayerControlResolver != null)
            {
                var resolved = _activeLayerControlResolver(state);
                if (resolved != null) return resolved;
            }
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

        // ── VRC Playable Layer Control section (multi-instance) ────────────────

        readonly Dictionary<string, bool> _playableLayerFoldoutExpanded = new Dictionary<string, bool>();
        AnimatorState[] _activePlayableLayerStates;
        Func<AnimatorState, VRCPlayableLayerControl> _activePlayableLayerResolver;

        void DrawVRCPlayableLayerSection()
        {
            _activePlayableLayerResolver = null;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCPlayableLayerControl>(state));

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.playable_layer"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    foreach (var state in _selectedStates) AddInstance<VRCPlayableLayerControl>(state, "Playable Layer");
                    return; // maxCount below is stale after this mutation — redraw fresh next repaint.
                }
                if (maxCount > 0 && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemovePlayableLayerFromAll();
                    return;
                }
            }

            if (maxCount == 0) return;

            var playableLayerGroups = GroupInstancesByName<VRCPlayableLayerControl>(_selectedStates);
            for (int i = 0; i < playableLayerGroups.Count; i++)
                DrawPlayableLayerFoldout(playableLayerGroups[i].name, playableLayerGroups[i].states, i == 0, i == playableLayerGroups.Count - 1);

            _activePlayableLayerResolver = null;
        }

        void DrawPlayableLayerFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            bool removeRequested = DrawInstanceFoldoutHeader<VRCPlayableLayerControl>(name, statesWithName, _playableLayerFoldoutExpanded, isFirst, isLast, out bool expanded, out bool moveUp, out bool moveDown);

            if (moveUp || moveDown)
            {
                MoveNamedInstance<VRCPlayableLayerControl>(name, statesWithName, moveUp ? -1 : 1);
                return; // order changed — redraw fresh next repaint.
            }

            if (removeRequested)
            {
                RemoveNamedInstance<VRCPlayableLayerControl>(name, statesWithName);
                return;
            }

            if (!expanded) return;

            _activePlayableLayerStates = statesWithName;
            _activePlayableLayerResolver = state => FindInstance<VRCPlayableLayerControl>(state, name);
            // Header may have just renamed this instance this frame — bail rather than index an empty
            // statesWithControl array below; next repaint recomputes namesUnion with the new name.
            if (!_activePlayableLayerStates.Any(state => _activePlayableLayerResolver(state) != null)) return;
            DrawPlayableLayerInstanceBody();
        }

        void DrawPlayableLayerInstanceBody()
        {
            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayableLayerFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPlayableLayerFields()
        {
            var statesWithControl = _activePlayableLayerStates.Where(state => GetPlayableLayerForState(state) != null).ToArray();
            var first = GetPlayableLayerForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                var newLayer = (VRC_PlayableLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.layer);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activePlayableLayerStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activePlayableLayerStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activePlayableLayerStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _activePlayableLayerStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        VRCPlayableLayerControl GetPlayableLayerForState(AnimatorState state)
            => _activePlayableLayerResolver != null ? _activePlayableLayerResolver(state) : InstanceAt<VRCPlayableLayerControl>(state, 0);

        static bool HasAnyPlayableLayer(AnimatorState state) => HasInstance<VRCPlayableLayerControl>(state);

        VRCPlayableLayerControl GetOrCreatePlayableLayer(AnimatorState state)
        {
            if (_activePlayableLayerResolver != null)
            {
                var resolved = _activePlayableLayerResolver(state);
                if (resolved != null) return resolved;
            }
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
