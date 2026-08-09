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
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        /* Allocates a List — only use where the full set is needed (e.g. destroying all instances of a type).
           Per-frame lookups should use HasInstance/InstanceCount/FindInstance/InstanceAt below (no allocation). */
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

        /* Single pass, O(states x instancesPerState) — replaces an O(names x states) "distinct then re-filter" pattern. */
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
            EnsureUniqueName(state, instance);
            Undo.RegisterCreatedObjectUndo(instance, $"Add {typeLabel}");
            EditorUtility.SetDirty(state);
            return instance;
        }

        /* Renames newBehavior to "{name} (N)" if another behaviour of the same type on state already has that name. */
        internal static void EnsureUniqueName(AnimatorState state, StateMachineBehaviour newBehavior)
        {
            var taken = new HashSet<string>(state.behaviours
                .Where(b => b != newBehavior && b.GetType() == newBehavior.GetType())
                .Select(b => b.name));
            if (!taken.Contains(newBehavior.name)) return;

            var baseName = newBehavior.name;
            int suffix = 1;
            string candidate;
            do candidate = $"{baseName} ({++suffix})";
            while (taken.Contains(candidate));
            newBehavior.name = candidate;
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

        /* direction -1 = up, +1 = down; skips over other behaviour types so the swap changes UI row position. No-op per-state at the edge. */
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

        // ── Native (UI Toolkit) equivalents, used by the migrated behavior sections below. ──────

        /* Unlike the IMGUI version, remove/move/rename call straight into RemoveNamedInstance/MoveNamedInstance then
           onMutated, since retained-mode rendering has no equivalent to the old "return early, next OnGUI redraws" trick. */
        VisualElement BuildInstanceFoldoutHeader<T>(string name, AnimatorState[] statesWithName, Dictionary<string, bool> expandedByName,
            bool isFirst, bool isLast, out bool expanded, Action<bool> onExpandToggled, Action onMutated,
            string extraButtonIcon = null, bool extraButtonEnabled = false, Action extraButtonClicked = null,
            string extraButton2Icon = null, bool extraButton2Enabled = false, Action extraButton2Clicked = null) where T : StateMachineBehaviour
        {
            var header = new VisualElement();
            header.AddToClassList("ygdr-behavior-foldout-header");
            header.style.backgroundColor = SharedWindowStyles.SecondaryColor;

            bool isExpanded = !expandedByName.TryGetValue(name, out var stored) || stored;
            expanded = isExpanded;

            var foldoutArrow = new VisualElement();
            foldoutArrow.AddToClassList("ygdr-behavior-foldout-arrow-icon");
            foldoutArrow.style.backgroundImage = new StyleBackground(DropdownArrowIconTex);
            foldoutArrow.style.rotate = new StyleRotate(new Rotate(isExpanded ? Angle.Degrees(0f) : Angle.Degrees(-90f)));
            foldoutArrow.RegisterCallback<ClickEvent>(_ =>
            {
                isExpanded = !isExpanded;
                expandedByName[name] = isExpanded;
                foldoutArrow.style.rotate = new StyleRotate(new Rotate(isExpanded ? Angle.Degrees(0f) : Angle.Degrees(-90f)));
                onExpandToggled(isExpanded);
            });
            header.Add(foldoutArrow);

            var nameField = new TextField { value = name, isDelayed = true };
            nameField.AddToClassList("ygdr-behavior-foldout-name");
            nameField.RegisterValueChangedCallback(evt =>
            {
                string newName = evt.newValue;
                if (string.IsNullOrEmpty(newName) || newName == name) { nameField.SetValueWithoutNotify(name); return; }
                foreach (var state in statesWithName)
                {
                    var instance = FindInstance<T>(state, name);
                    if (instance == null) continue;
                    Undo.RecordObject(instance, "Rename Instance");
                    instance.name = newName;
                    EditorUtility.SetDirty(instance);
                }
                expandedByName.Remove(name);
                expandedByName[newName] = isExpanded;
                onMutated();
            });
            header.Add(nameField);

            if (extraButtonIcon != null)
            {
                var extraButton = new Button(() => extraButtonClicked?.Invoke()) { text = extraButtonIcon };
                extraButton.SetEnabled(extraButtonEnabled);
                extraButton.AddToClassList("ygdr-behavior-icon-btn");
                StyleSecondaryButton(extraButton);
                header.Add(extraButton);
            }

            if (extraButton2Icon != null)
            {
                var extraButton2 = new Button(() => extraButton2Clicked?.Invoke()) { text = extraButton2Icon };
                extraButton2.SetEnabled(extraButton2Enabled);
                extraButton2.AddToClassList("ygdr-behavior-icon-btn");
                StyleSecondaryButton(extraButton2);
                header.Add(extraButton2);
            }

            var upButton = new Button(() => { MoveNamedInstance<T>(name, statesWithName, -1); onMutated(); }) { text = "↑" };
            upButton.SetEnabled(!isFirst);
            upButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(upButton);
            header.Add(upButton);

            var downButton = new Button(() => { MoveNamedInstance<T>(name, statesWithName, 1); onMutated(); }) { text = "↓" };
            downButton.SetEnabled(!isLast);
            downButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(downButton);
            header.Add(downButton);

            var removeButton = new Button(() => { RemoveNamedInstance<T>(name, statesWithName); onMutated(); }) { text = "−" };
            removeButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(removeButton);
            header.Add(removeButton);

            return header;
        }

        /* content is the field body directly for single-instance sections (Tracking/Locomotion/PoseSpace), or a
           rows container with one foldout per named instance for multi-instance sections (LayerControl/PlayableLayer).
           No per-section "Add" button — sections stay hidden until populated via the single top-level Add Behavior dropdown. */
        static VisualElement BuildBehaviorSectionShell(string label, out Button removeButton, out VisualElement content)
        {
            var section = new VisualElement();
            section.AddToClassList("ygdr-behavior-section");

            var header = new VisualElement();
            header.AddToClassList("ygdr-behavior-section-header");
            header.style.backgroundColor = SharedWindowStyles.AccentColor;
            var headerLabel = new Label(label);
            headerLabel.AddToClassList("ygdr-behavior-section-label");
            header.Add(headerLabel);

            removeButton = new Button { text = L10n.Get("vrc.remove_all") };
            removeButton.AddToClassList("ygdr-behavior-header-btn");
            StyleSecondaryButton(removeButton);
            header.Add(removeButton);

            section.Add(header);

            content = new VisualElement();
            content.AddToClassList("ygdr-behavior-section-rows");
            section.Add(content);

            return section;
        }

        /* _sharedBehaviorsContainer is built once, so unlike other panels nothing here auto re-applies a later
           palette edit. Re-style by class name (same pattern as Settings.cs) instead of threading header refs. */
        void RefreshSharedBehaviorsPaletteColors()
        {
            if (_sharedBehaviorsContainer == null) return;
            // Section shell (header/add-all/remove-all) is never rebuilt, only restyled here.
            _sharedBehaviorsContainer.Query<VisualElement>(className: "ygdr-behavior-section-header").ForEach(h => h.style.backgroundColor = SharedWindowStyles.AccentColor);
            _sharedBehaviorsContainer.Query<Button>(className: "ygdr-behavior-header-btn").ForEach(b => b.style.backgroundColor = SharedWindowStyles.SecondaryColor);
            // Rows/bodies/dropdowns below the shell ARE rebuilt on selection change, so the simplest
            // way to repaint them with live colors is to rebuild them now instead of re-deriving every
            // row's alt-banding and dropdown state via Query.
            RefreshDriverBody();
            RefreshAudioBody();
            RefreshLayerControlBody();
            RefreshOtherBehaviorsBody();
        }

        /* Caller's onChanged must both mutate data and trigger whatever refresh keeps the tint in sync — no automatic re-render. */
        static VisualElement BuildBoolToggleButtonsField(bool currentValue, bool isMixed, string trueLabel, string falseLabel, Action<bool> onChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-bool-toggle-row");

            var trueButton = new Button(() => { if (isMixed || !currentValue) onChanged(true); }) { text = trueLabel };
            trueButton.AddToClassList("ygdr-bool-toggle-btn");
            trueButton.style.color = isMixed ? Color.gray : currentValue ? Color.green : Color.gray;
            row.Add(trueButton);

            var falseButton = new Button(() => { if (isMixed || currentValue) onChanged(false); }) { text = falseLabel };
            falseButton.AddToClassList("ygdr-bool-toggle-btn");
            falseButton.style.color = isMixed ? Color.gray : !currentValue ? Color.green : Color.gray;
            row.Add(falseButton);

            return row;
        }

        /* Stand-in for EditorGUILayout.Popup(int, string[]) where labels are localized text, not raw enum member names — a plain EnumField would show the wrong text. */
        static Button BuildLocalizedIndexDropdown(int currentIndex, bool mixed, string[] labels, Action<int> onChanged)
        {
            var button = new Button { text = mixed ? "—" : (currentIndex >= 0 && currentIndex < labels.Length ? labels[currentIndex] : "") };
            StyleAccentButton(button);
            button.clicked += () =>
            {
                var menu = new GenericMenu();
                for (int i = 0; i < labels.Length; i++)
                {
                    int capturedIndex = i;
                    menu.AddItem(new GUIContent(labels[capturedIndex]), !mixed && capturedIndex == currentIndex, () =>
                    {
                        onChanged(capturedIndex);
                        button.text = labels[capturedIndex];
                    });
                }
                menu.ShowAsContext();
            };
            return button;
        }

        /* EnumField/PopupField don't expose the input as the field itself, so StyleAccentButton needs to target the actual clickable box. */
        static void StyleAccentPopupField(VisualElement field)
        {
            var input = field.Q(className: "unity-base-popup-field__input")
                ?? field.Q(className: "unity-enum-field__input")
                ?? field.Q(className: "unity-popup-field__input");
            if (input != null) StyleAccentButton(input);
        }

        /* Single entry point for adding a shared behavior — mirrors native Unity's "Add Behaviour" menu.
           Multi-instance types (Driver/Audio/LayerControl/PlayableLayer) always listed since duplicates are valid.
           Singleton types (Tracking/Locomotion/PoseSpace) drop out once every selected state already has one —
           further edits happen in the section below instead. */
        Button BuildAddBehaviorDropdownButton()
        {
            var button = new Button { text = "+ " + L10n.Get("states.add_behavior") };
            button.AddToClassList("ygdr-add-behavior-btn");
            StyleAccentButton(button);
            button.clicked += () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(L10n.Get("vrc.param_driver")), false, AddDriverBehaviorToSelected);
                menu.AddItem(new GUIContent(L10n.Get("vrc.audio")), false, AddAudioBehaviorToSelected);
                menu.AddItem(new GUIContent(L10n.Get("vrc.layer_control")), false, AddLayerControlBehaviorToSelected);
                menu.AddItem(new GUIContent(L10n.Get("vrc.playable_layer")), false, AddPlayableLayerBehaviorToSelected);

                bool allHaveTracking = _selectedStates.Length > 0 && _selectedStates.All(state => GetTrackingForState(state) != null);
                if (!allHaveTracking) menu.AddItem(new GUIContent(L10n.Get("vrc.tracking")), false, AddTrackingBehaviorToSelected);

                bool allHaveLocomotion = _selectedStates.Length > 0 && _selectedStates.All(state => GetLocomotionForState(state) != null);
                if (!allHaveLocomotion) menu.AddItem(new GUIContent(L10n.Get("vrc.locomotion")), false, AddLocomotionBehaviorToSelected);

                bool allHavePoseSpace = _selectedStates.Length > 0 && _selectedStates.All(state => GetPoseSpaceForState(state) != null);
                if (!allHavePoseSpace) menu.AddItem(new GUIContent(L10n.Get("vrc.pose_space")), false, AddPoseSpaceBehaviorToSelected);

                menu.ShowAsContext();
            };
            return button;
        }

        static VisualElement BuildBehaviorFieldRow(string label, string tooltip, VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var labelElement = new Label(label) { tooltip = tooltip };
            labelElement.AddToClassList("ygdr-behavior-field-label");
            row.Add(labelElement);
            field.AddToClassList("ygdr-behavior-field-value");
            field.AddToClassList("u-flex-fill");
            field.AddToClassList("u-mr-4");
            row.Add(field);
            return row;
        }

        /* Destroys every instance of T (all names) on every given state. Caller still clears its own
           foldout-expanded dictionaries afterward since those are section-specific. */
        static void RemoveAllInstancesOfType<T>(AnimatorState[] states, string undoName) where T : StateMachineBehaviour
        {
            foreach (var state in states)
            {
                var instances = Instances<T>(state);
                if (instances.Count == 0) continue;
                Undo.RegisterCompleteObjectUndo(state, undoName);
                state.behaviours = state.behaviours.Where(b => !(b is T)).ToArray();
                foreach (var instance in instances) Undo.DestroyObjectImmediate(instance);
                EditorUtility.SetDirty(state);
            }
        }

        /* Shifts array[oldIndex] to newIndex, preserving the order of everything between. */
        static void MoveArrayElement<T>(T[] array, int oldIndex, int newIndex)
        {
            T item = array[oldIndex];
            if (oldIndex < newIndex)
                Array.Copy(array, oldIndex + 1, array, oldIndex, newIndex - oldIndex);
            else
                Array.Copy(array, newIndex, array, newIndex + 1, oldIndex - newIndex);
            array[newIndex] = item;
        }

        /* Shared by every reorderable ListView in this UI (Driver params, Audio clips, Menu controls). moveData
           mutates the real data synchronously (plain C# — safe) so any rebind before the deferred rebuild runs
           still shows the dropped order. rebuild (which destroys/recreates the ListView) must be deferred via
           delayCall — doing that inside ListViewDraggerAnimated.OnDrop, which still touches its own element after
           this returns, throws a NullReferenceException in DragAndDropUtility once the dragger resumes. */
        static void WireListViewReorder(ListView listView, Action<int, int> moveData, Action rebuild)
        {
            listView.itemIndexChanged += (oldIndex, newIndex) =>
            {
                moveData(oldIndex, newIndex);
                EditorApplication.delayCall += () => rebuild();
            };
        }
    }
}
#endif
