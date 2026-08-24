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

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        static Texture2D _mergeIconTex;
        static Texture2D MergeIconTex => _mergeIconTex ??= EditorGUIUtility.IconContent("AnimatorStateTransition Icon").image as Texture2D;
        static Texture2D _separateIconTex;
        static Texture2D SeparateIconTex => _separateIconTex ??= EditorGUIUtility.IconContent("d_BlendTree Icon").image as Texture2D;
        static Texture2D _filterIconTex;
        static Texture2D FilterIconTex => _filterIconTex ??= EditorGUIUtility.IconContent("d_filterbylabel@2x").image as Texture2D;
        static Texture2D _duplicateParamIconTex;
        static Texture2D DuplicateParamIconTex => _duplicateParamIconTex ??= EditorGUIUtility.IconContent("d_console.erroricon").image as Texture2D;
        static Texture2D _dropdownArrowIconTex;
        static Texture2D DropdownArrowIconTex => _dropdownArrowIconTex ??= EditorGUIUtility.IconContent("d_profilertimelinedigdownarrow@2x").image as Texture2D;
        static Texture2D _dragHandleIconTex;
        static Texture2D DragHandleIconTex => _dragHandleIconTex ??= EditorGUIUtility.IconContent("d_align_vertically_center_active").image as Texture2D;

        /* ── Transitions tab (native UI Toolkit shell + conditions grid) ── */

        VisualElement _transitionsPanel;
        Label _transitionsEmptyLabel;
        ScrollView _transitionsTagsScroll;
        VisualElement _transitionsTagsContainer;
        VisualElement _transitionsTagsResizeGrip;

        const string TagsScrollHeightPrefsKey = "AnimatorTools.Transitions.TagsScrollHeight";
        const float TagsScrollMinHeight = 40f;
        const float TagsScrollMaxHeight = 400f;
        VisualElement _transitionsPropertiesContainer;
        VisualElement _condHeader;
        Button _condModeButton;
        Button _matchNameButton, _matchModeButton, _matchValueButton;
        Button _condSwitchButton, _condMergeButton, _condSeparateButton;
        VisualElement _condRowsContainer;
        VisualElement _condAddRow;
        Button _condAddButton;

        Toggle _hasExitTimeToggle;
        FloatField _exitTimeField;
        Toggle _hasFixedDurationToggle;
        FloatField _durationField;
        FloatField _offsetField;
        PopupField<string> _interruptionSourcePopup;
        VisualElement _interruptionSourcePopupInput;
        Toggle _orderedInterruptionToggle;
        Toggle _muteToggle;
        Toggle _canTransitionToSelfToggle;
        Toggle _soloToggle;

        VisualElement BuildTransitionsBody()
        {
            _transitionsPanel = new VisualElement();
            _transitionsPanel.AddToClassList("ygdr-transitions-panel");

            _transitionsEmptyLabel = new Label(L10n.Get("transitions.empty"));
            _transitionsEmptyLabel.AddToClassList("ygdr-empty-label");
            _transitionsPanel.Add(_transitionsEmptyLabel);

            var transitionsTagsWrapper = new VisualElement();
            transitionsTagsWrapper.AddToClassList("ygdr-transitions-tags-wrapper");

            _transitionsTagsScroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            _transitionsTagsScroll.AddToClassList("ygdr-transitions-tags-scroll");
            _transitionsTagsScroll.style.height = Mathf.Clamp(EditorPrefs.GetFloat(TagsScrollHeightPrefsKey, 96f), TagsScrollMinHeight, TagsScrollMaxHeight);
            _transitionsTagsScroll.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _transitionsTagsContainer = new VisualElement();
            _transitionsTagsContainer.AddToClassList("ygdr-transitions-tags-container");
            _transitionsTagsScroll.Add(_transitionsTagsContainer);
            transitionsTagsWrapper.Add(_transitionsTagsScroll);

            _transitionsTagsResizeGrip = new VisualElement();
            _transitionsTagsResizeGrip.AddToClassList("ygdr-transitions-tags-resize-grip");
            _transitionsTagsResizeGrip.style.backgroundImage = new StyleBackground(SharedWindowStyles.ResizeGripTex);
            _transitionsTagsResizeGrip.style.unityBackgroundImageTintColor = SharedWindowStyles.AccentColor;
            RegisterTagsScrollResizeDrag(_transitionsTagsResizeGrip);
            transitionsTagsWrapper.Add(_transitionsTagsResizeGrip);

            _transitionsPanel.Add(transitionsTagsWrapper);

            _transitionsPropertiesContainer = new VisualElement();
            _transitionsPropertiesContainer.AddToClassList("ygdr-transitions-properties");
            BuildTransitionsProperties(_transitionsPropertiesContainer);
            _transitionsPanel.Add(_transitionsPropertiesContainer);

            _transitionsPanel.Add(BuildConditionsSection());
            _transitionsPanel.Add(BuildTransitionPreviewSection());

            return _transitionsPanel;
        }

        /* Triangle is border-top+border-left, not bottom/right — a 0x0 box at bottom:0/right:0 can only draw
           into the container on those sides; border-bottom/right would draw outside the container bounds. */
        void RegisterTagsScrollResizeDrag(VisualElement grip)
        {
            float startHeight = 0f;
            float startY = 0f;
            bool dragging = false;

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                dragging = true;
                startHeight = _transitionsTagsScroll.resolvedStyle.height;
                startY = evt.position.y;
                _transitionsTagsScroll.style.backgroundColor = SharedWindowStyles.RowAltColor;
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                float newHeight = Mathf.Clamp(startHeight + (evt.position.y - startY), TagsScrollMinHeight, TagsScrollMaxHeight);
                _transitionsTagsScroll.style.height = newHeight;
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                _transitionsTagsScroll.style.backgroundColor = SharedWindowStyles.SecondaryColor;
                grip.ReleasePointer(evt.pointerId);
                EditorPrefs.SetFloat(TagsScrollHeightPrefsKey, _transitionsTagsScroll.resolvedStyle.height);
                evt.StopPropagation();
            });
        }

        /* Mirrors the old per-frame IMGUI redraw. */
        void RefreshTransitionsLocalizedLabels()
        {
            if (_hasExitTimeToggle != null) _hasExitTimeToggle.label = L10n.Get("transitions.has_exit_time");
            if (_exitTimeField != null) _exitTimeField.label = L10n.Get("transitions.exit_time");
            if (_hasFixedDurationToggle != null) _hasFixedDurationToggle.label = L10n.Get("transitions.has_fixed_duration");
            if (_durationField != null) _durationField.label = L10n.Get("transitions.duration");
            if (_offsetField != null) _offsetField.label = L10n.Get("transitions.offset");
            if (_orderedInterruptionToggle != null) _orderedInterruptionToggle.label = L10n.Get("transitions.ordered_interruption");
            if (_muteToggle != null) _muteToggle.label = L10n.Get("transitions.mute");
            if (_canTransitionToSelfToggle != null) _canTransitionToSelfToggle.label = L10n.Get("transitions.can_transition_to_self");
            if (_soloToggle != null) _soloToggle.label = L10n.Get("transitions.solo");

            if (_interruptionSourcePopup != null)
            {
                var current = _interruptionSourcePopup.index;
                _interruptionSourcePopup.label = L10n.Get("transitions.interruption_source");
                _interruptionSourcePopup.choices = new List<string>
                {
                    L10n.Get("transitions.interruption.none"), L10n.Get("transitions.interruption.source"), L10n.Get("transitions.interruption.destination"),
                    L10n.Get("transitions.interruption.source_then_destination"), L10n.Get("transitions.interruption.destination_then_source")
                };
                _interruptionSourcePopup.SetValueWithoutNotify(_interruptionSourcePopup.choices[current]);
            }

            if (_transitionsEmptyLabel != null) _transitionsEmptyLabel.text = L10n.Get("transitions.empty");
            RefreshTransitionPreviewHeaderLabel();

            if (_transitionsRightLabel != null)
                _transitionsRightLabel.text = _selectedTransitions.Length > 0
                    ? L10n.Get("header.n_selected").Replace("{n}", _selectedTransitions.Length.ToString())
                    : string.Empty;
        }

        void RefreshTransitionsTab()
        {
            if (_transitionsPanel == null) return;

            int count = _selectedTransitions.Length + _selectedEntryTransitions.Length;
            _transitionsEmptyLabel.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _transitionsTagsScroll.style.display = count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _transitionsEmptyLabel.text = L10n.Get("transitions.empty");
            RebuildTransitionTags();

            _transitionsPropertiesContainer.style.display = _selectedTransitions.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshTransitionsProperties();

            if (_transitionsRightLabel != null)
                _transitionsRightLabel.text = _selectedTransitions.Length > 0
                    ? L10n.Get("header.n_selected").Replace("{n}", _selectedTransitions.Length.ToString())
                    : string.Empty;

            RebuildConditionRows();
            RefreshTransitionPreviewSection();
        }

        void RefreshTransitionsPaletteColors()
        {
            if (_transitionsPanel != null) _transitionsPanel.style.backgroundColor = SharedWindowStyles.PrimaryColor;
            if (_transitionsTagsScroll != null) _transitionsTagsScroll.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_transitionsTagsContainer != null) RebuildTransitionTags();
            if (_transitionsTagsResizeGrip != null) _transitionsTagsResizeGrip.style.unityBackgroundImageTintColor = SharedWindowStyles.AccentColor;
            if (_previewWrapper != null)
            {
                _previewWrapper.Query<VisualElement>(className: "ygdr-settings-section-header").ForEach(h => h.style.backgroundColor = SharedWindowStyles.AccentColor);
                _previewWrapper.Query<VisualElement>(className: "ygdr-settings-section-body").ForEach(b => b.style.backgroundColor = SharedWindowStyles.SecondaryColor);
            }
            if (_condRowsContainer != null) _condRowsContainer.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_condHeader == null) return;
            _condModeButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _condSwitchButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _condMergeButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _condSeparateButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            _condAddButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_interruptionSourcePopupInput != null) _interruptionSourcePopupInput.style.backgroundColor = SharedWindowStyles.AccentColor;
            RebuildConditionRows();
        }

        /* ── Transition Tags ─────────────────────────────────────────────── */

        void RebuildTransitionTags()
        {
            _transitionsTagsContainer.Clear();
            foreach (var transition in _selectedTransitions)
                _transitionsTagsContainer.Add(BuildTransitionTagChip(GetTransitionLabel(transition), onRemove: () =>
                {
                    Selection.objects = _selectedTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()
                        .Concat(_selectedEntryTransitions.Cast<UnityEngine.Object>()).ToArray();
                }, onDelete: () => DeleteTransitionFromChip(transition)));
            foreach (var transition in _selectedEntryTransitions)
                _transitionsTagsContainer.Add(BuildTransitionTagChip(GetEntryTransitionLabel(transition), onRemove: () =>
                {
                    Selection.objects = _selectedTransitions.Cast<UnityEngine.Object>()
                        .Concat(_selectedEntryTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()).ToArray();
                }, onDelete: () => DeleteEntryTransitionFromChip(transition)));
        }

        static VisualElement BuildTransitionTagChip(string label, Action onRemove, Action onDelete)
        {
            var chip = new VisualElement();
            chip.AddToClassList("ygdr-transition-tag-chip");
            chip.style.backgroundColor = SecondaryButtonHoverColor;

            var deleteButton = new Button(onDelete) { text = "-", tooltip = L10n.Get("transitions.tag_delete_tooltip") };
            deleteButton.AddToClassList("ygdr-transition-tag-remove");
            deleteButton.AddToClassList("ygdr-transition-tag-delete");
            chip.Add(deleteButton);

            var removeButton = new Button(onRemove) { text = "✕", tooltip = L10n.Get("transitions.tag_deselect_tooltip") };
            removeButton.AddToClassList("ygdr-transition-tag-remove");
            chip.Add(removeButton);

            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-transition-tag-label");
            chip.Add(labelElement);

            return chip;
        }

        /* Deletes the transition asset entirely (not just from selection) — mirrors DeleteTransition's use in MergeTransitions. */
        void DeleteTransitionFromChip(AnimatorStateTransition transition)
        {
            if (_controller == null) return;
            var ownerStateMachine = FindOwnerSM(_controller, transition);
            if (ownerStateMachine == null) return;
            Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Delete Transition");
            DeleteTransition(ownerStateMachine, transition);
            Selection.objects = _selectedTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()
                .Concat(_selectedEntryTransitions.Cast<UnityEngine.Object>()).ToArray();
            EditorUtility.SetDirty(_controller);
            InvalidateConditionCache();
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        void DeleteEntryTransitionFromChip(AnimatorTransition transition)
        {
            if (_controller == null) return;
            var ownerStateMachine = FindEntryOwnerSM(_controller, transition);
            if (ownerStateMachine == null) return;
            Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Delete Transition");
            ownerStateMachine.RemoveEntryTransition(transition);
            Selection.objects = _selectedTransitions.Cast<UnityEngine.Object>()
                .Concat(_selectedEntryTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()).ToArray();
            EditorUtility.SetDirty(_controller);
            InvalidateConditionCache();
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        /* ── Property rows (native) ──────────────────────────────────────── */

        void BuildTransitionsProperties(VisualElement parent)
        {
            _hasExitTimeToggle = new Toggle(L10n.Get("transitions.has_exit_time"));
            _hasExitTimeToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetOnAll(transition => transition.hasExitTime = value); });
            _exitTimeField = new FloatField(L10n.Get("transitions.exit_time"));
            _exitTimeField.RegisterValueChangedCallback(evt => { float value = evt.newValue; SetOnAll(transition => transition.exitTime = value); });
            parent.Add(BuildPropertyRow(_hasExitTimeToggle, _exitTimeField));

            _hasFixedDurationToggle = new Toggle(L10n.Get("transitions.has_fixed_duration"));
            _hasFixedDurationToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetOnAll(transition => transition.hasFixedDuration = value); });
            _durationField = new FloatField(L10n.Get("transitions.duration"));
            _durationField.RegisterValueChangedCallback(evt => { float value = evt.newValue; SetOnAll(transition => transition.duration = value); });
            parent.Add(BuildPropertyRow(_hasFixedDurationToggle, _durationField));

            _offsetField = new FloatField(L10n.Get("transitions.offset"));
            _offsetField.AddToClassList("ygdr-transitions-property-field-wide");
            _offsetField.RegisterValueChangedCallback(evt => { float value = evt.newValue; SetOnAll(transition => transition.offset = value); });
            var offsetRow = new VisualElement();
            offsetRow.AddToClassList("ygdr-transitions-property-row");
            offsetRow.AddToClassList("u-row");
            offsetRow.AddToClassList("u-mb-2");
            offsetRow.Add(_offsetField);
            parent.Add(offsetRow);

            var interruptionChoices = new List<string>
            {
                L10n.Get("transitions.interruption.none"), L10n.Get("transitions.interruption.source"), L10n.Get("transitions.interruption.destination"),
                L10n.Get("transitions.interruption.source_then_destination"), L10n.Get("transitions.interruption.destination_then_source")
            };
            _interruptionSourcePopup = new PopupField<string>(L10n.Get("transitions.interruption_source"), interruptionChoices, 0);
            _interruptionSourcePopup.AddToClassList("ygdr-transitions-property-field-wide");
            _interruptionSourcePopupInput = _interruptionSourcePopup.Q(className: "unity-base-popup-field__input");
            if (_interruptionSourcePopupInput != null) StyleAccentButton(_interruptionSourcePopupInput);
            _interruptionSourcePopup.RegisterValueChangedCallback(evt =>
            {
                var value = (TransitionInterruptionSource)interruptionChoices.IndexOf(evt.newValue);
                SetOnAll(transition => transition.interruptionSource = value);
            });
            var interruptionRow = new VisualElement();
            interruptionRow.AddToClassList("ygdr-transitions-property-row");
            interruptionRow.AddToClassList("u-row");
            interruptionRow.AddToClassList("u-mb-2");
            interruptionRow.Add(_interruptionSourcePopup);
            parent.Add(interruptionRow);

            _orderedInterruptionToggle = new Toggle(L10n.Get("transitions.ordered_interruption"));
            _orderedInterruptionToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetOnAll(transition => transition.orderedInterruption = value); });
            _muteToggle = new Toggle(L10n.Get("transitions.mute"));
            _muteToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetOnAll(transition => transition.mute = value); });
            parent.Add(BuildPropertyRow(_orderedInterruptionToggle, _muteToggle));

            _canTransitionToSelfToggle = new Toggle(L10n.Get("transitions.can_transition_to_self"));
            _canTransitionToSelfToggle.RegisterValueChangedCallback(evt =>
            {
                var anyStateSelected = _selectedTransitions.Where(x => IsAnyStateTransition(x)).ToArray();
                if (anyStateSelected.Length == 0) return;
                bool value = evt.newValue;
                Undo.RecordObjects(anyStateSelected, "Edit Transition");
                foreach (var transition in anyStateSelected) transition.canTransitionToSelf = value;
                foreach (var transition in anyStateSelected) EditorUtility.SetDirty(transition);
            });
            _soloToggle = new Toggle(L10n.Get("transitions.solo"));
            _soloToggle.RegisterValueChangedCallback(evt => { bool value = evt.newValue; SetOnAll(transition => transition.solo = value); });
            parent.Add(BuildPropertyRow(_canTransitionToSelfToggle, _soloToggle));
        }

        static VisualElement BuildPropertyRow(VisualElement left, VisualElement right)
        {
            left.AddToClassList("ygdr-transitions-property-field");
            left.AddToClassList("u-flex-fill");
            right.AddToClassList("ygdr-transitions-property-field");
            right.AddToClassList("u-flex-fill");
            var row = BuildRow("ygdr-transitions-property-row", null, left, right);
            row.AddToClassList("u-row");
            row.AddToClassList("u-mb-2");
            return row;
        }

        /* Pushes current selection values into the property controls without re-firing their change callbacks. */
        void RefreshTransitionsProperties()
        {
            int count = _selectedTransitions.Length;
            bool empty = count == 0;
            bool multi = count > 1;
            var first = empty ? null : _selectedTransitions[0];
            if (empty) return;

            _hasExitTimeToggle.SetValueWithoutNotify(first.hasExitTime);
            _hasExitTimeToggle.showMixedValue = multi && _selectedTransitions.Any(x => x.hasExitTime != first.hasExitTime);
            _exitTimeField.SetValueWithoutNotify(first.exitTime);
            _exitTimeField.showMixedValue = multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.exitTime, first.exitTime));

            _hasFixedDurationToggle.SetValueWithoutNotify(first.hasFixedDuration);
            _hasFixedDurationToggle.showMixedValue = multi && _selectedTransitions.Any(x => x.hasFixedDuration != first.hasFixedDuration);
            _durationField.SetValueWithoutNotify(first.duration);
            _durationField.showMixedValue = multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.duration, first.duration));

            _offsetField.SetValueWithoutNotify(first.offset);
            _offsetField.showMixedValue = multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.offset, first.offset));

            int interruptionIndex = Mathf.Clamp((int)first.interruptionSource, 0, _interruptionSourcePopup.choices.Count - 1);
            _interruptionSourcePopup.SetValueWithoutNotify(_interruptionSourcePopup.choices[interruptionIndex]);
            _interruptionSourcePopup.showMixedValue = multi && _selectedTransitions.Any(x => x.interruptionSource != first.interruptionSource);

            _orderedInterruptionToggle.SetValueWithoutNotify(first.orderedInterruption);
            _orderedInterruptionToggle.showMixedValue = multi && _selectedTransitions.Any(x => x.orderedInterruption != first.orderedInterruption);
            _muteToggle.SetValueWithoutNotify(first.mute);
            _muteToggle.showMixedValue = multi && _selectedTransitions.Any(x => x.mute != first.mute);

            var anyStateSelected = _selectedTransitions.Where(x => IsAnyStateTransition(x)).ToArray();
            _canTransitionToSelfToggle.SetEnabled(anyStateSelected.Length > 0);
            _canTransitionToSelfToggle.SetValueWithoutNotify(anyStateSelected.Length > 0 && anyStateSelected[0].canTransitionToSelf);
            _canTransitionToSelfToggle.showMixedValue = anyStateSelected.Length > 0 && anyStateSelected.Any(x => x.canTransitionToSelf != anyStateSelected[0].canTransitionToSelf);
            _soloToggle.SetValueWithoutNotify(first.solo);
            _soloToggle.showMixedValue = multi && _selectedTransitions.Any(x => x.solo != first.solo);
        }

        /* Returns a "Source → Destination" display string for a transition, resolving anyState, exit, and SM destinations. */
        string GetTransitionLabel(AnimatorStateTransition transition)
        {
            string sourceName = FindSrcName(_controller, transition) ?? "Any State";
            string destinationName = transition.isExit ? "Exit"
                : transition.destinationState != null ? transition.destinationState.name
                : transition.destinationStateMachine != null ? transition.destinationStateMachine.name
                : "?";
            return $"{sourceName} → {destinationName}";
        }

        /* Returns an "Entry → Destination" display string for an entry transition. */
        static string GetEntryTransitionLabel(AnimatorTransition transition)
        {
            string destinationName = transition.isExit ? "Exit"
                : transition.destinationState != null ? transition.destinationState.name
                : transition.destinationStateMachine != null ? transition.destinationStateMachine.name
                : "?";
            return $"Entry → {destinationName}";
        }

        /* Searches all layers in the controller for the state that owns the transition, returning its name or null. */
        static string FindSrcName(AnimatorController controller, AnimatorStateTransition transition)
        {
            if (controller == null) return null;
            foreach (var layer in controller.layers)
            {
                var name = WalkSM(layer.stateMachine, sm =>
                {
                    if (sm.anyStateTransitions.Contains(transition)) return "Any State";
                    foreach (var childState in sm.states)
                        if (childState.state.transitions.Contains(transition)) return childState.state.name;
                    return null;
                });
                if (name != null) return name;
            }
            return null;
        }

        /* Scans sm.states (single level, not recursive) for the state that owns transition — for callers that
           already know the owning SM (e.g. via FindOwnerSM) and want the AnimatorState itself, not just its name. */
        static AnimatorState FindSourceState(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            foreach (var childState in sm.states)
                if (childState.state.transitions.Contains(transition)) return childState.state;
            return null;
        }

        /* Recursively searches sm and its sub-SMs, returning the first non-null result of tryMatch (checked at each level, depth-first). */
        static T WalkSM<T>(AnimatorStateMachine sm, Func<AnimatorStateMachine, T> tryMatch) where T : class
        {
            var result = tryMatch(sm);
            if (result != null) return result;
            foreach (var childSM in sm.stateMachines)
            {
                var found = WalkSM(childSM.stateMachine, tryMatch);
                if (found != null) return found;
            }
            return null;
        }

        /* ── Conditions cache ────────────────────────────────────────────── */

        bool _conditionCacheDirty = true;
        bool _cachedForSharedMode;
        UnityEngine.Object[] _cachedForOwners;
        List<CondEntry> _cachedEntries;
        HashSet<(UnityEngine.Object, string)> _cachedDuplicateParameters;
        int _condDragIndex = -1;
        VisualElement _condDragIndicator;
        string[] _cachedParameterNames;
        HashSet<string> _cachedParamNameSet;
        Dictionary<string, string> _danglingParamResolution;

        /* Returns true if the given owner array matches the cached selection — used to skip condition rebuilds. */
        bool ConditionSelectionUnchanged(UnityEngine.Object[] owners)
        {
            if (_cachedForOwners == null || _cachedForOwners.Length != owners.Length) return false;
            for (int i = 0; i < owners.Length; i++)
                if (_cachedForOwners[i] != owners[i]) return false;
            return true;
        }

        void InvalidateConditionCache() { _conditionCacheDirty = true; _cachedParamNameSet = null; }

        /* Toggles one shared-condition match criterion (Name/Mode/Value), refusing to leave all three inactive. */
        void SetConditionMatchCriterion(ref bool criterion, bool value)
        {
            int activeCount = (_matchConditionName ? 1 : 0) + (_matchConditionMode ? 1 : 0) + (_matchConditionValue ? 1 : 0);
            bool willTurnOff = criterion && !value;
            if (willTurnOff && activeCount <= 1) return;
            criterion = value;
            InvalidateConditionCache();
        }

        bool ParameterListChanged()
        {
            if (_controller == null) return _cachedParameterNames != null && _cachedParameterNames.Length > 0;
            var parameters = _controller.parameters;
            if (_cachedParameterNames == null || _cachedParameterNames.Length != parameters.Length) return true;
            for (int i = 0; i < parameters.Length; i++)
                if (_cachedParameterNames[i] != parameters[i].name) return true;
            return false;
        }

        /* Called before _cachedParameterNames update so old names are still available. */
        Dictionary<string, string> BuildDanglingResolution()
        {
            var map = new Dictionary<string, string>();
            if (_controller == null || _cachedParameterNames == null) return map;
            var currentParams = _controller.parameters;
            if (currentParams.Length != _cachedParameterNames.Length) return map;
            for (int i = 0; i < _cachedParameterNames.Length; i++)
            {
                string oldName = _cachedParameterNames[i];
                string newName = currentParams[i].name;
                if (oldName != newName)
                    map[oldName] = newName;
            }
            return map;
        }

        /* ── Conditions section ──────────────────────────────────────────── */

        /* Builds the header toolbar (mode toggle + N/M/V + switch/merge/separate) and the rows container. */
        VisualElement BuildConditionsSection()
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-cond-section");
            container.Add(BuildConditionsHeader());

            _condRowsContainer = new VisualElement();
            _condRowsContainer.AddToClassList("ygdr-cond-body");
            container.Add(_condRowsContainer);

            _condAddRow = new VisualElement();
            _condAddRow.AddToClassList("ygdr-cond-add-row");
            _condAddButton = new Button(() => { AddConditionToAll(); RebuildConditionRows(); }) { text = "+" };
            _condAddButton.AddToClassList("ygdr-cond-add-btn");
            StyleSecondaryButton(_condAddButton);
            _condAddRow.Add(_condAddButton);
            container.Add(_condAddRow);

            return container;
        }

        VisualElement BuildConditionsHeader()
        {
            _condHeader = new VisualElement();
            _condHeader.AddToClassList("ygdr-cond-header");

            _condModeButton = new Button(() => { _showSharedConditions = !_showSharedConditions; RebuildConditionRows(); });
            _condModeButton.AddToClassList("ygdr-cond-mode-btn");
            _condModeButton.tooltip = L10n.Get("transitions.tooltip.toggle_conditions");
            StyleConditionHeaderButton(_condModeButton, () => false);
            _condHeader.Add(_condModeButton);

            _matchNameButton = BuildConditionHeaderIconButton("N", L10n.Get("transitions.tooltip.match_name"), () => { SetConditionMatchCriterion(ref _matchConditionName, !_matchConditionName); RebuildConditionRows(); }, () => _matchConditionName);
            _matchModeButton = BuildConditionHeaderIconButton("M", L10n.Get("transitions.tooltip.match_mode"), () => { SetConditionMatchCriterion(ref _matchConditionMode, !_matchConditionMode); RebuildConditionRows(); }, () => _matchConditionMode);
            _matchValueButton = BuildConditionHeaderIconButton("V", L10n.Get("transitions.tooltip.match_value"), () => { SetConditionMatchCriterion(ref _matchConditionValue, !_matchConditionValue); RebuildConditionRows(); }, () => _matchConditionValue);
            _condHeader.Add(_matchNameButton);
            _condHeader.Add(_matchModeButton);
            _condHeader.Add(_matchValueButton);

            _condSwitchButton = BuildConditionHeaderIconButton("⇄", L10n.Get("transitions.tooltip.switch_modes"), () => { ReverseAllConditions(); RebuildConditionRows(); }, () => false);
            _condHeader.Add(_condSwitchButton);

            _condMergeButton = new Button(() => { MergeTransitions(); MergeEntryTransitions(); });
            _condMergeButton.AddToClassList("ygdr-cond-header-icon-btn");
            _condMergeButton.style.backgroundImage = new StyleBackground(MergeIconTex);
            _condMergeButton.tooltip = L10n.Get("transitions.tooltip.merge");
            StyleConditionHeaderButton(_condMergeButton, () => false);
            _condHeader.Add(_condMergeButton);

            _condSeparateButton = new Button(() => { SeparateTransitions(); SeparateEntryTransitions(); });
            _condSeparateButton.AddToClassList("ygdr-cond-header-icon-btn");
            _condSeparateButton.style.backgroundImage = new StyleBackground(SeparateIconTex);
            _condSeparateButton.tooltip = L10n.Get("transitions.tooltip.separate");
            StyleConditionHeaderButton(_condSeparateButton, () => false);
            _condHeader.Add(_condSeparateButton);

            return _condHeader;
        }

        static Button BuildConditionHeaderIconButton(string label, string tooltip, Action onClick, Func<bool> isActive)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("ygdr-cond-header-icon-btn");
            button.tooltip = tooltip;
            StyleConditionHeaderButton(button, isActive);
            return button;
        }

        /* Inline backgroundColor, not USS :hover, so it can live-update from the palette; "active" permanently shows the hover tint. */
        static void StyleConditionHeaderButton(VisualElement button, Func<bool> isActive) =>
            StyleHoverTint(button, isActive, () => AccentHoverColor, () => SharedWindowStyles.AccentColor);

        /* Colors are getters, not baked values — callbacks bind once at build time, so a baked color would make
           hover-leave snap back to the build-time palette instead of the one refreshed later via style.backgroundColor. */
        internal static void StyleHoverTint(VisualElement element, Func<bool> isActive, Func<Color> hoverColor, Func<StyleColor> baseColor)
        {
            element.RegisterCallback<MouseEnterEvent>(_ => element.style.backgroundColor = hoverColor());
            element.RegisterCallback<MouseLeaveEvent>(_ => element.style.backgroundColor = isActive() ? hoverColor() : baseColor());
            element.style.backgroundColor = isActive() ? hoverColor() : baseColor();
        }

        internal static Color AccentHoverColor => new Color(SharedWindowStyles.AccentColor.r + 0.1f, SharedWindowStyles.AccentColor.g + 0.1f, SharedWindowStyles.AccentColor.b + 0.1f, 1f);

        /* +/- buttons: same bg as the section body (blend in) with a hover tint lighter than SecondaryColor. */
        static Color SecondaryButtonHoverColor => new Color(SharedWindowStyles.SecondaryColor.r + 0.14f, SharedWindowStyles.SecondaryColor.g + 0.14f, SharedWindowStyles.SecondaryColor.b + 0.14f, 1f);
        static void StyleSecondaryButton(VisualElement button) =>
            StyleHoverTint(button, () => false, () => SecondaryButtonHoverColor, () => SharedWindowStyles.SecondaryColor);

        static void StyleAccentButton(VisualElement button) =>
            StyleHoverTint(button, () => false, () => AccentHoverColor, () => SharedWindowStyles.AccentColor);

        void RefreshConditionsHeaderState()
        {
            _condModeButton.text = _showSharedConditions ? L10n.Get("transitions.shared_conditions") : L10n.Get("transitions.all_conditions");
            _matchNameButton.style.backgroundColor = _matchConditionName ? AccentHoverColor : SharedWindowStyles.AccentColor;
            _matchModeButton.style.backgroundColor = _matchConditionMode ? AccentHoverColor : SharedWindowStyles.AccentColor;
            _matchValueButton.style.backgroundColor = _matchConditionValue ? AccentHoverColor : SharedWindowStyles.AccentColor;
        }

        /* Threshold value edits do NOT trigger this — would steal focus mid-type. */
        void RebuildConditionRows()
        {
            if (_condRowsContainer == null) return;
            RefreshConditionsHeaderState();

            var allOwners = AllSelectedOwners();
            if (_conditionCacheDirty || _cachedEntries == null || _cachedForSharedMode != _showSharedConditions || !ConditionSelectionUnchanged(allOwners) || ParameterListChanged())
            {
                _cachedEntries = GetDisplayedConditions(allOwners);
                _cachedDuplicateParameters = new HashSet<(UnityEngine.Object, string)>(
                    _cachedEntries.GroupBy(entry => (entry.owner, entry.condition.parameter))
                                  .Where(group => group.Count() > 1)
                                  .Where(group =>
                                  {
                                      var paramType = GetParamType(group.Key.Item2);
                                      if (paramType == AnimatorControllerParameterType.Float)
                                      {
                                          var thresholds = group.Select(e => e.condition.threshold).ToList();
                                          return thresholds.Count != thresholds.Distinct().Count();
                                      }
                                      if (paramType == AnimatorControllerParameterType.Int)
                                      {
                                          /* Equals/NotEqual are exact-match and always suspicious in pairs; Greater/Less only warn on an exact threshold clash. */
                                          var conditions = group.Select(e => e.condition).ToList();
                                          int eqNeqCount = conditions.Count(c => c.mode == AnimatorConditionMode.Equals || c.mode == AnimatorConditionMode.NotEqual);
                                          if (eqNeqCount > 1) return true;
                                          var rangeThresholds = conditions.Where(c => c.mode == AnimatorConditionMode.Greater || c.mode == AnimatorConditionMode.Less)
                                                                           .Select(c => c.threshold).ToList();
                                          return rangeThresholds.Count != rangeThresholds.Distinct().Count();
                                      }
                                      return true;
                                  })
                                  .Select(group => group.Key));
                _cachedForOwners = allOwners;
                _cachedForSharedMode = _showSharedConditions;
                _danglingParamResolution = BuildDanglingResolution();
                _cachedParameterNames = _controller != null ? _controller.parameters.Select(parameter => parameter.name).ToArray() : Array.Empty<string>();
                _cachedParamNameSet   = new HashSet<string>(_cachedParameterNames);

                _conditionCacheDirty = false;
            }
            var entries = _cachedEntries;
            var duplicateParameters = _cachedDuplicateParameters;

            /* Reorder only makes unambiguous sense within one owner's conditions — with multiple owners
               flattened together in individual mode, a cross-owner drag has no coherent meaning. Same
               condition _condAddRow already uses to hide when editing wouldn't apply uniformly. */
            bool reorderEnabled = _showSharedConditions || allOwners.Length <= 1;

            _condRowsContainer.Clear();
            if (entries.Count == 0)
            {
                var emptyLabel = new Label(L10n.Get("transitions.conditions_empty"));
                emptyLabel.AddToClassList("ygdr-empty-label");
                _condRowsContainer.Add(emptyLabel);
            }
            else
            {
                int groupIndex = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    bool newGroup = !_showSharedConditions && i > 0 && entries[i].owner != entries[i - 1].owner;
                    if (newGroup) groupIndex++;
                    if (entries[i].owner == null) continue;
                    bool altRow = _showSharedConditions ? i % 2 == 1 : groupIndex % 2 == 1;
                    var rowElement = BuildConditionRow(entries[i], duplicateParameters, altRow, reorderEnabled ? i : -1);
                    if (newGroup) rowElement.AddToClassList("ygdr-cond-row-group-gap");
                    _condRowsContainer.Add(rowElement);
                }
            }

            _condAddRow.style.display = (_showSharedConditions || allOwners.Length <= 1) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static VisualElement BuildDropdownArrow()
        {
            var arrow = new VisualElement();
            arrow.AddToClassList("ygdr-cond-dropdown-arrow");
            arrow.style.backgroundImage = new StyleBackground(DropdownArrowIconTex);
            arrow.pickingMode = PickingMode.Ignore;
            return arrow;
        }

        VisualElement BuildConditionFilterButton(AnimatorCondition condition)
        {
            var filterButton = new Button(() => SelectMatchingConditionTransitions(condition));
            filterButton.AddToClassList("ygdr-cond-filter-btn");
            filterButton.tabIndex = -1;
            filterButton.style.backgroundImage = new StyleBackground(FilterIconTex);
            filterButton.tooltip = L10n.Get("transitions.tooltip.select_matching");
            return filterButton;
        }

        /* Manual pointer-drag handle — ListView's built-in Animated reorder fought our interactive row
           content (buttons/dropdowns inside draggable rows caused red flicker + recycled/blank rows).
           Small self-contained drag instead: capture on the grip only, insertion-line indicator, drop
           reads the target row's stored displayIndex (row.userData) and hands off to MoveCondition. */
        VisualElement BuildConditionDragHandle(VisualElement row, int displayIndex)
        {
            var grip = new Image { image = DragHandleIconTex };
            grip.AddToClassList("ygdr-cond-drag-handle");

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _condDragIndex = displayIndex;
                row.AddToClassList("ygdr-cond-row-dragging");
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_condDragIndex < 0 || !grip.HasPointerCapture(evt.pointerId)) return;
                ShowConditionDragIndicator(evt.position);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_condDragIndex < 0) return;
                grip.ReleasePointer(evt.pointerId);
                row.RemoveFromClassList("ygdr-cond-row-dragging");
                int dropIndex = FindConditionDropIndex(evt.position);
                HideConditionDragIndicator();
                int fromIndex = _condDragIndex;
                _condDragIndex = -1;
                if (dropIndex >= 0 && dropIndex != fromIndex)
                {
                    MoveCondition(fromIndex, dropIndex);
                    RebuildConditionRows();
                }
                evt.StopPropagation();
            });
            return grip;
        }

        /* Row whose vertical midpoint the pointer is above, or the last row if pointer is below all of them.
           Shared by drop-index lookup (on release) and the drag indicator (on every move). */
        VisualElement FindConditionRowUnderPointer(Vector2 pointerPosition)
        {
            float localY = _condRowsContainer.WorldToLocal(pointerPosition).y;
            VisualElement last = null;
            foreach (var child in _condRowsContainer.Children())
            {
                if (!(child.userData is int)) continue;
                if (localY < child.layout.y + child.layout.height / 2f) return child;
                last = child;
            }
            return last;
        }

        int FindConditionDropIndex(Vector2 pointerPosition)
        {
            var row = FindConditionRowUnderPointer(pointerPosition);
            return row != null ? (int)row.userData : -1;
        }

        void ShowConditionDragIndicator(Vector2 pointerPosition)
        {
            _condDragIndicator ??= new VisualElement { pickingMode = PickingMode.Ignore };
            _condDragIndicator.AddToClassList("ygdr-cond-drag-indicator");
            if (_condDragIndicator.parent != _condRowsContainer) _condRowsContainer.Add(_condDragIndicator);

            float localY = _condRowsContainer.WorldToLocal(pointerPosition).y;
            var row = FindConditionRowUnderPointer(pointerPosition);
            float indicatorY = row == null ? 0f
                : localY < row.layout.y + row.layout.height / 2f ? row.layout.y
                : row.layout.y + row.layout.height;
            _condDragIndicator.style.top = indicatorY;
        }

        void HideConditionDragIndicator() => _condDragIndicator?.RemoveFromHierarchy();

        /* Builds one condition row: optional drag handle (reorderIndex >= 0), parameter dropdown, mode/value controls, remove button. */
        VisualElement BuildConditionRow(CondEntry entry, HashSet<(UnityEngine.Object, string)> duplicateParameters, bool altRow, int reorderIndex)
        {
            var row = new VisualElement();
            if (reorderIndex >= 0)
            {
                row.userData = reorderIndex;
                row.Add(BuildConditionDragHandle(row, reorderIndex));
            }

            var ownerConditions = GetConditions(entry.owner);
            var condition = entry.index < ownerConditions.Length ? ownerConditions[entry.index] : entry.condition;
            if (_danglingParamResolution != null && _danglingParamResolution.TryGetValue(condition.parameter, out string resolvedParam))
                condition = new AnimatorCondition { parameter = resolvedParam, mode = condition.mode, threshold = condition.threshold };

            row.AddToClassList("ygdr-cond-row");

            /* Accent strip only in "All Conditions" mode — shared mode's altRow is per-row, not per-group. */
            bool groupAccent = !_showSharedConditions && altRow;
            row.style.borderLeftWidth = 6;
            row.style.borderLeftColor = groupAccent ? SecondaryButtonHoverColor : Color.clear;

            if (_controller == null || _controller.parameters.Length == 0)
            {
                var paramLabel = new Label(condition.parameter);
                paramLabel.AddToClassList("ygdr-cond-param-label");
                row.Add(paramLabel);
                row.Add(BuildConditionFilterButton(condition));
                var inertRemoveButton = new Button { text = "−" };
                inertRemoveButton.AddToClassList("ygdr-cond-remove-btn");
                StyleSecondaryButton(inertRemoveButton);
                row.Add(inertRemoveButton);
                return row;
            }

            var capturedEntry = entry;

            bool parameterExists = _cachedParamNameSet?.Contains(condition.parameter) ?? false;
            if (!parameterExists)
            {
                var paramLabel = new Label(condition.parameter);
                paramLabel.AddToClassList("ygdr-cond-param-label");
                paramLabel.AddToClassList("ygdr-cond-param-label-missing");
                row.Add(paramLabel);
                row.Add(BuildConditionFilterButton(condition));
                var removeButton = new Button(() => { RemoveConditionFromTargets(capturedEntry); RebuildConditionRows(); }) { text = "−" };
                removeButton.AddToClassList("ygdr-cond-remove-btn");
                StyleSecondaryButton(removeButton);
                row.Add(removeButton);
                return row;
            }

            var parameterType = GetParamType(condition.parameter);
            bool showTypeIcons = AnimatorDefaultSettings.Load().showParamTypeIcons;
            bool isDuplicateParam = duplicateParameters.Contains((entry.owner, condition.parameter));
            var capturedCondition = condition;

            var paramCell = new VisualElement();
            paramCell.AddToClassList("ygdr-cond-param-cell");

            Button paramButton = null;
            paramButton = new Button(() =>
            {
                ShowParameterDropdown(paramButton.worldBound, capturedCondition.parameter, newParam =>
                {
                    var newType = GetParamType(newParam);
                    var sourceType = GetParamType(capturedCondition.parameter);
                    AnimatorConditionMode seededMode;
                    if (sourceType == newType) seededMode = capturedCondition.mode;
                    else if (AnimatorParameterOps.TryConvertCondition(capturedCondition, sourceType, newType, out var converted)) seededMode = converted.mode;
                    else seededMode = DefaultModeForType(newType);
                    ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition { parameter = newParam, mode = seededMode, threshold = 0f }, preserveThreshold: true);
                    RebuildConditionRows();
                });
            });
            paramButton.AddToClassList("ygdr-cond-param-dropdown");
            paramButton.tabIndex = -1;
            StyleAccentButton(paramButton);
            paramCell.Add(paramButton);
            SetTruncatedDropdownLabel(paramButton, entry.mixedName ? "—" : condition.parameter, 74f);
            RegisterDropdownLabelResize(paramButton, 74f);

            /* Type label / duplicate warning / filter icon overlay on top of the dropdown button, not beside it. */
            paramButton.Add(BuildDropdownArrow());

            if (showTypeIcons)
            {
                var typeLabel = new Label(parameterType.ToString());
                typeLabel.AddToClassList("ygdr-cond-type-label");
                typeLabel.style.right = isDuplicateParam ? 54 : 36;
                paramCell.Add(typeLabel);
            }

            if (isDuplicateParam)
            {
                paramCell.Add(BuildWarningIcon(DuplicateParamIconTex, L10n.Get("transitions.duplicate_param_tooltip"), "ygdr-cond-duplicate-icon"));
            }

            paramCell.Add(BuildConditionFilterButton(condition));
            row.Add(paramCell);

            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                bool isTrue = condition.mode != AnimatorConditionMode.IfNot;
                var boolButton = new Button(() =>
                {
                    ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition { parameter = capturedCondition.parameter, mode = isTrue ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, threshold = 0f }, preserveParameter: true);
                    RebuildConditionRows();
                })
                { text = entry.mixedMode ? "—" : (isTrue ? L10n.Get("transitions.bool_true") : L10n.Get("transitions.bool_false")) };
                boolButton.AddToClassList("ygdr-cond-bool-btn");
                boolButton.AddToClassList(isTrue ? "ygdr-cond-bool-btn-true" : "ygdr-cond-bool-btn-false");
                boolButton.tabIndex = -1;
                StyleAccentButton(boolButton);
                row.Add(boolButton);
            }
            else if (parameterType != AnimatorControllerParameterType.Trigger)
            {
                var modeButton = new Button(() =>
                {
                    var menu = new GenericMenu();
                    foreach (var conditionMode in ModesForType(parameterType))
                    {
                        menu.AddItem(new GUIContent(ModeLabel(conditionMode)), conditionMode == capturedCondition.mode, () =>
                        {
                            ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition { parameter = capturedCondition.parameter, mode = conditionMode, threshold = 0f }, preserveThreshold: true, preserveParameter: true);
                            RebuildConditionRows();
                        });
                    }
                    menu.ShowAsContext();
                })
                { text = entry.mixedMode ? "—" : ModeLabel(condition.mode) };
                modeButton.AddToClassList("ygdr-cond-mode-dropdown");
                modeButton.tabIndex = -1;
                StyleAccentButton(modeButton);
                modeButton.Add(BuildDropdownArrow());
                row.Add(modeButton);

                if (parameterType == AnimatorControllerParameterType.Int)
                {
                    var intField = new IntegerField { value = (int)condition.threshold, showMixedValue = entry.mixedThreshold };
                    intField.AddToClassList("ygdr-cond-value-field");
                    /* ValueChangedCallback only fires when the value differs from the field's own starting value —
                       in shared mode with mixed thresholds that misses the case where the typed value matches that
                       starting value but other owners still differ. Sync on blur instead, using the current value. */
                    intField.RegisterCallback<FocusOutEvent>(_ =>
                    {
                        intField.showMixedValue = false;
                        ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition { parameter = capturedCondition.parameter, mode = capturedCondition.mode, threshold = intField.value }, preserveParameter: true, preserveMode: true);
                    });
                    row.Add(intField);
                }
                else
                {
                    var floatField = new FloatField { value = condition.threshold, showMixedValue = entry.mixedThreshold };
                    floatField.AddToClassList("ygdr-cond-value-field");
                    /* See intField above: sync on blur instead of on change, so a typed value matching the field's
                       own starting value still propagates to owners whose threshold differs. */
                    floatField.RegisterCallback<FocusOutEvent>(_ =>
                    {
                        floatField.showMixedValue = false;
                        ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition { parameter = capturedCondition.parameter, mode = capturedCondition.mode, threshold = floatField.value }, preserveParameter: true, preserveMode: true);
                    });
                    row.Add(floatField);
                }
            }

            var removeBtn = new Button(() => { RemoveConditionFromTargets(capturedEntry); RebuildConditionRows(); }) { text = "−" };
            removeBtn.AddToClassList("ygdr-cond-remove-btn");
            removeBtn.tabIndex = -1;
            StyleSecondaryButton(removeBtn);
            row.Add(removeBtn);

            return row;
        }

        readonly struct CondEntry
        {
            internal readonly UnityEngine.Object owner;
            internal readonly AnimatorCondition condition;
            internal readonly int index;
            internal readonly Dictionary<UnityEngine.Object, int> sharedIndices;
            internal readonly bool mixedThreshold;
            internal readonly bool mixedName;
            internal readonly bool mixedMode;

            internal CondEntry(UnityEngine.Object owner, AnimatorCondition condition, int index)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = null; this.mixedThreshold = false; this.mixedName = false; this.mixedMode = false; }

            internal CondEntry(UnityEngine.Object owner, AnimatorCondition condition, int index, Dictionary<UnityEngine.Object, int> sharedIndices, bool mixedThreshold, bool mixedName, bool mixedMode)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = sharedIndices; this.mixedThreshold = mixedThreshold; this.mixedName = mixedName; this.mixedMode = mixedMode; }

            internal int IndexFor(UnityEngine.Object obj)
                => sharedIndices != null && sharedIndices.TryGetValue(obj, out int idx) ? idx : index;
        }

        /* Builds the flat list of CondEntry to display: all conditions per owner in individual mode, or only conditions shared across all selected owners in shared mode. */
        List<CondEntry> GetDisplayedConditions(UnityEngine.Object[] owners)
        {
            var result = new List<CondEntry>();
            if (owners.Length == 0) return result;

            if (!_showSharedConditions)
            {
                foreach (var owner in owners)
                {
                    var conditions = GetConditions(owner);
                    for (int i = 0; i < conditions.Length; i++)
                        result.Add(new CondEntry(owner, conditions[i], i));
                }
                return result;
            }

            var first = owners[0];
            var firstConditions = GetConditions(first);
            var claimed = new Dictionary<UnityEngine.Object, HashSet<int>>();
            foreach (var owner in owners) claimed[owner] = new HashSet<int>();

            for (int i = 0; i < firstConditions.Length; i++)
            {
                var condition = firstConditions[i];
                var indexMap = new Dictionary<UnityEngine.Object, int> { [first] = i };
                bool allMatch = true;

                foreach (var owner in owners)
                {
                    if (owner == first) continue;
                    int matchIdx = FindConditionIndexExcluding(owner, condition.parameter, condition.mode, condition.threshold, claimed[owner], _matchConditionName, _matchConditionMode, _matchConditionValue);
                    if (matchIdx < 0) { allMatch = false; break; }
                    indexMap[owner] = matchIdx;
                }

                if (!allMatch) continue;
                foreach (var pair in indexMap) claimed[pair.Key].Add(pair.Value);

                bool AnyOwnerDiffers(Func<AnimatorCondition, object> field) => indexMap.Any(pair =>
                {
                    if (pair.Key == first) return false;
                    var ownerConditions = GetConditions(pair.Key);
                    return pair.Value < ownerConditions.Length && !Equals(field(ownerConditions[pair.Value]), field(condition));
                });
                bool mixedThreshold = !_matchConditionValue && AnyOwnerDiffers(c => c.threshold);
                bool mixedName      = !_matchConditionName  && AnyOwnerDiffers(c => c.parameter);
                bool mixedMode      = !_matchConditionMode  && AnyOwnerDiffers(c => c.mode);
                result.Add(new CondEntry(first, condition, i, indexMap, mixedThreshold, mixedName, mixedMode));
            }
            return result;
        }

        void SelectMatchingConditionTransitions(AnimatorCondition target)
        {
            if (_activeStateMachine == null) return;
            var candidates = _activeStateMachine.states
                .SelectMany(childState => childState.state.transitions)
                .Concat<UnityEngine.Object>(_activeStateMachine.anyStateTransitions)
                .Concat(_activeStateMachine.entryTransitions);
            Selection.objects = candidates
                .Where(obj => GetConditions(obj).Any(c => ConditionMatchesCriteria(c, target, _matchConditionName, _matchConditionMode, _matchConditionValue)))
                .ToArray();
        }

        /* Used by Menus/States/StatesBehaviours.Driver (IMGUI) and by SetTruncatedDropdownLabel below (UI Toolkit). */
        static string TruncateTextLeft(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            const string ellipsis = "…";
            for (int start = 1; start < text.Length; start++)
            {
                string candidate = ellipsis + text.Substring(start);
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) return candidate;
            }
            return ellipsis;
        }

        /* Truncated from the left (tail stays visible) since USS text-overflow only ellipsizes from the right.
           fullText is stashed on tooltip so ApplyTruncatedDropdownLabel can re-truncate against the button's current width. */
        static void SetTruncatedDropdownLabel(Button button, string fullText, float reservedWidth)
        {
            button.tooltip = fullText;
            ApplyTruncatedDropdownLabel(button, reservedWidth);
        }

        static void ApplyTruncatedDropdownLabel(Button button, float reservedWidth)
        {
            float width = button.resolvedStyle.width;
            button.text = width > 1f && !float.IsNaN(width)
                ? TruncateTextLeft(button.tooltip, EditorStyles.popup, width - reservedWidth)
                : button.tooltip;
        }

        /* Handles both initial layout (not yet resolved at creation time) and later resizes. */
        static void RegisterDropdownLabelResize(Button button, float reservedWidth)
        {
            button.RegisterCallback<GeometryChangedEvent>(_ => ApplyTruncatedDropdownLabel(button, reservedWidth));
        }

        /* Looks up the parameter type by name from the active controller, defaulting to Float if not found. */
        AnimatorControllerParameterType GetParamType(string paramName)
        {
            if (_controller == null) return AnimatorControllerParameterType.Float;
            var parameter = _controller.parameters.FirstOrDefault(x => x.name == paramName);
            return parameter?.type ?? AnimatorControllerParameterType.Float;
        }

        static AnimatorConditionMode[] ModesForType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Bool    => new[] { AnimatorConditionMode.If, AnimatorConditionMode.IfNot },
            AnimatorControllerParameterType.Trigger => new[] { AnimatorConditionMode.If },
            AnimatorControllerParameterType.Int     => new[] { AnimatorConditionMode.Equals, AnimatorConditionMode.NotEqual, AnimatorConditionMode.Greater, AnimatorConditionMode.Less },
            _                                       => new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less }
        };

        static AnimatorConditionMode DefaultModeForType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Bool or AnimatorControllerParameterType.Trigger => AnimatorConditionMode.If,
            AnimatorControllerParameterType.Int => AnimatorConditionMode.Equals,
            _ => AnimatorConditionMode.Greater
        };

        static string ModeLabel(AnimatorConditionMode mode) => mode switch
        {
            AnimatorConditionMode.If       => L10n.Get("transitions.mode_true"),
            AnimatorConditionMode.IfNot    => L10n.Get("transitions.mode_false"),
            AnimatorConditionMode.Equals   => L10n.Get("transitions.mode_equals"),
            AnimatorConditionMode.NotEqual => L10n.Get("transitions.mode_not_equal"),
            AnimatorConditionMode.Greater  => L10n.Get("transitions.mode_greater"),
            AnimatorConditionMode.Less     => L10n.Get("transitions.mode_less"),
            _                             => mode.ToString()
        };

        /* preserveParameter/preserveMode/preserveThreshold keep that field as each owner's own value — used in shared
           mode when the field isn't a required match criterion, so it may legitimately differ per owner. */
        void ReplaceConditionOnTargets(CondEntry entry, AnimatorCondition replacement, bool preserveThreshold = false, bool preserveParameter = false, bool preserveMode = false)
        {
            InvalidateConditionCache();
            if (!_showSharedConditions)
            {
                var ownerConditions = GetConditions(entry.owner);
                if (entry.index < ownerConditions.Length)
                    RebuildConditions(entry.owner, entry.index, replacement);
            }
            else
            {
                foreach (var owner in AllSelectedOwners())
                {
                    int idx = entry.IndexFor(owner);
                    var ownerConditions = GetConditions(owner);
                    if (idx < 0 || idx >= ownerConditions.Length) continue;
                    var own = ownerConditions[idx];
                    var actual = new AnimatorCondition
                    {
                        parameter = preserveParameter ? own.parameter : replacement.parameter,
                        mode      = preserveMode      ? own.mode      : replacement.mode,
                        threshold = preserveThreshold  ? own.threshold : replacement.threshold
                    };
                    RebuildConditions(owner, idx, actual);
                }
            }
        }

        /* Removes the entry's condition from one owner (individual mode) or from all selected owners (shared mode). */
        void RemoveConditionFromTargets(CondEntry entry)
        {
            InvalidateConditionCache();
            var targets = _showSharedConditions ? AllSelectedOwners() : new[] { entry.owner };
            foreach (var owner in targets)
            {
                int idx = _showSharedConditions ? entry.IndexFor(owner) : entry.index;
                var ownerConditions = GetConditions(owner);
                if (idx < 0 || idx >= ownerConditions.Length) continue;
                Undo.RecordObject(owner, "Remove Condition");
                var allConditions = ownerConditions.ToArray();
                foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
                for (int i = 0; i < allConditions.Length; i++)
                    if (i != idx) AddConditionTo(owner, allConditions[i].mode, allConditions[i].threshold, allConditions[i].parameter);
                EditorUtility.SetDirty(owner);
            }
        }

        /* Drag-reorder callback from the manual condition-row drag handle. Individual mode only reaches
           here with a single owner (reorderEnabled gates multi-owner individual mode off in the caller),
           so newIndex is just that owner's local index. */
        void MoveCondition(int oldIndex, int newIndex)
        {
            var entries = _cachedEntries;
            if (entries == null || oldIndex < 0 || oldIndex >= entries.Count) return;
            newIndex = Mathf.Clamp(newIndex, 0, entries.Count - 1);
            if (oldIndex == newIndex) return;

            if (!_showSharedConditions)
            {
                var movedEntry = entries[oldIndex];
                int segStart = entries.FindIndex(e => e.owner == movedEntry.owner);
                int count = GetConditions(movedEntry.owner).Length;
                InvalidateConditionCache();
                ReorderConditionOnOwner(movedEntry.owner, movedEntry.index, Mathf.Clamp(newIndex - segStart, 0, count - 1));
                return;
            }

            /* Shared mode: one consistent rank order applied across every owner, permuting only the
               array slots each owner already uses for its shared conditions — its other conditions
               never move — so the relative order among shared entries matches across owners even when
               they have differing numbers/positions of non-shared conditions in between. */
            var rankOrder = Enumerable.Range(0, entries.Count).ToArray();
            MoveArrayElement(rankOrder, oldIndex, newIndex);

            InvalidateConditionCache();
            foreach (var owner in AllSelectedOwners())
            {
                var localForRank = entries.Select(e => e.IndexFor(owner)).ToArray();
                if (localForRank.Any(idx => idx < 0)) continue;
                var slots = localForRank.OrderBy(idx => idx).ToArray();
                var original = GetConditions(owner);
                var reordered = (AnimatorCondition[])original.Clone();
                for (int k = 0; k < slots.Length; k++)
                    reordered[slots[k]] = original[localForRank[rankOrder[k]]];

                Undo.RecordObject(owner, "Reorder Condition");
                SetConditions(owner, reordered);
                EditorUtility.SetDirty(owner);
            }
        }

        static void ReorderConditionOnOwner(UnityEngine.Object owner, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            var conditions = GetConditions(owner);
            if (oldIndex < 0 || oldIndex >= conditions.Length) return;
            Undo.RecordObject(owner, "Reorder Condition");
            MoveArrayElement(conditions, oldIndex, newIndex);
            SetConditions(owner, conditions);
            EditorUtility.SetDirty(owner);
        }

        /* Adds a new condition using the parameter after the last one added (params-list order, wraps at end). */
        void AddConditionToAll()
        {
            InvalidateConditionCache();
            if (_controller == null || _controller.parameters.Length == 0) return;
            var owners = AllSelectedOwners();
            if (owners.Length == 0) return;
            var defaultParam = _controller.parameters[0];
            if (owners.Length == 1 || _showSharedConditions)
            {
                var lastCondition = GetConditions(owners[0]).LastOrDefault();
                var previousIndex = System.Array.FindIndex(_controller.parameters, p => p.name == lastCondition.parameter);
                if (previousIndex >= 0)
                    defaultParam = _controller.parameters[(previousIndex + 1) % _controller.parameters.Length];
            }
            foreach (var owner in owners)
            {
                Undo.RecordObject(owner, "Add Condition");
                AddConditionTo(owner, DefaultModeForType(defaultParam.type), 0f, defaultParam.name);
                EditorUtility.SetDirty(owner);
            }
        }

        /* Inverts every condition mode on all selected owners (If↔IfNot, Greater↔Less, Equals↔NotEqual). */
        void ReverseAllConditions()
        {
            InvalidateConditionCache();
            foreach (var owner in AllSelectedOwners())
            {
                Undo.RecordObject(owner, "Reverse Conditions");
                var allConditions = GetConditions(owner).ToArray();
                foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
                foreach (var condition in allConditions) AddConditionTo(owner, ReverseMode(condition.mode), condition.threshold, condition.parameter);
                EditorUtility.SetDirty(owner);
            }
        }

        /* Returns the logical inverse of a condition mode (If↔IfNot, Equals↔NotEqual, Greater↔Less). */
        static AnimatorConditionMode ReverseMode(AnimatorConditionMode mode) => mode switch
        {
            AnimatorConditionMode.If       => AnimatorConditionMode.IfNot,
            AnimatorConditionMode.IfNot    => AnimatorConditionMode.If,
            AnimatorConditionMode.Equals   => AnimatorConditionMode.NotEqual,
            AnimatorConditionMode.NotEqual => AnimatorConditionMode.Equals,
            AnimatorConditionMode.Greater  => AnimatorConditionMode.Less,
            AnimatorConditionMode.Less     => AnimatorConditionMode.Greater,
            _                             => mode
        };

        /* Returns the first unclaimed index in owner's conditions matching the reference condition, or -1. */
        int FindConditionIndexExcluding(UnityEngine.Object owner, string paramName, AnimatorConditionMode mode, float threshold, HashSet<int> exclude, bool matchName, bool matchMode, bool matchValue)
        {
            var reference = new AnimatorCondition { parameter = paramName, mode = mode, threshold = threshold };
            var conditions = GetConditions(owner);
            for (int i = 0; i < conditions.Length; i++)
            {
                if (exclude.Contains(i)) continue;
                if (!ConditionMatchesCriteria(conditions[i], reference, matchName, matchMode, matchValue)) continue;
                return i;
            }
            return -1;
        }

        /* Bool/Trigger encode value in mode (If/IfNot), not threshold — comparing raw threshold across a bool and an int/float condition is meaningless. */
        static bool ConditionMatchesCriteria(AnimatorCondition condition, AnimatorCondition target, bool matchName, bool matchMode, bool matchValue)
        {
            if (matchName && condition.parameter != target.parameter) return false;
            if (matchMode && condition.mode != target.mode) return false;
            if (matchValue)
            {
                bool conditionIsBoolLike = IsBoolLikeMode(condition.mode);
                bool targetIsBoolLike = IsBoolLikeMode(target.mode);
                if (conditionIsBoolLike != targetIsBoolLike) return false;
                if (conditionIsBoolLike ? condition.mode != target.mode : condition.threshold != target.threshold) return false;
            }
            return true;
        }

        static bool IsBoolLikeMode(AnimatorConditionMode mode) => mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot;

        /* Clears and re-adds all conditions on the owner, substituting replacement at replaceIdx. */
        static void RebuildConditions(UnityEngine.Object owner, int replaceIdx, AnimatorCondition replacement)
        {
            Undo.RecordObject(owner, "Edit Condition");
            var allConditions = GetConditions(owner).ToArray();
            foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
            for (int i = 0; i < allConditions.Length; i++)
            {
                var condition = i == replaceIdx ? replacement : allConditions[i];
                AddConditionTo(owner, condition.mode, condition.threshold, condition.parameter);
            }
            EditorUtility.SetDirty(owner);
        }

        /* ── Merge / Separate ────────────────────────────────────────────── */

        /* Groups selected transitions by src+dst key, then collapses each group into its first transition by appending all other transitions' conditions onto it and deleting the rest. */
        void MergeTransitions()
        {
            if (_selectedTransitions.Length < 2 || _controller == null) return;
            var transitions = _selectedTransitions.ToArray();
            var controller = _controller;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            Undo.RegisterCompleteObjectUndo(controller, "Merge Transitions");

            var groups = new Dictionary<(string src, string dst), List<AnimatorStateTransition>>();
            foreach (var transition in transitions)
            {
                var key = (GetSrcKey(controller, transition), GetDstKey(transition));
                if (!groups.ContainsKey(key)) groups[key] = new List<AnimatorStateTransition>();
                groups[key].Add(transition);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                var primary = group[0];
                var ownerStateMachine = FindOwnerSM(controller, primary);
                if (ownerStateMachine == null) continue;

                Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Merge Transitions");
                Undo.RecordObject(primary, "Merge Transitions");
                foreach (var transition in group.Skip(1))
                {
                    foreach (var childState in ownerStateMachine.states)
                        if (childState.state.transitions.Contains(transition))
                        {
                            Undo.RegisterCompleteObjectUndo(childState.state, "Merge Transitions");
                            break;
                        }
                }

                foreach (var transition in group.Skip(1))
                {
                    foreach (var condition in transition.conditions)
                        AddConditionTo(primary, condition.mode, condition.threshold, condition.parameter);
                    DeleteTransition(ownerStateMachine, transition);
                }
                EditorUtility.SetDirty(primary);
                EditorUtility.SetDirty(ownerStateMachine);
            }

            EditorUtility.SetDirty(controller);
            InvalidateConditionCache();
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        /* Splits each selected transition that has multiple conditions into one transition per condition, copying all non-condition settings to each new transition. */
        void SeparateTransitions()
        {
            if (_selectedTransitions.Length == 0 || _controller == null) return;
            var transitions = _selectedTransitions.ToArray();
            var controller = _controller;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            Undo.RegisterCompleteObjectUndo(controller, "Separate Transitions");

            foreach (var transition in transitions)
            {
                var conditions = transition.conditions.ToArray();
                if (conditions.Length <= 1) continue;

                var ownerStateMachine = FindOwnerSM(controller, transition);
                if (ownerStateMachine == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Separate Transitions");

                bool isAnyState = ownerStateMachine.anyStateTransitions.Contains(transition);
                AnimatorState sourceState = isAnyState ? null
                    : ownerStateMachine.states.FirstOrDefault(x => x.state.transitions.Contains(transition)).state;

                for (int i = 1; i < conditions.Length; i++)
                {
                    var newTransition = CreateMatchingTransition(ownerStateMachine, sourceState, isAnyState, transition);
                    if (newTransition == null) continue;
                    Undo.RegisterCreatedObjectUndo(newTransition, "Separate Transitions");
                    CopyTransitionSettings(transition, newTransition);
                    foreach (var condition in newTransition.conditions.ToArray()) newTransition.RemoveCondition(condition);
                    newTransition.AddCondition(conditions[i].mode, conditions[i].threshold, conditions[i].parameter);
                    EditorUtility.SetDirty(newTransition);
                }

                Undo.RecordObject(transition, "Separate Transitions");
                foreach (var condition in conditions.Skip(1)) transition.RemoveCondition(condition);
                EditorUtility.SetDirty(transition);
                EditorUtility.SetDirty(ownerStateMachine);
            }

            EditorUtility.SetDirty(controller);
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        void MergeEntryTransitions()
        {
            if (_selectedEntryTransitions.Length < 2 || _controller == null) return;
            var transitions = _selectedEntryTransitions.Where(t => t != null).ToArray();
            Undo.RegisterCompleteObjectUndo(_controller, "Merge Transitions");

            var groups = new Dictionary<string, List<AnimatorTransition>>();
            foreach (var transition in transitions)
            {
                string key = transition.destinationState != null ? transition.destinationState.GetInstanceID().ToString()
                    : transition.destinationStateMachine != null ? transition.destinationStateMachine.GetInstanceID().ToString()
                    : "?";
                if (!groups.ContainsKey(key)) groups[key] = new List<AnimatorTransition>();
                groups[key].Add(transition);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                var ownerSM = FindEntryOwnerSM(_controller, group[0]);
                if (ownerSM == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerSM, "Merge Transitions");
                Undo.RecordObject(group[0], "Merge Transitions");
                foreach (var transition in group.Skip(1))
                {
                    foreach (var condition in transition.conditions)
                        AddConditionTo(group[0], condition.mode, condition.threshold, condition.parameter);
                    ownerSM.RemoveEntryTransition(transition);
                }
                EditorUtility.SetDirty(group[0]);
                EditorUtility.SetDirty(ownerSM);
            }

            EditorUtility.SetDirty(_controller);
            InvalidateConditionCache();
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        void SeparateEntryTransitions()
        {
            if (_selectedEntryTransitions.Length == 0 || _controller == null) return;
            var transitions = _selectedEntryTransitions.Where(t => t != null).ToArray();
            Undo.RegisterCompleteObjectUndo(_controller, "Separate Transitions");

            foreach (var transition in transitions)
            {
                var conditions = transition.conditions.ToArray();
                if (conditions.Length <= 1) continue;
                var ownerSM = FindEntryOwnerSM(_controller, transition);
                if (ownerSM == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerSM, "Separate Transitions");

                for (int i = 1; i < conditions.Length; i++)
                {
                    AnimatorTransition newTransition = transition.destinationState != null
                        ? ownerSM.AddEntryTransition(transition.destinationState)
                        : transition.destinationStateMachine != null
                            ? ownerSM.AddEntryTransition(transition.destinationStateMachine)
                            : null;
                    if (newTransition == null) continue;
                    Undo.RegisterCreatedObjectUndo(newTransition, "Separate Transitions");
                    newTransition.mute = transition.mute;
                    newTransition.solo = transition.solo;
                    newTransition.AddCondition(conditions[i].mode, conditions[i].threshold, conditions[i].parameter);
                    EditorUtility.SetDirty(newTransition);
                }

                Undo.RecordObject(transition, "Separate Transitions");
                foreach (var condition in conditions.Skip(1)) transition.RemoveCondition(condition);
                EditorUtility.SetDirty(transition);
                EditorUtility.SetDirty(ownerSM);
            }

            EditorUtility.SetDirty(_controller);
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        /* Creates a new transition in sm with the same source/destination topology as original (anyState, exit, state, or SM). */
        static AnimatorStateTransition CreateMatchingTransition(AnimatorStateMachine sm, AnimatorState srcState, bool isAnyState, AnimatorStateTransition original)
        {
            if (isAnyState)
            {
                if (original.destinationState != null) return sm.AddAnyStateTransition(original.destinationState);
                if (original.destinationStateMachine != null) return sm.AddAnyStateTransition(original.destinationStateMachine);
                return null;
            }
            if (srcState == null) return null;
            if (original.isExit) return srcState.AddExitTransition();
            if (original.destinationState != null) return srcState.AddTransition(original.destinationState);
            if (original.destinationStateMachine != null) return srcState.AddTransition(original.destinationStateMachine);
            return null;
        }

        /* Copies all timing, interruption, and flag settings from sourceTransition to destinationTransition (no conditions). */
        static void CopyTransitionSettings(AnimatorStateTransition sourceTransition, AnimatorStateTransition destinationTransition)
        {
            destinationTransition.hasExitTime = sourceTransition.hasExitTime;
            destinationTransition.exitTime = sourceTransition.exitTime;
            destinationTransition.hasFixedDuration = sourceTransition.hasFixedDuration;
            destinationTransition.duration = sourceTransition.duration;
            destinationTransition.offset = sourceTransition.offset;
            destinationTransition.interruptionSource = sourceTransition.interruptionSource;
            destinationTransition.orderedInterruption = sourceTransition.orderedInterruption;
            destinationTransition.mute = sourceTransition.mute;
            destinationTransition.solo = sourceTransition.solo;
            destinationTransition.canTransitionToSelf = sourceTransition.canTransitionToSelf;
        }

        /* Returns the SM that directly contains the transition (as anyState or as a state's transition), searching all layers. */
        static AnimatorStateMachine FindOwnerSM(AnimatorController controller, AnimatorStateTransition transition)
        {
            foreach (var layer in controller.layers)
            {
                var found = WalkSM(layer.stateMachine, sm =>
                    sm.anyStateTransitions.Contains(transition) || sm.states.Any(childState => childState.state.transitions.Contains(transition)) ? sm : null);
                if (found != null) return found;
            }
            return null;
        }

        static AnimatorStateMachine FindEntryOwnerSM(AnimatorController controller, AnimatorTransition transition)
        {
            foreach (var layer in controller.layers)
            {
                var found = WalkSM(layer.stateMachine, sm => sm.entryTransitions.Contains(transition) ? sm : null);
                if (found != null) return found;
            }
            return null;
        }

        /* Removes a transition from sm's anyState list or from the source state that owns it. */
        static void DeleteTransition(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            if (sm.anyStateTransitions.Contains(transition))
            {
                sm.RemoveAnyStateTransition(transition);
                return;
            }
            foreach (var childState in sm.states)
            {
                if (childState.state.transitions.Contains(transition))
                {
                    childState.state.RemoveTransition(transition);
                    return;
                }
            }
        }

        /* Returns a stable string key identifying the transition's source (anystate or state instance ID), used to group transitions for merge. */
        static string GetSrcKey(AnimatorController controller, AnimatorStateTransition transition)
        {
            if (controller == null) return "?";
            var ownerSM = FindOwnerSM(controller, transition);
            if (ownerSM == null) return "?";
            if (ownerSM.anyStateTransitions.Contains(transition)) return "anystate";
            foreach (var childState in ownerSM.states)
                if (childState.state.transitions.Contains(transition)) return childState.state.GetInstanceID().ToString();
            return "?";
        }

        string GetDstKey(AnimatorStateTransition transition)
        {
            if (transition.isExit) return "exit";
            if (transition.destinationState != null) return transition.destinationState.GetInstanceID().ToString();
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine.GetInstanceID().ToString();
            return "?";
        }

        /* ── Utility ─────────────────────────────────────────────────────── */

        /* True if transition originates from an AnyState node. */
        bool IsAnyStateTransition(AnimatorStateTransition transition)
        {
            var ownerSM = FindOwnerSM(_controller, transition);
            return ownerSM != null && ownerSM.anyStateTransitions.Contains(transition);
        }

        /* Applies mutate to every selected state transition with undo recording, then marks each dirty. */
        void SetOnAll(Action<AnimatorStateTransition> mutate)
        {
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Edit Transition");
                mutate(transition);
                EditorUtility.SetDirty(transition);
            }
        }

        /* Returns all selected owners (state transitions + entry transitions) as a combined UnityEngine.Object array. */
        UnityEngine.Object[] AllSelectedOwners() =>
            _selectedTransitions.Where(t => t != null).Cast<UnityEngine.Object>()
            .Concat(_selectedEntryTransitions.Where(t => t != null))
            .ToArray();

        /* Returns the conditions array for any transition type. */
        static AnimatorCondition[] GetConditions(UnityEngine.Object obj) =>
            obj is AnimatorStateTransition stateTrans ? stateTrans.conditions :
            obj is AnimatorTransition entryTrans      ? entryTrans.conditions :
            Array.Empty<AnimatorCondition>();

        /* Adds a condition to any transition type. */
        static void AddConditionTo(UnityEngine.Object obj, AnimatorConditionMode mode, float threshold, string param)
        {
            if (obj is AnimatorStateTransition stateTrans) stateTrans.AddCondition(mode, threshold, param);
            else if (obj is AnimatorTransition entryTrans) entryTrans.AddCondition(mode, threshold, param);
        }

        /* Removes a condition from any transition type. */
        static void RemoveConditionFrom(UnityEngine.Object obj, AnimatorCondition condition)
        {
            if (obj is AnimatorStateTransition stateTrans) stateTrans.RemoveCondition(condition);
            else if (obj is AnimatorTransition entryTrans) entryTrans.RemoveCondition(condition);
        }

        /* Overwrites the conditions array wholesale for any transition type — used for drag-reorder. */
        static void SetConditions(UnityEngine.Object obj, AnimatorCondition[] conditions)
        {
            if (obj is AnimatorStateTransition stateTrans) stateTrans.conditions = conditions;
            else if (obj is AnimatorTransition entryTrans) entryTrans.conditions = conditions;
        }
    }
}
#endif
