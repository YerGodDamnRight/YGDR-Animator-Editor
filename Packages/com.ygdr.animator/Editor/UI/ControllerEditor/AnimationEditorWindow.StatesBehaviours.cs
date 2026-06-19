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
        // ── VRC Drivers section ───────────────────────────────────────────────

        void DrawVRCDriversSection()
        {
            bool anyHave = _selectedStates.Any(state => GetDriverForState(state) != null);
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetDriverForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.param_driver"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                bool hasAnyParams = _selectedStates.Any(state => { var driver = GetDriverForState(state); return driver != null && driver.parameters.Count > 0; });
                if (!hasAnyParams && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    AddDriverParam();
                    anyHave = true;
                }
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemoveDriverFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.CondBody, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            // Debug String + Local Only row
            using (new EditorGUILayout.HorizontalScope())
            {
                var drivers = _selectedStates.Select(state => GetDriverForState(state)).Where(driver => driver != null).ToArray();
                if (drivers.Length > 0)
                {
                    bool multiDrivers = drivers.Length > 1;
                    var firstDriver = drivers[0];
                    EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(80));
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

            var sharedParams = GetSharedDriverParams();

            if (sharedParams.Count == 0)
                EditorGUILayout.LabelField(L10n.Get("vrc.list_empty"), Styles.EmptyLabel);
            else
            {
                if (Event.current.type == EventType.Layout)
                {
                    if (_driverParamListData == null || _driverParamListData.Count != sharedParams.Count)
                    {
                        _driverParamListData = new List<VRC_AvatarParameterDriver.Parameter>(sharedParams.Select(entry => entry.param));
                        _driverParamReorderList = null;
                    }
                    else
                        for (int i = 0; i < sharedParams.Count; i++)
                            _driverParamListData[i] = sharedParams[i].param;
                    _stableElementHeights = sharedParams.Select(entry => ComputeDriverParamHeight(entry.param)).ToArray();
                }

                if (_driverParamReorderList == null)
                {
                    _driverParamReorderList = new ReorderableList(_driverParamListData, typeof(VRC_AvatarParameterDriver.Parameter), true, false, false, false)
                    {
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };
                    _driverParamReorderList.elementHeightCallback = index =>
                        _stableElementHeights != null && index < _stableElementHeights.Length
                            ? _stableElementHeights[index]
                            : EditorGUIUtility.singleLineHeight;

                    _driverParamReorderList.drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (Event.current.type == EventType.Repaint)
                            EditorGUI.DrawRect(rect, index % 2 == 0 ? Styles.SecondaryColor : Styles.RowAltColor);
                    };

                    _driverParamReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index >= _driverParamListData.Count) return;
                        var param = _driverParamListData[index];
                        var localStates = _selectedStates.Where(state => GetDriverForState(state) != null).ToArray();
                        bool localMulti = localStates.Length > 1;
                        bool hasMixedTypes = localMulti && !localStates.All(state => {
                            var driver = GetDriverForState(state);
                            foreach (var p in driver.parameters)
                                if (p.name == param.name) return p.type == param.type;
                            return false;
                        });
                        bool hasMixedValues = hasMixedTypes || (localMulti && !localStates.All(state => {
                            var driver = GetDriverForState(state);
                            foreach (var p in driver.parameters)
                                if (p.name == param.name) return DriverParamsMatch(p, param);
                            return false;
                        }));
                        bool mixedSourceMin = false, mixedSourceMax = false, mixedDestMin = false, mixedDestMax = false;
                        if (localMulti && param.type == VRC_AvatarParameterDriver.ChangeType.Copy && param.convertRange)
                        {
                            foreach (var localState in localStates)
                            {
                                var stateDriver = GetDriverForState(localState);
                                if (stateDriver == null) continue;
                                var stateParam = stateDriver.parameters.FirstOrDefault(p => p.name == param.name);
                                if (stateParam == null) continue;
                                if (!Mathf.Approximately(stateParam.sourceMin, param.sourceMin)) mixedSourceMin = true;
                                if (!Mathf.Approximately(stateParam.sourceMax, param.sourceMax)) mixedSourceMax = true;
                                if (!Mathf.Approximately(stateParam.destMin,   param.destMin))   mixedDestMin   = true;
                                if (!Mathf.Approximately(stateParam.destMax,   param.destMax))   mixedDestMax   = true;
                            }
                        }
                        DrawDriverParamRowRect(new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 2f), new DriverParamEntry(param, index, hasMixedValues, hasMixedTypes, mixedSourceMin, mixedSourceMax, mixedDestMin, mixedDestMax));
                    };

                    _driverParamReorderList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                    {
                        foreach (var state in _selectedStates)
                        {
                            var driver = GetDriverForState(state);
                            if (driver == null || driver.parameters.Count < 2) continue;
                            Undo.RecordObject(driver, "Reorder Driver Parameters");
                            var paramList = driver.parameters.ToList();
                            if (oldIndex < paramList.Count)
                            {
                                var item = paramList[oldIndex];
                                paramList.RemoveAt(oldIndex);
                                paramList.Insert(Mathf.Clamp(newIndex, 0, paramList.Count), item);
                                driver.parameters = paramList;
                            }
                            EditorUtility.SetDirty(driver);
                        }
                    };
                }

                _driverParamReorderList.DoLayoutList();
            }

            if (_removeDriverParamIndex >= 0)
            {
                var capturedEntries = GetSharedDriverParams();
                if (_removeDriverParamIndex < capturedEntries.Count)
                    RemoveDriverParam(capturedEntries[_removeDriverParamIndex]);
                _removeDriverParamIndex = -1;
                _driverParamReorderList = null;
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            float addBtnSize = EditorGUIUtility.singleLineHeight;
            var addRow = EditorGUILayout.GetControlRect(false, addBtnSize);
            if (CursorBtn(new Rect(addRow.xMax - 40f, addRow.y, 24f, addBtnSize), "+", Styles.CondBtn))
            {
                AddDriverParam();
                _driverParamReorderList = null;
            }
        }

        void DrawLocalOnlyButton()
        {
            bool? localOnly = GetSharedLocalOnly();
            var prevColor = GUI.color;
            GUI.color = localOnly == null ? Color.grey
                      : localOnly.Value   ? new Color(0.4f, 0.9f, 0.4f)
                      :                     new Color(0.9f, 0.4f, 0.4f);
            if (CursorBtn(L10n.Get("vrc.local_only"), EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(24)))
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
            internal readonly bool hasMixedValues;
            internal readonly bool hasMixedTypes;
            internal readonly bool mixedSourceMin;
            internal readonly bool mixedSourceMax;
            internal readonly bool mixedDestMin;
            internal readonly bool mixedDestMax;
            internal DriverParamEntry(VRC_AvatarParameterDriver.Parameter param, int index, bool hasMixedValues, bool hasMixedTypes,
                bool mixedSourceMin = false, bool mixedSourceMax = false, bool mixedDestMin = false, bool mixedDestMax = false)
            {
                this.param = param; this.index = index; this.hasMixedValues = hasMixedValues; this.hasMixedTypes = hasMixedTypes;
                this.mixedSourceMin = mixedSourceMin; this.mixedSourceMax = mixedSourceMax;
                this.mixedDestMin = mixedDestMin; this.mixedDestMax = mixedDestMax;
            }
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
                    return driver != null && driver.parameters.Any(parameter => parameter.name == param.name);
                });
                if (!sharedAcrossAll) continue;
                bool hasMixedTypes = !_selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    if (driver == null) return false;
                    foreach (var parameter in driver.parameters)
                        if (parameter.name == param.name) return parameter.type == param.type;
                    return false;
                });
                bool hasMixedValues = hasMixedTypes || !_selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    if (driver == null) return false;
                    foreach (var parameter in driver.parameters)
                        if (parameter.name == param.name) return DriverParamsMatch(parameter, param);
                    return false;
                });
                result.Add(new DriverParamEntry(param, i, hasMixedValues, hasMixedTypes));
            }
            return result;
        }

        /* Draws one row of the shared parameter driver list. Copy type expands to 4–6 rows (source, dest, convertRange, optional min/max). */
        void DrawDriverParamRowRect(Rect row, DriverParamEntry entry)
        {
            var param         = entry.param;
            var capturedEntry = entry;
            float removeWidth    = 24f;
            float rightOverhang  = 6f;
            float singleLine     = EditorGUIUtility.singleLineHeight;

            var paramType = GetParamType(param.name);
            bool isBool   = paramType == AnimatorControllerParameterType.Bool;

            var changeTypes = isBool
                ? new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Random, VRC_AvatarParameterDriver.ChangeType.Copy }
                : new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Add, VRC_AvatarParameterDriver.ChangeType.Random, VRC_AvatarParameterDriver.ChangeType.Copy };
            var changeLabels = isBool
                ? new[] { L10n.Get("vrc.param_driver.set"), L10n.Get("vrc.param_driver.random"), L10n.Get("vrc.param_driver.copy") }
                : new[] { L10n.Get("vrc.param_driver.set"), L10n.Get("vrc.param_driver.add"), L10n.Get("vrc.param_driver.random"), L10n.Get("vrc.param_driver.copy") };

            if (param.type == VRC_AvatarParameterDriver.ChangeType.Copy)
            {
                float labelWidth = row.width * 0.3f;
                float dropWidth  = row.width - labelWidth - removeWidth;

                // Row 1: Type label + type popup + remove
                var copyTypeRect   = new Rect(row.x + labelWidth, row.y, dropWidth, singleLine);
                var copyRemoveRect = new Rect(row.xMax - removeWidth, row.y, removeWidth + rightOverhang, singleLine);

                GUI.Label(new Rect(row.x, row.y, labelWidth, singleLine), L10n.Get("vrc.param_driver.type"), EditorStyles.label);

                int copyTypeIndex = Mathf.Max(0, Array.IndexOf(changeTypes, param.type));
                EditorGUI.showMixedValue = entry.hasMixedTypes;
                EditorGUI.BeginChangeCheck();
                int newCopyTypeIndex = EditorGUI.Popup(copyTypeRect, copyTypeIndex, changeLabels);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, type: changeTypes[newCopyTypeIndex]));
                EditorGUI.showMixedValue = false;

                var previousCopyRemoveBgColor = GUI.backgroundColor;
                if (entry.index % 2 != 0) GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                if (CursorBtn(copyRemoveRect, "−", Styles.CondBtn))
                    _removeDriverParamIndex = entry.index;
                GUI.backgroundColor = previousCopyRemoveBgColor;

                // Row 2: Source dropdown (GenericMenu avoids AdvancedDropdown click-through onto the checkbox below)
                float sourceRowY = row.y + singleLine;
                GUI.Label(new Rect(row.x, sourceRowY, labelWidth, singleLine), L10n.Get("vrc.param_driver.source"), EditorStyles.label);
                var sourceDropRect = new Rect(row.x + labelWidth, sourceRowY, dropWidth, singleLine);
                if (EditorGUI.DropdownButton(sourceDropRect, new GUIContent(string.IsNullOrEmpty(param.source) ? "—" : param.source), FocusType.Passive))
                    ShowCopyParamMenu(capturedEntry, isCopySource: true);
                if (!string.IsNullOrEmpty(param.source))
                    GUI.Label(sourceDropRect, GetParamType(param.source).ToString(), Styles.MiniLabelRight);

                // Row 3: Destination dropdown (GenericMenu avoids AdvancedDropdown click-through onto the checkbox below)
                float destRowY = row.y + singleLine * 2f;
                GUI.Label(new Rect(row.x, destRowY, labelWidth, singleLine), L10n.Get("vrc.param_driver.destination"), EditorStyles.label);
                var destDropRect = new Rect(row.x + labelWidth, destRowY, dropWidth, singleLine);
                if (EditorGUI.DropdownButton(destDropRect, new GUIContent(string.IsNullOrEmpty(param.name) ? "—" : param.name), FocusType.Passive))
                    ShowCopyParamMenu(capturedEntry, isCopySource: false);
                if (!string.IsNullOrEmpty(param.name))
                    GUI.Label(destDropRect, GetParamType(param.name).ToString(), Styles.MiniLabelRight);

                // Hint: source→destination type mismatch
                bool showCopyHint = !string.IsNullOrEmpty(param.source) && GetParamType(param.source) != GetParamType(param.name);
                float hintOffset  = showCopyHint ? singleLine * 2f : 0f;
                if (showCopyHint)
                    EditorGUI.HelpBox(new Rect(row.x, row.y + singleLine * 3f, row.width - removeWidth, singleLine * 2f),
                        $"Value will be converted to {GetParamType(param.name)}", MessageType.Info);

                // Row 4: Convert Range checkbox
                float checkRowY = row.y + singleLine * 3f + hintOffset;
                GUI.Label(new Rect(row.x, checkRowY, labelWidth, singleLine), L10n.Get("vrc.param_driver.convert_range"), EditorStyles.label);
                EditorGUI.BeginChangeCheck();
                bool newConvertRange = EditorGUI.Toggle(new Rect(row.x + labelWidth, checkRowY, singleLine, singleLine), param.convertRange);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, convertRange: newConvertRange));

                if (param.convertRange)
                {
                    float rangeIndent      = 12f;
                    float minMaxLabelWidth = 26f;
                    float rangeFieldWidth  = (dropWidth - minMaxLabelWidth * 2f) * 0.5f;
                    float controlStartX    = row.x + labelWidth;

                    // Row 5: Source (indented) | Min [field] Max [field]
                    float srcRowY = row.y + singleLine * 4f + hintOffset;
                    GUI.Label(new Rect(row.x + rangeIndent, srcRowY, labelWidth - rangeIndent, singleLine), L10n.Get("vrc.param_driver.source"), EditorStyles.label);
                    float srcX = controlStartX;
                    GUI.Label(new Rect(srcX, srcRowY, minMaxLabelWidth, singleLine), L10n.Get("vrc.param_driver.min"), EditorStyles.label);
                    srcX += minMaxLabelWidth;
                    EditorGUI.showMixedValue = entry.mixedSourceMin;
                    EditorGUI.BeginChangeCheck();
                    float newSrcMin = EditorGUI.FloatField(new Rect(srcX, srcRowY, rangeFieldWidth, singleLine), param.sourceMin);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, sourceMin: newSrcMin));
                    EditorGUI.showMixedValue = false;
                    srcX += rangeFieldWidth;
                    GUI.Label(new Rect(srcX, srcRowY, minMaxLabelWidth, singleLine), L10n.Get("vrc.param_driver.max"), EditorStyles.label);
                    srcX += minMaxLabelWidth;
                    EditorGUI.showMixedValue = entry.mixedSourceMax;
                    EditorGUI.BeginChangeCheck();
                    float newSrcMax = EditorGUI.FloatField(new Rect(srcX, srcRowY, rangeFieldWidth, singleLine), param.sourceMax);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, sourceMax: newSrcMax));
                    EditorGUI.showMixedValue = false;

                    // Row 6: Destination (indented) | Min [field] Max [field]
                    float dstRowY = row.y + singleLine * 5f + hintOffset;
                    GUI.Label(new Rect(row.x + rangeIndent, dstRowY, labelWidth - rangeIndent, singleLine), L10n.Get("vrc.param_driver.destination"), EditorStyles.label);
                    float dstX = controlStartX;
                    GUI.Label(new Rect(dstX, dstRowY, minMaxLabelWidth, singleLine), L10n.Get("vrc.param_driver.min"), EditorStyles.label);
                    dstX += minMaxLabelWidth;
                    EditorGUI.showMixedValue = entry.mixedDestMin;
                    EditorGUI.BeginChangeCheck();
                    float newDstMin = EditorGUI.FloatField(new Rect(dstX, dstRowY, rangeFieldWidth, singleLine), param.destMin);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, destMin: newDstMin));
                    EditorGUI.showMixedValue = false;
                    dstX += rangeFieldWidth;
                    GUI.Label(new Rect(dstX, dstRowY, minMaxLabelWidth, singleLine), L10n.Get("vrc.param_driver.max"), EditorStyles.label);
                    dstX += minMaxLabelWidth;
                    EditorGUI.showMixedValue = entry.mixedDestMax;
                    EditorGUI.BeginChangeCheck();
                    float newDstMax = EditorGUI.FloatField(new Rect(dstX, dstRowY, rangeFieldWidth, singleLine), param.destMax);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, destMax: newDstMax));
                    EditorGUI.showMixedValue = false;
                }
                return;
            }

            // Non-Copy: 3 rows — Mode | Parameter | Threshold
            float nonCopyLabelWidth   = row.width * 0.3f;
            float nonCopyControlWidth = row.width - nonCopyLabelWidth - removeWidth;

            // Row 1: Mode label | type popup | remove
            GUI.Label(new Rect(row.x, row.y, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.type"), EditorStyles.label);
            var typeRect   = new Rect(row.x + nonCopyLabelWidth, row.y, nonCopyControlWidth, singleLine);
            var removeRect = new Rect(row.xMax - removeWidth, row.y, removeWidth + rightOverhang, singleLine);

            int typeIndex = Mathf.Max(0, Array.IndexOf(changeTypes, param.type));
            EditorGUI.showMixedValue = entry.hasMixedTypes;
            EditorGUI.BeginChangeCheck();
            int newTypeIndex = EditorGUI.Popup(typeRect, typeIndex, changeLabels);
            if (EditorGUI.EndChangeCheck())
                ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, type: changeTypes[newTypeIndex]));
            EditorGUI.showMixedValue = false;

            var previousBackgroundColor = GUI.backgroundColor;
            if (entry.index % 2 != 0) GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            if (CursorBtn(removeRect, "−", Styles.CondBtn))
                _removeDriverParamIndex = entry.index;
            GUI.backgroundColor = previousBackgroundColor;

            // Row 2: Parameter label | parameter dropdown
            float paramRowY  = row.y + singleLine;
            float paramDropWidth = row.width - nonCopyLabelWidth - removeWidth;
            GUI.Label(new Rect(row.x, paramRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.destination"), EditorStyles.label);
            var nameRect = new Rect(row.x + nonCopyLabelWidth, paramRowY, paramDropWidth, singleLine);
            if (EditorGUI.DropdownButton(nameRect, new GUIContent(string.IsNullOrEmpty(param.name) ? "—" : param.name), FocusType.Passive))
            {
                var nameMenu = new GenericMenu();
                foreach (var controllerParameter in _controller.parameters)
                {
                    var capturedName = controllerParameter.name;
                    nameMenu.AddItem(new GUIContent(capturedName), capturedName == param.name, () =>
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, name: capturedName)));
                }
                nameMenu.ShowAsContext();
            }
            if (!string.IsNullOrEmpty(param.name) && Event.current.type == EventType.Repaint && AnimatorDefaultSettings.Load().showParamTypeIcons)
                GUI.Label(nameRect, paramType.ToString(), Styles.MiniLabelRight);

            // Row 3: threshold label + control (adapts to type and parameter kind)
            float thresholdRowY  = row.y + singleLine * 2f;
            float thresholdWidth = row.width - nonCopyLabelWidth - removeWidth;
            var   thresholdRect  = new Rect(row.x + nonCopyLabelWidth, thresholdRowY, thresholdWidth, singleLine);

            EditorGUI.showMixedValue = entry.hasMixedValues;
            if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Set)
            {
                GUI.Label(new Rect(row.x, thresholdRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.value"), EditorStyles.label);
                float toggleWidth = singleLine;
                EditorGUI.BeginChangeCheck();
                bool newBoolValue = EditorGUI.Toggle(new Rect(thresholdRect.x, thresholdRowY, toggleWidth, singleLine), param.value >= 0.5f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newBoolValue ? 1f : 0f));
            }
            else if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                GUI.Label(new Rect(row.x, thresholdRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.chance"), EditorStyles.label);
                EditorGUI.BeginChangeCheck();
                float newChance = EditorGUI.Slider(thresholdRect, param.chance, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, chance: newChance));
            }
            else if (param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                GUI.Label(new Rect(row.x, thresholdRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.min_value"), EditorStyles.label);
                EditorGUI.BeginChangeCheck();
                float newMin = EditorGUI.FloatField(thresholdRect, param.valueMin);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMin: newMin));

                float maxRowY = row.y + singleLine * 3f;
                GUI.Label(new Rect(row.x, maxRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.max_value"), EditorStyles.label);
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUI.FloatField(new Rect(row.x + nonCopyLabelWidth, maxRowY, thresholdWidth, singleLine), param.valueMax);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMax: newMax));

                float extraRowY = row.y + singleLine * 4f;
                if (paramType == AnimatorControllerParameterType.Int)
                {
                    GUI.Label(new Rect(row.x, extraRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.prevent_repeats"), EditorStyles.label);
                    EditorGUI.BeginChangeCheck();
                    bool newPreventRepeats = EditorGUI.Toggle(new Rect(row.x + nonCopyLabelWidth, extraRowY, singleLine, singleLine), param.preventRepeats);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, preventRepeats: newPreventRepeats));
                }
                else
                {
                    GUI.Label(new Rect(row.x, extraRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.chance"), EditorStyles.label);
                    EditorGUI.BeginChangeCheck();
                    float newChance = EditorGUI.Slider(new Rect(row.x + nonCopyLabelWidth, extraRowY, thresholdWidth, singleLine), param.chance, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, chance: newChance));
                }
            }
            else
            {
                GUI.Label(new Rect(row.x, thresholdRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.value"), EditorStyles.label);
                EditorGUI.BeginChangeCheck();
                float newValue = EditorGUI.FloatField(thresholdRect, param.value);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newValue));
            }
            EditorGUI.showMixedValue = false;
        }

        /* Returns a shallow copy of original with any provided fields overridden. Used to produce immutable replacements for driver parameter rows. */
        static VRC_AvatarParameterDriver.Parameter CloneParam(
            VRC_AvatarParameterDriver.Parameter original,
            string name = null,
            VRC_AvatarParameterDriver.ChangeType? type = null,
            float? value = null,
            float? valueMin = null,
            float? valueMax = null,
            float? chance = null,
            string source = null,
            bool? convertRange = null,
            float? sourceMin = null,
            float? sourceMax = null,
            float? destMin = null,
            float? destMax = null,
            bool? preventRepeats = null)
        => new VRC_AvatarParameterDriver.Parameter
        {
            name           = name           ?? original.name,
            type           = type           ?? original.type,
            value          = value          ?? original.value,
            valueMin       = valueMin       ?? original.valueMin,
            valueMax       = valueMax       ?? original.valueMax,
            chance         = chance         ?? original.chance,
            source         = source         ?? original.source,
            convertRange   = convertRange   ?? original.convertRange,
            sourceMin      = sourceMin      ?? original.sourceMin,
            sourceMax      = sourceMax      ?? original.sourceMax,
            destMin        = destMin        ?? original.destMin,
            destMax        = destMax        ?? original.destMax,
            preventRepeats = preventRepeats ?? original.preventRepeats
        };

void ShowCopyParamMenu(DriverParamEntry entry, bool isCopySource)
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            string current = isCopySource ? entry.param.source : entry.param.name;
            var menu = new GenericMenu();
            foreach (var controllerParameter in _controller.parameters)
            {
                var capturedName = controllerParameter.name;
                bool isSelected  = capturedName == current;
                menu.AddItem(new GUIContent(capturedName), isSelected, () =>
                {
                    var updated = isCopySource
                        ? CloneParam(entry.param, source: capturedName)
                        : CloneParam(entry.param, name: capturedName);
                    ReplaceDriverParam(entry, updated);
                });
            }
            menu.ShowAsContext();
        }

        void ReplaceDriverParam(
            DriverParamEntry entry,
            VRC_AvatarParameterDriver.Parameter replacement)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null)
                    continue;

                int parameterIndex = FindDriverParamIndex(driver, entry.param, entry.index);
                if (parameterIndex < 0)
                    continue;

                Undo.RecordObject(driver, "Edit Driver Parameter");
                driver.parameters[parameterIndex] = MergeParam(driver.parameters[parameterIndex], entry.param, replacement);
                _suppressExternalRepaint = true;
                EditorUtility.SetDirty(driver);
            }
            _driverParamReorderList = null;
        }

        static VRC_AvatarParameterDriver.Parameter MergeParam(
            VRC_AvatarParameterDriver.Parameter existing,
            VRC_AvatarParameterDriver.Parameter original,
            VRC_AvatarParameterDriver.Parameter replacement)
        => new VRC_AvatarParameterDriver.Parameter
        {
            name         = replacement.name         != original.name         ? replacement.name         : existing.name,
            type         = replacement.type         != original.type         ? replacement.type         : existing.type,
            value        = replacement.value        != original.value        ? replacement.value        : existing.value,
            valueMin     = replacement.valueMin     != original.valueMin     ? replacement.valueMin     : existing.valueMin,
            valueMax     = replacement.valueMax     != original.valueMax     ? replacement.valueMax     : existing.valueMax,
            chance       = replacement.chance       != original.chance       ? replacement.chance       : existing.chance,
            source       = replacement.source       != original.source       ? replacement.source       : existing.source,
            convertRange = replacement.convertRange != original.convertRange ? replacement.convertRange : existing.convertRange,
            sourceMin    = replacement.sourceMin    != original.sourceMin    ? replacement.sourceMin    : existing.sourceMin,
            sourceMax    = replacement.sourceMax    != original.sourceMax    ? replacement.sourceMax    : existing.sourceMax,
            destMin      = replacement.destMin      != original.destMin      ? replacement.destMin      : existing.destMin,
            destMax        = replacement.destMax        != original.destMax        ? replacement.destMax        : existing.destMax,
            preventRepeats = replacement.preventRepeats != original.preventRepeats ? replacement.preventRepeats : existing.preventRepeats,
        };

        /* Removes entry's parameter from every selected state's driver, destroying the driver component entirely if its list becomes empty. */
        void RemoveDriverParam(DriverParamEntry entry)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                int parameterIndex = FindDriverParamIndex(driver, entry.param, entry.index);
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
            if (_selectedStates.Length > 1) EnsureUniqueDrivers();
            string defaultName = string.Empty;
            if (_controller?.parameters.Length > 0)
            {
                var defaultParam = _controller.parameters[0];
                var usedNames = new HashSet<string>(_selectedStates.SelectMany(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null ? driver.parameters.Select(parameter => parameter.name) : Enumerable.Empty<string>();
                }));
                var unusedParam = _controller.parameters.FirstOrDefault(parameter => !usedNames.Contains(parameter.name));
                if (unusedParam != null) defaultParam = unusedParam;
                defaultName = defaultParam.name;
            }
            foreach (var state in _selectedStates)
            {
                var driver = GetOrCreateDriver(state);
                Undo.RecordObject(driver, "Add Driver Parameter");
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = defaultName,
                    value = 0f
                });
                EditorUtility.SetDirty(driver);
            }
        }

        /* Detects shared VRCAvatarParameterDriver instances across selected states (caused by Unity
           state duplication sharing C++ behaviours arrays). Breaks sharing by destroying all drivers,
           calling SaveAssets to write independent empty behaviours to disk (reimport separates the
           C++ arrays), then recreating unique drivers and restoring the saved parameter data. */
        void EnsureUniqueDrivers()
        {
            var seenIds = new HashSet<int>();
            bool needsRebuild = false;
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null || !seenIds.Add(driver.GetInstanceID()))
                    needsRebuild = true;
            }
            if (!needsRebuild) return;

            var savedParameters  = new Dictionary<AnimatorState, List<VRC_AvatarParameterDriver.Parameter>>();
            var savedLocalOnly   = new Dictionary<AnimatorState, bool>();
            var savedDebugString = new Dictionary<AnimatorState, string>();
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                savedParameters[state]  = new List<VRC_AvatarParameterDriver.Parameter>(driver.parameters);
                savedLocalOnly[state]   = driver.localOnly;
                savedDebugString[state] = driver.debugString ?? string.Empty;
            }

            var destroyedIds = new HashSet<int>();
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Add Driver Parameter");
                state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                EditorUtility.SetDirty(state);
                if (destroyedIds.Add(driver.GetInstanceID()))
                    Undo.DestroyObjectImmediate(driver);
            }

            // Flush empty behaviours to disk — reimport gives each state an independent
            // C++ behaviours array, permanently breaking the Unity-level sharing.
            AssetDatabase.SaveAssets();

            foreach (var state in _selectedStates)
            {
                var driver = GetOrCreateDriver(state);
                if (!savedParameters.ContainsKey(state)) continue;
                Undo.RecordObject(driver, "Add Driver Parameter");
                driver.parameters  = savedParameters[state];
                driver.localOnly   = savedLocalOnly[state];
                driver.debugString = savedDebugString[state];
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

        /* Returns the index of the parameter in driver.parameters matching target.
           Uses indexHint first (positional match by name) — handles duplicate-name params correctly.
           Falls back to first name match if hint is out of range or name differs. */
        static int FindDriverParamIndex(VRCAvatarParameterDriver driver, VRC_AvatarParameterDriver.Parameter target, int indexHint = -1)
        {
            var parameters = driver.parameters;
            if (indexHint >= 0 && indexHint < parameters.Count && parameters[indexHint].name == target.name)
                return indexHint;
            for (int i = 0; i < parameters.Count; i++)
                if (parameters[i].name == target.name) return i;
            return -1;
        }

        /* Returns true if a and b share the same name, type, and value fields (uses min/max/chance for Random, source for Copy). */
        static bool DriverParamsMatch(VRC_AvatarParameterDriver.Parameter a, VRC_AvatarParameterDriver.Parameter b)
        {
            if (a.name != b.name || a.type != b.type) return false;
            return b.type switch
            {
                VRC_AvatarParameterDriver.ChangeType.Random => Mathf.Approximately(a.valueMin, b.valueMin) &&
                                                               Mathf.Approximately(a.valueMax, b.valueMax) &&
                                                               Mathf.Approximately(a.chance,   b.chance)   &&
                                                               a.preventRepeats == b.preventRepeats,
                VRC_AvatarParameterDriver.ChangeType.Copy   => a.source == b.source &&
                                                               a.convertRange == b.convertRange &&
                                                               (!a.convertRange || (
                                                                   Mathf.Approximately(a.sourceMin, b.sourceMin) &&
                                                                   Mathf.Approximately(a.sourceMax, b.sourceMax) &&
                                                                   Mathf.Approximately(a.destMin,   b.destMin)   &&
                                                                   Mathf.Approximately(a.destMax,   b.destMax))),
                _                                           => Mathf.Approximately(a.value, b.value)
            };
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

        ReorderableList _driverParamReorderList;
        List<VRC_AvatarParameterDriver.Parameter> _driverParamListData;
        float[] _stableElementHeights;
        int _removeDriverParamIndex = -1;

        float ComputeDriverParamHeight(VRC_AvatarParameterDriver.Parameter p)
        {
            float singleLine = EditorGUIUtility.singleLineHeight;
            if (p.type != VRC_AvatarParameterDriver.ChangeType.Copy)
            {
                bool isRandomNonBool = p.type == VRC_AvatarParameterDriver.ChangeType.Random &&
                                      GetParamType(p.name) != AnimatorControllerParameterType.Bool;
                return singleLine * (isRandomNonBool ? 5f : 3f);
            }
            float copyLines = !string.IsNullOrEmpty(p.source) && GetParamType(p.source) != GetParamType(p.name) ? 6f : 4f;
            return p.convertRange ? singleLine * (copyLines + 2f) : singleLine * copyLines;
        }

        void DrawVRCPlayAudioSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetAudioForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetAudioForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.audio"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateAudio(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemoveAudioFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

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

        void SetAudioOnAll(string undoName, Action<VRCAnimatorPlayAudio> mutate)
        {
            foreach (var state in _selectedStates)
            {
                var audio = GetOrCreateAudio(state);
                Undo.RecordObject(audio, undoName);
                mutate(audio);
                EditorUtility.SetDirty(audio);
            }
        }

        void DrawPlayAudioFields()
        {
            var statesWithAudio = _selectedStates.Where(state => GetAudioForState(state) != null).ToArray();
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;

            DrawAudioSourceDragField();
            DrawAudioSourcePathField(first, statesWithAudio, multi);
            DrawAudioPlaybackOrderField(first, statesWithAudio, multi);
            if (first.PlaybackOrder == VRCAnimatorPlayAudio.Order.Parameter)
                DrawAudioParameterNameField(first, statesWithAudio, multi);
            DrawPlayAudioClipsList(statesWithAudio);
            DrawAudioVolumeFields(first, statesWithAudio, multi);
            DrawAudioPitchFields(first, statesWithAudio, multi);
            DrawAudioLoopField(first, statesWithAudio, multi);
            DrawAudioPlayStopColumnHeaders();
            DrawAudioOnEnterFields(first, statesWithAudio, multi);
            DrawAudioOnExitFields(first, statesWithAudio, multi);
            DrawAudioDelayField(first, statesWithAudio, multi);
        }

        void DrawAudioSourceDragField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.source"), GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var droppedSource = (AudioSource)EditorGUILayout.ObjectField(null, typeof(AudioSource), true);
                if (EditorGUI.EndChangeCheck() && droppedSource != null)
                {
                    var descriptor = droppedSource.GetComponentInParent<VRCAvatarDescriptor>();
                    string resolvedPath = GetAudioSourcePath(droppedSource.transform, descriptor != null ? descriptor.transform : null);
                    SetAudioOnAll("Set Source Path", audio => audio.SourcePath = resolvedPath);
                }
            }
        }

        void DrawAudioSourcePathField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.source_path"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).SourcePath != first.SourcePath);
                EditorGUI.BeginChangeCheck();
                string newPath = EditorGUILayout.TextField(first.SourcePath ?? "");
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Source Path", audio => audio.SourcePath = newPath);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPlaybackOrderField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.playback_order"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlaybackOrder != first.PlaybackOrder);
                EditorGUI.BeginChangeCheck();
                var orderLabels = new[] { L10n.Get("vrc.audio.order.random"), L10n.Get("vrc.audio.order.unique"), L10n.Get("vrc.audio.order.roundabout"), L10n.Get("vrc.audio.order.parameter") };
                var newOrder = (VRCAnimatorPlayAudio.Order)EditorGUILayout.Popup((int)first.PlaybackOrder, orderLabels);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Playback Order", audio => audio.PlaybackOrder = newOrder);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ClipsApplySettings != first.ClipsApplySettings);
                EditorGUI.BeginChangeCheck();
                var applyLabels = new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") };
                var newClipsApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.ClipsApplySettings, applyLabels, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Clips Apply Settings", audio => audio.ClipsApplySettings = newClipsApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioParameterNameField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.param_name"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ParameterName != first.ParameterName);
                EditorGUI.BeginChangeCheck();
                string newParam = DrawIntParamDropdown(first.ParameterName ?? "");
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Parameter Name", audio => audio.ParameterName = newParam);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioVolumeFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.volume"), GUILayout.Width(95));
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.min"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.x, first.Volume.x));
                EditorGUI.BeginChangeCheck();
                float newVolMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.x), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Min", audio => audio.Volume = new Vector2(newVolMin, audio.Volume.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.max"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.y, first.Volume.y));
                EditorGUI.BeginChangeCheck();
                float newVolMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.y), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Max", audio => audio.Volume = new Vector2(audio.Volume.x, newVolMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).VolumeApplySettings != first.VolumeApplySettings);
                EditorGUI.BeginChangeCheck();
                var newVolApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.VolumeApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Apply Settings", audio => audio.VolumeApplySettings = newVolApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPitchFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.pitch"), GUILayout.Width(95));
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.min"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.x, first.Pitch.x));
                EditorGUI.BeginChangeCheck();
                float newPitchMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.x), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Min", audio => audio.Pitch = new Vector2(newPitchMin, audio.Pitch.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField(L10n.Get("vrc.param_driver.max"), EditorStyles.label, GUILayout.Width(35));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.y, first.Pitch.y));
                EditorGUI.BeginChangeCheck();
                float newPitchMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.y), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Max", audio => audio.Pitch = new Vector2(audio.Pitch.x, newPitchMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PitchApplySettings != first.PitchApplySettings);
                EditorGUI.BeginChangeCheck();
                var newPitchApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.PitchApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Apply Settings", audio => audio.PitchApplySettings = newPitchApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioLoopField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.loop"), GUILayout.Width(55));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).Loop != first.Loop);
                EditorGUI.BeginChangeCheck();
                bool newLoop = EditorGUILayout.Toggle(first.Loop, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop", audio => audio.Loop = newLoop);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).LoopApplySettings != first.LoopApplySettings);
                EditorGUI.BeginChangeCheck();
                var newLoopApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.Popup((int)first.LoopApplySettings, new[] { L10n.Get("vrc.audio.apply.always"), L10n.Get("vrc.audio.apply.if_stopped"), L10n.Get("vrc.audio.apply.never") }, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop Apply Settings", audio => audio.LoopApplySettings = newLoopApply);
                EditorGUI.showMixedValue = false;
            }
        }

        static void DrawAudioPlayStopColumnHeaders()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label(L10n.Get("vrc.audio.stop"), EditorStyles.label, GUILayout.Width(40));
                GUILayout.Label(L10n.Get("vrc.audio.play"), EditorStyles.label, GUILayout.Width(40));
            }
        }

        void DrawAudioOnEnterFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.on_enter"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnEnter != first.StopOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newStopEnter = EditorGUILayout.Toggle(first.StopOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Enter", audio => audio.StopOnEnter = newStopEnter);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnEnter != first.PlayOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newPlayEnter = EditorGUILayout.Toggle(first.PlayOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Enter", audio => audio.PlayOnEnter = newPlayEnter);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioOnExitFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.on_exit"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnExit != first.StopOnExit);
                EditorGUI.BeginChangeCheck();
                bool newStopExit = EditorGUILayout.Toggle(first.StopOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Exit", audio => audio.StopOnExit = newStopExit);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnExit != first.PlayOnExit);
                EditorGUI.BeginChangeCheck();
                bool newPlayExit = EditorGUILayout.Toggle(first.PlayOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Exit", audio => audio.PlayOnExit = newPlayExit);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioDelayField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.audio.delay"), GUILayout.Width(220));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).DelayInSeconds, first.DelayInSeconds));
                EditorGUI.BeginChangeCheck();
                float newDelay = Mathf.Clamp(EditorGUILayout.FloatField(first.DelayInSeconds), 0f, 60f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play Delay", audio => audio.DelayInSeconds = newDelay);
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
                EditorGUI.DrawRect(outerRect, Styles.SecondaryColor);

            // Foldout header + size int field — now inside the background
            var headerRow = EditorGUILayout.GetControlRect(false, rowHeight);
            const float sizeWidth = 40f;
            var foldoutRect = new Rect(headerRow.x, headerRow.y, headerRow.width - sizeWidth - 4f, rowHeight);
            _clipsExpanded = EditorGUI.Foldout(foldoutRect, _clipsExpanded, L10n.Get("vrc.audio.clips"), true, EditorStyles.foldout);
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

                        if (GUI.Button(new Rect(rect.xMax - 24f, rect.y + 1f, 24f, rect.height - 2f), "−", Styles.CondBtn))
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
                    EditorGUILayout.LabelField(L10n.Get("vrc.list_empty"), Styles.EmptyLabel);
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
                    if (CursorBtn(new Rect(addRow.xMax - 24f, addRow.y, 24f, rowHeight), "+", Styles.CondBtn))
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

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.tracking"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateTracking(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemoveTrackingFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

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
                GUILayout.Label(L10n.Get("vrc.tracking.no_change"), EditorStyles.label, GUILayout.Width(70));
                GUILayout.Label(L10n.Get("vrc.tracking.tracking"),   EditorStyles.label, GUILayout.Width(70));
                GUILayout.Label(L10n.Get("vrc.tracking.animation"),  EditorStyles.label, GUILayout.Width(70));
            }

            // Set All row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("vrc.tracking.set_all"), GUILayout.Width(110));
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.NoChange,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Tracking,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Animation, 70f);
            }
            EditorGUILayout.Space(2f);

            DrawTrackingRow(L10n.Get("vrc.tracking.head"),          statesWithTracking, audio => audio.trackingHead,         (a, v) => a.trackingHead         = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_hand"),     statesWithTracking, audio => audio.trackingLeftHand,      (a, v) => a.trackingLeftHand     = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_hand"),    statesWithTracking, audio => audio.trackingRightHand,     (a, v) => a.trackingRightHand    = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.hip"),           statesWithTracking, audio => audio.trackingHip,           (a, v) => a.trackingHip          = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_foot"),     statesWithTracking, audio => audio.trackingLeftFoot,      (a, v) => a.trackingLeftFoot     = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_foot"),    statesWithTracking, audio => audio.trackingRightFoot,     (a, v) => a.trackingRightFoot    = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.left_fingers"),  statesWithTracking, audio => audio.trackingLeftFingers,   (a, v) => a.trackingLeftFingers  = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.right_fingers"), statesWithTracking, audio => audio.trackingRightFingers,  (a, v) => a.trackingRightFingers = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.eyes_eyelids"),  statesWithTracking, audio => audio.trackingEyes,          (a, v) => a.trackingEyes         = v);
            DrawTrackingRow(L10n.Get("vrc.tracking.mouth_jaw"),     statesWithTracking, audio => audio.trackingMouth,         (a, v) => a.trackingMouth        = v);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
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

        // ── VRC Locomotion Control section ────────────────────────────────────

        void DrawVRCLocomotionSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetLocomotionForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetLocomotionForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.locomotion"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateLocomotion(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemoveLocomotionFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLocomotionFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLocomotionFields()
        {
            var statesWithLocomotion = _selectedStates.Where(state => GetLocomotionForState(state) != null).ToArray();
            var first = GetLocomotionForState(statesWithLocomotion[0]);
            bool multi = statesWithLocomotion.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedDisable = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).disableLocomotion != first.disableLocomotion);
                EditorGUILayout.LabelField(L10n.Get("vrc.locomotion.label"), GUILayout.Width(110));
                DrawBoolToggleButtons(first.disableLocomotion, mixedDisable, L10n.Get("vrc.locomotion.disable"), L10n.Get("vrc.locomotion.enable"), 60f, isDisabled =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Locomotion Control");
                        locomotion.disableLocomotion = isDisabled;
                        EditorUtility.SetDirty(locomotion);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Debug String");
                        locomotion.debugString = newDebugString;
                        EditorUtility.SetDirty(locomotion);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorLocomotionControl GetLocomotionForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();

        static VRCAnimatorLocomotionControl GetOrCreateLocomotion(AnimatorState state)
        {
            var locomotion = state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();
            if (locomotion != null) return locomotion;
            locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
            Undo.RegisterCreatedObjectUndo(locomotion, "Create VRC Locomotion Control");
            EditorUtility.SetDirty(state);
            return locomotion;
        }

        void RemoveLocomotionFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var locomotion = GetLocomotionForState(state);
                if (locomotion == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Locomotion Control");
                state.behaviours = state.behaviours.Where(b => b != locomotion).ToArray();
                Undo.DestroyObjectImmediate(locomotion);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Animator Layer Control section ────────────────────────────────

        void DrawVRCLayerControlSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetLayerControlForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetLayerControlForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.layer_control"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateLayerControl(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemoveLayerControlFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLayerControlFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLayerControlFields()
        {
            var statesWithControl = _selectedStates.Where(state => GetLayerControlForState(state) != null).ToArray();
            var first = GetLayerControlForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer_control.playable"), L10n.Get("vrc.tooltip.playable_layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).playable != first.playable);
                EditorGUI.BeginChangeCheck();
                var newPlayable = (VRC_AnimatorLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.playable);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Playable");
                        control.playable = newPlayable;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.sub_layer_index")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                int newLayer = Mathf.Max(0, EditorGUILayout.IntField(first.layer));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration_layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorLayerControl GetLayerControlForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLayerControl>().FirstOrDefault();

        static VRCAnimatorLayerControl GetOrCreateLayerControl(AnimatorState state)
        {
            var control = state.behaviours.OfType<VRCAnimatorLayerControl>().FirstOrDefault();
            if (control != null) return control;
            control = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            Undo.RegisterCreatedObjectUndo(control, "Create VRC Animator Layer Control");
            EditorUtility.SetDirty(state);
            return control;
        }

        void RemoveLayerControlFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var control = GetLayerControlForState(state);
                if (control == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Animator Layer Control");
                state.behaviours = state.behaviours.Where(b => b != control).ToArray();
                Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Playable Layer Control section ────────────────────────────────

        void DrawVRCPlayableLayerSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetPlayableLayerForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetPlayableLayerForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.playable_layer"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreatePlayableLayer(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemovePlayableLayerFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayableLayerFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPlayableLayerFields()
        {
            var statesWithControl = _selectedStates.Where(state => GetPlayableLayerForState(state) != null).ToArray();
            var first = GetPlayableLayerForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.layer"), L10n.Get("vrc.tooltip.layer")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                var newLayer = (VRC_PlayableLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.layer);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.goal_weight"), L10n.Get("vrc.tooltip.goal_weight")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.blend_duration"), L10n.Get("vrc.tooltip.blend_duration")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCPlayableLayerControl GetPlayableLayerForState(AnimatorState state)
            => state.behaviours.OfType<VRCPlayableLayerControl>().FirstOrDefault();

        static VRCPlayableLayerControl GetOrCreatePlayableLayer(AnimatorState state)
        {
            var control = state.behaviours.OfType<VRCPlayableLayerControl>().FirstOrDefault();
            if (control != null) return control;
            control = state.AddStateMachineBehaviour<VRCPlayableLayerControl>();
            Undo.RegisterCreatedObjectUndo(control, "Create VRC Playable Layer Control");
            EditorUtility.SetDirty(state);
            return control;
        }

        void RemovePlayableLayerFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var control = GetPlayableLayerForState(state);
                if (control == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Playable Layer Control");
                state.behaviours = state.behaviours.Where(b => b != control).ToArray();
                Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Temporary Pose Space section ─────────────────────────────────

        void DrawVRCPoseSpaceSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetPoseSpaceForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetPoseSpaceForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.pose_space"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn(L10n.Get("vrc.add_to_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreatePoseSpace(state);
                if (anyHave && CursorBtn(L10n.Get("vrc.remove_all"), EditorStyles.miniButton, GUILayout.Width(125), GUILayout.Height(24)))
                {
                    RemovePoseSpaceFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPoseSpaceFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPoseSpaceFields()
        {
            var statesWithPoseSpace = _selectedStates.Where(state => GetPoseSpaceForState(state) != null).ToArray();
            var first = GetPoseSpaceForState(statesWithPoseSpace[0]);
            bool multi = statesWithPoseSpace.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedEnter = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).enterPoseSpace != first.enterPoseSpace);
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.pose_space.pose_space"), L10n.Get("vrc.tooltip.pose_space")), GUILayout.Width(110));
                DrawBoolToggleButtons(first.enterPoseSpace, mixedEnter, L10n.Get("vrc.pose_space.enter"), L10n.Get("vrc.pose_space.exit"), 60f, isEnter =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Pose Space");
                        poseSpace.enterPoseSpace = isEnter;
                        EditorUtility.SetDirty(poseSpace);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.pose_space.fixed_delay"), L10n.Get("vrc.tooltip.fixed_delay")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).fixedDelay != first.fixedDelay);
                EditorGUI.BeginChangeCheck();
                bool newFixedDelay = EditorGUILayout.Toggle(first.fixedDelay, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Fixed Delay");
                        poseSpace.fixedDelay = newFixedDelay;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(first.fixedDelay ? L10n.Get("vrc.pose_space.delay_time_s") : L10n.Get("vrc.pose_space.delay_time_pct"), L10n.Get("vrc.tooltip.delay_time")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => !Mathf.Approximately(GetPoseSpaceForState(state).delayTime, first.delayTime));
                EditorGUI.BeginChangeCheck();
                float newDelayTime = EditorGUILayout.FloatField(first.delayTime);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Delay Time");
                        poseSpace.delayTime = newDelayTime;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(L10n.Get("vrc.debug_string"), L10n.Get("vrc.tooltip.debug_string")), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Debug String");
                        poseSpace.debugString = newDebugString;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorTemporaryPoseSpace GetPoseSpaceForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();

        static VRCAnimatorTemporaryPoseSpace GetOrCreatePoseSpace(AnimatorState state)
        {
            var poseSpace = state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();
            if (poseSpace != null) return poseSpace;
            poseSpace = state.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();
            Undo.RegisterCreatedObjectUndo(poseSpace, "Create VRC Temporary Pose Space");
            EditorUtility.SetDirty(state);
            return poseSpace;
        }

        void RemovePoseSpaceFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var poseSpace = GetPoseSpaceForState(state);
                if (poseSpace == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Temporary Pose Space");
                state.behaviours = state.behaviours.Where(b => b != poseSpace).ToArray();
                Undo.DestroyObjectImmediate(poseSpace);
                EditorUtility.SetDirty(state);
            }
        }
    }
}
#endif
