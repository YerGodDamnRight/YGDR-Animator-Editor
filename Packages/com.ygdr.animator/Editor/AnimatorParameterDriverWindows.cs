#if UNITY_EDITOR
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
    internal class AddParameterDriverWindow : EditorWindow
    {
        AnimatorStateMachine _stateMachine;
        AnimatorState[] _states;
        string[] _paramNames;
        Vector2 _scroll;

        readonly List<DriverEntry> _entries = new();

        struct DriverEntry
        {
            public int ParamIndex;
            public VRC_AvatarParameterDriver.ChangeType ChangeType;
            public float Value;
        }

        internal static void Open(AnimatorStateMachine stateMachine, AnimatorState[] states)
        {
            var window = GetWindow<AddParameterDriverWindow>(true, "Add Parameter Driver");
            window._stateMachine = stateMachine;
            window._states = states;
            var controller = GetController(stateMachine);
            window._paramNames = controller != null
                ? controller.parameters.Select(parameter => parameter.name).ToArray()
                : Array.Empty<string>();
            window._entries.Clear();
            window._entries.Add(new DriverEntry());
            window.minSize = new Vector2(320, 180);
            window.ShowUtility();
        }

        void OnGUI()
        {
            if (_paramNames == null || _paramNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No parameters found on controller.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"Applying to {_states?.Length ?? 0} state(s)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int removeAt = -1;
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                EditorGUILayout.BeginHorizontal();
                entry.ParamIndex = EditorGUILayout.Popup(entry.ParamIndex, _paramNames, GUILayout.MinWidth(120));
                entry.ChangeType = (VRC_AvatarParameterDriver.ChangeType)EditorGUILayout.EnumPopup(entry.ChangeType, GUILayout.Width(70));
                entry.Value = EditorGUILayout.FloatField(entry.Value, GUILayout.Width(60));

                if (GUILayout.Button("+", GUILayout.Width(22)))
                    _entries.Insert(i + 1, new DriverEntry());

                if (GUILayout.Button("−", GUILayout.Width(22)) && _entries.Count > 1)
                    removeAt = i;

                EditorGUILayout.EndHorizontal();
                _entries[i] = entry;
            }

            if (removeAt >= 0) _entries.RemoveAt(removeAt);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            if (GUILayout.Button("Apply"))
            {
                Apply();
                Close();
            }
        }

        void Apply()
        {
            if (_states == null || _entries.Count == 0) return;

            var driverParams = _entries.Select(entry => new VRC_AvatarParameterDriver.Parameter
            {
                type = entry.ChangeType,
                name = _paramNames[entry.ParamIndex],
                value = entry.Value
            }).ToList();

            foreach (var state in _states)
            {
                Undo.RegisterCompleteObjectUndo(state, "Add Parameter Driver");
                var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                Undo.RegisterCreatedObjectUndo(driver, "Add Parameter Driver");
                driver.localOnly = false;
                driver.parameters = driverParams;
                EditorUtility.SetDirty(state);
            }
        }

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }
    }

    internal class MultiTransitionConditionsWindow : EditorWindow
    {
        AnimatorStateMachine _stateMachine;
        string[] _paramNames;
        AnimatorControllerParameterType[] _paramTypes;
        Vector2 _scroll;
        string _pendingFocus;
        int _lastFocusedValControl = -1;

        static readonly AnimatorConditionMode[] NumericModes =
        {
            AnimatorConditionMode.Greater,
            AnimatorConditionMode.Less,
            AnimatorConditionMode.Equals,
            AnimatorConditionMode.NotEqual,
        };
        static readonly string[] NumericModeLabels = { "Greater", "Less", "Equals", "NotEqual" };

        readonly List<TransitionEntry> _entries = new();

        class TransitionEntry
        {
            public AnimatorStateTransition Transition;
            public string Label;
            public readonly List<ConditionEntry> Conditions = new();
        }

        struct ConditionEntry
        {
            public int ParamIndex;
            public AnimatorConditionMode Mode;
            public float Value;
        }

        internal static void Open(AnimatorStateMachine stateMachine, AnimatorStateTransition[] transitions)
        {
            var window = GetWindow<MultiTransitionConditionsWindow>(true, "Multi Transition Conditions");
            window._stateMachine = stateMachine;
            var parameters = GetController(stateMachine)?.parameters ?? Array.Empty<AnimatorControllerParameter>();
            window._paramNames = parameters.Select(parameter => parameter.name).ToArray();
            window._paramTypes = parameters.Select(parameter => parameter.type).ToArray();
            window._entries.Clear();
            foreach (var transition in transitions)
                window._entries.Add(window.BuildEntry(transition));
            window.minSize = new Vector2(320, 180);
            window.ShowUtility();
        }

        void OnEnable() => Selection.selectionChanged += OnSelectionChanged;
        void OnDisable() => Selection.selectionChanged -= OnSelectionChanged;

        void OnSelectionChanged()
        {
            var current = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            _entries.RemoveAll(entry => !current.Contains(entry.Transition));
            var existing = _entries.Select(entry => entry.Transition).ToHashSet();
            foreach (var transition in current)
            {
                if (!existing.Contains(transition))
                    _entries.Add(BuildEntry(transition));
            }
            Repaint();
        }

        TransitionEntry BuildEntry(AnimatorStateTransition transition)
        {
            var entry = new TransitionEntry { Transition = transition, Label = GetLabel(_stateMachine, transition) };
            foreach (var existingCondition in transition.conditions)
            {
                var parameterIndex = Array.IndexOf(_paramNames, existingCondition.parameter);
                entry.Conditions.Add(new ConditionEntry
                {
                    ParamIndex = parameterIndex >= 0 ? parameterIndex : 0,
                    Mode = existingCondition.mode,
                    Value = existingCondition.threshold,
                });
            }
            if (entry.Conditions.Count == 0)
                entry.Conditions.Add(new ConditionEntry());
            return entry;
        }

        void OnGUI()
        {
            if (_paramNames == null || _paramNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No parameters found on controller.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"Editing {_entries.Count} transition(s)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Apply pending focus from previous frame's Tab press (before any controls drawn)
            if (_pendingFocus != null)
            {
                EditorGUI.FocusTextInControl(_pendingFocus);
                _pendingFocus = null;
            }

            // Track last focused val control
            string currentFocus = GUI.GetNameOfFocusedControl();
            var valueControlNames = BuildValueControlNames();
            int focusedValIdx = valueControlNames.IndexOf(currentFocus);
            if (focusedValIdx >= 0) _lastFocusedValControl = focusedValIdx;

            // Intercept Tab — store target for next frame to avoid same-frame focus race
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Tab || Event.current.character == '\t'))
            {
                if (valueControlNames.Count > 0)
                {
                    int nextIdx = (_lastFocusedValControl + 1) % valueControlNames.Count;
                    _pendingFocus = valueControlNames[nextIdx];
                    _lastFocusedValControl = nextIdx;
                    GUIUtility.keyboardControl = 0;
                    Event.current.Use();
                    Repaint();
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int entryIdx = 0;
            foreach (var entry in _entries)
            {
                EditorGUILayout.LabelField(entry.Label, EditorStyles.boldLabel);

                int removeAt = -1;
                for (var i = 0; i < entry.Conditions.Count; i++)
                {
                    var conditionEntry = entry.Conditions[i];
                    EditorGUILayout.BeginHorizontal();

                    conditionEntry.ParamIndex = EditorGUILayout.Popup(conditionEntry.ParamIndex, _paramNames, GUILayout.MinWidth(100));

                    bool isBool = conditionEntry.ParamIndex < _paramTypes.Length &&
                                  _paramTypes[conditionEntry.ParamIndex] == AnimatorControllerParameterType.Bool;

                    if (isBool)
                    {
                        bool isTrue = conditionEntry.Mode != AnimatorConditionMode.IfNot;
                        isTrue = EditorGUILayout.Toggle(isTrue, GUILayout.Width(80));
                        conditionEntry.Mode = isTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;

                        var style = new GUIStyle(EditorStyles.label);
                        style.normal.textColor = isTrue ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
                        EditorGUILayout.LabelField(isTrue ? "true" : "false", style, GUILayout.Width(50));
                    }
                    else
                    {
                        var modeIdx = Array.IndexOf(NumericModes, conditionEntry.Mode);
                        if (modeIdx < 0) modeIdx = 0;
                        modeIdx = EditorGUILayout.Popup(modeIdx, NumericModeLabels, GUILayout.Width(80));
                        conditionEntry.Mode = NumericModes[modeIdx];
                        GUI.SetNextControlName($"val_{entryIdx}_{i}");
                        conditionEntry.Value = EditorGUILayout.FloatField(conditionEntry.Value, GUILayout.Width(50));
                    }

                    if (GUILayout.Button("+", GUILayout.Width(22)))
                        entry.Conditions.Insert(i + 1, new ConditionEntry());
                    if (GUILayout.Button("−", GUILayout.Width(22)) && entry.Conditions.Count > 1)
                        removeAt = i;

                    EditorGUILayout.EndHorizontal();
                    entry.Conditions[i] = conditionEntry;
                }

                if (removeAt >= 0) entry.Conditions.RemoveAt(removeAt);
                EditorGUILayout.Space(4);
                entryIdx++;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            if (GUILayout.Button("Apply"))
            {
                Apply();
                Close();
            }
        }

        List<string> BuildValueControlNames()
        {
            var names = new List<string>();
            for (int e = 0; e < _entries.Count; e++)
            {
                var entry = _entries[e];
                for (int c = 0; c < entry.Conditions.Count; c++)
                {
                    var conditionEntry = entry.Conditions[c];
                    bool isBool = conditionEntry.ParamIndex < _paramTypes.Length &&
                                  _paramTypes[conditionEntry.ParamIndex] == AnimatorControllerParameterType.Bool;
                    if (!isBool)
                        names.Add($"val_{e}_{c}");
                }
            }
            return names;
        }

        void Apply()
        {
            foreach (var entry in _entries)
            {
                Undo.RegisterCompleteObjectUndo(entry.Transition, "Multi Transition Conditions");
                foreach (var existingCondition in entry.Transition.conditions.ToArray())
                    entry.Transition.RemoveCondition(existingCondition);
                foreach (var conditionEntry in entry.Conditions)
                    entry.Transition.AddCondition(conditionEntry.Mode, conditionEntry.Value, _paramNames[conditionEntry.ParamIndex]);
                EditorUtility.SetDirty(entry.Transition);
            }
        }

        static string GetLabel(AnimatorStateMachine stateMachine, AnimatorStateTransition transition)
        {
            string sourceName;
            if (stateMachine.anyStateTransitions.Contains(transition))
                sourceName = "Any State";
            else
            {
                var sourceState = stateMachine.states.FirstOrDefault(x => x.state.transitions.Contains(transition)).state;
                sourceName = sourceState?.name ?? "?";
            }

            string destinationName;
            if (transition.isExit) destinationName = "Exit";
            else if (transition.destinationState != null) destinationName = transition.destinationState.name;
            else if (transition.destinationStateMachine != null) destinationName = transition.destinationStateMachine.name;
            else destinationName = "?";

            return $"{sourceName} → {destinationName}";
        }

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var path = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }
    }

    internal class RemoveParameterDriverWindow : EditorWindow
    {
        AnimatorState[] _states;
        string[] _paramNames;
        string[] _displayNames;
        Vector2 _scroll;

        readonly List<int> _selections = new(); // -1 = none

        internal static void Open(AnimatorStateMachine stateMachine, AnimatorState[] states)
        {
            var window = GetWindow<RemoveParameterDriverWindow>(true, "Remove Parameter Drivers");
            window._states = states;
            var controller = GetController(stateMachine);
            window._paramNames = controller != null
                ? controller.parameters.Select(parameter => parameter.name).ToArray()
                : Array.Empty<string>();
            window._displayNames = new[] { "— none —" }.Concat(window._paramNames).ToArray();
            window._selections.Clear();
            window._selections.Add(-1);
            window.minSize = new Vector2(320, 180);
            window.ShowUtility();
        }

        void OnGUI()
        {
            if (_paramNames == null || _paramNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No parameters found on controller.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"Removing from {_states?.Length ?? 0} state(s)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int removeAt = -1;
            for (var i = 0; i < _selections.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                // +1 offset: display index 0 = "— none —", 1..n = params
                var currentSelection = _selections[i];
                var newSelection = EditorGUILayout.Popup(currentSelection + 1, _displayNames) - 1;
                if (newSelection != currentSelection)
                {
                    // Prevent duplicate selections
                    bool isDuplicate = false;
                    if (newSelection >= 0)
                    {
                        for (int j = 0; j < _selections.Count; j++)
                        {
                            if (j != i && _selections[j] == newSelection)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }
                    }

                    if (!isDuplicate)
                    {
                        _selections[i] = newSelection;
                    }
                }

                if (GUILayout.Button("+", GUILayout.Width(22)))
                    _selections.Insert(i + 1, -1);

                if (GUILayout.Button("−", GUILayout.Width(22)))
                    removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                _selections.RemoveAt(removeAt);
                if (_selections.Count == 0) _selections.Add(-1);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            var selected = _selections
                .Where(selection => selection >= 0)
                .Select(selection => _paramNames[selection])
                .ToHashSet();

            GUI.enabled = selected.Count > 0;
            if (GUILayout.Button("Apply"))
            {
                Apply(selected);
                Close();
            }
            GUI.enabled = true;
        }

        void Apply(HashSet<string> paramNames)
        {
            if (_states == null || paramNames.Count == 0) return;

            foreach (var state in _states)
            {
                var toRemove = state.behaviours
                    .OfType<VRCAvatarParameterDriver>()
                    .Where(driver => driver.parameters.Any(x => paramNames.Contains(x.name)))
                    .ToArray();

                if (toRemove.Length == 0) continue;

                Undo.RegisterCompleteObjectUndo(state, "Remove Parameter Drivers");
                state.behaviours = state.behaviours
                    .Where(behaviour => !toRemove.Contains(behaviour))
                    .ToArray();
                foreach (var driver in toRemove)
                    Undo.DestroyObjectImmediate(driver);
                EditorUtility.SetDirty(state);
            }
        }

        static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }
    }
}
#endif
