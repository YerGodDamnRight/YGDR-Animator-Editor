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
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YGDR.Editor.Animation
{
    // Caches VRC expression parameter sync state for the last qualifying avatar + open controller.
    // Icons persist when clicking non-avatar objects. Rebuilds only when a different qualifying GO is selected.
    internal static class VRCSyncCache
    {
        // _cachedAvatarRoot is null in VRCFury path — use _cachedSelectedGO as the cache-warm guard.
        static GameObject _cachedAvatarRoot;
        static GameObject _cachedSelectedGO;
        static Dictionary<string, bool> _syncMap;
        static Dictionary<string, VRCExpressionParameters.ValueType> _valueTypeMap;
        static Dictionary<string, VRCExpressionParameters.Parameter> _paramMap;
        static bool _isVrcFurySource;
        static VRCExpressionParameters _vrcFuryParams;
        static AnimatorController _vrcFuryController;
        static VRCExpressionsMenu _vrcFuryMenu;

        // Resolved once per domain load — TypeByName does a full assembly scan.
        static bool _vrcfuryReflectionResolved;
        static Type _vrcfuryType;
        static MethodInfo _getAllFeaturesMethod;

        static VRCSyncCache()
        {
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            ObjectChangeEvents.changesPublished += OnObjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                ClearCache();
            // Re-run selection logic once edit-mode objects are live again — the animator
            // graph otherwise stays blank until the user manually reselects the avatar GO.
            else if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += OnSelectionChanged;
        }

        // Forces the cache current for the active selection before a read, since this
        // class's own Selection.selectionChanged subscription can run after callers'
        // (subscription order depends on which type touches VRCSyncCache first).
        internal static void EnsureSynced() => OnSelectionChanged();

        static void OnSelectionChanged()
        {
            var activeGO = Selection.activeGameObject;
            if (activeGO == null) return;
            if (ReferenceEquals(activeGO, _cachedSelectedGO)) return;

            // Only the exact selected GameObject qualifies — a child merely sitting under an avatar
            // or a FullController host must not touch the cache at all. Descriptor wins when the
            // same GO carries both (avatar root with its own FullController).
            var ownDescriptor = activeGO.GetComponent<VRCAvatarDescriptor>();
            if (ownDescriptor != null)
            {
                Rebuild(ownDescriptor, activeGO);
                EditorApplication.delayCall += RepaintAnimatorWindow;
                return;
            }

            if (FindFirstFullControllerData(activeGO, out var fullControllerController, out var fullControllerParams, out var fullControllerMenu))
                RebuildFromVrcFury(activeGO, fullControllerController, fullControllerParams, fullControllerMenu);
        }

        static void OnUndoRedo()
        {
            if (_cachedSelectedGO == null) return;

            if (_isVrcFurySource)
            {
                _syncMap = null;
                _valueTypeMap = null;
                if (_vrcFuryParams?.parameters != null)
                    BuildSyncMaps(_vrcFuryParams.parameters);
                return;
            }

            if (_cachedAvatarRoot == null) return;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor != null) Rebuild(avatarDescriptor, _cachedSelectedGO);
        }

        static void OnObjectChanged(ref ObjectChangeEventStream stream)
        {
            if (_cachedSelectedGO == null) return;

            int avatarParamsId = 0;
            if (!_isVrcFurySource && _cachedAvatarRoot != null)
            {
                var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
                if (avatarDescriptor != null && avatarDescriptor.expressionParameters != null)
                    avatarParamsId = avatarDescriptor.expressionParameters.GetInstanceID();
            }
            int vrcFuryParamsId = _isVrcFurySource && _vrcFuryParams != null ? _vrcFuryParams.GetInstanceID() : 0;

            if (avatarParamsId == 0 && vrcFuryParamsId == 0) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var changeEvent);
                if (changeEvent.instanceId != avatarParamsId && changeEvent.instanceId != vrcFuryParamsId) continue;

                if (_isVrcFurySource)
                {
                    _syncMap = null;
                    _valueTypeMap = null;
                    if (_vrcFuryParams?.parameters != null)
                        BuildSyncMaps(_vrcFuryParams.parameters);
                }
                else if (_cachedAvatarRoot != null)
                {
                    var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
                    if (avatarDescriptor != null) Rebuild(avatarDescriptor, _cachedSelectedGO);
                }
                return;
            }
        }

        static void Rebuild(VRCAvatarDescriptor avatarDescriptor, GameObject selectedGO)
        {
            try
            {
                var expressionParameters = avatarDescriptor.expressionParameters;
                if (expressionParameters?.parameters == null)
                {
                    // Descriptor has no expression params — keep existing sync data so params from
                    // the previous qualifying avatar remain visible in the animator window. Still
                    // push the controller below so the graph updates regardless.
                    _cachedSelectedGO = selectedGO;
                }
                else
                {
                    ClearCache();
                    _cachedAvatarRoot = avatarDescriptor.gameObject;
                    _cachedSelectedGO = selectedGO;
#if VRC_SDK_VRCSDK3
                    PatchParameterRow.InvalidateVrcComponentCache();
#endif
                    BuildSyncMaps(expressionParameters.parameters);
                }

                // Force-pushing the controller on every rebuild (like RebuildFromVrcFury does) keeps the
                // native window's graph in an eager-sync mode; without this, clip changes made through our
                // dropdown only actually commit to state.motion while the graph tab is visible.
                var animator = avatarDescriptor.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController is AnimatorController animatorController)
                    OpenControllerInAnimatorWindow(animatorController);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] VRCSyncCache rebuild error: {e}");
            }
        }

        static void RebuildFromVrcFury(GameObject selectedGO, AnimatorController controller, VRCExpressionParameters expressionParameters, VRCExpressionsMenu expressionsMenu)
        {
            try
            {
                ClearCache();
                _cachedSelectedGO = selectedGO;
                _isVrcFurySource = true;
                _vrcFuryController = controller;
                _vrcFuryParams = expressionParameters;
                _vrcFuryMenu = expressionsMenu;

                if (controller != null)
                    OpenControllerInAnimatorWindow(controller);

                if (expressionParameters?.parameters == null) return;
#if VRC_SDK_VRCSDK3
                PatchParameterRow.InvalidateVrcComponentCache();
#endif
                BuildSyncMaps(expressionParameters.parameters);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] VRCSyncCache VRCFury rebuild error: {e}");
            }
        }

        static void ClearCache()
        {
            _syncMap = null;
            _valueTypeMap = null;
            _paramMap = null;
            _cachedAvatarRoot = null;
            _cachedSelectedGO = null;
            _isVrcFurySource = false;
            _vrcFuryParams = null;
            _vrcFuryController = null;
            _vrcFuryMenu = null;
        }

        static void BuildSyncMaps(VRCExpressionParameters.Parameter[] parameters)
        {
            _syncMap = new Dictionary<string, bool>(parameters.Length);
            _valueTypeMap = new Dictionary<string, VRCExpressionParameters.ValueType>(parameters.Length);
            _paramMap = new Dictionary<string, VRCExpressionParameters.Parameter>(parameters.Length);
            foreach (var expressionParameter in parameters)
            {
                if (!string.IsNullOrEmpty(expressionParameter.name))
                {
                    _syncMap[expressionParameter.name] = expressionParameter.networkSynced;
                    _valueTypeMap[expressionParameter.name] = expressionParameter.valueType;
                    _paramMap[expressionParameter.name] = expressionParameter;
                }
            }
        }

        static void RepaintAnimatorWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
            if (windows.Length > 0)
                (windows[0] as EditorWindow)?.Repaint();
        }

        static void OpenControllerInAnimatorWindow(AnimatorController controller)
        {
            var windows = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
            if (windows.Length > 0 && WindowPatchReflection.AnimatorControllerProperty != null)
            {
                // ResetBreadCrumbs() (called by the animatorController setter below) only resets
                // selectedLayerIndex to 0 if it was already -1 — otherwise it silently keeps the
                // previous controller's layer index, leaving graph/zoom state stale until the user
                // clicks a node/layer manually. Forcing it to -1 first makes the setter's own reset
                // logic run against a valid trigger every time.
                WindowPatchReflection.SelectedLayerIndexProperty?.SetValue(windows[0], -1);
                WindowPatchReflection.AnimatorControllerProperty.SetValue(windows[0], controller);
                (windows[0] as EditorWindow)?.Repaint();
            }
            else
            {
                AssetDatabase.OpenAsset(controller);
            }
        }

        static bool EnsureVrcFuryReflection()
        {
            if (_vrcfuryReflectionResolved) return _vrcfuryType != null && _getAllFeaturesMethod != null;
            _vrcfuryReflectionResolved = true;
            _vrcfuryType = AccessTools.TypeByName("VF.Model.VRCFury");
            if (_vrcfuryType == null) return false;
            _getAllFeaturesMethod = AccessTools.Method(_vrcfuryType, "GetAllFeatures");
            return _getAllFeaturesMethod != null;
        }

        // Reads the first non-null objRef of type T from a list field (list → entry.entryField → objRef),
        // falling back to a direct field (feature.directField → objRef) for the legacy single-entry format.
        static T ExtractObjRef<T>(object feature, Type featureType, string listField, string entryField, string directField) where T : UnityEngine.Object
        {
            var entries = AccessTools.Field(featureType, listField)?.GetValue(feature) as System.Collections.IEnumerable;
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    var guidRef = AccessTools.Field(entry.GetType(), entryField)?.GetValue(entry);
                    if (guidRef == null) continue;
                    var result = AccessTools.Field(guidRef.GetType(), "objRef")?.GetValue(guidRef) as T;
                    if (result != null) return result;
                }
            }
            var directRef = AccessTools.Field(featureType, directField)?.GetValue(feature);
            if (directRef == null) return null;
            return AccessTools.Field(directRef.GetType(), "objRef")?.GetValue(directRef) as T;
        }

        // Extracts controller, params, and menu from the first FullController feature found directly on go.
        // Returns true if at least one FullController feature was found with a controller or params assigned.
        static bool FindFirstFullControllerData(GameObject go, out AnimatorController controller, out VRCExpressionParameters expressionParameters, out VRCExpressionsMenu expressionsMenu)
        {
            controller = null;
            expressionParameters = null;
            expressionsMenu = null;

            if (!EnsureVrcFuryReflection()) return false;

            var components = go.GetComponents(_vrcfuryType);
            if (components.Length == 0) return false;

            foreach (var component in components)
            {
                var features = _getAllFeaturesMethod.Invoke(component, null) as System.Collections.IEnumerable;
                if (features == null) continue;

                foreach (var feature in features)
                {
                    if (feature?.GetType().FullName != "VF.Model.Feature.FullController") continue;

                    var featureType = feature.GetType();
                    controller           = ExtractObjRef<AnimatorController>      (feature, featureType, "controllers", "controller",  "controller");
                    expressionParameters = ExtractObjRef<VRCExpressionParameters>  (feature, featureType, "prms",        "parameters",  "parameters");
                    expressionsMenu      = ExtractObjRef<VRCExpressionsMenu>        (feature, featureType, "menus",       "menu",        "menu");

                    if (controller != null || expressionParameters != null) return true;
                }
            }

            return false;
        }

        internal static bool TryGetSync(string paramName, out bool synced)
        {
            synced = false;
            if (_syncMap == null) return false;
            return _syncMap.TryGetValue(paramName, out synced);
        }

        internal static bool TryGetVrcValueType(string paramName, out VRCExpressionParameters.ValueType valueType)
        {
            valueType = default;
            if (_valueTypeMap == null) return false;
            return _valueTypeMap.TryGetValue(paramName, out valueType);
        }

        internal static bool TryGetParameter(string paramName, out VRCExpressionParameters.Parameter parameter)
        {
            parameter = null;
            if (_paramMap == null) return false;
            return _paramMap.TryGetValue(paramName, out parameter);
        }

        internal static GameObject GetVrcFuryComponentHost() =>
            _isVrcFurySource ? _cachedSelectedGO : null;

        internal static GameObject GetSearchRoot() =>
            _cachedAvatarRoot ?? _cachedSelectedGO;

        internal static List<VRCExpressionsMenu> GetVrcFuryExpressionsMenus()
        {
            var result = new List<VRCExpressionsMenu>();
            if (!_isVrcFurySource || _cachedSelectedGO == null) return result;
            if (!EnsureVrcFuryReflection()) return result;

            foreach (var component in _cachedSelectedGO.GetComponents(_vrcfuryType))
            {
                var features = _getAllFeaturesMethod.Invoke(component, null) as System.Collections.IEnumerable;
                if (features == null) continue;

                foreach (var feature in features)
                {
                    if (feature?.GetType().FullName != "VF.Model.Feature.FullController") continue;
                    var featureType = feature.GetType();

                    bool anyFromList = false;
                    var menuEntries = AccessTools.Field(featureType, "menus")?.GetValue(feature) as System.Collections.IEnumerable;
                    if (menuEntries != null)
                    {
                        foreach (var menuEntry in menuEntries)
                        {
                            if (menuEntry == null) continue;
                            var guidMenu = AccessTools.Field(menuEntry.GetType(), "menu")?.GetValue(menuEntry);
                            if (guidMenu == null) continue;
                            var expressionsMenu = AccessTools.Field(guidMenu.GetType(), "objRef")?.GetValue(guidMenu) as VRCExpressionsMenu;
                            if (expressionsMenu != null) { result.Add(expressionsMenu); anyFromList = true; }
                        }
                    }
                    if (!anyFromList)
                    {
                        var directGuidMenu = AccessTools.Field(featureType, "menu")?.GetValue(feature);
                        if (directGuidMenu != null)
                        {
                            var directMenu = AccessTools.Field(directGuidMenu.GetType(), "objRef")?.GetValue(directGuidMenu) as VRCExpressionsMenu;
                            if (directMenu != null) result.Add(directMenu);
                        }
                    }
                }
            }

            return result;
        }

        internal static VRCExpressionParameters GetExpressionParameters()
        {
            if (_cachedSelectedGO == null) return null;
            if (_isVrcFurySource) return _vrcFuryParams;
            if (_cachedAvatarRoot == null) return null;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            return avatarDescriptor != null ? avatarDescriptor.expressionParameters : null;
        }

        internal static VRCExpressionsMenu GetExpressionsMenu()
        {
            if (_cachedSelectedGO == null) return null;
            if (_isVrcFurySource) return _vrcFuryMenu;
            if (_cachedAvatarRoot == null) return null;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            return avatarDescriptor != null ? avatarDescriptor.expressionsMenu : null;
        }
    }
}
#endif
