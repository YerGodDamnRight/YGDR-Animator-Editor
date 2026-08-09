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
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal struct NetworkSyncConfig
    {
        internal bool useBool;
        internal string paramName;
        internal string statesPrefix;
        internal bool removeParamDrivers;
        internal bool removeAudioPlay;
        internal bool removeTracking;
        internal bool anyStateTransitions;
        internal bool packIntoSubSM;
        internal bool preserveExitTime;
        internal bool preserveDuration;
        internal bool preserveOffset;
        /* false (default): merge the sync parameter into whichever VRCAvatarParameterDriver instance
           already exists on the state (or create one if none exists) — matches pre-multi-instance behavior.
           true: always use/create a dedicated driver instance named "Network", leaving any other driver
           instances on the state untouched. */
        internal bool useOwnNetworkInstance;
        /* When true, states tagged "network merge" that share the same motion are collapsed into a single
           mirror state and sync value, reducing the number of sync bools/values needed. States tagged but
           without a duplicate-motion match sync individually as normal. Only the first state encountered in
           each merged group contributes behaviours/transition properties to the mirror side. */
        internal bool mergeTaggedDuplicates;
        /* When true, duplicates the target layer and sets the copy's weight to 0 before applying any
           other network sync operations, giving the user an untouched fallback layer. */
        internal bool createBackup;
    }

    internal static class AnimatorNetworkSync
    {
        internal const string MergeTag = "network merge";

        /* Duplicates all states in parentSM into a remote-only mirror layer driven by a sync parameter, wiring IsLocal conditions on all transitions.
           Adds parameter drivers to original states and optionally packs the mirror states into a sub SM.
           When config.mergeTaggedDuplicates is set, states tagged MergeTag with identical motion share one mirror state and sync value. */
        internal static void NetworkSync(AnimatorStateMachine parentSM, NetworkSyncConfig config)
        {
            var entriesList = new List<(AnimatorState state, Vector3 position)>();
            CollectStates(parentSM, entriesList);
            var entries = entriesList.ToArray();

            if (entries.Length == 0) return;

            var controller = AnimatorBulkTransitionOps.GetController(parentSM);
            if (controller == null) return;

            if (config.createBackup)
            {
                int sourceLayerIndex = Array.FindIndex(controller.layers, layer => layer.stateMachine == parentSM);
                if (sourceLayerIndex >= 0) CreateBackupLayer(controller, sourceLayerIndex);
            }

            var groups = BuildSyncGroups(entries, config.mergeTaggedDuplicates);

            // Default state's group must be group 0: sync params default to 0 before the first
            // network value arrives, so the remote side would otherwise transition away from the
            // mirrored default state into whichever group happens to sit at index 0.
            int defaultGroupIndex = groups.FindIndex(group => group.Any(member => member.state == parentSM.defaultState));
            if (defaultGroupIndex > 0)
                (groups[0], groups[defaultGroupIndex]) = (groups[defaultGroupIndex], groups[0]);

            var stateValues = new Dictionary<AnimatorState, int>();
            for (int g = 0; g < groups.Count; g++)
                foreach (var member in groups[g])
                    stateValues[member.state] = g;

            var removedTypes = BuildRemovedTypeSet(config);
            var priorBehaviors = entries.ToDictionary(
                childState => childState.state,
                childState => childState.state.behaviours.Where(behaviour => !removedTypes.Contains(behaviour.GetType())).ToArray());

            var originalTransitions = entries.ToDictionary(
                childState => childState.state,
                childState => childState.state.transitions.ToArray());

            var undoTargets = new List<UnityEngine.Object> { parentSM, controller };
            foreach (var entry in entries) undoTargets.Add(entry.state);
            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Network Sync");

            // IsLocal parameter
            if (!controller.parameters.Any(parameter => parameter.name == "IsLocal"))
                AnimatorParameterOps.InsertParameterAtIndex(controller, controller.parameters.Length,
                    "IsLocal", AnimatorControllerParameterType.Bool);

            // Sync parameter(s)
            string[] syncParams;
            if (!config.useBool)
            {
                if (!controller.parameters.Any(parameter => parameter.name == config.paramName))
                    AnimatorParameterOps.InsertParameterAtIndex(controller, controller.parameters.Length,
                        config.paramName, AnimatorControllerParameterType.Int);
                syncParams = new[] { config.paramName };
            }
            else
            {
                int bitCount = BitsRequired(groups.Count);
                syncParams = new string[bitCount];
                for (int i = 0; i < bitCount; i++)
                {
                    syncParams[i] = $"{config.paramName}{i}";
                    if (!controller.parameters.Any(parameter => parameter.name == syncParams[i]))
                        AnimatorParameterOps.InsertParameterAtIndex(controller, controller.parameters.Length,
                            syncParams[i], AnimatorControllerParameterType.Bool);
                }
            }

            // Add VRCParameterDrivers to original states
            foreach (var entry in entries)
            {
                var state = entry.state;
                int value = stateValues[state];

                var existingDriver = config.useOwnNetworkInstance
                    ? state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault(d => d.name == "Network")
                    : state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();
                VRCAvatarParameterDriver driver;
                if (existingDriver != null)
                {
                    driver = existingDriver;
                    Undo.RecordObject(driver, "Network Sync");
                }
                else
                {
                    driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    Undo.RegisterCreatedObjectUndo(driver, "Network Sync");
                    driver.localOnly = false;
                    if (config.useOwnNetworkInstance) driver.name = "Network";
                }

                if (!config.useBool)
                {
                    if (!driver.parameters.Any(parameter => parameter.name == config.paramName))
                        driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                        {
                            type = VRC_AvatarParameterDriver.ChangeType.Set,
                            name = config.paramName,
                            value = value
                        });
                }
                else
                {
                    for (int i = 0; i < syncParams.Length; i++)
                    {
                        if (!driver.parameters.Any(parameter => parameter.name == syncParams[i]))
                            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                            {
                                type = VRC_AvatarParameterDriver.ChangeType.Set,
                                name = syncParams[i],
                                value = (value >> i) & 1
                            });
                    }
                }
                EditorUtility.SetDirty(state);
            }

            // Target SM + copy positions
            var bbox = GetBoundingBox(entries);
            float verticalOffset = bbox.height + 150f;

            var occupied = entries.Select(entry => entry.position).ToList();

            var switchStatePosition = ResolveNonOverlappingPosition(
                new Vector3(parentSM.entryPosition.x - 20f, parentSM.entryPosition.y + 80f, 0f), occupied, NodeSize);
            occupied.Add(switchStatePosition);

            AnimatorStateMachine targetSM;
            if (config.packIntoSubSM)
            {
                var subStateMachinePosition = ResolveNonOverlappingPosition(
                    new Vector3(switchStatePosition.x, switchStatePosition.y + 150f, 0f), occupied, NodeSize);
                targetSM = parentSM.AddStateMachine("Network Sync", subStateMachinePosition);
                Undo.RegisterCreatedObjectUndo(targetSM, "Network Sync");
            }
            else
            {
                targetSM = parentSM;
            }

            // Create copies (one mirror state per sync group; merged groups share a single copy)
            var stateCopyMap = new Dictionary<AnimatorState, AnimatorState>();
            var groupCopies = new AnimatorState[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                var repEntry = groups[g][0];
                var copyPos = config.packIntoSubSM
                    ? repEntry.position
                    : repEntry.position + new Vector3(0f, verticalOffset, 0f);

                var copy = targetSM.AddState($"{config.statesPrefix}{repEntry.state.name}", copyPos);
                Undo.RegisterCreatedObjectUndo(copy, "Network Sync");
                copy.motion = repEntry.state.motion;
                copy.speed = repEntry.state.speed;
                copy.writeDefaultValues = repEntry.state.writeDefaultValues;
                copy.mirror = repEntry.state.mirror;
                copy.cycleOffset = repEntry.state.cycleOffset;
                copy.iKOnFeet = repEntry.state.iKOnFeet;
                copy.tag = repEntry.state.tag;
                copy.speedParameterActive = repEntry.state.speedParameterActive;
                copy.speedParameter = repEntry.state.speedParameter;
                copy.mirrorParameterActive = repEntry.state.mirrorParameterActive;
                copy.mirrorParameter = repEntry.state.mirrorParameter;
                copy.cycleOffsetParameterActive = repEntry.state.cycleOffsetParameterActive;
                copy.cycleOffsetParameter = repEntry.state.cycleOffsetParameter;
                copy.timeParameterActive = repEntry.state.timeParameterActive;
                copy.timeParameter = repEntry.state.timeParameter;

                if (priorBehaviors.TryGetValue(repEntry.state, out var behaviors))
                {
                    foreach (var sourceBehaviour in behaviors)
                    {
                        var destinationBehaviour = copy.AddStateMachineBehaviour(sourceBehaviour.GetType());
                        if (destinationBehaviour != null) EditorUtility.CopySerialized(sourceBehaviour, destinationBehaviour);
                    }
                }

                groupCopies[g] = copy;
                foreach (var member in groups[g])
                    stateCopyMap[member.state] = copy;
            }

            // Transitions
            if (config.anyStateTransitions)
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    var copy = groupCopies[g];
                    var transition = parentSM.AddAnyStateTransition(copy);
                    Undo.RegisterCreatedObjectUndo(transition, "Network Sync");
                    transition.hasExitTime = false;
                    transition.exitTime = 0f;
                    transition.duration = 0f;
                    transition.canTransitionToSelf = false;
                    AddSyncConditions(transition, config.useBool, syncParams, g);
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");
                }
            }
            else if (config.preserveExitTime || config.preserveDuration || config.preserveOffset)
            {
                var originalTransitionMap = new Dictionary<(AnimatorState, AnimatorState), AnimatorStateTransition>();
                foreach (var (sourceState, transitions) in originalTransitions)
                    foreach (var originalTransition in transitions)
                        if (originalTransition.destinationState != null)
                            originalTransitionMap.TryAdd((sourceState, originalTransition.destinationState), originalTransition);

                for (int sg = 0; sg < groups.Count; sg++)
                {
                    var sourceRepState = groups[sg][0].state;
                    var sourceCopy = groupCopies[sg];
                    for (int dg = 0; dg < groups.Count; dg++)
                    {
                        if (sg == dg) continue;
                        var destinationRepState = groups[dg][0].state;
                        var destinationCopy = groupCopies[dg];
                        var transition = sourceCopy.AddTransition(destinationCopy);
                        Undo.RegisterCreatedObjectUndo(transition, "Network Sync");
                        originalTransitionMap.TryGetValue((sourceRepState, destinationRepState), out var originalTransition);

                        if (config.preserveExitTime && originalTransition != null)
                        {
                            transition.hasExitTime = originalTransition.hasExitTime;
                            transition.exitTime    = originalTransition.exitTime;
                        }
                        else
                        {
                            transition.hasExitTime = false;
                            transition.exitTime    = 0f;
                        }

                        if (config.preserveDuration && originalTransition != null)
                        {
                            transition.hasFixedDuration = originalTransition.hasFixedDuration;
                            transition.duration         = originalTransition.duration;
                        }
                        else
                        {
                            transition.duration = 0f;
                        }

                        if (config.preserveOffset && originalTransition != null)
                            transition.offset = originalTransition.offset;

                        AddSyncConditions(transition, config.useBool, syncParams, dg);
                    }
                }
            }
            else
            {
                for (int sg = 0; sg < groups.Count; sg++)
                {
                    var sourceCopy = groupCopies[sg];
                    for (int dg = 0; dg < groups.Count; dg++)
                    {
                        if (sg == dg) continue;
                        var destinationCopy = groupCopies[dg];
                        var transition = sourceCopy.AddTransition(destinationCopy);
                        Undo.RegisterCreatedObjectUndo(transition, "Network Sync");
                        transition.hasExitTime = false;
                        transition.exitTime = 0f;
                        transition.duration = 0f;
                        AddSyncConditions(transition, config.useBool, syncParams, dg);
                    }
                }
            }

            // Add IsLocal=true to pre-existing transitions on original states
            foreach (var (state, transitions) in originalTransitions)
            {
                foreach (var transition in transitions)
                {
                    if (!transition.conditions.Any(condition => condition.parameter == "IsLocal"))
                        transition.AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");
                }
                EditorUtility.SetDirty(state);
            }

            // Add IsLocal=true to AnyState transitions (all sub-SMs) targeting original states
            var originalStateSet = new HashSet<AnimatorState>(originalTransitions.Keys);
            var allAnyTransitions = new List<AnimatorStateTransition>();
            CollectAnyTransitions(parentSM, allAnyTransitions);
            foreach (var anyTransition in allAnyTransitions)
            {
                if (anyTransition.destinationState != null && originalStateSet.Contains(anyTransition.destinationState))
                {
                    if (!anyTransition.conditions.Any(condition => condition.parameter == "IsLocal"))
                        anyTransition.AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");
                }
            }

            // Add IsLocal=false to all transitions on copied states
            foreach (var copy in groupCopies.Distinct())
            {
                foreach (var transition in copy.transitions)
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");
                EditorUtility.SetDirty(copy);
            }

            // Network Switch: routes layer entry based on IsLocal
            var origDefault = parentSM.defaultState;
            if (origDefault != null && stateCopyMap.TryGetValue(origDefault, out var copyDefault))
            {
                var switchState = parentSM.AddState("Network Switch", switchStatePosition);
                Undo.RegisterCreatedObjectUndo(switchState, "Network Sync");
                switchState.motion = origDefault.motion;
                switchState.speed = origDefault.speed;
                switchState.writeDefaultValues = origDefault.writeDefaultValues;

                var toOrig = switchState.AddTransition(origDefault);
                Undo.RegisterCreatedObjectUndo(toOrig, "Network Sync");
                toOrig.hasExitTime = false;
                toOrig.exitTime = 0f;
                toOrig.duration = 0f;
                toOrig.AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");

                var toCopy = switchState.AddTransition(copyDefault);
                Undo.RegisterCreatedObjectUndo(toCopy, "Network Sync");
                toCopy.hasExitTime = false;
                toCopy.exitTime = 0f;
                toCopy.duration = 0f;
                toCopy.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");

                parentSM.defaultState = switchState;
                EditorUtility.SetDirty(switchState);
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(parentSM);
            EditorUtility.SetDirty(targetSM);
        }

        /* Returns a set of behaviour types that should be stripped from state copies, based on config flags. */
        static HashSet<Type> BuildRemovedTypeSet(NetworkSyncConfig config)
        {
            var removedTypes = new HashSet<Type>();
            if (config.removeParamDrivers) removedTypes.Add(typeof(VRCAvatarParameterDriver));
            if (config.removeAudioPlay)    removedTypes.Add(typeof(VRCAnimatorPlayAudio));
            if (config.removeTracking)     removedTypes.Add(typeof(VRCAnimatorTrackingControl));
            return removedTypes;
        }

        /* Groups entries into sync groups. When mergeTaggedDuplicates is set, states tagged MergeTag that share
           an identical motion are collapsed into one group (one sync value, one mirror state); all other states
           (untagged, or tagged without a duplicate-motion match) form singleton groups. Group order is stable
           relative to entries so results are deterministic across runs. */
        static List<List<(AnimatorState state, Vector3 position)>> BuildSyncGroups(
            (AnimatorState state, Vector3 position)[] entries, bool mergeTaggedDuplicates)
        {
            var groups = new List<List<(AnimatorState state, Vector3 position)>>();
            if (!mergeTaggedDuplicates)
            {
                foreach (var entry in entries)
                    groups.Add(new List<(AnimatorState, Vector3)> { entry });
                return groups;
            }

            var motionGroups = new Dictionary<Motion, List<(AnimatorState state, Vector3 position)>>();
            var grouped = new HashSet<AnimatorState>();
            foreach (var entry in entries)
            {
                if (entry.state.tag != MergeTag || entry.state.motion == null) continue;
                if (!motionGroups.TryGetValue(entry.state.motion, out var list))
                    motionGroups[entry.state.motion] = list = new List<(AnimatorState, Vector3)>();
                list.Add(entry);
            }
            foreach (var entry in entries)
            {
                if (entry.state.tag != MergeTag) continue;
                if (motionGroups.TryGetValue(entry.state.motion, out var list) && list.Count >= 2)
                {
                    grouped.Add(entry.state);
                }
            }
            var addedGroups = new HashSet<Motion>();
            foreach (var entry in entries)
            {
                if (!grouped.Contains(entry.state)) continue;
                if (addedGroups.Contains(entry.state.motion)) continue;
                addedGroups.Add(entry.state.motion);
                groups.Add(motionGroups[entry.state.motion]);
            }
            foreach (var entry in entries)
            {
                if (grouped.Contains(entry.state)) continue;
                groups.Add(new List<(AnimatorState, Vector3)> { entry });
            }
            return groups;
        }

        /* Recursively collects all states with their node positions from sm and all nested sub state machines. */
        /* Deep-copies the layer at sourceLayerIndex into a new layer with weight 0, leaving the original untouched. */
        static void CreateBackupLayer(AnimatorController controller, int sourceLayerIndex)
        {
            var sourceLayer = controller.layers[sourceLayerIndex];
            Unsupported.CopyStateMachineDataToPasteboard(sourceLayer.stateMachine, controller, sourceLayerIndex);

            string backupName = controller.MakeUniqueLayerName(sourceLayer.name + " (Backup)");
            Undo.RecordObject(controller, "Create Backup Layer");
            controller.AddLayer(backupName);
            var layers = controller.layers;
            var backupLayer = layers[layers.Length - 1];
            Unsupported.PasteToStateMachineFromPasteboard(backupLayer.stateMachine, controller, layers.Length - 1, Vector3.zero);

            // PasteToStateMachineFromPasteboard creates the pasted SM as a child of a wrapper SM — promote it to top-level
            var wrapperSM = backupLayer.stateMachine;
            var pastedSM = wrapperSM.stateMachines[0].stateMachine;
            pastedSM.name = backupName;
            wrapperSM.stateMachines = new ChildAnimatorStateMachine[0];
            Undo.DestroyObjectImmediate(wrapperSM);
            backupLayer.stateMachine = pastedSM;

            PatchLayerCopyPaste.PasteLayerProperties(backupLayer, sourceLayer);
            backupLayer.defaultWeight = 0f;

            layers[layers.Length - 1] = backupLayer;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        static void CollectStates(AnimatorStateMachine sm, List<(AnimatorState state, Vector3 position)> result)
        {
            foreach (var childState in sm.states)
                result.Add((childState.state, childState.position));
            foreach (var childStateMachine in sm.stateMachines)
                CollectStates(childStateMachine.stateMachine, result);
        }

        /* Recursively collects all anyState transitions from sm and all nested sub state machines. */
        static void CollectAnyTransitions(AnimatorStateMachine sm, List<AnimatorStateTransition> result)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                result.Add(anyStateTransition);
            foreach (var childStateMachine in sm.stateMachines)
                CollectAnyTransitions(childStateMachine.stateMachine, result);
        }

        /* Appends sync conditions to the transition: a single int-equals condition in int mode, or per-bit bool conditions in bool mode.
           value is the state's index in the sync table; bits are encoded LSB-first across syncParams. */
        static void AddSyncConditions(AnimatorStateTransition transition, bool useBool, string[] syncParams, int value)
        {
            if (!useBool)
            {
                transition.AddCondition(AnimatorConditionMode.Equals, value, syncParams[0]);
            }
            else
            {
                for (int i = 0; i < syncParams.Length; i++)
                {
                    bool bit = ((value >> i) & 1) == 1;
                    transition.AddCondition(bit ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, syncParams[i]);
                }
            }
        }

        /* Returns the minimum number of bits needed to represent n distinct integer values. */
        static int BitsRequired(int n)
        {
            if (n <= 1) return 1;
            int bits = 0, remaining = n - 1;
            while (remaining > 0) { bits++; remaining >>= 1; }
            return bits;
        }

        static readonly Vector2 NodeSize = new Vector2(130f, 40f);

        /* Shifts desired sideways (in NodeSize.x + margin steps) until its node box overlaps none of existingPositions. */
        static Vector3 ResolveNonOverlappingPosition(Vector3 desired, IReadOnlyList<Vector3> existingPositions, Vector2 size)
        {
            var pos = desired;
            var rect = new Rect(pos.x, pos.y, size.x, size.y);
            while (existingPositions.Any(existing => new Rect(existing.x, existing.y, size.x, size.y).Overlaps(rect)))
            {
                pos.x += size.x + 20f;
                rect = new Rect(pos.x, pos.y, size.x, size.y);
            }
            return pos;
        }

        /* Computes the axis-aligned bounding rectangle of the given state node positions. */
        static Rect GetBoundingBox(IEnumerable<(AnimatorState state, Vector3 position)> entries)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var (_, pos) in entries)
            {
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x);
                maxY = Mathf.Max(maxY, pos.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

    }
}
#endif
