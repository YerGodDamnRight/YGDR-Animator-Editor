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
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorClipRemapper
    {
        internal struct ScanResult
        {
            internal (string segment, int count)[] brokenSegments;
            internal int                           totalBrokenCount;
        }

        internal static ScanResult ScanBrokenPaths(AnimatorController controller, GameObject avatarRoot)
        {
            if (avatarRoot == null) return default;

            var clips              = CollectAllClips(controller);
            var brokenPathsBySegment = new Dictionary<string, HashSet<string>>();
            var validPaths         = BuildValidPathSet(avatarRoot.transform);

            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    AccumulateBroken(binding.path, brokenPathsBySegment, validPaths);

                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    AccumulateBroken(binding.path, brokenPathsBySegment, validPaths);
            }

            if (brokenPathsBySegment.Count == 0)
                return default;

            var orderedSegments = brokenPathsBySegment
                .OrderByDescending(kvp => kvp.Value.Count)
                .ThenBy(kvp => kvp.Key, System.StringComparer.Ordinal)
                .Select(kvp => (kvp.Key, kvp.Value.Count))
                .ToArray();
            int totalBroken = brokenPathsBySegment.Values.Sum(set => set.Count);
            return new ScanResult
            {
                brokenSegments   = orderedSegments,
                totalBrokenCount = totalBroken
            };
        }

        static void AccumulateBroken(string path,
            Dictionary<string, HashSet<string>> brokenPathsBySegment,
            HashSet<string> validPaths)
        {
            if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains("//") || validPaths.Contains(path)) return;
            string brokenSegment = FindFirstBrokenSegment(path, validPaths);
            if (!brokenPathsBySegment.TryGetValue(brokenSegment, out var pathSet))
                brokenPathsBySegment[brokenSegment] = pathSet = new HashSet<string>();
            pathSet.Add(path);
        }

        // Walks path segments from root; returns the name of the first segment not found in the hierarchy.
        static string FindFirstBrokenSegment(string path, HashSet<string> validPaths)
        {
            var segments = path.Split('/');
            string prefix = "";
            for (int i = 0; i < segments.Length; i++)
            {
                prefix = i == 0 ? segments[0] : prefix + "/" + segments[i];
                if (!validPaths.Contains(prefix))
                    return segments[i];
            }
            return segments[segments.Length - 1];
        }

        // Remap all clips in controller: prefix path replacement wrapped in StartAssetEditing for batched reimport.
        // Note: AnimationUtility.SetEditorCurve edits are not reliably undoable via RecordObject — remap is effectively permanent.
        internal static void RemapAll(AnimatorController controller, string fromPath, string toPath)
        {
            fromPath = fromPath.TrimEnd('/');
            toPath   = toPath.TrimEnd('/');

            var clips = CollectAllClips(controller);
            if (clips.Length == 0) return;

            Undo.SetCurrentGroupName("Remap Clip Paths");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var clip in clips)
                    RemapClip(clip, fromPath, toPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Remap only clips whose instance IDs are in selectedIds.
        internal static void RemapSelectedClips(AnimatorController controller, HashSet<int> selectedIds, string fromPath, string toPath)
        {
            fromPath = fromPath.TrimEnd('/');
            toPath   = toPath.TrimEnd('/');

            var clips = CollectAllClips(controller).Where(c => selectedIds.Contains(c.GetInstanceID())).ToArray();
            if (clips.Length == 0) return;

            Undo.SetCurrentGroupName("Remap Selected Clip Paths");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var clip in clips)
                    RemapClip(clip, fromPath, toPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Apply arbitrary old→new path substitutions from changedPaths dict (used by auto-repath).
        internal static void RemapAllPaths(AnimatorController controller, Dictionary<string, string> changedPaths)
        {
            var clips = CollectAllClips(controller);
            if (clips.Length == 0) return;

            Undo.SetCurrentGroupName("Auto-Repath Animations");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var clip in clips)
                    RemapClipPaths(clip, changedPaths);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Returns true if any clip binding path appears as a key in changedPaths (pre-check before remapping).
        internal static bool AnyClipUsesChangedPaths(AnimatorController controller, Dictionary<string, string> changedPaths)
        {
            foreach (var clip in CollectAllClips(controller))
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (changedPaths.ContainsKey(binding.path)) return true;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    if (changedPaths.ContainsKey(binding.path)) return true;
            }
            return false;
        }

        static void RemapClip(AnimationClip clip, string fromPath, string toPath)
        {
            bool modified = false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!PathMatches(binding.path, fromPath)) continue;
                if (!modified) { Undo.RecordObject(clip, "Remap Clip Paths"); modified = true; }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = ReplacePath(binding.path, fromPath, toPath);
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!PathMatches(binding.path, fromPath)) continue;
                if (!modified) { Undo.RecordObject(clip, "Remap Clip Paths"); modified = true; }
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = ReplacePath(binding.path, fromPath, toPath);
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keys);
            }

            if (modified)
                EditorUtility.SetDirty(clip);
        }

        static void RemapClipPaths(AnimationClip clip, Dictionary<string, string> changedPaths)
        {
            bool modified = false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!changedPaths.TryGetValue(binding.path, out string newPath)) continue;
                if (!modified) { Undo.RecordObject(clip, "Auto-Repath Animations"); modified = true; }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!changedPaths.TryGetValue(binding.path, out string newPath)) continue;
                if (!modified) { Undo.RecordObject(clip, "Auto-Repath Animations"); modified = true; }
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keys);
            }

            if (modified)
                EditorUtility.SetDirty(clip);
        }

        // Segment-aware match: fromPath must align to "/" boundaries.
        // "Body" matches "Body", "Body/Arm", "Rig/Body", "Rig/Body/Arm" — never "SomeBody/Arm".
        static bool PathMatches(string path, string fromPath)
        {
            if (path == fromPath) return true;
            if (path.StartsWith(fromPath + "/")) return true;
            if (path.EndsWith("/" + fromPath)) return true;
            if (path.Contains("/" + fromPath + "/")) return true;
            return false;
        }

        static string ReplacePath(string path, string fromPath, string toPath)
        {
            if (path == fromPath) return toPath;
            var fromSegs  = fromPath.Split('/');
            var toSegs    = toPath.Split('/');
            var segments  = new System.Collections.Generic.List<string>(path.Split('/'));
            for (int i = 0; i <= segments.Count - fromSegs.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < fromSegs.Length; j++)
                    if (segments[i + j] != fromSegs[j]) { match = false; break; }
                if (!match) continue;
                segments.RemoveRange(i, fromSegs.Length);
                segments.InsertRange(i, toSegs);
                i += toSegs.Length - 1;
            }
            return string.Join("/", segments);
        }

        internal static HashSet<int> CollectBrokenClipIds(AnimatorController controller, GameObject avatarRoot)
        {
            if (avatarRoot == null) return new HashSet<int>();

            var result     = new HashSet<int>();
            var validPaths = BuildValidPathSet(avatarRoot.transform);

            foreach (var clip in CollectAllClips(controller))
            {
                bool hasBroken = AnimationUtility.GetCurveBindings(clip)
                    .Any(b => !string.IsNullOrEmpty(b.path) && !validPaths.Contains(b.path))
                 || AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .Any(b => !string.IsNullOrEmpty(b.path) && !validPaths.Contains(b.path));
                if (hasBroken)
                    result.Add(clip.GetInstanceID());
            }
            return result;
        }

        // One DFS traversal builds the full set of valid transform paths from root.
        static HashSet<string> BuildValidPathSet(Transform root)
        {
            var validPaths = new HashSet<string>();
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current != root)
                    validPaths.Add(AnimationUtility.CalculateTransformPath(current, root));
                foreach (Transform child in current)
                    stack.Push(child);
            }
            return validPaths;
        }

        internal static void RemapAapParameter(AnimatorController controller, string fromParamName, string toParamName)
        {
            foreach (var clip in CollectAllClips(controller))
                RemapAapInClip(clip, fromParamName, toParamName);
        }

        static void RemapAapInClip(AnimationClip clip, string fromParamName, string toParamName)
        {
            bool modified = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Animator) || binding.propertyName != fromParamName) continue;
                if (!modified) { Undo.RecordObject(clip, "Remap Parameter"); modified = true; }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.propertyName = toParamName;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            }
            if (modified)
                EditorUtility.SetDirty(clip);
        }

        internal static AnimationClip[] CollectAllClips(AnimatorController controller) =>
            controller.animationClips
                .Where(c => c != null && AssetDatabase.Contains(c))
                .Distinct()
                .ToArray();
    }
}
#endif
