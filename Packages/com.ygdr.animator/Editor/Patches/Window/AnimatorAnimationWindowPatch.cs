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
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
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

            if (Selection.activeObject is not AnimatorState selectedState) return;
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

            var stateProxy = new WindowPatchReflection.AnimationWindowStateProxy(__instance);
            if (stateProxy.ActiveRootGameObject == animatorGameObject) return;

            try
            {
                _editGameObjectArgs[0] = animatorGameObject; EditGameObjectMethod?.Invoke(__instance, _editGameObjectArgs);
                new WindowPatchReflection.AnimationWindowStateProxy(__instance).ActiveAnimationClip = animationClip;
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

    // Replaces the clip popup OnGUI with a searchable AdvancedDropdown backed by controller clips.
    [HarmonyPatch]
    internal static class PatchClipMenuAdvancedDropdown
    {
        internal const string CreateNewClipLabel = "Create New Clip...";

        class ClipCacheEntry { internal AnimatorController Controller; internal List<AnimationClip> Clips; }
        static readonly Dictionary<AnimationWindow, ClipCacheEntry> _clipCache = new();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.AnimationWindowClipPopupType, "OnGUI");

        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            if (!AnimatorDefaultSettings.Load().clipMenuNestingEnabled) return true;

            var popupState = WindowPatchReflection.AnimationWindowClipPopupStateField?.GetValue(__instance);
            if (popupState == null) return true;

            var animWindow = WindowPatchReflection.FindWindowOwningState(popupState);
            if (animWindow == null) return true;

            var stateProxy = WindowPatchReflection.AnimationWindowStateProxy.FromState(popupState);

            // A locked window keeps editing whatever GameObject it's locked to, even while the graph
            // editor has a different controller open elsewhere — so prefer that GameObject's controller
            // over the globally open one, letting a locked and unlocked window compare different controllers.
            var controller = (stateProxy.ActiveRootGameObject?.GetComponentInParent<Animator>(true)?.runtimeAnimatorController as AnimatorController)
                ?? WindowPatchReflection.GetOpenController();
            if (controller == null) return true;

            if (!_clipCache.TryGetValue(animWindow, out var cacheEntry) || cacheEntry.Controller != controller)
            {
                cacheEntry = new ClipCacheEntry { Controller = controller, Clips = CollectClips(controller) };
                _clipCache[animWindow] = cacheEntry;
            }
            var cachedClips = cacheEntry.Clips;

            var activeClip = stateProxy.ActiveAnimationClip;
            string label = activeClip != null ? activeClip.name : "[No Clip]";

            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, EditorStyles.toolbarPopup);
            if (EditorGUI.DropdownButton(rect, new GUIContent(label), FocusType.Passive, EditorStyles.toolbarPopup))
            {
                void SelectClip(AnimationClip clip)
                {
                    try { WindowPatchReflection.AnimationWindowEditAnimationClipMethod?.Invoke(animWindow, new object[] { clip }); }
                    catch (Exception e) { Debug.LogError($"[AnimatorTools] ClipMenuDropdown select: {e}"); }

                    // EditAnimationClip silently no-ops while the window is locked; force the write directly.
                    if (stateProxy.ActiveAnimationClip != clip) stateProxy.ActiveAnimationClip = clip;
                    animWindow.Repaint();
                }

                new ClipMenuDropdown(cachedClips, activeClip, SelectClip, () =>
                {
                    var newClip = CreateNewClipAsset(controller);
                    if (newClip == null) return;
                    InvalidateClipCache();
                    SelectClip(newClip);
                }).ShowDropdown(rect);
            }

            return false;
        }

        static PatchClipMenuAdvancedDropdown() => ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => InvalidateClipCache();

        internal static void InvalidateClipCache() => _clipCache.Clear();

        // In-memory only: reset every editor session, seeded from the controller's own folder.
        static string _lastClipCreationFolder;

        static AnimationClip CreateNewClipAsset(AnimatorController controller)
        {
            if (_lastClipCreationFolder == null || !AssetDatabase.IsValidFolder(_lastClipCreationFolder))
            {
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                _lastClipCreationFolder = string.IsNullOrEmpty(controllerPath)
                    ? "Assets" : System.IO.Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            }

            var path = EditorUtility.SaveFilePanelInProject("Create New Animation", "New Animation", "anim", "Create a new animation clip.", _lastClipCreationFolder);
            if (string.IsNullOrEmpty(path)) return null;

            _lastClipCreationFolder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');

            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();

            AddClipToBaseLayer(controller, clip);
            return clip;
        }

        static void AddClipToBaseLayer(AnimatorController controller, AnimationClip clip)
        {
            var stateMachine = controller.layers[0].stateMachine;
            var states       = stateMachine.states;
            var position     = states.Length > 0 ? states[^1].position + new Vector3(0f, 60f, 0f) : Vector3.zero;

            var newState = stateMachine.AddState(clip.name, position);
            Undo.RegisterCompleteObjectUndo(newState, "Create New Clip");
            newState.motion = clip;
            EditorUtility.SetDirty(newState);
        }

        static List<AnimationClip> CollectClips(AnimatorController controller)
        {
            var set = new HashSet<AnimationClip>();
            foreach (var layer in controller.layers)
                CollectFromSM(layer.stateMachine, set);
            return set.OrderBy(c => c.name).ToList();
        }

        static void CollectFromSM(AnimatorStateMachine sm, HashSet<AnimationClip> set)
        {
            foreach (var s in sm.states)        CollectFromMotion(s.state.motion, set);
            foreach (var s in sm.stateMachines) CollectFromSM(s.stateMachine, set);
        }

        static void CollectFromMotion(Motion motion, HashSet<AnimationClip> set)
        {
            if (motion is AnimationClip c && c != null) set.Add(c);
            else if (motion is BlendTree bt)
                foreach (var ch in bt.children) CollectFromMotion(ch.motion, set);
        }
    }

    internal class ClipMenuDropdown : YgdrAdvancedDropdownBase
    {
        readonly List<AnimationClip>   _clips;
        readonly AnimationClip         _currentClip;
        readonly Action<AnimationClip> _onSelected;
        readonly Action                _onCreateNew;
        ClipItem                       _currentItem;

        internal ClipMenuDropdown(List<AnimationClip> clips, AnimationClip currentClip, Action<AnimationClip> onSelected, Action onCreateNew)
            : base(new Vector2(220, 150))
        {
            _clips       = clips;
            _currentClip = currentClip;
            _onSelected  = onSelected;
            _onCreateNew = onCreateNew;
        }

        internal void ShowDropdown(Rect rect) => ShowCapped(rect, 500f, getPreselect: () => _currentItem);

        protected override AdvancedDropdownItem BuildRoot()
        {
            _currentItem = null;
            var root   = new AdvancedDropdownItem("Clips");
            var groups = new Dictionary<string, AdvancedDropdownItem>();

            var delimiter = AnimatorDefaultSettings.Load().clipMenuNestingDelimiter;
            foreach (var clip in _clips)
            {
                var parts  = clip.name.Replace(delimiter, '/').Split('/');
                var parent = root;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var key = string.Join("/", parts[..(i + 1)]);
                    if (!groups.TryGetValue(key, out var group))
                    {
                        group = new AdvancedDropdownItem(parts[i]);
                        parent.AddChild(group);
                        groups[key] = group;
                    }
                    parent = group;
                }

                var item = new ClipItem(parts[^1], clip);
                if (clip == _currentClip) _currentItem = item;
                parent.AddChild(item);
            }

            root.AddSeparator();
            root.AddChild(new ClipItem(PatchClipMenuAdvancedDropdown.CreateNewClipLabel, null));

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is not ClipItem ci) return;
            if (ci.Clip != null) _onSelected?.Invoke(ci.Clip);
            else _onCreateNew?.Invoke();
        }

        class ClipItem : AdvancedDropdownItem
        {
            internal readonly AnimationClip Clip;
            internal ClipItem(string name, AnimationClip clip) : base(name) => Clip = clip;
        }
    }

    internal static class HierarchyContextMenu
    {
        [MenuItem("GameObject/Find Animation Uses", false, 0)]
        static void FindAnimationUses()
        {
            var gameObject = Selection.activeGameObject;
            var animator = gameObject.GetComponentInParent<Animator>(true);
            var controller = (animator.runtimeAnimatorController as AnimatorController)
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
            if ((animator.runtimeAnimatorController as AnimatorController) != null) return true;
            var activeController = WindowPatchReflection.GetOpenController();
            if (activeController == null) return false;
#if VRC_SDK_VRCSDK3
            var descriptor = gameObject.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null) return false;
            return descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                .Any(layer => layer.animatorController as AnimatorController == activeController);
#else
            return false;
#endif
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var parts = new List<string>();
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
