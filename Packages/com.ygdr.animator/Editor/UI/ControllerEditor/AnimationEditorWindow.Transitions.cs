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
        static GUIContent _mergeIcon;
        static GUIContent MergeIcon => _mergeIcon ??= EditorGUIUtility.IconContent("AnimatorStateTransition Icon");
        static GUIContent _separateIcon;
        static GUIContent SeparateIcon => _separateIcon ??= EditorGUIUtility.IconContent("d_BlendTree Icon");
        static GUIContent _filterIcon;
        static GUIContent FilterIcon => _filterIcon ??= EditorGUIUtility.IconContent("d_filterbylabel@2x");

        /* ── Transitions tab ─────────────────────────────────────────────── */

        void DrawTransitionsTab()
        {
            bool hasState = _selectedTransitions.Length > 0;
            int count = _selectedTransitions.Length + _selectedEntryTransitions.Length;

            var panelRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && panelRect.height > 0)
                EditorGUI.DrawRect(panelRect, Styles.PrimaryColor);

            if (count == 0)
                EditorGUILayout.LabelField(L10n.Get("transitions.empty"), Styles.EmptyLabel);
            else
                DrawTransitionTags();

            EditorGUILayout.Space(8);
            if (hasState) DrawProperties();
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
            foreach (var transition in _selectedTransitions)
            {
                string label = GetTransitionLabel(transition);
                float tagW = Mathf.Clamp(Styles.TransitionTagLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, area.width - 4f);
                if (currentX + tagW > area.width && currentX > 4f) { currentX = 4f; currentY += tagH + gap; }
                var tag = new Rect(area.x + currentX, area.y + currentY, tagW, tagH);
                EditorGUI.DrawRect(tag, Styles.PrimaryColor);
                if (CursorBtn(new Rect(tag.x + 2f, tag.y + 2f, 16f, 16f), "✕", Styles.TransitionTagBtn))
                {
                    Selection.objects = _selectedTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()
                        .Concat(_selectedEntryTransitions.Cast<UnityEngine.Object>()).ToArray();
                    return;
                }
                GUI.Label(new Rect(tag.x + 20f, tag.y, tagW - 22f, tagH), label, Styles.TransitionTagLabel);
                currentX += tagW + gap;
            }
            foreach (var transition in _selectedEntryTransitions)
            {
                string label = GetEntryTransitionLabel(transition);
                float tagW = Mathf.Clamp(Styles.TransitionTagLabel.CalcSize(new GUIContent(label)).x + 36f, 80f, area.width - 4f);
                if (currentX + tagW > area.width && currentX > 4f) { currentX = 4f; currentY += tagH + gap; }
                var tag = new Rect(area.x + currentX, area.y + currentY, tagW, tagH);
                EditorGUI.DrawRect(tag, Styles.PrimaryColor);
                if (CursorBtn(new Rect(tag.x + 2f, tag.y + 2f, 16f, 16f), "✕", Styles.TransitionTagBtn))
                {
                    Selection.objects = _selectedTransitions.Cast<UnityEngine.Object>()
                        .Concat(_selectedEntryTransitions.Where(x => x != transition).Cast<UnityEngine.Object>()).ToArray();
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
            var labels = _selectedTransitions.Select(t => GetTransitionLabel(t))
                .Concat(_selectedEntryTransitions.Select(t => GetEntryTransitionLabel(t)));
            foreach (var label in labels)
            {
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

        /* Returns an "Entry → Destination" display string for an entry transition. */
        static string GetEntryTransitionLabel(AnimatorTransition transition)
        {
            string destinationName = transition.isExit ? "Exit"
                : transition.destinationState != null ? transition.destinationState.name
                : transition.destinationStateMachine != null ? transition.destinationStateMachine.name
                : "?";
            return $"Entry → {destinationName}";
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
                var anyStateSelected = _selectedTransitions.Where(x => IsAnyStateTransition(x)).ToArray();
                using (new EditorGUI.DisabledScope(anyStateSelected.Length == 0))
                {
                    EditorGUI.showMixedValue = anyStateSelected.Length == 0
                        || anyStateSelected.Any(x => x.canTransitionToSelf != anyStateSelected[0].canTransitionToSelf);
                    EditorGUI.BeginChangeCheck();
                    bool newSelf = EditorGUILayout.Toggle(anyStateSelected.Length == 0 ? false : anyStateSelected[0].canTransitionToSelf, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck() && anyStateSelected.Length > 0)
                    {
                        Undo.RecordObjects(anyStateSelected, "Edit Transition");
                        foreach (var transition in anyStateSelected) transition.canTransitionToSelf = newSelf;
                        foreach (var transition in anyStateSelected) EditorUtility.SetDirty(transition);
                    }
                    EditorGUI.showMixedValue = false;
                }
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
        UnityEngine.Object[] _cachedForOwners;
        List<CondEntry> _cachedEntries;
        HashSet<(UnityEngine.Object, string)> _cachedDuplicateParameters;
        string[] _cachedParameterNames;
        HashSet<string> _cachedParamNameSet;
        Dictionary<string, string> _danglingParamResolution;

        /* Returns true if the given owner array matches the cached selection — used to skip condition rebuilds. */
        bool ConditionSelectionUnchanged(UnityEngine.Object[] owners)
        {
            if (_cachedForOwners == null || _cachedForOwners.Length != owners.Length) return false;
            for (int i = 0; i < owners.Length; i++)
                if (_cachedForOwners[i] != owners[i]) return false;
            return true;
        }

        void InvalidateConditionCache() { _conditionCacheDirty = true; _paramCachedController = null; _cachedParamNameSet = null; }

        /* Toggles one shared-condition match criterion (Name/Mode/Value), refusing to leave all three inactive. */
        void SetConditionMatchCriterion(ref bool criterion, bool value)
        {
            int activeCount = (_matchConditionName ? 1 : 0) + (_matchConditionMode ? 1 : 0) + (_matchConditionValue ? 1 : 0);
            bool willTurnOff = criterion && !value;
            if (willTurnOff && activeCount <= 1) return;
            criterion = value;
            InvalidateConditionCache();
        }

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
            float parameterColumnWidth = headerRect.width * 0.4f;
            float smallBtnWidth = (headerRect.width - parameterColumnWidth) / 6f;

            if (Event.current.type == EventType.Repaint)
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);

            string modeLabel = _showSharedConditions ? L10n.Get("transitions.shared_conditions") : L10n.Get("transitions.all_conditions");
            if (CursorBtn(new Rect(headerRect.x, headerRect.y, parameterColumnWidth, headerRect.height), new GUIContent("  " + modeLabel, L10n.Get("transitions.tooltip.toggle_conditions")), Styles.CondModeBtn))
                _showSharedConditions = !_showSharedConditions;

            float columnX = headerRect.x + parameterColumnWidth;
            var matchNameRect  = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height); columnX += smallBtnWidth;
            var matchModeRect  = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height); columnX += smallBtnWidth;
            var matchValueRect = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height); columnX += smallBtnWidth;
            var switchRect     = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height); columnX += smallBtnWidth;
            var mergeRect      = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height); columnX += smallBtnWidth;
            var separateRect   = new Rect(columnX,                     headerRect.y, smallBtnWidth, headerRect.height);

            if (CursorBtn(matchNameRect,  new GUIContent("N", L10n.Get("transitions.tooltip.match_name")),  _matchConditionName  ? Styles.IconBtnActive : Styles.IconBtn)) SetConditionMatchCriterion(ref _matchConditionName, !_matchConditionName);
            if (CursorBtn(matchModeRect,  new GUIContent("M", L10n.Get("transitions.tooltip.match_mode")),  _matchConditionMode  ? Styles.IconBtnActive : Styles.IconBtn)) SetConditionMatchCriterion(ref _matchConditionMode, !_matchConditionMode);
            if (CursorBtn(matchValueRect, new GUIContent("V", L10n.Get("transitions.tooltip.match_value")), _matchConditionValue ? Styles.IconBtnActive : Styles.IconBtn)) SetConditionMatchCriterion(ref _matchConditionValue, !_matchConditionValue);

            if (CursorBtn(switchRect, new GUIContent("⇄", L10n.Get("transitions.tooltip.switch_modes")), Styles.CondSwitchBtn)) ReverseAllConditions();

            MergeIcon.tooltip = L10n.Get("transitions.tooltip.merge");
            if (CursorBtn(mergeRect, MergeIcon, Styles.IconBtn)) { MergeTransitions(); MergeEntryTransitions(); }

            SeparateIcon.tooltip = L10n.Get("transitions.tooltip.separate");
            if (CursorBtn(separateRect, SeparateIcon, Styles.IconBtn)) { SeparateTransitions(); SeparateEntryTransitions(); }

            /* Padded + colored body — rows and add button only */
            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            var bodyRect = EditorGUILayout.BeginVertical(Styles.CondBody, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            var allOwners = AllSelectedOwners();
            if (_conditionCacheDirty || _cachedEntries == null || _cachedForSharedMode != _showSharedConditions || !ConditionSelectionUnchanged(allOwners) || ParameterListChanged())
            {
                _cachedEntries = GetDisplayedConditions(allOwners);
                _cachedDuplicateParameters = new HashSet<(UnityEngine.Object, string)>(
                    _cachedEntries.GroupBy(entry => (entry.owner, entry.condition.parameter))
                                  .Where(group => group.Count() > 1)
                                  .Where(group =>
                                  {
                                      var paramType = GetParamType(group.Key.Item2);
                                      if (paramType == AnimatorControllerParameterType.Float)
                                      {
                                          var thresholds = group.Select(e => e.condition.threshold).ToList();
                                          return thresholds.Count != thresholds.Distinct().Count();
                                      }
                                      if (paramType == AnimatorControllerParameterType.Int)
                                      {
                                          /* Equals/NotEqual are exact-match and always suspicious in pairs; Greater/Less only warn on an exact threshold clash. */
                                          var conditions = group.Select(e => e.condition).ToList();
                                          int eqNeqCount = conditions.Count(c => c.mode == AnimatorConditionMode.Equals || c.mode == AnimatorConditionMode.NotEqual);
                                          if (eqNeqCount > 1) return true;
                                          var rangeThresholds = conditions.Where(c => c.mode == AnimatorConditionMode.Greater || c.mode == AnimatorConditionMode.Less)
                                                                           .Select(c => c.threshold).ToList();
                                          return rangeThresholds.Count != rangeThresholds.Distinct().Count();
                                      }
                                      return true;
                                  })
                                  .Select(group => group.Key));
                _cachedForOwners = allOwners;
                _cachedForSharedMode = _showSharedConditions;
                _danglingParamResolution = BuildDanglingResolution();
                _cachedParameterNames = _controller != null ? _controller.parameters.Select(parameter => parameter.name).ToArray() : Array.Empty<string>();
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

            if (_showSharedConditions || allOwners.Length <= 1)
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
            internal readonly UnityEngine.Object owner;
            internal readonly AnimatorCondition condition;
            internal readonly int index;
            internal readonly Dictionary<UnityEngine.Object, int> sharedIndices;
            internal readonly bool mixedThreshold;
            internal readonly bool mixedName;
            internal readonly bool mixedMode;

            internal CondEntry(UnityEngine.Object owner, AnimatorCondition condition, int index)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = null; this.mixedThreshold = false; this.mixedName = false; this.mixedMode = false; }

            internal CondEntry(UnityEngine.Object owner, AnimatorCondition condition, int index, Dictionary<UnityEngine.Object, int> sharedIndices, bool mixedThreshold, bool mixedName, bool mixedMode)
            { this.owner = owner; this.condition = condition; this.index = index; this.sharedIndices = sharedIndices; this.mixedThreshold = mixedThreshold; this.mixedName = mixedName; this.mixedMode = mixedMode; }

            internal int IndexFor(UnityEngine.Object obj)
                => sharedIndices != null && sharedIndices.TryGetValue(obj, out int idx) ? idx : index;
        }

        /* Builds the flat list of CondEntry to display: all conditions per owner in individual mode, or only conditions shared across all selected owners in shared mode. */
        List<CondEntry> GetDisplayedConditions(UnityEngine.Object[] owners)
        {
            var result = new List<CondEntry>();
            if (owners.Length == 0) return result;

            if (!_showSharedConditions)
            {
                foreach (var owner in owners)
                {
                    var conditions = GetConditions(owner);
                    for (int i = 0; i < conditions.Length; i++)
                        result.Add(new CondEntry(owner, conditions[i], i));
                }
                return result;
            }

            var first = owners[0];
            var firstConditions = GetConditions(first);
            var claimed = new Dictionary<UnityEngine.Object, HashSet<int>>();
            foreach (var owner in owners) claimed[owner] = new HashSet<int>();

            for (int i = 0; i < firstConditions.Length; i++)
            {
                var condition = firstConditions[i];
                var indexMap = new Dictionary<UnityEngine.Object, int> { [first] = i };
                bool allMatch = true;

                foreach (var owner in owners)
                {
                    if (owner == first) continue;
                    int matchIdx = FindConditionIndexExcluding(owner, condition.parameter, condition.mode, condition.threshold, claimed[owner], _matchConditionName, _matchConditionMode, _matchConditionValue);
                    if (matchIdx < 0) { allMatch = false; break; }
                    indexMap[owner] = matchIdx;
                }

                if (!allMatch) continue;
                foreach (var pair in indexMap) claimed[pair.Key].Add(pair.Value);

                bool AnyOwnerDiffers(Func<AnimatorCondition, object> field) => indexMap.Any(pair =>
                {
                    if (pair.Key == first) return false;
                    var ownerConditions = GetConditions(pair.Key);
                    return pair.Value < ownerConditions.Length && !Equals(field(ownerConditions[pair.Value]), field(condition));
                });
                bool mixedThreshold = !_matchConditionValue && AnyOwnerDiffers(c => c.threshold);
                bool mixedName      = !_matchConditionName  && AnyOwnerDiffers(c => c.parameter);
                bool mixedMode      = !_matchConditionMode  && AnyOwnerDiffers(c => c.mode);
                result.Add(new CondEntry(first, condition, i, indexMap, mixedThreshold, mixedName, mixedMode));
            }
            return result;
        }

        /* Selects every transition directly on the current state machine level (states' transitions, any-state, entry)
           whose conditions contain a match for target under the active N/M/V criteria. Replaces the current selection. */
        void SelectMatchingConditionTransitions(AnimatorCondition target)
        {
            if (_activeStateMachine == null) return;
            var candidates = _activeStateMachine.states
                .SelectMany(childState => childState.state.transitions)
                .Concat<UnityEngine.Object>(_activeStateMachine.anyStateTransitions)
                .Concat(_activeStateMachine.entryTransitions);
            Selection.objects = candidates
                .Where(obj => GetConditions(obj).Any(c => ConditionMatchesCriteria(c, target, _matchConditionName, _matchConditionMode, _matchConditionValue)))
                .ToArray();
        }

        /* Draws one condition row: parameter dropdown, mode/value controls, and a remove button.
           Layout is split into parameter (50%), mode (25%), value, and remove columns. */
        void DrawConditionRow(int rowIdx, CondEntry entry, HashSet<(UnityEngine.Object, string)> duplicateParameters, bool altRow = false)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var ownerConditions = GetConditions(entry.owner);
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

            const float filterIconOffsetNoTypeIcons = 30f;
            const float dropdownArrowWidth = 18f;

            /* Consumes a click inside rect before any wider control (e.g. the parameter dropdown) gets a chance to claim it. */
            bool filterClicked = false;
            void ClaimFilterClick(Rect rect)
            {
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    filterClicked = true;
                    Event.current.Use();
                }
            }

            void PaintFilterIcon(Rect rect)
            {
                FilterIcon.tooltip = L10n.Get("transitions.tooltip.select_matching");
                GUI.Label(rect, FilterIcon);
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }

            if (_controller == null || _controller.parameters.Length == 0)
            {
                var filterRect = new Rect(parameterRect.xMax - filterIconOffsetNoTypeIcons, parameterRect.y, 16f, parameterRect.height);
                ClaimFilterClick(filterRect);
                GUI.Label(parameterRect, condition.parameter, EditorStyles.miniLabel);
                PaintFilterIcon(filterRect);
                CursorBtn(removeRect, "−", Styles.CondBtn);
                if (filterClicked) SelectMatchingConditionTransitions(condition);
                return;
            }

            bool parameterExists = _cachedParamNameSet?.Contains(condition.parameter) ?? false;
            if (!parameterExists)
            {
                var filterRect = new Rect(parameterRect.xMax - filterIconOffsetNoTypeIcons, parameterRect.y, 16f, parameterRect.height);
                ClaimFilterClick(filterRect);
                var previousColor = GUI.color;
                GUI.color = Color.red;
                GUI.Label(parameterRect, condition.parameter, EditorStyles.miniLabel);
                GUI.color = previousColor;
                PaintFilterIcon(filterRect);
                if (CursorBtn(removeRect, "−", Styles.CondBtn)) RemoveConditionFromTargets(entry);
                if (filterClicked) SelectMatchingConditionTransitions(condition);
                return;
            }

            var parameterType = GetParamType(condition.parameter);
            bool showTypeIcons  = AnimatorDefaultSettings.Load().showParamTypeIcons;
            bool isDuplicateParam = duplicateParameters.Contains((entry.owner, condition.parameter));
            float duplicateIconRightOffset = showTypeIcons
                ? parameterType switch
                {
                    AnimatorControllerParameterType.Float   => 58f,
                    AnimatorControllerParameterType.Bool    => 55f,
                    AnimatorControllerParameterType.Int     => 45f,
                    _                                       => 68f
                }
                : filterIconOffsetNoTypeIcons;
            float filterIconRightOffset = isDuplicateParam ? duplicateIconRightOffset + 18f : duplicateIconRightOffset;
            var mainFilterRect = new Rect(parameterRect.xMax - filterIconRightOffset, parameterRect.y, 16f, parameterRect.height);
            ClaimFilterClick(mainFilterRect);

            var capturedEntry = entry;
            var capturedCondition = condition;
            string paramLabel = entry.mixedName ? "—" : condition.parameter;
            float paramLabelMaxWidth = parameterRect.width - filterIconRightOffset - dropdownArrowWidth;
            paramLabel = TruncateTextLeft(paramLabel, EditorStyles.popup, paramLabelMaxWidth);
            if (EditorGUI.DropdownButton(parameterRect, new GUIContent(paramLabel), FocusType.Passive))
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

            if (isDuplicateParam)
            {
                var duplicateIconContent = new GUIContent(EditorGUIUtility.IconContent("d_console.erroricon").image, L10n.Get("transitions.duplicate_param_tooltip"));
                var duplicateIconRect    = new Rect(parameterRect.xMax - duplicateIconRightOffset, parameterRect.y, 16, parameterRect.height);
                GUI.Label(duplicateIconRect, duplicateIconContent);
            }
            PaintFilterIcon(mainFilterRect);
            if (filterClicked) SelectMatchingConditionTransitions(condition);

            if (Event.current.type == EventType.Repaint && showTypeIcons)
                GUI.Label(parameterRect, parameterType.ToString(), Styles.MiniLabelRight);

            if (parameterType == AnimatorControllerParameterType.Bool)
            {
                bool isTrue = condition.mode != AnimatorConditionMode.IfNot;
                var boolButtonRect = new Rect(conditionModeRect.x, conditionModeRect.y, conditionModeRect.width + valueColumnWidth, conditionModeRect.height);
                string boolLabel = entry.mixedMode ? "—" : (isTrue ? L10n.Get("transitions.bool_true") : L10n.Get("transitions.bool_false"));
                if (CursorBtn(boolButtonRect, boolLabel, isTrue ? Styles.BoolBtnTrue : Styles.BoolBtnFalse))
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = isTrue ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, threshold = 0f }, preserveParameter: true);
            }
            else if (parameterType != AnimatorControllerParameterType.Trigger)
            {
                var modeLabel = entry.mixedMode ? "—" : ModeLabel(condition.mode);
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
                            }, preserveThreshold: true, preserveParameter: true);
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
                    ReplaceConditionOnTargets(entry, new AnimatorCondition { parameter = condition.parameter, mode = condition.mode, threshold = newThreshold }, preserveParameter: true, preserveMode: true);
                EditorGUI.showMixedValue = false;
            }

            if (CursorBtn(removeRect, "−", Styles.CondBtn))
                RemoveConditionFromTargets(entry);
        }

        /* Truncates text from the left (prepending an ellipsis) so it fits maxWidth, keeping the tail of the string (e.g. parameter name suffixes) visible. */
        static string TruncateTextLeft(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            const string ellipsis = "…";
            for (int start = 1; start < text.Length; start++)
            {
                string candidate = ellipsis + text.Substring(start);
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) return candidate;
            }
            return ellipsis;
        }

        /* Looks up the parameter type by name from the active controller, defaulting to Float if not found. */
        AnimatorControllerParameterType GetParamType(string paramName)
        {
            if (_controller == null) return AnimatorControllerParameterType.Float;
            var parameter = _controller.parameters.FirstOrDefault(x => x.name == paramName);
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

        /* Replaces the entry's condition with replacement on one owner (individual mode) or all selected owners (shared mode).
           preserveParameter/preserveMode/preserveThreshold keep that field as each owner's own existing value instead of
           overwriting it with replacement's — used in shared mode when that field isn't a required match criterion, so it
           may legitimately differ per owner and must not be clobbered by an edit to a different field. */
        void ReplaceConditionOnTargets(CondEntry entry, AnimatorCondition replacement, bool preserveThreshold = false, bool preserveParameter = false, bool preserveMode = false)
        {
            InvalidateConditionCache();
            if (!_showSharedConditions)
            {
                var ownerConditions = GetConditions(entry.owner);
                if (entry.index < ownerConditions.Length)
                    RebuildConditions(entry.owner, entry.index, replacement);
            }
            else
            {
                foreach (var owner in AllSelectedOwners())
                {
                    int idx = entry.IndexFor(owner);
                    var ownerConditions = GetConditions(owner);
                    if (idx < 0 || idx >= ownerConditions.Length) continue;
                    var own = ownerConditions[idx];
                    var actual = new AnimatorCondition
                    {
                        parameter = preserveParameter ? own.parameter : replacement.parameter,
                        mode      = preserveMode      ? own.mode      : replacement.mode,
                        threshold = preserveThreshold  ? own.threshold : replacement.threshold
                    };
                    RebuildConditions(owner, idx, actual);
                }
            }
        }

        /* Removes the entry's condition from one owner (individual mode) or from all selected owners (shared mode). */
        void RemoveConditionFromTargets(CondEntry entry)
        {
            InvalidateConditionCache();
            var targets = _showSharedConditions ? AllSelectedOwners() : new[] { entry.owner };
            foreach (var owner in targets)
            {
                int idx = _showSharedConditions ? entry.IndexFor(owner) : entry.index;
                var ownerConditions = GetConditions(owner);
                if (idx < 0 || idx >= ownerConditions.Length) continue;
                Undo.RecordObject(owner, "Remove Condition");
                var allConditions = ownerConditions.ToArray();
                foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
                for (int i = 0; i < allConditions.Length; i++)
                    if (i != idx) AddConditionTo(owner, allConditions[i].mode, allConditions[i].threshold, allConditions[i].parameter);
                EditorUtility.SetDirty(owner);
            }
        }

        /* Adds a new condition using the parameter after the last one added (params-list order, wraps at end). */
        void AddConditionToAll()
        {
            InvalidateConditionCache();
            if (_controller == null || _controller.parameters.Length == 0) return;
            var owners = AllSelectedOwners();
            if (owners.Length == 0) return;
            var defaultParam = _controller.parameters[0];
            if (owners.Length == 1 || _showSharedConditions)
            {
                var lastCondition = GetConditions(owners[0]).LastOrDefault();
                var previousIndex = System.Array.FindIndex(_controller.parameters, p => p.name == lastCondition.parameter);
                if (previousIndex >= 0)
                    defaultParam = _controller.parameters[(previousIndex + 1) % _controller.parameters.Length];
            }
            foreach (var owner in owners)
            {
                Undo.RecordObject(owner, "Add Condition");
                AddConditionTo(owner, DefaultModeForType(defaultParam.type), 0f, defaultParam.name);
                EditorUtility.SetDirty(owner);
            }
        }

        /* Inverts every condition mode on all selected owners (If↔IfNot, Greater↔Less, Equals↔NotEqual). */
        void ReverseAllConditions()
        {
            InvalidateConditionCache();
            foreach (var owner in AllSelectedOwners())
            {
                Undo.RecordObject(owner, "Reverse Conditions");
                var allConditions = GetConditions(owner).ToArray();
                foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
                foreach (var condition in allConditions) AddConditionTo(owner, ReverseMode(condition.mode), condition.threshold, condition.parameter);
                EditorUtility.SetDirty(owner);
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

        /* Returns the first unclaimed index in owner's conditions matching the reference condition, or -1. */
        int FindConditionIndexExcluding(UnityEngine.Object owner, string paramName, AnimatorConditionMode mode, float threshold, HashSet<int> exclude, bool matchName, bool matchMode, bool matchValue)
        {
            var reference = new AnimatorCondition { parameter = paramName, mode = mode, threshold = threshold };
            var conditions = GetConditions(owner);
            for (int i = 0; i < conditions.Length; i++)
            {
                if (exclude.Contains(i)) continue;
                if (!ConditionMatchesCriteria(conditions[i], reference, matchName, matchMode, matchValue)) continue;
                return i;
            }
            return -1;
        }

        /* Bool/Trigger params encode their value in mode (If/IfNot), not threshold — comparing raw
           threshold across a bool and an int/float condition is meaningless, so matchValue only
           compares threshold when both conditions' parameters share the same value representation. */
        static bool ConditionMatchesCriteria(AnimatorCondition condition, AnimatorCondition target, bool matchName, bool matchMode, bool matchValue)
        {
            if (matchName && condition.parameter != target.parameter) return false;
            if (matchMode && condition.mode != target.mode) return false;
            if (matchValue)
            {
                bool conditionIsBoolLike = IsBoolLikeMode(condition.mode);
                bool targetIsBoolLike = IsBoolLikeMode(target.mode);
                if (conditionIsBoolLike != targetIsBoolLike) return false;
                if (conditionIsBoolLike ? condition.mode != target.mode : condition.threshold != target.threshold) return false;
            }
            return true;
        }

        static bool IsBoolLikeMode(AnimatorConditionMode mode) => mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot;

        /* Clears and re-adds all conditions on the owner, substituting replacement at replaceIdx. */
        static void RebuildConditions(UnityEngine.Object owner, int replaceIdx, AnimatorCondition replacement)
        {
            Undo.RecordObject(owner, "Edit Condition");
            var allConditions = GetConditions(owner).ToArray();
            foreach (var condition in allConditions) RemoveConditionFrom(owner, condition);
            for (int i = 0; i < allConditions.Length; i++)
            {
                var condition = i == replaceIdx ? replacement : allConditions[i];
                AddConditionTo(owner, condition.mode, condition.threshold, condition.parameter);
            }
            EditorUtility.SetDirty(owner);
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
                        AddConditionTo(primary, condition.mode, condition.threshold, condition.parameter);
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

        void MergeEntryTransitions()
        {
            if (_selectedEntryTransitions.Length < 2 || _controller == null) return;
            var transitions = _selectedEntryTransitions.Where(t => t != null).ToArray();
            Undo.RegisterCompleteObjectUndo(_controller, "Merge Transitions");

            var groups = new Dictionary<string, List<AnimatorTransition>>();
            foreach (var transition in transitions)
            {
                string key = transition.destinationState != null ? transition.destinationState.GetInstanceID().ToString()
                    : transition.destinationStateMachine != null ? transition.destinationStateMachine.GetInstanceID().ToString()
                    : "?";
                if (!groups.ContainsKey(key)) groups[key] = new List<AnimatorTransition>();
                groups[key].Add(transition);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                var ownerSM = FindEntryOwnerSM(_controller, group[0]);
                if (ownerSM == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerSM, "Merge Transitions");
                Undo.RecordObject(group[0], "Merge Transitions");
                foreach (var transition in group.Skip(1))
                {
                    foreach (var condition in transition.conditions)
                        AddConditionTo(group[0], condition.mode, condition.threshold, condition.parameter);
                    ownerSM.RemoveEntryTransition(transition);
                }
                EditorUtility.SetDirty(group[0]);
            }

            EditorUtility.SetDirty(_controller);
            InvalidateConditionCache();
        }

        void SeparateEntryTransitions()
        {
            if (_selectedEntryTransitions.Length == 0 || _controller == null) return;
            var transitions = _selectedEntryTransitions.Where(t => t != null).ToArray();
            Undo.RegisterCompleteObjectUndo(_controller, "Separate Transitions");

            foreach (var transition in transitions)
            {
                var conditions = transition.conditions.ToArray();
                if (conditions.Length <= 1) continue;
                var ownerSM = FindEntryOwnerSM(_controller, transition);
                if (ownerSM == null) continue;
                Undo.RegisterCompleteObjectUndo(ownerSM, "Separate Transitions");

                for (int i = 1; i < conditions.Length; i++)
                {
                    AnimatorTransition newTransition = transition.destinationState != null
                        ? ownerSM.AddEntryTransition(transition.destinationState)
                        : transition.destinationStateMachine != null
                            ? ownerSM.AddEntryTransition(transition.destinationStateMachine)
                            : null;
                    if (newTransition == null) continue;
                    Undo.RegisterCreatedObjectUndo(newTransition, "Separate Transitions");
                    newTransition.mute = transition.mute;
                    newTransition.solo = transition.solo;
                    newTransition.AddCondition(conditions[i].mode, conditions[i].threshold, conditions[i].parameter);
                    EditorUtility.SetDirty(newTransition);
                }

                Undo.RecordObject(transition, "Separate Transitions");
                foreach (var condition in conditions.Skip(1)) transition.RemoveCondition(condition);
                EditorUtility.SetDirty(transition);
            }

            EditorUtility.SetDirty(_controller);
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

        static AnimatorStateMachine FindEntryOwnerSM(AnimatorController controller, AnimatorTransition transition)
        {
            foreach (var layer in controller.layers)
            {
                var found = FindEntryOwnerSMRecursive(layer.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }

        static AnimatorStateMachine FindEntryOwnerSMRecursive(AnimatorStateMachine sm, AnimatorTransition transition)
        {
            if (sm.entryTransitions.Contains(transition)) return sm;
            foreach (var childSM in sm.stateMachines)
            {
                var found = FindEntryOwnerSMRecursive(childSM.stateMachine, transition);
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
            var ownerSM = FindOwnerSM(controller, transition);
            if (ownerSM == null) return "?";
            if (ownerSM.anyStateTransitions.Contains(transition)) return "anystate";
            foreach (var childState in ownerSM.states)
                if (childState.state.transitions.Contains(transition)) return childState.state.GetInstanceID().ToString();
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

        /* True if transition originates from an AnyState node. */
        bool IsAnyStateTransition(AnimatorStateTransition transition)
        {
            var ownerSM = FindOwnerSM(_controller, transition);
            return ownerSM != null && ownerSM.anyStateTransitions.Contains(transition);
        }

        /* Applies mutate to every selected state transition with undo recording, then marks each dirty. */
        void SetOnAll(Action<AnimatorStateTransition> mutate)
        {
            foreach (var transition in _selectedTransitions)
            {
                Undo.RecordObject(transition, "Edit Transition");
                mutate(transition);
                EditorUtility.SetDirty(transition);
            }
        }

        /* Returns all selected owners (state transitions + entry transitions) as a combined UnityEngine.Object array. */
        UnityEngine.Object[] AllSelectedOwners() =>
            _selectedTransitions.Where(t => t != null).Cast<UnityEngine.Object>()
            .Concat(_selectedEntryTransitions.Where(t => t != null))
            .ToArray();

        /* Returns the conditions array for any transition type. */
        static AnimatorCondition[] GetConditions(UnityEngine.Object obj) =>
            obj is AnimatorStateTransition stateTrans ? stateTrans.conditions :
            obj is AnimatorTransition entryTrans      ? entryTrans.conditions :
            Array.Empty<AnimatorCondition>();

        /* Adds a condition to any transition type. */
        static void AddConditionTo(UnityEngine.Object obj, AnimatorConditionMode mode, float threshold, string param)
        {
            if (obj is AnimatorStateTransition stateTrans) stateTrans.AddCondition(mode, threshold, param);
            else if (obj is AnimatorTransition entryTrans) entryTrans.AddCondition(mode, threshold, param);
        }

        /* Removes a condition from any transition type. */
        static void RemoveConditionFrom(UnityEngine.Object obj, AnimatorCondition condition)
        {
            if (obj is AnimatorStateTransition stateTrans) stateTrans.RemoveCondition(condition);
            else if (obj is AnimatorTransition entryTrans) entryTrans.RemoveCondition(condition);
        }
    }
}
#endif
