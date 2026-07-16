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
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        void DrawStatesTab()
        {
            var panelRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && panelRect.height > 0)
                EditorGUI.DrawRect(panelRect, Styles.PrimaryColor);

            if (_selectedStates.Length == 0)
                EditorGUILayout.LabelField(L10n.Get("states.empty"), Styles.EmptyLabel);
            else
                DrawStateRows();

            EditorGUILayout.Space(4);
            DrawStateAlignButtons();
            EditorGUILayout.Space(8);
            DrawStateProperties();
            EditorGUILayout.Space(EditorGUIUtility.singleLineHeight);
#if VRC_SDK_VRCSDK3
            EditorGUILayout.LabelField(L10n.Get("states.shared_behaviors"), Styles.FooterText);
            DrawVRCDriversSection();
            if (_selectedStates.Any(state => HasAnyDriver(state))) EditorGUILayout.Space(8);
            DrawVRCPlayAudioSection();
            if (_selectedStates.Any(state => HasAnyAudio(state))) EditorGUILayout.Space(8);
            DrawVRCTrackingSection();
            if (_selectedStates.Any(state => GetTrackingForState(state) != null)) EditorGUILayout.Space(8);
            DrawVRCLocomotionSection();
            if (_selectedStates.Any(state => GetLocomotionForState(state) != null)) EditorGUILayout.Space(8);
            DrawVRCLayerControlSection();
            if (_selectedStates.Any(state => HasAnyLayerControl(state))) EditorGUILayout.Space(8);
            DrawVRCPlayableLayerSection();
            if (_selectedStates.Any(state => HasAnyPlayableLayer(state))) EditorGUILayout.Space(8);
            DrawVRCPoseSpaceSection();
#endif

            EditorGUILayout.EndVertical();
        }

        // ── State list ────────────────────────────────────────────────────────

