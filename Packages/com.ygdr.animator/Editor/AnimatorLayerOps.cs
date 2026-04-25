#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorLayerOps
    {
        internal static void DeleteAllTransitions(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return;

            var controller = GetController(stateMachine);

            // Only register the top-level SM and its direct states for undo — no recursion.
            var undoTargets = new List<Object> { stateMachine };
            foreach (var childState in stateMachine.states)
                undoTargets.Add(childState.state);
            if (controller != null) undoTargets.Add(controller);

            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Delete All Transitions in Layer");

            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];
            stateMachine.entryTransitions = new AnimatorTransition[0];
            foreach (var childState in stateMachine.states)
                childState.state.transitions = new AnimatorStateTransition[0];

            EditorUtility.SetDirty(stateMachine);
            if (controller != null) AssetDatabase.SaveAssets();
        }

        internal static void ReverseNegateTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions)
        {
            if (transitions == null || transitions.Length == 0) return;

            // Resolve valid state-to-state pairs; skip anyState/exit/SM-destination transitions
            var validPairs = new List<(AnimatorState sourceState, AnimatorState destinationState, AnimatorStateTransition originalTransition)>();
            foreach (var transition in transitions)
            {
                if (transition == null || transition.destinationState == null) continue;
                var sourceState = FindStateContainingTransition(activeSM, transition);
                if (sourceState == null) continue; // anyState transition — skip
                validPairs.Add((sourceState, transition.destinationState, transition));
            }
            if (validPairs.Count == 0) return;

            var undoTargets = new List<Object> { activeSM };
            foreach (var (sourceState, destinationState, _) in validPairs)
            {
                if (!undoTargets.Contains(sourceState)) undoTargets.Add(sourceState);
                if (!undoTargets.Contains(destinationState)) undoTargets.Add(destinationState);
            }
            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Reverse Negate Transitions");

            foreach (var (sourceState, destinationState, originalTransition) in validPairs)
            {
                var reversedTransition = destinationState.AddTransition(sourceState);
                Undo.RegisterCreatedObjectUndo(reversedTransition, "Reverse Negate Transitions");

                foreach (var condition in originalTransition.conditions)
                    reversedTransition.AddCondition(NegateConditionMode(condition.mode), condition.threshold, condition.parameter);

                reversedTransition.hasExitTime = originalTransition.hasExitTime;
                reversedTransition.exitTime = originalTransition.exitTime;
                reversedTransition.duration = originalTransition.duration;
                reversedTransition.offset = originalTransition.offset;
                reversedTransition.interruptionSource = originalTransition.interruptionSource;
                reversedTransition.orderedInterruption = originalTransition.orderedInterruption;
                reversedTransition.canTransitionToSelf = originalTransition.canTransitionToSelf;
                reversedTransition.mute = originalTransition.mute;
                reversedTransition.solo = originalTransition.solo;

                EditorUtility.SetDirty(destinationState);
            }

            EditorUtility.SetDirty(activeSM);
            AssetDatabase.SaveAssets();
        }

        internal static void MultiTransition(AnimatorStateMachine activeSM, AnimatorState[] sourceStates, AnimatorState[] destinationStates)
        {
            if (sourceStates == null || destinationStates == null || sourceStates.Length == 0 || destinationStates.Length == 0) return;

            var undoTargets = sourceStates.Cast<Object>().Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Multi Transition");

            foreach (var sourceState in sourceStates)
                foreach (var destinationState in destinationStates)
                    Undo.RegisterCreatedObjectUndo(sourceState.AddTransition(destinationState), "Multi Transition");

            foreach (var sourceState in sourceStates) EditorUtility.SetDirty(sourceState);
            EditorUtility.SetDirty(activeSM);
            AssetDatabase.SaveAssets();
        }

        internal static void RedirectTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions, AnimatorState[] destinationStates)
        {
            if (transitions == null || destinationStates == null || destinationStates.Length == 0) return;

            var validPairs = transitions
                .Where(transition => transition != null)
                .Select(transition => (sourceState: FindStateContainingTransition(activeSM, transition), originalTransition: transition))
                .Where(pair => pair.sourceState != null)
                .ToList();
            if (validPairs.Count == 0) return;

            var undoTargets = validPairs.Select(pair => (Object)pair.sourceState).Distinct()
                .Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Redirect Transitions");

            foreach (var (sourceState, originalTransition) in validPairs)
                foreach (var destinationState in destinationStates)
                {
                    var newTransition = sourceState.AddTransition(destinationState);
                    Undo.RegisterCreatedObjectUndo(newTransition, "Redirect Transitions");
                    CopyTransitionSettings(originalTransition, newTransition);
                    EditorUtility.SetDirty(sourceState);
                }

            EditorUtility.SetDirty(activeSM);
            AssetDatabase.SaveAssets();
        }

        internal static void ReplicateTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions, AnimatorState[] newSourceStates)
        {
            if (transitions == null || newSourceStates == null || newSourceStates.Length == 0) return;

            var validPairs = transitions
                .Where(transition => transition != null && transition.destinationState != null)
                .Select(transition => (destinationState: transition.destinationState, originalTransition: transition))
                .ToList();
            if (validPairs.Count == 0) return;

            var undoTargets = newSourceStates.Cast<Object>().Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Replicate Transitions");

            foreach (var (destinationState, originalTransition) in validPairs)
                foreach (var sourceState in newSourceStates)
                {
                    var newTransition = sourceState.AddTransition(destinationState);
                    Undo.RegisterCreatedObjectUndo(newTransition, "Replicate Transitions");
                    CopyTransitionSettings(originalTransition, newTransition);
                    EditorUtility.SetDirty(sourceState);
                }

            EditorUtility.SetDirty(activeSM);
            AssetDatabase.SaveAssets();
        }

        static void CopyTransitionSettings(AnimatorStateTransition sourceTransition, AnimatorStateTransition destinationTransition)
        {
            foreach (var condition in sourceTransition.conditions)
                destinationTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            destinationTransition.hasExitTime = sourceTransition.hasExitTime;
            destinationTransition.exitTime = sourceTransition.exitTime;
            destinationTransition.duration = sourceTransition.duration;
            destinationTransition.offset = sourceTransition.offset;
            destinationTransition.interruptionSource = sourceTransition.interruptionSource;
            destinationTransition.orderedInterruption = sourceTransition.orderedInterruption;
            destinationTransition.canTransitionToSelf = sourceTransition.canTransitionToSelf;
            destinationTransition.mute = sourceTransition.mute;
            destinationTransition.solo = sourceTransition.solo;
        }

        static AnimatorState FindStateContainingTransition(AnimatorStateMachine stateMachine, AnimatorStateTransition transition)
        {
            // Search direct states
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.transitions.Contains(transition))
                    return childState.state;
            }

            // Search sub state machines recursively
            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                var found = FindStateContainingTransition(childStateMachine.stateMachine, transition);
                if (found != null) return found;
            }

            return null;
        }

        static AnimatorConditionMode NegateConditionMode(AnimatorConditionMode mode)
        {
            return mode switch
            {
                AnimatorConditionMode.If => AnimatorConditionMode.IfNot,
                AnimatorConditionMode.IfNot => AnimatorConditionMode.If,
                AnimatorConditionMode.Greater => AnimatorConditionMode.Less,
                AnimatorConditionMode.Less => AnimatorConditionMode.Greater,
                AnimatorConditionMode.Equals => AnimatorConditionMode.NotEqual,
                AnimatorConditionMode.NotEqual => AnimatorConditionMode.Equals,
                _ => mode
            };
        }

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }
    }
}
#endif
