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
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        /* ── States tab (native shell: rows/align/properties; VRC behavior sections still IMGUI-bridged) ── */

        VisualElement _statesPanel;
        Label _statesEmptyLabel;
        ScrollView _stateRowsScroll;
        VisualElement _stateRowsContainer;
        VisualElement _stateRowsResizeGrip;

        const string StateRowsScrollHeightPrefsKey = "AnimatorTools.States.RowsScrollHeight";
        const float StateRowsScrollMinHeight = 40f;
        const float StateRowsScrollMaxHeight = 400f;
        Button _alignVerticalButton, _alignHorizontalButton, _distributeVerticalButton, _distributeHorizontalButton;
        VisualElement _statePropertiesContainer;
        VisualElement _sharedBehaviorsContainer;
        Label _sharedBehaviorsLabel;
        TextField _stateNameField, _stateTagField;
        ObjectField _stateMotionField;
        FloatField _stateSpeedField;
        Button _stateMultiplierParamButton;
        Toggle _stateMultiplierActiveToggle;
        Button _stateTimeParamButton;
        Toggle _stateTimeActiveToggle;
        Toggle _stateMirrorBoolToggle;
        Button _stateMirrorParamButton;
        Toggle _stateMirrorActiveToggle;
        FloatField _stateCycleOffsetField;
        Button _stateCycleOffsetParamButton;
        Toggle _stateCycleOffsetActiveToggle;
        Toggle _stateFootIKToggle;
        Toggle _stateWriteDefaultsToggle;
        Label _stateMultiplierLabel, _stateMotionTimeLabel, _stateMirrorLabel, _stateCycleOffsetLabel;

        VisualElement BuildStatesBody()
        {
            _statesPanel = new VisualElement();
            _statesPanel.AddToClassList("ygdr-states-panel");

            _statesEmptyLabel = new Label(L10n.Get("states.empty"));
            _statesEmptyLabel.AddToClassList("ygdr-empty-label");
            _statesPanel.Add(_statesEmptyLabel);

            var stateRowsWrapper = new VisualElement();
            stateRowsWrapper.AddToClassList("ygdr-states-rows-wrapper");

            _stateRowsScroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            _stateRowsScroll.AddToClassList("ygdr-states-rows-scroll");
            _stateRowsScroll.style.height = Mathf.Clamp(EditorPrefs.GetFloat(StateRowsScrollHeightPrefsKey, 96f), StateRowsScrollMinHeight, StateRowsScrollMaxHeight);
            _stateRowsContainer = new VisualElement();
            _stateRowsContainer.AddToClassList("ygdr-states-rows-container");
            _stateRowsScroll.Add(_stateRowsContainer);
            stateRowsWrapper.Add(_stateRowsScroll);

            _stateRowsResizeGrip = new VisualElement();
            _stateRowsResizeGrip.AddToClassList("ygdr-states-rows-resize-grip");
            _stateRowsResizeGrip.style.backgroundImage = new StyleBackground(SharedWindowStyles.ResizeGripTex);
            _stateRowsResizeGrip.style.unityBackgroundImageTintColor = SharedWindowStyles.AccentColor;
            RegisterStateRowsScrollResizeDrag(_stateRowsResizeGrip);
            stateRowsWrapper.Add(_stateRowsResizeGrip);

            _statesPanel.Add(stateRowsWrapper);

            _statesPanel.Add(BuildStateAlignButtons());
            _statesPanel.Add(BuildStateProperties());

#if VRC_SDK_VRCSDK3
            _sharedBehaviorsContainer = new VisualElement();

            _sharedBehaviorsLabel = new Label(L10n.Get("states.shared_behaviors"));
            _sharedBehaviorsLabel.AddToClassList("ygdr-states-shared-behaviors-label");
            _sharedBehaviorsContainer.Add(_sharedBehaviorsLabel);
            _sharedBehaviorsContainer.Add(BuildAddBehaviorDropdownButton());

            _sharedBehaviorsContainer.Add(BuildDriverBody());
            _sharedBehaviorsContainer.Add(BuildAudioBody());
            _sharedBehaviorsContainer.Add(BuildLayerControlBody());
            _sharedBehaviorsContainer.Add(BuildOtherBehaviorsBody());
            _statesPanel.Add(_sharedBehaviorsContainer);
#endif

            return _statesPanel;
        }

        /* Same pattern as RegisterTagsScrollResizeDrag in Transitions.cs. */
        void RegisterStateRowsScrollResizeDrag(VisualElement grip)
        {
            float startHeight = 0f;
            float startY = 0f;
            bool dragging = false;

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                dragging = true;
                startHeight = _stateRowsScroll.resolvedStyle.height;
                startY = evt.position.y;
                _stateRowsScroll.style.backgroundColor = SharedWindowStyles.RowAltColor;
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                float newHeight = Mathf.Clamp(startHeight + (evt.position.y - startY), StateRowsScrollMinHeight, StateRowsScrollMaxHeight);
                _stateRowsScroll.style.height = newHeight;
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                _stateRowsScroll.style.backgroundColor = SharedWindowStyles.SecondaryColor;
                grip.ReleasePointer(evt.pointerId);
                EditorPrefs.SetFloat(StateRowsScrollHeightPrefsKey, _stateRowsScroll.resolvedStyle.height);
                evt.StopPropagation();
            });
        }

        /* Called by patches (e.g. context menu paste) that mutate state behaviours outside this window's own UI flow. */
        internal static void RefreshOpenWindowsStatesTab()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<AnimationEditorWindow>())
            {
                window.RefreshStatesTab();
                window.Repaint();
            }
        }

        /* Mirrors the old per-frame IMGUI redraw. */
        void RefreshStatesTab()
        {
            if (_statesPanel == null) return;

            bool hasSelection = _selectedStates.Length > 0;
            _statesEmptyLabel.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
            _stateRowsScroll.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            RebuildStateRows();

            _alignVerticalButton.SetEnabled(_selectedStates.Length >= 2);
            _alignHorizontalButton.SetEnabled(_selectedStates.Length >= 2);
            _distributeVerticalButton.SetEnabled(_selectedStates.Length >= 3);
            _distributeHorizontalButton.SetEnabled(_selectedStates.Length >= 3);

            _statePropertiesContainer.SetEnabled(hasSelection);
            if (hasSelection) RefreshStatePropertyValues();

#if VRC_SDK_VRCSDK3
            _sharedBehaviorsContainer.SetEnabled(hasSelection);
            RefreshDriverBody();
            RefreshAudioBody();
            RefreshLayerControlBody();
            RefreshOtherBehaviorsBody();
#endif

            if (_statesRightLabel != null)
                _statesRightLabel.text = hasSelection
                    ? L10n.Get("header.n_selected").Replace("{n}", _selectedStates.Length.ToString())
                    : string.Empty;
        }

        void RefreshStatesLocalizedLabels()
        {
            if (_statesEmptyLabel != null) _statesEmptyLabel.text = L10n.Get("states.empty");

            if (_statesRightLabel != null)
                _statesRightLabel.text = _selectedStates.Length > 0
                    ? L10n.Get("header.n_selected").Replace("{n}", _selectedStates.Length.ToString())
                    : string.Empty;

            if (_alignVerticalButton != null) _alignVerticalButton.text = L10n.Get("states.align_vertical");
            if (_alignHorizontalButton != null) _alignHorizontalButton.text = L10n.Get("states.align_horizontal");
            if (_distributeVerticalButton != null) _distributeVerticalButton.text = L10n.Get("states.distribute_vertical");
            if (_distributeHorizontalButton != null) _distributeHorizontalButton.text = L10n.Get("states.distribute_horizontal");

            if (_stateNameField != null) _stateNameField.label = L10n.Get("states.name");
            if (_stateTagField != null) _stateTagField.label = L10n.Get("states.tag");
            if (_stateMotionField != null) _stateMotionField.label = L10n.Get("states.motion");
            if (_stateSpeedField != null) _stateSpeedField.label = L10n.Get("states.speed");
            if (_stateFootIKToggle != null) _stateFootIKToggle.label = L10n.Get("states.foot_ik");
            if (_stateWriteDefaultsToggle != null) _stateWriteDefaultsToggle.label = L10n.Get("states.write_defaults");

            if (_stateMultiplierLabel != null) _stateMultiplierLabel.text = L10n.Get("states.multiplier");
            if (_stateMotionTimeLabel != null) _stateMotionTimeLabel.text = L10n.Get("states.motion_time");
            if (_stateMirrorLabel != null) _stateMirrorLabel.text = L10n.Get("states.mirror");
            if (_stateCycleOffsetLabel != null) _stateCycleOffsetLabel.text = L10n.Get("states.cycle_offset");

            if (_stateMultiplierActiveToggle != null) _stateMultiplierActiveToggle.label = L10n.Get("states.parameter");
            if (_stateTimeActiveToggle != null) _stateTimeActiveToggle.label = L10n.Get("states.parameter");
            if (_stateMirrorActiveToggle != null) _stateMirrorActiveToggle.label = L10n.Get("states.parameter");
            if (_stateCycleOffsetActiveToggle != null) _stateCycleOffsetActiveToggle.label = L10n.Get("states.parameter");

#if VRC_SDK_VRCSDK3
            if (_sharedBehaviorsLabel != null) _sharedBehaviorsLabel.text = L10n.Get("states.shared_behaviors");
#endif
        }

        void RefreshStatesPaletteColors()
        {
            if (_statesPanel != null) _statesPanel.style.backgroundColor = SharedWindowStyles.PrimaryColor;
            if (_stateRowsScroll != null) _stateRowsScroll.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_stateRowsResizeGrip != null) _stateRowsResizeGrip.style.unityBackgroundImageTintColor = SharedWindowStyles.AccentColor;
            if (_alignVerticalButton == null) return;
            _alignVerticalButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _alignHorizontalButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _distributeVerticalButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _distributeHorizontalButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _stateMultiplierParamButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _stateTimeParamButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _stateMirrorParamButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _stateCycleOffsetParamButton.style.backgroundColor = SharedWindowStyles.AccentColor;

            // In/Out buttons are rebuilt fresh per state row (RebuildStateRows), baking in whatever
            // SharedWindowStyles.AccentColor was live at that moment — a later palette change only self-corrects
            // on mouse-leave (StyleAccentButton reads SharedWindowStyles.AccentColor live there) unless restyled here.
            _stateRowsContainer.Query<Button>(className: "ygdr-state-row-btn").ForEach(b => b.style.backgroundColor = SharedWindowStyles.AccentColor);
        }

        // ── State list ────────────────────────────────────────────────────────

        void RebuildStateRows()
        {
            _stateRowsContainer.Clear();
            foreach (var state in _selectedStates)
            {
                var capturedState = state;
                var row = new VisualElement();
                row.AddToClassList("ygdr-state-row");

                var inButton = new Button(() => SelectIncomingTransitions(_controller, new[] { capturedState })) { text = L10n.Get("states.in") };
                inButton.AddToClassList("ygdr-state-row-btn");
                StyleAccentButton(inButton);
                row.Add(inButton);

                var nameLabel = new Label(state.name);
                nameLabel.AddToClassList("ygdr-state-row-name");
                row.Add(nameLabel);

                var outButton = new Button(() => SelectOutgoingTransitions(new[] { capturedState })) { text = L10n.Get("states.out") };
                outButton.AddToClassList("ygdr-state-row-btn");
                StyleAccentButton(outButton);
                row.Add(outButton);

                _stateRowsContainer.Add(row);
            }
        }

        // ── Align buttons ─────────────────────────────────────────────────────

        VisualElement BuildStateAlignButtons()
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-states-align-container");

            var alignRow = new VisualElement();
            alignRow.AddToClassList("ygdr-states-button-row");
            _alignVerticalButton = new Button(() => AlignStates(vertical: true)) { text = L10n.Get("states.align_vertical") };
            _alignHorizontalButton = new Button(() => AlignStates(vertical: false)) { text = L10n.Get("states.align_horizontal") };
            _alignVerticalButton.AddToClassList("ygdr-states-align-btn");
            _alignVerticalButton.AddToClassList("u-flex-fill");
            _alignVerticalButton.AddToClassList("u-flat-btn-sm");
            _alignHorizontalButton.AddToClassList("ygdr-states-align-btn");
            _alignHorizontalButton.AddToClassList("u-flex-fill");
            _alignHorizontalButton.AddToClassList("u-flat-btn-sm");
            StyleSecondaryButton(_alignVerticalButton);
            StyleSecondaryButton(_alignHorizontalButton);
            alignRow.Add(_alignVerticalButton);
            alignRow.Add(_alignHorizontalButton);
            container.Add(alignRow);

            var distributeRow = new VisualElement();
            distributeRow.AddToClassList("ygdr-states-button-row");
            _distributeVerticalButton = new Button(() => DistributeStates(vertical: true)) { text = L10n.Get("states.distribute_vertical") };
            _distributeHorizontalButton = new Button(() => DistributeStates(vertical: false)) { text = L10n.Get("states.distribute_horizontal") };
            _distributeVerticalButton.AddToClassList("ygdr-states-align-btn");
            _distributeVerticalButton.AddToClassList("u-flex-fill");
            _distributeVerticalButton.AddToClassList("u-flat-btn-sm");
            _distributeHorizontalButton.AddToClassList("ygdr-states-align-btn");
            _distributeHorizontalButton.AddToClassList("u-flex-fill");
            _distributeHorizontalButton.AddToClassList("u-flat-btn-sm");
            StyleSecondaryButton(_distributeVerticalButton);
            StyleSecondaryButton(_distributeHorizontalButton);
            distributeRow.Add(_distributeVerticalButton);
            distributeRow.Add(_distributeHorizontalButton);
            container.Add(distributeRow);

            return container;
        }

        // ── State properties (native) ───────────────────────────────────────────

        VisualElement BuildStateProperties()
        {
            _statePropertiesContainer = new VisualElement();
            _statePropertiesContainer.AddToClassList("ygdr-states-properties");

            _stateNameField = new TextField(L10n.Get("states.name"));
            _stateNameField.RegisterValueChangedCallback(evt => ApplyStateNameChange(evt.newValue));
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateNameField));

            _stateTagField = new TextField(L10n.Get("states.tag"));
            _stateTagField.RegisterValueChangedCallback(evt => { string value = evt.newValue; SetStateOnAll(state => state.tag = value); });
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateTagField));

            _stateMotionField = new ObjectField(L10n.Get("states.motion")) { objectType = typeof(Motion) };
            _stateMotionField.RegisterValueChangedCallback(evt => { var value = (Motion)evt.newValue; SetStateOnAll(state => state.motion = value); });
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateMotionField));

            _stateSpeedField = new FloatField(L10n.Get("states.speed"));
            _stateSpeedField.RegisterValueChangedCallback(evt => { float value = evt.newValue; SetStateOnAll(state => state.speed = value); });
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateSpeedField));

            _statePropertiesContainer.Add(BuildStateMultiplierRow());
            _statePropertiesContainer.Add(BuildStateMotionTimeRow());
            _statePropertiesContainer.Add(BuildStateMirrorRow());
            _statePropertiesContainer.Add(BuildStateCycleOffsetRow());

            _stateFootIKToggle = new Toggle(L10n.Get("states.foot_ik"));
            _stateFootIKToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.iKOnFeet = value); });
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateFootIKToggle));

            _stateWriteDefaultsToggle = new Toggle(L10n.Get("states.write_defaults"));
            _stateWriteDefaultsToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.writeDefaultValues = value); });
            _statePropertiesContainer.Add(BuildStatePropertyRow(_stateWriteDefaultsToggle));

            return _statePropertiesContainer;
        }

        static VisualElement BuildStatePropertyRow(VisualElement field)
        {
            field.AddToClassList("ygdr-states-property-field-full");
            var row = BuildRow("ygdr-states-property-row", null, field);
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            return row;
        }

        VisualElement BuildStateMultiplierRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-states-property-row");
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            _stateMultiplierLabel = new Label(L10n.Get("states.multiplier"));
            _stateMultiplierLabel.AddToClassList("ygdr-states-property-label");
            row.Add(_stateMultiplierLabel);

            _stateMultiplierParamButton = new Button(() =>
            {
                var first = _selectedStates.FirstOrDefault();
                if (first == null) return;
                ShowParameterDropdown(_stateMultiplierParamButton.worldBound, first.speedParameter, AnimatorControllerParameterType.Float,
                    newParam => { SetStateOnAll(state => state.speedParameter = newParam); RefreshStatePropertyValues(); });
            });
            _stateMultiplierParamButton.AddToClassList("ygdr-states-property-field");
            _stateMultiplierParamButton.AddToClassList("u-flex-fill");
            _stateMultiplierParamButton.AddToClassList("u-mr-4");
            _stateMultiplierParamButton.AddToClassList("ygdr-states-param-dropdown");
            RegisterDropdownLabelResize(_stateMultiplierParamButton, 18f);
            StyleAccentButton(_stateMultiplierParamButton);
            _stateMultiplierParamButton.Add(BuildDropdownArrow());
            row.Add(_stateMultiplierParamButton);

            _stateMultiplierActiveToggle = new Toggle(L10n.Get("states.parameter"));
            _stateMultiplierActiveToggle.AddToClassList("ygdr-states-active-toggle");
            _stateMultiplierActiveToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.speedParameterActive = value); RefreshStatePropertyValues(); });
            row.Add(_stateMultiplierActiveToggle);

            return row;
        }

        VisualElement BuildStateMotionTimeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-states-property-row");
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            _stateMotionTimeLabel = new Label(L10n.Get("states.motion_time"));
            _stateMotionTimeLabel.AddToClassList("ygdr-states-property-label");
            row.Add(_stateMotionTimeLabel);

            _stateTimeParamButton = new Button(() =>
            {
                var first = _selectedStates.FirstOrDefault();
                if (first == null) return;
                ShowParameterDropdown(_stateTimeParamButton.worldBound, first.timeParameter, AnimatorControllerParameterType.Float,
                    newParam => { SetStateOnAll(state => state.timeParameter = newParam); RefreshStatePropertyValues(); });
            });
            _stateTimeParamButton.AddToClassList("ygdr-states-property-field");
            _stateTimeParamButton.AddToClassList("u-flex-fill");
            _stateTimeParamButton.AddToClassList("u-mr-4");
            _stateTimeParamButton.AddToClassList("ygdr-states-param-dropdown");
            RegisterDropdownLabelResize(_stateTimeParamButton, 18f);
            StyleAccentButton(_stateTimeParamButton);
            _stateTimeParamButton.Add(BuildDropdownArrow());
            row.Add(_stateTimeParamButton);

            _stateTimeActiveToggle = new Toggle(L10n.Get("states.parameter"));
            _stateTimeActiveToggle.AddToClassList("ygdr-states-active-toggle");
            _stateTimeActiveToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.timeParameterActive = value); RefreshStatePropertyValues(); });
            row.Add(_stateTimeActiveToggle);

            return row;
        }

        VisualElement BuildStateMirrorRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-states-property-row");
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            _stateMirrorLabel = new Label(L10n.Get("states.mirror"));
            _stateMirrorLabel.AddToClassList("ygdr-states-property-label");
            row.Add(_stateMirrorLabel);

            _stateMirrorBoolToggle = new Toggle();
            _stateMirrorBoolToggle.AddToClassList("ygdr-states-mirror-bool-toggle");
            _stateMirrorBoolToggle.AddToClassList("u-flex-fill");
            _stateMirrorBoolToggle.AddToClassList("u-mr-4");
            _stateMirrorBoolToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.mirror = value); });
            row.Add(_stateMirrorBoolToggle);

            _stateMirrorParamButton = new Button(() =>
            {
                var first = _selectedStates.FirstOrDefault();
                if (first == null) return;
                ShowParameterDropdown(_stateMirrorParamButton.worldBound, first.mirrorParameter, AnimatorControllerParameterType.Bool,
                    newParam => { SetStateOnAll(state => state.mirrorParameter = newParam); RefreshStatePropertyValues(); });
            });
            _stateMirrorParamButton.AddToClassList("ygdr-states-property-field");
            _stateMirrorParamButton.AddToClassList("u-flex-fill");
            _stateMirrorParamButton.AddToClassList("u-mr-4");
            _stateMirrorParamButton.AddToClassList("ygdr-states-param-dropdown");
            RegisterDropdownLabelResize(_stateMirrorParamButton, 18f);
            StyleAccentButton(_stateMirrorParamButton);
            _stateMirrorParamButton.Add(BuildDropdownArrow());
            row.Add(_stateMirrorParamButton);

            _stateMirrorActiveToggle = new Toggle(L10n.Get("states.parameter"));
            _stateMirrorActiveToggle.AddToClassList("ygdr-states-active-toggle");
            _stateMirrorActiveToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.mirrorParameterActive = value); RefreshStatePropertyValues(); });
            row.Add(_stateMirrorActiveToggle);

            return row;
        }

        VisualElement BuildStateCycleOffsetRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-states-property-row");
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            _stateCycleOffsetLabel = new Label(L10n.Get("states.cycle_offset"));
            _stateCycleOffsetLabel.AddToClassList("ygdr-states-property-label");
            row.Add(_stateCycleOffsetLabel);

            _stateCycleOffsetField = new FloatField();
            _stateCycleOffsetField.AddToClassList("ygdr-states-property-field");
            _stateCycleOffsetField.AddToClassList("u-flex-fill");
            _stateCycleOffsetField.AddToClassList("u-mr-4");
            _stateCycleOffsetField.RegisterValueChangedCallback(evt => { float value = evt.newValue; SetStateOnAll(state => state.cycleOffset = value); });
            row.Add(_stateCycleOffsetField);

            _stateCycleOffsetParamButton = new Button(() =>
            {
                var first = _selectedStates.FirstOrDefault();
                if (first == null) return;
                ShowParameterDropdown(_stateCycleOffsetParamButton.worldBound, first.cycleOffsetParameter, AnimatorControllerParameterType.Float,
                    newParam => { SetStateOnAll(state => state.cycleOffsetParameter = newParam); RefreshStatePropertyValues(); });
            });
            _stateCycleOffsetParamButton.AddToClassList("ygdr-states-property-field");
            _stateCycleOffsetParamButton.AddToClassList("u-flex-fill");
            _stateCycleOffsetParamButton.AddToClassList("u-mr-4");
            _stateCycleOffsetParamButton.AddToClassList("ygdr-states-param-dropdown");
            RegisterDropdownLabelResize(_stateCycleOffsetParamButton, 18f);
            StyleAccentButton(_stateCycleOffsetParamButton);
            _stateCycleOffsetParamButton.Add(BuildDropdownArrow());
            row.Add(_stateCycleOffsetParamButton);

            _stateCycleOffsetActiveToggle = new Toggle(L10n.Get("states.parameter"));
            _stateCycleOffsetActiveToggle.AddToClassList("ygdr-states-active-toggle");
            _stateCycleOffsetActiveToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetStateOnAll(state => state.cycleOffsetParameterActive = value); RefreshStatePropertyValues(); });
            row.Add(_stateCycleOffsetActiveToggle);

            return row;
        }

        /* Pushes current selection values into the property controls without re-firing their change callbacks. */
        void RefreshStatePropertyValues()
        {
            int count = _selectedStates.Length;
            bool multi = count > 1;
            var first = _selectedStates[0];

            _stateNameField.SetValueWithoutNotify(first.name);
            _stateNameField.showMixedValue = multi && _selectedStates.Any(x => x.name != first.name);

            _stateTagField.SetValueWithoutNotify(first.tag);
            _stateTagField.showMixedValue = multi && _selectedStates.Any(x => x.tag != first.tag);

            _stateMotionField.SetValueWithoutNotify(first.motion);
            _stateMotionField.showMixedValue = multi && _selectedStates.Any(x => x.motion != first.motion);

            _stateSpeedField.SetValueWithoutNotify(first.speed);
            _stateSpeedField.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.speed, first.speed));

            bool speedParamActive = first.speedParameterActive;
            SetTruncatedDropdownLabel(_stateMultiplierParamButton, string.IsNullOrEmpty(first.speedParameter) ? "—" : first.speedParameter, 18f);
            _stateMultiplierParamButton.SetEnabled(speedParamActive);
            _stateMultiplierActiveToggle.SetValueWithoutNotify(speedParamActive);
            _stateMultiplierActiveToggle.showMixedValue = multi && _selectedStates.Any(x => x.speedParameterActive != first.speedParameterActive);

            bool timeParamActive = first.timeParameterActive;
            SetTruncatedDropdownLabel(_stateTimeParamButton, string.IsNullOrEmpty(first.timeParameter) ? "—" : first.timeParameter, 18f);
            _stateTimeParamButton.style.display = timeParamActive ? DisplayStyle.Flex : DisplayStyle.None;
            _stateTimeActiveToggle.SetValueWithoutNotify(timeParamActive);
            _stateTimeActiveToggle.showMixedValue = multi && _selectedStates.Any(x => x.timeParameterActive != first.timeParameterActive);

            bool mirrorParamActive = first.mirrorParameterActive;
            _stateMirrorBoolToggle.style.display = mirrorParamActive ? DisplayStyle.None : DisplayStyle.Flex;
            _stateMirrorParamButton.style.display = mirrorParamActive ? DisplayStyle.Flex : DisplayStyle.None;
            _stateMirrorBoolToggle.SetValueWithoutNotify(first.mirror);
            _stateMirrorBoolToggle.showMixedValue = multi && _selectedStates.Any(x => x.mirror != first.mirror);
            SetTruncatedDropdownLabel(_stateMirrorParamButton, string.IsNullOrEmpty(first.mirrorParameter) ? "—" : first.mirrorParameter, 18f);
            _stateMirrorActiveToggle.SetValueWithoutNotify(mirrorParamActive);
            _stateMirrorActiveToggle.showMixedValue = multi && _selectedStates.Any(x => x.mirrorParameterActive != first.mirrorParameterActive);

            bool cycleOffsetParamActive = first.cycleOffsetParameterActive;
            _stateCycleOffsetField.style.display = cycleOffsetParamActive ? DisplayStyle.None : DisplayStyle.Flex;
            _stateCycleOffsetParamButton.style.display = cycleOffsetParamActive ? DisplayStyle.Flex : DisplayStyle.None;
            _stateCycleOffsetField.SetValueWithoutNotify(first.cycleOffset);
            _stateCycleOffsetField.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.cycleOffset, first.cycleOffset));
            SetTruncatedDropdownLabel(_stateCycleOffsetParamButton, string.IsNullOrEmpty(first.cycleOffsetParameter) ? "—" : first.cycleOffsetParameter, 18f);
            _stateCycleOffsetActiveToggle.SetValueWithoutNotify(cycleOffsetParamActive);
            _stateCycleOffsetActiveToggle.showMixedValue = multi && _selectedStates.Any(x => x.cycleOffsetParameterActive != first.cycleOffsetParameterActive);

            _stateFootIKToggle.SetValueWithoutNotify(first.iKOnFeet);
            _stateFootIKToggle.showMixedValue = multi && _selectedStates.Any(x => x.iKOnFeet != first.iKOnFeet);

            _stateWriteDefaultsToggle.SetValueWithoutNotify(first.writeDefaultValues);
            _stateWriteDefaultsToggle.showMixedValue = multi && _selectedStates.Any(x => x.writeDefaultValues != first.writeDefaultValues);
        }

        /* Multiple selected states get " N" suffixes to stay unique among all state names in the controller. */
        void ApplyStateNameChange(string newName)
        {
            if (_selectedStates.Length == 0) return;
            if (_selectedStates.Length > 1)
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
            RebuildStateRows();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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

        /* AnyState transitions all live on the layer's root SM; only ones destined into scopeSM are selected. */
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

            var anchor2D = anchorPos.Value;
            Vector2 AlignedPos(AnimatorState _, Vector2 pos) =>
                vertical ? new Vector2(anchor2D.x, pos.y) : new Vector2(pos.x, anchor2D.y);

            var toAlign = new HashSet<AnimatorState>(_selectedStates.Where(state => state != anchor));
            foreach (var layer in _controller.layers)
            {
                ApplyNewPositions(layer.stateMachine, toAlign, AlignedPos);
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
                ApplyNewPositions(layer.stateMachine, remaining, (state, _) => newPositions[state]);
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

        /* Moves each state in targets found within sm (or its descendants) to the position computeNewPos returns given its current position. Removes found states from targets to avoid double-visiting. */
        static void ApplyNewPositions(AnimatorStateMachine sm, HashSet<AnimatorState> targets, Func<AnimatorState, Vector2, Vector2> computeNewPos)
        {
            var states = sm.states;
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (!targets.Remove(states[i].state)) continue;
                var newPos = computeNewPos(states[i].state, states[i].position);
                states[i].position = new Vector3(newPos.x, newPos.y, 0f);
                changed = true;
            }
            if (changed) { sm.states = states; EditorUtility.SetDirty(sm); }
            if (targets.Count == 0) return;
            foreach (var childStateMachine in sm.stateMachines)
            {
                ApplyNewPositions(childStateMachine.stateMachine, targets, computeNewPos);
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
