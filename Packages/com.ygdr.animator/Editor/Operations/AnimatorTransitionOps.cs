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
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorTransitionOps
    {
        internal struct TransitionData
        {
            internal bool hasExitTime;
            internal float exitTime;
            internal float duration;
            internal float offset;
            internal TransitionInterruptionSource interruptionSource;
            internal bool orderedInterruption;
            internal bool canTransitionToSelf;
            internal bool mute;
            internal bool solo;
            internal AnimatorCondition[] conditions;

            internal static TransitionData From(AnimatorStateTransition transition) => new TransitionData
            {
                hasExitTime         = transition.hasExitTime,
                exitTime            = transition.exitTime,
                duration            = transition.duration,
                offset              = transition.offset,
                interruptionSource  = transition.interruptionSource,
                orderedInterruption = transition.orderedInterruption,
                canTransitionToSelf = transition.canTransitionToSelf,
                mute                = transition.mute,
                solo                = transition.solo,
                conditions          = transition.conditions.ToArray(),
            };
        }

        internal static void PasteTransitions(AnimatorState source, AnimatorState destination, TransitionData[] clipboard)
        {
            Undo.RegisterCompleteObjectUndo(source, "Paste Transitions");
            foreach (var template in clipboard)
            {
                var newTransition = source.AddTransition(destination);
                CopySettings(newTransition, template);
            }
            EditorUtility.SetDirty(source);
        }

        internal static void PasteExitTransitions(AnimatorStateMachine sm, AnimatorState[] sources, TransitionData[] clipboard)
        {
            Undo.RegisterCompleteObjectUndo(sources.Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[] { sm }).ToArray(), "Paste Exit Transitions");
            foreach (var sourceState in sources)
                foreach (var template in clipboard)
                {
                    var newTransition = sourceState.AddExitTransition();
                    Undo.RegisterCreatedObjectUndo(newTransition, "Paste Exit Transitions");
                    CopySettings(newTransition, template);
                }
            foreach (var sourceState in sources) EditorUtility.SetDirty(sourceState);
            EditorUtility.SetDirty(sm);
        }

        internal static void PasteAnyStateTransitions(AnimatorStateMachine sm, AnimatorState destination, TransitionData[] clipboard)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Paste AnyState Transitions");
            foreach (var template in clipboard)
            {
                var newTransition = sm.AddAnyStateTransition(destination);
                Undo.RegisterCreatedObjectUndo(newTransition, "Paste AnyState Transitions");
                CopySettings(newTransition, template);
            }
            EditorUtility.SetDirty(sm);
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        internal static void PasteEntryTransitions(AnimatorStateMachine sm, AnimatorState destination, TransitionData[] clipboard)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Paste Entry Transitions");
            foreach (var template in clipboard)
                CopySettings(sm.AddEntryTransition(destination), template);
            EditorUtility.SetDirty(sm);
            AnimatorBulkTransitionOps.RebuildAnimatorGraph();
        }

        internal static void CopySettings(AnimatorStateTransition destination, AnimatorStateTransition source)
        {
            destination.hasExitTime         = source.hasExitTime;
            destination.exitTime            = source.exitTime;
            destination.duration            = source.duration;
            destination.offset              = source.offset;
            destination.interruptionSource  = source.interruptionSource;
            destination.orderedInterruption = source.orderedInterruption;
            destination.canTransitionToSelf = source.canTransitionToSelf;
            destination.mute                = source.mute;
            destination.solo                = source.solo;
            destination.conditions          = source.conditions;
        }

        internal static void CopySettings(AnimatorTransition destination, AnimatorTransition source)
        {
            destination.mute       = source.mute;
            destination.solo       = source.solo;
            destination.conditions = source.conditions;
        }

        internal static void CopySettings(AnimatorTransition destination, AnimatorStateTransition source)
        {
            destination.mute       = source.mute;
            destination.solo       = source.solo;
            destination.conditions = source.conditions;
        }

        internal static void CopySettings(AnimatorStateTransition destination, AnimatorTransition source)
        {
            destination.mute       = source.mute;
            destination.solo       = source.solo;
            destination.conditions = source.conditions;
        }

        internal static void CopySettings(AnimatorTransition destination, TransitionData source)
        {
            destination.mute       = source.mute;
            destination.solo       = source.solo;
            destination.conditions = source.conditions;
        }

        internal static void CopySettings(AnimatorStateTransition destination, TransitionData source)
        {
            destination.hasExitTime         = source.hasExitTime;
            destination.exitTime            = source.exitTime;
            destination.duration            = source.duration;
            destination.offset              = source.offset;
            destination.interruptionSource  = source.interruptionSource;
            destination.orderedInterruption = source.orderedInterruption;
            destination.canTransitionToSelf = source.canTransitionToSelf;
            destination.mute                = source.mute;
            destination.solo                = source.solo;
            destination.conditions          = source.conditions;
        }
    }
}
#endif
