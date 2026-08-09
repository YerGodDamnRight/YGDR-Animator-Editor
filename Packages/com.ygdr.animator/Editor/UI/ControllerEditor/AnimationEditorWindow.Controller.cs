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
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        enum WDState { On, Off, Mixed }

        int _controllerSubTab;
        readonly List<Action> _controllerRelabelActions = new List<Action>();

        string ControllerSectionCountLabel
        {
            get
            {
                if (_controller == null) return null;
                if (_controllerSubTab == 0) return L10n.Get("controller.count.layers").Replace("{n}", _controller.layers.Length.ToString());
                if (_controllerSubTab == 2 && _subAssetsByType != null)
                    return _subAssetTypeFilter switch
                    {
                        0 => L10n.Get("controller.count.state_machines").Replace("{n}", (_subAssetsByType[0]?.Length ?? 0).ToString()),
                        1 => L10n.Get("controller.count.states").Replace("{n}", (_subAssetsByType[1]?.Length ?? 0).ToString()),
                        2 => L10n.Get("controller.count.blend_trees").Replace("{n}", (_subAssetsByType[2]?.Length ?? 0).ToString()),
                        3 => L10n.Get("controller.count.clips").Replace("{n}", (_subAssetsByType[3]?.Length ?? 0).ToString()),
                        _ => null
                    };
                return null;
            }
        }

        /* ── Controller tab (native shell + Write Defaults; Network Sync/Sub-Assets/Menus still IMGUI-bridged) ── */

        VisualElement _controllerPanel;
        VisualElement _controllerSubTabStrip;
        Button _wdTabButton, _networkTabButton, _subAssetsTabButton, _menusTabButton;
        Button _controllerCleanButton;
        VisualElement _wdBody;
        VisualElement _networkSyncBody;
        VisualElement _subAssetsBody;
        VisualElement _menusBody;

        VisualElement BuildControllerBody()
        {
            _controllerPanel = new VisualElement();
            _controllerPanel.AddToClassList("ygdr-controller-panel");

            _controllerPanel.Add(BuildControllerSubTabStrip());

            _wdBody = BuildWriteDefaultsBody();
            _controllerPanel.Add(_wdBody);

            _networkSyncBody = BuildNetworkSyncBody();
            _controllerPanel.Add(_networkSyncBody);

            _subAssetsBody = BuildSubAssetsBody();
            _controllerPanel.Add(_subAssetsBody);

            _menusBody = BuildMenusBody();
            _controllerPanel.Add(_menusBody);

            return _controllerPanel;
        }

        VisualElement BuildControllerSubTabStrip()
        {
            _controllerSubTabStrip = new VisualElement();
            _controllerSubTabStrip.AddToClassList("ygdr-tab-strip");
            _controllerSubTabStrip.AddToClassList("ygdr-controller-subtab-strip");

            _wdTabButton        = BuildControllerSubTabButton(L10n.Get("controller.subtab.wd"), 0);
            _networkTabButton   = BuildControllerSubTabButton(L10n.Get("controller.subtab.network_sync"), 1);
            _subAssetsTabButton = BuildControllerSubTabButton(L10n.Get("controller.subtab.sub_assets"), 2);
            _menusTabButton     = BuildControllerSubTabButton(L10n.Get("controller.subtab.menus"), 3);

            _controllerCleanButton = new Button(() =>
            {
                if ((_orphanedAssets?.Length ?? 0) > 0) CleanOrphanedAssets();
                RefreshControllerCleanButton();
            });
            _controllerCleanButton.AddToClassList("ygdr-tab-strip-button");
            StyleAccentButton(_controllerCleanButton);
            _controllerSubTabStrip.Add(_controllerCleanButton);

            return _controllerSubTabStrip;
        }

        Button BuildControllerSubTabButton(string label, int index)
        {
            var button = new Button(() =>
            {
                int previousSubTab = _controllerSubTab;
                _controllerSubTab = index;
                RefreshControllerSubTabVisibility(previousSubTab);
            })
            { text = label };
            button.AddToClassList("ygdr-tab-strip-button");
            // Hover callbacks bound once here (not per-refresh) since RefreshControllerSubTabVisibility/
            // RefreshControllerPaletteColors re-apply the tint on every sub-tab switch and palette change.
            StyleHoverTint(button, () => _controllerSubTab == index, () => AccentHoverColor, () => SharedWindowStyles.AccentColor);
            _controllerSubTabStrip.Add(button);
            return button;
        }

        /* animateFromIndex = sub-tab switching away from (-1 = snap, no animation). */
        void RefreshControllerSubTabVisibility(int animateFromIndex = -1)
        {
            _wdTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _controllerSubTab == 0);
            _networkTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _controllerSubTab == 1);
            _subAssetsTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _controllerSubTab == 2);
            _menusTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _controllerSubTab == 3);

            if (animateFromIndex >= 0 && animateFromIndex != _controllerSubTab)
            {
                Button[] subTabButtons = { _wdTabButton, _networkTabButton, _subAssetsTabButton, _menusTabButton };
                for (int i = 0; i < subTabButtons.Length; i++)
                {
                    if (i == animateFromIndex) AnimateSubTabHighlight(subTabButtons[i], false);
                    else if (i == _controllerSubTab) AnimateSubTabHighlight(subTabButtons[i], true);
                    else ApplySubTabAccent(subTabButtons[i], false);
                }
            }
            else
            {
                ApplySubTabAccent(_wdTabButton, _controllerSubTab == 0);
                ApplySubTabAccent(_networkTabButton, _controllerSubTab == 1);
                ApplySubTabAccent(_subAssetsTabButton, _controllerSubTab == 2);
                ApplySubTabAccent(_menusTabButton, _controllerSubTab == 3);
            }

            if (_wdBody != null) _wdBody.style.display = _controllerSubTab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_networkSyncBody != null) _networkSyncBody.style.display = _controllerSubTab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_subAssetsBody != null) _subAssetsBody.style.display = _controllerSubTab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_menusBody != null) _menusBody.style.display = _controllerSubTab == 3 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_controllerSubTab == 0) RefreshWriteDefaultsBody();
            else if (_controllerSubTab == 1) RefreshNetworkSyncBody();
            else if (_controllerSubTab == 2) RefreshSubAssetsBody();
            else if (_controllerSubTab == 3) RefreshMenusBody();

            if (_subAssetsByType == null || _subAssetCachedController != _controller)
                RebuildSubAssetCache();
            RefreshControllerCleanButton();
            if (_controllerRightLabel != null) _controllerRightLabel.text = ControllerSectionCountLabel ?? string.Empty;
        }

        static void ApplySubTabAccent(Button button, bool active)
        {
            if (button == null) return;
            button.style.backgroundColor = active ? AccentHoverColor : SharedWindowStyles.AccentColor;
        }

        /* Drops to the opposite color first so the USS transition always has something to interpolate. */
        static void AnimateSubTabHighlight(Button button, bool toActive)
        {
            if (button == null) return;
            button.style.backgroundColor = toActive ? SharedWindowStyles.AccentColor : AccentHoverColor;
            button.schedule.Execute(() => button.style.backgroundColor = toActive ? AccentHoverColor : SharedWindowStyles.AccentColor).ExecuteLater(16);
        }

        void RefreshControllerSubTabLabels()
        {
            if (_wdTabButton != null) _wdTabButton.text = L10n.Get("controller.subtab.wd");
            if (_networkTabButton != null) _networkTabButton.text = L10n.Get("controller.subtab.network_sync");
            if (_subAssetsTabButton != null) _subAssetsTabButton.text = L10n.Get("controller.subtab.sub_assets");
            if (_menusTabButton != null) _menusTabButton.text = L10n.Get("controller.subtab.menus");

            if (_wdEmptyLabel != null) _wdEmptyLabel.text = L10n.Get("controller.no_controller");
            if (_wdSetAllOnButton != null) _wdSetAllOnButton.text = L10n.Get("controller.wd.set_all_on");
            if (_wdSetAllOffButton != null) _wdSetAllOffButton.text = L10n.Get("controller.wd.set_all_off");
            if (_wdOnHeaderLabel != null) _wdOnHeaderLabel.text = L10n.Get("controller.wd.on_col");
            if (_wdOffHeaderLabel != null) _wdOffHeaderLabel.text = L10n.Get("controller.wd.off_col");
            if (_wdMixedHeaderLabel != null) _wdMixedHeaderLabel.text = L10n.Get("controller.wd.mixed");

            foreach (var relabel in _controllerRelabelActions) relabel();

            if (_controllerRightLabel != null) _controllerRightLabel.text = ControllerSectionCountLabel ?? string.Empty;

            // Menu inspector row labels (Name/Type/Parameter/Rotation/Horizontal/etc) are baked into
            // VisualElements at build time in RebuildMenuInspector, not read live like other labels —
            // a full rebuild is the only way to re-localize them.
            RefreshMenusBody();
        }

        void RefreshControllerCleanButton()
        {
            if (_controllerCleanButton == null) return;
            int orphanCount = _orphanedAssets?.Length ?? 0;
            _controllerCleanButton.text = L10n.Get("controller.clean").Replace("{n}", orphanCount.ToString());
        }

        void RefreshControllerPaletteColors()
        {
            if (_controllerPanel != null) _controllerPanel.style.backgroundColor = SharedWindowStyles.PrimaryColor;
            ApplySubTabAccent(_wdTabButton, _controllerSubTab == 0);
            ApplySubTabAccent(_networkTabButton, _controllerSubTab == 1);
            ApplySubTabAccent(_subAssetsTabButton, _controllerSubTab == 2);
            ApplySubTabAccent(_menusTabButton, _controllerSubTab == 3);
            if (_controllerCleanButton != null) _controllerCleanButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            if (_wdOnHeaderLabel != null) _wdOnHeaderLabel.style.backgroundColor = SharedWindowStyles.AccentColor;
            if (_wdOffHeaderLabel != null) _wdOffHeaderLabel.style.backgroundColor = SharedWindowStyles.AccentColor;
            if (_wdMixedHeaderLabel != null) _wdMixedHeaderLabel.style.backgroundColor = SharedWindowStyles.AccentColor;
            if (_wdOnRowsContainer != null) _wdOnRowsContainer.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_wdOffRowsContainer != null) _wdOffRowsContainer.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_wdMixedRowsContainer != null) _wdMixedRowsContainer.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_wdSetAllOnButton != null)
            {
                _wdSetAllOnButton.style.backgroundColor = SharedWindowStyles.AccentColor;
                _wdSetAllOffButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            }
            // Per-row On/Off/Mixed arrow buttons are rebuilt from the controller's current layer
            // states (RefreshWriteDefaultsBody), baking in SharedWindowStyles.AccentColor at that moment — a plain
            // container-background set above doesn't reach them, so rebuild to repaint with live colors.
            RefreshWriteDefaultsBody();
            RefreshNetworkToggleButtonsPalette();

            if (_subAssetRowsContainer != null) _subAssetRowsContainer.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_subAssetFilterButtons != null)
                for (int i = 0; i < _subAssetFilterButtons.Length; i++)
                    ApplySubTabAccent(_subAssetFilterButtons[i], _subAssetTypeFilter == i);
            // Alt-row shading (RowAltColor) is baked per-row in BuildSubAssetRow, baking in the color at
            // build time — rebuild the list so it repaints with the live palette.
            RefreshSubAssetsList();
            RefreshMenusPaletteColors();
        }

        /* No-op when VRC SDK isn't present — _menuControlsListView only exists under VRC_SDK_VRCSDK3. */
        void RefreshMenusPaletteColors()
        {
#if VRC_SDK_VRCSDK3
            if (_menuControlsListView != null) _menuControlsListView.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_menuInspectorPanel != null) _menuInspectorPanel.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_menuCountFrame != null) _menuCountFrame.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_menuAddControlButton != null) _menuAddControlButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            if (_menuRemoveControlButton != null) _menuRemoveControlButton.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            // Control rows (selection tint) and the inspector's type/value dropdowns are rebuilt from
            // the current menu, baking in Styles colors at that moment — rebuild to repaint them live.
            RefreshMenusBody();