void DrawStateRows()
        {
            float rowHeight = EditorGUIUtility.singleLineHeight;
            const float gap = 4f;
            const float nameWidth = 140f;
            const float btnWidth = 44f;
            float toggleW = Styles.k_pillW;

            float totalH = gap + _selectedStates.Length * (rowHeight + gap);
            float maxVisibleH = 4f * (rowHeight + gap);
            float displayH = _stateRowScrollEnabled ? Mathf.Min(totalH, maxVisibleH) : totalH;

            var area = EditorGUILayout.GetControlRect(false, displayH + gap);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(area, Styles.SecondaryColor);

            var toggleRect = new Rect(area.xMax - toggleW, area.y, toggleW, area.height);
            _stateRowScrollEnabled = GUI.Toggle(toggleRect, _stateRowScrollEnabled, "", Styles.ScrollToggleBtn);
            EditorGUIUtility.AddCursorRect(toggleRect, MouseCursor.Link);

            var viewRect = new Rect(area.x, area.y, area.width - toggleW, area.height);

            if (_stateRowScrollEnabled && totalH > maxVisibleH)
            {
                var contentRect = new Rect(0, 0, viewRect.width, totalH + gap);
                _stateRowScrollPos = GUI.BeginScrollView(viewRect, _stateRowScrollPos, contentRect, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawStateRowsInto(contentRect, rowHeight, gap, nameWidth, btnWidth);
                GUI.EndScrollView();
            }
            else
            {
                DrawStateRowsInto(viewRect, rowHeight, gap, nameWidth, btnWidth);
            }
        }

        void DrawStateRowsInto(Rect area, float rowHeight, float gap, float nameWidth, float btnWidth)
        {
            float groupW = btnWidth + nameWidth + btnWidth;
            float groupX = area.x + (area.width - groupW) * 0.5f;
            float currentY = area.y + gap;

            foreach (var state in _selectedStates)
            {
                if (CursorBtn(new Rect(groupX, currentY, btnWidth, rowHeight), L10n.Get("states.in"), Styles.IconBtn))
                    SelectIncomingTransitions(_controller, new[] { state });

                var nameRect = new Rect(groupX + btnWidth, currentY, nameWidth, rowHeight);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(nameRect, Styles.SecondaryColor);
                GUI.Label(nameRect, TruncateToFit(state.name, Styles.StateRowName, nameWidth), Styles.StateRowName);

                if (CursorBtn(new Rect(groupX + btnWidth + nameWidth, currentY, btnWidth, rowHeight), L10n.Get("states.out"), Styles.IconBtn))
                    SelectOutgoingTransitions(new[] { state });

                currentY += rowHeight + gap;
            }
        }

        /* Truncates text to fit within maxWidth pixels using style's CalcSize, appending an ellipsis when trimmed. */
        static string TruncateToFit(string text, GUIStyle style, float maxWidth)
        {
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            string truncated = text;
            while (truncated.Length > 0 && style.CalcSize(new GUIContent(truncated + "…")).x > maxWidth)
                truncated = truncated[..^1];
            return truncated + "…";
        }

        // ── Align buttons ─────────────────────────────────────────────────────

        void DrawStateAlignButtons()
        {
            using (new EditorGUI.DisabledScope(_selectedStates.Length < 2))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (CursorBtn(L10n.Get("states.align_vertical"),   Styles.IconBtn)) AlignStates(vertical: true);
                if (CursorBtn(L10n.Get("states.align_horizontal"), Styles.IconBtn)) AlignStates(vertical: false);
            }
            using (new EditorGUI.DisabledScope(_selectedStates.Length < 3))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (CursorBtn(L10n.Get("states.distribute_vertical"),   Styles.IconBtn)) DistributeStates(vertical: true);
                if (CursorBtn(L10n.Get("states.distribute_horizontal"), Styles.IconBtn)) DistributeStates(vertical: false);
            }
        }

        // ── State properties ──────────────────────────────────────────────────

        void DrawStateProperties()
        {
            int count = _selectedStates.Length;
            bool empty = count == 0;
            bool multi = count > 1;
            var first = empty ? null : _selectedStates[0];

            using var disabled = new EditorGUI.DisabledScope(empty);
            var stateIcon = EditorGUIUtility.ObjectContent(null, typeof(AnimatorState)).image;
            float rowHeight  = EditorGUIUtility.singleLineHeight;
            float iconHeight = rowHeight * 2f + EditorGUIUtility.standardVerticalSpacing;

            DrawStateNameTagFields(stateIcon, iconHeight, multi, empty, first);
            EditorGUILayout.Space(10);
            DrawStateMotionField(multi, empty, first);
            DrawStateSpeedField(multi, empty, first);
            DrawStateMultiplierField(multi, empty, first);
            DrawStateMotionTimeField(multi, empty, first);
            DrawStateMirrorField(multi, empty, first);
            DrawStateCycleOffsetField(multi, empty, first);
            DrawStateFootIKField(multi, empty, first);
            DrawStateWriteDefaultsField(multi, empty, first);
        }

        void DrawStateNameTagFields(Texture stateIcon, float iconHeight, bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var iconRect = EditorGUILayout.GetControlRect(false, iconHeight, GUILayout.Width(iconHeight));
                if (stateIcon != null)
                    GUI.DrawTexture(iconRect, stateIcon, ScaleMode.ScaleToFit);

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(L10n.Get("states.name"), GUILayout.Width(80));
                        EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.name != first.name);
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField(empty ? "" : first.name);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (multi)
                            {
                                var layerStateNames = CollectLayerStateNamesExcluding(_selectedStates);
                                int nextIndex = 1;
                                for (int i = 0; i < _selectedStates.Length; i++)
                                {
                                    string candidate;
                                    if (i == 0) { candidate = newName; }
                                    else { do { candidate = newName + " " + nextIndex++; } while (layerStateNames.Contains(candidate)); }
                                    layerStateNames.Add(candidate);
                                    Undo.RecordObject(_selectedStates[i], "Edit State");
                                    _selectedStates[i].name = candidate;
                                    EditorUtility.SetDirty(_selectedStates[i]);
                                }
                            }
                            else
                            {
                                SetStateOnAll(state => state.name = newName);
                            }
                        }
                        EditorGUI.showMixedValue = false;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(L10n.Get("states.tag"), GUILayout.Width(80));
                        EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.tag != first.tag);
                        EditorGUI.BeginChangeCheck();
                        string newTag = EditorGUILayout.TextField(empty ? "" : first.tag);
                        if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.tag = newTag);
                        EditorGUI.showMixedValue = false;
                    }
                }
            }
        }

        void DrawStateMotionField(bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.motion"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.motion != first.motion);
                EditorGUI.BeginChangeCheck();
                var newMotion = (Motion)EditorGUILayout.ObjectField(empty ? null : first.motion, typeof(Motion), false);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.motion = newMotion);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateSpeedField(bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.speed"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.speed, first.speed));
                EditorGUI.BeginChangeCheck();
                float newSpeed = EditorGUILayout.FloatField(empty ? 1f : first.speed);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.speed = newSpeed);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateMultiplierField(bool multi, bool empty, AnimatorState first)
        {
            bool speedParamActive = !empty && first.speedParameterActive;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!speedParamActive))
                {
                    EditorGUILayout.LabelField(L10n.Get("states.multiplier"), GUILayout.Width(110));
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.speedParameter != first.speedParameter);
                    DrawFloatParamDropdown(empty ? "" : first.speedParameter,
                        newSpeedParameter => SetStateOnAll(state => state.speedParameter = newSpeedParameter),
                        GUILayout.ExpandWidth(true));
                    EditorGUI.showMixedValue = false;
                    GUILayout.Space(EditorGUIUtility.currentViewWidth * ParamDropdownTogglePaddingPercent);
                }
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.speedParameterActive != first.speedParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newSpeedActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"),empty ? false : first.speedParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.speedParameterActive = newSpeedActive);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateMotionTimeField(bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.motion_time"), GUILayout.Width(110));
                if (!empty && first.timeParameterActive)
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.timeParameter != first.timeParameter);
                    DrawFloatParamDropdown(first.timeParameter,
                        newTimeParameter => SetStateOnAll(state => state.timeParameter = newTimeParameter),
                        GUILayout.ExpandWidth(true));
                    EditorGUI.showMixedValue = false;
                    GUILayout.Space(EditorGUIUtility.currentViewWidth * ParamDropdownTogglePaddingPercent);
                }
                else GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.timeParameterActive != first.timeParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newTimeActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"),empty ? false : first.timeParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.timeParameterActive = newTimeActive);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateMirrorField(bool multi, bool empty, AnimatorState first)
        {
            bool mirrorParamActive = !empty && first.mirrorParameterActive;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.mirror"), GUILayout.Width(110));
                if (mirrorParamActive)
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.mirrorParameter != first.mirrorParameter);
                    DrawBoolParamDropdown(empty ? "" : first.mirrorParameter,
                        newMirrorParameter => SetStateOnAll(state => state.mirrorParameter = newMirrorParameter),
                        GUILayout.ExpandWidth(true));
                    EditorGUI.showMixedValue = false;
                    GUILayout.Space(EditorGUIUtility.currentViewWidth * ParamDropdownTogglePaddingPercent);
                }
                else
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.mirror != first.mirror);
                    EditorGUI.BeginChangeCheck();
                    bool newMirror = EditorGUILayout.Toggle(empty ? false : first.mirror, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.mirror = newMirror);
                    EditorGUI.showMixedValue = false;
                    GUILayout.FlexibleSpace();
                }
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.mirrorParameterActive != first.mirrorParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newMirrorActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"),empty ? false : first.mirrorParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.mirrorParameterActive = newMirrorActive);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateCycleOffsetField(bool multi, bool empty, AnimatorState first)
        {
            bool cycleOffsetParamActive = !empty && first.cycleOffsetParameterActive;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.cycle_offset"), GUILayout.Width(110));
                if (cycleOffsetParamActive)
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.cycleOffsetParameter != first.cycleOffsetParameter);
                    DrawFloatParamDropdown(empty ? "" : first.cycleOffsetParameter,
                        newCycleOffsetParameter => SetStateOnAll(state => state.cycleOffsetParameter = newCycleOffsetParameter),
                        GUILayout.ExpandWidth(true));
                    EditorGUI.showMixedValue = false;
                    GUILayout.Space(EditorGUIUtility.currentViewWidth * ParamDropdownTogglePaddingPercent);
                }
                else
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.cycleOffset, first.cycleOffset));
                    EditorGUI.BeginChangeCheck();
                    float newCycleOffset = EditorGUILayout.FloatField(empty ? 0f : first.cycleOffset);
                    if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.cycleOffset = newCycleOffset);
                    EditorGUI.showMixedValue = false;
                    GUILayout.FlexibleSpace();
                }
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.cycleOffsetParameterActive != first.cycleOffsetParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newOffsetParameterActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"), empty ? false : first.cycleOffsetParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.cycleOffsetParameterActive = newOffsetParameterActive);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateFootIKField(bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.foot_ik"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.iKOnFeet != first.iKOnFeet);
                EditorGUI.BeginChangeCheck();
                bool newFootIK = EditorGUILayout.Toggle(empty ? false : first.iKOnFeet, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.iKOnFeet = newFootIK);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawStateWriteDefaultsField(bool multi, bool empty, AnimatorState first)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("states.write_defaults"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.writeDefaultValues != first.writeDefaultValues);
                EditorGUI.BeginChangeCheck();
                bool newWriteDefaults = EditorGUILayout.Toggle(empty ? true : first.writeDefaultValues, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.writeDefaultValues = newWriteDefaults);
                EditorGUI.showMixedValue = false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        AnimatorController _paramCachedController;
        string[] _cachedIntParams   = Array.Empty<string>();
        string[] _cachedFloatParams = Array.Empty<string>();
        string[] _cachedBoolParams  = Array.Empty<string>();

        void RebuildParamNameCaches()
        {
            _paramCachedController = _controller;
            if (_controller == null)
            {
                _cachedIntParams   = Array.Empty<string>();
                _cachedFloatParams = Array.Empty<string>();
                _cachedBoolParams  = Array.Empty<string>();
                return;
            }
            var parameters     = _controller.parameters;
            _cachedIntParams   = parameters.Where(x => x.type == AnimatorControllerParameterType.Int).Select(x => x.name).ToArray();
            _cachedFloatParams = parameters.Where(x => x.type == AnimatorControllerParameterType.Float).Select(x => x.name).ToArray();
            _cachedBoolParams  = parameters.Where(x => x.type == AnimatorControllerParameterType.Bool).Select(x => x.name).ToArray();
        }

        const float ParamDropdownArrowWidth = 18f;
        const float ParamDropdownTogglePaddingPercent = 0.05f;

        /* Draws an AdvancedDropdown button listing all Int parameters in the active controller; invokes onSelected with the chosen name. */
        void DrawIntParamDropdown(string current, Action<string> onSelected)
        {
            if (_paramCachedController != _controller) RebuildParamNameCaches();

            if (_cachedIntParams.Length == 0)
            {
                GUILayout.Label(L10n.Get("states.no_int_float_params"), EditorStyles.miniLabel);
                return;
            }

            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
            string label = EditorGUI.showMixedValue ? "—" : (string.IsNullOrEmpty(current) ? "—" : current);
            label = TruncateTextLeft(label, EditorStyles.popup, rect.width - ParamDropdownArrowWidth);
            if (EditorGUI.DropdownButton(rect, new GUIContent(label), FocusType.Passive))
                ShowParameterDropdown(rect, current, AnimatorControllerParameterType.Int, onSelected);
        }

        /* Draws an AdvancedDropdown button listing all Float parameters in the active controller; invokes onSelected with the chosen name. */
        void DrawFloatParamDropdown(string current, Action<string> onSelected, params GUILayoutOption[] options)
        {
            if (_paramCachedController != _controller) RebuildParamNameCaches();

            if (_cachedFloatParams.Length == 0)
            {
                GUILayout.Label(string.IsNullOrEmpty(current) ? "—" : current, EditorStyles.miniLabel, options);
                return;
            }

            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, options);
            string label = EditorGUI.showMixedValue ? "—" : (string.IsNullOrEmpty(current) ? "—" : current);
            label = TruncateTextLeft(label, EditorStyles.popup, rect.width - ParamDropdownArrowWidth);
            if (EditorGUI.DropdownButton(rect, new GUIContent(label), FocusType.Passive))
                ShowParameterDropdown(rect, current, AnimatorControllerParameterType.Float, onSelected);
        }

        /* Draws an AdvancedDropdown button listing all Bool parameters in the active controller; invokes onSelected with the chosen name. */
        void DrawBoolParamDropdown(string current, Action<string> onSelected, params GUILayoutOption[] options)
        {
            if (_paramCachedController != _controller) RebuildParamNameCaches();

            if (_cachedBoolParams.Length == 0)
            {
                GUILayout.Label(string.IsNullOrEmpty(current) ? "—" : current, EditorStyles.miniLabel, options);
                return;
            }

            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, options);
            string label = EditorGUI.showMixedValue ? "—" : (string.IsNullOrEmpty(current) ? "—" : current);
            label = TruncateTextLeft(label, EditorStyles.popup, rect.width - ParamDropdownArrowWidth);
            if (EditorGUI.DropdownButton(rect, new GUIContent(label), FocusType.Passive))
                ShowParameterDropdown(rect, current, AnimatorControllerParameterType.Bool, onSelected);
        }

        /* Sets Selection.objects to all outgoing transitions from every state in states. */
        internal static void SelectOutgoingTransitions(AnimatorState[] states)
        {
            SelectTransitionsAndFocusAnimator(states
                .SelectMany(state => state.transitions));
        }

        /* Sets Selection.objects to all transitions in the active layer that point to any state in states. */
        internal static void SelectIncomingTransitions(AnimatorController controller, AnimatorState[] states)
        {
            if (controller == null) return;
            var targets = new HashSet<AnimatorState>(states);
            var sm = GetActiveLayerStateMachine(controller);
            var incoming = new List<AnimatorStateTransition>();
            CollectIncoming(sm, targets, incoming);
            var entryIncoming = new List<AnimatorTransition>();
            CollectIncomingEntryTransitions(sm, targets, entryIncoming);
            Selection.objects = incoming.Cast<UnityEngine.Object>().Concat(entryIncoming).ToArray();
            FocusAnimatorWindow();
        }

        /* Sets Selection.objects to all incoming and outgoing transitions for every state in states (active layer only). */
        internal static void SelectBothTransitions(AnimatorController controller, AnimatorState[] states)
        {
            if (controller == null) return;
            var targets = new HashSet<AnimatorState>(states);
            var sm = GetActiveLayerStateMachine(controller);
            var incoming = new List<AnimatorStateTransition>();
            CollectIncoming(sm, targets, incoming);
            var entryIncoming = new List<AnimatorTransition>();
            CollectIncomingEntryTransitions(sm, targets, entryIncoming);
            Selection.objects = incoming.Cast<UnityEngine.Object>()
                .Concat(entryIncoming)
                .Concat(states.SelectMany(state => state.transitions))
                .ToArray();
            FocusAnimatorWindow();
        }

        static AnimatorStateMachine GetActiveLayerStateMachine(AnimatorController controller)
        {
            var tool = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType).FirstOrDefault();
            if (tool == null) return controller.layers[0].stateMachine;
            var idx = (int)WindowPatchReflection.SelectedLayerIndexProperty.GetValue(tool);
            if ((uint)idx >= (uint)controller.layers.Length) return controller.layers[0].stateMachine;
            return controller.layers[idx].stateMachine;
        }

        static void FocusAnimatorWindow()
            => (Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType).FirstOrDefault() as EditorWindow)?.Focus();

        static void SelectTransitionsAndFocusAnimator(IEnumerable<AnimatorStateTransition> transitions)
        {
            Selection.objects = transitions.Cast<UnityEngine.Object>().ToArray();
            FocusAnimatorWindow();
        }

        /* Recursively collects into result all anyState and state transitions within sm (and nested sub SMs) whose destinationState is in targets. */
        static void CollectIncoming(AnimatorStateMachine sm, HashSet<AnimatorState> targets, List<AnimatorStateTransition> result)
        {
            foreach (var transition in sm.anyStateTransitions)
                if (transition.destinationState != null && targets.Contains(transition.destinationState))
                    result.Add(transition);
            foreach (var childState in sm.states)
                foreach (var transition in childState.state.transitions)
                    if (transition.destinationState != null && targets.Contains(transition.destinationState))
                        result.Add(transition);
            foreach (var childStateMachine in sm.stateMachines)
                CollectIncoming(childStateMachine.stateMachine, targets, result);
        }

        /* AnyState transitions all live on the layer's root SM, but only the ones whose destination is a direct
           child of scopeSM (the currently active/viewed SM — root or a drilled-into subSM) are selected. */
        internal static void SelectOutgoingFromAnyState(AnimatorStateMachine rootSM, AnimatorStateMachine scopeSM)
        {
            if (rootSM == null || scopeSM == null) return;
            var scopedStates = new HashSet<AnimatorState>(scopeSM.states.Select(childState => childState.state));
            var result = rootSM.anyStateTransitions
                .Where(transition => transition.destinationState != null && scopedStates.Contains(transition.destinationState));
            SelectTransitionsAndFocusAnimator(result);
        }

        /* Scoped to sm only — no recursion into sub-SMs, no falling back to a parent SM. */
        internal static void SelectIncomingToExit(AnimatorStateMachine sm)
        {
            if (sm == null) return;
            var result = sm.states
                .SelectMany(childState => childState.state.transitions)
                .Where(transition => transition.isExit);
            SelectTransitionsAndFocusAnimator(result);
        }

        /* Scoped to sm only — no recursion into sub-SMs, no falling back to a parent SM. */
        internal static void SelectOutgoingFromEntry(AnimatorStateMachine sm)
        {
            if (sm == null) return;
            Selection.objects = sm.entryTransitions.Cast<UnityEngine.Object>().ToArray();
            FocusAnimatorWindow();
        }

        static void CollectIncomingEntryTransitions(AnimatorStateMachine sm, HashSet<AnimatorState> targets, List<AnimatorTransition> result)
        {
            foreach (var transition in sm.entryTransitions)
                if (transition.destinationState != null && targets.Contains(transition.destinationState))
                    result.Add(transition);
            foreach (var childStateMachine in sm.stateMachines)
                CollectIncomingEntryTransitions(childStateMachine.stateMachine, targets, result);
        }

        // ── Alignment ─────────────────────────────────────────────────────────

        /* Aligns all selected states to the X (vertical=true) or Y (vertical=false) coordinate of the last selected state, using the last-selected state as anchor. */
        void AlignStates(bool vertical)
        {
            if (_selectedStates.Length < 2 || _controller == null) return;
            var anchor = _selectedStates[_selectedStates.Length - 1];
            var anchorPos = FindStatePosition(anchor);
            if (anchorPos == null) return;

            string undoName = vertical ? "Align States Vertical" : "Align States Horizontal";
            RegisterAllSMUndos(undoName);

            var toAlign = new HashSet<AnimatorState>(_selectedStates.Where(state => state != anchor));
            foreach (var layer in _controller.layers)
            {
                ApplyAlignment(layer.stateMachine, toAlign, vertical, anchorPos.Value);
                if (toAlign.Count == 0) break;
            }

            EditorUtility.SetDirty(_controller);
        }

        /* Evenly spaces all selected states along the vertical or horizontal axis between their minimum and maximum coordinate. */
        void DistributeStates(bool vertical)
        {
            if (_selectedStates.Length < 3 || _controller == null) return;

            var statePositions = _selectedStates
                .Select(state => (state, pos: FindStatePosition(state)))
                .Where(pair => pair.pos.HasValue)
                .Select(pair => (pair.state, pos: pair.pos.Value))
                .OrderBy(pair => vertical ? pair.pos.y : pair.pos.x)
                .ToArray();

            if (statePositions.Length < 3) return;

            float min = vertical ? statePositions[0].pos.y : statePositions[0].pos.x;
            float max = vertical ? statePositions[^1].pos.y : statePositions[^1].pos.x;
            float spacing = (max - min) / (statePositions.Length - 1);

            var newPositions = new Dictionary<AnimatorState, Vector2>();
            for (int i = 0; i < statePositions.Length; i++)
            {
                var (state, pos) = statePositions[i];
                newPositions[state] = vertical
                    ? new Vector2(pos.x, min + i * spacing)
                    : new Vector2(min + i * spacing, pos.y);
            }

            string undoName = vertical ? "Distribute States Vertical" : "Distribute States Horizontal";
            RegisterAllSMUndos(undoName);

            var remaining = new HashSet<AnimatorState>(newPositions.Keys);
            foreach (var layer in _controller.layers)
            {
                ApplyDistribution(layer.stateMachine, remaining, newPositions);
                if (remaining.Count == 0) break;
            }

            EditorUtility.SetDirty(_controller);
        }

        void RegisterAllSMUndos(string name)
        {
            foreach (var layer in _controller.layers)
                RegisterSMUndosRecursive(layer.stateMachine, name);
        }

        /* Registers a complete object undo for sm and all nested sub state machines under name. */
        static void RegisterSMUndosRecursive(AnimatorStateMachine sm, string name)
        {
            Undo.RegisterCompleteObjectUndo(sm, name);
            foreach (var childStateMachine in sm.stateMachines)
                RegisterSMUndosRecursive(childStateMachine.stateMachine, name);
        }

        /* Moves each state in targets found within sm (or its descendants) to match anchor's X (vertical) or Y (horizontal) coordinate. Removes found states from targets to avoid double-visiting. */
        static void ApplyAlignment(AnimatorStateMachine sm, HashSet<AnimatorState> targets, bool vertical, Vector2 anchor)
        {
            var states = sm.states;
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (!targets.Remove(states[i].state)) continue;
                var pos = (Vector2)states[i].position;
                states[i].position = vertical
                    ? new Vector3(anchor.x, pos.y, 0f)
                    : new Vector3(pos.x, anchor.y, 0f);
                changed = true;
            }
            if (changed) { sm.states = states; EditorUtility.SetDirty(sm); }
            if (targets.Count == 0) return;
            foreach (var childStateMachine in sm.stateMachines)
            {
                ApplyAlignment(childStateMachine.stateMachine, targets, vertical, anchor);
                if (targets.Count == 0) return;
            }
        }

        /* Writes the pre-computed positions from newPositions to each matching state in sm and its descendants, removing found states from targets. */
        static void ApplyDistribution(AnimatorStateMachine sm, HashSet<AnimatorState> targets, Dictionary<AnimatorState, Vector2> newPositions)
        {
            var states = sm.states;
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (!targets.Remove(states[i].state)) continue;
                var newPos = newPositions[states[i].state];
                states[i].position = new Vector3(newPos.x, newPos.y, 0f);
                changed = true;
            }
            if (changed) { sm.states = states; EditorUtility.SetDirty(sm); }
            if (targets.Count == 0) return;
            foreach (var childStateMachine in sm.stateMachines)
            {
                ApplyDistribution(childStateMachine.stateMachine, targets, newPositions);
                if (targets.Count == 0) return;
            }
        }

        /* Searches all layers of the active controller for target and returns its node position, or null if not found. */
        Vector2? FindStatePosition(AnimatorState target)
        {
            foreach (var layer in _controller.layers)
            {
                var pos = FindStatePositionInSM(layer.stateMachine, target);
                if (pos.HasValue) return pos;
            }
            return null;
        }

        /* Recursively searches sm and nested sub SMs for target, returning the node position or null. */
        static Vector2? FindStatePositionInSM(AnimatorStateMachine sm, AnimatorState target)
        {
            foreach (var childState in sm.states)
                if (childState.state == target) return (Vector2)childState.position;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var pos = FindStatePositionInSM(childStateMachine.stateMachine, target);
                if (pos.HasValue) return pos;
            }
            return null;
        }

        /* Applies mutate to every selected state under a single Undo.RecordObject call per state. */
        void SetStateOnAll(Action<AnimatorState> mutate)
        {
            foreach (var state in _selectedStates)
            {
                Undo.RecordObject(state, "Edit State");
                mutate(state);
                EditorUtility.SetDirty(state);
            }
        }

        /* Returns the set of all state names across every layer of the active controller, excluding the states in exclude. Used to find available names when batch-renaming. */
        HashSet<string> CollectLayerStateNamesExcluding(AnimatorState[] exclude)
        {
            var excludeSet = new HashSet<AnimatorState>(exclude);
            var names = new HashSet<string>();
            if (_controller == null) return names;
            foreach (var layer in _controller.layers)
                CollectStateNamesExcluding(layer.stateMachine, excludeSet, names);
            return names;
        }

        /* Recursively adds state names from sm and all nested sub SMs into names, skipping any state present in exclude. */
        static void CollectStateNamesExcluding(AnimatorStateMachine sm, HashSet<AnimatorState> exclude, HashSet<string> names)
        {
            foreach (var childState in sm.states)
                if (!exclude.Contains(childState.state))
                    names.Add(childState.state.name);
            foreach (var childStateMachine in sm.stateMachines)
                CollectStateNamesExcluding(childStateMachine.stateMachine, exclude, names);
        }
    }
}
#endif
