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
        // ── VRC Play Audio section (multi-instance) ─────────────────────────────

        readonly Dictionary<string, bool> _clipsExpandedByKey = new Dictionary<string, bool>();
        readonly Dictionary<string, ReorderableList> _clipsReorderListByKey = new Dictionary<string, ReorderableList>();
        readonly Dictionary<string, List<AudioClip>> _clipsListDataByKey = new Dictionary<string, List<AudioClip>>();
        readonly Dictionary<string, bool> _audioFoldoutExpanded = new Dictionary<string, bool>();
        (string key, int index) _pendingRemoveClip = (null, -1);
        string _currentAudioBodyKey;

        AnimatorState[] _activeAudioStates;
        Func<AnimatorState, VRCAnimatorPlayAudio> _activeAudioResolver;

        void DrawVRCPlayAudioSection()
        {
            _activeAudioResolver = null;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAnimatorPlayAudio>(state));

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.audio"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    foreach (var state in _selectedStates) AddInstance<VRCAnimatorPlayAudio>(state, "Play Audio");
                    return; // maxCount below is stale after this mutation — redraw fresh next repaint.
                }
                if (maxCount > 0 && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemoveAudioFromAll();
                    return;
                }
            }

            if (maxCount == 0) return;

            var audioGroups = GroupInstancesByName<VRCAnimatorPlayAudio>(_selectedStates);
            for (int i = 0; i < audioGroups.Count; i++)
                DrawAudioFoldout(audioGroups[i].name, audioGroups[i].states, i == 0, i == audioGroups.Count - 1);

            _activeAudioResolver = null;
        }

        void DrawAudioFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            bool removeRequested = DrawInstanceFoldoutHeader<VRCAnimatorPlayAudio>(name, statesWithName, _audioFoldoutExpanded, isFirst, isLast, out bool expanded, out bool moveUp, out bool moveDown);

            if (moveUp || moveDown)
            {
                MoveNamedInstance<VRCAnimatorPlayAudio>(name, statesWithName, moveUp ? -1 : 1);
                return; // order changed — redraw fresh next repaint.
            }

            if (removeRequested)
            {
                RemoveNamedInstance<VRCAnimatorPlayAudio>(name, statesWithName);
                _clipsReorderListByKey.Remove(name);
                _clipsListDataByKey.Remove(name);
                _clipsExpandedByKey.Remove(name);
                return;
            }

            if (!expanded) return;

            _activeAudioStates = statesWithName;
            _activeAudioResolver = state => FindInstance<VRCAnimatorPlayAudio>(state, name);
            // Header may have just renamed this instance this frame — `name` is now stale and the resolver
            // won't find it on any state until next repaint recomputes namesUnion. Bail instead of indexing
            // an empty statesWithAudio array below.
            if (!_activeAudioStates.Any(state => _activeAudioResolver(state) != null)) return;
            DrawAudioInstanceBody(name);
        }

        void DrawAudioInstanceBody(string key)
        {
            _currentAudioBodyKey = key;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayAudioFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void SetAudioOnAll(string undoName, Action<VRCAnimatorPlayAudio> mutate)
        {
            foreach (var state in _activeAudioStates)
            {
                var audio = GetOrCreateAudio(state);
                Undo.RecordObject(audio, undoName);
                mutate(audio);
                EditorUtility.SetDirty(audio);
            }
        }

        void DrawPlayAudioFields()
        {
            var statesWithAudio = _activeAudioStates.Where(state => GetAudioForState(state) != null).ToArray();
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;

            DrawAudioSourceDragField();
            DrawAudioSourcePathField(first, statesWithAudio, multi);
            DrawAudioPlaybackOrderField(first, statesWithAudio, multi);
            if (first.PlaybackOrder == VRCAnimatorPlayAudio.Order.Parameter)
                DrawAudioParameterNameField(first, statesWithAudio, multi);
            DrawPlayAudioClipsList(statesWithAudio);
            DrawAudioVolumeFields(first, statesWithAudio, multi);
            DrawAudioPitchFields(first, statesWithAudio, multi);
            DrawAudioLoopField(first, statesWithAudio, multi);
            DrawAudioPlayStopColumnHeaders();
            DrawAudioOnEnterFields(first, statesWithAudio, multi);
            DrawAudioOnExitFields(first, statesWithAudio, multi);
            DrawAudioDelayField(first, statesWithAudio, multi);
        }

        void DrawAudioSourceDragField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.source"), GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var droppedSource = (AudioSource)EditorGUILayout.ObjectField(null, typeof(AudioSource), true);
                if (EditorGUI.EndChangeCheck() && droppedSource != null)
                {
                    var descriptor = droppedSource.GetComponentInParent<VRCAvatarDescriptor>();
                    string resolvedPath = GetAudioSourcePath(droppedSource.transform, descriptor != null ? descriptor.transform : null);
                    SetAudioOnAll("Set Source Path", audio => audio.SourcePath = resolvedPath);
                }
            }
        }

        void DrawAudioSourcePathField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.source_path"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).SourcePath != first.SourcePath);
                EditorGUI.BeginChangeCheck();
                string newPath = EditorGUILayout.TextField(first.SourcePath ?? "");
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Source Path", audio => audio.SourcePath = newPath);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPlaybackOrderField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.playback_order"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlaybackOrder != first.PlaybackOrder);
                EditorGUI.BeginChangeCheck();
                var orderLabels = new[] { L10n.Get("vrc.audio.order.random"), L10n.Get("vrc.audio.order.unique"), L10n.Get("vrc.audio.order.roundabout"), L10n.Get("vrc.audio.order.parameter") };
                var newOrder = (VRCAnimatorPlayAudio.Order)EditorGUILayout.Popup((int)first.PlaybackOrder, orderLabels);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Playback Order", audio => audio.PlaybackOrder = newOrder);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ClipsApplySettings != first.ClipsApplySettings);
                EditorGUI.BeginChangeCheck();
                var applyLabels = new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") };
                var newClipsApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.ClipsApplySettings, applyLabels, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Clips Apply Settings", audio => audio.ClipsApplySettings = newClipsApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioParameterNameField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.param_name"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ParameterName != first.ParameterName);
                DrawIntParamDropdown(first.ParameterName ?? "",
                    newParam => SetAudioOnAll("Edit Parameter Name", audio => audio.ParameterName = newParam));
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioVolumeFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.volume"), GUILayout.Width(95));
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.min"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.x, first.Volume.x));
                EditorGUI.BeginChangeCheck();
                float newVolMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.x), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Min", audio => audio.Volume = new Vector2(newVolMin, audio.Volume.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.max"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.y, first.Volume.y));
                EditorGUI.BeginChangeCheck();
                float newVolMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.y), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Max", audio => audio.Volume = new Vector2(audio.Volume.x, newVolMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).VolumeApplySettings != first.VolumeApplySettings);
                EditorGUI.BeginChangeCheck();
                var newVolApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.VolumeApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Apply Settings", audio => audio.VolumeApplySettings = newVolApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPitchFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.pitch"), GUILayout.Width(95));
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.min"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.x, first.Pitch.x));
                EditorGUI.BeginChangeCheck();
                float newPitchMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.x), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Min", audio => audio.Pitch = new Vector2(newPitchMin, audio.Pitch.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.max"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.y, first.Pitch.y));
                EditorGUI.BeginChangeCheck();
                float newPitchMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.y), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Max", audio => audio.Pitch = new Vector2(audio.Pitch.x, newPitchMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PitchApplySettings != first.PitchApplySettings);
                EditorGUI.BeginChangeCheck();
                var newPitchApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.PitchApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Apply Settings", audio => audio.PitchApplySettings = newPitchApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioLoopField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.loop"), GUILayout.Width(55));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).Loop != first.Loop);
                EditorGUI.BeginChangeCheck();
                bool newLoop = EditorGUILayout.Toggle(first.Loop, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop", audio => audio.Loop = newLoop);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).LoopApplySettings != first.LoopApplySettings);
                EditorGUI.BeginChangeCheck();
                var newLoopApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.LoopApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop Apply Settings", audio => audio.LoopApplySettings = newLoopApply);
                EditorGUI.showMixedValue = false;
            }
        }

        static void DrawAudioPlayStopColumnHeaders()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label(L10n.Get("vrc.audio.stop"), EditorStyles.label, GUILayout.Width(40));
                GUILayout.Label(L10n.Get("vrc.audio.play"), EditorStyles.label, GUILayout.Width(40));
            }
        }

        void DrawAudioOnEnterFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.on_enter"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnEnter != first.StopOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newStopEnter = EditorGUILayout.Toggle(first.StopOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Enter", audio => audio.StopOnEnter = newStopEnter);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnEnter != first.PlayOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newPlayEnter = EditorGUILayout.Toggle(first.PlayOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Enter", audio => audio.PlayOnEnter = newPlayEnter);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioOnExitFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.on_exit"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnExit != first.StopOnExit);
                EditorGUI.BeginChangeCheck();
                bool newStopExit = EditorGUILayout.Toggle(first.StopOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Exit", audio => audio.StopOnExit = newStopExit);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnExit != first.PlayOnExit);
                EditorGUI.BeginChangeCheck();
                bool newPlayExit = EditorGUILayout.Toggle(first.PlayOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Exit", audio => audio.PlayOnExit = newPlayExit);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioDelayField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.delay"), GUILayout.Width(220));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).DelayInSeconds, first.DelayInSeconds));
                EditorGUI.BeginChangeCheck();
                float newDelay = Mathf.Clamp(EditorGUILayout.FloatField(first.DelayInSeconds), 0f, 60f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play Delay", audio => audio.DelayInSeconds = newDelay);
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws the foldable clips list with a size int field and a ReorderableList for editing, reordering, and removing audio clips across all statesWithAudio. */
        void DrawPlayAudioClipsList(AnimatorState[] statesWithAudio)
        {
            string key = _currentAudioBodyKey;
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;
            var clips = first.Clips ?? Array.Empty<AudioClip>();
            float rowHeight = EditorGUIUtility.singleLineHeight;

            // Outer container — single background covers foldout header + list body
            var outerRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, Styles.SecondaryColor);

            // Foldout header + size int field — now inside the background
            var headerRow = EditorGUILayout.GetControlRect(false, rowHeight);
            const float sizeWidth = 40f;
            var foldoutRect = new Rect(headerRow.x, headerRow.y, headerRow.width - sizeWidth - 4f, rowHeight);
            bool clipsExpanded = !_clipsExpandedByKey.TryGetValue(key, out var storedClipsExpanded) || storedClipsExpanded;
            clipsExpanded = EditorGUI.Foldout(foldoutRect, clipsExpanded, L10n.Get("vrc.audio.clips"), true, EditorStyles.foldout);
            _clipsExpandedByKey[key] = clipsExpanded;
            EditorGUIUtility.AddCursorRect(foldoutRect, MouseCursor.Link);

            EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => (GetAudioForState(s).Clips?.Length ?? 0) != clips.Length);
            EditorGUI.BeginChangeCheck();
            int newSize = Mathf.Max(0, EditorGUI.IntField(new Rect(headerRow.xMax - sizeWidth, headerRow.y, sizeWidth, rowHeight), clips.Length));
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var state in _activeAudioStates)
                {
                    var audio = GetOrCreateAudio(state);
                    Undo.RecordObject(audio, "Resize Clips");
                    var resized = new AudioClip[newSize];
                    if (audio.Clips != null) Array.Copy(audio.Clips, resized, Mathf.Min(audio.Clips.Length, newSize));
                    audio.Clips = resized;
                    EditorUtility.SetDirty(audio);
                }
                clips = first.Clips ?? Array.Empty<AudioClip>();
                _clipsListDataByKey.Remove(key);
                _clipsReorderListByKey.Remove(key);
            }
            EditorGUI.showMixedValue = false;

            if (clipsExpanded)
            {
                // Keep listData in sync with current clips
                if (!_clipsListDataByKey.TryGetValue(key, out var listData) || listData.Count != clips.Length)
                {
                    listData = new List<AudioClip>(clips);
                    _clipsListDataByKey[key] = listData;
                }
                else
                    for (int i = 0; i < clips.Length; i++)
                        listData[i] = clips[i];

                // Build ReorderableList once; rebuilt when removed from cache
                if (!_clipsReorderListByKey.TryGetValue(key, out var reorderList))
                {
                    reorderList = new ReorderableList(listData, typeof(AudioClip), true, false, false, false)
                    {
                        elementHeight = rowHeight,
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };

                    reorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        var currentListData = _clipsListDataByKey[key];
                        if (index >= currentListData.Count) return;
                        var localStates = _activeAudioStates.Where(state => GetAudioForState(state) != null).ToArray();
                        bool localMulti = localStates.Length > 1;

                        EditorGUI.showMixedValue = localMulti && localStates.Any(state => {
                            var audio = GetAudioForState(state);
                            return audio.Clips == null || index >= audio.Clips.Length || audio.Clips[index] != currentListData[index];
                        });
                        EditorGUI.BeginChangeCheck();
                        var newClip = (AudioClip)EditorGUI.ObjectField(
                            new Rect(rect.x, rect.y + 1f, rect.width - 26f, rect.height - 2f),
                            currentListData[index], typeof(AudioClip), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            currentListData[index] = newClip;
                            int capturedIndex = index;
                            foreach (var state in _activeAudioStates)
                            {
                                var audio = GetOrCreateAudio(state);
                                if (audio.Clips == null || capturedIndex >= audio.Clips.Length)
                                {
                                    var expanded = new AudioClip[capturedIndex + 1];
                                    audio.Clips?.CopyTo(expanded, 0);
                                    audio.Clips = expanded;
                                }
                                Undo.RecordObject(audio, "Edit Audio Clip");
                                audio.Clips[capturedIndex] = newClip;
                                EditorUtility.SetDirty(audio);
                            }
                        }
                        EditorGUI.showMixedValue = false;

                        if (GUI.Button(new Rect(rect.xMax - 24f, rect.y + 1f, 24f, rect.height - 2f), "−", Styles.CondBtn))
                            _pendingRemoveClip = (key, index);
                    };

                    reorderList.onReorderCallbackWithDetails = (reorderableList, oldIndex, newIndex) =>
                    {
                        var currentListData = _clipsListDataByKey[key];
                        var firstAudio = GetAudioForState(_activeAudioStates[0]);
                        if (firstAudio != null)
                        {
                            Undo.RecordObject(firstAudio, "Reorder Clips");
                            firstAudio.Clips = currentListData.ToArray();
                            EditorUtility.SetDirty(firstAudio);
                        }
                        for (int stateIndex = 1; stateIndex < _activeAudioStates.Length; stateIndex++)
                        {
                            var audio = GetOrCreateAudio(_activeAudioStates[stateIndex]);
                            if (audio.Clips == null || audio.Clips.Length < 2) continue;
                            Undo.RecordObject(audio, "Reorder Clips");
                            var stateClips = audio.Clips.ToList();
                            if (oldIndex < stateClips.Count)
                            {
                                var item = stateClips[oldIndex];
                                stateClips.RemoveAt(oldIndex);
                                stateClips.Insert(Mathf.Clamp(newIndex, 0, stateClips.Count), item);
                                audio.Clips = stateClips.ToArray();
                            }
                            EditorUtility.SetDirty(audio);
                        }
                    };

                    _clipsReorderListByKey[key] = reorderList;
                }

                if (clips.Length == 0)
                    EditorGUILayout.LabelField(L10n.Get("vrc.list_empty"), Styles.EmptyLabel);
                else
                    reorderList.DoLayoutList();

                // Deferred remove — avoids layout mismatch from inside drawElementCallback
                if (_pendingRemoveClip.index >= 0 && _pendingRemoveClip.key == key)
                {
                    int capturedIndex = _pendingRemoveClip.index;
                    _pendingRemoveClip = (null, -1);
                    foreach (var state in _activeAudioStates)
                    {
                        var audio = GetOrCreateAudio(state);
                        if (audio.Clips == null || capturedIndex >= audio.Clips.Length) continue;
                        Undo.RecordObject(audio, "Remove Audio Clip");
                        audio.Clips = audio.Clips.Where((_, idx) => idx != capturedIndex).ToArray();
                        EditorUtility.SetDirty(audio);
                    }
                    _clipsReorderListByKey.Remove(key);
                }
                else
                {
                    var addRow = EditorGUILayout.GetControlRect(false, rowHeight);
                    if (CursorBtn(new Rect(addRow.xMax - 24f, addRow.y, 24f, rowHeight), "+", Styles.CondBtn))
                    {
                        foreach (var state in _activeAudioStates)
                        {
                            var audio = GetOrCreateAudio(state);
                            Undo.RecordObject(audio, "Add Audio Clip");
                            var expanded = new AudioClip[(audio.Clips?.Length ?? 0) + 1];
                            audio.Clips?.CopyTo(expanded, 0);
                            audio.Clips = expanded;
                            EditorUtility.SetDirty(audio);
                        }
                        _clipsReorderListByKey.Remove(key);
                    }
                }

                GUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        VRCAnimatorPlayAudio GetAudioForState(AnimatorState state)
            => _activeAudioResolver != null ? _activeAudioResolver(state) : InstanceAt<VRCAnimatorPlayAudio>(state, 0);

        /* Non-generic helper so callers outside the VRC_SDK_VRCSDK3 guard block can check audio presence
           without naming VRCAnimatorPlayAudio. */
        static bool HasAnyAudio(AnimatorState state) => HasInstance<VRCAnimatorPlayAudio>(state);

        /* Returns the resolver-scoped existing audio, or the first audio, or adds and registers a new one via Undo. */
        VRCAnimatorPlayAudio GetOrCreateAudio(AnimatorState state)
        {
            if (_activeAudioResolver != null)
            {
                var resolved = _activeAudioResolver(state);
                if (resolved != null) return resolved;
            }
            var existing = InstanceAt<VRCAnimatorPlayAudio>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCAnimatorPlayAudio>(state, "Play Audio");
        }

        /* Rescoped Remove All: destroys every audio instance (all names) on every selected state. */
        void RemoveAudioFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var audios = Instances<VRCAnimatorPlayAudio>(state);
                if (audios.Count == 0) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Play Audio");
                state.behaviours = state.behaviours.Where(b => !(b is VRCAnimatorPlayAudio)).ToArray();
                foreach (var audio in audios) Undo.DestroyObjectImmediate(audio);
                EditorUtility.SetDirty(state);
            }
            _clipsReorderListByKey.Clear();
            _clipsListDataByKey.Clear();
            _clipsExpandedByKey.Clear();
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