#endif
        }

        /* No-op when VRC SDK isn't present — network fields only exist under VRC_SDK_VRCSDK3. */
        void RefreshNetworkToggleButtonsPalette()
        {
#if VRC_SDK_VRCSDK3
            RefreshNetworkToggleButtons();
            if (_networkLayerButton != null) _networkLayerButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            if (_networkRunButton != null) _networkRunButton.style.backgroundColor = SharedWindowStyles.AccentColor;
#endif
        }

        // ── Write Defaults (native) ──────────────────────────────────────────────

        Label _wdEmptyLabel;
        VisualElement _wdButtonsRow, _wdColumnsRow;
        Button _wdSetAllOnButton, _wdSetAllOffButton;
        VisualElement _wdOnRowsContainer, _wdOffRowsContainer;
        Label _wdOnHeaderLabel, _wdOffHeaderLabel;
        VisualElement _wdMixedSection;
        Label _wdMixedHeaderLabel;
        VisualElement _wdMixedRowsContainer;

        VisualElement BuildWriteDefaultsBody()
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-wd-panel");

            _wdEmptyLabel = new Label(L10n.Get("controller.no_controller"));
            _wdEmptyLabel.AddToClassList("ygdr-empty-label");
            container.Add(_wdEmptyLabel);

            _wdButtonsRow = new VisualElement();
            _wdButtonsRow.AddToClassList("ygdr-wd-buttons-row");
            _wdSetAllOnButton = new Button(() => { SetAllLayersWD(true); RefreshWriteDefaultsBody(); }) { text = L10n.Get("controller.wd.set_all_on") };
            _wdSetAllOffButton = new Button(() => { SetAllLayersWD(false); RefreshWriteDefaultsBody(); }) { text = L10n.Get("controller.wd.set_all_off") };
            _wdSetAllOnButton.AddToClassList("ygdr-wd-set-all-btn");
            _wdSetAllOffButton.AddToClassList("ygdr-wd-set-all-btn");
            StyleAccentButton(_wdSetAllOnButton);
            StyleAccentButton(_wdSetAllOffButton);
            _wdButtonsRow.Add(_wdSetAllOnButton);
            _wdButtonsRow.Add(_wdSetAllOffButton);
            container.Add(_wdButtonsRow);

            _wdColumnsRow = new VisualElement();
            _wdColumnsRow.AddToClassList("ygdr-wd-columns-row");

            var onColumn = new VisualElement();
            onColumn.AddToClassList("ygdr-wd-column");
            _wdOnHeaderLabel = new Label(L10n.Get("controller.wd.on_col"));
            _wdOnHeaderLabel.AddToClassList("ygdr-wd-column-header");
            onColumn.Add(_wdOnHeaderLabel);
            var onScroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            onScroll.AddToClassList("ygdr-wd-column-scroll");
            _wdOnRowsContainer = new VisualElement();
            onScroll.Add(_wdOnRowsContainer);
            onColumn.Add(onScroll);
            _wdColumnsRow.Add(onColumn);

            var offColumn = new VisualElement();
            offColumn.AddToClassList("ygdr-wd-column");
            _wdOffHeaderLabel = new Label(L10n.Get("controller.wd.off_col"));
            _wdOffHeaderLabel.AddToClassList("ygdr-wd-column-header");
            offColumn.Add(_wdOffHeaderLabel);
            var offScroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            offScroll.AddToClassList("ygdr-wd-column-scroll");
            _wdOffRowsContainer = new VisualElement();
            offScroll.Add(_wdOffRowsContainer);
            offColumn.Add(offScroll);
            _wdColumnsRow.Add(offColumn);

            container.Add(_wdColumnsRow);

            _wdMixedSection = new VisualElement();
            _wdMixedSection.AddToClassList("ygdr-wd-mixed-section");
            _wdMixedHeaderLabel = new Label(L10n.Get("controller.wd.mixed"));
            _wdMixedHeaderLabel.AddToClassList("ygdr-wd-mixed-header");
            _wdMixedHeaderLabel.style.backgroundColor = SharedWindowStyles.AccentColor;
            _wdMixedSection.Add(_wdMixedHeaderLabel);
            _wdMixedRowsContainer = new VisualElement();
            _wdMixedRowsContainer.AddToClassList("ygdr-wd-mixed-rows");
            _wdMixedSection.Add(_wdMixedRowsContainer);
            container.Add(_wdMixedSection);

            return container;
        }

        /* Called on sub-tab switch, selection change, undo/redo, and after every WD mutation. */
        void RefreshWriteDefaultsBody()
        {
            if (_wdBody == null) return;
            bool hasController = _controller != null;
            _wdEmptyLabel.style.display = hasController ? DisplayStyle.None : DisplayStyle.Flex;
            _wdButtonsRow.style.display = hasController ? DisplayStyle.Flex : DisplayStyle.None;
            _wdColumnsRow.style.display = hasController ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasController) { _wdMixedSection.style.display = DisplayStyle.None; return; }

            var layers      = _controller.layers;
            var onLayers    = layers.Where(layer => GetLayerWDState(layer) == WDState.On).ToArray();
            var offLayers   = layers.Where(layer => GetLayerWDState(layer) == WDState.Off).ToArray();
            var mixedLayers = layers.Where(layer => GetLayerWDState(layer) == WDState.Mixed).ToArray();

            RebuildWDColumn(_wdOnRowsContainer, onLayers, toOff: true);
            RebuildWDColumn(_wdOffRowsContainer, offLayers, toOff: false);

            _wdMixedSection.style.display = mixedLayers.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _wdMixedRowsContainer.Clear();
            foreach (var layer in mixedLayers)
            {
                var capturedLayer = layer;
                var row = new VisualElement();
                row.AddToClassList("ygdr-wd-mixed-row");

                var toOnButton = new Button(() => { SetLayerWD(capturedLayer, true); RefreshWriteDefaultsBody(); }) { text = "← On" };
                toOnButton.AddToClassList("ygdr-wd-mixed-btn");
                StyleAccentButton(toOnButton);
                row.Add(toOnButton);

                var nameLabel = new Label(layer.name);
                nameLabel.AddToClassList("ygdr-wd-mixed-name");
                row.Add(nameLabel);

                var toOffButton = new Button(() => { SetLayerWD(capturedLayer, false); RefreshWriteDefaultsBody(); }) { text = "→ Off" };
                toOffButton.AddToClassList("ygdr-wd-mixed-btn");
                StyleAccentButton(toOffButton);
                row.Add(toOffButton);

                _wdMixedRowsContainer.Add(row);
            }
        }

        void RebuildWDColumn(VisualElement rowsContainer, AnimatorControllerLayer[] layers, bool toOff)
        {
            rowsContainer.Clear();
            if (layers.Length == 0)
            {
                var emptyLabel = new Label("—");
                emptyLabel.AddToClassList("ygdr-empty-label");
                rowsContainer.Add(emptyLabel);
                return;
            }
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                var row = new VisualElement();
                row.AddToClassList("ygdr-wd-row");
                row.EnableInClassList("ygdr-wd-row-alt", i % 2 == 1);

                if (!toOff)
                {
                    var toOnButton = new Button(() => { SetLayerWD(layer, true); RefreshWriteDefaultsBody(); }) { text = "←" };
                    toOnButton.AddToClassList("ygdr-wd-row-btn");
                    StyleAccentButton(toOnButton);
                    row.Add(toOnButton);
                }

                var nameLabel = new Label(layer.name);
                nameLabel.AddToClassList("ygdr-wd-row-name");
                row.Add(nameLabel);

                if (toOff)
                {
                    var toOffButton = new Button(() => { SetLayerWD(layer, false); RefreshWriteDefaultsBody(); }) { text = "→" };
                    toOffButton.AddToClassList("ygdr-wd-row-btn");
                    StyleAccentButton(toOffButton);
                    row.Add(toOffButton);
                }

                rowsContainer.Add(row);
            }
        }

        // ── Network Sync ──────────────────────────────────────────────────────

#if VRC_SDK_VRCSDK3
        bool   _networkUseBool;
        string _networkParamName        = "network";
        string _networkStatesPrefix     = "{N} ";
        bool   _networkRemoveParamDrivers;
        bool   _networkRemoveAudioPlay;
        bool   _networkRemoveTracking;
        bool   _networkAnyStateTransitions;
        bool   _networkPackIntoSubSM;
        bool   _networkPreserveExitTime;
        bool   _networkPreserveDuration;
        bool   _networkPreserveOffset;
        bool   _networkUseOwnInstance;
        bool   _networkMergeTaggedDuplicates;
        bool   _networkCreateBackup;
        int    _networkLayerIndex;
#endif

        // ── Network Sync (native) ────────────────────────────────────────────────

#if VRC_SDK_VRCSDK3
        VisualElement _networkPanel;
        Label _networkEmptyLabel;
        VisualElement _networkContent;
        Button _networkLayerButton;
        Button _networkParamTypeIntButton, _networkParamTypeBoolButton;
        Button _networkTransitionsAllButton, _networkTransitionsAnyButton;
        Toggle _networkPreserveExitTimeToggle, _networkPreserveDurationToggle, _networkPreserveOffsetToggle;
        TextField _networkParamNameField;
        Image _networkDuplicateWarningIcon;
        TextField _networkStatesPrefixField;
        Toggle _networkRemoveParamDriversToggle, _networkRemoveAudioToggle, _networkRemoveTrackingToggle;
        Toggle _networkPackIntoSubSMToggle;
        Toggle _networkUseOwnInstanceToggle;
        Toggle _networkMergeTaggedDuplicatesToggle;
        Toggle _networkCreateBackupToggle;
        Button _networkRunButton;
#endif

        VisualElement BuildNetworkSyncBody()
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-network-panel");

