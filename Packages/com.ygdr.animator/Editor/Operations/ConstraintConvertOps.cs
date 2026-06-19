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
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Animations;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
#endif

namespace YGDR.Editor.Animation
{
    internal static class ConstraintConvertOps
    {
        internal static void Convert(Component sourceConstraint, Type targetType)
        {
            var gameObject = sourceConstraint.gameObject;

            if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                EditorUtility.DisplayDialog("Convert Constraint",
                    "Cannot convert on a prefab instance. Open the prefab asset first.", "OK");
                return;
            }

            var animator = gameObject.GetComponentInParent<Animator>();
            if (animator == null)
            {
                EditorUtility.DisplayDialog("Convert Constraint",
                    "No Animator found in parents. Cannot remap animation clips.", "OK");
                return;
            }

            string targetPath = AnimationUtility.CalculateTransformPath(
                gameObject.transform, animator.transform);

            Undo.SetCurrentGroupName("Convert Constraint");
            int undoGroup = Undo.GetCurrentGroup();

            Type sourceType = sourceConstraint.GetType();

#if VRC_SDK_VRCSDK3
            if (sourceConstraint is VRCConstraintBase vrcSource)
                ConvertVRC(vrcSource, targetType, gameObject);
            else
#endif
                ConvertUnity(sourceConstraint, targetType, gameObject);

            RemapClips(animator, sourceType, targetType, targetPath);
            Undo.CollapseUndoOperations(undoGroup);
        }

        // ── VRC ───────────────────────────────────────────────────────────────

#if VRC_SDK_VRCSDK3
        static void ConvertVRC(VRCConstraintBase source, Type targetType, GameObject gameObject)
        {
            bool isActive = source.IsActive;
            float globalWeight = source.GlobalWeight;
            Transform targetTransform = source.TargetTransform;
            bool solveInLocalSpace = source.SolveInLocalSpace;
            bool freezeToWorld = source.FreezeToWorld;
            bool rebakeOffsetsWhenUnfrozen = source.RebakeOffsetsWhenUnfrozen;
            bool locked = source.Locked;

            var snapshotSources = new List<VRCConstraintSource>();
            for (int i = 0; i < source.Sources.Count; i++)
                snapshotSources.Add(source.Sources[i]);

            Vector3 positionAtRest = Vector3.zero;
            bool affectsPositionX = true, affectsPositionY = true, affectsPositionZ = true;
            Vector3 rotationAtRest = Vector3.zero;
            bool affectsRotationX = true, affectsRotationY = true, affectsRotationZ = true;

            if (source is VRCPositionConstraint posSource)
            {
                positionAtRest = posSource.PositionAtRest;
                affectsPositionX = posSource.AffectsPositionX;
                affectsPositionY = posSource.AffectsPositionY;
                affectsPositionZ = posSource.AffectsPositionZ;
            }
            else if (source is VRCRotationConstraint rotSource)
            {
                rotationAtRest = rotSource.RotationAtRest;
                affectsRotationX = rotSource.AffectsRotationX;
                affectsRotationY = rotSource.AffectsRotationY;
                affectsRotationZ = rotSource.AffectsRotationZ;
            }
            else if (source is VRCParentConstraint parentSource)
            {
                positionAtRest = parentSource.PositionAtRest;
                affectsPositionX = parentSource.AffectsPositionX;
                affectsPositionY = parentSource.AffectsPositionY;
                affectsPositionZ = parentSource.AffectsPositionZ;
                rotationAtRest = parentSource.RotationAtRest;
                affectsRotationX = parentSource.AffectsRotationX;
                affectsRotationY = parentSource.AffectsRotationY;
                affectsRotationZ = parentSource.AffectsRotationZ;
            }

            int componentIndex = GetComponentIndex(gameObject, source);
            Undo.RegisterCompleteObjectUndo(gameObject, "Convert Constraint");
            Undo.DestroyObjectImmediate(source);

            var newComp = (VRCConstraintBase)Undo.AddComponent(gameObject, targetType);
            newComp.IsActive = isActive;
            newComp.GlobalWeight = globalWeight;
            newComp.TargetTransform = targetTransform;
            newComp.SolveInLocalSpace = solveInLocalSpace;
            newComp.FreezeToWorld = freezeToWorld;
            newComp.RebakeOffsetsWhenUnfrozen = rebakeOffsetsWhenUnfrozen;
            newComp.Locked = locked;

            newComp.Sources.Clear();
            foreach (var constraintSource in snapshotSources)
                newComp.Sources.Add(constraintSource);

            if (newComp is VRCPositionConstraint newPos)
            {
                newPos.PositionAtRest = positionAtRest;
                newPos.AffectsPositionX = affectsPositionX;
                newPos.AffectsPositionY = affectsPositionY;
                newPos.AffectsPositionZ = affectsPositionZ;
            }
            else if (newComp is VRCRotationConstraint newRot)
            {
                newRot.RotationAtRest = rotationAtRest;
                newRot.AffectsRotationX = affectsRotationX;
                newRot.AffectsRotationY = affectsRotationY;
                newRot.AffectsRotationZ = affectsRotationZ;
            }
            else if (newComp is VRCParentConstraint newParent)
            {
                newParent.PositionAtRest = positionAtRest;
                newParent.AffectsPositionX = affectsPositionX;
                newParent.AffectsPositionY = affectsPositionY;
                newParent.AffectsPositionZ = affectsPositionZ;
                newParent.RotationAtRest = rotationAtRest;
                newParent.AffectsRotationX = affectsRotationX;
                newParent.AffectsRotationY = affectsRotationY;
                newParent.AffectsRotationZ = affectsRotationZ;
            }

            RestoreComponentOrder(gameObject, newComp, componentIndex);
            EditorUtility.SetDirty(gameObject);
        }

