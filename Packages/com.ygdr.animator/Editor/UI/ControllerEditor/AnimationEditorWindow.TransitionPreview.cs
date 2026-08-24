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
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace YGDR.Editor.Animation
{
    /* Single transition or an ordered chain, AnimationClip-only motions (no BlendTree). Reflects into
       UnityEditor.AvatarPreview for viewport/camera/model/scrub UI; drives a custom PlayableGraph for the
       N-clip blend since AvatarPreview has no blend logic of its own. See YGDR-Notes/TRANSITION_PREVIEW_PLAN.md. */
    internal partial class AnimationEditorWindow
    {
        const string PreviewExpandedPrefsKey = "AnimatorTools.Transitions.PreviewExpanded";

        VisualElement _previewWrapper;
        VisualElement _previewHeader;
        VisualElement _previewBody;
        bool _previewOpen;
        IMGUIContainer _previewContainer;
        Label _previewStatusLabel;

        object _previewAvatarPreview;
        Animator _previewBoundAnimator;
        PlayableGraph _previewGraph;
        AnimationMixerPlayable _previewMixer;
        AnimationClipPlayable[] _previewPlayables;
        IVisualElementScheduledItem _previewTick;
        readonly object[] _previewDoPreviewArgs = { default(Rect), GUIStyle.none }; // reused every repaint to skip a per-frame array alloc; only [0] (rect) ever changes

        // Chain of N transitions has N+1 clips (consecutive segments share the "dest of N == source of N+1" clip).
        AnimatorStateTransition[] _previewChain;
        AnimationClip[] _previewClips;
        // Per-clip[i]: absolute preview-timeline time / local clip time when clip i starts playing.
        float[] _previewClipStartAbs;
        float[] _previewClipStartLocal;
        // Per-chain[i]: absolute time transition i's blend begins, and its blend duration.
        float[] _previewExitAbs;
        float[] _previewBlendDur;
        float _previewWindowSeconds;

        // Captured from the outgoing AvatarPreview right before teardown, reapplied to the next one created.
        bool _previewCameraStateCaptured;
        Vector2 _previewCameraDir;
        float _previewCameraZoom;
        Vector3 _previewCameraPivotOffset;

        /* ── Reflection cache (resolved once, never per-frame) ── */

        static bool _previewReflectionAttempted;
        static bool _previewReflectionOk;

        static ConstructorInfo _avatarPreviewCtor;
        static PropertyInfo _avatarPreviewAnimatorProp;
        static FieldInfo _avatarPreviewTimeControlField;
        static MethodInfo _avatarPreviewDoPreviewMethod;
        static MethodInfo _avatarPreviewOnDisableMethod;
        static FieldInfo _avatarPreviewAvatarScaleField; // optional — min zoom-in clamp is m_ZoomFactor >= m_AvatarScale/10
        static FieldInfo _avatarPreviewPreviewDirField;  // Vector2 orbit yaw/pitch
        static FieldInfo _avatarPreviewZoomFactorField;  // float
        static FieldInfo _avatarPreviewPivotOffsetField; // Vector3 pan offset
        static bool HasCameraFields => _avatarPreviewPreviewDirField != null && _avatarPreviewZoomFactorField != null && _avatarPreviewPivotOffsetField != null;

        static FieldInfo _timeControlCurrentTimeField;
        static FieldInfo _timeControlStartTimeField;
        static FieldInfo _timeControlStopTimeField;
        static PropertyInfo _timeControlNormalizedTimeProp;
        static PropertyInfo _timeControlPlayingProp;
        static MethodInfo _timeControlUpdateMethod;

        static void EnsurePreviewReflectionCache()
        {
            if (_previewReflectionAttempted) return;
            _previewReflectionAttempted = true;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try
            {
                var avatarPreviewType = Type.GetType("UnityEditor.AvatarPreview, UnityEditor");
                var timeControlType = Type.GetType("UnityEditor.TimeControl, UnityEditor");
                if (avatarPreviewType == null || timeControlType == null) return;

                _avatarPreviewCtor = avatarPreviewType.GetConstructor(flags, null, new[] { typeof(Animator), typeof(Motion) }, null);
                _avatarPreviewAnimatorProp = avatarPreviewType.GetProperty("Animator", flags);
                _avatarPreviewTimeControlField = avatarPreviewType.GetField("timeControl", flags);
                _avatarPreviewDoPreviewMethod = avatarPreviewType.GetMethod("DoAvatarPreview", flags, null, new[] { typeof(Rect), typeof(GUIStyle) }, null);
                _avatarPreviewOnDisableMethod = avatarPreviewType.GetMethod("OnDisable", flags, null, Type.EmptyTypes, null);
                _avatarPreviewAvatarScaleField = avatarPreviewType.GetField("m_AvatarScale", flags);
                _avatarPreviewPreviewDirField = avatarPreviewType.GetField("m_PreviewDir", flags);
                _avatarPreviewZoomFactorField = avatarPreviewType.GetField("m_ZoomFactor", flags);
                _avatarPreviewPivotOffsetField = avatarPreviewType.GetField("m_PivotPositionOffset", flags);

                _timeControlCurrentTimeField = timeControlType.GetField("currentTime", flags);
                _timeControlStartTimeField = timeControlType.GetField("startTime", flags);
                _timeControlStopTimeField = timeControlType.GetField("stopTime", flags);
                _timeControlNormalizedTimeProp = timeControlType.GetProperty("normalizedTime", flags);
                _timeControlPlayingProp = timeControlType.GetProperty("playing", flags);
                _timeControlUpdateMethod = timeControlType.GetMethod("Update", flags, null, Type.EmptyTypes, null);

                _previewReflectionOk = _avatarPreviewCtor != null && _avatarPreviewAnimatorProp != null && _avatarPreviewTimeControlField != null
                    && _avatarPreviewDoPreviewMethod != null && _avatarPreviewOnDisableMethod != null
                    && _timeControlCurrentTimeField != null && _timeControlStartTimeField != null && _timeControlStopTimeField != null
                    && _timeControlNormalizedTimeProp != null && _timeControlPlayingProp != null && _timeControlUpdateMethod != null;

                if (!_previewReflectionOk)
                    Debug.LogWarning("[AnimatorTools] Transition preview: Unity internal AvatarPreview/TimeControl API shape changed, preview disabled.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimatorTools] Transition preview: reflection setup failed, preview disabled. {e}");
            }
        }

        /* ── UI ── */

        VisualElement BuildTransitionPreviewSection()
        {
            _previewWrapper = new VisualElement();
            _previewWrapper.style.marginTop = 12;
            _previewOpen = EditorPrefs.GetBool(PreviewExpandedPrefsKey, false);

            _previewBody = new VisualElement();

            _previewStatusLabel = new Label();
            _previewStatusLabel.AddToClassList("ygdr-empty-label");
            _previewBody.Add(_previewStatusLabel);

            _previewContainer = new IMGUIContainer(DrawTransitionPreviewGUI);
            _previewContainer.style.height = 300;
            _previewContainer.style.display = DisplayStyle.None;
            _previewBody.Add(_previewContainer);

            _previewHeader = BuildSettingsSectionHeader(_previewWrapper, _previewBody, L10n.Get("transitions.preview"),
                () => _previewOpen,
                open =>
                {
                    _previewOpen = open;
                    EditorPrefs.SetBool(PreviewExpandedPrefsKey, open);
                    if (open) RefreshTransitionPreviewSection();
                    else TeardownTransitionPreview();
                });
            _previewBody.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            // Panel already has bottom padding — zero this section's or it stacks.
            _previewBody.style.paddingBottom = 0;

            return _previewWrapper;
        }

        // Unlike Settings tabs, this section isn't rebuilt on language change — re-set the label text explicitly.
        void RefreshTransitionPreviewHeaderLabel()
        {
            var label = _previewHeader?.Q<Label>(className: "ygdr-behavior-section-label");
            if (label != null) label.text = (_previewOpen ? "▼ " : "▶ ") + L10n.Get("transitions.preview");
        }

        void ShowStatus(string message)
        {
            _previewStatusLabel.text = message;
            _previewStatusLabel.style.display = DisplayStyle.Flex;
            _previewContainer.style.display = DisplayStyle.None;
        }

        // Called on every selection change; only (re)builds the graph when the selection actually changed.
        void RefreshTransitionPreviewSection()
        {
            if (_previewWrapper == null) return;

            bool hasSelection = _selectedTransitions.Length > 0 || _selectedEntryTransitions.Length > 0;
            _previewWrapper.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasSelection) { TeardownTransitionPreview(); return; }

            if (!_previewOpen) return; // collapsed — zero cost, don't touch reflection/graph

            if (!TryResolvePreviewMotions(out var chain, out var clips, out string invalidReason))
            {
                // Keep AvatarPreview alive (camera persists) — drop only our own blend graph.
                DestroyPreviewGraph();
                _previewTick?.Pause();
                _previewChain = null;
                _previewClips = null;
                ShowStatus(invalidReason);
                return;
            }

            if (_previewChain != null && chain.SequenceEqual(_previewChain) && clips.SequenceEqual(_previewClips)
                && _previewAvatarPreview != null)
                return; // already built for this exact selection

            _previewChain = chain;
            _previewClips = clips;
            ComputePreviewTimeMapping(chain, clips);

            if (_previewAvatarPreview == null)
            {
                EnsurePreviewReflectionCache();
                if (!_previewReflectionOk) { ShowStatus(L10n.Get("transitions.preview_api_unavailable")); return; }

                try
                {
                    _previewAvatarPreview = _avatarPreviewCtor.Invoke(new object[] { FindScenePreviewAnimator(_controller), clips[0] });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AnimatorTools] Transition preview: failed to create AvatarPreview. {e}");
                    _previewAvatarPreview = null;
                    ShowStatus(L10n.Get("transitions.preview_init_failed"));
                    return;
                }

                // currentTime defaults to float.NegativeInfinity and nothing else re-seeds it.
                var timeControl = _avatarPreviewTimeControlField.GetValue(_previewAvatarPreview);
                _timeControlCurrentTimeField.SetValue(timeControl, 0f);

                RestoreCameraState();

                _previewTick = _previewContainer.schedule.Execute(TickTransitionPreview).Every(16);
            }
            else
            {
                // AvatarPreview instance survives the switch (camera persists); only the blend graph is stale.
                DestroyPreviewGraph();
                var timeControl = _avatarPreviewTimeControlField.GetValue(_previewAvatarPreview);
                _timeControlCurrentTimeField.SetValue(timeControl, 0f);
                _previewTick?.Resume(); // undo a Pause() from a prior transient invalid-selection frame
            }

            _previewStatusLabel.style.display = DisplayStyle.None;
            _previewContainer.style.display = DisplayStyle.Flex;
        }

        // Prefer the real scene avatar over AvatarPreview's generic/humanoid fallback dummy — VRC clips that
        // toggle specific GameObjects/blendshapes animate nothing on a dummy lacking those objects.
        static Animator FindScenePreviewAnimator(AnimatorController controller)
        {
            if (controller == null) return null;
            var matches = UnityEngine.Object.FindObjectsOfType<Animator>().Where(animator => animator.runtimeAnimatorController == controller).ToArray();
            if (matches.Length == 0) return null;
            // Prefer the rigged body (has an Avatar assigned) over props sharing the same controller asset.
            return matches.FirstOrDefault(animator => animator.avatar != null) ?? matches[0];
        }

        // Resolves selection into an ordered chain (dest of i == source of i+1), order derived structurally
        // from source/dest states — Selection.objects array order isn't a reliable selection-click order.
        bool TryResolvePreviewMotions(out AnimatorStateTransition[] chain, out AnimationClip[] clips, out string invalidReason)
        {
            chain = null; clips = null; invalidReason = null;

            if (_selectedTransitions.Length == 0 || _selectedEntryTransitions.Length != 0 || _controller == null)
            { invalidReason = L10n.Get("transitions.preview_select_single"); return false; }

            var sourceStateByTransition = new Dictionary<AnimatorStateTransition, AnimatorState>();
            var destStateByTransition = new Dictionary<AnimatorStateTransition, AnimatorState>();

            foreach (var transition in _selectedTransitions)
            {
                var ownerStateMachine = FindOwnerSM(_controller, transition);
                if (ownerStateMachine == null || ownerStateMachine.anyStateTransitions.Contains(transition))
                { invalidReason = L10n.Get("transitions.preview_no_anystate"); return false; }

                var sourceState = FindSourceState(ownerStateMachine, transition);
                if (sourceState == null)
                { invalidReason = L10n.Get("transitions.preview_select_single"); return false; }

                if (transition.destinationState == null)
                { invalidReason = L10n.Get("transitions.preview_no_exit"); return false; }

                sourceStateByTransition[transition] = sourceState;
                destStateByTransition[transition] = transition.destinationState;
            }

            // Unbroken chain: each state is source of at most one selected transition AND dest of at most
            // one (a state landed on twice means a merge/cycle, not a line), and exactly one state is a
            // source but never a dest (the chain root).
            var destStates = new HashSet<AnimatorState>();
            var transitionBySourceState = new Dictionary<AnimatorState, AnimatorStateTransition>();
            foreach (var kvp in sourceStateByTransition)
            {
                if (transitionBySourceState.ContainsKey(kvp.Value))
                { invalidReason = L10n.Get("transitions.preview_broken_chain"); return false; }
                transitionBySourceState[kvp.Value] = kvp.Key;
            }
            foreach (var destState in destStateByTransition.Values)
            {
                if (!destStates.Add(destState))
                { invalidReason = L10n.Get("transitions.preview_broken_chain"); return false; }
            }

            var rootStates = sourceStateByTransition.Values.Where(state => !destStates.Contains(state)).ToArray();
            if (rootStates.Length != 1)
            { invalidReason = L10n.Get("transitions.preview_broken_chain"); return false; }

            // Loop bounded by selection size regardless of the checks above — a defense-in-depth guard
            // against ever re-walking a cycle into an unbounded list (a graph-construction bug elsewhere
            // must never turn into an OOM crash here).
            var orderedChain = new List<AnimatorStateTransition>();
            var currentState = rootStates[0];
            while (orderedChain.Count < _selectedTransitions.Length && transitionBySourceState.TryGetValue(currentState, out var nextTransition))
            {
                orderedChain.Add(nextTransition);
                currentState = destStateByTransition[nextTransition];
            }
            if (orderedChain.Count != _selectedTransitions.Length)
            { invalidReason = L10n.Get("transitions.preview_broken_chain"); return false; }

            var orderedClips = new AnimationClip[orderedChain.Count + 1];
            orderedClips[0] = sourceStateByTransition[orderedChain[0]].motion as AnimationClip;
            for (int i = 0; i < orderedChain.Count; i++)
                orderedClips[i + 1] = destStateByTransition[orderedChain[i]].motion as AnimationClip;

            if (orderedClips.Any(clip => clip == null))
            { invalidReason = L10n.Get("transitions.preview_needs_clip"); return false; }

            chain = orderedChain.ToArray();
            clips = orderedClips;
            return true;
        }

        // exitTime/offset are normalized (0-1) fractions of clip length unless duration is fixed (seconds).
        // clip[i+1] starts playing at chain[i]'s blend-begin time, from local time = offset (not 0) — each
        // clip's local time is tracked relative to when IT started, so a clip that's both a dest and the
        // next segment's source plays continuously across the join instead of resetting. Window includes
        // the final clip's tail after its own offset, or an instant (0-duration) chain would freeze on one frame.
        void ComputePreviewTimeMapping(AnimatorStateTransition[] chain, AnimationClip[] clips)
        {
            int segmentCount = chain.Length;
            // Reused across repaints (this runs every one) — only reallocate when the clip count changes,
            // not every call. clips[0]'s start is always t=0/local=0 and must be reset explicitly now that
            // the array isn't guaranteed fresh-zeroed.
            if (_previewClipStartAbs == null || _previewClipStartAbs.Length != clips.Length)
            {
                _previewClipStartAbs = new float[clips.Length];
                _previewClipStartLocal = new float[clips.Length];
                _previewExitAbs = new float[segmentCount];
                _previewBlendDur = new float[segmentCount];
            }
            _previewClipStartAbs[0] = 0f;
            _previewClipStartLocal[0] = 0f;

            for (int i = 0; i < segmentCount; i++)
            {
                var transition = chain[i];
                var sourceClip = clips[i];

                float exitLocal = transition.hasExitTime ? transition.exitTime * sourceClip.length : 0f;
                float exitAbs = _previewClipStartAbs[i] + Mathf.Max(0f, exitLocal - _previewClipStartLocal[i]);
                _previewExitAbs[i] = exitAbs;
                _previewBlendDur[i] = Mathf.Max(0f, transition.hasFixedDuration ? transition.duration : transition.duration * sourceClip.length);

                _previewClipStartAbs[i + 1] = exitAbs;
                _previewClipStartLocal[i + 1] = transition.offset * clips[i + 1].length;
            }

            float tailSeconds = Mathf.Max(0.05f, clips[^1].length - _previewClipStartLocal[^1]);
            _previewWindowSeconds = Mathf.Max(0.01f, _previewExitAbs[^1] + _previewBlendDur[^1] + tailSeconds);
        }

        // Repaints while playing so playback advances with no user input. TimeControl.Update() itself runs
        // in DrawTransitionPreviewGUI unconditionally — gating it on "playing" left scrub-drag-while-paused inert.
        void TickTransitionPreview()
        {
            if (_previewAvatarPreview == null) { _previewTick?.Pause(); return; }
            var timeControl = _avatarPreviewTimeControlField.GetValue(_previewAvatarPreview);
            if (!(bool)_timeControlPlayingProp.GetValue(timeControl)) return;
            _previewContainer.MarkDirtyRepaint();
        }

        void DrawTransitionPreviewGUI()
        {
            if (_previewAvatarPreview == null) return;

            // Transition/state could be deleted externally (Undo, other UI) without a selection event firing.
            if (_previewChain == null || _previewChain.Length == 0) { TeardownTransitionPreview(); return; }

            RebindPreviewAnimatorIfNeeded();

            if (Event.current.type == EventType.Repaint)
            {
                var timeControl = _avatarPreviewTimeControlField.GetValue(_previewAvatarPreview);

                // Duration/Exit Time/offset can be live-edited above without the selection changing identity —
                // recompute every repaint (cheap floats). Only start/stop change; currentTime never gets touched here.
                ComputePreviewTimeMapping(_previewChain, _previewClips);
                _timeControlStartTimeField.SetValue(timeControl, 0f);
                _timeControlStopTimeField.SetValue(timeControl, _previewWindowSeconds);

                _timeControlUpdateMethod.Invoke(timeControl, null); // applies pending scrub-drag, advances playback if playing

                float t = (float)_timeControlNormalizedTimeProp.GetValue(timeControl);
                EvaluatePreviewBlend(t);
            }

            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(280));
            try
            {
                _previewDoPreviewArgs[0] = rect;
                _avatarPreviewDoPreviewMethod.Invoke(_previewAvatarPreview, _previewDoPreviewArgs);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimatorTools] Transition preview: DoAvatarPreview threw. {e}");
                TeardownTransitionPreview();
            }
        }

        // AvatarPreview.Animator is null until its first DoAvatarPreview call, and it rebuilds whenever the
        // user picks a different model via its built-in avatar picker — never bind once. A transition switch
        // (same model) also lands here to rebuild the now-stale blend graph, so model-only setup (controller
        // strip, zoom shrink) is gated on isNewAnimator — re-running it on every switch would compound the
        // zoom shrink (0.1x, then 0.01x, ...) since AvatarPreview doesn't reset m_AvatarScale on its own.
        void RebindPreviewAnimatorIfNeeded()
        {
            var currentAnimator = (Animator)_avatarPreviewAnimatorProp.GetValue(_previewAvatarPreview);
            if (currentAnimator == null) return;

            bool isNewAnimator = currentAnimator != _previewBoundAnimator;
            if (!isNewAnimator && _previewGraph.IsValid()) return;

            DestroyPreviewGraph();

            if (isNewAnimator)
            {
                currentAnimator.runtimeAnimatorController = null;
                currentAnimator.applyRootMotion = false;
                currentAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                currentAnimator.enabled = true; // a disabled Animator won't write PlayableOutput results to transforms even under manual Evaluate()

                // m_AvatarScale is sized for a full avatar; min zoom-in clamp (m_ZoomFactor >= m_AvatarScale/10)
                // is too coarse for small props — shrink it once per real model, right after AvatarPreview
                // recalculates its own m_AvatarScale for the new model.
                if (_avatarPreviewAvatarScaleField != null)
                {
                    float currentScale = (float)_avatarPreviewAvatarScaleField.GetValue(_previewAvatarPreview);
                    _avatarPreviewAvatarScaleField.SetValue(_previewAvatarPreview, currentScale * 0.1f);
                }
            }

            _previewGraph = PlayableGraph.Create("YGDR Transition Preview");
            _previewMixer = AnimationMixerPlayable.Create(_previewGraph, _previewClips.Length);
            _previewPlayables = new AnimationClipPlayable[_previewClips.Length];
            for (int i = 0; i < _previewClips.Length; i++)
            {
                _previewPlayables[i] = AnimationClipPlayable.Create(_previewGraph, _previewClips[i]);
                _previewGraph.Connect(_previewPlayables[i], 0, _previewMixer, i);
                _previewMixer.SetInputWeight(i, i == 0 ? 1f : 0f);
            }
            var output = AnimationPlayableOutput.Create(_previewGraph, "Preview", currentAnimator);
            output.SetSourcePlayable(_previewMixer);
            _previewGraph.Play();

            _previewBoundAnimator = currentAnimator;
        }

        // Must run BEFORE DoAvatarPreview in the same pass — AvatarPreview does not sample the motion itself.
        // t is normalized over the full window; at most two adjacent clips are ever weighted non-zero at once.
        void EvaluatePreviewBlend(float t)
        {
            if (!_previewGraph.IsValid()) return;

            float absoluteTime = t * _previewWindowSeconds;
            int segmentCount = _previewChain.Length;

            int activeLower = segmentCount; // past every blend — final clip alone
            float blendWeight = 0f;
            for (int i = 0; i < segmentCount; i++)
            {
                if (absoluteTime < _previewExitAbs[i]) { activeLower = i; blendWeight = 0f; break; }
                if (absoluteTime < _previewExitAbs[i] + _previewBlendDur[i])
                {
                    activeLower = i;
                    blendWeight = _previewBlendDur[i] <= 0.0001f ? 1f : (absoluteTime - _previewExitAbs[i]) / _previewBlendDur[i];
                    break;
                }
            }

            for (int clipIndex = 0; clipIndex < _previewClips.Length; clipIndex++)
            {
                var clip = _previewClips[clipIndex];
                float localTime = _previewClipStartLocal[clipIndex] + Mathf.Max(0f, absoluteTime - _previewClipStartAbs[clipIndex]);
                localTime = clip.isLooping ? Mathf.Repeat(localTime, Mathf.Max(0.0001f, clip.length)) : Mathf.Clamp(localTime, 0f, clip.length);
                _previewPlayables[clipIndex].SetTime(localTime);

                float weight = clipIndex == activeLower ? 1f - blendWeight
                    : clipIndex == activeLower + 1 ? blendWeight
                    : 0f;
                _previewMixer.SetInputWeight(clipIndex, weight);
            }

            _previewGraph.Evaluate(0f);
        }

        void DestroyPreviewGraph()
        {
            if (_previewGraph.IsValid()) _previewGraph.Destroy();
        }

        void CaptureCameraState()
        {
            if (!HasCameraFields) return;
            _previewCameraDir = (Vector2)_avatarPreviewPreviewDirField.GetValue(_previewAvatarPreview);
            _previewCameraZoom = (float)_avatarPreviewZoomFactorField.GetValue(_previewAvatarPreview);
            _previewCameraPivotOffset = (Vector3)_avatarPreviewPivotOffsetField.GetValue(_previewAvatarPreview);
            _previewCameraStateCaptured = true;
        }

        void RestoreCameraState()
        {
            if (!_previewCameraStateCaptured || !HasCameraFields) return;
            _avatarPreviewPreviewDirField.SetValue(_previewAvatarPreview, _previewCameraDir);
            _avatarPreviewZoomFactorField.SetValue(_previewAvatarPreview, _previewCameraZoom);
            _avatarPreviewPivotOffsetField.SetValue(_previewAvatarPreview, _previewCameraPivotOffset);
        }

        /* Called on collapse, selection change away from a valid transition, window OnDisable, and domain reload. */
        void TeardownTransitionPreview()
        {
            _previewTick?.Pause();
            _previewTick = null;

            DestroyPreviewGraph();

            if (_previewAvatarPreview != null)
            {
                CaptureCameraState();

                if (_avatarPreviewOnDisableMethod != null)
                {
                    try { _avatarPreviewOnDisableMethod.Invoke(_previewAvatarPreview, null); }
                    catch (Exception e) { Debug.LogWarning($"[AnimatorTools] Transition preview: OnDisable threw during teardown. {e}"); }
                }
            }
            _previewAvatarPreview = null;
            _previewBoundAnimator = null;
            _previewChain = null;
            _previewClips = null;
        }
    }
}
#endif
