#if UNITY_EDITOR
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
        void DrawStatesTab()
        {
            DrawStateListHeader();

            if (_selectedStates.Length > 0)
                DrawStateRows();

            EditorGUILayout.Space(4);
            DrawStateAlignButtons();
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawStateProperties();
            EditorGUILayout.Space(4);
            DrawVRCDriversSection();
            EditorGUILayout.Space(4);
            DrawVRCPlayAudioSection();
            EditorGUILayout.Space(4);
            DrawVRCTrackingSection();
        }

        // ── State list ────────────────────────────────────────────────────────

        void DrawStateListHeader()
        {
            using var _ = new EditorGUILayout.HorizontalScope(Styles.SectionHeader);

            if (CursorBtn("✕", Styles.HeaderCloseBtn, GUILayout.Width(20), GUILayout.Height(24)))
                Selection.objects = Array.Empty<UnityEngine.Object>();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Selected {_selectedStates.Length} States", Styles.HeaderLabel, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
        }

        void DrawStateRows()
        {
            float rowHeight = EditorGUIUtility.singleLineHeight;
            const float nameWidth = 140f;
            foreach (var state in _selectedStates)
            {
                using var _ = new EditorGUILayout.HorizontalScope(GUILayout.Height(rowHeight));

                if (CursorBtn("✕", Styles.StateRowXBtn, GUILayout.Width(20), GUILayout.Height(rowHeight)))
                {
                    Selection.objects = _selectedStates.Where(x => x != state).Cast<UnityEngine.Object>().ToArray();
                    return;
                }

                GUILayout.FlexibleSpace();

                if (CursorBtn("Out", Styles.IconBtn, GUILayout.Width(44), GUILayout.Height(rowHeight)))
                    SelectOutgoingTransitions(new[] { state });

                GUILayout.Label(TruncateToFit(state.name, Styles.StateRowName, nameWidth), Styles.StateRowName, GUILayout.Width(nameWidth), GUILayout.Height(rowHeight));

                if (CursorBtn("In", Styles.IconBtn, GUILayout.Width(44), GUILayout.Height(rowHeight)))
                    SelectIncomingTransitions(_controller, new[] { state });

                GUILayout.FlexibleSpace();
            }
        }

        /* Truncates text to fit within maxWidth pixels using style's CalcSize, appending an ellipsis when trimmed. */
        static string TruncateToFit(string text, GUIStyle style, float maxWidth)
        {
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            string truncated = text;
            while (truncated.Length > 0 && style.CalcSize(new GUIContent(truncated + "…")).x > maxWidth)
                truncated = truncated[..^1];
            return truncated + "…";
        }

        // ── Align buttons ─────────────────────────────────────────────────────

        void DrawStateAlignButtons()
        {
            using (new EditorGUI.DisabledScope(_selectedStates.Length < 2))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (CursorBtn("Align Vertical",   Styles.IconBtn)) AlignStates(vertical: true);
                if (CursorBtn("Align Horizontal", Styles.IconBtn)) AlignStates(vertical: false);
            }
            using (new EditorGUI.DisabledScope(_selectedStates.Length < 3))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (CursorBtn("Distribute Vertical",   Styles.IconBtn)) DistributeStates(vertical: true);
                if (CursorBtn("Distribute Horizontal", Styles.IconBtn)) DistributeStates(vertical: false);
            }
        }

        // ── State properties ──────────────────────────────────────────────────

        void DrawStateProperties()
        {
            int count = _selectedStates.Length;
            bool empty = count == 0;
            bool multi = count > 1;
            var first = empty ? null : _selectedStates[0];

            using var disabled = new EditorGUI.DisabledScope(empty);
            var stateIcon = EditorGUIUtility.ObjectContent(null, typeof(AnimatorState)).image;
            float rowHeight  = EditorGUIUtility.singleLineHeight;
            float iconHeight = rowHeight * 2f + EditorGUIUtility.standardVerticalSpacing;

            // Name + Tag with shared tall icon
            using (new EditorGUILayout.HorizontalScope())
            {
                var iconRect = EditorGUILayout.GetControlRect(false, iconHeight, GUILayout.Width(iconHeight));
                if (stateIcon != null)
                    GUI.DrawTexture(iconRect, stateIcon, ScaleMode.ScaleToFit);

                using (new EditorGUILayout.VerticalScope())
                {
                    // Name
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Name", GUILayout.Width(80));
                        EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.name != first.name);
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField(empty ? "" : first.name);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (multi)
                            {
                                var layerStateNames = CollectLayerStateNamesExcluding(_selectedStates);
                                int nextIndex = 1;
                                for (int i = 0; i < _selectedStates.Length; i++)
                                {
                                    string candidate;
                                    if (i == 0)
                                    {
                                        candidate = newName;
                                    }
                                    else
                                    {
                                        do { candidate = newName + " " + nextIndex++; } while (layerStateNames.Contains(candidate));
                                    }
                                    layerStateNames.Add(candidate);
                                    Undo.RecordObject(_selectedStates[i], "Edit State");
                                    _selectedStates[i].name = candidate;
                                    EditorUtility.SetDirty(_selectedStates[i]);
                                }
                            }
                            else
                            {
                                SetStateOnAll(state => state.name = newName);
                            }
                        }
                        EditorGUI.showMixedValue = false;
                    }

                    // Tag
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Tag", GUILayout.Width(80));
                        EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.tag != first.tag);
                        EditorGUI.BeginChangeCheck();
                        string newTag = EditorGUILayout.TextField(empty ? "" : first.tag);
                        if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.tag = newTag);
                        EditorGUI.showMixedValue = false;
                    }
                }
            }

            // Motion
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Motion", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.motion != first.motion);
                EditorGUI.BeginChangeCheck();
                var newMotion = (Motion)EditorGUILayout.ObjectField(empty ? null : first.motion, typeof(Motion), false);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.motion = newMotion);
                EditorGUI.showMixedValue = false;
            }

            // Speed
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Speed", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.speed, first.speed));
                EditorGUI.BeginChangeCheck();
                float newSpeed = EditorGUILayout.FloatField(empty ? 1f : first.speed);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.speed = newSpeed);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.speedParameterActive != first.speedParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newSpeedActive = EditorGUILayout.ToggleLeft("Parameter", empty ? false : first.speedParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.speedParameterActive = newSpeedActive);
                EditorGUI.showMixedValue = false;
            }

            // Multiplier (speed parameter name)
            bool speedParamActive = !empty && first.speedParameterActive;
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!speedParamActive))
            {
                EditorGUILayout.LabelField("Multiplier", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.speedParameter != first.speedParameter);
                EditorGUI.BeginChangeCheck();
                string newSpeedParameter = DrawFloatParamDropdown(empty ? "" : first.speedParameter);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.speedParameter = newSpeedParameter);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                GUILayout.Label("Parameter", GUILayout.Width(90));
            }

            // Motion Time
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Motion Time", GUILayout.Width(110));
                if (!empty && first.timeParameterActive)
                {
                    EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.timeParameter != first.timeParameter);
                    EditorGUI.BeginChangeCheck();
                    string newTimeParameter = DrawFloatParamDropdown(first.timeParameter);
                    if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.timeParameter = newTimeParameter);
                    EditorGUI.showMixedValue = false;
                }
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.timeParameterActive != first.timeParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newTimeActive = EditorGUILayout.ToggleLeft("Parameter", empty ? false : first.timeParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.timeParameterActive = newTimeActive);
                EditorGUI.showMixedValue = false;
            }

            // Mirror
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Mirror", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.mirror != first.mirror);
                EditorGUI.BeginChangeCheck();
                bool newMirror = EditorGUILayout.Toggle(empty ? false : first.mirror, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.mirror = newMirror);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.mirrorParameterActive != first.mirrorParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newMirrorActive = EditorGUILayout.ToggleLeft("Parameter", empty ? false : first.mirrorParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.mirrorParameterActive = newMirrorActive);
                EditorGUI.showMixedValue = false;
            }

            // Cycle Offset
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Cycle Offset", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => !Mathf.Approximately(x.cycleOffset, first.cycleOffset));
                EditorGUI.BeginChangeCheck();
                float newCycleOffset = EditorGUILayout.FloatField(empty ? 0f : first.cycleOffset);
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.cycleOffset = newCycleOffset);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.cycleOffsetParameterActive != first.cycleOffsetParameterActive);
                EditorGUI.BeginChangeCheck();
                bool newOffsetParameterActive = EditorGUILayout.ToggleLeft("Parameter", empty ? false : first.cycleOffsetParameterActive, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.cycleOffsetParameterActive = newOffsetParameterActive);
                EditorGUI.showMixedValue = false;
            }

            // Write Defaults | Foot IK
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Write Defaults", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.writeDefaultValues != first.writeDefaultValues);
                EditorGUI.BeginChangeCheck();
                bool newWriteDefaults = EditorGUILayout.Toggle(empty ? true : first.writeDefaultValues, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.writeDefaultValues = newWriteDefaults);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Foot IK", GUILayout.Width(55));
                EditorGUI.showMixedValue = multi && _selectedStates.Any(x => x.iKOnFeet != first.iKOnFeet);
                EditorGUI.BeginChangeCheck();
                bool newFootIK = EditorGUILayout.Toggle(empty ? false : first.iKOnFeet, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetStateOnAll(state => state.iKOnFeet = newFootIK);
                EditorGUI.showMixedValue = false;
            }
        }

        // ── VRC Drivers section ───────────────────────────────────────────────

        void DrawVRCDriversSection()
        {
            bool anyHave = _selectedStates.Any(state => GetDriverForState(state) != null);
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetDriverForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                GUILayout.Label("Shared VRCParameter Drivers", Styles.HeaderLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                bool hasAnyParams = _selectedStates.Any(state => { var driver = GetDriverForState(state); return driver != null && driver.parameters.Count > 0; });
                if (!hasAnyParams && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                {
                    AddDriverParam();
                    anyHave = true;
                }
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveDriverFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.CondSectionBg);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            // Debug String + Local Only row
            using (new EditorGUILayout.HorizontalScope())
            {
                var drivers = _selectedStates.Select(state => GetDriverForState(state)).Where(driver => driver != null).ToArray();
                if (drivers.Length > 0)
                {
                    bool multiDrivers = drivers.Length > 1;
                    var firstDriver = drivers[0];
                    EditorGUILayout.LabelField("Debug String", GUILayout.Width(80));
                    EditorGUI.showMixedValue = multiDrivers && drivers.Any(driver => driver.debugString != firstDriver.debugString);
                    EditorGUI.BeginChangeCheck();
                    string newDebugString = EditorGUILayout.TextField(firstDriver.debugString ?? "");
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (var state in _selectedStates)
                        {
                            var driver = GetDriverForState(state);
                            if (driver == null) continue;
                            Undo.RecordObject(driver, "Edit Debug String");
                            driver.debugString = newDebugString;
                            EditorUtility.SetDirty(driver);
                        }
                    }
                    EditorGUI.showMixedValue = false;
                }
                DrawLocalOnlyButton();
            }

            var entries = GetSharedDriverParams();
            if (entries.Count == 0)
                EditorGUILayout.LabelField("List is Empty", Styles.EmptyLabel);
            else
                foreach (var entry in entries)
                    DrawDriverParamRow(entry);

            float rowHeight = EditorGUIUtility.singleLineHeight;
            var addRow = EditorGUILayout.GetControlRect(false, rowHeight);
            if (CursorBtn(new Rect(addRow.xMax - 24f, addRow.y, 24f, rowHeight), "+", Styles.CondActionBtn))
                AddDriverParam();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLocalOnlyButton()
        {
            bool? localOnly = GetSharedLocalOnly();
            var prevColor = GUI.color;
            GUI.color = localOnly == null ? Color.grey
                      : localOnly.Value   ? new Color(0.4f, 0.9f, 0.4f)
                      :                     new Color(0.9f, 0.4f, 0.4f);
            if (CursorBtn("Local Only", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(24)))
            {
                bool newLocalOnly = localOnly != true;
                foreach (var state in _selectedStates)
                {
                    var driver = GetOrCreateDriver(state);
                    Undo.RecordObject(driver, "Set Local Only");
                    driver.localOnly = newLocalOnly;
                    EditorUtility.SetDirty(driver);
                }
            }
            GUI.color = prevColor;
        }

        bool? GetSharedLocalOnly()
        {
            if (_selectedStates.Length == 0) return false;
            var drivers = _selectedStates
                .Select(state => GetDriverForState(state))
                .Where(driver => driver != null)
                .ToArray();
            if (drivers.Length == 0) return false;
            bool firstLocalOnly = drivers[0].localOnly;
            return drivers.All(driver => driver.localOnly == firstLocalOnly) ? (bool?)firstLocalOnly : null;
        }

        readonly struct DriverParamEntry
        {
            internal readonly VRC_AvatarParameterDriver.Parameter param;
            internal readonly int index;
            internal DriverParamEntry(VRC_AvatarParameterDriver.Parameter param, int index)
            { this.param = param; this.index = index; }
        }

        List<DriverParamEntry> GetSharedDriverParams()
        {
            var result = new List<DriverParamEntry>();
            if (_selectedStates.Length == 0) return result;

            var firstDriver = GetDriverForState(_selectedStates[0]);
            if (firstDriver == null || firstDriver.parameters.Count == 0) return result;

            for (int i = 0; i < firstDriver.parameters.Count; i++)
            {
                var param = firstDriver.parameters[i];
                bool sharedAcrossAll = _selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null && driver.parameters.Any(parameter => DriverParamsMatch(parameter, param));
                });
                if (sharedAcrossAll)
                    result.Add(new DriverParamEntry(param, i));
            }
            return result;
        }

        /* Draws one row of the shared parameter driver list: name dropdown, type popup, value/range/chance field (adapted to param type and ChangeType), and remove button. */
        void DrawDriverParamRow(DriverParamEntry entry)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var param = entry.param;

            float nameWidth   = row.width * 0.5f;
            float typeWidth   = row.width * 0.25f;
            float removeWidth = 24f;
            float valueWidth  = row.width - nameWidth - typeWidth - removeWidth;

            float currentX = row.x;
            var nameRect   = new Rect(currentX, row.y, nameWidth,    row.height); currentX += nameWidth;
            var typeRect   = new Rect(currentX, row.y, typeWidth,    row.height); currentX += typeWidth;
            var valRect    = new Rect(currentX, row.y, valueWidth,   row.height); currentX += valueWidth;
            var removeRect = new Rect(currentX, row.y, removeWidth,  row.height);

            var capturedEntry = entry;
            if (EditorGUI.DropdownButton(nameRect, new GUIContent(string.IsNullOrEmpty(param.name) ? "—" : param.name), FocusType.Keyboard))
                ShowParameterDropdown(nameRect, newName =>
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, name: newName)));

            var paramType = GetParamType(param.name);
            bool isBool = paramType == AnimatorControllerParameterType.Bool;

            var changeTypes = isBool
                ? new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Random }
                : new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Add, VRC_AvatarParameterDriver.ChangeType.Random };
            var changeLabels = isBool ? new[] { "Set", "Random" } : new[] { "Set", "Add", "Random" };

            int typeIndex = Mathf.Max(0, Array.IndexOf(changeTypes, param.type));
            EditorGUI.BeginChangeCheck();
            int newTypeIndex = EditorGUI.Popup(typeRect, typeIndex, changeLabels);
            if (EditorGUI.EndChangeCheck())
                ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, type: changeTypes[newTypeIndex]));

            if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Set)
            {
                float toggleWidth = EditorGUIUtility.singleLineHeight;
                var toggleRect = new Rect(valRect.x + (valRect.width - toggleWidth) * 0.5f, valRect.y, toggleWidth, valRect.height);
                EditorGUI.BeginChangeCheck();
                bool newBoolValue = EditorGUI.Toggle(toggleRect, param.value >= 0.5f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newBoolValue ? 1f : 0f));
            }
            else if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                float labelWidth = 44f;
                GUI.Label(new Rect(valRect.x, valRect.y, labelWidth, valRect.height), "Chance", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newChance = EditorGUI.Slider(new Rect(valRect.x + labelWidth, valRect.y, valRect.width - labelWidth, valRect.height), param.chance, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, chance: newChance));
            }
            else if (param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                float labelWidth = 26f;
                float fieldWidth = (valueWidth - labelWidth * 2f) * 0.5f;
                float valueX = valRect.x;
                GUI.Label(new Rect(valueX, valRect.y, labelWidth, valRect.height), "Min", EditorStyles.miniLabel);
                valueX += labelWidth;
                EditorGUI.BeginChangeCheck();
                float newMin = EditorGUI.FloatField(new Rect(valueX, valRect.y, fieldWidth, valRect.height), param.valueMin);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMin: newMin));
                valueX += fieldWidth;
                GUI.Label(new Rect(valueX, valRect.y, labelWidth, valRect.height), "Max", EditorStyles.miniLabel);
                valueX += labelWidth;
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUI.FloatField(new Rect(valueX, valRect.y, fieldWidth, valRect.height), param.valueMax);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMax: newMax));
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                float newValue = EditorGUI.FloatField(valRect, param.value);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newValue));
            }

            if (CursorBtn(removeRect, "−", Styles.CondActionBtn))
                RemoveDriverParam(entry);
        }

        /* Returns a shallow copy of original with any provided fields overridden. Used to produce immutable replacements for driver parameter rows. */
        static VRC_AvatarParameterDriver.Parameter CloneParam(
            VRC_AvatarParameterDriver.Parameter original,
            string name = null,
            VRC_AvatarParameterDriver.ChangeType? type = null,
            float? value = null,
            float? valueMin = null,
            float? valueMax = null,
            float? chance = null)
        => new VRC_AvatarParameterDriver.Parameter
        {
            name     = name     ?? original.name,
            type     = type     ?? original.type,
            value    = value    ?? original.value,
            valueMin = valueMin ?? original.valueMin,
            valueMax = valueMax ?? original.valueMax,
            chance   = chance   ?? original.chance
        };

        /* Replaces the parameter at entry's position across all selected states' drivers with replacement, matched by DriverParamsMatch. */
        void ReplaceDriverParam(DriverParamEntry entry, VRC_AvatarParameterDriver.Parameter replacement)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                int parameterIndex = FindDriverParamIndex(driver, entry.param);
                if (parameterIndex < 0) continue;
                Undo.RecordObject(driver, "Edit Driver Parameter");
                driver.parameters[parameterIndex] = replacement;
                EditorUtility.SetDirty(driver);
            }
        }

        /* Removes entry's parameter from every selected state's driver, destroying the driver component entirely if its list becomes empty. */
        void RemoveDriverParam(DriverParamEntry entry)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                int parameterIndex = FindDriverParamIndex(driver, entry.param);
                if (parameterIndex < 0) continue;
                Undo.RecordObject(driver, "Remove Driver Parameter");
                driver.parameters.RemoveAt(parameterIndex);
                if (driver.parameters.Count == 0)
                {
                    Undo.RegisterCompleteObjectUndo(state, "Remove Driver Parameter");
                    state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                    Undo.DestroyObjectImmediate(driver);
                }
                EditorUtility.SetDirty(state);
            }
        }

        void AddDriverParam()
        {
            string firstParam = _controller?.parameters.Length > 0 ? _controller.parameters[0].name : string.Empty;
            foreach (var state in _selectedStates)
            {
                var driver = GetOrCreateDriver(state);
                Undo.RecordObject(driver, "Add Driver Parameter");
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = firstParam,
                    value = 0f
                });
                EditorUtility.SetDirty(driver);
            }
        }

        void RemoveDriverFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Driver");
                state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                Undo.DestroyObjectImmediate(driver);
                EditorUtility.SetDirty(state);
            }
        }

        /* Returns the index of the first parameter in driver.parameters that matches target via DriverParamsMatch, or -1 if not found. */
        static int FindDriverParamIndex(VRCAvatarParameterDriver driver, VRC_AvatarParameterDriver.Parameter target)
        {
            var parameters = driver.parameters;
            for (int i = 0; i < parameters.Count; i++)
                if (DriverParamsMatch(parameters[i], target)) return i;
            return -1;
        }

        /* Returns true if a and b share the same name, type, and value fields (uses min/max/chance for Random type). */
        static bool DriverParamsMatch(VRC_AvatarParameterDriver.Parameter a, VRC_AvatarParameterDriver.Parameter b)
        {
            if (a.name != b.name || a.type != b.type) return false;
            return b.type == VRC_AvatarParameterDriver.ChangeType.Random
                ? Mathf.Approximately(a.valueMin, b.valueMin) &&
                  Mathf.Approximately(a.valueMax, b.valueMax) &&
                  Mathf.Approximately(a.chance,   b.chance)
                : Mathf.Approximately(a.value, b.value);
        }

        static VRCAvatarParameterDriver GetDriverForState(AnimatorState state)
            => state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();

        /* Returns the existing VRCAvatarParameterDriver on state, or adds and registers a new one via Undo. */
        static VRCAvatarParameterDriver GetOrCreateDriver(AnimatorState state)
        {
            var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();
            if (driver != null) return driver;
            driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            Undo.RegisterCreatedObjectUndo(driver, "Create VRC Driver");
            EditorUtility.SetDirty(state);
            return driver;
        }

        // ── VRC Play Audio section ────────────────────────────────────────────

        bool _clipsExpanded = true;
        ReorderableList _clipsReorderList;
        List<AudioClip> _clipsListData;
        int _removeClipIndex = -1;

        void DrawVRCPlayAudioSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetAudioForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetAudioForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                GUILayout.Label("Shared VRC Play Audio", Styles.HeaderLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateAudio(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveAudioFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.CondSectionBg);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayAudioFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPlayAudioFields()
        {
            var statesWithAudio = _selectedStates.Where(state => GetAudioForState(state) != null).ToArray();
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;

            void SetOnAll(string undoName, Action<VRCAnimatorPlayAudio> mutate)
            {
                foreach (var state in _selectedStates)
                {
                    var audio = GetOrCreateAudio(state);
                    Undo.RecordObject(audio, undoName);
                    mutate(audio);
                    EditorUtility.SetDirty(audio);
                }
            }

            // AudioSource drag & drop → fills Source Path
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("AudioSource", GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var droppedSource = (AudioSource)EditorGUILayout.ObjectField(null, typeof(AudioSource), true);
                if (EditorGUI.EndChangeCheck() && droppedSource != null)
                {
                    var descriptor = droppedSource.GetComponentInParent<VRCAvatarDescriptor>();
                    string resolvedPath = GetAudioSourcePath(droppedSource.transform, descriptor != null ? descriptor.transform : null);
                    SetOnAll("Set Source Path", audio => audio.SourcePath = resolvedPath);
                }
            }

            // Source Path
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source Path", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).SourcePath != first.SourcePath);
                EditorGUI.BeginChangeCheck();
                string newPath = EditorGUILayout.TextField(first.SourcePath ?? "");
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Source Path", audio => audio.SourcePath = newPath);
                EditorGUI.showMixedValue = false;
            }

            // Playback Order + Clips Apply Settings
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Playback Order", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlaybackOrder != first.PlaybackOrder);
                EditorGUI.BeginChangeCheck();
                var newOrder = (VRCAnimatorPlayAudio.Order)EditorGUILayout.EnumPopup(first.PlaybackOrder);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Playback Order", audio => audio.PlaybackOrder = newOrder);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ClipsApplySettings != first.ClipsApplySettings);
                EditorGUI.BeginChangeCheck();
                var newClipsApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.ClipsApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Clips Apply Settings", audio => audio.ClipsApplySettings = newClipsApply);
                EditorGUI.showMixedValue = false;
            }

            // Parameter Name (only when first uses Parameter order)
            if (first.PlaybackOrder == VRCAnimatorPlayAudio.Order.Parameter)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Parameter Name", GUILayout.Width(110));
                    EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ParameterName != first.ParameterName);
                    EditorGUI.BeginChangeCheck();
                    string newParam = DrawIntParamDropdown(first.ParameterName ?? "");
                    if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Parameter Name", audio => audio.ParameterName = newParam);
                    EditorGUI.showMixedValue = false;
                }
            }

            // Clips list
            DrawPlayAudioClipsList(statesWithAudio);

            // Volume
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Volume", GUILayout.Width(55));
                EditorGUILayout.LabelField("Min", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.x, first.Volume.x));
                EditorGUI.BeginChangeCheck();
                float newVolMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.x), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Volume Min", audio => audio.Volume = new Vector2(newVolMin, audio.Volume.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField("Max", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.y, first.Volume.y));
                EditorGUI.BeginChangeCheck();
                float newVolMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.y), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Volume Max", audio => audio.Volume = new Vector2(audio.Volume.x, newVolMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).VolumeApplySettings != first.VolumeApplySettings);
                EditorGUI.BeginChangeCheck();
                var newVolApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.VolumeApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Volume Apply Settings", audio => audio.VolumeApplySettings = newVolApply);
                EditorGUI.showMixedValue = false;
            }

            // Pitch
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Pitch", GUILayout.Width(55));
                EditorGUILayout.LabelField("Min", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.x, first.Pitch.x));
                EditorGUI.BeginChangeCheck();
                float newPitchMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.x), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Pitch Min", audio => audio.Pitch = new Vector2(newPitchMin, audio.Pitch.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField("Max", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.y, first.Pitch.y));
                EditorGUI.BeginChangeCheck();
                float newPitchMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.y), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Pitch Max", audio => audio.Pitch = new Vector2(audio.Pitch.x, newPitchMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PitchApplySettings != first.PitchApplySettings);
                EditorGUI.BeginChangeCheck();
                var newPitchApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.PitchApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Pitch Apply Settings", audio => audio.PitchApplySettings = newPitchApply);
                EditorGUI.showMixedValue = false;
            }

            // Loop
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Loop", GUILayout.Width(55));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).Loop != first.Loop);
                EditorGUI.BeginChangeCheck();
                bool newLoop = EditorGUILayout.Toggle(first.Loop, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Loop", audio => audio.Loop = newLoop);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).LoopApplySettings != first.LoopApplySettings);
                EditorGUI.BeginChangeCheck();
                var newLoopApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.LoopApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Loop Apply Settings", audio => audio.LoopApplySettings = newLoopApply);
                EditorGUI.showMixedValue = false;
            }

            // Play/Stop column headers
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label("Stop", EditorStyles.miniLabel, GUILayout.Width(40));
                GUILayout.Label("Play", EditorStyles.miniLabel, GUILayout.Width(40));
            }

            // On Enter
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("On Enter", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnEnter != first.StopOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newStopEnter = EditorGUILayout.Toggle(first.StopOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Stop On Enter", audio => audio.StopOnEnter = newStopEnter);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnEnter != first.PlayOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newPlayEnter = EditorGUILayout.Toggle(first.PlayOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Play On Enter", audio => audio.PlayOnEnter = newPlayEnter);
                EditorGUI.showMixedValue = false;
            }

            // On Exit
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("On Exit", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnExit != first.StopOnExit);
                EditorGUI.BeginChangeCheck();
                bool newStopExit = EditorGUILayout.Toggle(first.StopOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Stop On Exit", audio => audio.StopOnExit = newStopExit);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnExit != first.PlayOnExit);
                EditorGUI.BeginChangeCheck();
                bool newPlayExit = EditorGUILayout.Toggle(first.PlayOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Play On Exit", audio => audio.PlayOnExit = newPlayExit);
                EditorGUI.showMixedValue = false;
            }

            // Play On Enter Delay (last, only relevant when PlayOnEnter)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Play On Enter Delay In Seconds", GUILayout.Width(220));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).DelayInSeconds, first.DelayInSeconds));
                EditorGUI.BeginChangeCheck();
                float newDelay = Mathf.Clamp(EditorGUILayout.FloatField(first.DelayInSeconds), 0f, 60f);
                if (EditorGUI.EndChangeCheck()) SetOnAll("Edit Play Delay", audio => audio.DelayInSeconds = newDelay);
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws the foldable clips list with a size int field and a ReorderableList for editing, reordering, and removing audio clips across all statesWithAudio. */
        void DrawPlayAudioClipsList(AnimatorState[] statesWithAudio)
        {
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;
            var clips = first.Clips ?? Array.Empty<AudioClip>();
            float rowHeight = EditorGUIUtility.singleLineHeight;

            // Outer container — single background covers foldout header + list body
            var outerRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, Styles.CondSectionBg);

            // Foldout header + size int field — now inside the background
            var headerRow = EditorGUILayout.GetControlRect(false, rowHeight);
            const float sizeWidth = 40f;
            var foldoutRect = new Rect(headerRow.x, headerRow.y, headerRow.width - sizeWidth - 4f, rowHeight);
            _clipsExpanded = EditorGUI.Foldout(foldoutRect, _clipsExpanded, "Clips", true, EditorStyles.foldout);
            EditorGUIUtility.AddCursorRect(foldoutRect, MouseCursor.Link);

            EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => (GetAudioForState(s).Clips?.Length ?? 0) != clips.Length);
            EditorGUI.BeginChangeCheck();
            int newSize = Mathf.Max(0, EditorGUI.IntField(new Rect(headerRow.xMax - sizeWidth, headerRow.y, sizeWidth, rowHeight), clips.Length));
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var state in _selectedStates)
                {
                    var audio = GetOrCreateAudio(state);
                    Undo.RecordObject(audio, "Resize Clips");
                    var resized = new AudioClip[newSize];
                    if (audio.Clips != null) Array.Copy(audio.Clips, resized, Mathf.Min(audio.Clips.Length, newSize));
                    audio.Clips = resized;
                    EditorUtility.SetDirty(audio);
                }
                clips = first.Clips ?? Array.Empty<AudioClip>();
                _clipsListData = null;
                _clipsReorderList = null;
            }
            EditorGUI.showMixedValue = false;

            if (_clipsExpanded)
            {
                // Keep _clipsListData in sync with current clips
                if (_clipsListData == null || _clipsListData.Count != clips.Length)
                    _clipsListData = new List<AudioClip>(clips);
                else
                    for (int i = 0; i < clips.Length; i++)
                        _clipsListData[i] = clips[i];

                // Build ReorderableList once; rebuilt when nulled
                if (_clipsReorderList == null)
                {
                    _clipsReorderList = new ReorderableList(_clipsListData, typeof(AudioClip), true, false, false, false)
                    {
                        elementHeight = rowHeight,
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };

                    _clipsReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index >= _clipsListData.Count) return;
                        var localStates = _selectedStates.Where(state => GetAudioForState(state) != null).ToArray();
                        bool localMulti = localStates.Length > 1;

                        EditorGUI.showMixedValue = localMulti && localStates.Any(state => {
                            var audio = GetAudioForState(state);
                            return audio.Clips == null || index >= audio.Clips.Length || audio.Clips[index] != _clipsListData[index];
                        });
                        EditorGUI.BeginChangeCheck();
                        var newClip = (AudioClip)EditorGUI.ObjectField(
                            new Rect(rect.x, rect.y + 1f, rect.width - 26f, rect.height - 2f),
                            _clipsListData[index], typeof(AudioClip), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _clipsListData[index] = newClip;
                            int capturedIndex = index;
                            foreach (var state in _selectedStates)
                            {
                                var audio = GetOrCreateAudio(state);
                                if (audio.Clips == null || capturedIndex >= audio.Clips.Length)
                                {
                                    var expanded = new AudioClip[capturedIndex + 1];
                                    audio.Clips?.CopyTo(expanded, 0);
                                    audio.Clips = expanded;
                                }
                                Undo.RecordObject(audio, "Edit Audio Clip");
                                audio.Clips[capturedIndex] = newClip;
                                EditorUtility.SetDirty(audio);
                            }
                        }
                        EditorGUI.showMixedValue = false;

                        if (GUI.Button(new Rect(rect.xMax - 24f, rect.y + 1f, 24f, rect.height - 2f), "−", Styles.CondActionBtn))
                            _removeClipIndex = index;
                    };

                    _clipsReorderList.onReorderCallbackWithDetails = (reorderableList, oldIndex, newIndex) =>
                    {
                        var firstAudio = GetAudioForState(_selectedStates[0]);
                        if (firstAudio != null)
                        {
                            Undo.RecordObject(firstAudio, "Reorder Clips");
                            firstAudio.Clips = _clipsListData.ToArray();
                            EditorUtility.SetDirty(firstAudio);
                        }
                        for (int stateIndex = 1; stateIndex < _selectedStates.Length; stateIndex++)
                        {
                            var audio = GetOrCreateAudio(_selectedStates[stateIndex]);
                            if (audio.Clips == null || audio.Clips.Length < 2) continue;
                            Undo.RecordObject(audio, "Reorder Clips");
                            var stateClips = audio.Clips.ToList();
                            if (oldIndex < stateClips.Count)
                            {
                                var item = stateClips[oldIndex];
                                stateClips.RemoveAt(oldIndex);
                                stateClips.Insert(Mathf.Clamp(newIndex, 0, stateClips.Count), item);
                                audio.Clips = stateClips.ToArray();
                            }
                            EditorUtility.SetDirty(audio);
                        }
                    };
                }

                if (clips.Length == 0)
                    EditorGUILayout.LabelField("List is Empty", Styles.EmptyLabel);
                else
                    _clipsReorderList.DoLayoutList();

                // Deferred remove — avoids layout mismatch from inside drawElementCallback
                if (_removeClipIndex >= 0)
                {
                    int capturedIndex = _removeClipIndex;
                    _removeClipIndex = -1;
                    foreach (var state in _selectedStates)
                    {
                        var audio = GetOrCreateAudio(state);
                        if (audio.Clips == null || capturedIndex >= audio.Clips.Length) continue;
                        Undo.RecordObject(audio, "Remove Audio Clip");
                        audio.Clips = audio.Clips.Where((_, idx) => idx != capturedIndex).ToArray();
                        EditorUtility.SetDirty(audio);
                    }
                    _clipsReorderList = null;
                }
                else
                {
                    var addRow = EditorGUILayout.GetControlRect(false, rowHeight);
                    if (CursorBtn(new Rect(addRow.xMax - 24f, addRow.y, 24f, rowHeight), "+", Styles.CondActionBtn))
                    {
                        foreach (var state in _selectedStates)
                        {
                            var audio = GetOrCreateAudio(state);
                            Undo.RecordObject(audio, "Add Audio Clip");
                            var expanded = new AudioClip[(audio.Clips?.Length ?? 0) + 1];
                            audio.Clips?.CopyTo(expanded, 0);
                            audio.Clips = expanded;
                            EditorUtility.SetDirty(audio);
                        }
                        _clipsReorderList = null;
                    }
                }

                GUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        static VRCAnimatorPlayAudio GetAudioForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorPlayAudio>().FirstOrDefault();

        /* Returns the existing VRCAnimatorPlayAudio on state, or adds and registers a new one via Undo. */
        static VRCAnimatorPlayAudio GetOrCreateAudio(AnimatorState state)
        {
            var audio = state.behaviours.OfType<VRCAnimatorPlayAudio>().FirstOrDefault();
            if (audio != null) return audio;
            audio = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            Undo.RegisterCreatedObjectUndo(audio, "Create VRC Play Audio");
            EditorUtility.SetDirty(state);
            return audio;
        }

        // ── VRC Tracking Control section ──────────────────────────────────────

        void DrawVRCTrackingSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetTrackingForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetTrackingForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                GUILayout.Label("Shared VRC Tracking Control", Styles.HeaderLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateTracking(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveTrackingFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.CondSectionBg);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawTrackingFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawTrackingFields()
        {
            var statesWithTracking = _selectedStates.Where(state => GetTrackingForState(state) != null).ToArray();
            var first = GetTrackingForState(statesWithTracking[0]);
            bool multi = statesWithTracking.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label("No Change", EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label("Tracking",  EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label("Animation", EditorStyles.miniLabel, GUILayout.Width(70));
            }

            // Set All row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Set All", GUILayout.Width(110));
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.NoChange,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Tracking,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Animation, 70f);
            }
            EditorGUILayout.Space(2f);

            DrawTrackingRow("Head",           statesWithTracking, audio => audio.trackingHead,         (a, v) => a.trackingHead         = v);
            DrawTrackingRow("Left Hand",      statesWithTracking, audio => audio.trackingLeftHand,      (a, v) => a.trackingLeftHand     = v);
            DrawTrackingRow("Right Hand",     statesWithTracking, audio => audio.trackingRightHand,     (a, v) => a.trackingRightHand    = v);
            DrawTrackingRow("Hip",            statesWithTracking, audio => audio.trackingHip,           (a, v) => a.trackingHip          = v);
            DrawTrackingRow("Left Foot",      statesWithTracking, audio => audio.trackingLeftFoot,      (a, v) => a.trackingLeftFoot     = v);
            DrawTrackingRow("Right Foot",     statesWithTracking, audio => audio.trackingRightFoot,     (a, v) => a.trackingRightFoot    = v);
            DrawTrackingRow("Left Fingers",   statesWithTracking, audio => audio.trackingLeftFingers,   (a, v) => a.trackingLeftFingers  = v);
            DrawTrackingRow("Right Fingers",  statesWithTracking, audio => audio.trackingRightFingers,  (a, v) => a.trackingRightFingers = v);
            DrawTrackingRow("Eyes & Eyelids", statesWithTracking, audio => audio.trackingEyes,          (a, v) => a.trackingEyes         = v);
            DrawTrackingRow("Mouth & Jaw",    statesWithTracking, audio => audio.trackingMouth,         (a, v) => a.trackingMouth        = v);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Debug String", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithTracking.Any(state => GetTrackingForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var tracking = GetOrCreateTracking(state);
                        Undo.RecordObject(tracking, "Edit Debug String");
                        tracking.debugString = newDebugString;
                        EditorUtility.SetDirty(tracking);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws a single tracking body-part row with label and three radio toggles (NoChange/Tracking/Animation), applying set to all selected states on change. */
        void DrawTrackingRow(
            string label,
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set)
        {
            var firstVal = get(GetTrackingForState(statesWithTracking[0]));
            bool mixed = statesWithTracking.Length > 1 && statesWithTracking.Any(state => get(GetTrackingForState(state)) != firstVal);

            Color labelColor = mixed
                ? new Color(0.4f, 0.7f, 1f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Tracking  ? new Color(0.4f, 0.9f, 0.4f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Animation ? new Color(1f, 0.85f, 0.2f)
                : Color.white;

            using (new EditorGUILayout.HorizontalScope())
            {
                var prevColor = GUI.color;
                GUI.color = labelColor;
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                GUI.color = prevColor;
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.NoChange,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Tracking,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Animation, firstVal, mixed, 70f);
            }
        }

        /* Draws one radio Toggle for targetType; sets all selected states to targetType via set when clicked while not already selected. */
        void DrawTrackingRadio(
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            VRC_AnimatorTrackingControl.TrackingType currentVal,
            bool mixed,
            float width)
        {
            bool isSelected = !mixed && currentVal == targetType;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(isSelected, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !isSelected)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Edit Tracking Control");
                    set(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        static VRCAnimatorTrackingControl GetTrackingForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();

        /* Returns the existing VRCAnimatorTrackingControl on state, or adds and registers a new one via Undo. */
        static VRCAnimatorTrackingControl GetOrCreateTracking(AnimatorState state)
        {
            var tracking = state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();
            if (tracking != null) return tracking;
            tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            Undo.RegisterCreatedObjectUndo(tracking, "Create VRC Tracking Control");
            EditorUtility.SetDirty(state);
            return tracking;
        }

        /* Draws a "Set All" radio toggle that sets every tracking field on all selected states to targetType when clicked. */
        void DrawSetAllTrackingRadio(
            AnimatorState[] statesWithTracking,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            float width)
        {
            bool allMatch = statesWithTracking.All(state => TrackingAllFieldsAre(GetTrackingForState(state), targetType));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(allMatch, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !allMatch)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Set All Tracking");
                    TrackingSetAllFields(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        /* Returns true if every tracking field on ctrl equals type, used to determine "Set All" radio state. */
        static bool TrackingAllFieldsAre(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
            => ctrl.trackingHead == type && ctrl.trackingLeftHand == type && ctrl.trackingRightHand == type
            && ctrl.trackingHip == type && ctrl.trackingLeftFoot == type && ctrl.trackingRightFoot == type
            && ctrl.trackingLeftFingers == type && ctrl.trackingRightFingers == type
            && ctrl.trackingEyes == type && ctrl.trackingMouth == type;

        /* Sets every tracking body-part field on ctrl to type in a single statement. */
        static void TrackingSetAllFields(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
        {
            ctrl.trackingHead = ctrl.trackingLeftHand = ctrl.trackingRightHand = ctrl.trackingHip =
            ctrl.trackingLeftFoot = ctrl.trackingRightFoot = ctrl.trackingLeftFingers =
            ctrl.trackingRightFingers = ctrl.trackingEyes = ctrl.trackingMouth = type;
        }

        void RemoveAudioFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var audio = GetAudioForState(state);
                if (audio == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Play Audio");
                state.behaviours = state.behaviours.Where(b => b != audio).ToArray();
                Undo.DestroyObjectImmediate(audio);
                EditorUtility.SetDirty(state);
            }
        }

        void RemoveTrackingFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var tracking = GetTrackingForState(state);
                if (tracking == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Tracking Control");
                state.behaviours = state.behaviours.Where(b => b != tracking).ToArray();
                Undo.DestroyObjectImmediate(tracking);
                EditorUtility.SetDirty(state);
            }
        }

        /* Builds a forward-slash path from sourceTransform up to root (exclusive). Returns "/name" prefixed with slash when root is null, indicating no avatar descriptor was found. */
        static string GetAudioSourcePath(Transform sourceTransform, Transform root)
        {
            string path = sourceTransform.name;
            for (Transform parentTransform = sourceTransform.parent; parentTransform != null && parentTransform != root; parentTransform = parentTransform.parent)
                path = parentTransform.name + "/" + path;
            return root == null ? "/" + path : path;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /* Draws an EditorGUILayout.Popup listing all Int parameters in the active controller and returns the selected parameter name. */
        string DrawIntParamDropdown(string current)
        {
            string[] intParameterNames = _controller != null
                ? _controller.parameters
                    .Where(x => x.type == AnimatorControllerParameterType.Int)
                    .Select(x => x.name)
                    .ToArray()
                : Array.Empty<string>();

            if (intParameterNames.Length == 0)
            {
                GUILayout.Label("No Int parameters in Controller", EditorStyles.miniLabel);
                return current;
            }

            int currentIndex = Mathf.Max(0, Array.IndexOf(intParameterNames, current));
            int selectedIndex = EditorGUILayout.Popup(currentIndex, intParameterNames);
            return intParameterNames[selectedIndex];
        }

        /* Draws an EditorGUILayout.Popup listing all Float parameters in the active controller and returns the selected parameter name. */
        string DrawFloatParamDropdown(string current)
        {
            string[] floatParameterNames = _controller != null
                ? _controller.parameters
                    .Where(x => x.type == AnimatorControllerParameterType.Float)
                    .Select(x => x.name)
                    .ToArray()
                : Array.Empty<string>();

            if (floatParameterNames.Length == 0)
            {
                GUILayout.Label(string.IsNullOrEmpty(current) ? "—" : current, EditorStyles.miniLabel);
                return current;
            }

            int currentIndex = Mathf.Max(0, Array.IndexOf(floatParameterNames, current));
            int selectedIndex = EditorGUILayout.Popup(currentIndex, floatParameterNames);
            return floatParameterNames[selectedIndex];
        }

        /* Sets Selection.objects to all outgoing transitions from every state in states. */
        internal static void SelectOutgoingTransitions(AnimatorState[] states)
        {
            Selection.objects = states
                .SelectMany(state => state.transitions)
                .Cast<UnityEngine.Object>()
                .ToArray();
        }

        /* Sets Selection.objects to all transitions across all layers of controller that point to any state in states. */
        internal static void SelectIncomingTransitions(AnimatorController controller, AnimatorState[] states)
        {
            if (controller == null) return;
            var targets = new HashSet<AnimatorState>(states);
            var incoming = new List<AnimatorStateTransition>();
            foreach (var layer in controller.layers)
                CollectIncoming(layer.stateMachine, targets, incoming);
            Selection.objects = incoming.Cast<UnityEngine.Object>().ToArray();
        }

        /* Recursively collects into result all anyState and state transitions within sm (and nested sub SMs) whose destinationState is in targets. */
        static void CollectIncoming(AnimatorStateMachine sm, HashSet<AnimatorState> targets, List<AnimatorStateTransition> result)
        {
            foreach (var transition in sm.anyStateTransitions)
                if (transition.destinationState != null && targets.Contains(transition.destinationState))
                    result.Add(transition);
            foreach (var childState in sm.states)
                foreach (var transition in childState.state.transitions)
                    if (transition.destinationState != null && targets.Contains(transition.destinationState))
                        result.Add(transition);
            foreach (var childStateMachine in sm.stateMachines)
                CollectIncoming(childStateMachine.stateMachine, targets, result);
        }

        // ── Alignment ─────────────────────────────────────────────────────────

        /* Aligns all selected states to the X (vertical=true) or Y (vertical=false) coordinate of the last selected state, using the last-selected state as anchor. */
        void AlignStates(bool vertical)
        {
            if (_selectedStates.Length < 2 || _controller == null) return;
            var anchor = _selectedStates[_selectedStates.Length - 1];
            var anchorPos = FindStatePosition(anchor);
            if (anchorPos == null) return;

            string undoName = vertical ? "Align States Vertical" : "Align States Horizontal";
            RegisterAllSMUndos(undoName);

            var toAlign = new HashSet<AnimatorState>(_selectedStates.Where(state => state != anchor));
            foreach (var layer in _controller.layers)
            {
                ApplyAlignment(layer.stateMachine, toAlign, vertical, anchorPos.Value);
                if (toAlign.Count == 0) break;
            }

            EditorUtility.SetDirty(_controller);
        }

        /* Evenly spaces all selected states along the vertical or horizontal axis between their minimum and maximum coordinate. */
        void DistributeStates(bool vertical)
        {
            if (_selectedStates.Length < 3 || _controller == null) return;

            var statePositions = _selectedStates
                .Select(state => (state, pos: FindStatePosition(state)))
                .Where(pair => pair.pos.HasValue)
                .Select(pair => (pair.state, pos: pair.pos.Value))
                .OrderBy(pair => vertical ? pair.pos.y : pair.pos.x)
                .ToArray();

            if (statePositions.Length < 3) return;

            float min = vertical ? statePositions[0].pos.y : statePositions[0].pos.x;
            float max = vertical ? statePositions[^1].pos.y : statePositions[^1].pos.x;
            float spacing = (max - min) / (statePositions.Length - 1);

            var newPositions = new Dictionary<AnimatorState, Vector2>();
            for (int i = 0; i < statePositions.Length; i++)
            {
                var (state, pos) = statePositions[i];
                newPositions[state] = vertical
                    ? new Vector2(pos.x, min + i * spacing)
                    : new Vector2(min + i * spacing, pos.y);
            }

            string undoName = vertical ? "Distribute States Vertical" : "Distribute States Horizontal";
            RegisterAllSMUndos(undoName);

            var remaining = new HashSet<AnimatorState>(newPositions.Keys);
            foreach (var layer in _controller.layers)
            {
                ApplyDistribution(layer.stateMachine, remaining, newPositions);
                if (remaining.Count == 0) break;
            }

            EditorUtility.SetDirty(_controller);
        }

        void RegisterAllSMUndos(string name)
        {
            foreach (var layer in _controller.layers)
                RegisterSMUndosRecursive(layer.stateMachine, name);
        }

        /* Registers a complete object undo for sm and all nested sub state machines under name. */
        static void RegisterSMUndosRecursive(AnimatorStateMachine sm, string name)
        {
            Undo.RegisterCompleteObjectUndo(sm, name);
            foreach (var childStateMachine in sm.stateMachines)
                RegisterSMUndosRecursive(childStateMachine.stateMachine, name);
        }

        /* Moves each state in targets found within sm (or its descendants) to match anchor's X (vertical) or Y (horizontal) coordinate. Removes found states from targets to avoid double-visiting. */
        static void ApplyAlignment(AnimatorStateMachine sm, HashSet<AnimatorState> targets, bool vertical, Vector2 anchor)
        {
            var states = sm.states;
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (!targets.Remove(states[i].state)) continue;
                var pos = (Vector2)states[i].position;
                states[i].position = vertical
                    ? new Vector3(anchor.x, pos.y, 0f)
                    : new Vector3(pos.x, anchor.y, 0f);
                changed = true;
            }
            if (changed) { sm.states = states; EditorUtility.SetDirty(sm); }
            if (targets.Count == 0) return;
            foreach (var childStateMachine in sm.stateMachines)
            {
                ApplyAlignment(childStateMachine.stateMachine, targets, vertical, anchor);
                if (targets.Count == 0) return;
            }
        }

        /* Writes the pre-computed positions from newPositions to each matching state in sm and its descendants, removing found states from targets. */
        static void ApplyDistribution(AnimatorStateMachine sm, HashSet<AnimatorState> targets, Dictionary<AnimatorState, Vector2> newPositions)
        {
            var states = sm.states;
            bool changed = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (!targets.Remove(states[i].state)) continue;
                var newPos = newPositions[states[i].state];
                states[i].position = new Vector3(newPos.x, newPos.y, 0f);
                changed = true;
            }
            if (changed) { sm.states = states; EditorUtility.SetDirty(sm); }
            if (targets.Count == 0) return;
            foreach (var childStateMachine in sm.stateMachines)
            {
                ApplyDistribution(childStateMachine.stateMachine, targets, newPositions);
                if (targets.Count == 0) return;
            }
        }

        /* Searches all layers of the active controller for target and returns its node position, or null if not found. */
        Vector2? FindStatePosition(AnimatorState target)
        {
            foreach (var layer in _controller.layers)
            {
                var pos = FindStatePositionInSM(layer.stateMachine, target);
                if (pos.HasValue) return pos;
            }
            return null;
        }

        /* Recursively searches sm and nested sub SMs for target, returning the node position or null. */
        static Vector2? FindStatePositionInSM(AnimatorStateMachine sm, AnimatorState target)
        {
            foreach (var childState in sm.states)
                if (childState.state == target) return (Vector2)childState.position;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var pos = FindStatePositionInSM(childStateMachine.stateMachine, target);
                if (pos.HasValue) return pos;
            }
            return null;
        }

        /* Applies mutate to every selected state under a single Undo.RecordObject call per state. */
        void SetStateOnAll(Action<AnimatorState> mutate)
        {
            foreach (var state in _selectedStates)
            {
                Undo.RecordObject(state, "Edit State");
                mutate(state);
                EditorUtility.SetDirty(state);
            }
        }

        /* Returns the set of all state names across every layer of the active controller, excluding the states in exclude. Used to find available names when batch-renaming. */
        HashSet<string> CollectLayerStateNamesExcluding(AnimatorState[] exclude)
        {
            var excludeSet = new HashSet<AnimatorState>(exclude);
            var names = new HashSet<string>();
            if (_controller == null) return names;
            foreach (var layer in _controller.layers)
                CollectStateNamesExcluding(layer.stateMachine, excludeSet, names);
            return names;
        }

        /* Recursively adds state names from sm and all nested sub SMs into names, skipping any state present in exclude. */
        static void CollectStateNamesExcluding(AnimatorStateMachine sm, HashSet<AnimatorState> exclude, HashSet<string> names)
        {
            foreach (var childState in sm.states)
                if (!exclude.Contains(childState.state))
                    names.Add(childState.state.name);
            foreach (var childStateMachine in sm.stateMachines)
                CollectStateNamesExcluding(childStateMachine.stateMachine, exclude, names);
        }
    }
}
#endif
