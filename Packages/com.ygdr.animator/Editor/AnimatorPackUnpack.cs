#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorPackUnpack
    {
        internal static void Pack(AnimatorStateMachine parentSM, AnimatorState[] selectedStates)
        {
            if (selectedStates == null || selectedStates.Length == 0) return;

            var controller = GetController(parentSM);

            // Collect child state entries for selected states before modifying arrays
            var selectedEntries = parentSM.states
                .Where(childState => selectedStates.Contains(childState.state))
                .ToArray();

            if (selectedEntries.Length == 0) return;

            // Calculate centroid position for the new sub state machine
            var centroid = selectedEntries
                .Aggregate(Vector3.zero, static (positionSum, childState) => positionSum + childState.position)
                / selectedEntries.Length;

            Undo.RegisterCompleteObjectUndo(
                new Object[] { parentSM, controller }.Where(static x => x != null).ToArray(),
                "Pack into Sub State Machine");

            // Create the sub state machine and register it for undo
            var subStateMachine = parentSM.AddStateMachine("Sub State Machine", centroid);
            Undo.RegisterCreatedObjectUndo(subStateMachine, "Pack into Sub State Machine");

            // Move states from parent to sub state machine.
            // We set the .states array directly rather than calling RemoveState()
            // to avoid destroying the state sub-assets.
            var subStates = selectedEntries
                .Select(childState => new ChildAnimatorState
                {
                    state = childState.state,
                    position = childState.position
                })
                .ToArray();
            subStateMachine.states = subStates;
            ApplyBoundingBoxNodePositions(subStateMachine, selectedEntries.Select(childState => childState.position));

            parentSM.states = parentSM.states
                .Where(childState => !selectedStates.Contains(childState.state))
                .ToArray();

            // If the default state is among selected, assign it in the sub SM
            if (selectedStates.Contains(parentSM.defaultState))
                subStateMachine.defaultState = parentSM.defaultState;

            MarkDirty(controller, parentSM);
        }

        internal static void Unpack(AnimatorStateMachine parentSM, AnimatorStateMachine subStateMachine)
        {
            if (subStateMachine == null) return;

            var controller = GetController(parentSM);

            Undo.RegisterCompleteObjectUndo(
                new Object[] { parentSM, subStateMachine, controller }.Where(static x => x != null).ToArray(),
                "Unpack Sub State Machine");

            // States and nested sub SMs use absolute positions (set during Pack), restore directly.
            var movedStates = subStateMachine.states
                .Select(childState => new ChildAnimatorState
                {
                    state = childState.state,
                    position = childState.position
                })
                .ToArray();

            parentSM.states = parentSM.states.Concat(movedStates).ToArray();

            var movedSubSMs = subStateMachine.stateMachines
                .Select(childStateMachine => new ChildAnimatorStateMachine
                {
                    stateMachine = childStateMachine.stateMachine,
                    position = childStateMachine.position
                })
                .ToArray();

            parentSM.stateMachines = parentSM.stateMachines
                .Where(childStateMachine => childStateMachine.stateMachine != subStateMachine)
                .Concat(movedSubSMs)
                .ToArray();

            // Preserve the default state if it was inside the sub SM
            if (subStateMachine.defaultState != null && parentSM.defaultState == null)
                parentSM.defaultState = subStateMachine.defaultState;

            // Clear sub SM arrays before destroying so the moved assets are not destroyed with it
            subStateMachine.states = new ChildAnimatorState[0];
            subStateMachine.stateMachines = new ChildAnimatorStateMachine[0];

            // Destroy the now-empty sub SM sub-asset (Undo-aware)
            Undo.DestroyObjectImmediate(subStateMachine);

            MarkDirty(controller, parentSM);
        }

        // Places Entry, Any State, Exit, and parent portal above the bounding box of the given positions.
        internal static void ApplyBoundingBoxNodePositions(AnimatorStateMachine stateMachine, IEnumerable<Vector3> statePositions)
        {
            var positions = statePositions.ToArray();
            if (positions.Length == 0) return;

            var minY = positions.Min(position => position.y);
            var minX = positions.Min(position => position.x);
            var maxX = positions.Max(position => position.x);
            var centerX = (minX + maxX) * 0.5f;
            const float nodePadding = 200f;

            stateMachine.entryPosition              = new Vector3(centerX - nodePadding, minY - nodePadding, 0f);
            stateMachine.anyStatePosition           = new Vector3(centerX,               minY - nodePadding, 0f);
            stateMachine.exitPosition               = new Vector3(centerX + nodePadding, minY - nodePadding, 0f);
            stateMachine.parentStateMachinePosition = new Vector3(centerX,               minY - nodePadding * 2f, 0f);
        }

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }

        static void MarkDirty(AnimatorController controller, AnimatorStateMachine stateMachine)
        {
            if (controller != null) EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(stateMachine);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
