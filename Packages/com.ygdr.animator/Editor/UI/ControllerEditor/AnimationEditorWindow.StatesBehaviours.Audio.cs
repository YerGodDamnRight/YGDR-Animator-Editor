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
        // ── VRC Play Audio section (native, multi-instance) ─────────────────────

        readonly Dictionary<string, bool> _audioFoldoutExpanded = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> _audioClipsExpandedByKey = new Dictionary<string, bool>();

        Button _audioRemoveButton;
        VisualElement _audioRows;
        VisualElement _audioSection;

        VisualElement BuildAudioBody()
        {
            _audioSection = BuildBehaviorSectionShell(L10n.Get("vrc.audio"), out _audioRemoveButton, out _audioRows);
            _audioRemoveButton.clicked += () =>
            {
                RemoveAudioFromAll();
                RefreshAudioSection();
            };
            return _audioSection;
        }

        void RefreshAudioBody() => RefreshAudioSection();

        /* Entry point for the top-level Add Behavior dropdown — always available since audio behaviors allow duplicates. */
        void AddAudioBehaviorToSelected()
        {
            foreach (var state in _selectedStates) AddInstance<VRCAnimatorPlayAudio>(state, "Play Audio");
            RefreshAudioSection();
        }

        void RefreshAudioSection()
        {
            if (_audioRows == null) return;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAnimatorPlayAudio>(state));
            _audioSection.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _audioRemoveButton.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _audioRows.Clear();
            if (maxCount == 0) return;

            var groups = GroupInstancesByName<VRCAnimatorPlayAudio>(_selectedStates);
            for (int i = 0; i < groups.Count; i++)
                _audioRows.Add(BuildAudioFoldout(groups[i].name, groups[i].states, i == 0, i == groups.Count - 1));
        }

        VisualElement BuildAudioFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-behavior-instance-container");

            var body = BuildAudioInstanceBody(name, statesWithName, state => FindInstance<VRCAnimatorPlayAudio>(state, name));
            body.style.display = IsExpandedByDefault(_audioFoldoutExpanded, name) ? DisplayStyle.Flex : DisplayStyle.None;

            var header = BuildInstanceFoldoutHeader<VRCAnimatorPlayAudio>(name, statesWithName, _audioFoldoutExpanded,
                isFirst, isLast, out _, expandedNow => body.style.display = expandedNow ? DisplayStyle.Flex : DisplayStyle.None,
                RefreshAudioSection);

            container.Add(header);
            container.Add(body);
            return container;
        }

        VisualElement BuildAudioInstanceBody(string name, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver)
        {
            var body = new VisualElement();
            body.AddToClassList("ygdr-behavior-instance-body");
            body.style.backgroundColor = SharedWindowStyles.SecondaryColor;

            var statesWithAudio = statesWithName.Where(state => resolver(state) != null).ToArray();
            if (statesWithAudio.Length == 0) return body;
            var first = resolver(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;

            body.Add(BuildAudioSourceDragField(statesWithName, resolver));
            body.Add(BuildAudioSourcePathField(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioPlaybackOrderRow(first, statesWithAudio, statesWithName, resolver, multi));
            if (first.PlaybackOrder == VRCAnimatorPlayAudio.Order.Parameter)
                body.Add(BuildAudioParameterNameField(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioClipsSection(name, statesWithName, resolver));
            body.Add(BuildAudioVolumeRow(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioPitchRow(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioLoopRow(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioPlayStopColumnHeaders());
            body.Add(BuildAudioOnEnterRow(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioOnExitRow(first, statesWithAudio, statesWithName, resolver, multi));
            body.Add(BuildAudioDelayRow(first, statesWithAudio, statesWithName, resolver, multi));

            return body;
        }

        static void SetAudioOnAll(AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, string undoName, Action<VRCAnimatorPlayAudio> mutate)
        {
            foreach (var state in statesWithName)
            {
                var audio = GetOrCreateAudio(state, resolver);
                Undo.RecordObject(audio, undoName);
                mutate(audio);
                EditorUtility.SetDirty(audio);
            }
        }

        VisualElement BuildAudioSourceDragField(AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver)
        {
            var dragField = new ObjectField { objectType = typeof(AudioSource), allowSceneObjects = true };
            dragField.RegisterValueChangedCallback(evt =>
            {
                var droppedSource = evt.newValue as AudioSource;
                dragField.SetValueWithoutNotify(null);
                if (droppedSource == null) return;
                var descriptor = droppedSource.GetComponentInParent<VRCAvatarDescriptor>();
                string resolvedPath = GetAudioSourcePath(droppedSource.transform, descriptor != null ? descriptor.transform : null);
                SetAudioOnAll(statesWithName, resolver, "Set Source Path", audio => audio.SourcePath = resolvedPath);
                RefreshAudioSection();
            });
            return BuildBehaviorFieldRow(L10n.Get("vrc.audio.source"), null, dragField);
        }

        static VisualElement BuildAudioSourcePathField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            var field = new TextField { value = first.SourcePath ?? "", showMixedValue = multi && statesWithAudio.Any(state => resolver(state).SourcePath != first.SourcePath) };
            field.RegisterValueChangedCallback(evt =>
                SetAudioOnAll(statesWithName, resolver, "Edit Source Path", audio => audio.SourcePath = evt.newValue));
            return BuildBehaviorFieldRow(L10n.Get("vrc.audio.source_path"), null, field);
        }

        VisualElement BuildAudioPlaybackOrderRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var label = new Label(L10n.Get("vrc.audio.playback_order"));
            label.AddToClassList("ygdr-behavior-field-label");
            row.Add(label);

            var orderLabels = new[] { L10n.Get("vrc.audio.order.random"), L10n.Get("vrc.audio.order.unique"), L10n.Get("vrc.audio.order.roundabout"), L10n.Get("vrc.audio.order.parameter") };
            bool orderMixed = multi && statesWithAudio.Any(state => resolver(state).PlaybackOrder != first.PlaybackOrder);
            var orderButton = BuildLocalizedIndexDropdown((int)first.PlaybackOrder, orderMixed, orderLabels, newIndex =>
            {
                var newOrder = (VRCAnimatorPlayAudio.Order)newIndex;
                SetAudioOnAll(statesWithName, resolver, "Edit Playback Order", audio => audio.PlaybackOrder = newOrder);
                RefreshAudioSection();
            });
            orderButton.AddToClassList("ygdr-behavior-field-value");
            orderButton.AddToClassList("u-flex-fill");
            row.Add(orderButton);

            bool applyMixed = multi && statesWithAudio.Any(state => resolver(state).ClipsApplySettings != first.ClipsApplySettings);
            row.Add(BuildApplySettingsDropdown(first.ClipsApplySettings, applyMixed, newValue =>
                SetAudioOnAll(statesWithName, resolver, "Edit Clips Apply Settings", audio => audio.ClipsApplySettings = newValue)));

            return row;
        }

        VisualElement BuildAudioParameterNameField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            bool mixed = multi && statesWithAudio.Any(state => resolver(state).ParameterName != first.ParameterName);
            var button = new Button { text = mixed ? "—" : (string.IsNullOrEmpty(first.ParameterName) ? "[None]" : first.ParameterName) };
            StyleAccentButton(button);
            button.clicked += () =>
            {
                if (_controller == null || _controller.parameters.Length == 0) return;
                ShowParameterDropdown(button.worldBound, first.ParameterName ?? "", AnimatorControllerParameterType.Int, selectedName =>
                {
                    SetAudioOnAll(statesWithName, resolver, "Edit Parameter Name", audio => audio.ParameterName = selectedName);
                    button.text = selectedName;
                });
            };
            return BuildBehaviorFieldRow(L10n.Get("vrc.audio.param_name"), null, button);
        }

        static VisualElement BuildAudioVolumeRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            return BuildAudioMinMaxApplyRow(L10n.Get("vrc.audio.volume"), 0f, 1f,
                audio => audio.Volume, (audio, v) => audio.Volume = v,
                audio => audio.VolumeApplySettings, (audio, v) => audio.VolumeApplySettings = v,
                first, statesWithAudio, statesWithName, resolver, multi, "Edit Volume Min", "Edit Volume Max", "Edit Volume Apply Settings");
        }

        static VisualElement BuildAudioPitchRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            return BuildAudioMinMaxApplyRow(L10n.Get("vrc.audio.pitch"), -3f, 3f,
                audio => audio.Pitch, (audio, v) => audio.Pitch = v,
                audio => audio.PitchApplySettings, (audio, v) => audio.PitchApplySettings = v,
                first, statesWithAudio, statesWithName, resolver, multi, "Edit Pitch Min", "Edit Pitch Max", "Edit Pitch Apply Settings");
        }

        static Button BuildApplySettingsDropdown(VRC_AnimatorPlayAudio.ApplySettings current, bool mixed, Action<VRC_AnimatorPlayAudio.ApplySettings> onChanged)
        {
            var labels = new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") };
            var button = BuildLocalizedIndexDropdown((int)current, mixed, labels, newIndex => onChanged((VRC_AnimatorPlayAudio.ApplySettings)newIndex));
            button.AddToClassList("ygdr-audio-apply-btn");
            return button;
        }

        static VisualElement BuildAudioMinMaxApplyRow(string label, float min, float max,
            Func<VRCAnimatorPlayAudio, Vector2> getValue, Action<VRCAnimatorPlayAudio, Vector2> setValue,
            Func<VRCAnimatorPlayAudio, VRC_AnimatorPlayAudio.ApplySettings> getApply, Action<VRCAnimatorPlayAudio, VRC_AnimatorPlayAudio.ApplySettings> setApply,
            VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi,
            string undoMin, string undoMax, string undoApply)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-behavior-field-label");
            row.Add(labelElement);

            var firstValue = getValue(first);
            var minField = new FloatField { value = firstValue.x, showMixedValue = multi && statesWithAudio.Any(state => !Mathf.Approximately(getValue(resolver(state)).x, firstValue.x)) };
            minField.AddToClassList("ygdr-audio-minmax-field");
            row.Add(minField);
            var maxField = new FloatField { value = firstValue.y, showMixedValue = multi && statesWithAudio.Any(state => !Mathf.Approximately(getValue(resolver(state)).y, firstValue.y)) };
            maxField.AddToClassList("ygdr-audio-minmax-field");
            row.Add(maxField);

            minField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                minField.SetValueWithoutNotify(clamped);
                SetAudioOnAll(statesWithName, resolver, undoMin, audio => setValue(audio, new Vector2(clamped, getValue(audio).y)));
            });
            maxField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                maxField.SetValueWithoutNotify(clamped);
                SetAudioOnAll(statesWithName, resolver, undoMax, audio => setValue(audio, new Vector2(getValue(audio).x, clamped)));
            });

            bool applyMixed = multi && statesWithAudio.Any(state => getApply(resolver(state)) != getApply(first));
            row.Add(BuildApplySettingsDropdown(getApply(first), applyMixed, newValue =>
                SetAudioOnAll(statesWithName, resolver, undoApply, audio => setApply(audio, newValue))));

            return row;
        }

        static VisualElement BuildAudioLoopRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var label = new Label(L10n.Get("vrc.audio.loop"));
            label.AddToClassList("ygdr-behavior-field-label");
            row.Add(label);

            var loopToggle = new Toggle { value = first.Loop, showMixedValue = multi && statesWithAudio.Any(state => resolver(state).Loop != first.Loop) };
            loopToggle.RegisterValueChangedCallback(evt =>
                SetAudioOnAll(statesWithName, resolver, "Edit Loop", audio => audio.Loop = evt.newValue));
            row.Add(loopToggle);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);

            bool applyMixed = multi && statesWithAudio.Any(state => resolver(state).LoopApplySettings != first.LoopApplySettings);
            row.Add(BuildApplySettingsDropdown(first.LoopApplySettings, applyMixed, newValue =>
                SetAudioOnAll(statesWithName, resolver, "Edit Loop Apply Settings", audio => audio.LoopApplySettings = newValue)));

            return row;
        }

        static VisualElement BuildAudioPlayStopColumnHeaders()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var spacer = new VisualElement();
            spacer.AddToClassList("ygdr-behavior-field-label");
            row.Add(spacer);
            var stopLabel = new Label(L10n.Get("vrc.audio.stop"));
            stopLabel.AddToClassList("ygdr-audio-col-label");
            row.Add(stopLabel);
            var playLabel = new Label(L10n.Get("vrc.audio.play"));
            playLabel.AddToClassList("ygdr-audio-col-label");
            row.Add(playLabel);
            return row;
        }

        static VisualElement BuildAudioOnEnterRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
            => BuildAudioStopPlayRow(L10n.Get("vrc.audio.on_enter"), first.StopOnEnter, first.PlayOnEnter,
                (audio, v) => audio.StopOnEnter = v, (audio, v) => audio.PlayOnEnter = v,
                audio => audio.StopOnEnter, audio => audio.PlayOnEnter,
                statesWithAudio, statesWithName, resolver, multi, "Edit Stop On Enter", "Edit Play On Enter");

        static VisualElement BuildAudioOnExitRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
            => BuildAudioStopPlayRow(L10n.Get("vrc.audio.on_exit"), first.StopOnExit, first.PlayOnExit,
                (audio, v) => audio.StopOnExit = v, (audio, v) => audio.PlayOnExit = v,
                audio => audio.StopOnExit, audio => audio.PlayOnExit,
                statesWithAudio, statesWithName, resolver, multi, "Edit Stop On Exit", "Edit Play On Exit");

        static VisualElement BuildAudioStopPlayRow(string label, bool firstStop, bool firstPlay,
            Action<VRCAnimatorPlayAudio, bool> setStop, Action<VRCAnimatorPlayAudio, bool> setPlay,
            Func<VRCAnimatorPlayAudio, bool> getStop, Func<VRCAnimatorPlayAudio, bool> getPlay,
            AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi,
            string undoStop, string undoPlay)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-behavior-field-label");
            row.Add(labelElement);

            var stopToggle = new Toggle { value = firstStop, showMixedValue = multi && statesWithAudio.Any(state => getStop(resolver(state)) != firstStop) };
            stopToggle.AddToClassList("ygdr-audio-col-toggle");
            stopToggle.RegisterValueChangedCallback(evt =>
                SetAudioOnAll(statesWithName, resolver, undoStop, audio => setStop(audio, evt.newValue)));
            row.Add(stopToggle);

            var playToggle = new Toggle { value = firstPlay, showMixedValue = multi && statesWithAudio.Any(state => getPlay(resolver(state)) != firstPlay) };
            playToggle.AddToClassList("ygdr-audio-col-toggle");
            playToggle.RegisterValueChangedCallback(evt =>
                SetAudioOnAll(statesWithName, resolver, undoPlay, audio => setPlay(audio, evt.newValue)));
            row.Add(playToggle);

            return row;
        }

        static VisualElement BuildAudioDelayRow(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, bool multi)
        {
            var field = new FloatField { value = first.DelayInSeconds, showMixedValue = multi && statesWithAudio.Any(state => !Mathf.Approximately(resolver(state).DelayInSeconds, first.DelayInSeconds)) };
            field.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, 0f, 60f);
                field.SetValueWithoutNotify(clamped);
                SetAudioOnAll(statesWithName, resolver, "Edit Play Delay", audio => audio.DelayInSeconds = clamped);
            });
            var row = BuildBehaviorFieldRow(L10n.Get("vrc.audio.delay"), null, field);
            row.AddToClassList("ygdr-audio-delay-row");
            return row;
        }

        // ── Clips list (native ListView with built-in drag reorder, mirrors the old IMGUI ReorderableList) ──

        VisualElement BuildAudioClipsSection(string name, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver)
        {
            var section = new VisualElement();
            section.AddToClassList("ygdr-audio-clips-section");
            section.style.backgroundColor = SharedWindowStyles.SecondaryColor;

            string expandKey = "clips:" + name;
            bool expanded = IsExpandedByDefault(_audioClipsExpandedByKey, expandKey);

            var headerRow = new VisualElement();
            headerRow.AddToClassList("ygdr-audio-clips-header");

            var foldoutArrow = new Label(expanded ? "▾" : "▸");
            foldoutArrow.AddToClassList("ygdr-behavior-foldout-arrow");
            headerRow.Add(foldoutArrow);

            var titleLabel = new Label(L10n.Get("vrc.audio.clips"));
            titleLabel.AddToClassList("ygdr-audio-clips-title");
            headerRow.Add(titleLabel);

            var statesWithAudio = statesWithName.Where(state => resolver(state) != null).ToArray();
            var first = resolver(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;
            var clips = first.Clips ?? Array.Empty<AudioClip>();

            var sizeField = new IntegerField { value = clips.Length, showMixedValue = multi && statesWithAudio.Any(state => (resolver(state).Clips?.Length ?? 0) != clips.Length) };
            sizeField.AddToClassList("ygdr-audio-clips-size-field");
            headerRow.Add(sizeField);
            section.Add(headerRow);

            var clipsListView = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = false,
                showAddRemoveFooter = false,
                selectionType = SelectionType.None,
                fixedItemHeight = 20,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = () => new VisualElement()
            };
            clipsListView.AddToClassList("ygdr-audio-clips-rows");
            clipsListView.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            section.Add(clipsListView);

            var clipsEmptyLabel = new Label(L10n.Get("vrc.list_empty"));
            clipsEmptyLabel.AddToClassList("ygdr-empty-label");
            clipsEmptyLabel.style.display = DisplayStyle.None;
            section.Add(clipsEmptyLabel);

            var addRow = new VisualElement();
            addRow.AddToClassList("ygdr-audio-clip-add-row");
            section.Add(addRow);

            /* itemsSource is a throwaway index list (see RebuildMenuControlRows in Menus.cs for why) — the real
               data is the per-state Clips array, moved/edited via bindItem's closures reading `index` fresh each call. */
            void RebuildRows()
            {
                var currentStatesWithAudio = statesWithName.Where(state => resolver(state) != null).ToArray();
                var currentClips = currentStatesWithAudio.Length > 0
                    ? resolver(currentStatesWithAudio[0]).Clips ?? Array.Empty<AudioClip>()
                    : Array.Empty<AudioClip>();

                clipsEmptyLabel.style.display = currentClips.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;

                var indices = new List<int>(currentClips.Length);
                for (int i = 0; i < currentClips.Length; i++) indices.Add(i);
                clipsListView.itemsSource = indices;
                clipsListView.bindItem = (element, index) => BindAudioClipRow(element, statesWithName, resolver, currentStatesWithAudio, currentClips, index, RebuildRows);
                clipsListView.Rebuild();
            }

            WireListViewReorder(clipsListView, (oldIndex, newIndex) => MoveAudioClipToIndex(statesWithName, resolver, oldIndex, newIndex), RebuildRows);

            var addButton = new Button(() =>
            {
                foreach (var state in statesWithName)
                {
                    var audio = GetOrCreateAudio(state, resolver);
                    Undo.RecordObject(audio, "Add Audio Clip");
                    Array.Resize(ref audio.Clips, (audio.Clips?.Length ?? 0) + 1);
                    EditorUtility.SetDirty(audio);
                }
                RebuildRows();
            }) { text = "+" };
            addButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(addButton);
            addRow.Add(addButton);

            RebuildRows();

            foldoutArrow.RegisterCallback<ClickEvent>(_ =>
            {
                bool nowExpanded = clipsListView.style.display == DisplayStyle.None;
                _audioClipsExpandedByKey[expandKey] = nowExpanded;
                foldoutArrow.text = nowExpanded ? "▾" : "▸";
                clipsListView.style.display = nowExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            });

            sizeField.RegisterValueChangedCallback(evt =>
            {
                int newSize = Mathf.Max(0, evt.newValue);
                foreach (var state in statesWithName)
                {
                    var audio = GetOrCreateAudio(state, resolver);
                    Undo.RecordObject(audio, "Resize Clips");
                    Array.Resize(ref audio.Clips, newSize);
                    EditorUtility.SetDirty(audio);
                }
                RebuildRows();
            });

            return section;
        }

        /* statesWithAudio/clips come pre-resolved from RebuildRows's own pass over statesWithName — resolving them
           again per row (there can be many) would repeat the same filter/lookup for every visible clip. */
        static void BindAudioClipRow(VisualElement element, AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver,
            AnimatorState[] statesWithAudio, AudioClip[] clips, int index, Action refresh)
        {
            element.Clear();
            element.ClearClassList();
            element.AddToClassList("ygdr-audio-clip-row");
            StyleHoverTint(element, () => false, () => SecondaryButtonHoverColor, () => new StyleColor(StyleKeyword.Null));

            if (statesWithAudio.Length == 0 || index >= clips.Length) return;
            bool multi = statesWithAudio.Length > 1;
            var currentClip = clips[index];

            var clipField = new ObjectField
            {
                objectType = typeof(AudioClip),
                value = currentClip,
                showMixedValue = multi && statesWithAudio.Any(state =>
                {
                    var stateClips = resolver(state).Clips;
                    var clip = stateClips != null && index < stateClips.Length ? stateClips[index] : null;
                    return clip != currentClip;
                })
            };
            clipField.AddToClassList("ygdr-audio-clip-field");
            clipField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in statesWithName)
                {
                    var audio = GetOrCreateAudio(state, resolver);
                    if (audio.Clips == null || index >= audio.Clips.Length)
                        Array.Resize(ref audio.Clips, index + 1);
                    Undo.RecordObject(audio, "Edit Audio Clip");
                    audio.Clips[index] = evt.newValue as AudioClip;
                    EditorUtility.SetDirty(audio);
                }
            });
            element.Add(clipField);

            var removeButton = new Button(() => { RemoveAudioClip(statesWithName, resolver, index); refresh(); }) { text = "−" };
            removeButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(removeButton);
            element.Add(removeButton);
        }

        static void MoveAudioClipToIndex(AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, int oldIndex, int newIndex)
        {
            foreach (var state in statesWithName)
            {
                var audio = GetOrCreateAudio(state, resolver);
                if (audio.Clips == null || oldIndex >= audio.Clips.Length || newIndex >= audio.Clips.Length) continue;
                Undo.RecordObject(audio, "Reorder Clips");
                MoveArrayElement(audio.Clips, oldIndex, newIndex);
                EditorUtility.SetDirty(audio);
            }
        }

        static void RemoveAudioClip(AnimatorState[] statesWithName, Func<AnimatorState, VRCAnimatorPlayAudio> resolver, int index)
        {
            foreach (var state in statesWithName)
            {
                var audio = GetOrCreateAudio(state, resolver);
                if (audio.Clips == null || index >= audio.Clips.Length) continue;
                Undo.RecordObject(audio, "Remove Audio Clip");
                audio.Clips = audio.Clips.Where((_, idx) => idx != index).ToArray();
                EditorUtility.SetDirty(audio);
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        static VRCAnimatorPlayAudio GetOrCreateAudio(AnimatorState state, Func<AnimatorState, VRCAnimatorPlayAudio> resolver)
        {
            var resolved = resolver(state);
            if (resolved != null) return resolved;
            var existing = InstanceAt<VRCAnimatorPlayAudio>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCAnimatorPlayAudio>(state, "Play Audio");
        }

        void RemoveAudioFromAll()
        {
            RemoveAllInstancesOfType<VRCAnimatorPlayAudio>(_selectedStates, "Remove VRC Play Audio");
            _audioClipsExpandedByKey.Clear();
            _audioFoldoutExpanded.Clear();
        }

        /* Builds a forward-slash path from sourceTransform up to root (exclusive). Returns "/name" prefixed with slash when root is null, indicating no avatar descriptor was found. */
        static string GetAudioSourcePath(Transform sourceTransform, Transform root)
        {
            string path = sourceTransform.name;
            for (Transform parentTransform = sourceTransform.parent; parentTransform != null && parentTransform != root; parentTransform = parentTransform.parent)
                path = parentTransform.name + "/" + path;
            return root == null ? "/" + path : path;
        }
    }
}
#endif