        // ── Unity ─────────────────────────────────────────────────────────────
#endif

        static void ConvertUnity(Component source, Type targetType, GameObject gameObject)
        {
            var unitySource = (IConstraint)source;

            float weight = unitySource.weight;
            bool constraintActive = unitySource.constraintActive;
            bool locked = unitySource.locked;

            var snapshotSources = new List<ConstraintSource>();
            unitySource.GetSources(snapshotSources);

            Vector3 translationAtRest = Vector3.zero;
            Vector3 rotationAtRest = Vector3.zero;
            Axis translationAxis = Axis.X | Axis.Y | Axis.Z;
            Axis rotationAxis = Axis.X | Axis.Y | Axis.Z;
            Vector3 translationOffset = Vector3.zero;
            Vector3 rotationOffset = Vector3.zero;
            Vector3[] translationOffsets = null;
            Vector3[] rotationOffsets = null;

            if (source is PositionConstraint posSource)
            {
                translationAtRest = posSource.translationAtRest;
                translationOffset = posSource.translationOffset;
                translationAxis = posSource.translationAxis;
            }
            else if (source is RotationConstraint rotSource)
            {
                rotationAtRest = rotSource.rotationAtRest;
                rotationOffset = rotSource.rotationOffset;
                rotationAxis = rotSource.rotationAxis;
            }
            else if (source is ParentConstraint parentSource)
            {
                translationAtRest = parentSource.translationAtRest;
                rotationAtRest = parentSource.rotationAtRest;
                translationAxis = parentSource.translationAxis;
                rotationAxis = parentSource.rotationAxis;
                translationOffsets = parentSource.translationOffsets;
                rotationOffsets = parentSource.rotationOffsets;
            }

            int componentIndex = GetComponentIndex(gameObject, source);
            Undo.RegisterCompleteObjectUndo(gameObject, "Convert Constraint");
            Undo.DestroyObjectImmediate(source);

            var newUnity = (IConstraint)Undo.AddComponent(gameObject, targetType);
            newUnity.weight = weight;
            newUnity.constraintActive = constraintActive;
            newUnity.locked = locked;
            newUnity.SetSources(snapshotSources);

            if (newUnity is PositionConstraint newPos)
            {
                newPos.translationAtRest = translationAtRest;
                newPos.translationOffset = translationOffset;
                newPos.translationAxis = translationAxis;
            }
            else if (newUnity is RotationConstraint newRot)
            {
                newRot.rotationAtRest = rotationAtRest;
                newRot.rotationOffset = rotationOffset;
                newRot.rotationAxis = rotationAxis;
            }
            else if (newUnity is ParentConstraint newParent)
            {
                newParent.translationAtRest = translationAtRest;
                newParent.rotationAtRest = rotationAtRest;
                newParent.translationAxis = translationAxis;
                newParent.rotationAxis = rotationAxis;
                if (translationOffsets != null) newParent.translationOffsets = translationOffsets;
                if (rotationOffsets != null) newParent.rotationOffsets = rotationOffsets;
            }

            RestoreComponentOrder(gameObject, (Component)newUnity, componentIndex);
            EditorUtility.SetDirty(gameObject);
        }

