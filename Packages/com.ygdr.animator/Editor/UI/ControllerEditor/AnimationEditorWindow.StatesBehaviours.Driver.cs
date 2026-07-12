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
        // ── VRC Drivers section (multi-instance reference implementation) ──────

        // Per-instance ReorderableList caches, keyed by instance name.
        readonly Dictionary<string, ReorderableList> _driverParamReorderListByKey = new Dictionary<string, ReorderableList>();
        readonly Dictionary<string, List<VRC_AvatarParameterDriver.Parameter>> _driverParamListDataByKey = new Dictionary<string, List<VRC_AvatarParameterDriver.Parameter>>();
        readonly Dictionary<string, float[]> _stableElementHeightsByKey = new Dictionary<string, float[]>();
        readonly Dictionary<string, bool> _driverFoldoutExpanded = new Dictionary<string, bool>();
        (string key, int index) _pendingRemoveDriverParam = (null, -1);
        string _currentDriverBodyKey;

        // Scopes GetDriverForState/GetOrCreateDriver/param-editing helpers to one instance's states for the current draw call.
        AnimatorState[] _activeDriverStates;
        Func<AnimatorState, VRCAvatarParameterDriver> _activeDriverResolver;

        void DrawVRCDriversSection()
        {
            _activeDriverResolver = null;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAvatarParameterDriver>(state));

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label(L10n.Get("vrc.param_driver"), Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (CursorBtn(L10n.Get("vrc.add_to_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    var created = _selectedStates.ToDictionary(state => state, state => AddInstance<VRCAvatarParameterDriver>(state, "Driver"));
                    _activeDriverStates = _selectedStates;
                    _activeDriverResolver = state => created.TryGetValue(state, out var driver) ? driver : null;
                    AddDriverParam();
                    _activeDriverResolver = null;
                    return; // maxCount below is stale after this mutation — redraw fresh next repaint.
                }
                if (maxCount > 0 && CursorBtn(L10n.Get("vrc.remove_all"), Styles.BehaviorHeaderBtn, GUILayout.Width(125)))
                {
                    RemoveDriverFromAll();
                    return;
                }
            }

            if (maxCount == 0) return;

            var driverGroups = GroupInstancesByName<VRCAvatarParameterDriver>(_selectedStates);
            for (int i = 0; i < driverGroups.Count; i++)
                DrawDriverFoldout(driverGroups[i].name, driverGroups[i].states, i == 0, i == driverGroups.Count - 1);

            // Don't leak the last-drawn instance's scope into calls made outside this section (e.g. States.cs spacing checks).
            _activeDriverResolver = null;
        }

        void DrawDriverFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast)
        {
            bool removeRequested = DrawInstanceFoldoutHeader<VRCAvatarParameterDriver>(name, statesWithName, _driverFoldoutExpanded, isFirst, isLast, out bool expanded, out bool moveUp, out bool moveDown);

            if (moveUp || moveDown)
            {
                MoveNamedInstance<VRCAvatarParameterDriver>(name, statesWithName, moveUp ? -1 : 1);
                return; // order changed — redraw fresh next repaint.
            }

            if (removeRequested)
            {
                RemoveNamedInstance<VRCAvatarParameterDriver>(name, statesWithName);
                _driverParamReorderListByKey.Remove(name);
                _driverParamListDataByKey.Remove(name);
                _stableElementHeightsByKey.Remove(name);
                return;
            }

            if (!expanded) return;

            _activeDriverStates = statesWithName;
            _activeDriverResolver = state => FindInstance<VRCAvatarParameterDriver>(state, name);
            DrawDriverInstanceBody(name);
        }

        /* Draws debug string/local-only row + shared driver-parameter list for the instance scoped by
           _activeDriverStates/_activeDriverResolver. Caller must set both before invoking. */
        void DrawDriverInstanceBody(string key)
        {
            _currentDriverBodyKey = key;
            var bodyRect = EditorGUILayout.BeginVertical(Styles.CondBody, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            // Debug String + Local Only row
            using (new EditorGUILayout.HorizontalScope())
            {
                var drivers = _activeDriverStates.Select(state => GetDriverForState(state)).Where(driver => driver != null).ToArray();
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
                        foreach (var state in _activeDriverStates)
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
                    if (!_driverParamListDataByKey.TryGetValue(key, out var listData) || listData.Count != sharedParams.Count)
                    {
                        listData = new List<VRC_AvatarParameterDriver.Parameter>(sharedParams.Select(entry => entry.param));
                        _driverParamListDataByKey[key] = listData;
                        _driverParamReorderListByKey.Remove(key);
                    }
                    else
                        for (int i = 0; i < sharedParams.Count; i++)
                            listData[i] = sharedParams[i].param;
                    _stableElementHeightsByKey[key] = sharedParams.Select(entry => ComputeDriverParamHeight(entry.param)).ToArray();
                }

                if (!_driverParamReorderListByKey.TryGetValue(key, out var reorderList))
                {
                    var listData = _driverParamListDataByKey[key];
                    reorderList = new ReorderableList(listData, typeof(VRC_AvatarParameterDriver.Parameter), true, false, false, false)
                    {
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };
                    reorderList.elementHeightCallback = index =>
                        _stableElementHeightsByKey.TryGetValue(key, out var heights) && index < heights.Length
                            ? heights[index]
                            : EditorGUIUtility.singleLineHeight;

                    reorderList.drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (Event.current.type == EventType.Repaint)
                            EditorGUI.DrawRect(rect, index % 2 == 0 ? Styles.SecondaryColor : Styles.RowAltColor);
                    };

                    reorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        var currentListData = _driverParamListDataByKey[key];
                        if (index >= currentListData.Count) return;
                        var param = currentListData[index];
                        var localStates = _activeDriverStates.Where(state => GetDriverForState(state) != null).ToArray();
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

                    reorderList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                    {
                        foreach (var state in _activeDriverStates)
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

                    _driverParamReorderListByKey[key] = reorderList;
                }

                reorderList.DoLayoutList();
            }

            if (_pendingRemoveDriverParam.index >= 0 && _pendingRemoveDriverParam.key == key)
            {
                var capturedEntries = GetSharedDriverParams();
                if (_pendingRemoveDriverParam.index < capturedEntries.Count)
                    RemoveDriverParam(capturedEntries[_pendingRemoveDriverParam.index]);
                _pendingRemoveDriverParam = (null, -1);
                _driverParamReorderListByKey.Remove(key);
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            float addBtnSize = EditorGUIUtility.singleLineHeight;
            var addRow = EditorGUILayout.GetControlRect(false, addBtnSize);
            if (CursorBtn(new Rect(addRow.xMax - 40f, addRow.y, 24f, addBtnSize), "+", Styles.CondBtn))
            {
                AddDriverParam();
                _driverParamReorderListByKey.Remove(key);
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
                foreach (var state in _activeDriverStates)
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
            if (_activeDriverStates.Length == 0) return false;
            var drivers = _activeDriverStates
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
            if (_activeDriverStates.Length == 0) return result;

            var firstDriver = GetDriverForState(_activeDriverStates[0]);
            if (firstDriver == null || firstDriver.parameters.Count == 0) return result;

            for (int i = 0; i < firstDriver.parameters.Count; i++)
            {
                var param = firstDriver.parameters[i];
                bool sharedAcrossAll = _activeDriverStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null && driver.parameters.Any(parameter => parameter.name == param.name);
                });
                if (!sharedAcrossAll) continue;
                bool hasMixedTypes = !_activeDriverStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    if (driver == null) return false;
                    foreach (var parameter in driver.parameters)
                        if (parameter.name == param.name) return parameter.type == param.type;
                    return false;
                });
                bool hasMixedValues = hasMixedTypes || !_activeDriverStates.All(state =>
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
                    RequestRemoveDriverParam(entry.index);
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
                RequestRemoveDriverParam(entry.index);
            GUI.backgroundColor = previousBackgroundColor;

            // Row 2: Parameter label | parameter dropdown
            float paramRowY  = row.y + singleLine;
            float paramDropWidth = row.width - nonCopyLabelWidth - removeWidth;
            GUI.Label(new Rect(row.x, paramRowY, nonCopyLabelWidth, singleLine), L10n.Get("vrc.param_driver.destination"), EditorStyles.label);
            var nameRect = new Rect(row.x + nonCopyLabelWidth, paramRowY, paramDropWidth, singleLine);
            if (EditorGUI.DropdownButton(nameRect, new GUIContent(string.IsNullOrEmpty(param.name) ? "—" : param.name), FocusType.Passive))
            {
                var driverSnapshot = CaptureActiveDriverSnapshot();
                var nameMenu = new GenericMenu();
                foreach (var controllerParameter in _controller.parameters)
                {
                    var capturedName = controllerParameter.name;
                    nameMenu.AddItem(new GUIContent(capturedName), capturedName == param.name, () =>
                        ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, name: capturedName), driverSnapshot));
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

        void RequestRemoveDriverParam(int index) => _pendingRemoveDriverParam = (_currentDriverBodyKey, index);

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
            var driverSnapshot = CaptureActiveDriverSnapshot();
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
                    ReplaceDriverParam(entry, updated, driverSnapshot);
                });
            }
            menu.ShowAsContext();
        }

        /* GenericMenu item callbacks fire on a later editor update, not inside the OnGUI call that built
           the menu — by then _activeDriverStates/_activeDriverResolver have moved on to whatever foldout
           was drawn last (or been reset to null), so a live GetDriverForState re-resolve silently falls
           back to instance 0. Call this at menu-build time (synchronous) and pass the snapshot through to
           the deferred callback instead of letting it re-resolve. */
        (AnimatorState state, VRCAvatarParameterDriver driver)[] CaptureActiveDriverSnapshot()
            => _activeDriverStates.Select(state => (state, GetDriverForState(state))).ToArray();

        void ReplaceDriverParam(
            DriverParamEntry entry,
            VRC_AvatarParameterDriver.Parameter replacement,
            (AnimatorState state, VRCAvatarParameterDriver driver)[] driverSnapshot = null)
        {
            foreach (var (state, driver) in driverSnapshot ?? CaptureActiveDriverSnapshot())
            {
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

        /* Removes entry's parameter from every driver in _activeDriverStates, destroying the driver instance entirely if its list becomes empty. */
        void RemoveDriverParam(DriverParamEntry entry)
        {
            foreach (var state in _activeDriverStates)
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
            if (_activeDriverStates.Length > 1) EnsureUniqueDrivers();
            string defaultName = string.Empty;
            if (_controller != null && _controller.parameters.Length > 0)
            {
                var defaultParam = _controller.parameters[0];
                var usedNames = new HashSet<string>(_activeDriverStates.SelectMany(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null ? driver.parameters.Select(parameter => parameter.name) : Enumerable.Empty<string>();
                }));
                var unusedParam = _controller.parameters.FirstOrDefault(parameter => !usedNames.Contains(parameter.name));
                if (unusedParam != null) defaultParam = unusedParam;
                defaultName = defaultParam.name;
            }
            foreach (var state in _activeDriverStates)
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

        /* Detects shared VRCAvatarParameterDriver instances across _activeDriverStates (caused by Unity
           state duplication sharing C++ behaviours arrays). Breaks sharing by destroying all drivers,
           calling SaveAssets to write independent empty behaviours to disk (reimport separates the
           C++ arrays), then recreating unique drivers and restoring the saved parameter data.
           Known limitation (tracked for Phase 1 step 5 / EnsureUniqueInstances<T>): recreated drivers
           don't restore their original instance name, so this can desync foldout grouping if triggered
           while editing a non-first named instance. */
        void EnsureUniqueDrivers()
        {
            var seenIds = new HashSet<int>();
            bool needsRebuild = false;
            foreach (var state in _activeDriverStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null || !seenIds.Add(driver.GetInstanceID()))
                    needsRebuild = true;
            }
            if (!needsRebuild) return;

            var savedParameters  = new Dictionary<AnimatorState, List<VRC_AvatarParameterDriver.Parameter>>();
            var savedLocalOnly   = new Dictionary<AnimatorState, bool>();
            var savedDebugString = new Dictionary<AnimatorState, string>();
            foreach (var state in _activeDriverStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                savedParameters[state]  = new List<VRC_AvatarParameterDriver.Parameter>(driver.parameters);
                savedLocalOnly[state]   = driver.localOnly;
                savedDebugString[state] = driver.debugString ?? string.Empty;
            }

            var destroyedIds = new HashSet<int>();
            foreach (var state in _activeDriverStates)
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

            foreach (var state in _activeDriverStates)
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

        /* Rescoped Remove All: destroys every driver instance (all names) on every selected state. */
        void RemoveDriverFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var drivers = Instances<VRCAvatarParameterDriver>(state);
                if (drivers.Count == 0) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Drivers");
                state.behaviours = state.behaviours.Where(b => !(b is VRCAvatarParameterDriver)).ToArray();
                foreach (var driver in drivers) Undo.DestroyObjectImmediate(driver);
                EditorUtility.SetDirty(state);
            }
            _driverParamReorderListByKey.Clear();
            _driverParamListDataByKey.Clear();
            _stableElementHeightsByKey.Clear();
            _driverFoldoutExpanded.Clear();
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

        VRCAvatarParameterDriver GetDriverForState(AnimatorState state)
            => _activeDriverResolver != null ? _activeDriverResolver(state) : InstanceAt<VRCAvatarParameterDriver>(state, 0);

        /* Non-generic helper so callers outside the VRC_SDK_VRCSDK3 guard block (e.g. States.cs, which has
           no VRC using directive) can check driver presence without naming VRCAvatarParameterDriver. */
        static bool HasAnyDriver(AnimatorState state) => HasInstance<VRCAvatarParameterDriver>(state);

        /* Returns the resolver-scoped existing driver, or the first driver, or adds and registers a new one via Undo. */
        VRCAvatarParameterDriver GetOrCreateDriver(AnimatorState state)
        {
            if (_activeDriverResolver != null)
            {
                var resolved = _activeDriverResolver(state);
                if (resolved != null) return resolved;
            }
            var existing = InstanceAt<VRCAvatarParameterDriver>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCAvatarParameterDriver>(state, "Driver");
        }

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
    }
}
#endif
