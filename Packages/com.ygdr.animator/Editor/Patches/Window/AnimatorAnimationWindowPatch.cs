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
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace YGDR.Editor.Animation
{
    // Caches the last selected scene GO with an Animator.
    // On state node selection, calls EditAnimationClip (which PatchEditAnimationClipGOContext upgrades to GO context).
    [InitializeOnLoad]
    internal static class PatchStateNodeClipSync
    {
        static readonly object[] _editClipArgs = new object[1];

        internal static GameObject CachedAnimatorGameObject;

        static PatchStateNodeClipSync()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        static void OnSelectionChanged()
        {
            var activeGameObject = Selection.activeGameObject;
            if (activeGameObject != null
                && !EditorUtility.IsPersistent(activeGameObject)
                && activeGameObject.GetComponentInParent<Animator>(true) != null)
                CachedAnimatorGameObject = activeGameObject;

            if (Selection.activeObject is not UnityEditor.Animations.AnimatorState selectedState) return;
            if (selectedState.motion is not AnimationClip clip) return;

            var animationWindow = Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();
            if (animationWindow == null) return;

            try { _editClipArgs[0] = clip; WindowPatchReflection.AnimationWindowEditAnimationClipMethod?.Invoke(animationWindow, _editClipArgs); }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] Clip sync error: {e}"); }
        }
    }

    // Postfix on EditAnimationClip: upgrades clip-only context to GO context when a cached GO is available.
    // Covers both state node clicks (via PatchStateNodeClipSync) and blend tree leaf node clicks (Unity native).
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchEditAnimationClipGOContext
    {
        static readonly MethodInfo EditGameObjectMethod =
            AccessTools.Method(typeof(AnimationWindow), "EditGameObject", new Type[] { typeof(GameObject) });
        static readonly object[] _editGameObjectArgs = new object[1];

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => WindowPatchReflection.AnimationWindowEditAnimationClipMethod;

        [HarmonyPostfix]
        static void Postfix(AnimationWindow __instance, AnimationClip animationClip)
        {
            if (animationClip != null && Selection.objects.Contains(animationClip)) return;

            var animatorGameObject = GetOrFindAnimatorGameObject();
            if (animatorGameObject == null) return;

            var activeRootGameObject = Traverse.Create(__instance).Property("state").Property("activeRootGameObject").GetValue<GameObject>();
            if (activeRootGameObject == animatorGameObject) return;

            try
            {
                _editGameObjectArgs[0] = animatorGameObject; EditGameObjectMethod?.Invoke(__instance, _editGameObjectArgs);
                Traverse.Create(__instance).Property("state").Property("activeAnimationClip").SetValue(animationClip);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] GO context upgrade error: {e}");
            }
        }

        static GameObject GetOrFindAnimatorGameObject()
        {
            var openController = WindowPatchReflection.GetOpenController();
            if (openController == null) return null;

            var cachedAnimatorGameObject = PatchStateNodeClipSync.CachedAnimatorGameObject;
            if (cachedAnimatorGameObject != null)
            {
                var cachedAnimator = cachedAnimatorGameObject.GetComponentInParent<Animator>(true);
                if (cachedAnimator != null && cachedAnimator.runtimeAnimatorController == openController)
                    return cachedAnimatorGameObject;
                PatchStateNodeClipSync.CachedAnimatorGameObject = null;
            }

            foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>(true))
            {
                if (animator.runtimeAnimatorController == openController
                    && !EditorUtility.IsPersistent(animator.gameObject))
                {
                    PatchStateNodeClipSync.CachedAnimatorGameObject = animator.gameObject;
                    return PatchStateNodeClipSync.CachedAnimatorGameObject;
                }
            }
            return null;
        }
    }

    // Format clip dropdown with '.' → '/' so clips appear as nested submenus + inject "Create New Clip..."
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchClipMenuNesting
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.AnimationWindowClipPopupType, "GetClipMenuContent");

        [HarmonyPostfix]
        static void Postfix(ref GUIContent[] __result)
        {
            if (!AnimatorDefaultSettings.Load().clipMenuNestingEnabled) return;
            if (__result == null) return;

            // Detect Unity's separator + "Create New Clip..." tail (added when canCreateClips).
            // Must check BEFORE replacing dots — "Create New Clip..." contains dots that would
            // become slashes and corrupt the entry into a nested submenu path.
            bool unityAddedCreate = __result.Length >= 2
                && __result[^2] == GUIContent.none
                && __result[^1]?.text == "Create New Clip...";

            int clipCount = unityAddedCreate ? __result.Length - 2 : __result.Length;
            for (int i = 0; i < clipCount; i++)
            {
                if (__result[i]?.text is { Length: > 0 } text)
                    __result[i] = new GUIContent(text.Replace('.', '/'), __result[i].tooltip);
            }

            if (unityAddedCreate) return;

            var withCreate = new GUIContent[__result.Length + 2];
            __result.CopyTo(withCreate, 0);
            withCreate[__result.Length]     = GUIContent.none;
            withCreate[__result.Length + 1] = new GUIContent("Create New Clip...");
            __result = withCreate;
        }
    }

    // DoClipPopup has no clickCount guard — double-click fires DisplayClipMenu twice, causing nested menu
    [HarmonyPatch]
    internal static class PatchClipMenuDoubleClickGuard
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.AnimationWindowClipPopupType, "DisplayClipMenu");

        [HarmonyPrefix]
        static bool Prefix() =>
            !AnimatorDefaultSettings.Load().clipMenuNestingEnabled ||
            Event.current.type != EventType.MouseDown || Event.current.clickCount <= 1;
    }

    internal static class HierarchyContextMenu
    {
        [MenuItem("GameObject/Find Animation Uses", false, 0)]
        static void FindAnimationUses()
        {
            var gameObject = Selection.activeGameObject;
            var animator = gameObject.GetComponentInParent<Animator>(true);
            var controller = (animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController)
                ?? WindowPatchReflection.GetOpenController();
            var relativePath = GetRelativePath(animator.transform, gameObject.transform);
            if (relativePath == null) return;
            AnimatorFindUsageWindow.Open(relativePath, controller, gameObject.name);
        }

        [MenuItem("GameObject/Find Animation Uses", true)]
        static bool FindAnimationUsesValidate()
        {
            var gameObject = Selection.activeGameObject;
            if (gameObject == null) return false;
            var animator = gameObject.GetComponentInParent<Animator>(true);
            if (animator == null) return false;
            if ((animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController) != null) return true;
            var activeController = WindowPatchReflection.GetOpenController();
            if (activeController == null) return false;
#if VRC_SDK_VRCSDK3
            var descriptor = gameObject.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null) return false;
            return descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                .Any(layer => layer.animatorController as UnityEditor.Animations.AnimatorController == activeController);
#else
            return false;
#endif
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var parts = new System.Collections.Generic.List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return current == null ? null : string.Join("/", parts);
        }
    }
}
#endif
