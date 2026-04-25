#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── Transitions tab ───────────────────────────────────────────────────

        void DrawTransitionsTab()
        {
            int count = _selectedTransitions.Length;

            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                GUILayout.FlexibleSpace();
                if (CursorBtn("✕", Styles.HeaderCloseBtn, GUILayout.Width(20), GUILayout.Height(24)))
                    Selection.objects = Array.Empty<UnityEngine.Object>();
                GUILayout.Label($"Editing {count} Transitions", Styles.HeaderLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
            }

            if (count > 0)
                DrawPills();

            EditorGUILayout.Space(4);
            DrawProperties();
            EditorGUILayout.Space(4);
            DrawConditionsSection();
        }

        // ── Pills ─────────────────────────────────────────────────────────────

        void DrawPills()
        {
            const float pillH = 20f;
            const float gap = 4f;
            float areaW = position.width - 8f;
            float totalH = CalcPillsHeight(areaW, pillH, gap);
            var area = EditorGUILayout.GetControlRect(false, totalH + gap);

            float currentX = 4f, currentY = 2f;
            for (int i = 0; i < _selectedTransitions.Length; i++)
            {
                var transition = _selectedTransitions[i];
                string label = GetTransitionLabel(transition);
                float pillW = Mathf.Clamp(Styles.PillLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, areaW);

                if (currentX + pillW > areaW && currentX > 4f) { currentX = 4f; currentY += pillH + gap; }

                var pill = new Rect(area.x + currentX, area.y + currentY, pillW, pillH);
                EditorGUI.DrawRect(pill, Styles.PillBg);

                if (CursorBtn(new Rect(pill.x + 2f, pill.y + 2f, 16f, 16f), "✕", Styles.PillBtn))
                {
                    Selection.objects = _selectedTransitions.Where(x => x != transition).Cast<UnityEngine.Object>().ToArray();
                    return;
                }

                GUI.Label(new Rect(pill.x + 20f, pill.y, pillW - 22f, pillH), label, Styles.PillLabel);
                currentX += pillW + gap;
            }
        }

        float CalcPillsHeight(float areaW, float pillH, float gap)
        {
            float currentX = 4f;
            int rows = 1;
            foreach (var transition in _selectedTransitions)
            {
                string label = GetTransitionLabel(transition);
                float pillW = Mathf.Clamp(Styles.PillLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, areaW);
                if (currentX + pillW > areaW && currentX > 4f) { currentX = 4f; rows++; }
                currentX += pillW + gap;
            }
            return rows * (pillH + gap);
        }

        string GetTransitionLabel(AnimatorStateTransition transition)
        {
            string sourceName = FindSrcName(_controller, transition) ?? "Any State";
            string destinationName = transition.isExit ? "Exit"
                : transition.destinationState != null ? transition.destinationState.name
                : transition.destinationStateMachine != null ? transition.destinationStateMachine.name
                : "?";
            return $"{sourceName} → {destinationName}";
        }

        static string FindSrcName(AnimatorController controller, AnimatorStateTransition transition)
        {
            if (controller == null) return null;
            foreach (var layer in controller.layers)
            {
                var name = FindSrcInSM(layer.stateMachine, transition);
                if (name != null) return name;
            }
            return null;
        }

        static string FindSrcInSM(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            if (sm.anyStateTransitions.Contains(transition)) return "Any State";
            foreach (var childState in sm.states)
                if (childState.state.transitions.Contains(transition)) return childState.state.name;
            foreach (var childSM in sm.stateMachines)
            {
                var found = FindSrcInSM(childSM.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }

        // ── Property rows ─────────────────────────────────────────────────────

        void DrawProperties()
        {
            int count = _selectedTransitions.Length;
            bool multi = count > 1;
            bool empty = count == 0;
            var first = empty ? null : _selectedTransitions[0];

            using var disabled = new EditorGUI.DisabledScope(empty);

            // Has Exit Time | Exit Time
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Has Exit Time", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.hasExitTime != first.hasExitTime));
                EditorGUI.BeginChangeCheck();
                bool newHasExit = EditorGUILayout.Toggle(empty ? false : first.hasExitTime, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.hasExitTime = newHasExit);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Exit Time", GUILayout.Width(120));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.exitTime, first.exitTime)));
                EditorGUI.BeginChangeCheck();
                float newExitTime = EditorGUILayout.FloatField(empty ? 0f : first.exitTime);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.exitTime = newExitTime);
                EditorGUI.showMixedValue = false;
            }

            // Has Fixed Duration | Transition Duration
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Has Fixed Duration", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.hasFixedDuration != first.hasFixedDuration));
                EditorGUI.BeginChangeCheck();
                bool newFixed = EditorGUILayout.Toggle(empty ? false : first.hasFixedDuration, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.hasFixedDuration = newFixed);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Transition Duration", GUILayout.Width(120));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.duration, first.duration)));
                EditorGUI.BeginChangeCheck();
                float newDuration = EditorGUILayout.FloatField(empty ? 0f : first.duration);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.duration = newDuration);
                EditorGUI.showMixedValue = false;
            }

            // Transition Offset
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Transition Offset", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.offset, first.offset)));
                EditorGUI.BeginChangeCheck();
                float newOffset = EditorGUILayout.FloatField(empty ? 0f : first.offset);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.offset = newOffset);
                EditorGUI.showMixedValue = false;
            }

            // Interruption Source
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Interruption Source", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.interruptionSource != first.interruptionSource));
                EditorGUI.BeginChangeCheck();
                var newInterruptionSource = (TransitionInterruptionSource)EditorGUILayout.EnumPopup(empty ? default : first.interruptionSource);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.interruptionSource = newInterruptionSource);
                EditorGUI.showMixedValue = false;
            }

            // Ordered Interruption | Mute
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Ordered Interruption", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.orderedInterruption != first.orderedInterruption));
                EditorGUI.BeginChangeCheck();
                bool newOrdered = EditorGUILayout.Toggle(empty ? false : first.orderedInterruption, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.orderedInterruption = newOrdered);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Mute", GUILayout.Width(80));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.mute != first.mute));
                EditorGUI.BeginChangeCheck();
                bool newMute = EditorGUILayout.Toggle(empty ? false : first.mute, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.mute = newMute);
                EditorGUI.showMixedValue = false;
            }

            // Can Transition To Self | Solo
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Can Transition To Self", GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.canTransitionToSelf != first.canTransitionToSelf));
                EditorGUI.BeginChangeCheck();
                bool newSelf = EditorGUILayout.Toggle(empty ? false : first.canTransitionToSelf, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.canTransitionToSelf = newSelf);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Solo", GUILayout.Width(80));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.solo != first.solo));
                EditorGUI.BeginChangeCheck();
                bool newSolo = EditorGUILayout.Toggle(empty ? false : first.solo, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.solo = newSolo);
                EditorGUI.showMixedValue = false;
            }
        }

        // ── Conditions section ────────────────────────────────────────────────

        void DrawConditionsSection()
        {
            // Header — not part of the padded/colored section
            var headerRect = EditorGUILayout.GetControlRect(false, 22f);
            float parameterColumnWidth = headerRect.width * 0.5f;
            float modeColumnWidth  = headerRect.width * 0.25f;
            float splitColumnWidth = (headerRect.width - parameterColumnWidth - modeColumnWidth) * 0.5f;

            if (Event.current.type == EventType.Repaint)
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);

            string modeLabel = _showSharedConditions ? "Shared Conditions" : "All Conditions";
            if (CursorBtn(new Rect(headerRect.x, headerRect.y, parameterColumnWidth, headerRect.height), new GUIContent("  " + modeLabel, "Toggle All / Shared conditions"), Styles.CondModeBtn))
                _showSharedConditions = !_showSharedConditions;

            float rightColumnX = headerRect.x + parameterColumnWidth;
            if (CursorBtn(new Rect(rightColumnX,                                       headerRect.y, modeColumnWidth,  headerRect.height), new GUIContent("⇄", "Switch condition modes"), Styles.CondSwitchBtn)) ReverseAllConditions();
            if (CursorBtn(new Rect(rightColumnX + modeColumnWidth,                     headerRect.y, splitColumnWidth, headerRect.height), new GUIContent("M", "Merge transitions"),        Styles.IconBtn)) MergeTransitions();
            if (CursorBtn(new Rect(rightColumnX + modeColumnWidth + splitColumnWidth,  headerRect.y, splitColumnWidth, headerRect.height), new GUIContent("S", "Separate transitions"),      Styles.IconBtn)) SeparateTransitions();

            // Padded + colored body — rows and add button only
            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.CondSectionBg);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            var entries = GetDisplayedConditions();
            if (entries.Count == 0)
                EditorGUILayout.LabelField("List is Empty", Styles.EmptyLabel);
            else
                for (int i = 0; i < entries.Count; i++)
                    DrawConditionRow(i, entries[i]);

            if (_showSharedConditions || _selectedTransitions.Length <= 1)
            {
                float rowH = EditorGUIUtility.singleLineHeight;
                var addRow = EditorGUILayout.GetControlRect(false, rowH);
                var addRect = new Rect(addRow.xMax - 24f, addRow.y, 24f, rowH);
                if (CursorBtn(addRect, "+", Styles.CondActionBtn))
                    AddConditionToAll();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        readonly struct CondEntry
        {
            internal readonly AnimatorStateTransition owner;
            internal readonly AnimatorCondition condition;
            internal readonly int index;
            internal CondEntry(AnimatorStateTransition owner, AnimatorCondition condition, int index)
            { this.owner = owner; this.condition = condition; this.index = index; }
        }

        List<CondEntry> GetDisplayedConditions()
        {
            var result = new List<CondEntry>();
            _selectedTransitions = _selectedTransitions.Where(transition => transition != null).ToArray();
            if (_selectedTransitions.Length == 0) return result;

            if (!_showSharedConditions)
            {
                foreach (var transition in _selectedTransitions)
                {
                    var conditions = transition.conditions;
                    for (int i = 0; i < conditions.Length; i++)
                        result.Add(new CondEntry(transition, conditions[i], i));
                }
                return result;
            }

            var first = _selectedTransitions[0];
            var firstConditions = first.conditions;
            for (int i = 0; i < firstConditions.Length; i++)
            {
                var condition = firstConditions[i];
                if (_selectedTransitions.All(transition =>
                        transition.conditions.Any(x => x.parameter == condition.parameter && x.mode == condition.mode)))
                    result.Add(new CondEntry(first, condition, i));
            }
            return result;
        }

        void DrawConditionRow(int rowIdx, CondEntry entry)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var condition = entry.condition;

            float parameterColumnWidth  = row.width * 0.5f;
            float modeColumnWidth       = row.width * 0.25f;
            float removeButtonWidth     = 24f;
            float valueColumnWidth      = row.width - parameterColumnWidth - modeColumnWidth - removeButtonWidth;

            float currentX = row.x;
            var parameterRect    = new Rect(currentX, row.y, parameterColumnWidth, row.height); currentX += parameterColumnWidth;
            var conditionModeRect = new Rect(currentX, row.y, modeColumnWidth,     row.height); currentX += modeColumnWidth;
            var valueRect        = new Rect(currentX, row.y, valueColumnWidth,     row.height); currentX += valueColumnWidth;
            var removeRect       = new Rect(currentX, row.y, removeButtonWidth,    row.height);

            if (_controller == null || _controller.parameters.Length == 0)
            {
                GUI.Label(parameterRect, condition.parameter, EditorStyles.miniLabel);
                CursorBtn(removeRect, "−", Styles.CondActionBtn);
                return;
            }

            var capturedEntry = entry;
            if (EditorGUI.DropdownButton(parameterRect, new GUIContent(condition.parameter), FocusType.Passive))
                ShowParameterDropdown(parameterRect, newParam =>
                    ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition
                    {
                        parameter = newParam,
                        mode      = DefaultModeForType(GetParamType(newParam)),
                        threshold = 0f
                    }));

            var parameterType = GetParamType(condition.parameter);

            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                bool isTrue = condition.mode != AnimatorConditionMode.IfNot;
                float toggleWidth = EditorGUIUtility.singleLineHeight;
                var toggleRect = new Rect(conditionModeRect.x + (conditionModeRect.width - toggleWidth) * 0.5f, conditionModeRect.y, toggleWidth, conditionModeRect.height);
                EditorGUI.BeginChangeCheck();
                isTrue = EditorGUI.Toggle(toggleRect, isTrue);
                if (EditorGUI.EndChangeCheck())
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = isTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, threshold = 0f });
                GUI.Label(valueRect, isTrue ? "True" : "False", isTrue ? Styles.CondTrue : Styles.CondFalse);
            }
            else if (parameterType != AnimatorControllerParameterType.Trigger)
            {
                var modeLabel = ModeLabel(condition.mode);
                if (GUI.Button(conditionModeRect, modeLabel, EditorStyles.popup))
                {
                    var menu = new GenericMenu();
                    var modes = ModesForType(parameterType);
                    foreach (var conditionMode in modes)
                    {
                        menu.AddItem(new GUIContent(ModeLabel(conditionMode)), conditionMode == condition.mode, () =>
                        {
                            ReplaceConditionOnTargets(entry, new AnimatorCondition
                            {
                                parameter = condition.parameter,
                                mode = conditionMode,
                                threshold = condition.threshold
                            });
                        });
                    }

                    menu.DropDown(conditionModeRect);
                }

                EditorGUI.BeginChangeCheck();
                float newThreshold = parameterType == AnimatorControllerParameterType.Int
                    ? EditorGUI.IntField(valueRect, (int)condition.threshold)
                    : EditorGUI.FloatField(valueRect, condition.threshold);
                if (EditorGUI.EndChangeCheck())
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = condition.mode, threshold = newThreshold });
            }

            if (CursorBtn(removeRect, "−", Styles.CondActionBtn))
                RemoveConditionFromTargets(entry);
        }

        AnimatorControllerParameterType GetParamType(string paramName)
        {
            var parameter = _controller?.parameters.FirstOrDefault(x => x.name == paramName);
            return parameter?.type ?? AnimatorControllerParameterType.Float;
        }

        static AnimatorConditionMode[] ModesForType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Bool    => new[] { AnimatorConditionMode.If, AnimatorConditionMode.IfNot },
            AnimatorControllerParameterType.Trigger => new[] { AnimatorConditionMode.If },
            AnimatorControllerParameterType.Int     => new[] { AnimatorConditionMode.Equals, AnimatorConditionMode.NotEqual, AnimatorConditionMode.Greater, AnimatorConditionMode.Less },
            _                                       => new[] { AnimatorConditionMode.Greater, AnimatorConditionMode.Less }
        };

        static AnimatorConditionMode DefaultModeForType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Bool or AnimatorControllerParameterType.Trigger => AnimatorConditionMode.If,
            AnimatorControllerParameterType.Int => AnimatorConditionMode.Equals,
            _ => AnimatorConditionMode.Greater
        };

        static string ModeLabel(AnimatorConditionMode mode) => mode switch
        {
            AnimatorConditionMode.If       => "True",
            AnimatorConditionMode.IfNot    => "False",
            AnimatorConditionMode.Equals   => "Equals",
            AnimatorConditionMode.NotEqual => "Not Equal",
            AnimatorConditionMode.Greater  => "Greater",
            AnimatorConditionMode.Less     => "Less",
            _                             => mode.ToString()
        };

        void ReplaceConditionOnTargets(CondEntry entry, AnimatorCondition replacement)
        {
            if (!_showSharedConditions)
            {
                if (entry.index < entry.owner.conditions.Length)
                    RebuildConditions(entry.owner, entry.index, replacement);
            }
            else
            {
                foreach (var transition in _selectedTransitions)
                {
                    int idx = FindConditionIndex(transition, entry.condition);
                    if (idx < 0) continue;
                    RebuildConditions(transition, idx, replacement);
                }
            }
        }

        void RemoveConditionFromTargets(CondEntry entry)
        {
            IEnumerable<AnimatorStateTransition> targets = _showSharedConditions
                ? (IEnumerable<AnimatorStateTransition>)_selectedTransitions
                : new[] { entry.owner };

            foreach (var transition in targets)
            {
                int idx = _showSharedConditions ? FindConditionIndex(transition, entry.condition) : entry.index;
                if (idx < 0 || idx >= transition.conditions.Length) continue;
                Undo.RecordObject(transition, "Remove Condition");
                var allConditions = transition.conditions.ToArray();
                foreach (var condition in allConditions) transition.RemoveCondition(condition);
                for (int i = 0; i < allConditions.Length; i++)
                    if (i != idx) transition.AddCondition(allConditions[i].mode, allConditions[i].threshold, allConditions[i].parameter);
                EditorUtility.SetDirty(transition);
            }
        }

        void AddConditionToAll()
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            var defaultParam = _controller.parameters[0];
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Add Condition");
                transition.AddCondition(DefaultModeForType(defaultParam.type), 0f, defaultParam.name);
                EditorUtility.SetDirty(transition);
            }
        }

        void ReverseAllConditions()
        {
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Reverse Conditions");
                var allConditions = transition.conditions.ToArray();
                foreach (var condition in allConditions) transition.RemoveCondition(condition);
                foreach (var condition in allConditions) transition.AddCondition(ReverseMode(condition.mode), condition.threshold, condition.parameter);
                EditorUtility.SetDirty(transition);
            }
        }

        static AnimatorConditionMode ReverseMode(AnimatorConditionMode mode) => mode switch
        {
            AnimatorConditionMode.If       => AnimatorConditionMode.IfNot,
            AnimatorConditionMode.IfNot    => AnimatorConditionMode.If,
            AnimatorConditionMode.Equals   => AnimatorConditionMode.NotEqual,
            AnimatorConditionMode.NotEqual => AnimatorConditionMode.Equals,
            AnimatorConditionMode.Greater  => AnimatorConditionMode.Less,
            AnimatorConditionMode.Less     => AnimatorConditionMode.Greater,
            _                             => mode
        };

        static int FindConditionIndex(AnimatorStateTransition transition, AnimatorCondition target)
        {
            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
                if (conditions[i].parameter == target.parameter && conditions[i].mode == target.mode)
                    return i;
            return -1;
        }

        static void RebuildConditions(AnimatorStateTransition transition, int replaceIdx, AnimatorCondition replacement)
        {
            Undo.RecordObject(transition, "Edit Condition");
            var allConditions = transition.conditions.ToArray();
            foreach (var condition in allConditions) transition.RemoveCondition(condition);
            for (int i = 0; i < allConditions.Length; i++)
            {
                var condition = i == replaceIdx ? replacement : allConditions[i];
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }
            EditorUtility.SetDirty(transition);
        }

        // ── Merge / Separate ──────────────────────────────────────────────────

        void MergeTransitions()
        {
            if (_selectedTransitions.Length < 2 || _controller == null) return;
            var transitions = _selectedTransitions.ToArray();
            var controller = _controller;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            Undo.RegisterCompleteObjectUndo(controller, "Merge Transitions");

            var groups = new Dictionary<(string src, string dst), List<AnimatorStateTransition>>();
            foreach (var transition in transitions)
            {
                var key = (GetSrcKey(controller, transition), GetDstKey(transition));
                if (!groups.ContainsKey(key)) groups[key] = new List<AnimatorStateTransition>();
                groups[key].Add(transition);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                var primary = group[0];
                var ownerStateMachine = FindOwnerSM(controller, primary);
                if (ownerStateMachine == null) continue;

                Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Merge Transitions");
                Undo.RecordObject(primary, "Merge Transitions");
                foreach (var transition in group.Skip(1))
                {
                    foreach (var childState in ownerStateMachine.states)
                        if (childState.state.transitions.Contains(transition))
                        {
                            Undo.RegisterCompleteObjectUndo(childState.state, "Merge Transitions");
                            break;
                        }
                }

                foreach (var transition in group.Skip(1))
                {
                    foreach (var condition in transition.conditions)
                        primary.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    DeleteTransition(ownerStateMachine, transition);
                }
                EditorUtility.SetDirty(primary);
            }

            EditorUtility.SetDirty(controller);
        }

        void SeparateTransitions()
        {
            if (_selectedTransitions.Length == 0 || _controller == null) return;
            var transitions = _selectedTransitions.ToArray();
            var controller = _controller;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            Undo.RegisterCompleteObjectUndo(controller, "Separate Transitions");

            foreach (var transition in transitions)
            {
                var conditions = transition.conditions.ToArray();
                if (conditions.Length <= 1) continue;

                var ownerStateMachine = FindOwnerSM(controller, transition);
                if (ownerStateMachine == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerStateMachine, "Separate Transitions");

                bool isAnyState = ownerStateMachine.anyStateTransitions.Contains(transition);
                AnimatorState sourceState = isAnyState ? null
                    : ownerStateMachine.states.FirstOrDefault(x => x.state.transitions.Contains(transition)).state;

                for (int i = 1; i < conditions.Length; i++)
                {
                    var newTransition = CreateMatchingTransition(ownerStateMachine, sourceState, isAnyState, transition);
                    if (newTransition == null) continue;
                    Undo.RegisterCreatedObjectUndo(newTransition, "Separate Transitions");
                    CopyTransitionSettings(transition, newTransition);
                    foreach (var condition in newTransition.conditions.ToArray()) newTransition.RemoveCondition(condition);
                    newTransition.AddCondition(conditions[i].mode, conditions[i].threshold, conditions[i].parameter);
                    EditorUtility.SetDirty(newTransition);
                }

                Undo.RecordObject(transition, "Separate Transitions");
                foreach (var condition in conditions.Skip(1)) transition.RemoveCondition(condition);
                EditorUtility.SetDirty(transition);
            }

            EditorUtility.SetDirty(controller);
        }

        static AnimatorStateTransition CreateMatchingTransition(AnimatorStateMachine sm, AnimatorState srcState, bool isAnyState, AnimatorStateTransition original)
        {
            if (isAnyState)
            {
                if (original.destinationState != null) return sm.AddAnyStateTransition(original.destinationState);
                if (original.destinationStateMachine != null) return sm.AddAnyStateTransition(original.destinationStateMachine);
                return null;
            }
            if (srcState == null) return null;
            if (original.isExit) return srcState.AddExitTransition();
            if (original.destinationState != null) return srcState.AddTransition(original.destinationState);
            if (original.destinationStateMachine != null) return srcState.AddTransition(original.destinationStateMachine);
            return null;
        }

        static void CopyTransitionSettings(AnimatorStateTransition sourceTransition, AnimatorStateTransition destinationTransition)
        {
            destinationTransition.hasExitTime = sourceTransition.hasExitTime;
            destinationTransition.exitTime = sourceTransition.exitTime;
            destinationTransition.hasFixedDuration = sourceTransition.hasFixedDuration;
            destinationTransition.duration = sourceTransition.duration;
            destinationTransition.offset = sourceTransition.offset;
            destinationTransition.interruptionSource = sourceTransition.interruptionSource;
            destinationTransition.orderedInterruption = sourceTransition.orderedInterruption;
            destinationTransition.mute = sourceTransition.mute;
            destinationTransition.solo = sourceTransition.solo;
            destinationTransition.canTransitionToSelf = sourceTransition.canTransitionToSelf;
        }

        static AnimatorStateMachine FindOwnerSM(AnimatorController controller, AnimatorStateTransition transition)
        {
            foreach (var layer in controller.layers)
            {
                var found = FindOwnerSMRecursive(layer.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }

        static AnimatorStateMachine FindOwnerSMRecursive(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            if (sm.anyStateTransitions.Contains(transition)) return sm;
            foreach (var childState in sm.states)
                if (childState.state.transitions.Contains(transition)) return sm;
            foreach (var childSM in sm.stateMachines)
            {
                var found = FindOwnerSMRecursive(childSM.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }

        static void DeleteTransition(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            if (sm.anyStateTransitions.Contains(transition))
            {
                sm.RemoveAnyStateTransition(transition);
                return;
            }
            foreach (var childState in sm.states)
            {
                if (childState.state.transitions.Contains(transition))
                {
                    childState.state.RemoveTransition(transition);
                    return;
                }
            }
        }

        static string GetSrcKey(AnimatorController controller, AnimatorStateTransition transition)
        {
            if (controller == null) return "?";
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine.anyStateTransitions.Contains(transition)) return "anystate";
                foreach (var childState in layer.stateMachine.states)
                    if (childState.state.transitions.Contains(transition)) return childState.state.GetInstanceID().ToString();
            }
            return "?";
        }

        string GetDstKey(AnimatorStateTransition transition)
        {
            if (transition.isExit) return "exit";
            if (transition.destinationState != null) return transition.destinationState.GetInstanceID().ToString();
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine.GetInstanceID().ToString();
            return "?";
        }

        // ── Utility ───────────────────────────────────────────────────────────

        void SetOnAll(Action<AnimatorStateTransition> mutate)
        {
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Edit Transition");
                mutate(transition);
                EditorUtility.SetDirty(transition);
            }
        }
    }
}
#endif
