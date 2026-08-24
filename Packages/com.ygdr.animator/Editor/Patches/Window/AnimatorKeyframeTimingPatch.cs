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
    // Small dropdown floatfield for keyframe offset entry — appears at the mouse position
    // where the menu item was clicked, applies on Enter, cancels on Escape or focus loss.
    internal class KeyframeOffsetPopup : EditorWindow
    {
        static readonly Vector2 Size = new(140, 22);

        Action<float> _onApply;
        float _value;
        bool _focused;

        internal static void Show(Vector2 screenPosition, Action<float> onApply)
        {
            var window = CreateInstance<KeyframeOffsetPopup>();
            window._onApply = onApply;
            window.ShowAsDropDown(new Rect(screenPosition, Vector2.zero), Size);
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { Apply(); e.Use(); return; }
                if (e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
            }

            GUI.SetNextControlName("KeyframeOffsetField");
            _value = EditorGUILayout.FloatField(_value);

            if (!_focused)
            {
                EditorGUI.FocusTextInControl("KeyframeOffsetField");
                _focused = true;
            }
        }

        void Apply()
        {
            _onApply?.Invoke(_value);
            Close();
        }
    }

    // Two-field dropdown (Min / Max) for Remap Range entry — Tab cycles fields, Enter applies
    // both regardless of which field has focus, Escape or focus loss cancels.
    internal class KeyframeRemapPopup : EditorWindow
    {
        static readonly Vector2 Size = new(160, 22);

        Action<float, float> _onApply;
        float _min;
        float _max = 1f;
        bool _focused;

        internal static void Show(Vector2 screenPosition, Action<float, float> onApply)
        {
            var window = CreateInstance<KeyframeRemapPopup>();
            window._onApply = onApply;
            window.ShowAsDropDown(new Rect(screenPosition, Vector2.zero), Size);
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { Apply(); e.Use(); return; }
                if (e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("KeyframeRemapMin");
            _min = EditorGUILayout.FloatField(_min, GUILayout.Width(70));
            GUI.SetNextControlName("KeyframeRemapMax");
            _max = EditorGUILayout.FloatField(_max, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            if (!_focused)
            {
                EditorGUI.FocusTextInControl("KeyframeRemapMin");
                _focused = true;
            }
        }

        void Apply()
        {
            _onApply?.Invoke(_min, _max);
            Close();
        }
    }

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

            string timingRoot = L10n.Get("keyframe_menu.timing");
            menu.AddItem(new GUIContent($"{timingRoot}/{L10n.Get("keyframe_menu.double_time")}"), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 2f, roundUp: false),
                orderedBindings);
            menu.AddItem(new GUIContent($"{timingRoot}/{L10n.Get("keyframe_menu.half_time_floor")}"), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 0.5f, roundUp: false),
                orderedBindings);
            menu.AddItem(new GUIContent($"{timingRoot}/{L10n.Get("keyframe_menu.half_time_ceiling")}"), false,
                static data => AnimatorKeyframeTimingOps.ScaleKeyframeSpacing(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data, 0.5f, roundUp: true),
                orderedBindings);

            var playheadLabel = new GUIContent($"{timingRoot}/{L10n.Get("keyframe_menu.compress_to_playhead")}");
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

            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.reverse")), false,
                static data => AnimatorKeyframeTimingOps.ReverseKeyframes(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                orderedBindings);

            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.ping_pong")), false,
                static data => AnimatorKeyframeTimingOps.PingPongKeyframes(
                    (List<(AnimationClip clip, EditorCurveBinding binding)>)data),
                orderedBindings);

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

            var mouseScreenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);

            string offsetRoot = L10n.Get("keyframe_menu.offset");
            menu.AddItem(new GUIContent($"{offsetRoot}/{L10n.Get("keyframe_menu.offset_selected_bindings")}"), false,
                static data =>
                {
                    var (bindings, screenPos) = ((List<(AnimationClip clip, EditorCurveBinding binding)>, Vector2))data;
                    KeyframeOffsetPopup.Show(screenPos,
                        offset => AnimatorKeyframeTimingOps.OffsetKeyframes(bindings, offset));
                },
                (orderedBindings, mouseScreenPos));

            var offsetAllLabel = new GUIContent($"{offsetRoot}/{L10n.Get("keyframe_menu.offset_all_clips")}");
            var controller = WindowPatchReflection.GetOpenController();
            if (controller != null)
            {
                menu.AddItem(offsetAllLabel, false,
                    static data =>
                    {
                        var (bindings, ctrl, screenPos) = ((List<(AnimationClip clip, EditorCurveBinding binding)>,
                            UnityEditor.Animations.AnimatorController, Vector2))data;
                        KeyframeOffsetPopup.Show(screenPos,
                            offset => AnimatorKeyframeTimingOps.OffsetKeyframesAllClips(bindings, ctrl, offset));
                    },
                    (orderedBindings, controller, mouseScreenPos));
            }
            else
            {
                menu.AddDisabledItem(offsetAllLabel);
            }

            string jitterRoot = L10n.Get("keyframe_menu.jitter");

            var jitterSelectedLabel = new GUIContent($"{jitterRoot}/{L10n.Get("keyframe_menu.jitter_selected_keyframes")}");
            var selectedKeyframes = GetSelectedKeyframes(hierarchyGUIInstance);
            if (selectedKeyframes.Count > 0)
            {
                menu.AddItem(jitterSelectedLabel, false,
                    static data =>
                    {
                        var (selected, screenPos) = ((List<(AnimationClip clip, EditorCurveBinding binding, float time)>, Vector2))data;
                        KeyframeOffsetPopup.Show(screenPos,
                            maxBound => AnimatorKeyframeTimingOps.JitterSelectedKeyframes(selected, maxBound));
                    },
                    (selectedKeyframes, mouseScreenPos));
            }
            else
            {
                menu.AddDisabledItem(jitterSelectedLabel);
            }

            menu.AddItem(new GUIContent($"{jitterRoot}/{L10n.Get("keyframe_menu.jitter_all_keyframes")}"), false,
                static data =>
                {
                    var (bindings, screenPos) = ((List<(AnimationClip clip, EditorCurveBinding binding)>, Vector2))data;
                    KeyframeOffsetPopup.Show(screenPos,
                        maxBound => AnimatorKeyframeTimingOps.JitterKeyframes(bindings, maxBound));
                },
                (orderedBindings, mouseScreenPos));

            menu.AddItem(new GUIContent(L10n.Get("keyframe_menu.remap_range")), false,
                static data =>
                {
                    var (bindings, screenPos) = ((List<(AnimationClip clip, EditorCurveBinding binding)>, Vector2))data;
                    KeyframeRemapPopup.Show(screenPos,
                        (newMin, newMax) => AnimatorKeyframeTimingOps.RemapKeyframeRange(bindings, newMin, newMax));
                },
                (orderedBindings, mouseScreenPos));
        }

        // Reads the dopesheet's actual per-key selection (AnimationWindowState.selectedKeys), not
        // just the selected binding rows — lets Jitter target only the keys the user marquee/click
        // selected in the curve view.
        static List<(AnimationClip clip, EditorCurveBinding binding, float time)> GetSelectedKeyframes(object hierarchyGUIInstance)
        {
            var result = new List<(AnimationClip clip, EditorCurveBinding binding, float time)>();
            if (WindowPatchReflection.AnimationWindowHierarchyGUIStateProperty == null
                || WindowPatchReflection.AnimationWindowStateSelectedKeysProperty == null
                || WindowPatchReflection.AnimationWindowKeyframeCurveProperty == null
                || WindowPatchReflection.AnimationWindowKeyframeTimeProperty == null) return result;

            var state = WindowPatchReflection.AnimationWindowHierarchyGUIStateProperty.GetValue(hierarchyGUIInstance);
            if (state == null) return result;
            if (WindowPatchReflection.AnimationWindowStateSelectedKeysProperty.GetValue(state) is not IEnumerable selectedKeys) return result;

            foreach (var keyframe in selectedKeys)
            {
                var curveObj = WindowPatchReflection.AnimationWindowKeyframeCurveProperty.GetValue(keyframe);
                if (curveObj == null) continue;
                if (WindowPatchReflection.AnimationWindowCurveClipProperty.GetValue(curveObj) is not AnimationClip clip) continue;
                var binding = (EditorCurveBinding)WindowPatchReflection.AnimationWindowCurveBindingProperty.GetValue(curveObj);
                float time = (float)WindowPatchReflection.AnimationWindowKeyframeTimeProperty.GetValue(keyframe);
                result.Add((clip, binding, time));
            }
            return result;
        }
    }
}
#endif
