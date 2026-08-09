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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Param Driver section (native, multi-instance reference implementation) ─

        readonly Dictionary<string, bool> _driverFoldoutExpanded = new Dictionary<string, bool>();

        Button _driverRemoveButton;
        VisualElement _driverRows;
        VisualElement _driverSection;

        VisualElement BuildDriverBody()
        {
            _driverSection = BuildBehaviorSectionShell(L10n.Get("vrc.param_driver"), out _driverRemoveButton, out _driverRows);
            _driverRemoveButton.clicked += () =>
            {
                RemoveDriverFromAll();
                RefreshDriverSection();
            };
            return _driverSection;
        }

        void RefreshDriverBody() => RefreshDriverSection();

        /* Entry point for the top-level Add Behavior dropdown — always available since drivers allow duplicates. */
        void AddDriverBehaviorToSelected()
        {
            var created = _selectedStates.ToDictionary(state => state, state => AddInstance<VRCAvatarParameterDriver>(state, "Driver"));
            Func<AnimatorState, VRCAvatarParameterDriver> resolver = state => created.TryGetValue(state, out var driver) ? driver : null;
            AddDriverParam(_selectedStates, resolver);
            RefreshDriverSection();
        }

        void RefreshDriverSection()
        {
            if (_driverRows == null) return;
            int maxCount = _selectedStates.Length == 0 ? 0 : _selectedStates.Max(state => InstanceCount<VRCAvatarParameterDriver>(state));
            _driverSection.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _driverRemoveButton.style.display = maxCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _driverRows.Clear();
            if (maxCount == 0) return;

            var driverGroups = GroupInstancesByName<VRCAvatarParameterDriver>(_selectedStates);
            for (int i = 0; i < driverGroups.Count; i++)
                _driverRows.Add(BuildDriverFoldout(driverGroups[i].name, driverGroups[i].states, i == 0, i == driverGroups.Count - 1,
                    i > 0 ? driverGroups[i - 1].name : null));
        }

        VisualElement BuildDriverFoldout(string name, AnimatorState[] statesWithName, bool isFirst, bool isLast, string aboveName)
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-behavior-instance-container");

            Func<AnimatorState, VRCAvatarParameterDriver> resolver = state => FindInstance<VRCAvatarParameterDriver>(state, name);

            var sharedParams = GetSharedDriverParams(statesWithName, resolver);
            var body = BuildDriverInstanceBody(statesWithName, resolver, sharedParams);
            body.style.display = IsExpandedByDefault(_driverFoldoutExpanded, name) ? DisplayStyle.Flex : DisplayStyle.None;

            bool canSwap = sharedParams.Any(entry => entry.param.type == VRC_AvatarParameterDriver.ChangeType.Copy);

            var header = BuildInstanceFoldoutHeader<VRCAvatarParameterDriver>(name, statesWithName, _driverFoldoutExpanded,
                isFirst, isLast, out _, expandedNow => body.style.display = expandedNow ? DisplayStyle.Flex : DisplayStyle.None,
                RefreshDriverSection,
                "⇄", canSwap, () => { SwapDriverCopySourceDest(statesWithName, resolver); RefreshDriverSection(); },
                "M", !isFirst, () => { MergeDriverWithAbove(name, statesWithName, aboveName); RefreshDriverSection(); });

            container.Add(header);
            container.Add(body);
            return container;
        }

        VisualElement BuildDriverInstanceBody(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, List<DriverParamEntry> initialSharedParams)
        {
            var wrapper = new VisualElement();

            var body = new VisualElement();
            body.AddToClassList("ygdr-behavior-instance-body");
            body.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            wrapper.Add(body);

            var statesWithDriver = statesWithName.Where(state => resolver(state) != null).ToArray();
            if (statesWithDriver.Length == 0) return wrapper;
            var first = resolver(statesWithDriver[0]);
            bool multi = statesWithDriver.Length > 1;

            // Debug string + local only row
            var debugRow = new VisualElement();
            debugRow.AddToClassList("ygdr-behavior-field-row");
            var debugLabel = new Label(L10n.Get("vrc.debug_string")) { tooltip = L10n.Get("vrc.tooltip.debug_string") };
            debugLabel.AddToClassList("ygdr-behavior-field-label");
            debugRow.Add(debugLabel);

            var debugField = new TextField { value = first.debugString ?? "", showMixedValue = multi && statesWithDriver.Any(state => resolver(state).debugString != first.debugString) };
            debugField.AddToClassList("ygdr-behavior-field-value");
            debugField.AddToClassList("u-flex-fill");
            debugField.AddToClassList("u-mr-4");
            debugField.RegisterValueChangedCallback(evt =>
                SetDriverOnAll(statesWithName, resolver, "Edit Debug String", driver => driver.debugString = evt.newValue));
            debugRow.Add(debugField);

            bool? localOnly = GetSharedLocalOnly(statesWithDriver, resolver);
            var localOnlyButton = new Button { text = L10n.Get("vrc.local_only") };
            localOnlyButton.style.color = localOnly == null ? Color.grey : localOnly.Value ? new Color(0.4f, 0.9f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
            localOnlyButton.clicked += () =>
            {
                bool newLocalOnly = localOnly != true;
                SetDriverOnAll(statesWithName, resolver, "Set Local Only", driver => driver.localOnly = newLocalOnly);
                localOnlyButton.style.color = newLocalOnly ? new Color(0.4f, 0.9f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
                localOnly = newLocalOnly;
            };
            debugRow.Add(localOnlyButton);
            body.Add(debugRow);

            var paramsListView = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = false,
                showAddRemoveFooter = false,
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = () => new VisualElement()
            };
            paramsListView.AddToClassList("ygdr-driver-params-rows");
            body.Add(paramsListView);

            var paramsEmptyLabel = new Label(L10n.Get("vrc.list_empty"));
            paramsEmptyLabel.AddToClassList("ygdr-empty-label");
            paramsEmptyLabel.style.display = DisplayStyle.None;
            body.Add(paramsEmptyLabel);

            /* Captured by both RebuildParamRows and itemIndexChanged below — itemIndexChanged needs the
               pre-move entry at oldIndex to resolve each state's own parameter position (see MoveDriverParam). */
            var sharedParams = new List<DriverParamEntry>();

            /* precomputed lets the initial call below reuse the list BuildDriverFoldout already walked for
               canSwap instead of walking driver.parameters a second time on every foldout build. */
            void RebuildParamRows(List<DriverParamEntry> precomputed = null)
            {
                sharedParams = precomputed ?? GetSharedDriverParams(statesWithName, resolver);

                paramsEmptyLabel.style.display = sharedParams.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

                var indices = new List<int>(sharedParams.Count);
                for (int i = 0; i < sharedParams.Count; i++) indices.Add(i);
                paramsListView.itemsSource = indices;
                paramsListView.bindItem = (element, index) => BindDriverParamRow(element, sharedParams[index], index, statesWithName, resolver, RefreshDriverSection);
                paramsListView.Rebuild();
            }

            WireListViewReorder(paramsListView, (oldIndex, newIndex) =>
            {
                if (oldIndex < 0 || oldIndex >= sharedParams.Count) return;
                var movedEntry = sharedParams[oldIndex];
                MoveDriverParam(statesWithName, resolver, movedEntry, newIndex);
                sharedParams.RemoveAt(oldIndex);
                sharedParams.Insert(newIndex, movedEntry);
            }, RefreshDriverSection);

            RebuildParamRows(initialSharedParams);

            /* "+" sits outside body's padded wrapper, flush against its bottom edge — mirrors Transitions' condAddRow. */
            var addRow = new VisualElement();
            addRow.AddToClassList("ygdr-driver-param-add-row");
            var addRowButton = new Button(() => { AddDriverParam(statesWithName, resolver); RefreshDriverSection(); }) { text = "+" };
            addRowButton.AddToClassList("ygdr-driver-param-add-btn");
            StyleSecondaryButton(addRowButton);
            addRow.Add(addRowButton);
            wrapper.Add(addRow);

            return wrapper;
        }

        static void SetDriverOnAll(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, string undoName, Action<VRCAvatarParameterDriver> mutate)
        {
            foreach (var state in statesWithName)
            {
                var driver = GetOrCreateDriver(state, resolver);
                Undo.RecordObject(driver, undoName);
                mutate(driver);
                EditorUtility.SetDirty(driver);
            }
        }

        static bool? GetSharedLocalOnly(AnimatorState[] statesWithDriver, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            if (statesWithDriver.Length == 0) return false;
            bool firstLocalOnly = resolver(statesWithDriver[0]).localOnly;
            return statesWithDriver.All(state => resolver(state).localOnly == firstLocalOnly) ? (bool?)firstLocalOnly : null;
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

        static List<DriverParamEntry> GetSharedDriverParams(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            var result = new List<DriverParamEntry>();
            var statesWithDriver = statesWithName.Where(state => resolver(state) != null).ToArray();
            if (statesWithDriver.Length == 0) return result;

            var firstDriver = resolver(statesWithDriver[0]);
            if (firstDriver.parameters.Count == 0) return result;

            for (int i = 0; i < firstDriver.parameters.Count; i++)
            {
                var param = firstDriver.parameters[i];
                bool sharedAcrossAll = statesWithDriver.All(state => resolver(state).parameters.Any(parameter => parameter.name == param.name));
                if (!sharedAcrossAll) continue;
                bool hasMixedTypes = !statesWithDriver.All(state =>
                {
                    foreach (var parameter in resolver(state).parameters)
                        if (parameter.name == param.name) return parameter.type == param.type;
                    return false;
                });
                bool hasMixedValues = hasMixedTypes || !statesWithDriver.All(state =>
                {
                    foreach (var parameter in resolver(state).parameters)
                        if (parameter.name == param.name) return DriverParamsMatch(parameter, param);
                    return false;
                });
                bool mixedSourceMin = false, mixedSourceMax = false, mixedDestMin = false, mixedDestMax = false;
                if (statesWithDriver.Length > 1 && param.type == VRC_AvatarParameterDriver.ChangeType.Copy && param.convertRange)
                {
                    foreach (var state in statesWithDriver)
                    {
                        var stateParam = resolver(state).parameters.FirstOrDefault(p => p.name == param.name);
                        if (stateParam == null) continue;
                        if (!Mathf.Approximately(stateParam.sourceMin, param.sourceMin)) mixedSourceMin = true;
                        if (!Mathf.Approximately(stateParam.sourceMax, param.sourceMax)) mixedSourceMax = true;
                        if (!Mathf.Approximately(stateParam.destMin,   param.destMin))   mixedDestMin   = true;
                        if (!Mathf.Approximately(stateParam.destMax,   param.destMax))   mixedDestMax   = true;
                    }
                }
                result.Add(new DriverParamEntry(param, i, hasMixedValues, hasMixedTypes, mixedSourceMin, mixedSourceMax, mixedDestMin, mixedDestMax));
            }
            return result;
        }

        /* Edits that change which rows are visible (type/dest/source/convertRange) call rebuild; plain value edits mutate in place. */
        void BindDriverParamRow(VisualElement element, DriverParamEntry entry, int index, AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, Action rebuild)
        {
            element.Clear();
            element.ClearClassList();
            element.AddToClassList("ygdr-driver-param-row");
            var param = entry.param;
            var idleColor = index % 2 == 0 ? SharedWindowStyles.SecondaryColor : SharedWindowStyles.RowAltColor;
            StyleHoverTint(element, () => false, () => SecondaryButtonHoverColor, () => idleColor);

            var paramType = GetParamType(param.name);
            bool isBool = paramType == AnimatorControllerParameterType.Bool;

            var changeTypes = isBool
                ? new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Random, VRC_AvatarParameterDriver.ChangeType.Copy }
                : new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Add, VRC_AvatarParameterDriver.ChangeType.Random, VRC_AvatarParameterDriver.ChangeType.Copy };
            var changeLabels = isBool
                ? new[] { L10n.Get("vrc.param_driver.set"), L10n.Get("vrc.param_driver.random"), L10n.Get("vrc.param_driver.copy") }
                : new[] { L10n.Get("vrc.param_driver.set"), L10n.Get("vrc.param_driver.add"), L10n.Get("vrc.param_driver.random"), L10n.Get("vrc.param_driver.copy") };

            var headerRow = new VisualElement();
            headerRow.AddToClassList("ygdr-behavior-field-row");

            var typeLabel = new Label(L10n.Get("vrc.param_driver.type"));
            typeLabel.AddToClassList("ygdr-behavior-field-label");
            headerRow.Add(typeLabel);

            int typeIndex = Mathf.Max(0, Array.IndexOf(changeTypes, param.type));
            var typeButton = BuildLocalizedIndexDropdown(typeIndex, entry.hasMixedTypes, changeLabels, newIndex =>
            {
                ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, type: changeTypes[newIndex]));
                rebuild();
            });
            typeButton.AddToClassList("ygdr-behavior-field-value");
            typeButton.AddToClassList("u-flex-fill");
            typeButton.AddToClassList("u-mr-4");
            headerRow.Add(typeButton);

            var removeButton = new Button(() => { RemoveDriverParam(statesWithName, resolver, entry); rebuild(); }) { text = "−" };
            removeButton.AddToClassList("ygdr-behavior-icon-btn");
            StyleSecondaryButton(removeButton);
            removeButton.style.backgroundColor = idleColor;
            removeButton.RegisterCallback<MouseLeaveEvent>(_ => removeButton.style.backgroundColor = idleColor);
            headerRow.Add(removeButton);
            element.Add(headerRow);

            if (param.type == VRC_AvatarParameterDriver.ChangeType.Copy)
            {
                var sourceButton = new Button { text = string.IsNullOrEmpty(param.source) ? "—" : param.source };
                StyleAccentButton(sourceButton);
                sourceButton.clicked += () =>
                {
                    if (_controller == null || _controller.parameters.Length == 0) return;
                    ShowParameterDropdown(sourceButton.worldBound, param.source ?? "", selectedName =>
                    {
                        ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, source: selectedName));
                        rebuild();
                    });
                };
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.source"), null, sourceButton));

                var destButton = new Button { text = string.IsNullOrEmpty(param.name) ? "—" : param.name };
                StyleAccentButton(destButton);
                destButton.clicked += () =>
                {
                    if (_controller == null || _controller.parameters.Length == 0) return;
                    ShowParameterDropdown(destButton.worldBound, param.name ?? "", selectedName =>
                    {
                        ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, name: selectedName));
                        rebuild();
                    });
                };
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.destination"), null, destButton));

                if (!string.IsNullOrEmpty(param.source) && GetParamType(param.source) != GetParamType(param.name))
                {
                    var hint = new Label($"Value will be converted to {GetParamType(param.name)}");
                    hint.AddToClassList("ygdr-driver-hint");
                    element.Add(hint);
                }

                var convertToggle = new Toggle { value = param.convertRange };
                convertToggle.RegisterValueChangedCallback(evt =>
                {
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, convertRange: evt.newValue));
                    rebuild();
                });
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.convert_range"), null, convertToggle));

                if (param.convertRange)
                {
                    element.Add(BuildDriverMinMaxRow(L10n.Get("vrc.param_driver.source"), param.sourceMin, param.sourceMax, entry.mixedSourceMin, entry.mixedSourceMax,
                        newMin => ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, sourceMin: newMin)),
                        newMax => ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, sourceMax: newMax))));
                    element.Add(BuildDriverMinMaxRow(L10n.Get("vrc.param_driver.destination"), param.destMin, param.destMax, entry.mixedDestMin, entry.mixedDestMax,
                        newMin => ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, destMin: newMin)),
                        newMax => ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, destMax: newMax))));
                }
                return;
            }

            var nameButton = new Button { text = string.IsNullOrEmpty(param.name) ? "—" : param.name };
            StyleAccentButton(nameButton);
            nameButton.clicked += () =>
            {
                if (_controller == null || _controller.parameters.Length == 0) return;
                ShowParameterDropdown(nameButton.worldBound, param.name ?? "", selectedName =>
                {
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, name: selectedName));
                    rebuild();
                });
            };
            element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.destination"), null, nameButton));

            if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Set)
            {
                var toggle = new Toggle { value = param.value >= 0.5f, showMixedValue = entry.hasMixedValues };
                toggle.RegisterValueChangedCallback(evt =>
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, value: evt.newValue ? 1f : 0f)));
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.value"), null, toggle));
            }
            else if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                var slider = new Slider(0f, 1f) { value = param.chance, showMixedValue = entry.hasMixedValues };
                slider.RegisterValueChangedCallback(evt =>
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, chance: evt.newValue)));
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.chance"), null, slider));
            }
            else if (param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                var minField = new FloatField { value = param.valueMin, showMixedValue = entry.hasMixedValues };
                minField.RegisterValueChangedCallback(evt =>
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, valueMin: evt.newValue)));
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.min_value"), null, minField));

                var maxField = new FloatField { value = param.valueMax, showMixedValue = entry.hasMixedValues };
                maxField.RegisterValueChangedCallback(evt =>
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, valueMax: evt.newValue)));
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.max_value"), null, maxField));

                if (paramType == AnimatorControllerParameterType.Int)
                {
                    var preventToggle = new Toggle { value = param.preventRepeats };
                    preventToggle.RegisterValueChangedCallback(evt =>
                        ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, preventRepeats: evt.newValue)));
                    element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.prevent_repeats"), null, preventToggle));
                }
                else
                {
                    var chanceSlider = new Slider(0f, 1f) { value = param.chance, showMixedValue = entry.hasMixedValues };
                    chanceSlider.RegisterValueChangedCallback(evt =>
                        ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, chance: evt.newValue)));
                    element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.chance"), null, chanceSlider));
                }
            }
            else
            {
                var valueField = new FloatField { value = param.value, showMixedValue = entry.hasMixedValues };
                valueField.RegisterValueChangedCallback(evt =>
                    ReplaceDriverParam(statesWithName, resolver, entry, CloneParam(entry.param, value: evt.newValue)));
                element.Add(BuildBehaviorFieldRow(L10n.Get("vrc.param_driver.value"), null, valueField));
            }
        }

        static VisualElement BuildDriverMinMaxRow(string label, float min, float max, bool mixedMin, bool mixedMax, Action<float> onMinChanged, Action<float> onMaxChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-behavior-field-row");
            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-behavior-field-label");
            row.Add(labelElement);

            var minLabel = new Label(L10n.Get("vrc.param_driver.min"));
            minLabel.AddToClassList("ygdr-driver-minmax-label");
            row.Add(minLabel);

            var minField = new FloatField { value = min, showMixedValue = mixedMin };
            minField.AddToClassList("ygdr-driver-minmax-field");
            minField.RegisterValueChangedCallback(evt => onMinChanged(evt.newValue));
            row.Add(minField);

            var maxLabel = new Label(L10n.Get("vrc.param_driver.max"));
            maxLabel.AddToClassList("ygdr-driver-minmax-label");
            row.Add(maxLabel);

            var maxField = new FloatField { value = max, showMixedValue = mixedMax };
            maxField.AddToClassList("ygdr-driver-minmax-field");
            maxField.RegisterValueChangedCallback(evt => onMaxChanged(evt.newValue));
            row.Add(maxField);

            return row;
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

        /* Swaps source/dest (param.source <-> param.name) on every Copy-mode row shared across this instance. */
        static void SwapDriverCopySourceDest(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            foreach (var entry in GetSharedDriverParams(statesWithName, resolver))
            {
                if (entry.param.type != VRC_AvatarParameterDriver.ChangeType.Copy) continue;
                var swapped = CloneParam(entry.param, name: entry.param.source, source: entry.param.name);
                ReplaceDriverParam(statesWithName, resolver, entry, swapped);
            }
        }

        /* States missing either side of the pair are skipped. */
        static void MergeDriverWithAbove(string name, AnimatorState[] statesWithName, string aboveName)
        {
            if (aboveName == null) return;

            foreach (var state in statesWithName)
            {
                var current = FindInstance<VRCAvatarParameterDriver>(state, name);
                var above = FindInstance<VRCAvatarParameterDriver>(state, aboveName);
                if (current == null || above == null) continue;

                Undo.RecordObject(above, "Merge Param Drivers");
                above.parameters.AddRange(current.parameters);
                EditorUtility.SetDirty(above);

                Undo.RegisterCompleteObjectUndo(state, "Merge Param Drivers");
                state.behaviours = state.behaviours.Where(b => b != current).ToArray();
                Undo.DestroyObjectImmediate(current);
                EditorUtility.SetDirty(state);
            }
        }

        static void ReplaceDriverParam(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, DriverParamEntry entry, VRC_AvatarParameterDriver.Parameter replacement)
        {
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
                if (driver == null) continue;

                int parameterIndex = FindDriverParamIndex(driver, entry.param, entry.index);
                if (parameterIndex < 0) continue;

                Undo.RecordObject(driver, "Edit Driver Parameter");
                driver.parameters[parameterIndex] = MergeParam(driver.parameters[parameterIndex], entry.param, replacement);
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
            destMin        = replacement.destMin        != original.destMin        ? replacement.destMin        : existing.destMin,
            destMax        = replacement.destMax        != original.destMax        ? replacement.destMax        : existing.destMax,
            preventRepeats = replacement.preventRepeats != original.preventRepeats ? replacement.preventRepeats : existing.preventRepeats,
        };

        /* Removes entry's parameter from every driver in statesWithName, destroying the driver instance entirely if its list becomes empty. */
        static void RemoveDriverParam(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, DriverParamEntry entry)
        {
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
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

        /* newIndex comes from the dragged row's drop position; each state resolves its own oldIndex via entry
           identity (name + index hint) since per-state parameter lists can be offset when rows are filtered
           to only shared params — same lookup RemoveDriverParam/ReplaceDriverParam already rely on. */
        static void MoveDriverParam(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver, DriverParamEntry entry, int newIndex)
        {
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
                if (driver == null) continue;
                int oldIndex = FindDriverParamIndex(driver, entry.param, entry.index);
                if (oldIndex < 0) continue;

                int clampedNewIndex = Mathf.Clamp(newIndex, 0, driver.parameters.Count - 1);
                if (clampedNewIndex == oldIndex) continue;

                Undo.RecordObject(driver, "Reorder Driver Parameters");
                var moved = driver.parameters[oldIndex];
                driver.parameters.RemoveAt(oldIndex);
                driver.parameters.Insert(clampedNewIndex, moved);
                EditorUtility.SetDirty(driver);
            }
        }

        void AddDriverParam(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            if (statesWithName.Length > 1) EnsureUniqueDrivers(statesWithName, resolver);
            string defaultName = string.Empty;
            if (_controller != null && _controller.parameters.Length > 0)
            {
                var defaultParam = _controller.parameters[0];
                var firstDriver = GetOrCreateDriver(statesWithName[0], resolver);
                var lastParam = firstDriver.parameters.LastOrDefault();
                var previousIndex = lastParam != null ? Array.FindIndex(_controller.parameters, p => p.name == lastParam.name) : -1;
                if (previousIndex >= 0)
                    defaultParam = _controller.parameters[(previousIndex + 1) % _controller.parameters.Length];
                defaultName = defaultParam.name;
            }
            foreach (var state in statesWithName)
            {
                var driver = GetOrCreateDriver(state, resolver);
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

        /* Handles Unity state duplication sharing C++ behaviours arrays: destroys all drivers, SaveAssets to
           reimport with independent arrays, then recreates and restores data. Known limitation: recreated
           drivers don't restore their original instance name, desyncing foldout grouping if triggered mid-edit. */
        static void EnsureUniqueDrivers(AnimatorState[] statesWithName, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            var seenIds = new HashSet<int>();
            bool needsRebuild = false;
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
                if (driver == null || !seenIds.Add(driver.GetInstanceID()))
                    needsRebuild = true;
            }
            if (!needsRebuild) return;

            var savedParameters  = new Dictionary<AnimatorState, List<VRC_AvatarParameterDriver.Parameter>>();
            var savedLocalOnly   = new Dictionary<AnimatorState, bool>();
            var savedDebugString = new Dictionary<AnimatorState, string>();
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
                if (driver == null) continue;
                savedParameters[state]  = new List<VRC_AvatarParameterDriver.Parameter>(driver.parameters);
                savedLocalOnly[state]   = driver.localOnly;
                savedDebugString[state] = driver.debugString ?? string.Empty;
            }

            var destroyedIds = new HashSet<int>();
            foreach (var state in statesWithName)
            {
                var driver = resolver(state);
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

            foreach (var state in statesWithName)
            {
                var driver = GetOrCreateDriver(state, resolver);
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
            RemoveAllInstancesOfType<VRCAvatarParameterDriver>(_selectedStates, "Remove VRC Drivers");
            _driverFoldoutExpanded.Clear();
        }

        /* indexHint (positional match) tried first to handle duplicate-name params correctly, then falls back to first name match. */
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

        /* Returns the resolver-scoped existing driver, or the first driver, or adds and registers a new one via Undo. */
        static VRCAvatarParameterDriver GetOrCreateDriver(AnimatorState state, Func<AnimatorState, VRCAvatarParameterDriver> resolver)
        {
            var resolved = resolver(state);
            if (resolved != null) return resolved;
            var existing = InstanceAt<VRCAvatarParameterDriver>(state, 0);
            if (existing != null) return existing;
            return AddInstance<VRCAvatarParameterDriver>(state, "Driver");
        }
    }
}
#endif
