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
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // All ops take the same shape — an ordered (clip, binding) list — so order-sensitive ops
    // (Cascade) and order-agnostic ops (Scale/Reverse/PingPong) share one input type instead of
    // each caller building a separate Dictionary<clip, List<binding>> alongside it.
    internal static class AnimatorKeyframeTimingOps
    {
        static IEnumerable<(AnimationClip clip, List<EditorCurveBinding> bindings)> GroupByClip(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings) =>
            orderedBindings
                .GroupBy(entry => entry.clip)
                .Select(group => (group.Key, group.Select(entry => entry.binding).ToList()));

        // Scans the given bindings for the earliest first-key / latest last-key time,
        // skipping curves with fewer than minKeyCount keys. Shared by every op below that
        // needs a range or anchor across multiple bindings.
        static (float first, float last) ComputeSharedRange(
            IEnumerable<(AnimationClip clip, EditorCurveBinding binding)> bindings, int minKeyCount)
        {
            float first = float.MaxValue, last = float.MinValue;
            foreach (var (clip, binding) in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < minKeyCount) continue;
                first = Mathf.Min(first, curve.keys[0].time);
                last = Mathf.Max(last, curve.keys[^1].time);
            }
            return (first, last);
        }

        // Rescales keyframe spacing, anchored at the earliest first-key time across all
        // selected bindings on a clip (one shared anchor, not per-curve independently), so
        // multiple selected tracks stay aligned relative to each other after scaling.
        // factor 2 = Double Time, factor 0.5 = Half Time; roundUp picks ceiling vs floor
        // frame snapping for the half-time case, where halved spacing can land off-grid.
        internal static void ScaleKeyframeSpacing(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings, float factor, bool roundUp)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                var curves = new AnimationCurve[bindings.Count];
                float anchor = float.MaxValue;
                for (int i = 0; i < bindings.Count; i++)
                {
                    curves[i] = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    if (curves[i] == null || curves[i].length == 0) continue;
                    anchor = Mathf.Min(anchor, curves[i].keys[0].time);
                }
                if (anchor == float.MaxValue) continue;

                Undo.RegisterCompleteObjectUndo(clip, factor > 1f ? "Double Time" : "Half Time");
                float frameRate = clip.frameRate;

                for (int i = 0; i < bindings.Count; i++)
                {
                    var curve = curves[i];
                    if (curve == null || curve.length == 0) continue;

                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        float scaledTime = anchor + (keys[k].time - anchor) * factor;
                        if (factor < 1f)
                        {
                            float frame = roundUp ? Mathf.Ceil(scaledTime * frameRate) : Mathf.Floor(scaledTime * frameRate);
                            scaledTime = frame / frameRate;
                        }
                        keys[k].time = Mathf.Max(0f, scaledTime);
                    }
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, bindings[i], curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Reflects keys around a pivot time: newTime = 2*pivot - time, values keep their
        // order-reversed pairing, tangents swap in/out and negate to match the flipped
        // direction of travel. Shared by ReverseKeyframes (pivot = range center) and
        // PingPongKeyframes (pivot = shared last-key time).
        static Keyframe[] BuildMirroredKeys(Keyframe[] keys, float pivotTime)
        {
            int count = keys.Length;
            var mirrored = new Keyframe[count];
            for (int i = 0; i < count; i++)
            {
                var source = keys[count - 1 - i];
                mirrored[i] = new Keyframe(
                    2f * pivotTime - source.time,
                    source.value,
                    -source.outTangent,
                    -source.inTangent,
                    source.outWeight,
                    source.inWeight)
                {
                    weightedMode = source.weightedMode
                };
            }
            return mirrored;
        }

        // Mirrors every keyframe on the selected bindings around their shared first/last key
        // time on a clip (one shared range per clip, not per-curve independently), so relative
        // timing between the selected tracks is preserved.
        internal static void ReverseKeyframes(List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                var curves = new AnimationCurve[bindings.Count];
                float firstTime = float.MaxValue, lastTime = float.MinValue;
                for (int i = 0; i < bindings.Count; i++)
                {
                    curves[i] = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    if (curves[i] == null || curves[i].length == 0) continue;
                    firstTime = Mathf.Min(firstTime, curves[i].keys[0].time);
                    lastTime = Mathf.Max(lastTime, curves[i].keys[^1].time);
                }
                if (firstTime >= lastTime) continue;

                Undo.RegisterCompleteObjectUndo(clip, "Reverse Keyframes");

                float pivot = (firstTime + lastTime) * 0.5f;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var curve = curves[i];
                    if (curve == null || curve.length == 0) continue;

                    curve.keys = BuildMirroredKeys(curve.keys, pivot);
                    AnimationUtility.SetEditorCurve(clip, bindings[i], curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Appends a mirrored copy of each selected curve's keys after its shared last-key
        // time — A-to-B becomes A-to-B-to-A. The mirrored run's first key lands exactly on
        // the shared last key (the ping-pong pivot), so it's dropped to avoid a duplicate.
        internal static void PingPongKeyframes(List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                var curves = new AnimationCurve[bindings.Count];
                float lastTime = float.MinValue;
                bool anyValid = false;
                for (int i = 0; i < bindings.Count; i++)
                {
                    curves[i] = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                    if (curves[i] == null || curves[i].length < 2) continue;
                    lastTime = Mathf.Max(lastTime, curves[i].keys[^1].time);
                    anyValid = true;
                }
                if (!anyValid) continue;

                Undo.RegisterCompleteObjectUndo(clip, "Ping-Pong Keyframes");

                for (int i = 0; i < bindings.Count; i++)
                {
                    var curve = curves[i];
                    if (curve == null || curve.length < 2) continue;

                    var keys = curve.keys;
                    var mirrored = BuildMirroredKeys(keys, lastTime);

                    var combined = new Keyframe[keys.Length + mirrored.Length - 1];
                    keys.CopyTo(combined, 0);
                    for (int j = 1; j < mirrored.Length; j++)
                        combined[keys.Length + j - 1] = mirrored[j];

                    curve.keys = combined;

                    // The pivot's outTangent was calibrated for a curve that ended there —
                    // spliced against the new segment's tangent it can overshoot past 0/1.
                    // Force linear just on the join; the rest of the curve keeps its shape.
                    int pivotIndex = keys.Length - 1;
                    if (pivotIndex + 1 < combined.Length)
                    {
                        AnimationUtility.SetKeyRightTangentMode(curve, pivotIndex, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyLeftTangentMode(curve, pivotIndex + 1, AnimationUtility.TangentMode.Linear);
                    }

                    AnimationUtility.SetEditorCurve(clip, bindings[i], curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Rescales keyframe spacing so the shared last-key time across the selected bindings
        // lands exactly on playheadTime — a thin wrapper around ScaleKeyframeSpacing with a
        // factor derived from the playhead instead of a fixed 2x/0.5x.
        internal static void CompressToPlayhead(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings, float playheadTime)
        {
            var (anchor, lastTime) = ComputeSharedRange(orderedBindings, minKeyCount: 1);
            if (anchor == float.MaxValue || lastTime <= anchor || playheadTime <= anchor) return;

            float factor = (playheadTime - anchor) / (lastTime - anchor);
            ScaleKeyframeSpacing(orderedBindings, factor, roundUp: false);
        }

        // Property suffix after the last '.' (e.g. "m_LocalPosition.x" -> "x"). Scalar
        // properties with no dot (blendshape weight, single toggle) fall into one group
        // ("") and cascade together, matching the original single-binding behavior.
        static string AxisSuffix(string propertyName)
        {
            int dot = propertyName.LastIndexOf('.');
            return dot >= 0 ? propertyName.Substring(dot + 1) : string.Empty;
        }

        static float FirstKeyValue(AnimationClip clip, EditorCurveBinding binding)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            return curve != null && curve.length > 0 ? curve.keys[0].value : 0f;
        }

        static float LastKeyValue(AnimationClip clip, EditorCurveBinding binding)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            return curve != null && curve.length > 0 ? curve.keys[^1].value : 0f;
        }

        // Triangular crossfade, computed independently per property axis (x/y/z/etc, grouped
        // by AxisSuffix) so object transform bindings don't cross-cascade between axes.
        // Within a group, binding[i] peaks at its own evenly-spaced time slot across the
        // shared first/last key range, ramping from/to its neighbors' peak values. Peak
        // heights staircase from the group's first binding's first-key value to its last
        // binding's last-key value, evenly interpolated across the group. Order = caller-supplied.
        internal static void CascadeBindings(List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings)
        {
            if (orderedBindings.Count < 2) return;

            var (firstTime, lastTime) = ComputeSharedRange(orderedBindings, minKeyCount: 1);
            if (firstTime >= lastTime) return;

            float span = lastTime - firstTime;
            var touchedClips = new HashSet<AnimationClip>();

            foreach (var axisGroup in orderedBindings.GroupBy(entry => AxisSuffix(entry.binding.propertyName)))
            {
                var group = axisGroup.ToList();
                int count = group.Count;
                if (count < 2) continue;

                float startValue = FirstKeyValue(group[0].clip, group[0].binding);
                float endValue = LastKeyValue(group[^1].clip, group[^1].binding);

                var peakTimes = new float[count];
                var peakValues = new float[count];
                for (int i = 0; i < count; i++)
                {
                    peakTimes[i] = firstTime + span * i / (count - 1);
                    peakValues[i] = Mathf.Lerp(startValue, endValue, (float)i / (count - 1));
                }

                for (int i = 0; i < count; i++)
                {
                    var (clip, binding) = group[i];
                    if (touchedClips.Add(clip))
                        Undo.RegisterCompleteObjectUndo(clip, "Cascade Bindings");

                    var keys = new List<Keyframe>();
                    if (i > 0) keys.Add(new Keyframe(peakTimes[i - 1], peakValues[i - 1]));
                    keys.Add(new Keyframe(peakTimes[i], peakValues[i]));
                    if (i < count - 1) keys.Add(new Keyframe(peakTimes[i + 1], peakValues[i + 1]));

                    var curve = new AnimationCurve(keys.ToArray());
                    for (int k = 0; k < curve.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    }

                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Shifts every keyframe's value on the given bindings by a fixed offset.
        internal static void OffsetKeyframes(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings, float offset)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                Undo.RegisterCompleteObjectUndo(clip, "Offset Keyframes");
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++)
                        keys[k].value += offset;
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Same value offset as OffsetKeyframes, but expanded from the selected bindings to
        // every clip on the controller carrying a matching path/type/propertyName binding —
        // e.g. offsetting one clip's rotation.z also offsets every other clip's rotation.z.
        internal static void OffsetKeyframesAllClips(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings,
            UnityEditor.Animations.AnimatorController controller, float offset)
        {
            var targetBindings = orderedBindings.Select(entry => entry.binding).Distinct().ToList();

            var expanded = new List<(AnimationClip clip, EditorCurveBinding binding)>();
            foreach (var clip in AnimatorClipRemapper.CollectAllClips(controller))
            {
                var clipBindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var target in targetBindings)
                {
                    foreach (var candidate in clipBindings)
                    {
                        if (candidate.path == target.path && candidate.type == target.type
                            && candidate.propertyName == target.propertyName)
                        {
                            expanded.Add((clip, candidate));
                            break;
                        }
                    }
                }
            }

            OffsetKeyframes(expanded, offset);
        }

        // Adds an independent random offset in [-maxBound, maxBound] to each keyframe's value.
        internal static void JitterKeyframes(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings, float maxBound)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                Undo.RegisterCompleteObjectUndo(clip, "Jitter Keyframes");
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++)
                        keys[k].value += UnityEngine.Random.Range(-maxBound, maxBound);
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Same random-value-per-key jitter as JitterKeyframes, but restricted to the specific
        // (clip, binding, time) keys the caller selected in the dopesheet, instead of every key
        // on the binding.
        internal static void JitterSelectedKeyframes(
            List<(AnimationClip clip, EditorCurveBinding binding, float time)> selectedKeys, float maxBound)
        {
            foreach (var clipGroup in selectedKeys.GroupBy(entry => entry.clip))
            {
                var clip = clipGroup.Key;
                Undo.RegisterCompleteObjectUndo(clip, "Jitter Selected Keyframes");
                foreach (var bindingGroup in clipGroup.GroupBy(entry => entry.binding))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, bindingGroup.Key);
                    if (curve == null || curve.length == 0) continue;

                    var targetTimes = bindingGroup.Select(entry => entry.time).ToList();
                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (!targetTimes.Any(t => Mathf.Approximately(t, keys[k].time))) continue;
                        keys[k].value += UnityEngine.Random.Range(-maxBound, maxBound);
                    }
    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, bindingGroup.Key, curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }

        // Linearly rescales each binding's own value range into [newMin, newMax], independently
        // per binding — a flat curve (min == max) is left untouched since there's no range to scale.
        internal static void RemapKeyframeRange(
            List<(AnimationClip clip, EditorCurveBinding binding)> orderedBindings, float newMin, float newMax)
        {
            foreach (var (clip, bindings) in GroupByClip(orderedBindings))
            {
                Undo.RegisterCompleteObjectUndo(clip, "Remap Keyframe Range");
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    var keys = curve.keys;
                    float oldMin = keys.Min(key => key.value);
                    float oldMax = keys.Max(key => key.value);
                    if (Mathf.Approximately(oldMin, oldMax)) continue;

                    for (int k = 0; k < keys.Length; k++)
                        keys[k].value = newMin + (keys[k].value - oldMin) / (oldMax - oldMin) * (newMax - newMin);
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            InternalEditorUtility.RepaintAllViews();
        }
    }
}
#endif