#if VRC_SDK_VRCSDK3
            _networkPanel = container;

            _networkEmptyLabel = new Label(L10n.Get("controller.network.no_window"));
            _networkEmptyLabel.AddToClassList("ygdr-empty-label");
            container.Add(_networkEmptyLabel);

            _networkContent = new VisualElement();
            container.Add(_networkContent);

            BuildLabeledRow("controller.network.target_layer", out var layerRowContent);
            _networkLayerButton = new Button(() =>
            {
                var activeController = GetNetworkActiveController();
                var layers = activeController != null ? activeController.layers : Array.Empty<AnimatorControllerLayer>();
                if (layers.Length == 0) return;
                var layerNames = layers.Select(layer => layer.name).ToArray();
                ShowLayerDropdown(_networkLayerButton.worldBound, layerNames, _networkLayerIndex, index => { _networkLayerIndex = index; RefreshNetworkSyncBody(); });
            });
            _networkLayerButton.AddToClassList("ygdr-network-field");
            _networkLayerButton.AddToClassList("u-flex-fill");
            _networkLayerButton.AddToClassList("ygdr-network-dropdown-field");
            RegisterDropdownLabelResize(_networkLayerButton, 18f);
            StyleAccentButton(_networkLayerButton);
            _networkLayerButton.Add(BuildDropdownArrow());
            layerRowContent.Add(_networkLayerButton);

            var paramTypeRow = BuildNetworkToggleRow("controller.network.sync_param_type", "Int", "Bool",
                out _networkParamTypeIntButton, out _networkParamTypeBoolButton,
                () => { _networkUseBool = false; RefreshNetworkToggleButtons(); },
                () => { _networkUseBool = true; RefreshNetworkToggleButtons(); });
            StyleConditionHeaderButton(_networkParamTypeIntButton, () => !_networkUseBool);
            StyleConditionHeaderButton(_networkParamTypeBoolButton, () => _networkUseBool);
            _networkContent.Add(paramTypeRow);

            var transitionsRow = BuildNetworkToggleRow("controller.network.transitions", "All-to-All", "Any State",
                out _networkTransitionsAllButton, out _networkTransitionsAnyButton,
                () => { _networkAnyStateTransitions = false; RefreshNetworkToggleButtons(); },
                () => { _networkAnyStateTransitions = true; RefreshNetworkToggleButtons(); });
            StyleConditionHeaderButton(_networkTransitionsAllButton, () => !_networkAnyStateTransitions);
            StyleConditionHeaderButton(_networkTransitionsAnyButton, () => _networkAnyStateTransitions);
            _networkContent.Add(transitionsRow);

            BuildLabeledRow("controller.network.preserve_props", out var preserveRowContent);
            preserveRowContent.Add(BuildNetworkInlineToggle("controller.network.preserve_exit_time", out _networkPreserveExitTimeToggle, value => _networkPreserveExitTime = value));
            preserveRowContent.Add(BuildNetworkInlineToggle("controller.network.preserve_duration", out _networkPreserveDurationToggle, value => _networkPreserveDuration = value));
            preserveRowContent.Add(BuildNetworkInlineToggle("controller.network.preserve_offset", out _networkPreserveOffsetToggle, value => _networkPreserveOffset = value));

            BuildLabeledRow("controller.network.remove_behaviours", out var removeRowContent, "controller.network.remove_behaviours_tooltip");
            removeRowContent.Add(BuildNetworkInlineToggle("controller.network.params", out _networkRemoveParamDriversToggle, value => _networkRemoveParamDrivers = value));
            removeRowContent.Add(BuildNetworkInlineToggle("controller.network.audio", out _networkRemoveAudioToggle, value => _networkRemoveAudioPlay = value));
            removeRowContent.Add(BuildNetworkInlineToggle("controller.network.tracking", out _networkRemoveTrackingToggle, value => _networkRemoveTracking = value));

            BuildLabeledRow("controller.network.sync_param_name", out var paramNameRowContent);
            _networkParamNameField = new TextField { value = _networkParamName };
            _networkParamNameField.AddToClassList("ygdr-network-field");
            _networkParamNameField.AddToClassList("u-flex-fill");
            _networkParamNameField.RegisterValueChangedCallback(evt => { _networkParamName = evt.newValue; RefreshNetworkValidity(); });
            paramNameRowContent.Add(_networkParamNameField);
            _networkDuplicateWarningIcon = BuildWarningIcon(EditorGUIUtility.IconContent("warning@2x").image, L10n.Get("controller.network.duplicate_name"), "ygdr-network-warning-icon");
            _controllerRelabelActions.Add(() => _networkDuplicateWarningIcon.tooltip = L10n.Get("controller.network.duplicate_name"));
            paramNameRowContent.Add(_networkDuplicateWarningIcon);

            BuildLabeledRow("controller.network.states_prefix", out var prefixRowContent);
            _networkStatesPrefixField = new TextField { value = _networkStatesPrefix };
            _networkStatesPrefixField.AddToClassList("ygdr-network-field");
            _networkStatesPrefixField.AddToClassList("u-flex-fill");
            _networkStatesPrefixField.RegisterValueChangedCallback(evt => { _networkStatesPrefix = evt.newValue; RefreshNetworkValidity(); });
            prefixRowContent.Add(_networkStatesPrefixField);

            BuildLabeledRow("controller.network.layer_options", out var optionsRowContent);
            optionsRowContent.Add(BuildNetworkInlineToggleWithTooltip("controller.network.pack_subsm", "controller.network.pack_subsm_tooltip", out _networkPackIntoSubSMToggle, value => _networkPackIntoSubSM = value));
            optionsRowContent.Add(BuildNetworkInlineToggleWithTooltip("controller.network.own_instance", "controller.network.own_instance_tooltip", out _networkUseOwnInstanceToggle, value => _networkUseOwnInstance = value));
            optionsRowContent.Add(BuildNetworkInlineToggleWithTooltip("controller.network.merge_tagged", "controller.network.merge_tagged_tooltip", out _networkMergeTaggedDuplicatesToggle, value =>
            {
                _networkMergeTaggedDuplicates = value;
                if (value) EnsureMergeTagColor();
            }));
            optionsRowContent.Add(BuildNetworkInlineToggleWithTooltip("controller.network.create_backup", "controller.network.create_backup_tooltip", out _networkCreateBackupToggle, value => _networkCreateBackup = value));

            _networkRunButton = new Button(RunNetworkSync) { text = L10n.Get("controller.network.run") };
            _controllerRelabelActions.Add(() => _networkRunButton.text = L10n.Get("controller.network.run"));
            _networkRunButton.AddToClassList("ygdr-network-run-btn");
            StyleAccentButton(_networkRunButton);
            _networkContent.Add(_networkRunButton);
#else
            var noVrcLabel = new Label(L10n.Get("controller.network.no_vrcsdk"));
            _controllerRelabelActions.Add(() => noVrcLabel.text = L10n.Get("controller.network.no_vrcsdk"));
            noVrcLabel.AddToClassList("ygdr-empty-label");
            container.Add(noVrcLabel);
#endif
            return container;
        }

#if VRC_SDK_VRCSDK3
        /* Builds a "label | content" row (25%/75% split via ygdr-network-label-wide / ygdr-network-row-content) and appends it to _networkContent. */
        VisualElement BuildLabeledRow(string labelKey, out VisualElement rowContent, string tooltipKey = null)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-network-row");
            var label = new Label(L10n.Get(labelKey)) { tooltip = tooltipKey != null ? L10n.Get(tooltipKey) : null };
            _controllerRelabelActions.Add(() =>
            {
                label.text = L10n.Get(labelKey);
                if (tooltipKey != null) label.tooltip = L10n.Get(tooltipKey);
            });
            label.AddToClassList("ygdr-network-label-wide");
            row.Add(label);

            rowContent = new VisualElement();
            rowContent.AddToClassList("ygdr-network-row-content");
            row.Add(rowContent);

            _networkContent.Add(row);
            return row;
        }

        /* Caller styles/refreshes the active state (see StyleConditionHeaderButton/RefreshNetworkToggleButtons in Transitions.cs). */
        VisualElement BuildNetworkToggleRow(string labelKey, string falseLabel, string trueLabel, out Button falseButton, out Button trueButton, Action onFalse, Action onTrue)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-network-row");
            var labelElement = new Label(L10n.Get(labelKey));
            _controllerRelabelActions.Add(() => labelElement.text = L10n.Get(labelKey));
            labelElement.AddToClassList("ygdr-network-label-wide");
            row.Add(labelElement);

            var rowContent = new VisualElement();
            rowContent.AddToClassList("ygdr-network-row-content");
            row.Add(rowContent);

            var falseBtn = new Button(onFalse) { text = falseLabel };
            falseBtn.AddToClassList("ygdr-network-toggle-btn");
            rowContent.Add(falseBtn);
            var trueBtn = new Button(onTrue) { text = trueLabel };
            trueBtn.AddToClassList("ygdr-network-toggle-btn");
            rowContent.Add(trueBtn);

            falseButton = falseBtn;
            trueButton = trueBtn;
            return row;
        }

        VisualElement BuildNetworkInlineToggle(string labelKey, out Toggle toggle, Action<bool> onChanged)
        {
            var container = new VisualElement();
            container.AddToClassList("ygdr-network-inline-toggle");
            var toggleElement = new Toggle();
            toggleElement.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            container.Add(toggleElement);
            var labelElement = new Label(L10n.Get(labelKey));
            _controllerRelabelActions.Add(() => labelElement.text = L10n.Get(labelKey));
            labelElement.AddToClassList("ygdr-network-inline-label");
            container.Add(labelElement);
            toggle = toggleElement;
            return container;
        }

        VisualElement BuildNetworkInlineToggleWithTooltip(string labelKey, string tooltipKey, out Toggle toggle, Action<bool> onChanged)
        {
            var container = BuildNetworkInlineToggle(labelKey, out toggle, onChanged);
            container.tooltip = L10n.Get(tooltipKey);
            _controllerRelabelActions.Add(() => container.tooltip = L10n.Get(tooltipKey));
            return container;
        }

#endif

        /* Called on sub-tab switch and whenever the active state machine changes (RefreshLayerBar/undo-redo). No-op when VRC SDK isn't present. */
        void RefreshNetworkSyncBody()
        {
#if VRC_SDK_VRCSDK3
            if (_networkPanel == null) return;
            bool hasWindow = _activeStateMachine != null;
            _networkEmptyLabel.style.display = hasWindow ? DisplayStyle.None : DisplayStyle.Flex;
            _networkContent.style.display = hasWindow ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasWindow) return;

            var activeController = GetNetworkActiveController();
            var layers = activeController != null ? activeController.layers : Array.Empty<AnimatorControllerLayer>();
            _networkLayerIndex = Mathf.Clamp(_networkLayerIndex, 0, Mathf.Max(0, layers.Length - 1));
            SetTruncatedDropdownLabel(_networkLayerButton, layers.Length > 0 ? layers[_networkLayerIndex].name : "—", 18f);
            _networkLayerButton.SetEnabled(layers.Length > 0);

            RefreshNetworkToggleButtons();

            _networkPreserveExitTimeToggle.SetValueWithoutNotify(_networkPreserveExitTime);
            _networkPreserveDurationToggle.SetValueWithoutNotify(_networkPreserveDuration);
            _networkPreserveOffsetToggle.SetValueWithoutNotify(_networkPreserveOffset);
            _networkParamNameField.SetValueWithoutNotify(_networkParamName);
            _networkStatesPrefixField.SetValueWithoutNotify(_networkStatesPrefix);
            _networkRemoveParamDriversToggle.SetValueWithoutNotify(_networkRemoveParamDrivers);
            _networkRemoveAudioToggle.SetValueWithoutNotify(_networkRemoveAudioPlay);
            _networkRemoveTrackingToggle.SetValueWithoutNotify(_networkRemoveTracking);
            _networkPackIntoSubSMToggle.SetValueWithoutNotify(_networkPackIntoSubSM);
            _networkUseOwnInstanceToggle.SetValueWithoutNotify(_networkUseOwnInstance);
            _networkMergeTaggedDuplicatesToggle.SetValueWithoutNotify(_networkMergeTaggedDuplicates);
            _networkCreateBackupToggle.SetValueWithoutNotify(_networkCreateBackup);

            RefreshNetworkValidity();