        // ── Component order ───────────────────────────────────────────────────

        static int GetComponentIndex(GameObject gameObject, Component component)
        {
            var allComponents = gameObject.GetComponents<Component>();
            return Array.IndexOf(allComponents, component);
        }

        static void RestoreComponentOrder(GameObject gameObject, Component newComponent, int targetIndex)
        {
            var allComponents = gameObject.GetComponents<Component>();
            int currentIndex = Array.IndexOf(allComponents, newComponent);
            int movesNeeded = currentIndex - targetIndex;
            for (int i = 0; i < movesNeeded; i++)
                ComponentUtility.MoveComponentUp(newComponent);
        }

        // ── Clip remap ────────────────────────────────────────────────────────

        static void RemapClips(Animator animator, Type sourceType, Type targetType, string targetPath)
        {
            // Collect all controllers: Animator + VRCAvatarDescriptor layers
            var controllers = new List<RuntimeAnimatorController>();
            if (animator.runtimeAnimatorController != null)
                controllers.Add(animator.runtimeAnimatorController);

#if VRC_SDK_VRCSDK3
            var descriptor = animator.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                foreach (var layer in descriptor.baseAnimationLayers)
                    if (!layer.isDefault && layer.animatorController != null)
                        controllers.Add(layer.animatorController);
                foreach (var layer in descriptor.specialAnimationLayers)
                    if (!layer.isDefault && layer.animatorController != null)
                        controllers.Add(layer.animatorController);
            }
#endif

            if (controllers.Count == 0)
            {
                Debug.LogWarning("[AnimatorTools] Convert Constraint: No controllers found, skipping clip remap.");
                return;
            }

            // Deduplicate clips across all controllers
            var seen = new HashSet<int>();
            var clips = new List<AnimationClip>();
            foreach (var controller in controllers)
                foreach (var clip in controller.animationClips)
                    if (clip != null && seen.Add(clip.GetInstanceID()))
                        clips.Add(clip);

