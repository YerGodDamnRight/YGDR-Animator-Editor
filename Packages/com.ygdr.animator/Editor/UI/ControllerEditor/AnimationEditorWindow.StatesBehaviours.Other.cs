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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Tracking / Locomotion / Temporary Pose Space sections (native, single-instance) ────────

        static readonly VRC_AnimatorTrackingControl.TrackingType[] TrackingTypes =
        {
            VRC_AnimatorTrackingControl.TrackingType.NoChange,
            VRC_AnimatorTrackingControl.TrackingType.Tracking,
            VRC_AnimatorTrackingControl.TrackingType.Animation,
        };

        VisualElement _trackingSection;
        Button _trackingRemoveButton;
        VisualElement _trackingBody;
        VisualElement _locomotionSection;
        Button _locomotionRemoveButton;
        VisualElement _locomotionBody;
        VisualElement _poseSpaceSection;
        Button _poseSpaceRemoveButton;
        VisualElement _poseSpaceBody;

        VisualElement BuildOtherBehaviorsBody()
        {
            var root = new VisualElement();
            root.AddToClassList("ygdr-behavior-group");

            _trackingSection = BuildBehaviorSectionShell(L10n.Get("vrc.tracking"), out _trackingRemoveButton, out _trackingBody);
            _trackingBody.AddToClassList("ygdr-behavior-instance-body");
            _trackingRemoveButton.clicked += () =>
            {
                RemoveTrackingFromAll();
                RefreshTrackingSection();
            };
            root.Add(_trackingSection);

            _locomotionSection = BuildBehaviorSectionShell(L10n.Get("vrc.locomotion"), out _locomotionRemoveButton, out _locomotionBody);
            _locomotionBody.AddToClassList("ygdr-behavior-instance-body");
            _locomotionRemoveButton.clicked += () =>
            {
                RemoveLocomotionFromAll();
                RefreshLocomotionSection();
            };
            root.Add(_locomotionSection);

            _poseSpaceSection = BuildBehaviorSectionShell(L10n.Get("vrc.pose_space"), out _poseSpaceRemoveButton, out _poseSpaceBody);
            _poseSpaceBody.AddToClassList("ygdr-behavior-instance-body");
            _poseSpaceRemoveButton.clicked += () =>
            {
                RemovePoseSpaceFromAll();
                RefreshPoseSpaceSection();
            };
            root.Add(_poseSpaceSection);

            return root;
        }

        void RefreshOtherBehaviorsBody()
        {
            RefreshTrackingSection();
            RefreshLocomotionSection();
            RefreshPoseSpaceSection();
        }

        /* Entry points for the top-level Add Behavior dropdown. Singleton types — loop is a no-op for states
           that already have one, so this doubles as "fill the gap" for a mixed selection. */
        void AddTrackingBehaviorToSelected()
        {
            foreach (var state in _selectedStates) GetOrCreateTracking(state);
            RefreshTrackingSection();
        }

        void AddLocomotionBehaviorToSelected()
        {
            foreach (var state in _selectedStates) GetOrCreateLocomotion(state);
            RefreshLocomotionSection();
        }

        void AddPoseSpaceBehaviorToSelected()
        {
            foreach (var state in _selectedStates) GetOrCreatePoseSpace(state);
            RefreshPoseSpaceSection();
        }

        // ── Tracking ──────────────────────────────────────────────────────────

        void RefreshTrackingSection()
        {
            if (_trackingBody == null) return;
            bool anyHave = _selectedStates.Any(state => GetTrackingForState(state) != null);
            _trackingSection.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            _trackingRemoveButton.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;

            _trackingBody.Clear();
            _trackingBody.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            if (!anyHave) return;

            var statesWithTracking = _selectedStates.Where(state => GetTrackingForState(state) != null).ToArray();
            var first = GetTrackingForState(statesWithTracking[0]);
            bool multi = statesWithTracking.Length > 1;

            _trackingBody.Add(BuildTrackingColumnHeaderRow());
            _trackingBody.Add(BuildTrackingSetAllRow(statesWithTracking));

            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.head"),          statesWithTracking, ctrl => ctrl.trackingHead,         (ctrl, v) => ctrl.trackingHead         = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.left_hand"),     statesWithTracking, ctrl => ctrl.trackingLeftHand,      (ctrl, v) => ctrl.trackingLeftHand     = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.right_hand"),    statesWithTracking, ctrl => ctrl.trackingRightHand,     (ctrl, v) => ctrl.trackingRightHand    = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.hip"),           statesWithTracking, ctrl => ctrl.trackingHip,           (ctrl, v) => ctrl.trackingHip          = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.left_foot"),     statesWithTracking, ctrl => ctrl.trackingLeftFoot,      (ctrl, v) => ctrl.trackingLeftFoot     = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.right_foot"),    statesWithTracking, ctrl => ctrl.trackingRightFoot,     (ctrl, v) => ctrl.trackingRightFoot    = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.left_fingers"),  statesWithTracking, ctrl => ctrl.trackingLeftFingers,   (ctrl, v) => ctrl.trackingLeftFingers  = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.right_fingers"), statesWithTracking, ctrl => ctrl.trackingRightFingers,  (ctrl, v) => ctrl.trackingRightFingers = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.eyes_eyelids"),  statesWithTracking, ctrl => ctrl.trackingEyes,          (ctrl, v) => ctrl.trackingEyes         = v));
            _trackingBody.Add(BuildTrackingBodyPartRow(L10n.Get("vrc.tracking.mouth_jaw"),     statesWithTracking, ctrl => ctrl.trackingMouth,         (ctrl, v) => ctrl.trackingMouth        = v));

            var debugStringField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithTracking.Any(state => GetTrackingForState(state).debugString != first.debugString) };
            debugStringField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Edit Debug String");
                    tracking.debugString = evt.newValue;
                    EditorUtility.SetDirty(tracking);
                }
            });
            _trackingBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string"), debugStringField));
        }

        static VisualElement BuildTrackingColumnHeaderRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-tracking-row");
            var spacer = new VisualElement();
            spacer.AddToClassList("ygdr-behavior-field-label");
            row.Add(spacer);
            row.Add(BuildTrackingColumnLabel(L10n.Get("vrc.tracking.no_change")));
            row.Add(BuildTrackingColumnLabel(L10n.Get("vrc.tracking.tracking")));
            row.Add(BuildTrackingColumnLabel(L10n.Get("vrc.tracking.animation")));
            return row;
        }

        static Label BuildTrackingColumnLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ygdr-tracking-col");
            return label;
        }

        VisualElement BuildTrackingSetAllRow(AnimatorState[] statesWithTracking)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-tracking-row");
            var label = new Label(L10n.Get("vrc.tracking.set_all"));
            label.AddToClassList("ygdr-behavior-field-label");
            row.Add(label);

            foreach (var type in TrackingTypes)
            {
                bool allMatch = statesWithTracking.All(state => TrackingAllFieldsAre(GetTrackingForState(state), type));
                var toggle = new Toggle { value = allMatch };
                toggle.AddToClassList("ygdr-tracking-col");
                var capturedType = type;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue) { toggle.SetValueWithoutNotify(allMatch); return; }
                    foreach (var state in _selectedStates)
                    {
                        var tracking = GetOrCreateTracking(state);
                        Undo.RecordObject(tracking, "Set All Tracking");
                        TrackingSetAllFields(tracking, capturedType);
                        EditorUtility.SetDirty(tracking);
                    }
                    RefreshTrackingSection();
                });
                row.Add(toggle);
            }
            return row;
        }

        VisualElement BuildTrackingBodyPartRow(string label, AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-tracking-row");

            var firstVal = get(GetTrackingForState(statesWithTracking[0]));
            bool mixed = statesWithTracking.Length > 1 && statesWithTracking.Any(state => get(GetTrackingForState(state)) != firstVal);
            Color labelColor = mixed
                ? new Color(0.4f, 0.7f, 1f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Tracking  ? new Color(0.4f, 0.9f, 0.4f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Animation ? new Color(1f, 0.85f, 0.2f)
                : Color.white;

            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-behavior-field-label");
            labelElement.style.color = labelColor;
            row.Add(labelElement);

            foreach (var type in TrackingTypes)
            {
                bool isSelected = !mixed && firstVal == type;
                var toggle = new Toggle { value = isSelected };
                toggle.AddToClassList("ygdr-tracking-col");
                var capturedType = type;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue) { toggle.SetValueWithoutNotify(isSelected); return; }
                    foreach (var state in _selectedStates)
                    {
                        var tracking = GetOrCreateTracking(state);
                        Undo.RecordObject(tracking, "Edit Tracking Control");
                        set(tracking, capturedType);
                        EditorUtility.SetDirty(tracking);
                    }
                    RefreshTrackingSection();
                });
                row.Add(toggle);
            }
            return row;
        }

        static VRCAnimatorTrackingControl GetTrackingForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();

        static VRCAnimatorTrackingControl GetOrCreateTracking(AnimatorState state)
        {
            var tracking = state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();
            if (tracking != null) return tracking;
            tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            Undo.RegisterCreatedObjectUndo(tracking, "Create VRC Tracking Control");
            EditorUtility.SetDirty(state);
            return tracking;
        }

        static bool TrackingAllFieldsAre(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
            => ctrl.trackingHead == type && ctrl.trackingLeftHand == type && ctrl.trackingRightHand == type
            && ctrl.trackingHip == type && ctrl.trackingLeftFoot == type && ctrl.trackingRightFoot == type
            && ctrl.trackingLeftFingers == type && ctrl.trackingRightFingers == type
            && ctrl.trackingEyes == type && ctrl.trackingMouth == type;

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

        // ── Locomotion ────────────────────────────────────────────────────────

        void RefreshLocomotionSection()
        {
            if (_locomotionBody == null) return;
            bool anyHave = _selectedStates.Any(state => GetLocomotionForState(state) != null);
            _locomotionSection.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            _locomotionRemoveButton.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;

            _locomotionBody.Clear();
            _locomotionBody.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            if (!anyHave) return;

            var statesWithLocomotion = _selectedStates.Where(state => GetLocomotionForState(state) != null).ToArray();
            var first = GetLocomotionForState(statesWithLocomotion[0]);
            bool multi = statesWithLocomotion.Length > 1;

            bool mixedDisable = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).disableLocomotion != first.disableLocomotion);
            var disableField = BuildBoolToggleButtonsField(first.disableLocomotion, mixedDisable, L10n.Get("vrc.locomotion.disable"), L10n.Get("vrc.locomotion.enable"), isDisabled =>
            {
                foreach (var state in _selectedStates)
                {
                    var locomotion = GetOrCreateLocomotion(state);
                    Undo.RecordObject(locomotion, "Edit Locomotion Control");
                    locomotion.disableLocomotion = isDisabled;
                    EditorUtility.SetDirty(locomotion);
                }
                RefreshLocomotionSection();
            });
            _locomotionBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.locomotion.label"), null, disableField));

            var debugStringField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).debugString != first.debugString) };
            debugStringField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in _selectedStates)
                {
                    var locomotion = GetOrCreateLocomotion(state);
                    Undo.RecordObject(locomotion, "Edit Debug String");
                    locomotion.debugString = evt.newValue;
                    EditorUtility.SetDirty(locomotion);
                }
            });
            _locomotionBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string"), debugStringField));
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

        // ── Temporary Pose Space ─────────────────────────────────────────────

        void RefreshPoseSpaceSection()
        {
            if (_poseSpaceBody == null) return;
            bool anyHave = _selectedStates.Any(state => GetPoseSpaceForState(state) != null);
            _poseSpaceSection.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            _poseSpaceRemoveButton.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;

            _poseSpaceBody.Clear();
            _poseSpaceBody.style.display = anyHave ? DisplayStyle.Flex : DisplayStyle.None;
            if (!anyHave) return;

            var statesWithPoseSpace = _selectedStates.Where(state => GetPoseSpaceForState(state) != null).ToArray();
            var first = GetPoseSpaceForState(statesWithPoseSpace[0]);
            bool multi = statesWithPoseSpace.Length > 1;

            bool mixedEnter = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).enterPoseSpace != first.enterPoseSpace);
            var enterField = BuildBoolToggleButtonsField(first.enterPoseSpace, mixedEnter, L10n.Get("vrc.pose_space.enter"), L10n.Get("vrc.pose_space.exit"), isEnter =>
            {
                foreach (var state in _selectedStates)
                {
                    var poseSpace = GetOrCreatePoseSpace(state);
                    Undo.RecordObject(poseSpace, "Edit Pose Space");
                    poseSpace.enterPoseSpace = isEnter;
                    EditorUtility.SetDirty(poseSpace);
                }
                RefreshPoseSpaceSection();
            });
            _poseSpaceBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.pose_space.pose_space"), L10n.Get("vrc.tooltip.pose_space"), enterField));

            var fixedDelayField = new Toggle { value = first.fixedDelay, showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).fixedDelay != first.fixedDelay) };
            fixedDelayField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in _selectedStates)
                {
                    var poseSpace = GetOrCreatePoseSpace(state);
                    Undo.RecordObject(poseSpace, "Edit Fixed Delay");
                    poseSpace.fixedDelay = evt.newValue;
                    EditorUtility.SetDirty(poseSpace);
                }
                RefreshPoseSpaceSection();
            });
            _poseSpaceBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.pose_space.fixed_delay"), L10n.Get("vrc.tooltip.fixed_delay"), fixedDelayField));

            var delayTimeField = new FloatField { value = first.delayTime, showMixedValue = multi && statesWithPoseSpace.Any(state => !Mathf.Approximately(GetPoseSpaceForState(state).delayTime, first.delayTime)) };
            delayTimeField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in _selectedStates)
                {
                    var poseSpace = GetOrCreatePoseSpace(state);
                    Undo.RecordObject(poseSpace, "Edit Delay Time");
                    poseSpace.delayTime = evt.newValue;
                    EditorUtility.SetDirty(poseSpace);
                }
            });
            string delayLabel = first.fixedDelay ? L10n.Get("vrc.pose_space.delay_time_s") : L10n.Get("vrc.pose_space.delay_time_pct");
            _poseSpaceBody.Add(BuildBehaviorFieldRow(delayLabel, L10n.Get("vrc.tooltip.delay_time"), delayTimeField));

            var debugStringField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).debugString != first.debugString) };
            debugStringField.RegisterValueChangedCallback(evt =>
            {
                foreach (var state in _selectedStates)
                {
                    var poseSpace = GetOrCreatePoseSpace(state);
                    Undo.RecordObject(poseSpace, "Edit Debug String");
                    poseSpace.debugString = evt.newValue;
                    EditorUtility.SetDirty(poseSpace);
                }
            });
            _poseSpaceBody.Add(BuildBehaviorFieldRow(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string"), debugStringField));
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