#endif
        }

#if VRC_SDK_VRCSDK3
        void RefreshNetworkToggleButtons()
        {
            if (_networkParamTypeIntButton == null) return;
            _networkParamTypeIntButton.style.backgroundColor = !_networkUseBool ? AccentHoverColor : SharedWindowStyles.AccentColor;
            _networkParamTypeBoolButton.style.backgroundColor = _networkUseBool ? AccentHoverColor : SharedWindowStyles.AccentColor;
            _networkTransitionsAllButton.style.backgroundColor = !_networkAnyStateTransitions ? AccentHoverColor : SharedWindowStyles.AccentColor;
            _networkTransitionsAnyButton.style.backgroundColor = _networkAnyStateTransitions ? AccentHoverColor : SharedWindowStyles.AccentColor;
        }

        /* Recomputes the duplicate-parameter-name warning and the Run button's enabled state. */
        void RefreshNetworkValidity()
        {
            if (_networkPanel == null) return;
            var activeController = GetNetworkActiveController();
            string trimmedNetworkParamName = _networkParamName.Trim();
            bool isDuplicateName = activeController != null
                && !string.IsNullOrWhiteSpace(trimmedNetworkParamName)
                && activeController.parameters.Any(parameter =>
                    parameter.name == trimmedNetworkParamName
                    || (parameter.name.StartsWith(trimmedNetworkParamName)
                        && parameter.name.Length > trimmedNetworkParamName.Length
                        && parameter.name[trimmedNetworkParamName.Length..].All(char.IsDigit)));

            _networkDuplicateWarningIcon.style.display = isDuplicateName ? DisplayStyle.Flex : DisplayStyle.None;

            bool canRun = !string.IsNullOrWhiteSpace(_networkParamName) && !string.IsNullOrWhiteSpace(_networkStatesPrefix) && !isDuplicateName;
            _networkRunButton.SetEnabled(canRun);
        }

        AnimatorController GetNetworkActiveController()
        {
            if (_activeStateMachine == null) return null;
            var smAssetPath = AssetDatabase.GetAssetPath(_activeStateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(smAssetPath);
        }

        AnimatorStateMachine GetNetworkTargetSM()
        {
            var activeController = GetNetworkActiveController();
            var layers = activeController != null ? activeController.layers : Array.Empty<AnimatorControllerLayer>();
            return (activeController == null || layers.Length == 0) ? _activeStateMachine : activeController.layers[_networkLayerIndex].stateMachine;
        }

        /* Adds a default color-tag entry for AnimatorNetworkSync.MergeTag if one doesn't already exist,
           so tagged states light up on the graph without the user needing to configure it manually.
           Never overwrites an existing entry (respects a color the user already chose). */
        void EnsureMergeTagColor()
        {
            var settings = AnimatorDefaultSettings.Load();
            if (settings.colorTags.Any(colorTag => colorTag.tagName == AnimatorNetworkSync.MergeTag)) return;
            settings.colorTags.Add(new AnimatorColorTag { tagName = AnimatorNetworkSync.MergeTag, color = new Color(0.35f, 0.75f, 1.00f, 1f) });
            settings.Save();
            if (_colorTagsListContainer != null) RebuildColorTagsList(settings);
        }

        void RunNetworkSync()
        {
            var targetSM = GetNetworkTargetSM();
            AnimatorNetworkSync.NetworkSync(targetSM, new NetworkSyncConfig
            {
                useBool                      = _networkUseBool,
                paramName                    = _networkParamName.Trim(),
                statesPrefix                 = _networkStatesPrefix,
                removeParamDrivers           = _networkRemoveParamDrivers,
                removeAudioPlay              = _networkRemoveAudioPlay,
                removeTracking               = _networkRemoveTracking,
                anyStateTransitions          = _networkAnyStateTransitions,
                packIntoSubSM                = _networkPackIntoSubSM,
                preserveExitTime             = _networkPreserveExitTime,
                preserveDuration             = _networkPreserveDuration,
                preserveOffset               = _networkPreserveOffset,
                useOwnNetworkInstance        = _networkUseOwnInstance,
                mergeTaggedDuplicates        = _networkMergeTaggedDuplicates,
                createBackup                 = _networkCreateBackup
            });
        }
#endif

        // ── Clips ─────────────────────────────────────────────────────────────

        string     _clipRemapFromPath  = "";
        string     _clipRemapToPath    = "";
        GameObject _clipRemapAvatarRoot;
        AnimatorController _slotController;
        bool       _slotHasNoAnimator;
        AnimatorController ClipController => _slotController != null ? _slotController : _controller;
        AnimatorClipRemapper.ScanResult _clipScanResult;
        bool       _clipScanned;
        HashSet<int> _clipsWithBrokenIds;
        bool         _brokenIdsDirty;
        GameObject   _cachedBrokenRoot;
        HashSet<int>                             _selectedClipIds         = new HashSet<int>();
        bool                                     _autoRepathEnabled;
        bool                                     _suppressAutoRepathDialog;
        List<(Transform transform, string path)> _hierarchySnapshot;

        // ── Sub-Assets + Clip Remapper (native) ──────────────────────────────────

        VisualElement _subAssetsPanel;
        Label _subAssetsEmptyLabel;
        VisualElement _subAssetsFilterBar;
        Button[] _subAssetFilterButtons;
        Label[] _subAssetFilterLabels;
        VisualElement _clipRemapperSection;
        ObjectField _clipRemapAvatarRootField;
        Button _clipRemapScanButton;
        Label _clipRemapInvalidSlotWarning;
        Button _clipRemapAutoRepathButton;
        Label _clipRemapScanResultLabel;
        VisualElement _clipRemapBrokenSegmentsContainer;
        TextField _clipRemapFromPathField;
        ObjectField _clipRemapFromPathGOField;
        TextField _clipRemapToPathField;
        ObjectField _clipRemapToPathGOField;
        Button _clipRemapConfirmButton;
        VisualElement _subAssetSearchRow;
        TextField _subAssetSearchField;
        Label _subAssetSearchHintLabel;
        Label _subAssetEmptyTypeLabel;
        Label _subAssetNoMatchesLabel;
        ScrollView _subAssetListScroll;
        VisualElement _subAssetRowsContainer;

        VisualElement BuildSubAssetsBody()
        {
            _subAssetsPanel = new VisualElement();
            _subAssetsPanel.AddToClassList("ygdr-subassets-panel");

            _subAssetsEmptyLabel = new Label(L10n.Get("controller.no_controller"));
            _controllerRelabelActions.Add(() => _subAssetsEmptyLabel.text = L10n.Get("controller.no_controller"));
            _subAssetsEmptyLabel.AddToClassList("ygdr-empty-label");
            _subAssetsPanel.Add(_subAssetsEmptyLabel);

            _subAssetsFilterBar = new VisualElement();
            _subAssetsFilterBar.AddToClassList("ygdr-subassets-filter-bar");
            _subAssetFilterButtons = new Button[4];
            _subAssetFilterLabels = new Label[4];
            var filterKeys = new[]
            {
                "controller.subassets.state_machines",
                "controller.subassets.states",
                "controller.subassets.blend_trees",
                "controller.subassets.clips",
            };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var button = new Button(() => SetSubAssetTypeFilter(index));
                button.AddToClassList("ygdr-subassets-filter-btn");
                StyleHoverTint(button, () => _subAssetTypeFilter == index, () => AccentHoverColor, () => SharedWindowStyles.AccentColor);
                var icon = new Image { image = SubAssetFilterIcons[i], scaleMode = ScaleMode.ScaleToFit };
                icon.AddToClassList("ygdr-subassets-filter-icon");
                button.Add(icon);
                var label = new Label(L10n.Get(filterKeys[index]));
                _controllerRelabelActions.Add(() => label.text = L10n.Get(filterKeys[index]));
                label.AddToClassList("ygdr-subassets-filter-label");
                button.Add(label);
                _subAssetFilterButtons[i] = button;
                _subAssetFilterLabels[i] = label;
                _subAssetsFilterBar.Add(button);
            }
            _subAssetsPanel.Add(_subAssetsFilterBar);

            _clipRemapperSection = BuildClipRemapperSection();
            _subAssetsPanel.Add(_clipRemapperSection);

            _subAssetSearchRow = new VisualElement();
            _subAssetSearchRow.AddToClassList("ygdr-subassets-search-row");
            _subAssetSearchField = new TextField();
            _subAssetSearchField.AddToClassList("ygdr-subassets-search-field");
            _subAssetSearchField.RegisterValueChangedCallback(evt => { _subAssetSearch = evt.newValue; RefreshSubAssetsList(); });
            _subAssetSearchRow.Add(_subAssetSearchField);
            _subAssetSearchHintLabel = new Label(L10n.Get("controller.subassets.search"));
            _controllerRelabelActions.Add(() => _subAssetSearchHintLabel.text = L10n.Get("controller.subassets.search"));
            _subAssetSearchHintLabel.AddToClassList("ygdr-subassets-search-hint");
            _subAssetSearchHintLabel.pickingMode = PickingMode.Ignore;
            _subAssetSearchRow.Add(_subAssetSearchHintLabel);
            _subAssetsPanel.Add(_subAssetSearchRow);

            _subAssetEmptyTypeLabel = new Label(L10n.Get("controller.subassets.none"));
            _controllerRelabelActions.Add(() => _subAssetEmptyTypeLabel.text = L10n.Get("controller.subassets.none"));
            _subAssetEmptyTypeLabel.AddToClassList("ygdr-empty-label");
            _subAssetsPanel.Add(_subAssetEmptyTypeLabel);

            _subAssetNoMatchesLabel = new Label(L10n.Get("controller.subassets.no_matches"));
            _controllerRelabelActions.Add(() => _subAssetNoMatchesLabel.text = L10n.Get("controller.subassets.no_matches"));
            _subAssetNoMatchesLabel.AddToClassList("ygdr-empty-label");
            _subAssetsPanel.Add(_subAssetNoMatchesLabel);

            _subAssetListScroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            _subAssetListScroll.AddToClassList("ygdr-subassets-list-scroll");
            _subAssetRowsContainer = new VisualElement();
            _subAssetListScroll.Add(_subAssetRowsContainer);
            _subAssetsPanel.Add(_subAssetListScroll);

            return _subAssetsPanel;
        }

        VisualElement BuildClipRemapperSection()
        {
            var section = new VisualElement();
            section.AddToClassList("ygdr-clipremap-section");

            var rootRow = new VisualElement();
            rootRow.AddToClassList("ygdr-clipremap-row");
            var rootLabel = new Label(L10n.Get("controller.repath.avatar_root"));
            _controllerRelabelActions.Add(() => rootLabel.text = L10n.Get("controller.repath.avatar_root"));
            rootLabel.AddToClassList("ygdr-clipremap-label-narrow");
            rootRow.Add(rootLabel);
            _clipRemapAvatarRootField = new ObjectField { objectType = typeof(GameObject) };
            _clipRemapAvatarRootField.AddToClassList("ygdr-clipremap-field");
            _clipRemapAvatarRootField.RegisterValueChangedCallback(evt => OnAvatarRootFieldChanged((GameObject)evt.newValue));
            rootRow.Add(_clipRemapAvatarRootField);
            _clipRemapScanButton = new Button(RunClipScan) { text = L10n.Get("controller.repath.scan") };
            _controllerRelabelActions.Add(() => _clipRemapScanButton.text = L10n.Get("controller.repath.scan"));
            _clipRemapScanButton.AddToClassList("ygdr-clipremap-scan-btn");
            StyleSecondaryButton(_clipRemapScanButton);
            rootRow.Add(_clipRemapScanButton);
            section.Add(rootRow);

            _clipRemapInvalidSlotWarning = new Label();
            _clipRemapInvalidSlotWarning.AddToClassList("ygdr-clipremap-warning");
            section.Add(_clipRemapInvalidSlotWarning);

            _clipRemapAutoRepathButton = new Button(ToggleAutoRepath);
            _clipRemapAutoRepathButton.AddToClassList("ygdr-clipremap-auto-btn");
            section.Add(_clipRemapAutoRepathButton);

            _clipRemapScanResultLabel = new Label();
            _clipRemapScanResultLabel.AddToClassList("ygdr-empty-label");
            section.Add(_clipRemapScanResultLabel);

            _clipRemapBrokenSegmentsContainer = new VisualElement();
            _clipRemapBrokenSegmentsContainer.AddToClassList("ygdr-clipremap-broken-list");
            section.Add(_clipRemapBrokenSegmentsContainer);

            var fromRow = new VisualElement();
            fromRow.AddToClassList("ygdr-clipremap-row");
            var fromLabel = new Label(L10n.Get("controller.repath.from_path"));
            _controllerRelabelActions.Add(() => fromLabel.text = L10n.Get("controller.repath.from_path"));
            fromLabel.AddToClassList("ygdr-clipremap-label-narrow");
            fromRow.Add(fromLabel);
            _clipRemapFromPathField = new TextField();
            _clipRemapFromPathField.AddToClassList("ygdr-clipremap-field");
            _clipRemapFromPathField.RegisterValueChangedCallback(evt => { _clipRemapFromPath = evt.newValue; RefreshClipRemapConfirmButton(); });
            fromRow.Add(_clipRemapFromPathField);
            _clipRemapFromPathGOField = new ObjectField { objectType = typeof(GameObject) };
            _clipRemapFromPathGOField.AddToClassList("ygdr-clipremap-go-field");
            _clipRemapFromPathGOField.RegisterValueChangedCallback(evt =>
            {
                var fromGO = (GameObject)evt.newValue;
                if (fromGO != null && _clipRemapAvatarRoot != null)
                {
                    _clipRemapFromPath = AnimationUtility.CalculateTransformPath(fromGO.transform, _clipRemapAvatarRoot.transform);
                    _clipRemapFromPathField.SetValueWithoutNotify(_clipRemapFromPath);
                }
                _clipRemapFromPathGOField.SetValueWithoutNotify(null);
                RefreshClipRemapConfirmButton();
            });
            fromRow.Add(_clipRemapFromPathGOField);
            section.Add(fromRow);

            var toRow = new VisualElement();
            toRow.AddToClassList("ygdr-clipremap-row");
            var toLabel = new Label(L10n.Get("controller.repath.to_path"));
            _controllerRelabelActions.Add(() => toLabel.text = L10n.Get("controller.repath.to_path"));
            toLabel.AddToClassList("ygdr-clipremap-label-narrow");
            toRow.Add(toLabel);
            _clipRemapToPathField = new TextField();
            _clipRemapToPathField.AddToClassList("ygdr-clipremap-field");
            _clipRemapToPathField.RegisterValueChangedCallback(evt => { _clipRemapToPath = evt.newValue; RefreshClipRemapConfirmButton(); });
            toRow.Add(_clipRemapToPathField);
            _clipRemapToPathGOField = new ObjectField { objectType = typeof(GameObject) };
            _clipRemapToPathGOField.AddToClassList("ygdr-clipremap-go-field");
            _clipRemapToPathGOField.RegisterValueChangedCallback(evt =>
            {
                var toGO = (GameObject)evt.newValue;
                if (toGO != null && _clipRemapAvatarRoot != null)
                {
                    _clipRemapToPath = AnimationUtility.CalculateTransformPath(toGO.transform, _clipRemapAvatarRoot.transform);
                    _clipRemapToPathField.SetValueWithoutNotify(_clipRemapToPath);
                }
                _clipRemapToPathGOField.SetValueWithoutNotify(null);
                RefreshClipRemapConfirmButton();
            });
            toRow.Add(_clipRemapToPathGOField);
            section.Add(toRow);

            _clipRemapConfirmButton = new Button(RunClipRemap);
            _clipRemapConfirmButton.AddToClassList("ygdr-clipremap-confirm-btn");
            StyleSecondaryButton(_clipRemapConfirmButton);
            section.Add(_clipRemapConfirmButton);

            return section;
        }

        void SetSubAssetTypeFilter(int index)
        {
            int previousFilter = _subAssetTypeFilter;
            if (previousFilter != index && _subAssetListScroll != null) _subAssetListScroll.scrollOffset = Vector2.zero;
            _subAssetTypeFilter = index;
            RefreshSubAssetsBody();
            if (_controllerRightLabel != null) _controllerRightLabel.text = ControllerSectionCountLabel ?? string.Empty;

            if (previousFilter != index && _subAssetFilterButtons != null)
            {
                AnimateSubTabHighlight(_subAssetFilterButtons[previousFilter], false);
                AnimateSubTabHighlight(_subAssetFilterButtons[index], true);
            }
        }

        void OnAvatarRootFieldChanged(GameObject newRoot)
        {
            _clipRemapAvatarRoot = newRoot;
            _clipScanned         = false;
            _brokenIdsDirty      = true;
            _cachedBrokenRoot    = null;
            _subAssetsByType     = null;
            RefreshAvatarSlot();

            if (_autoRepathEnabled)
            {
                if (_slotController != null) BuildHierarchySnapshot(_clipRemapAvatarRoot.GetComponent<Animator>().transform);
                else _hierarchySnapshot = null;
            }
            RefreshSubAssetsBody();
        }

        void RunClipScan()
        {
            _clipScanResult = AnimatorClipRemapper.ScanBrokenPaths(ClipController, _clipRemapAvatarRoot);
            _clipScanned = true;
            if (_clipScanResult.brokenSegments != null && _clipScanResult.brokenSegments.Length > 0)
                _clipRemapFromPath = _clipScanResult.brokenSegments[0].segment;
            RefreshClipRemapperSection();
        }

        void ToggleAutoRepath()
        {
            if (_autoRepathEnabled)
            {
                SetAutoRepathEnabled(false);
            }
            else if (EditorUtility.DisplayDialog(
                L10n.Get("controller.repath.confirm_title"),
                L10n.Get("controller.repath.confirm_body"),
                L10n.Get("controller.repath.confirm_ok"), L10n.Get("controller.repath.confirm_cancel")))
            {
                SetAutoRepathEnabled(true);
            }
            RefreshClipRemapperSection();
        }

        void RunClipRemap()
        {
            string trimmedFrom = _clipRemapFromPath.TrimEnd('/');
            string trimmedTo   = _clipRemapToPath.TrimEnd('/');
            bool hasSelection  = _selectedClipIds.Count > 0;
            if (hasSelection)
                AnimatorClipRemapper.RemapSelectedClips(ClipController, _selectedClipIds, trimmedFrom, trimmedTo);
            else
                AnimatorClipRemapper.RemapAll(ClipController, trimmedFrom, trimmedTo);
            _clipScanned = false;
            _subAssetsByType = null;
            RefreshSubAssetsBody();
        }

        void RefreshClipRemapConfirmButton()
        {
            if (_clipRemapConfirmButton == null) return;
            string trimmedFrom = _clipRemapFromPath.TrimEnd('/');
            string trimmedTo   = _clipRemapToPath.TrimEnd('/');
            bool canRemap      = !string.IsNullOrEmpty(trimmedFrom) && trimmedFrom != trimmedTo;
            bool hasSelection  = _selectedClipIds.Count > 0;
            _clipRemapConfirmButton.text = hasSelection ? $"{L10n.Get("controller.repath.remap_selected")} ({_selectedClipIds.Count})" : L10n.Get("controller.repath.remap_clips");
            _clipRemapConfirmButton.SetEnabled(canRemap);
        }

        /* Called whenever the block is shown or its inputs change. */
        void RefreshClipRemapperSection()
        {
            if (_clipRemapperSection == null) return;
            _clipRemapAvatarRootField.SetValueWithoutNotify(_clipRemapAvatarRoot);

            bool slotInvalid = _clipRemapAvatarRoot != null && _slotController == null;
            _clipRemapScanButton.SetEnabled(_clipRemapAvatarRoot != null && !slotInvalid);

            _clipRemapInvalidSlotWarning.style.display = slotInvalid ? DisplayStyle.Flex : DisplayStyle.None;
            if (slotInvalid)
                _clipRemapInvalidSlotWarning.text = _slotHasNoAnimator
                    ? "No Animator component on this GameObject"
                    : "Animator has no AnimatorController assigned";

            bool autoRepathDisabled = _clipRemapAvatarRoot == null || slotInvalid;
            _clipRemapAutoRepathButton.SetEnabled(!autoRepathDisabled);
            _clipRemapAutoRepathButton.text = _autoRepathEnabled ? L10n.Get("controller.repath.auto_on") : L10n.Get("controller.repath.auto_off");
            // Sky-blue when enabled — deliberately different hue from row selection green to avoid palette conflicts.
            _clipRemapAutoRepathButton.style.backgroundColor = _autoRepathEnabled ? new Color(0.3f, 0.75f, 1f) : SharedWindowStyles.SecondaryColor;

            bool hasNone = !_clipScanned || _clipScanResult.brokenSegments == null || _clipScanResult.brokenSegments.Length == 0;
            _clipRemapScanResultLabel.style.display = _clipScanned ? DisplayStyle.Flex : DisplayStyle.None;
            if (_clipScanned)
                _clipRemapScanResultLabel.text = hasNone ? L10n.Get("controller.repath.no_broken") : $"{_clipScanResult.totalBrokenCount} {L10n.Get("controller.repath.broken_bindings")}";

            _clipRemapBrokenSegmentsContainer.Clear();
            if (_clipScanned && !hasNone)
            {
                int displayCount = Mathf.Min(_clipScanResult.brokenSegments.Length, 5);
                for (int i = 0; i < displayCount; i++)
                {
                    var (segment, count) = _clipScanResult.brokenSegments[i];
                    var capturedSegment = segment;
                    var segmentButton = new Button(() =>
                    {
                        _clipRemapFromPath = capturedSegment;
                        _clipRemapFromPathField.SetValueWithoutNotify(capturedSegment);
                        RefreshClipRemapConfirmButton();
                    })
                    { text = $"  {segment}  ({count})" };
                    segmentButton.AddToClassList("ygdr-clipremap-segment-btn");
                    StyleSecondaryButton(segmentButton);
                    _clipRemapBrokenSegmentsContainer.Add(segmentButton);
                }
                if (_clipScanResult.brokenSegments.Length > 5)
                {
                    var moreLabel = new Label($"and {_clipScanResult.brokenSegments.Length - 5} more…");
                    moreLabel.AddToClassList("ygdr-empty-label");
                    _clipRemapBrokenSegmentsContainer.Add(moreLabel);
                }
            }

            _clipRemapFromPathField.SetValueWithoutNotify(_clipRemapFromPath);
            _clipRemapToPathField.SetValueWithoutNotify(_clipRemapToPath);
            RefreshClipRemapConfirmButton();
        }

        /* Called on sub-tab switch and whenever the controller's assets, the active selection, or the clip-remapper's avatar root/hierarchy change. */
        void RefreshSubAssetsBody()
        {
            if (_subAssetsPanel == null) return;
            bool hasController = _controller != null;
            _subAssetsEmptyLabel.style.display = hasController ? DisplayStyle.None : DisplayStyle.Flex;
            _subAssetsFilterBar.style.display = hasController ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasController)
            {
                _clipRemapperSection.style.display = DisplayStyle.None;
                HideSubAssetSearchAndList();
                return;
            }

            if (_subAssetsByType == null || _subAssetCachedController != _controller)
                RebuildSubAssetCache();

            for (int i = 0; i < 4; i++)
            {
                _subAssetFilterButtons[i].EnableInClassList("ygdr-subassets-filter-btn-active", _subAssetTypeFilter == i);
                ApplySubTabAccent(_subAssetFilterButtons[i], _subAssetTypeFilter == i);
            }

            if (_subAssetsByType == null)
            {
                _clipRemapperSection.style.display = DisplayStyle.None;
                HideSubAssetSearchAndList();
                return;
            }

            bool showClipRemapper = _subAssetTypeFilter == 3;
            _clipRemapperSection.style.display = showClipRemapper ? DisplayStyle.Flex : DisplayStyle.None;
            if (showClipRemapper)
            {
                if (_brokenIdsDirty)
                {
                    GameObject brokenRoot = _clipRemapAvatarRoot;
                    if (brokenRoot == null)
                    {
                        if (_cachedBrokenRoot == null ||
                            _cachedBrokenRoot.GetComponent<Animator>()?.runtimeAnimatorController != ClipController)
                        {
#pragma warning disable CS0618
                            var sceneAnimator = UnityEngine.Object.FindObjectsOfType<Animator>()
                                .FirstOrDefault(a => a.runtimeAnimatorController == ClipController);
#pragma warning restore CS0618
                            _cachedBrokenRoot = sceneAnimator != null ? sceneAnimator.gameObject : null;
                        }
                        brokenRoot = _cachedBrokenRoot;
                    }
                    _clipsWithBrokenIds = brokenRoot != null
                        ? AnimatorClipRemapper.CollectBrokenClipIds(ClipController, brokenRoot) : null;
                    _brokenIdsDirty = false;
                }
                RefreshClipRemapperSection();
            }

            RefreshSubAssetsList();
        }

        void HideSubAssetSearchAndList()
        {
            if (_subAssetSearchRow != null) _subAssetSearchRow.style.display = DisplayStyle.None;
            if (_subAssetEmptyTypeLabel != null) _subAssetEmptyTypeLabel.style.display = DisplayStyle.None;
            if (_subAssetNoMatchesLabel != null) _subAssetNoMatchesLabel.style.display = DisplayStyle.None;
            if (_subAssetListScroll != null) _subAssetListScroll.style.display = DisplayStyle.None;
        }

        /* Rebuilds the row list for the current type filter + search text. */
        void RefreshSubAssetsList()
        {
            if (_subAssetsPanel == null || _subAssetsByType == null) return;
            _subAssetSearchRow.style.display = DisplayStyle.Flex;
            _subAssetSearchField.SetValueWithoutNotify(_subAssetSearch);
            _subAssetSearchHintLabel.style.display = string.IsNullOrEmpty(_subAssetSearch) ? DisplayStyle.Flex : DisplayStyle.None;

            var assets = _subAssetsByType[_subAssetTypeFilter];
            if (assets == null || assets.Length == 0)
            {
                _subAssetEmptyTypeLabel.style.display = DisplayStyle.Flex;
                _subAssetNoMatchesLabel.style.display = DisplayStyle.None;
                _subAssetListScroll.style.display = DisplayStyle.None;
                return;
            }
            _subAssetEmptyTypeLabel.style.display = DisplayStyle.None;

            bool hasSearch = !string.IsNullOrEmpty(_subAssetSearch);
            var filtered = new List<UnityEngine.Object>();
            foreach (var asset in assets)
            {
                if (asset == null) continue;
                if (hasSearch && asset.name.IndexOf(_subAssetSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                filtered.Add(asset);
            }

            if (filtered.Count == 0)
            {
                _subAssetNoMatchesLabel.style.display = DisplayStyle.Flex;
                _subAssetListScroll.style.display = DisplayStyle.None;
                return;
            }
            _subAssetNoMatchesLabel.style.display = DisplayStyle.None;
            _subAssetListScroll.style.display = DisplayStyle.Flex;

            _subAssetRowsContainer.Clear();
            for (int i = 0; i < filtered.Count; i++)
                _subAssetRowsContainer.Add(BuildSubAssetRow(filtered[i], i));
        }

        VisualElement BuildSubAssetRow(UnityEngine.Object asset, int rowIndex)
        {
            bool isClips = _subAssetTypeFilter == 3;
            var row = new VisualElement();
            row.AddToClassList("ygdr-subassets-row");
            if (rowIndex % 2 == 1) row.style.backgroundColor = SharedWindowStyles.RowAltColor;

            var chip = new VisualElement();
            chip.AddToClassList("ygdr-subassets-row-chip");
            chip.RegisterCallback<MouseEnterEvent>(_ => chip.style.backgroundColor = SecondaryButtonHoverColor);
            chip.RegisterCallback<MouseLeaveEvent>(_ => chip.style.backgroundColor = new StyleColor(StyleKeyword.Null));
            row.Add(chip);

            if (isClips)
            {
                int assetId = asset.GetInstanceID();
                chip.EnableInClassList("ygdr-subassets-row-selected", _selectedClipIds.Contains(assetId));

                var button = new Button(() => { EditorGUIUtility.PingObject(asset); Selection.activeObject = asset; }) { text = asset.name };
                button.AddToClassList("ygdr-subassets-row-btn");
                chip.Add(button);

                if (_clipsWithBrokenIds != null && _clipsWithBrokenIds.Contains(assetId))
                {
                    var warningIcon = new Image { image = EditorGUIUtility.IconContent("d_console.warnicon").image, tooltip = L10n.Get("controller.subassets.warn_broken_bindings") };
                    warningIcon.AddToClassList("ygdr-subassets-row-warning");
                    chip.Add(warningIcon);
                }
                return row;
            }

            string label = asset.name;
            bool showEmptyWarning = false, showInvalidWarning = false, showEmptyMotionWarning = false;

            if (_subAssetTypeFilter == 0)
            {
                if (_rootSMIds != null && !_rootSMIds.Contains(asset.GetInstanceID()))
                    label += "  (Sub State Machine)";
                if (_emptySMIds != null && _emptySMIds.Contains(asset.GetInstanceID()))
                    showEmptyWarning = true;
            }
            else if (_subAssetTypeFilter == 1 && _statesWithInvalidTransitions != null && _statesWithInvalidTransitions.Contains(asset.GetInstanceID()))
                showInvalidWarning = true;
            else if (_subAssetTypeFilter == 1 && _statesWithNoMotion != null && _statesWithNoMotion.Contains(asset.GetInstanceID()))
                showEmptyMotionWarning = true;
            else if (_subAssetTypeFilter == 2 && _blendTreesWithEmptyMotion != null && _blendTreesWithEmptyMotion.Contains(asset.GetInstanceID()))
                showEmptyMotionWarning = true;

            var navButton = new Button(() => NavigateToAsset(asset)) { text = label };
            navButton.AddToClassList("ygdr-subassets-row-btn");
            chip.Add(navButton);

            if (showEmptyWarning || showInvalidWarning || showEmptyMotionWarning)
            {
                string tooltip = showEmptyWarning ? L10n.Get("controller.subassets.warn_empty_layer")
                    : showEmptyMotionWarning ? L10n.Get("controller.subassets.warn_empty_motion")
                    : L10n.Get("controller.subassets.warn_invalid_transition");
                var warningIcon = new Image { image = EditorGUIUtility.IconContent("d_console.warnicon").image, tooltip = tooltip };
                warningIcon.AddToClassList("ygdr-subassets-row-warning");
                chip.Add(warningIcon);
            }
            return row;
        }

        static Texture2D[] _subAssetFilterIcons;
        static Texture2D[] SubAssetFilterIcons => _subAssetFilterIcons ??= new[]
        {
            EditorGUIUtility.IconContent("d_AnimatorController Icon").image as Texture2D,
            EditorGUIUtility.IconContent("AnimatorState Icon").image as Texture2D,
            EditorGUIUtility.IconContent("d_BlendTree Icon").image as Texture2D,
            EditorGUIUtility.IconContent("AnimationClip Icon").image as Texture2D,
        };

        static Type       _animatorControllerToolType;
        static MethodInfo _setCurrentLayerMethod;
        static MethodInfo _addBreadCrumbMethod;
        static MethodInfo _frameSelectionMethod;

        static Type AnimatorControllerToolType =>
            _animatorControllerToolType ??= AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.Graphs.AnimatorControllerTool"))
                .FirstOrDefault(t => t != null);

        int                    _subAssetTypeFilter;
        string                 _subAssetSearch = "";
        AnimatorController     _subAssetCachedController;
        UnityEngine.Object[][] _subAssetsByType;
        UnityEngine.Object[]   _orphanedAssets;
        HashSet<int>           _statesWithInvalidTransitions;
        HashSet<int>           _statesWithNoMotion;
        HashSet<int>           _emptySMIds;
        HashSet<int>           _blendTreesWithEmptyMotion;
        HashSet<int>           _rootSMIds;
        HashSet<int>           _allKnownSubAssetIds;
        UnityEngine.Object     _cachedAnimatorControllerTool;

        /* Invalidates and repaints only when a change event touches an object inside the active controller's asset file. Ignores unrelated scene and asset changes. Destroyed objects are matched against the cached sub-asset ID set since their path is no longer resolvable. */
        void OnAssetChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_controller == null) return;
            string controllerPath = AssetDatabase.GetAssetPath(_controller);

            for (int i = 0; i < stream.length; i++)
            {
                bool relevant = false;
                var kind = stream.GetEventType(i);

                if (kind == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                    var changedObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = changedObj != null && AssetDatabase.GetAssetPath(changedObj) == controllerPath;
                }
                else if (kind == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var args);
                    var createdObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = createdObj != null && AssetDatabase.GetAssetPath(createdObj) == controllerPath;
                }
                else if (kind == ObjectChangeKind.DestroyAssetObject)
                {
                    stream.GetDestroyAssetObjectEvent(i, out var args);
                    relevant = _allKnownSubAssetIds?.Contains(args.instanceId) ?? false;
                }
                else if (_clipRemapAvatarRoot != null &&
                         (kind == ObjectChangeKind.ChangeGameObjectOrComponentProperties ||
                          kind == ObjectChangeKind.ChangeGameObjectStructure))
                {
                    int changedInstanceId;
                    if (kind == ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var sceneArgs);
                        changedInstanceId = sceneArgs.instanceId;
                    }
                    else
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var sceneArgs);
                        changedInstanceId = sceneArgs.instanceId;
                    }
                    var changedObj = EditorUtility.InstanceIDToObject(changedInstanceId);
                    bool isAvatarRootRelated = changedObj is GameObject changedGO && changedGO == _clipRemapAvatarRoot
                        || changedObj is Component changedComponent && changedComponent.gameObject == _clipRemapAvatarRoot;
                    if (isAvatarRootRelated)
                    {
                        RefreshAvatarSlot();
                        RefreshSubAssetsBody();
                        Repaint();
                        return;
                    }
                }

                if (!relevant) continue;
                _subAssetsByType = null;
                InvalidateConditionCache();
                if (_suppressExternalRepaint)
                {
                    EditorApplication.delayCall += () => _suppressExternalRepaint = false;
                    return;
                }
                RefreshSubAssetsBody();
                Repaint();
                return;
            }
        }

        /* Loads all sub-assets from the controller .asset file, buckets them by type into _subAssetsByType, collects unreferenced objects as orphans, and flags states with invalid transitions. */
        void RebuildSubAssetCache()
        {
            _subAssetCachedController = _controller;
            if (_controller == null) { _subAssetsByType = null; _orphanedAssets = null; return; }

            var allAssets     = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(_controller));
            var referencedIDs = CollectReferencedInstanceIDs(_controller);

            var stateMachines = new List<UnityEngine.Object>();
            var states        = new List<UnityEngine.Object>();
            var blendTrees    = new List<UnityEngine.Object>();
            var orphans       = new List<UnityEngine.Object>();

            foreach (var asset in allAssets)
            {
                if (asset == null || asset == _controller) continue;

                if (!referencedIDs.Contains(asset.GetInstanceID()))
                {
                    if (asset is FrameLayoutData or TransitionPathData) continue;
                    orphans.Add(asset);
                    continue;
                }

                if      (asset is AnimatorStateMachine) stateMachines.Add(asset);
                else if (asset is BlendTree)            blendTrees.Add(asset);
                else if (asset is AnimatorState)        states.Add(asset);
            }

            var clips = AnimatorClipRemapper.CollectAllClips(ClipController)
                .Cast<UnityEngine.Object>()
                .OrderBy(a => a.name)
                .ToArray();

            _subAssetsByType = new[]
            {
                stateMachines.OrderBy(a => a.name).ToArray(),
                states.OrderBy(a => a.name).ToArray(),
                blendTrees.OrderBy(a => a.name).ToArray(),
                clips
            };
            _brokenIdsDirty = true;
            _clipsWithBrokenIds = null;
            _cachedBrokenRoot = null;
            _orphanedAssets = orphans.ToArray();

            var paramNames = new HashSet<string>(_controller.parameters.Select(p => p.name));
            _statesWithInvalidTransitions = new HashSet<int>();
            _statesWithNoMotion = new HashSet<int>();
            foreach (var asset in states)
            {
                if (asset is AnimatorState state)
                {
                    if (HasInvalidTransition(state, paramNames))
                        _statesWithInvalidTransitions.Add(asset.GetInstanceID());
                    if (state.motion == null && !state.name.StartsWith("!"))
                        _statesWithNoMotion.Add(asset.GetInstanceID());
                }
            }

            _emptySMIds = new HashSet<int>(
                stateMachines
                    .OfType<AnimatorStateMachine>()
                    .Where(sm => sm.states.Length == 0 && sm.stateMachines.Length == 0 && !sm.name.StartsWith("!"))
                    .Select(sm => sm.GetInstanceID()));

            _blendTreesWithEmptyMotion = new HashSet<int>(
                blendTrees
                    .OfType<BlendTree>()
                    .Where(blendTree => blendTree.children.Any(child => child.motion == null))
                    .Select(blendTree => blendTree.GetInstanceID()));

            _rootSMIds = new HashSet<int>(
                _controller.layers.Select(layer => layer.stateMachine.GetInstanceID()));

            _allKnownSubAssetIds = new HashSet<int>(
                allAssets.Where(a => a != null && a != _controller).Select(a => a.GetInstanceID()));
        }

        /* Returns true if any transition on the state has no exit time and no conditions, or references a parameter not present in the controller. */
        static bool HasInvalidTransition(AnimatorState state, HashSet<string> paramNames)
        {
            foreach (var transition in state.transitions)
            {
                if (!transition.hasExitTime && transition.conditions.Length == 0) return true;
                foreach (var condition in transition.conditions)
                    if (!paramNames.Contains(condition.parameter)) return true;
            }
            return false;
        }

        /* Destroys all orphaned sub-assets via Undo.DestroyObjectImmediate, marks the controller dirty, and refreshes the cache. */
        void CleanOrphanedAssets()
        {
            if (_orphanedAssets == null || _orphanedAssets.Length == 0) return;
            foreach (var asset in _orphanedAssets)
            {
                if (asset != null)
                    Undo.DestroyObjectImmediate(asset);
            }
            EditorUtility.SetDirty(_controller);
            RebuildSubAssetCache();
        }

        /* Traverses all layers of the controller and returns the instance IDs of every object reachable from the graph — SMs, states, behaviours, transitions, and blend trees. Anything not in this set is an orphan. */
        static HashSet<int> CollectReferencedInstanceIDs(AnimatorController controller)
        {
            var ids = new HashSet<int>();
            ids.Add(controller.GetInstanceID());
            foreach (var layer in controller.layers)
                CollectSMReferences(layer.stateMachine, ids);
            return ids;
        }

        /* Recursively adds instance IDs of sm and all its children (states, behaviours, transitions, sub-SMs, blend trees) to ids. The ids.Add guard prevents revisiting the same SM twice. */
        static void CollectSMReferences(AnimatorStateMachine sm, HashSet<int> ids)
        {
            if (sm == null || !ids.Add(sm.GetInstanceID())) return;
            foreach (var behaviour in sm.behaviours)
                if (behaviour != null) ids.Add(behaviour.GetInstanceID());
            foreach (var transition in sm.anyStateTransitions)
                if (transition != null) ids.Add(transition.GetInstanceID());
            foreach (var transition in sm.entryTransitions)
                if (transition != null) ids.Add(transition.GetInstanceID());
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;
                ids.Add(state.GetInstanceID());
                foreach (var behaviour in state.behaviours)
                    if (behaviour != null) ids.Add(behaviour.GetInstanceID());
                foreach (var transition in state.transitions)
                    if (transition != null) ids.Add(transition.GetInstanceID());
                CollectBlendTreeReferences(state.motion as BlendTree, ids);
            }
            foreach (var childStateMachine in sm.stateMachines)
            {
                foreach (var transition in sm.GetStateMachineTransitions(childStateMachine.stateMachine))
                    if (transition != null) ids.Add(transition.GetInstanceID());
                CollectSMReferences(childStateMachine.stateMachine, ids);
            }
        }

        /* Recursively adds instance IDs of blendTree and all its child blend tree nodes to ids. */
        static void CollectBlendTreeReferences(BlendTree blendTree, HashSet<int> ids)
        {
            if (blendTree == null || !ids.Add(blendTree.GetInstanceID())) return;
            foreach (var childMotion in blendTree.children)
                CollectBlendTreeReferences(childMotion.motion as BlendTree, ids);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        void NavigateToAsset(UnityEngine.Object asset) => FocusAsset(asset, _controller);

        /* Navigates the Animator window to the layer containing asset, selects it, and frames it. Handles AnimatorState, AnimatorStateMachine, and BlendTree. */
        internal static void FocusAsset(UnityEngine.Object asset, AnimatorController controller)
        {
            var toolType = AnimatorControllerToolType;
            if (toolType == null || controller == null) return;

            var tools = Resources.FindObjectsOfTypeAll(toolType);
            if (tools.Length == 0) return;
            var tool = tools[0];

            int layerIndex = -1;
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var sm = layers[i].stateMachine;
                if (asset is AnimatorState layerState        && SMContainsState(sm, layerState))         { layerIndex = i; break; }
                if (asset is AnimatorStateMachine layerSubSM && SMContainsOrIs(sm, layerSubSM))          { layerIndex = i; break; }
                if (asset is BlendTree layerBlendTree        && SMContainsBlendTree(sm, layerBlendTree)) { layerIndex = i; break; }
            }
            if (layerIndex < 0) return;

            _setCurrentLayerMethod ??= toolType.GetMethod("SetCurrentLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _addBreadCrumbMethod   ??= toolType.GetMethod("AddBreadCrumb",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _frameSelectionMethod  ??= toolType.GetMethod("FrameSelection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _setCurrentLayerMethod?.Invoke(tool, new object[] { layerIndex });

            var rootSM = controller.layers[layerIndex].stateMachine;

            if (asset is BlendTree blendTree)
            {
                var containingState = FindStateWithBlendTree(rootSM, blendTree);
                if (containingState != null)
                {
                    var parentSM = FindParentSM(rootSM, containingState);
                    PushSMBreadcrumbs(tool, rootSM, parentSM ?? rootSM);

                    var blendTreePath = FindBlendTreePath(containingState.motion as BlendTree, blendTree);
                    if (blendTreePath != null)
                    {
                        for (int i = 0; i < blendTreePath.Count; i++)
                            _addBreadCrumbMethod?.Invoke(tool, new object[] { (UnityEngine.Object)blendTreePath[i], i == blendTreePath.Count - 1 });
                    }
                }
            }
            else
            {
                AnimatorStateMachine targetSM = rootSM;
                if (asset is AnimatorState state)
                    targetSM = FindParentSM(rootSM, state) ?? rootSM;
                else if (asset is AnimatorStateMachine subSM)
                    targetSM = subSM;

                PushSMBreadcrumbs(tool, rootSM, targetSM);
            }

            Selection.activeObject = asset;

            var capturedTool   = tool;
            var capturedAsset  = asset;
            var capturedMethod = _frameSelectionMethod;
            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = capturedAsset;
                EditorApplication.delayCall += () => capturedMethod?.Invoke(capturedTool, null);
            };
        }

        /* Calls AnimatorControllerTool.AddBreadCrumb for each SM along the path from rootSM to targetSM, updating the graph only on the final entry so the window navigates in one step. */
        static void PushSMBreadcrumbs(object tool, AnimatorStateMachine rootSM, AnimatorStateMachine targetSM)
        {
            if (targetSM == rootSM) return;
            var path = FindSMPath(rootSM, targetSM);
            if (path == null) return;
            for (int i = 1; i < path.Count; i++)
                _addBreadCrumbMethod?.Invoke(tool, new object[] { (UnityEngine.Object)path[i], i == path.Count - 1 });
        }

        /* Returns the ordered list of SMs from root down to target (inclusive), or null if target is not reachable from root. */
        static List<AnimatorStateMachine> FindSMPath(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == target) return new List<AnimatorStateMachine> { root };
            foreach (var childStateMachine in root.stateMachines)
            {
                var path = FindSMPath(childStateMachine.stateMachine, target);
                if (path != null) { path.Insert(0, root); return path; }
            }
            return null;
        }

        /* Returns the SM that directly contains state, searching recursively from root. Returns null if not found. */
        static AnimatorStateMachine FindParentSM(AnimatorStateMachine root, AnimatorState state)
        {
            foreach (var childState in root.states)
                if (childState.state == state) return root;
            foreach (var childStateMachine in root.stateMachines)
            {
                var found = FindParentSM(childStateMachine.stateMachine, state);
                if (found != null) return found;
            }
            return null;
        }

        /* Returns the first AnimatorState whose motion tree contains target (at any depth), or null if not found. */
        static AnimatorState FindStateWithBlendTree(AnimatorStateMachine sm, BlendTree target)
        {
            foreach (var childState in sm.states)
                if (BlendTreeContains(childState.state.motion as BlendTree, target)) return childState.state;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var found = FindStateWithBlendTree(childStateMachine.stateMachine, target);
                if (found != null) return found;
            }
            return null;
        }

        /* Returns true if state is directly or recursively contained in sm. */
        static bool SMContainsState(AnimatorStateMachine sm, AnimatorState state)
        {
            foreach (var childState in sm.states)
                if (childState.state == state) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsState(childStateMachine.stateMachine, state)) return true;
            return false;
        }

        /* Returns true if any state in sm or its sub-SMs uses target anywhere in its blend tree hierarchy. */
        static bool SMContainsBlendTree(AnimatorStateMachine sm, BlendTree target)
        {
            foreach (var childState in sm.states)
                if (BlendTreeContains(childState.state.motion as BlendTree, target)) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsBlendTree(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        /* Returns the ordered list of blend trees from root down to target (inclusive), preserving intermediate nodes for breadcrumb navigation. Returns null if target is not reachable. */
        static List<BlendTree> FindBlendTreePath(BlendTree root, BlendTree target)
        {
            if (root == null) return null;
            if (root == target) return new List<BlendTree> { root };
            foreach (var childMotion in root.children)
            {
                var path = FindBlendTreePath(childMotion.motion as BlendTree, target);
                if (path != null) { path.Insert(0, root); return path; }
            }
            return null;
        }

        /* Returns true if target is root or is reachable anywhere in root's child motion hierarchy. */
        static bool BlendTreeContains(BlendTree root, BlendTree target)
        {
            if (root == null) return false;
            if (root == target) return true;
            foreach (var childMotion in root.children)
                if (BlendTreeContains(childMotion.motion as BlendTree, target)) return true;
            return false;
        }

        // ── WD helpers ────────────────────────────────────────────────────────

        /* Returns On, Off, or Mixed depending on whether states in the layer have Write Defaults enabled, disabled, or both. Blend tree states are excluded if wdIncludeBlendTreeStates is false. */
        WDState GetLayerWDState(AnimatorControllerLayer layer)
        {
            bool hasOn = false, hasOff = false;
            bool includeBlendTrees = AnimatorDefaultSettings.Load().wdIncludeBlendTreeStates;
            CollectWDState(layer.stateMachine, ref hasOn, ref hasOff, includeBlendTrees);
            if (hasOn && hasOff) return WDState.Mixed;
            return hasOn ? WDState.On : WDState.Off;
        }

        /* Recursively sets hasOn and hasOff flags based on writeDefaultValues across all states in sm and its sub SMs. Skips states whose motion is a BlendTree when includeBlendTrees is false. */
        static void CollectWDState(AnimatorStateMachine sm, ref bool hasOn, ref bool hasOff, bool includeBlendTrees)
        {
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is BlendTree) continue;
                if (childState.state.writeDefaultValues) hasOn = true;
                else hasOff = true;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CollectWDState(childStateMachine.stateMachine, ref hasOn, ref hasOff, includeBlendTrees);
        }

        /* Sets Write Defaults on all states in a layer recursively and marks the controller dirty. Blend tree states are excluded if wdIncludeBlendTreeStates is false. */
        void SetLayerWD(AnimatorControllerLayer layer, bool value)
        {
            bool includeBlendTrees = AnimatorDefaultSettings.Load().wdIncludeBlendTreeStates;
            SetSMWD(layer.stateMachine, value, includeBlendTrees);
            EditorUtility.SetDirty(_controller);
        }

        /* Recursively sets writeDefaultValues on all states in sm and its sub SMs, registering each for undo. Skips states whose motion is a BlendTree when includeBlendTrees is false. */
        static void SetSMWD(AnimatorStateMachine sm, bool value, bool includeBlendTrees)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Set Write Defaults");
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is BlendTree) continue;
                Undo.RecordObject(childState.state, "Set Write Defaults");
                childState.state.writeDefaultValues = value;
                EditorUtility.SetDirty(childState.state);
            }
            foreach (var childStateMachine in sm.stateMachines)
                SetSMWD(childStateMachine.stateMachine, value, includeBlendTrees);
            EditorUtility.SetDirty(sm);
        }

        void SetAllLayersWD(bool value)
        {
            foreach (var layer in _controller.layers)
                SetLayerWD(layer, value);
        }

        // ── Clip selection + auto-repath ──────────────────────────────────────

        internal void UpdateSelectedClipIds()
        {
            _selectedClipIds = new HashSet<int>(
                Selection.GetFiltered<AnimationClip>(SelectionMode.Assets)
                    .Select(c => c.GetInstanceID()));
        }

        void RefreshAvatarSlot()
        {
            _slotController    = null;
            _slotHasNoAnimator = false;
            if (_clipRemapAvatarRoot == null) return;
            var animator = _clipRemapAvatarRoot.GetComponent<Animator>();
            if (animator == null) _slotHasNoAnimator = true;
            else _slotController = animator.runtimeAnimatorController as AnimatorController;
        }

        bool _suppressExternalRepaint;

        void OnHierarchyChangedRefresh()
        {
            _brokenIdsDirty = true;
            if (_clipRemapAvatarRoot != null) RefreshAvatarSlot();
            if (_suppressExternalRepaint) return;
            RefreshSubAssetsBody();
            Repaint();
        }

        void SetAutoRepathEnabled(bool enabled)
        {
            if (_autoRepathEnabled == enabled) return;
            _autoRepathEnabled = enabled;
            if (enabled)
            {
                EditorApplication.hierarchyChanged += OnHierarchyChanged;
                if (_clipRemapAvatarRoot != null)
                {
                    var animator = _clipRemapAvatarRoot.GetComponentInParent<Animator>();
                    if (animator != null) BuildHierarchySnapshot(animator.transform);
                }
            }
            else
            {
                EditorApplication.hierarchyChanged -= OnHierarchyChanged;
                _hierarchySnapshot = null;
            }
        }

        void BuildHierarchySnapshot(Transform root)
        {
            _hierarchySnapshot = new List<(Transform, string)>();
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root) continue;
                _hierarchySnapshot.Add((child, AnimationUtility.CalculateTransformPath(child, root)));
            }
        }

        void OnHierarchyChanged()
        {
            if (!_autoRepathEnabled || _clipRemapAvatarRoot == null || _suppressAutoRepathDialog) return;

            var animator = _clipRemapAvatarRoot.GetComponentInParent<Animator>();
            if (animator == null) return;
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) return;

            Transform root = animator.transform;

            if (_hierarchySnapshot == null)
            {
                BuildHierarchySnapshot(root);
                return;
            }

            var changedPaths = new Dictionary<string, string>();
            foreach (var (transform, snapshotPath) in _hierarchySnapshot)
            {
                if (transform == null || !transform.IsChildOf(root)) continue;
                string currentPath = AnimationUtility.CalculateTransformPath(transform, root);
                if (string.IsNullOrEmpty(currentPath) || string.IsNullOrEmpty(snapshotPath)) continue;
                if (currentPath != snapshotPath)
                    changedPaths[snapshotPath] = currentPath;
            }

            BuildHierarchySnapshot(root);

            if (changedPaths.Count == 0) return;
            if (!AnimatorClipRemapper.AnyClipUsesChangedPaths(controller, changedPaths)) return;

            _suppressAutoRepathDialog = true;
            try
            {
                AnimatorClipRemapper.RemapAllPaths(controller, changedPaths);
                _subAssetsByType = null;
                Repaint();
            }
            finally
            {
                _suppressAutoRepathDialog = false;
            }
        }

        // ── Transition focus (shared with AnimatorFindUsageWindow) ────────────

        /* Switches the Animator window to the layer and sub-SM containing transition, selects it, and frames it on the next editor tick. */
        internal static void FocusTransition(AnimatorStateTransition transition, AnimatorController controller)
        {
            var toolType = AnimatorEditorInit.AnimatorControllerToolType;
            if (toolType == null || controller == null) return;

            var tools = Resources.FindObjectsOfTypeAll(toolType);
            if (tools.Length == 0) return;
            var tool = tools[0];

            int layerIndex = -1;
            AnimatorStateMachine containingSM = null;

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var found = FindSMContainingTransition(controller.layers[i].stateMachine, transition);
                if (found == null) continue;
                layerIndex = i;
                containingSM = found;
                break;
            }
            if (layerIndex < 0) return;

            _setCurrentLayerMethod ??= toolType.GetMethod("SetCurrentLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _addBreadCrumbMethod   ??= toolType.GetMethod("AddBreadCrumb",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _frameSelectionMethod  ??= toolType.GetMethod("FrameSelection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _setCurrentLayerMethod?.Invoke(tool, new object[] { layerIndex });
            PushSMBreadcrumbs(tool, controller.layers[layerIndex].stateMachine, containingSM);

            Selection.activeObject = transition;
            var capturedTool = tool;
            var capturedFrameMethod = _frameSelectionMethod;
            EditorApplication.delayCall += () => capturedFrameMethod?.Invoke(capturedTool, null);
        }

        /* Switches the Animator window to the layer and sub-SM containing state, selects it, and frames it on the next editor tick. */
        internal static void FocusState(AnimatorState state, AnimatorController controller)
        {
            var toolType = AnimatorEditorInit.AnimatorControllerToolType;
            if (toolType == null || controller == null) return;

            var tools = Resources.FindObjectsOfTypeAll(toolType);
            if (tools.Length == 0) return;
            var tool = tools[0];

            int layerIndex = -1;
            AnimatorStateMachine containingSM = null;

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var found = FindSMContainingState(controller.layers[i].stateMachine, state);
                if (found == null) continue;
                layerIndex = i;
                containingSM = found;
                break;
            }
            if (layerIndex < 0) return;

            _setCurrentLayerMethod ??= toolType.GetMethod("SetCurrentLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _addBreadCrumbMethod   ??= toolType.GetMethod("AddBreadCrumb",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _frameSelectionMethod  ??= toolType.GetMethod("FrameSelection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _setCurrentLayerMethod?.Invoke(tool, new object[] { layerIndex });
            PushSMBreadcrumbs(tool, controller.layers[layerIndex].stateMachine, containingSM);

            Selection.activeObject = state;
            var capturedTool = tool;
            var capturedState = state;
            var capturedFrameMethod = _frameSelectionMethod;
            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = capturedState;
                EditorApplication.delayCall += () => capturedFrameMethod?.Invoke(capturedTool, null);
            };
        }

        static AnimatorStateMachine FindSMContainingState(AnimatorStateMachine sm, AnimatorState state)
        {
            foreach (var childState in sm.states)
                if (childState.state == state) return sm;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var found = FindSMContainingState(childStateMachine.stateMachine, state);
                if (found != null) return found;
            }
            return null;
        }

        /* Returns the SM that directly owns transition via states or anyStateTransitions, searching recursively. Returns null if not found. */
        static AnimatorStateMachine FindSMContainingTransition(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                if (anyStateTransition == transition) return sm;
            foreach (var childState in sm.states)
                foreach (var stateTransition in childState.state.transitions)
                    if (stateTransition == transition) return sm;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var found = FindSMContainingTransition(childStateMachine.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
