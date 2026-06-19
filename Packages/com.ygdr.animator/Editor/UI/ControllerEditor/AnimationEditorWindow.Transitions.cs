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
        /* ── Transitions tab ─────────────────────────────────────────────── */

        void DrawTransitionsTab()
        {
            int count = _selectedTransitions.Length;

            var panelRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && panelRect.height > 0)
                EditorGUI.DrawRect(panelRect, Styles.PrimaryColor);

            if (count == 0)
                EditorGUILayout.LabelField(L10n.Get("transitions.empty"), Styles.EmptyLabel);
            else
                DrawTransitionTags();

            EditorGUILayout.Space(8);
            DrawProperties();
            EditorGUILayout.Space(4);
            DrawConditionsSection();

            EditorGUILayout.EndVertical();
        }

        /* ── Transition Tags ─────────────────────────────────────────────── */

        float _tagAreaCachedWidth;

        void DrawTransitionTags()
        {
            const float tagH = 20f;
            const float gap = 4f;
            float toggleW = Styles.k_pillW;
            float tagDrawW = (_tagAreaCachedWidth > 0f ? _tagAreaCachedWidth : EditorGUIUtility.currentViewWidth - 24f) - toggleW;
            float totalH = CalcTransitionTagsHeight(tagDrawW, tagH, gap);
            float maxVisibleH = 4f * (tagH + gap);
            float displayH = _tagScrollEnabled ? Mathf.Min(totalH, maxVisibleH) : totalH;

            var area = EditorGUILayout.GetControlRect(false, displayH + gap);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, Styles.SecondaryColor);
                if (!Mathf.Approximately(_tagAreaCachedWidth, area.width))
                {
                    _tagAreaCachedWidth = area.width;
                    Repaint();
                }
            }

            var toggleRect = new Rect(area.xMax - toggleW, area.y, toggleW, area.height);
            _tagScrollEnabled = GUI.Toggle(toggleRect, _tagScrollEnabled, "", Styles.ScrollToggleBtn);
            EditorGUIUtility.AddCursorRect(toggleRect, MouseCursor.Link);

            var tagViewRect = new Rect(area.x, area.y, area.width - toggleW, area.height);

            if (_tagScrollEnabled && totalH > maxVisibleH)
            {
                var contentRect = new Rect(0, 0, tagViewRect.width - 13f, totalH + gap);
                _tagScrollPos = GUI.BeginScrollView(tagViewRect, _tagScrollPos, contentRect, false, true);
                DrawTagsInto(contentRect, tagH, gap);
                GUI.EndScrollView();
            }
            else
            {
                DrawTagsInto(tagViewRect, tagH, gap);
            }
        }

        void DrawTagsInto(Rect area, float tagH, float gap)
        {
            float currentX = 4f, currentY = 2f;
            for (int i = 0; i < _selectedTransitions.Length; i++)
            {
                var transition = _selectedTransitions[i];
                string label = GetTransitionLabel(transition);
                float tagW = Mathf.Clamp(Styles.TransitionTagLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, area.width - 4f);

                if (currentX + tagW > area.width && currentX > 4f) { currentX = 4f; currentY += tagH + gap; }

                var tag = new Rect(area.x + currentX, area.y + currentY, tagW, tagH);
                EditorGUI.DrawRect(tag, Styles.PrimaryColor);

                if (CursorBtn(new Rect(tag.x + 2f, tag.y + 2f, 16f, 16f), "✕", Styles.TransitionTagBtn))
                {
                    Selection.objects = _selectedTransitions.Where(x => x != transition).Cast<UnityEngine.Object>().ToArray();
                    return;
                }

                GUI.Label(new Rect(tag.x + 20f, tag.y, tagW - 22f, tagH), label, Styles.TransitionTagLabel);
                currentX += tagW + gap;
            }
        }

        /* Simulates the tag layout to compute the total height needed for GetControlRect before drawing. */
        float CalcTransitionTagsHeight(float estimatedW, float tagH, float gap)
        {
            float currentX = 4f;
            int rows = 1;
            foreach (var transition in _selectedTransitions)
            {
                string label = GetTransitionLabel(transition);
                float tagW = Mathf.Clamp(Styles.TransitionTagLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, estimatedW);
                if (currentX + tagW > estimatedW && currentX > 4f) { currentX = 4f; rows++; }
                currentX += tagW + gap;
            }
            return rows * (tagH + gap);
        }

        /* Returns a "Source → Destination" display string for a transition, resolving anyState, exit, and SM destinations. */
        string GetTransitionLabel(AnimatorStateTransition transition)
        {
            string sourceName = FindSrcName(_controller, transition) ?? "Any State";
            string destinationName = transition.isExit ? "Exit"
                : transition.destinationState != null ? transition.destinationState.name
                : transition.destinationStateMachine != null ? transition.destinationStateMachine.name
                : "?";
            return $"{sourceName} → {destinationName}";
        }

        /* Searches all layers in the controller for the state that owns the transition, returning its name or null. */
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

        /* Recursively searches sm and its sub-SMs for the state or anyState that owns the transition, returning its name. */
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

        /* ── Property rows ───────────────────────────────────────────────── */

        void DrawProperties()
        {
            int count = _selectedTransitions.Length;
            bool multi = count > 1;
            bool empty = count == 0;
            var first = empty ? null : _selectedTransitions[0];

            using var disabled = new EditorGUI.DisabledScope(empty);

            /* Has Exit Time | Exit Time */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.has_exit_time"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.hasExitTime != first.hasExitTime));
                EditorGUI.BeginChangeCheck();
                bool newHasExit = EditorGUILayout.Toggle(empty ? false : first.hasExitTime, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.hasExitTime = newHasExit);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(L10n.Get("transitions.exit_time"), GUILayout.Width(120));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.exitTime, first.exitTime)));
                EditorGUI.BeginChangeCheck();
                float newExitTime = EditorGUILayout.FloatField(empty ? 0f : first.exitTime);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.exitTime = newExitTime);
                EditorGUI.showMixedValue = false;
            }

            /* Has Fixed Duration | Transition Duration */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.has_fixed_duration"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.hasFixedDuration != first.hasFixedDuration));
                EditorGUI.BeginChangeCheck();
                bool newFixed = EditorGUILayout.Toggle(empty ? false : first.hasFixedDuration, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.hasFixedDuration = newFixed);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(L10n.Get("transitions.duration"), GUILayout.Width(120));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.duration, first.duration)));
                EditorGUI.BeginChangeCheck();
                float newDuration = EditorGUILayout.FloatField(empty ? 0f : first.duration);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.duration = newDuration);
                EditorGUI.showMixedValue = false;
            }

            /* Transition Offset */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.offset"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => !Mathf.Approximately(x.offset, first.offset)));
                EditorGUI.BeginChangeCheck();
                float newOffset = EditorGUILayout.FloatField(empty ? 0f : first.offset);
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.offset = newOffset);
                EditorGUI.showMixedValue = false;
            }

            /* Interruption Source */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.interruption_source"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.interruptionSource != first.interruptionSource));
                EditorGUI.BeginChangeCheck();
                var newInterruptionSource = (TransitionInterruptionSource)EditorGUILayout.Popup(
                    (int)(empty ? default : first.interruptionSource),
                    new[] { L10n.Get("transitions.interruption.none"), L10n.Get("transitions.interruption.source"), L10n.Get("transitions.interruption.destination"), L10n.Get("transitions.interruption.source_then_destination"), L10n.Get("transitions.interruption.destination_then_source") });
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.interruptionSource = newInterruptionSource);
                EditorGUI.showMixedValue = false;
            }

            /* Ordered Interruption | Mute */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.ordered_interruption"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.orderedInterruption != first.orderedInterruption));
                EditorGUI.BeginChangeCheck();
                bool newOrdered = EditorGUILayout.Toggle(empty ? false : first.orderedInterruption, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.orderedInterruption = newOrdered);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(L10n.Get("transitions.mute"), GUILayout.Width(80));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.mute != first.mute));
                EditorGUI.BeginChangeCheck();
                bool newMute = EditorGUILayout.Toggle(empty ? false : first.mute, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.mute = newMute);
                EditorGUI.showMixedValue = false;
            }

            /* Can Transition To Self | Solo */
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("transitions.can_transition_to_self"), GUILayout.Width(160));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.canTransitionToSelf != first.canTransitionToSelf));
                EditorGUI.BeginChangeCheck();
                bool newSelf = EditorGUILayout.Toggle(empty ? false : first.canTransitionToSelf, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.canTransitionToSelf = newSelf);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(L10n.Get("transitions.solo"), GUILayout.Width(80));
                EditorGUI.showMixedValue = empty || (multi && _selectedTransitions.Any(x => x.solo != first.solo));
                EditorGUI.BeginChangeCheck();
                bool newSolo = EditorGUILayout.Toggle(empty ? false : first.solo, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck() && !empty) SetOnAll(transition => transition.solo = newSolo);
                EditorGUI.showMixedValue = false;
            }
        }

        /* ── Conditions cache ────────────────────────────────────────────── */

        bool _conditionCacheDirty = true;
        bool _cachedForSharedMode;
        AnimatorStateTransition[] _cachedForTransitions;
        List<CondEntry> _cachedEntries;
        HashSet<(AnimatorStateTransition, string)> _cachedDuplicateParameters;
        string[] _cachedParameterNames;
        HashSet<string> _cachedParamNameSet;
        Dictionary<string, string> _danglingParamResolution;

        /* Returns true if the cached transition array matches the current selection — used to skip condition rebuilds. */
        bool ConditionSelectionUnchanged()
        {
            if (_cachedForTransitions == null || _cachedForTransitions.Length != _selectedTransitions.Length) return false;
            for (int i = 0; i < _selectedTransitions.Length; i++)
                if (_cachedForTransitions[i] != _selectedTransitions[i]) return false;
            return true;
        }

        void InvalidateConditionCache() { _conditionCacheDirty = true; _paramCachedController = null; _cachedParamNameSet = null; }

        bool ParameterListChanged()
        {
            if (_controller == null) return _cachedParameterNames != null && _cachedParameterNames.Length > 0;
            var parameters = _controller.parameters;
            if (_cachedParameterNames == null || _cachedParameterNames.Length != parameters.Length) return true;
            for (int i = 0; i < parameters.Length; i++)
                if (_cachedParameterNames[i] != parameters[i].name) return true;
            return false;
        }

        /* Builds old→new name map for condition params Unity didn't revert on undo.
           Called before _cachedParameterNames update so old names are still available. */
        Dictionary<string, string> BuildDanglingResolution()
        {
            var map = new Dictionary<string, string>();
            if (_controller == null || _cachedParameterNames == null) return map;
            var currentParams = _controller.parameters;
            if (currentParams.Length != _cachedParameterNames.Length) return map;
            for (int i = 0; i < _cachedParameterNames.Length; i++)
            {
                string oldName = _cachedParameterNames[i];
                string newName = currentParams[i].name;
                if (oldName != newName)
                    map[oldName] = newName;
            }
            return map;
        }

        /* ── Conditions section ──────────────────────────────────────────── */

        void DrawConditionsSection()
        {
            /* Header — not part of the padded/colored section */
            var headerRect = EditorGUILayout.GetControlRect(false, 22f);
            float parameterColumnWidth = headerRect.width * 0.5f;
            float modeColumnWidth  = headerRect.width * 0.25f;
            float splitColumnWidth = (headerRect.width - parameterColumnWidth - modeColumnWidth) * 0.5f;

            if (Event.current.type == EventType.Repaint)
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);

            string modeLabel = _showSharedConditions ? L10n.Get("transitions.shared_conditions") : L10n.Get("transitions.all_conditions");
            if (CursorBtn(new Rect(headerRect.x, headerRect.y, parameterColumnWidth, headerRect.height), new GUIContent("  " + modeLabel, L10n.Get("transitions.tooltip.toggle_conditions")), Styles.CondModeBtn))
                _showSharedConditions = !_showSharedConditions;

            float rightColumnX = headerRect.x + parameterColumnWidth;
            if (CursorBtn(new Rect(rightColumnX,                                       headerRect.y, modeColumnWidth,  headerRect.height), new GUIContent("⇄", L10n.Get("transitions.tooltip.switch_modes")),  Styles.CondSwitchBtn)) ReverseAllConditions();
            if (CursorBtn(new Rect(rightColumnX + modeColumnWidth,                     headerRect.y, splitColumnWidth, headerRect.height), new GUIContent("M", L10n.Get("transitions.tooltip.merge")),           Styles.IconBtn)) MergeTransitions();
            if (CursorBtn(new Rect(rightColumnX + modeColumnWidth + splitColumnWidth,  headerRect.y, splitColumnWidth, headerRect.height), new GUIContent("S", L10n.Get("transitions.tooltip.separate")),        Styles.IconBtn)) SeparateTransitions();

            /* Padded + colored body — rows and add button only */
            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            var bodyRect = EditorGUILayout.BeginVertical(Styles.CondBody, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            if (_conditionCacheDirty || _cachedEntries == null || _cachedForSharedMode != _showSharedConditions || !ConditionSelectionUnchanged() || ParameterListChanged())
            {
                _cachedEntries = GetDisplayedConditions();
                _cachedDuplicateParameters = new HashSet<(AnimatorStateTransition, string)>(
                    _cachedEntries.GroupBy(entry => (entry.owner, entry.condition.parameter))
                                  .Where(group => group.Count() > 1)
                                  .Select(group => group.Key));
                _cachedForTransitions = _selectedTransitions.ToArray();
                _cachedForSharedMode = _showSharedConditions;
                _danglingParamResolution = BuildDanglingResolution();
                _cachedParameterNames = _controller?.parameters.Select(parameter => parameter.name).ToArray() ?? Array.Empty<string>();
                _cachedParamNameSet   = new HashSet<string>(_cachedParameterNames);
                _conditionCacheDirty = false;
            }
            var entries = _cachedEntries;
            var duplicateParameters = _cachedDuplicateParameters;

            if (entries.Count == 0)
                EditorGUILayout.LabelField(L10n.Get("transitions.conditions_empty"), Styles.EmptyLabel);
            else
            {
                int groupIndex = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (!_showSharedConditions && i > 0 && entries[i].owner != entries[i - 1].owner)
                    {
                        GUILayout.Space(6f);
                        groupIndex++;
                    }
                    if (entries[i].owner == null) continue;
                    bool altRow = _showSharedConditions ? i % 2 == 1 : groupIndex % 2 == 1;
                    DrawConditionRow(i, entries[i], duplicateParameters, altRow);
                }
            }

            EditorGUILayout.EndVertical();

            if (_showSharedConditions || _selectedTransitions.Length <= 1)
            {
                GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
                float rowH = EditorGUIUtility.singleLineHeight;
                var addRow = EditorGUILayout.GetControlRect(false, rowH);
                var addRect = new Rect(addRow.xMax - 40f, addRow.y, 24f, rowH);
                if (CursorBtn(addRect, "+", Styles.CondBtn))
                    AddConditionToAll();
            }
        }

        readonly struct CondEntry
        {
            internal readonly AnimatorStateTransition owner;
            internal readonly AnimatorCondition condition;
            internal readonly int index;
            internal readonly Dictionary<AnimatorStateTransition, int> sharedIndices;
            internal readonly bool mixedThreshold;

            internal CondEntry(AnimatorStateTransition owner, AnimatorCondition condition, int index)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = null; this.mixedThreshold = false; }

            internal CondEntry(AnimatorStateTransition owner, AnimatorCondition condition, int index, Dictionary<AnimatorStateTransition, int> sharedIndices, bool mixedThreshold)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = sharedIndices; this.mixedThreshold = mixedThreshold; }

            internal int IndexFor(AnimatorStateTransition transition)
                => sharedIndices != null && sharedIndices.TryGetValue(transition, out int idx) ? idx : index;
        }

        /* Builds the flat list of CondEntry to display: all conditions per transition in individual mode, or only conditions shared across all selected transitions in shared mode. */
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
            var claimed = new Dictionary<AnimatorStateTransition, HashSet<int>>();
            foreach (var transition in _selectedTransitions) claimed[transition] = new HashSet<int>();

            for (int i = 0; i < firstConditions.Length; i++)
            {
                var condition = firstConditions[i];
                var indexMap = new Dictionary<AnimatorStateTransition, int> { [first] = i };
                bool allMatch = true;

                foreach (var transition in _selectedTransitions)
                {
                    if (transition == first) continue;
                    int matchIdx = FindConditionIndexExcluding(transition, condition.parameter, condition.mode, claimed[transition]);
                    if (matchIdx < 0) { allMatch = false; break; }
                    indexMap[transition] = matchIdx;
                }

                if (!allMatch) continue;
                foreach (var pair in indexMap) claimed[pair.Key].Add(pair.Value);
                bool mixedThreshold = indexMap.Any(pair =>
                    pair.Key != first && pair.Value < pair.Key.conditions.Length &&
                    pair.Key.conditions[pair.Value].threshold != condition.threshold);
                result.Add(new CondEntry(first, condition, i, indexMap, mixedThreshold));
            }
            return result;
        }

        /* Draws one condition row: parameter dropdown, mode/value controls, and a remove button.
           Layout is split into parameter (50%), mode (25%), value, and remove columns. */
        void DrawConditionRow(int rowIdx, CondEntry entry, HashSet<(AnimatorStateTransition, string)> duplicateParameters, bool altRow = false)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var ownerConditions = entry.owner.conditions;
            var condition = entry.index < ownerConditions.Length ? ownerConditions[entry.index] : entry.condition;
            if (_danglingParamResolution != null && _danglingParamResolution.TryGetValue(condition.parameter, out string resolvedParam))
                condition = new AnimatorCondition { parameter = resolvedParam, mode = condition.mode, threshold = condition.threshold };

            const float stripeWidth   = 7f;
            const float leftOverhang  = 6f;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(row.x - leftOverhang, row.y, stripeWidth + leftOverhang, row.height),
                    altRow ? Styles.RowAltColor : Styles.SecondaryColor);

            float contentWidth          = row.width - stripeWidth;
            float parameterColumnWidth  = contentWidth * 0.5f;
            float modeColumnWidth       = contentWidth * 0.25f;
            float removeButtonWidth     = 24f;
            float valueColumnWidth      = contentWidth - parameterColumnWidth - modeColumnWidth - removeButtonWidth;

            float currentX = row.x + stripeWidth;
            var parameterRect    = new Rect(currentX, row.y, parameterColumnWidth, row.height); currentX += parameterColumnWidth;
            var conditionModeRect = new Rect(currentX, row.y, modeColumnWidth,     row.height); currentX += modeColumnWidth;
            var valueRect        = new Rect(currentX, row.y, valueColumnWidth,     row.height); currentX += valueColumnWidth;
            var removeRect       = new Rect(currentX, row.y, removeButtonWidth,    row.height);

            if (_controller == null || _controller.parameters.Length == 0)
            {
                GUI.Label(parameterRect, condition.parameter, EditorStyles.miniLabel);
                CursorBtn(removeRect, "−", Styles.CondBtn);
                return;
            }

            bool parameterExists = _cachedParamNameSet?.Contains(condition.parameter) ?? false;
            if (!parameterExists)
            {
                var previousColor = GUI.color;
                GUI.color = Color.red;
                GUI.Label(parameterRect, condition.parameter, EditorStyles.miniLabel);
                GUI.color = previousColor;
                CursorBtn(removeRect, "−", Styles.CondBtn);
                return;
            }

            var capturedEntry = entry;
            var capturedCondition = condition;
            if (EditorGUI.DropdownButton(parameterRect, new GUIContent(condition.parameter), FocusType.Passive))
                ShowParameterDropdown(parameterRect, condition.parameter, newParam =>
                {
                    var newType = GetParamType(newParam);
                    var sourceType = GetParamType(capturedCondition.parameter);
                    AnimatorConditionMode seededMode;
                    if (sourceType == newType)
                        seededMode = capturedCondition.mode;
                    else if (AnimatorParameterOps.TryConvertCondition(capturedCondition, sourceType, newType, out var converted))
                        seededMode = converted.mode;
                    else
                        seededMode = DefaultModeForType(newType);
                    ReplaceConditionOnTargets(capturedEntry, new AnimatorCondition
                    {
                        parameter = newParam,
                        mode      = seededMode,
                        threshold = 0f
                    }, preserveThreshold: true);
                });

            if (duplicateParameters.Contains((entry.owner, condition.parameter)))
            {
                var duplicateIconContent = new GUIContent(EditorGUIUtility.IconContent("d_console.erroricon").image, L10n.Get("transitions.duplicate_param_tooltip"));
                var duplicateIconRect = new Rect(parameterRect.xMax - 40, parameterRect.y, 16, parameterRect.height);
                GUI.Label(duplicateIconRect, duplicateIconContent);
            }

            var parameterType = GetParamType(condition.parameter);
            if (Event.current.type == EventType.Repaint && AnimatorDefaultSettings.Load().showParamTypeIcons)
                GUI.Label(parameterRect, parameterType.ToString(), Styles.MiniLabelRight);

            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                bool isTrue = condition.mode != AnimatorConditionMode.IfNot;
                var boolButtonRect = new Rect(conditionModeRect.x, conditionModeRect.y, conditionModeRect.width + valueColumnWidth, conditionModeRect.height);
                if (CursorBtn(boolButtonRect, isTrue ? L10n.Get("transitions.bool_true") : L10n.Get("transitions.bool_false"), isTrue ? Styles.BoolBtnTrue : Styles.BoolBtnFalse))
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = isTrue ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, threshold = 0f });
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
                                threshold = 0f
                            }, preserveThreshold: true);
                        });
                    }

                    menu.DropDown(conditionModeRect);
                }

                EditorGUI.showMixedValue = entry.mixedThreshold;
                EditorGUI.BeginChangeCheck();
                float newThreshold = parameterType == AnimatorControllerParameterType.Int
                    ? EditorGUI.IntField(valueRect, (int)condition.threshold)
                    : EditorGUI.FloatField(valueRect, condition.threshold);
                if (EditorGUI.EndChangeCheck())
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = condition.mode, threshold = newThreshold });
                EditorGUI.showMixedValue = false;
            }

            if (CursorBtn(removeRect, "−", Styles.CondBtn))
                RemoveConditionFromTargets(entry);

        }

        /* Looks up the parameter type by name from the active controller, defaulting to Float if not found. */
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
            AnimatorConditionMode.If       => L10n.Get("transitions.mode_true"),
            AnimatorConditionMode.IfNot    => L10n.Get("transitions.mode_false"),
            AnimatorConditionMode.Equals   => L10n.Get("transitions.mode_equals"),
            AnimatorConditionMode.NotEqual => L10n.Get("transitions.mode_not_equal"),
            AnimatorConditionMode.Greater  => L10n.Get("transitions.mode_greater"),
            AnimatorConditionMode.Less     => L10n.Get("transitions.mode_less"),
            _                             => mode.ToString()
        };

        /* Replaces the entry's condition with replacement on one transition (individual mode) or all selected transitions (shared mode).
           When preserveThreshold is true, each target keeps its own existing threshold — only parameter and mode are overwritten. */
        void ReplaceConditionOnTargets(CondEntry entry, AnimatorCondition replacement, bool preserveThreshold = false)
        {
            InvalidateConditionCache();
            if (!_showSharedConditions)
            {
                if (entry.index < entry.owner.conditions.Length)
                    RebuildConditions(entry.owner, entry.index, replacement);
            }
            else
            {
                foreach (var transition in _selectedTransitions)
                {
                    int idx = entry.IndexFor(transition);
                    if (idx < 0 || idx >= transition.conditions.Length) continue;
                    var actual = preserveThreshold
                        ? new AnimatorCondition { parameter = replacement.parameter, mode = replacement.mode, threshold = transition.conditions[idx].threshold }
                        : replacement;
                    RebuildConditions(transition, idx, actual);
                }
            }
        }

        /* Removes the entry's condition from one transition (individual mode) or from all selected transitions (shared mode). */
        void RemoveConditionFromTargets(CondEntry entry)
        {
            InvalidateConditionCache();
            IEnumerable<AnimatorStateTransition> targets = _showSharedConditions
                ? (IEnumerable<AnimatorStateTransition>)_selectedTransitions
                : new[] { entry.owner };

            foreach (var transition in targets)
            {
                int idx = _showSharedConditions ? entry.IndexFor(transition) : entry.index;
                if (idx < 0 || idx >= transition.conditions.Length) continue;
                Undo.RecordObject(transition, "Remove Condition");
                var allConditions = transition.conditions.ToArray();
                foreach (var condition in allConditions) transition.RemoveCondition(condition);
                for (int i = 0; i < allConditions.Length; i++)
                    if (i != idx) transition.AddCondition(allConditions[i].mode, allConditions[i].threshold, allConditions[i].parameter);
                EditorUtility.SetDirty(transition);
            }
        }

        /* Adds a new condition using the controller's first parameter (and its default mode) to every selected transition. */
        void AddConditionToAll()
        {
            InvalidateConditionCache();
            if (_controller == null || _controller.parameters.Length == 0) return;
            var defaultParam = _controller.parameters[0];
            if (_selectedTransitions.Length == 1)
            {
                var usedNames = new HashSet<string>(_selectedTransitions[0].conditions.Select(condition => condition.parameter));
                var unusedParam = _controller.parameters.FirstOrDefault(parameter => !usedNames.Contains(parameter.name));
                if (unusedParam != null) defaultParam = unusedParam;
            }
            else if (_showSharedConditions)
            {
                var usedNames = new HashSet<string>(_selectedTransitions.SelectMany(transition => transition.conditions.Select(condition => condition.parameter)));
                var unusedParam = _controller.parameters.FirstOrDefault(parameter => !usedNames.Contains(parameter.name));
                if (unusedParam != null) defaultParam = unusedParam;
            }
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Add Condition");
                transition.AddCondition(DefaultModeForType(defaultParam.type), 0f, defaultParam.name);
                EditorUtility.SetDirty(transition);
            }
        }

        /* Inverts every condition mode on all selected transitions (If↔IfNot, Greater↔Less, Equals↔NotEqual). */
        void ReverseAllConditions()
        {
            InvalidateConditionCache();
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Reverse Conditions");
                var allConditions = transition.conditions.ToArray();
                foreach (var condition in allConditions) transition.RemoveCondition(condition);
                foreach (var condition in allConditions) transition.AddCondition(ReverseMode(condition.mode), condition.threshold, condition.parameter);
                EditorUtility.SetDirty(transition);
            }
        }

        /* Returns the logical inverse of a condition mode (If↔IfNot, Equals↔NotEqual, Greater↔Less). */
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

        /* Returns the first unclaimed index matching paramName+mode, or -1 if not found. */
        static int FindConditionIndexExcluding(AnimatorStateTransition transition, string paramName, AnimatorConditionMode mode, HashSet<int> exclude)
        {
            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
                if (!exclude.Contains(i) && conditions[i].parameter == paramName && conditions[i].mode == mode)
                    return i;
            return -1;
        }

        /* Clears and re-adds all conditions on the transition, substituting replacement at replaceIdx. */
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

        /* ── Merge / Separate ────────────────────────────────────────────── */

        /* Groups selected transitions by src+dst key, then collapses each group into its first transition by appending all other transitions' conditions onto it and deleting the rest. */
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
            InvalidateConditionCache();
        }

        /* Splits each selected transition that has multiple conditions into one transition per condition, copying all non-condition settings to each new transition. */
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

        /* Creates a new transition in sm with the same source/destination topology as original (anyState, exit, state, or SM). */
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

        /* Copies all timing, interruption, and flag settings from sourceTransition to destinationTransition (no conditions). */
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

        /* Returns the SM that directly contains the transition (as anyState or as a state's transition), searching all layers. */
        static AnimatorStateMachine FindOwnerSM(AnimatorController controller, AnimatorStateTransition transition)
        {
            foreach (var layer in controller.layers)
            {
                var found = FindOwnerSMRecursive(layer.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }

        /* Recursively searches sm and its sub-SMs for the one that directly contains the transition. */
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

        /* Removes a transition from sm's anyState list or from the source state that owns it. */
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

        /* Returns a stable string key identifying the transition's source (anystate or state instance ID), used to group transitions for merge. */
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

        /* ── Utility ─────────────────────────────────────────────────────── */

        /* Applies mutate to every selected transition with undo recording, then marks each dirty. */
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
