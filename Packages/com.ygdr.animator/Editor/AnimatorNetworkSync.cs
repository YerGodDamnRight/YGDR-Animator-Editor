#if UNITY_EDITOR
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
    }

    internal static class AnimatorNetworkSync
    {
        internal static void NetworkSync(AnimatorStateMachine parentSM, NetworkSyncConfig config)
        {
            var entriesList = new List<(AnimatorState state, Vector3 position)>();
            CollectStates(parentSM, entriesList);
            var entries = entriesList.ToArray();

            if (entries.Length == 0) return;

            var controller = GetController(parentSM);
            if (controller == null) return;

            var stateValues = new Dictionary<AnimatorState, int>();
            for (int i = 0; i < entries.Length; i++)
                stateValues[entries[i].state] = i;

            var removedTypes = BuildRemovedTypeSet(config);
            var priorBehaviors = entries.ToDictionary(
                childState => childState.state,
                childState => childState.state.behaviours.Where(behaviour => !removedTypes.Contains(behaviour.GetType())).ToArray());

            var originalTransitions = entries.ToDictionary(
                childState => childState.state,
                childState => childState.state.transitions.ToArray());

            var undoTargets = new List<Object> { parentSM, controller };
            foreach (var entry in entries) undoTargets.Add(entry.state);
            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Network Sync");

            // IsLocal parameter
            if (!controller.parameters.Any(parameter => parameter.name == "IsLocal"))
                controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);

            // Sync parameter(s)
            string[] syncParams;
            if (!config.useBool)
            {
                if (!controller.parameters.Any(parameter => parameter.name == config.paramName))
                    controller.AddParameter(config.paramName, AnimatorControllerParameterType.Int);
                syncParams = new[] { config.paramName };
            }
            else
            {
                int bitCount = BitsRequired(entries.Length);
                syncParams = new string[bitCount];
                for (int i = 0; i < bitCount; i++)
                {
                    syncParams[i] = $"{config.paramName}{i}";
                    if (!controller.parameters.Any(parameter => parameter.name == syncParams[i]))
                        controller.AddParameter(syncParams[i], AnimatorControllerParameterType.Bool);
                }
            }

            // Add VRCParameterDrivers to original states
            foreach (var entry in entries)
            {
                var state = entry.state;
                int value = stateValues[state];
                var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                Undo.RegisterCreatedObjectUndo(driver, "Network Sync");
                driver.localOnly = false;

                if (!config.useBool)
                {
                    driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                    {
                        new VRC_AvatarParameterDriver.Parameter
                        {
                            type = VRC_AvatarParameterDriver.ChangeType.Set,
                            name = config.paramName,
                            value = value
                        }
                    };
                }
                else
                {
                    var driverParams = new List<VRC_AvatarParameterDriver.Parameter>();
                    for (int i = 0; i < syncParams.Length; i++)
                    {
                        driverParams.Add(new VRC_AvatarParameterDriver.Parameter
                        {
                            type = VRC_AvatarParameterDriver.ChangeType.Set,
                            name = syncParams[i],
                            value = (value >> i) & 1
                        });
                    }
                    driver.parameters = driverParams;
                }
                EditorUtility.SetDirty(state);
            }

            // Target SM + copy positions
            var bbox = GetBoundingBox(entries);
            float verticalOffset = bbox.height + 150f;

            AnimatorStateMachine targetSM;
            if (config.packIntoSubSM)
            {
                var subStateMachinePosition = new Vector3(bbox.xMin, bbox.yMax + 150f, 0f);
                targetSM = parentSM.AddStateMachine("Network Sync", subStateMachinePosition);
                Undo.RegisterCreatedObjectUndo(targetSM, "Network Sync");
            }
            else
            {
                targetSM = parentSM;
            }

            // Create copies
            var stateCopyMap = new Dictionary<AnimatorState, AnimatorState>();
            foreach (var entry in entries)
            {
                var copyPos = config.packIntoSubSM
                    ? entry.position
                    : entry.position + new Vector3(0f, verticalOffset, 0f);

                var copy = targetSM.AddState($"{config.statesPrefix}{entry.state.name}", copyPos);
                Undo.RegisterCreatedObjectUndo(copy, "Network Sync");
                copy.motion = entry.state.motion;
                copy.speed = entry.state.speed;
                copy.writeDefaultValues = entry.state.writeDefaultValues;

                if (priorBehaviors.TryGetValue(entry.state, out var behaviors))
                {
                    foreach (var sourceBehaviour in behaviors)
                    {
                        var destinationBehaviour = copy.AddStateMachineBehaviour(sourceBehaviour.GetType());
                        if (destinationBehaviour != null) EditorUtility.CopySerialized(sourceBehaviour, destinationBehaviour);
                    }
                }

                stateCopyMap[entry.state] = copy;
            }

            // Transitions
            if (config.anyStateTransitions)
            {
                foreach (var entry in entries)
                {
                    var copy = stateCopyMap[entry.state];
                    var transition = parentSM.AddAnyStateTransition(copy);
                    Undo.RegisterCreatedObjectUndo(transition, "Network Sync");
                    transition.hasExitTime = false;
                    transition.exitTime = 0f;
                    transition.duration = 0f;
                    transition.canTransitionToSelf = false;
                    AddSyncConditions(transition, config.useBool, syncParams, stateValues[entry.state]);
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");
                }
            }
            else
            {
                foreach (var sourceEntry in entries)
                {
                    var sourceCopy = stateCopyMap[sourceEntry.state];
                    foreach (var destinationEntry in entries)
                    {
                        if (sourceEntry.state == destinationEntry.state) continue;
                        var destinationCopy = stateCopyMap[destinationEntry.state];
                        var transition = sourceCopy.AddTransition(destinationCopy);
                        Undo.RegisterCreatedObjectUndo(transition, "Network Sync");
                        transition.hasExitTime = false;
                        transition.exitTime = 0f;
                        transition.duration = 0f;
                        AddSyncConditions(transition, config.useBool, syncParams, stateValues[destinationEntry.state]);
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
            foreach (var copy in stateCopyMap.Values)
            {
                foreach (var transition in copy.transitions)
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");
                EditorUtility.SetDirty(copy);
            }

            // Network Switch: routes layer entry based on IsLocal
            var origDefault = parentSM.defaultState;
            if (origDefault != null && stateCopyMap.TryGetValue(origDefault, out var copyDefault))
            {
                var switchPos = new Vector3(parentSM.entryPosition.x - 20f, parentSM.entryPosition.y + 80f, 0f);
                var switchState = parentSM.AddState("Network Switch", switchPos);
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

        static System.Collections.Generic.HashSet<System.Type> BuildRemovedTypeSet(NetworkSyncConfig config)
        {
            var removedTypes = new System.Collections.Generic.HashSet<System.Type>();
            if (config.removeParamDrivers) removedTypes.Add(typeof(VRCAvatarParameterDriver));
            if (config.removeAudioPlay)    removedTypes.Add(typeof(VRCAnimatorPlayAudio));
            if (config.removeTracking)     removedTypes.Add(typeof(VRCAnimatorTrackingControl));
            return removedTypes;
        }

        static void CollectStates(AnimatorStateMachine sm, List<(AnimatorState state, Vector3 position)> result)
        {
            foreach (var childState in sm.states)
                result.Add((childState.state, childState.position));
            foreach (var childStateMachine in sm.stateMachines)
                CollectStates(childStateMachine.stateMachine, result);
        }

        static void CollectAnyTransitions(AnimatorStateMachine sm, List<AnimatorStateTransition> result)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                result.Add(anyStateTransition);
            foreach (var childStateMachine in sm.stateMachines)
                CollectAnyTransitions(childStateMachine.stateMachine, result);
        }

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

        static int BitsRequired(int n)
        {
            if (n <= 1) return 1;
            int bits = 0, remaining = n - 1;
            while (remaining > 0) { bits++; remaining >>= 1; }
            return bits;
        }

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

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }
    }
}
#endif
