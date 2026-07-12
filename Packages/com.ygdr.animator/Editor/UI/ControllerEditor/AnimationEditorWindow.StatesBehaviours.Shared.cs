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
        /* Multi-instance accessors (Phase 1 step 1) for the 4 in-scope behaviour types
           (Driver, PlayAudio, LayerControl, PlayableLayerControl). Instances<T>(state)[0]
           == today's GetXForState(state) — N=0/1 cases are unaffected.
           Allocates a List — only use where the full set is actually needed (e.g. destroying
           all instances of a type). Per-frame lookups should use HasInstance/InstanceCount/
           FindInstance/InstanceAt below, which scan state.behaviours directly with no allocation. */
        static List<T> Instances<T>(AnimatorState state) where T : StateMachineBehaviour
            => state.behaviours.OfType<T>().ToList();

        static bool HasInstance<T>(AnimatorState state) where T : StateMachineBehaviour
        {
            foreach (var b in state.behaviours)
                if (b is T) return true;
            return false;
        }

        static int InstanceCount<T>(AnimatorState state) where T : StateMachineBehaviour
        {
            int count = 0;
            foreach (var b in state.behaviours)
                if (b is T) count++;
            return count;
        }

        static T FindInstance<T>(AnimatorState state, string name) where T : StateMachineBehaviour
        {
            foreach (var b in state.behaviours)
                if (b is T typed && typed.name == name) return typed;
            return null;
        }

        static T InstanceAt<T>(AnimatorState state, int index) where T : StateMachineBehaviour
        {
            int i = 0;
            foreach (var b in state.behaviours)
            {
                if (!(b is T typed)) continue;
                if (i == index) return typed;
                i++;
            }
            return null;
        }

        /* Groups instances of T across states by name in a single pass (no per-name re-scan of every
           state), preserving first-seen name order. Replaces the "namesUnion.Distinct() then re-filter
           states per name" pattern, which is O(names x states) — this is O(states x instancesPerState). */
        static List<(string name, AnimatorState[] states)> GroupInstancesByName<T>(AnimatorState[] selectedStates) where T : StateMachineBehaviour
        {
            var order = new List<string>();
            var statesByName = new Dictionary<string, List<AnimatorState>>();
            foreach (var state in selectedStates)
            {
                foreach (var b in state.behaviours)
                {
                    if (!(b is T typed)) continue;
                    if (!statesByName.TryGetValue(typed.name, out var list))
                    {
                        list = new List<AnimatorState>();
                        statesByName[typed.name] = list;
                        order.Add(typed.name);
                    }
                    if (list.Count == 0 || list[list.Count - 1] != state) list.Add(state);
                }
            }

            var result = new List<(string name, AnimatorState[] states)>(order.Count);
            foreach (var name in order) result.Add((name, statesByName[name].ToArray()));
            return result;
        }

        /* Adds a new instance of T to state, auto-named "{typeLabel} {N}" (N = existing count + 1). */
        static T AddInstance<T>(AnimatorState state, string typeLabel) where T : StateMachineBehaviour
        {
            int count = Instances<T>(state).Count;
            var instance = state.AddStateMachineBehaviour<T>();
            instance.name = $"{typeLabel} {count + 1}";
            Undo.RegisterCreatedObjectUndo(instance, $"Add {typeLabel}");
            EditorUtility.SetDirty(state);
            return instance;
        }

        /* Draws a foldout header for one named instance: expand arrow, editable name field, ↑/↓ reorder
           buttons, "−" remove button. Renames on the spot (rejects blank names — pairing requires a
           non-blank name, see plan §7). isFirst/isLast (position within the grouped-by-name list, not
           the raw behaviours array) gray out the arrow that would be a no-op.
           Returns true if removal was requested; caller destroys the instance(s) and clears its own caches. */
        bool DrawInstanceFoldoutHeader<T>(string name, AnimatorState[] statesWithName, Dictionary<string, bool> expandedByName, bool isFirst, bool isLast, out bool expanded, out bool moveUp, out bool moveDown) where T : StateMachineBehaviour
        {
            expanded = !expandedByName.TryGetValue(name, out var stored) || stored;
            bool removeRequested = false;
            moveUp = false;
            moveDown = false;

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                var foldoutRect = EditorGUILayout.GetControlRect(false, 24, GUILayout.Width(16));
                bool newExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (newExpanded != expanded)
                {
                    expandedByName[name] = newExpanded;
                    expanded = newExpanded;
                }

                // The name is the grouping key — every instance in statesWithName shares it exactly, so this
                // field must never show mixed. Force it off in case a prior field left showMixedValue leaked true.
                EditorGUI.showMixedValue = false;
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.DelayedTextField(name, GUILayout.ExpandWidth(true), GUILayout.Height(24));
                if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName) && newName != name)
                {
                    foreach (var state in statesWithName)
                    {
                        var instance = FindInstance<T>(state, name);
                        if (instance == null) continue;
                        Undo.RecordObject(instance, "Rename Instance");
                        instance.name = newName;
                        EditorUtility.SetDirty(instance);
                    }
                    expandedByName.Remove(name);
                    expandedByName[newName] = expanded;
                }

                using (new EditorGUI.DisabledScope(isFirst))
                    if (CursorBtn("↑", Styles.IconBtn, GUILayout.Width(18), GUILayout.Height(24)))
                        moveUp = true;
                using (new EditorGUI.DisabledScope(isLast))
                    if (CursorBtn("↓", Styles.IconBtn, GUILayout.Width(18), GUILayout.Height(24)))
                        moveDown = true;

                if (CursorBtn("−", Styles.IconBtn, GUILayout.Width(24), GUILayout.Height(24)))
                    removeRequested = true;
            }

            return removeRequested;
        }

        /* Destroys the instance named `name` on every state in statesWithName. */
        void RemoveNamedInstance<T>(string name, AnimatorState[] statesWithName) where T : StateMachineBehaviour
        {
            foreach (var state in statesWithName)
            {
                var instance = FindInstance<T>(state, name);
                if (instance == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove Instance");
                state.behaviours = state.behaviours.Where(b => b != instance).ToArray();
                Undo.DestroyObjectImmediate(instance);
                EditorUtility.SetDirty(state);
            }
        }

        /* Swaps the named instance with its nearest same-type neighbor in state.behaviours (direction
           -1 = up, +1 = down), skipping over other behaviour types so the swap actually changes this
           instance's position among same-type rows in the UI. No-op per-state at the edge. */
        static void MoveNamedInstance<T>(string name, AnimatorState[] statesWithName, int direction) where T : StateMachineBehaviour
        {
            foreach (var state in statesWithName)
            {
                var behaviours = state.behaviours;
                var instance = FindInstance<T>(state, name);
                if (instance == null) continue;
                int index = Array.IndexOf(behaviours, instance);

                int neighborIndex = index + direction;
                while (neighborIndex >= 0 && neighborIndex < behaviours.Length && !(behaviours[neighborIndex] is T))
                    neighborIndex += direction;
                if (neighborIndex < 0 || neighborIndex >= behaviours.Length) continue;

                Undo.RegisterCompleteObjectUndo(state, "Reorder Instance");
                (behaviours[index], behaviours[neighborIndex]) = (behaviours[neighborIndex], behaviours[index]);
                state.behaviours = behaviours;
                EditorUtility.SetDirty(state);
            }
        }

        void DrawBoolToggleButtons(bool currentValue, bool isMixed, string trueLabel, string falseLabel, float buttonWidth, Action<bool> onChanged)
        {
            var prevContentColor = GUI.contentColor;
            GUI.contentColor = isMixed ? Color.gray : currentValue ? Color.green : Color.gray;
            if (CursorBtn(trueLabel, EditorStyles.miniButton, GUILayout.Width(buttonWidth)) && (isMixed || !currentValue))
                onChanged(true);
            GUILayout.Space(2f);
            GUI.contentColor = isMixed ? Color.gray : !currentValue ? Color.green : Color.gray;
            if (CursorBtn(falseLabel, EditorStyles.miniButton, GUILayout.Width(buttonWidth)) && (isMixed || currentValue))
                onChanged(false);
            GUI.contentColor = prevContentColor;
        }
    }
}
#endif
