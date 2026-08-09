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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Adds Double/Half Time keyframe-spacing ops to the Animation window's binding-name
    // context menu (AnimationWindowHierarchyGUI.GenerateMenu — same menu as "Remove Property").
    [HarmonyPatch]
    internal static class PatchKeyframeTimingMenu
    {
        // Property names vary by binding shape — raw serialized paths use "Array.data[2]",
        // display-style paths use "source 2" — so instead of matching one format, take the
        // last standalone number anywhere in the name as the component index.
        static readonly Regex TrailingIndexRegex = new(@"\d+", RegexOptions.Compiled);

        static int ExtractComponentIndex(EditorCurveBinding binding)
        {
            var matches = TrailingIndexRegex.Matches(binding.propertyName);
            return matches.Count > 0 ? int.Parse(matches[^1].Value) : int.MaxValue;
        }

        static MethodBase TargetMethod() =>
            WindowPatchReflection.AnimationWindowHierarchyNodeListType == null ? null :
            AccessTools.Method(
                WindowPatchReflection.AnimationWindowHierarchyGUIType,
                "GenerateMenu",
                new[] { WindowPatchReflection.AnimationWindowHierarchyNodeListType, typeof(bool) });

        static void Postfix(object __instance, object interactedNodes, ref GenericMenu __result)
        {
            try { AddMenuItems(__instance, interactedNodes, __result); }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] Keyframe timing menu error: {e}"); }
        }

        static void AddMenuItems(object hierarchyGUIInstance, object interactedNodes, GenericMenu menu)
        {
            if (WindowPatchReflection.AnimationWindowHierarchyNodeCurvesField == null
                || WindowPatchReflection.AnimationWindowCurveClipProperty == null
                || WindowPatchReflection.AnimationWindowCurveBindingProperty == null) return;
            if (menu == null || interactedNodes is not IEnumerable nodes) return;

            var orderedBindings = new List<(AnimationClip clip, EditorCurveBinding binding)>();
            var seen = new HashSet<(AnimationClip clip, EditorCurveBinding binding)>();
            foreach (var node in nodes)
            {
                if (WindowPatchReflection.AnimationWindowHierarchyNodeCurvesField.GetValue(node) is not IEnumerable curves) continue;
                foreach (var curveObj in curves)
                {
                    if (WindowPatchReflection.AnimationWindowCurveClipProperty.GetValue(curveObj) is not AnimationClip clip) continue;
                    var binding = (EditorCurveBinding)WindowPatchReflection.AnimationWindowCurveBindingProperty.GetValue(curveObj);
                    if (seen.Add((clip, binding)))
                        orderedBindings.Add((clip, binding));
                }
            }
            if (orderedBindings.Count == 0) return;

            var orderedByComponentIndex = orderedBindings
                .OrderBy(entry => ExtractComponentIndex(entry.binding))
                .ToList();

            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.double_time")), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 2f, roundUp: false),
                orderedBindings);
            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.half_time_floor")), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 0.5f, roundUp: false),
                orderedBindings);
            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.half_time_ceiling")), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 0.5f, roundUp: true),
                orderedBindings);

            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.reverse")), false,
                static data => AnimatorKeyframeTimingOps.ReverseKeyframes(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                orderedBindings);

            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.ping_pong")), false,
                static data => AnimatorKeyframeTimingOps.PingPongKeyframes(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                orderedBindings);

            var playheadLabel = new GUIContent(L10n.Get("keyframe_menu.compress_to_playhead"));
            if (WindowPatchReflection.AnimationWindowHierarchyGUIStateProperty != null
                && WindowPatchReflection.CurrentTimeProperty != null
                && WindowPatchReflection.AnimationWindowHierarchyGUIStateProperty.GetValue(hierarchyGUIInstance) is object state)
            {
                float playheadTime = (float)WindowPatchReflection.CurrentTimeProperty.GetValue(state);
                menu.AddItem(playheadLabel, false,
                    static data =>
                    {
                        var (bindings, time) = ((List<(AnimationClip clip, EditorCurveBinding binding)>, float))data;
                        AnimatorKeyframeTimingOps.CompressToPlayhead(bindings, time);
                    },
                    (orderedBindings, playheadTime));
            }
            else
            {
                menu.AddDisabledItem(playheadLabel);
            }

            string cascadeRoot = L10n.Get("keyframe_menu.cascade_bindings");
            var componentIndexLabel = new GUIContent($"{cascadeRoot}/{L10n.Get("keyframe_menu.cascade_by_component_index")}");
            var selectionOrderLabel = new GUIContent($"{cascadeRoot}/{L10n.Get("keyframe_menu.cascade_by_selection_order")}");
            if (orderedBindings.Count >= 2)
            {
                menu.AddItem(componentIndexLabel, false,
                    static data => AnimatorKeyframeTimingOps.CascadeBindings(
                        (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                    orderedByComponentIndex);
                // Unaltered order — matches the order rows were clicked/shift-selected in, so
                // this is the manual-control variant: click rows in the sequence you want.
                menu.AddItem(selectionOrderLabel, false,
                    static data => AnimatorKeyframeTimingOps.CascadeBindings(
                        (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                    orderedBindings);
            }
            else
            {
                menu.AddDisabledItem(componentIndexLabel);
                menu.AddDisabledItem(selectionOrderLabel);
            }
        }
    }
}
#endif