            var skippedReadOnly = new List<string>();
            try
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Converting Constraint",
                        $"Scanning clip {i + 1}/{clips.Count}", (float)i / clips.Count);

                    var clip = clips[i];
                    if ((clip.hideFlags & HideFlags.NotEditable) != 0)
                    {
                        skippedReadOnly.Add(clip.name);
                        continue;
                    }

                    RemapBindings(clip, sourceType, targetType, targetPath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            if (skippedReadOnly.Count > 0)
                Debug.LogWarning($"[AnimatorTools] Convert Constraint: skipped {skippedReadOnly.Count} read-only clips (FBX/embedded):\n"
                    + string.Join("\n", skippedReadOnly));
        }

        static void RemapBindings(AnimationClip clip, Type sourceType, Type targetType, string targetPath)
        {
            var (prefixes, substrings) = GetDiscardSets(sourceType, targetType);
            bool dirty = false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != sourceType || binding.path != targetPath) continue;
                Undo.RegisterCompleteObjectUndo(clip, "Remap Constraint Binding");
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                if (ShouldDiscard(binding.propertyName, prefixes, substrings)) { dirty = true; continue; }
                var newBinding = binding;
                newBinding.type = targetType;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                dirty = true;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != sourceType || binding.path != targetPath) continue;
                Undo.RegisterCompleteObjectUndo(clip, "Remap Constraint Binding");
                var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                if (ShouldDiscard(binding.propertyName, prefixes, substrings)) { dirty = true; continue; }
                var newBinding = binding;
                newBinding.type = targetType;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, frames);
                dirty = true;
            }

            if (dirty) EditorUtility.SetDirty(clip);
        }

        // prefixes: checked with StartsWith (top-level fields)
        // substrings: checked with Contains (nested per-source fields e.g. Sources.Array.data[0].ParentPositionOffset.x)
        static bool ShouldDiscard(string propertyName, HashSet<string> prefixes, HashSet<string> substrings)
        {
            foreach (var prefix in prefixes)
                if (propertyName.StartsWith(prefix, StringComparison.Ordinal)) return true;
            foreach (var sub in substrings)
                if (propertyName.Contains(sub)) return true;
            return false;
        }

        // Returns (prefixes, substrings) to discard for a given conversion pair.
        // VRC: bare C# field names. Unity: m_-prefixed C++ serialized names (verify if wrong).
        static (HashSet<string> prefixes, HashSet<string> substrings) GetDiscardSets(Type sourceType, Type targetType)
        {
#if VRC_SDK_VRCSDK3
            // VRC family
            if (sourceType == typeof(VRCPositionConstraint) && targetType == typeof(VRCRotationConstraint))
                return (new HashSet<string> { "PositionOffset", "PositionAtRest", "AffectsPositionX", "AffectsPositionY", "AffectsPositionZ" }, Empty);

            if (sourceType == typeof(VRCPositionConstraint) && targetType == typeof(VRCParentConstraint))
                return (new HashSet<string> { "PositionOffset" }, Empty); // Parent has no single PositionOffset

            if (sourceType == typeof(VRCRotationConstraint) && targetType == typeof(VRCPositionConstraint))
                return (new HashSet<string> { "RotationOffset", "RotationAtRest", "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ" }, Empty);

            if (sourceType == typeof(VRCRotationConstraint) && targetType == typeof(VRCParentConstraint))
                return (new HashSet<string> { "RotationOffset" }, Empty); // Parent has no single RotationOffset

            if (sourceType == typeof(VRCParentConstraint) && targetType == typeof(VRCPositionConstraint))
                return (new HashSet<string> { "RotationAtRest", "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ" },
                        new HashSet<string> { "ParentPositionOffset", "ParentRotationOffset" });

            if (sourceType == typeof(VRCParentConstraint) && targetType == typeof(VRCRotationConstraint))
                return (new HashSet<string> { "PositionAtRest", "AffectsPositionX", "AffectsPositionY", "AffectsPositionZ" },
                        new HashSet<string> { "ParentPositionOffset", "ParentRotationOffset" });
#endif

            // Unity family
            if (sourceType == typeof(PositionConstraint) && targetType == typeof(RotationConstraint))
                return (new HashSet<string> { "m_TranslationOffset", "m_TranslationAtRest", "m_AffectTranslationX", "m_AffectTranslationY", "m_AffectTranslationZ" }, Empty);

            if (sourceType == typeof(PositionConstraint) && targetType == typeof(ParentConstraint))
                return (new HashSet<string> { "m_TranslationOffset" }, Empty);

            if (sourceType == typeof(RotationConstraint) && targetType == typeof(PositionConstraint))
                return (new HashSet<string> { "m_RotationOffset", "m_RotationAtRest", "m_AffectRotationX", "m_AffectRotationY", "m_AffectRotationZ" }, Empty);

            if (sourceType == typeof(RotationConstraint) && targetType == typeof(ParentConstraint))
                return (new HashSet<string> { "m_RotationOffset" }, Empty);

            if (sourceType == typeof(ParentConstraint) && targetType == typeof(PositionConstraint))
                return (new HashSet<string> { "m_RotationAtRest", "m_AffectRotationX", "m_AffectRotationY", "m_AffectRotationZ", "m_RotationOffsets" }, Empty);

            if (sourceType == typeof(ParentConstraint) && targetType == typeof(RotationConstraint))
                return (new HashSet<string> { "m_TranslationAtRest", "m_AffectTranslationX", "m_AffectTranslationY", "m_AffectTranslationZ", "m_TranslationOffsets" }, Empty);

            return (Empty, Empty);
        }

        static readonly HashSet<string> Empty = new HashSet<string>();
    }
}
#endif
