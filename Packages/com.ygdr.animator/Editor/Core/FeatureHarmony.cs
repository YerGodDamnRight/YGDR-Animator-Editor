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
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class FeatureHarmony
    {
        internal const string ContextMenuId   = "com.ygdr.animatortools.contextmenu";
        internal const string NodeOverlayId   = "com.ygdr.animatortools.nodeoverlay";
        internal const string NodeColorId     = "com.ygdr.animatortools.nodecolor";
        internal const string TransitionId    = "com.ygdr.animatortools.transitionoverlay";
        internal const string GraphInteractId = "com.ygdr.animatortools.graphinteraction";
        internal const string GridBgId        = "com.ygdr.animatortools.gridbackground";
        internal const string LayerViewId     = "com.ygdr.animatortools.layerview";
        internal const string ParamViewId     = "com.ygdr.animatortools.paramview";
        internal const string BlendTreeId     = "com.ygdr.animatortools.blendtree";
        internal const string BottomBarId     = "com.ygdr.animatortools.bottombar";

        internal static readonly string[] AllFeatureIds =
        {
            ContextMenuId, NodeOverlayId, NodeColorId, TransitionId,
            GraphInteractId, GridBgId, LayerViewId, ParamViewId, BlendTreeId, BottomBarId,
        };

        static readonly Dictionary<string, Type[]> _featureClasses = new()
        {
            [ContextMenuId] = new[]
            {
                typeof(PatchStateNodeMenu),
                typeof(PatchStateMachineNodeMenu),
                typeof(PatchTransitionContextMenu),
                typeof(PatchClipMenuNesting),
                typeof(PatchClipMenuDoubleClickGuard),
            },
            [NodeOverlayId] = new[]
            {
                typeof(AnimatorStateNodeOverlayPatch),
                typeof(AnimatorEntryNodeOverlayPatch),
                typeof(AnimatorExitNodeOverlayPatch),
                typeof(AnimatorAnyStateNodeOverlayPatch),
                typeof(AnimatorSubSMNodeOverlayPatch),
                typeof(PatchStateNodeTitle),
            },
            [NodeColorId] = new[]
            {
                typeof(PatchNodeStyles),
            },
            [TransitionId] = new[]
            {
                typeof(PatchDrawEdge),
                typeof(PatchDrawArrows),
            },
            [GraphInteractId] = new[]
            {
                typeof(FrameDrawPatch),
                typeof(FrameInteractionPatch),
                typeof(AnimatorStateCreationPatch),
                typeof(AnimatorTransitionCreationPatch),
                typeof(PatchStateNodeClipSync),
                typeof(PatchEditAnimationClipGOContext),
                typeof(PatchAddStateDrop),
                typeof(PatchGraphInputHandler),
                typeof(PatchEdgeGUIDoEdges),
                typeof(PatchStateChainTransition),
                typeof(PatchTransitionCopyPaste),
                typeof(PatchCopySelectionToPasteboard),
                typeof(PatchDuplicateSmartNaming),
            },
            [GridBgId] = new[]
            {
                typeof(AnimatorGridBackgroundPatch),
            },
            [LayerViewId] = new[]
            {
                // Native bug fixes — layer view
                typeof(PatchLayerReorderSelection),
                typeof(PatchLayerF2Rename),
                typeof(PatchLayerListFocusHighlight),
                typeof(PatchLayerScrollReset),
                typeof(PatchLayerScrollRefocus),
                typeof(PatchLayerWeightDefault),
                typeof(PatchLayerCopyPaste),
                typeof(PatchLayerWDIndicator),
                typeof(PatchLayerCompact),
                typeof(PatchLayerCompactButton),
                typeof(PatchLayerCompactDraw),
                typeof(PatchLayerToolbar),
                typeof(PatchLayerRightClick),
            },
            [ParamViewId] = new[]
            {
                typeof(PatchParameterRenameUndo),
                typeof(PatchParameterF2Rename),
                typeof(PatchNewParameterScroll),
                typeof(PatchParameterRow),
                typeof(PatchParameterAddMenu),
                typeof(PatchParameterContextMenu),
                typeof(PatchParameterBudget),
            },
            [BlendTreeId] = new[]
            {
                typeof(PatchBlendTreeNodeGUI),
                typeof(PatchBlendTreeOnGraphGUI),
                typeof(PatchBlendTreeGetNodeStyle),
                typeof(PatchBlendTreeNodeTitle),
                typeof(PatchBlendTreeHandleNodeInput),
                typeof(PatchGenericMenuBlendTreeCopyPaste),
            },
            [BottomBarId] = new[]
            {
                typeof(PatchBottomBar),
            },
        };

        static readonly Dictionary<string, Harmony> _instances = new();

        // Whether a feature is currently patched (use this for UI display — reflects actual patch state)
        internal static bool IsEnabled(string featureId) => _instances.ContainsKey(featureId);

        internal static void SetEnabled(string featureId, bool enabled)
        {
            // Always persist user preference, even if patch state is already correct
            EditorPrefs.SetBool($"AnimatorTools.Feature.{featureId}", enabled);

            if (enabled)
            {
                if (_instances.ContainsKey(featureId)) return;
                // Layer 1: write crash guard flag; cleared next frame if no lockup
                EditorPrefs.SetBool($"AnimatorTools.PendingEnable.{featureId}", true);
                var harmony = new Harmony(featureId);
                _instances[featureId] = harmony;
                foreach (var type in _featureClasses[featureId])
                    harmony.CreateClassProcessor(type).Patch();
                // Clear pending flag next frame — proves patch survived without crash
                EditorApplication.delayCall += () => EditorPrefs.DeleteKey($"AnimatorTools.PendingEnable.{featureId}");
            }
            else
            {
                if (!_instances.ContainsKey(featureId)) return;
                var harmony = _instances[featureId];
                harmony.UnpatchAll(harmony.Id);  // pass ID — no-arg removes ALL patches system-wide
                _instances.Remove(featureId);
            }
        }

        internal static void UnpatchAll()
        {
            foreach (var featureId in _instances.Keys.ToArray())
            {
                var harmony = _instances[featureId];
                harmony.UnpatchAll(harmony.Id);
            }
            _instances.Clear();
        }

        // Layer 1: clears PendingEnable flags for all features — call one frame after patching
        internal static void ClearPendingFlags()
        {
            foreach (var featureId in AllFeatureIds)
                EditorPrefs.DeleteKey($"AnimatorTools.PendingEnable.{featureId}");
        }

        // Warn if another tool's transpiler conflicts on the same method
        internal static void WarnIfConflict(MethodBase method, string featureName)
        {
            if (method == null) return;
            var info = Harmony.GetPatchInfo(method);
            if (info == null) return;
            var foreign = info.Transpilers
                .Where(patch => !patch.owner.StartsWith("com.ygdr.animatortools"))
                .ToArray();
            if (foreign.Length > 0)
                Debug.LogWarning(
                    $"[AnimatorTools] {featureName}: conflicting transpiler from '{foreign[0].owner}' detected. " +
                    $"Disable this feature in AnimatorTools Compatibility settings if you see missing menu items or crashes.");
        }
    }
}
#endif
