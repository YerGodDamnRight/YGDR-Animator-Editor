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
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using ReorderableList = UnityEditorInternal.ReorderableList;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Tracking Control section ──────────────────────────────────────

        void DrawVRCTrackingSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetTrackingForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetTrackingForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.tracking"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                    foreach (var state in _selectedStates)
                        GetOrCreateTracking(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemoveTrackingFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawTrackingFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawTrackingFields()
        {
            var statesWithTracking = _selectedStates.Where(state => GetTrackingForState(state) != null).ToArray();
            var first = GetTrackingForState(statesWithTracking[0]);
            bool multi = statesWithTracking.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label(L10n.Get("vrc.tracking.no_change"), EditorStyles.label, GUILayout.Width(70));
                GUILayout.Label(L10n.Get("vrc.tracking.tracking"),   EditorStyles.label, GUILayout.Width(70));
                GUILayout.Label(L10n.Get("vrc.tracking.animation"),  EditorStyles.label, GUILayout.Width(70));
            }

            // Set All row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.tracking.set_all"), GUILayout.Width(110));
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.NoChange,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Tracking,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Animation, 70f);
            }
            EditorGUILayout.Space(2f);

            DrawTrackingRow(L10n.Get("vrc.tracking.head"),          statesWithTracking, audio => audio.trackingHead,         (a, v) => a.trackingHead         = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_hand"),     statesWithTracking, audio => audio.trackingLeftHand,      (a, v) => a.trackingLeftHand     = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_hand"),    statesWithTracking, audio => audio.trackingRightHand,     (a, v) => a.trackingRightHand    = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.hip"),           statesWithTracking, audio => audio.trackingHip,           (a, v) => a.trackingHip          = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_foot"),     statesWithTracking, audio => audio.trackingLeftFoot,      (a, v) => a.trackingLeftFoot     = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_foot"),    statesWithTracking, audio => audio.trackingRightFoot,     (a, v) => a.trackingRightFoot    = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_fingers"),  statesWithTracking, audio => audio.trackingLeftFingers,   (a, v) => a.trackingLeftFingers  = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_fingers"), statesWithTracking, audio => audio.trackingRightFingers,  (a, v) => a.trackingRightFingers = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.eyes_eyelids"),  statesWithTracking, audio => audio.trackingEyes,          (a, v) => a.trackingEyes         = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.mouth_jaw"),     statesWithTracking, audio => audio.trackingMouth,         (a, v) => a.trackingMouth        = v);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithTracking.Any(state => GetTrackingForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var tracking = GetOrCreateTracking(state);
                        Undo.RecordObject(tracking, "Edit Debug String");
                        tracking.debugString = newDebugString;
                        EditorUtility.SetDirty(tracking);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws a single tracking body-part row with label and three radio toggles (NoChange/Tracking/Animation), applying set to all selected states on change. */
        void DrawTrackingRow(
            string label,
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set)
        {
            var firstVal = get(GetTrackingForState(statesWithTracking[0]));
            bool mixed = statesWithTracking.Length > 1 && statesWithTracking.Any(state => get(GetTrackingForState(state)) != firstVal);

            Color labelColor = mixed
                ? new Color(0.4f, 0.7f, 1f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Tracking  ? new Color(0.4f, 0.9f, 0.4f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Animation ? new Color(1f, 0.85f, 0.2f)
                : Color.white;

            using (new EditorGUILayout.HorizontalScope())
            {
                var prevColor = GUI.color;
                GUI.color = labelColor;
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                GUI.color = prevColor;
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.NoChange,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Tracking,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Animation, firstVal, mixed, 70f);
            }
        }

        /* Draws one radio Toggle for targetType; sets all selected states to targetType via set when clicked while not already selected. */
        void DrawTrackingRadio(
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            VRC_AnimatorTrackingControl.TrackingType currentVal,
            bool mixed,
            float width)
        {
            bool isSelected = !mixed && currentVal == targetType;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(isSelected, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !isSelected)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Edit Tracking Control");
                    set(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        static VRCAnimatorTrackingControl GetTrackingForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();

        /* Returns the existing VRCAnimatorTrackingControl on state, or adds and registers a new one via Undo. */
        static VRCAnimatorTrackingControl GetOrCreateTracking(AnimatorState state)
        {
            var tracking = state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();
            if (tracking != null) return tracking;
            tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            Undo.RegisterCreatedObjectUndo(tracking, "Create VRC Tracking Control");
            EditorUtility.SetDirty(state);
            return tracking;
        }

        /* Draws a "Set All" radio toggle that sets every tracking field on all selected states to targetType when clicked. */
        void DrawSetAllTrackingRadio(
            AnimatorState[] statesWithTracking,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            float width)
        {
            bool allMatch = statesWithTracking.All(state => TrackingAllFieldsAre(GetTrackingForState(state), targetType));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(allMatch, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !allMatch)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Set All Tracking");
                    TrackingSetAllFields(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        /* Returns true if every tracking field on ctrl equals type, used to determine "Set All" radio state. */
        static bool TrackingAllFieldsAre(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
            => ctrl.trackingHead == type && ctrl.trackingLeftHand == type && ctrl.trackingRightHand == type
            && ctrl.trackingHip == type && ctrl.trackingLeftFoot == type && ctrl.trackingRightFoot == type
            && ctrl.trackingLeftFingers == type && ctrl.trackingRightFingers == type
            && ctrl.trackingEyes == type && ctrl.trackingMouth == type;

        /* Sets every tracking body-part field on ctrl to type in a single statement. */
        static void TrackingSetAllFields(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
        {
            ctrl.trackingHead = ctrl.trackingLeftHand = ctrl.trackingRightHand = ctrl.trackingHip =
            ctrl.trackingLeftFoot = ctrl.trackingRightFoot = ctrl.trackingLeftFingers =
            ctrl.trackingRightFingers = ctrl.trackingEyes = ctrl.trackingMouth = type;
        }

        void RemoveTrackingFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var tracking = GetTrackingForState(state);
                if (tracking == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Tracking Control");
                state.behaviours = state.behaviours.Where(b => b != tracking).ToArray();
                Undo.DestroyObjectImmediate(tracking);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Locomotion Control section ────────────────────────────────────

        void DrawVRCLocomotionSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetLocomotionForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetLocomotionForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.locomotion"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                    foreach (var state in _selectedStates)
                        GetOrCreateLocomotion(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemoveLocomotionFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLocomotionFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLocomotionFields()
        {
            var statesWithLocomotion = _selectedStates.Where(state => GetLocomotionForState(state) != null).ToArray();
            var first = GetLocomotionForState(statesWithLocomotion[0]);
            bool multi = statesWithLocomotion.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedDisable = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).disableLocomotion != first.disableLocomotion);
                EditorGUILayout.LabelField(L10n.Get("vrc.locomotion.label"), GUILayout.Width(110));
                DrawBoolToggleButtons(first.disableLocomotion, mixedDisable, L10n.Get("vrc.locomotion.disable"), L10n.Get("vrc.locomotion.enable"), 60f, isDisabled =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Locomotion Control");
                        locomotion.disableLocomotion = isDisabled;
                        EditorUtility.SetDirty(locomotion);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Debug String");
                        locomotion.debugString = newDebugString;
                        EditorUtility.SetDirty(locomotion);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorLocomotionControl GetLocomotionForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();

        static VRCAnimatorLocomotionControl GetOrCreateLocomotion(AnimatorState state)
        {
            var locomotion = state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();
            if (locomotion != null) return locomotion;
            locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
            Undo.RegisterCreatedObjectUndo(locomotion, "Create VRC Locomotion Control");
            EditorUtility.SetDirty(state);
            return locomotion;
        }

        void RemoveLocomotionFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var locomotion = GetLocomotionForState(state);
                if (locomotion == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Locomotion Control");
                state.behaviours = state.behaviours.Where(b => b != locomotion).ToArray();
                Undo.DestroyObjectImmediate(locomotion);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Temporary Pose Space section ─────────────────────────────────

        void DrawVRCPoseSpaceSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetPoseSpaceForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetPoseSpaceForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.pose_space"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                    foreach (var state in _selectedStates)
                        GetOrCreatePoseSpace(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemovePoseSpaceFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPoseSpaceFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPoseSpaceFields()
        {
            var statesWithPoseSpace = _selectedStates.Where(state => GetPoseSpaceForState(state) != null).ToArray();
            var first = GetPoseSpaceForState(statesWithPoseSpace[0]);
            bool multi = statesWithPoseSpace.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedEnter = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).enterPoseSpace != first.enterPoseSpace);
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.pose_space.pose_space"), L10n.Get("vrc.tooltip.pose_space")), GUILayout.Width(110));
                DrawBoolToggleButtons(first.enterPoseSpace, mixedEnter, L10n.Get("vrc.pose_space.enter"), L10n.Get("vrc.pose_space.exit"), 60f, isEnter =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Pose Space");
                        poseSpace.enterPoseSpace = isEnter;
                        EditorUtility.SetDirty(poseSpace);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.pose_space.fixed_delay"), L10n.Get("vrc.tooltip.fixed_delay")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).fixedDelay != first.fixedDelay);
                EditorGUI.BeginChangeCheck();
                bool newFixedDelay = EditorGUILayout.Toggle(first.fixedDelay, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Fixed Delay");
                        poseSpace.fixedDelay = newFixedDelay;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(first.fixedDelay ? L10n.Get("vrc.pose_space.delay_time_s") : L10n.Get("vrc.pose_space.delay_time_pct"), L10n.Get("vrc.tooltip.delay_time")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => !Mathf.Approximately(GetPoseSpaceForState(state).delayTime, first.delayTime));
                EditorGUI.BeginChangeCheck();
                float newDelayTime = EditorGUILayout.FloatField(first.delayTime);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Delay Time");
                        poseSpace.delayTime = newDelayTime;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Debug String");
                        poseSpace.debugString = newDebugString;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorTemporaryPoseSpace GetPoseSpaceForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();

        static VRCAnimatorTemporaryPoseSpace GetOrCreatePoseSpace(AnimatorState state)
        {
            var poseSpace = state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();
            if (poseSpace != null) return poseSpace;
            poseSpace = state.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();
            Undo.RegisterCreatedObjectUndo(poseSpace, "Create VRC Temporary Pose Space");
            EditorUtility.SetDirty(state);
            return poseSpace;
        }

        void RemovePoseSpaceFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var poseSpace = GetPoseSpaceForState(state);
                if (poseSpace == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Temporary Pose Space");
                state.behaviours = state.behaviours.Where(b => b != poseSpace).ToArray();
                Undo.DestroyObjectImmediate(poseSpace);
                EditorUtility.SetDirty(state);
            }
        }
    }
}
#endif
