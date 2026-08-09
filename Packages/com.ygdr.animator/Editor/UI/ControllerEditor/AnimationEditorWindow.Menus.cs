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
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
#if VRC_SDK_VRCSDK3
        List<VRCExpressionsMenu> _menuStack = new();
        int _selectedMenuControlIndex = -1;

        VisualElement _menusPanel;
        Label _menusEmptyLabel;
        VisualElement _menuBreadcrumbRow;
        VisualElement _menuControlsPanel;
        IntegerField _menuControlCountField;
        Label _menuControlCountSuffixLabel;
        ListView _menuControlsListView;
        VRCExpressionsMenu _menuControlsBoundMenu;
        Button _menuAddControlButton, _menuRemoveControlButton;
        Label _menuMaxControlsLabel;
        VisualElement _menuInspectorPanel;
        VisualElement _menuCountFrame;

        VisualElement BuildMenusBody()
        {
            _menusPanel = new VisualElement();
            _menusPanel.AddToClassList("ygdr-menu-panel");

            _menusEmptyLabel = new Label(L10n.Get("controller.menus.no_menu"));
            _controllerRelabelActions.Add(() => _menusEmptyLabel.text = L10n.Get("controller.menus.no_menu"));
            _menusEmptyLabel.AddToClassList("ygdr-empty-label");
            _menusPanel.Add(_menusEmptyLabel);

            _menuBreadcrumbRow = new VisualElement();
            _menuBreadcrumbRow.AddToClassList("ygdr-menu-breadcrumb-row");
            _menusPanel.Add(_menuBreadcrumbRow);

            _menuControlsPanel = BuildMenuControlsPanel();
            _menusPanel.Add(_menuControlsPanel);

            _menuInspectorPanel = new VisualElement();
            _menuInspectorPanel.AddToClassList("ygdr-menu-inspector-panel");
            _menuInspectorPanel.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _menusPanel.Add(_menuInspectorPanel);

            return _menusPanel;
        }

        VisualElement BuildMenuControlsPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList("ygdr-menu-controls-panel");

            var countRow = new VisualElement();
            countRow.AddToClassList("ygdr-menu-count-row");

            var countFrame = new VisualElement();
            countFrame.AddToClassList("ygdr-menu-count-frame");
            countFrame.style.backgroundColor = SharedWindowStyles.SecondaryColor;
            _menuCountFrame = countFrame;
            countRow.Add(countFrame);

            _menuControlCountField = new IntegerField();
            _menuControlCountField.AddToClassList("ygdr-menu-count-field");
            _menuControlCountField.RegisterValueChangedCallback(evt =>
            {
                var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
                if (menu == null) return;
                SetMenuControlCount(menu, Mathf.Clamp(evt.newValue, 0, VRCExpressionsMenu.MAX_CONTROLS));
                RefreshMenusBody();
            });
            countFrame.Add(_menuControlCountField);
            _menuControlCountSuffixLabel = new Label($"/ {VRCExpressionsMenu.MAX_CONTROLS}");
            _menuControlCountSuffixLabel.AddToClassList("ygdr-menu-count-suffix");
            countFrame.Add(_menuControlCountSuffixLabel);
            panel.Add(countRow);

            _menuControlsListView = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = false,
                showAddRemoveFooter = false,
                selectionType = SelectionType.Single,
                fixedItemHeight = 24,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = MakeMenuControlRow,
                bindItem = (element, index) => BindMenuControlRow(element, _menuControlsBoundMenu, index)
            };
            _menuControlsListView.AddToClassList("ygdr-menu-controls-scroll");
            WireListViewReorder(_menuControlsListView, MoveMenuControl, () =>
            {
                var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
                if (menu != null) RebuildMenuControlRows(menu);
            });
            _menuControlsListView.selectionChanged += _ =>
            {
                _selectedMenuControlIndex = _menuControlsListView.selectedIndex;
                _menuControlsListView.RefreshItems(); // rebinds visible rows so the (unselected → selected) tint follows, since we drive it ourselves instead of Unity's default selected-item background
                var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
                if (menu != null) RebuildMenuInspector(menu);
            };
            _menuControlsListView.itemsChosen += _ => TryEnterSelectedSubMenu();
            panel.Add(_menuControlsListView);

            var footerRow = new VisualElement();
            footerRow.AddToClassList("ygdr-menu-controls-footer-row");
            _menuMaxControlsLabel = new Label(L10n.Get("controller.menus.max_controls"));
            _controllerRelabelActions.Add(() => _menuMaxControlsLabel.text = L10n.Get("controller.menus.max_controls"));
            _menuMaxControlsLabel.AddToClassList("ygdr-empty-label");
            footerRow.Add(_menuMaxControlsLabel);
            _menuAddControlButton = new Button(() =>
            {
                var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
                if (menu == null) return;
                AddMenuControl(menu);
                RefreshMenusBody();
            }) { text = "+" };
            _menuAddControlButton.AddToClassList("ygdr-menu-add-btn");
            StyleSecondaryButton(_menuAddControlButton);
            footerRow.Add(_menuAddControlButton);
            _menuRemoveControlButton = new Button(() =>
            {
                var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
                if (menu == null) return;
                bool hasSelection = _selectedMenuControlIndex >= 0 && _selectedMenuControlIndex < menu.controls.Count;
                DeleteMenuControl(menu, hasSelection ? _selectedMenuControlIndex : menu.controls.Count - 1);
                RefreshMenusBody();
            }) { text = "−" };
            _menuRemoveControlButton.AddToClassList("ygdr-menu-remove-btn");
            StyleSecondaryButton(_menuRemoveControlButton);
            footerRow.Add(_menuRemoveControlButton);
            panel.Add(footerRow);

            return panel;
        }

        /* Live-reads VRCSyncCache.GetExpressionsMenu() each call (mirrors the old every-frame OnGUI read). */
        void RefreshMenusBody()
        {
            if (_menusPanel == null) return;
            VRCSyncCache.EnsureSynced();
            var rootMenu = VRCSyncCache.GetExpressionsMenu();
            if (rootMenu == null)
            {
                _menuStack.Clear();
                _selectedMenuControlIndex = -1;
                _menusEmptyLabel.style.display = DisplayStyle.Flex;
                _menuBreadcrumbRow.style.display = DisplayStyle.None;
                _menuControlsPanel.style.display = DisplayStyle.None;
                _menuInspectorPanel.style.display = DisplayStyle.None;
                return;
            }

            if (_menuStack.Count == 0 || _menuStack[0] != rootMenu)
            {
                _menuStack.Clear();
                _menuStack.Add(rootMenu);
                _selectedMenuControlIndex = -1;
            }

            _menusEmptyLabel.style.display = DisplayStyle.None;
            _menuBreadcrumbRow.style.display = DisplayStyle.Flex;
            _menuControlsPanel.style.display = DisplayStyle.Flex;
            _menuInspectorPanel.style.display = DisplayStyle.Flex;

            var currentMenu = _menuStack[_menuStack.Count - 1];
            if (currentMenu.controls == null)
                currentMenu.controls = new List<VRCExpressionsMenu.Control>();

            RebuildMenuBreadcrumbs();
            RebuildMenuControlRows(currentMenu);

            _menuControlCountField.SetValueWithoutNotify(currentMenu.controls.Count);
            bool canAdd = currentMenu.controls.Count < VRCExpressionsMenu.MAX_CONTROLS;
            _menuAddControlButton.SetEnabled(canAdd);
            _menuRemoveControlButton.SetEnabled(currentMenu.controls.Count > 0);
            _menuMaxControlsLabel.style.display = canAdd ? DisplayStyle.None : DisplayStyle.Flex;

            RebuildMenuInspector(currentMenu);
        }

        void RebuildMenuBreadcrumbs()
        {
            _menuBreadcrumbRow.Clear();
            for (int i = 0; i < _menuStack.Count; i++)
            {
                var menu = _menuStack[i];
                bool isLast = i == _menuStack.Count - 1;
                string label = string.IsNullOrEmpty(menu.name) ? "(menu)" : menu.name;

                if (isLast)
                {
                    var leafLabel = new Label(label);
                    leafLabel.AddToClassList("ygdr-menu-breadcrumb-leaf");
                    _menuBreadcrumbRow.Add(leafLabel);
                }
                else
                {
                    int capturedIndex = i;
                    var crumbButton = new Button(() => { TruncateMenuStack(capturedIndex); RefreshMenusBody(); }) { text = label };
                    crumbButton.AddToClassList("ygdr-menu-breadcrumb-btn");
                    _menuBreadcrumbRow.Add(crumbButton);

                    var sep = new Label(">");
                    sep.AddToClassList("ygdr-menu-breadcrumb-sep");
                    _menuBreadcrumbRow.Add(sep);
                }
            }
        }

        void TruncateMenuStack(int index)
        {
            _menuStack.RemoveRange(index + 1, _menuStack.Count - index - 1);
            _selectedMenuControlIndex = -1;
        }

        void MoveMenuControl(int oldIndex, int newIndex)
        {
            var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
            if (menu == null || oldIndex == newIndex) return;

            Undo.RecordObject(menu, "Reorder Menu Controls");
            var movedControl = menu.controls[oldIndex];
            menu.controls.RemoveAt(oldIndex);
            menu.controls.Insert(newIndex, movedControl);
            if (_selectedMenuControlIndex == oldIndex) _selectedMenuControlIndex = newIndex;
            EditorUtility.SetDirty(menu);
        }

        void TryEnterSelectedSubMenu()
        {
            var menu = _menuStack.Count > 0 ? _menuStack[_menuStack.Count - 1] : null;
            if (menu == null) return;
            int selectedIndex = _menuControlsListView.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= menu.controls.Count) return;

            var control = menu.controls[selectedIndex];
            if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null) return;

            _menuStack.Add(control.subMenu);
            _selectedMenuControlIndex = -1;
            RefreshMenusBody();
        }

        /* itemsSource is a throwaway index list, not menu.controls directly — ListView's built-in reorder would
           mutate it without going through Undo, and bindItem always re-reads menu.controls[index] fresh anyway. */
        void RebuildMenuControlRows(VRCExpressionsMenu menu)
        {
            var indices = new List<int>(menu.controls.Count);
            for (int i = 0; i < menu.controls.Count; i++) indices.Add(i);
            _menuControlsBoundMenu = menu;
            _menuControlsListView.itemsSource = indices;
            _menuControlsListView.SetSelectionWithoutNotify(_selectedMenuControlIndex >= 0 ? new[] { _selectedMenuControlIndex } : Array.Empty<int>());
            _menuControlsListView.Rebuild();
        }

        /* Hover handlers registered once here (not in bindItem) — ListView recycles these elements across binds,
           and re-registering per bind stacked stale closures from prior menus, causing wrong tint after breadcrumb navigation. */
        VisualElement MakeMenuControlRow()
        {
            var element = new VisualElement();
            element.RegisterCallback<MouseEnterEvent>(_ => element.style.backgroundColor = SecondaryButtonHoverColor);
            element.RegisterCallback<MouseLeaveEvent>(_ =>
                element.style.backgroundColor = (int)element.userData == _selectedMenuControlIndex
                    ? SecondaryButtonHoverColor
                    : new StyleColor(StyleKeyword.Null));
            return element;
        }

        void BindMenuControlRow(VisualElement element, VRCExpressionsMenu menu, int index)
        {
            element.Clear();
            element.ClearClassList();
            element.AddToClassList("ygdr-menu-control-row");
            if (index % 2 != 0) element.AddToClassList("ygdr-menu-control-row-alt");
            element.userData = index;
            element.style.backgroundColor = index == _selectedMenuControlIndex ? SecondaryButtonHoverColor : new StyleColor(StyleKeyword.Null);

            var control = menu.controls[index];

            var icon = new Image { image = control.icon, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("ygdr-menu-control-icon");
            element.Add(icon);

            var nameLabel = new Label(string.IsNullOrEmpty(control.name) ? "(unnamed)" : control.name);
            nameLabel.AddToClassList("ygdr-menu-control-name");
            element.Add(nameLabel);

            var typeLabel = new Label(control.type.ToString());
            typeLabel.AddToClassList("ygdr-menu-control-type");
            element.Add(typeLabel);
        }

        static void AddMenuControl(VRCExpressionsMenu menu)
        {
            Undo.RecordObject(menu, "Add Menu Control");
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name          = "New Control",
                type          = VRCExpressionsMenu.Control.ControlType.Button,
                parameter     = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels        = Array.Empty<VRCExpressionsMenu.Control.Label>(),
            });
            EditorUtility.SetDirty(menu);
        }

        void DeleteMenuControl(VRCExpressionsMenu menu, int index)
        {
            if (index < 0 || index >= menu.controls.Count) return;
            Undo.RecordObject(menu, "Delete Menu Control");
            menu.controls.RemoveAt(index);
            EditorUtility.SetDirty(menu);
            _selectedMenuControlIndex = -1;
        }

        void SetMenuControlCount(VRCExpressionsMenu menu, int targetCount)
        {
            if (targetCount == menu.controls.Count) return;
            Undo.RecordObject(menu, "Set Menu Control Count");
            while (menu.controls.Count < targetCount)
                AddMenuControl(menu);
            while (menu.controls.Count > targetCount)
                menu.controls.RemoveAt(menu.controls.Count - 1);
            if (_selectedMenuControlIndex >= menu.controls.Count)
                _selectedMenuControlIndex = -1;
            EditorUtility.SetDirty(menu);
        }

        // ── Inspector (rebuilt on selection / type change) ──────────────────────

        void RebuildMenuInspector(VRCExpressionsMenu menu)
        {
            _menuInspectorPanel.Clear();
            if (_selectedMenuControlIndex < 0 || _selectedMenuControlIndex >= menu.controls.Count) return;
            var control = menu.controls[_selectedMenuControlIndex];

            _menuInspectorPanel.Add(BuildMenuNameIconRow(menu, control));
            _menuInspectorPanel.Add(BuildMenuTypeField(menu, control));
            _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.parameter"), control.parameter, false,
                onParameterChanged: () => RebuildMenuInspector(menu)));
            _menuInspectorPanel.Add(BuildMenuValueRow(menu, control));

            switch (control.type)
            {
                case VRCExpressionsMenu.Control.ControlType.SubMenu:
                    _menuInspectorPanel.Add(BuildMenuSubMenuRow(menu, control));
                    break;

                case VRCExpressionsMenu.Control.ControlType.RadialPuppet:
                    EnsureSubParameterCount(control, 1);
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.rotation"), control.subParameters[0], true));
                    break;

                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet:
                    EnsureSubParameterCount(control, 2);
                    EnsureLabelCount(control, 4);
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.horizontal"), control.subParameters[0], true));
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.vertical"),   control.subParameters[1], true));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.up"),    control.labels, 0));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.right"), control.labels, 1));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.down"),  control.labels, 2));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.left"),  control.labels, 3));
                    break;

                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                    EnsureSubParameterCount(control, 4);
                    EnsureLabelCount(control, 4);
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.up"),    control.subParameters[0], true));
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.right"), control.subParameters[1], true));
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.down"),  control.subParameters[2], true));
                    _menuInspectorPanel.Add(BuildMenuParameterFieldRow(menu, L10n.Get("controller.menus.left"),  control.subParameters[3], true));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.up"),    control.labels, 0));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.right"), control.labels, 1));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.down"),  control.labels, 2));
                    _menuInspectorPanel.Add(BuildMenuAxisLabelRow(menu, L10n.Get("controller.menus.left"),  control.labels, 3));
                    break;
            }
        }

        VisualElement BuildMenuNameIconRow(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var label = new Label(L10n.Get("controller.menus.name"));
            label.AddToClassList("ygdr-menu-property-label");
            row.Add(label);

            var nameField = new TextField { value = control.name };
            nameField.AddToClassList("ygdr-menu-property-field");
            nameField.AddToClassList("u-flex-fill");
            nameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Edit Menu Control");
                control.name = evt.newValue;
                EditorUtility.SetDirty(menu);
                RebuildMenuControlRows(menu);
            });
            row.Add(nameField);

            var iconField = new ObjectField { objectType = typeof(Texture2D), value = control.icon };
            iconField.AddToClassList("ygdr-menu-icon-field");
            iconField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Edit Menu Control");
                control.icon = evt.newValue as Texture2D;
                EditorUtility.SetDirty(menu);
                RebuildMenuControlRows(menu);
            });
            row.Add(iconField);

            return row;
        }

        VisualElement BuildMenuTypeField(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var label = new Label(L10n.Get("controller.menus.type"));
            label.AddToClassList("ygdr-menu-property-label");
            row.Add(label);

            var typeButton = new Button { text = control.type.ToString() };
            typeButton.AddToClassList("ygdr-menu-property-field");
            typeButton.AddToClassList("u-flex-fill");
            typeButton.AddToClassList("ygdr-menu-param-dropdown-btn");
            StyleAccentButton(typeButton);
            typeButton.Add(BuildDropdownArrow());
            typeButton.clicked += () =>
            {
                var genericMenu = new GenericMenu();
                foreach (VRCExpressionsMenu.Control.ControlType type in Enum.GetValues(typeof(VRCExpressionsMenu.Control.ControlType)))
                {
                    var capturedType = type;
                    genericMenu.AddItem(new GUIContent(type.ToString()), type == control.type, () =>
                    {
                        Undo.RecordObject(menu, "Change Control Type");
                        control.type = capturedType;
                        EditorUtility.SetDirty(menu);
                        RefreshMenusBody();
                    });
                }
                genericMenu.ShowAsContext();
            };
            row.Add(typeButton);

            return row;
        }

        VisualElement BuildMenuValueRow(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var label = new Label(L10n.Get("controller.menus.value"));
            label.AddToClassList("ygdr-menu-property-label");
            row.Add(label);

            var boundParam = FindParameter(_controller, control.parameter.name);

            if (boundParam != null && boundParam.type == AnimatorControllerParameterType.Bool)
            {
                var boolLabel = new Label(L10n.Get("controller.menus.value_bool_fixed"));
                boolLabel.AddToClassList("ygdr-menu-value-field");
                boolLabel.SetEnabled(false);
                row.Add(boolLabel);
                return row;
            }

            bool isInt = boundParam != null && boundParam.type == AnimatorControllerParameterType.Int;
            float min = isInt ? 0f : (boundParam == null || boundParam.type == AnimatorControllerParameterType.Float ? -1f : 0f);
            float max = isInt ? 255f : 1f;

            if (isInt)
            {
                var intSlider = new SliderInt((int)min, (int)max) { value = Mathf.RoundToInt(control.value) };
                intSlider.AddToClassList("ygdr-menu-value-slider");
                var intField = new IntegerField { value = Mathf.RoundToInt(control.value) };
                intField.AddToClassList("ygdr-menu-value-field");

                intSlider.RegisterValueChangedCallback(evt =>
                {
                    int clamped = Mathf.Clamp(evt.newValue, (int)min, (int)max);
                    intField.SetValueWithoutNotify(clamped);
                    Undo.RecordObject(menu, "Edit Control Value");
                    control.value = clamped;
                    EditorUtility.SetDirty(menu);
                });
                intField.RegisterValueChangedCallback(evt =>
                {
                    int clamped = Mathf.Clamp(evt.newValue, (int)min, (int)max);
                    intSlider.SetValueWithoutNotify(clamped);
                    Undo.RecordObject(menu, "Edit Control Value");
                    control.value = clamped;
                    EditorUtility.SetDirty(menu);
                });

                row.Add(intSlider);
                row.Add(intField);
                return row;
            }

            var slider = new Slider(min, max) { value = control.value };
            slider.AddToClassList("ygdr-menu-value-slider");
            var floatField = new FloatField { value = control.value };
            floatField.AddToClassList("ygdr-menu-value-field");

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                floatField.SetValueWithoutNotify(clamped);
                Undo.RecordObject(menu, "Edit Control Value");
                control.value = clamped;
                EditorUtility.SetDirty(menu);
            });
            floatField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                slider.SetValueWithoutNotify(clamped);
                Undo.RecordObject(menu, "Edit Control Value");
                control.value = clamped;
                EditorUtility.SetDirty(menu);
            });

            row.Add(slider);
            row.Add(floatField);
            return row;
        }

        VisualElement BuildMenuSubMenuRow(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var label = new Label(L10n.Get("controller.menus.enter_submenu"));
            label.AddToClassList("ygdr-menu-property-label");
            row.Add(label);

            var subMenuField = new ObjectField { objectType = typeof(VRCExpressionsMenu), value = control.subMenu };
            subMenuField.AddToClassList("ygdr-menu-property-field");
            subMenuField.AddToClassList("u-flex-fill");
            subMenuField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Assign Submenu");
                control.subMenu = evt.newValue as VRCExpressionsMenu;
                EditorUtility.SetDirty(menu);
                RebuildMenuInspector(menu);
            });
            row.Add(subMenuField);

            if (control.subMenu == null)
            {
                var createButton = new Button(() =>
                {
                    Undo.RecordObject(menu, "Create Submenu");
                    control.subMenu = CreateSubMenuAsset(menu, control.name);
                    EditorUtility.SetDirty(menu);
                    RebuildMenuInspector(menu);
                }) { text = L10n.Get("controller.menus.new_submenu") };
                createButton.AddToClassList("ygdr-menu-submenu-btn");
                row.Add(createButton);
            }
            else
            {
                var openButton = new Button(() =>
                {
                    _menuStack.Add(control.subMenu);
                    _selectedMenuControlIndex = -1;
                    RefreshMenusBody();
                }) { text = L10n.Get("controller.menus.open_submenu") };
                openButton.AddToClassList("ygdr-menu-submenu-btn");
                StyleAccentButton(openButton);
                row.Add(openButton);
            }

            return row;
        }

        static AnimatorControllerParameter FindParameter(AnimatorController controller, string name)
        {
            if (controller == null || string.IsNullOrEmpty(name)) return null;
            return Array.Find(controller.parameters, p => p.name == name);
        }

        VisualElement BuildMenuParameterFieldRow(VRCExpressionsMenu menu, string label, VRCExpressionsMenu.Control.Parameter parameter, bool expectFloat, Action onParameterChanged = null)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-menu-property-label");
            row.Add(labelElement);

            var dropdownButton = new Button();
            dropdownButton.AddToClassList("ygdr-menu-property-field");
            dropdownButton.AddToClassList("ygdr-menu-param-dropdown-btn");
            dropdownButton.style.flexGrow = 2;
            dropdownButton.style.flexBasis = 0;
            RegisterDropdownLabelResize(dropdownButton, 76f);
            StyleAccentButton(dropdownButton);
            dropdownButton.Add(BuildDropdownArrow());
            row.Add(dropdownButton);

            var manualField = new TextField { value = parameter.name };
            manualField.AddToClassList("ygdr-menu-property-field");
            manualField.AddToClassList("u-flex-fill");
            row.Add(manualField);

            /* Overlaid on the dropdown button (not row siblings) so showing/hiding never shifts row flex layout. */
            var notFoundIcon = BuildWarningIcon(EditorGUIUtility.IconContent("d_console.erroricon").image, L10n.Get("controller.menus.param_not_found"), "ygdr-menu-warning-icon");
            notFoundIcon.AddToClassList("ygdr-menu-param-overlay-icon");
            dropdownButton.Add(notFoundIcon);

            var mismatchIcon = BuildWarningIcon(EditorGUIUtility.IconContent("d_console.warnicon").image, L10n.Get("controller.menus.type_mismatch"), "ygdr-menu-warning-icon");
            mismatchIcon.AddToClassList("ygdr-menu-param-overlay-icon");
            dropdownButton.Add(mismatchIcon);

            var typeLabel = new Label();
            typeLabel.AddToClassList("ygdr-menu-param-type-label");
            dropdownButton.Add(typeLabel);

            void RefreshFieldState()
            {
                SetTruncatedDropdownLabel(dropdownButton, string.IsNullOrEmpty(parameter.name) ? "[None]" : parameter.name, 76f);
                var boundParam = _controller != null ? FindParameter(_controller, parameter.name) : null;
                bool hasName = !string.IsNullOrEmpty(parameter.name);
                notFoundIcon.style.display = hasName && boundParam == null ? DisplayStyle.Flex : DisplayStyle.None;
                bool showMismatch = hasName && boundParam != null && expectFloat &&
                    AnimatorParameterOps.MapToVrcValueType(boundParam.type) != VRCExpressionParameters.ValueType.Float;
                mismatchIcon.style.display = showMismatch ? DisplayStyle.Flex : DisplayStyle.None;
                typeLabel.style.display = boundParam != null ? DisplayStyle.Flex : DisplayStyle.None;
                typeLabel.text = boundParam != null ? boundParam.type.ToString() : string.Empty;
            }

            dropdownButton.clicked += () =>
            {
                if (_controller == null || _controller.parameters.Length == 0) return;
                ShowParameterDropdown(dropdownButton.worldBound, parameter.name, selectedName =>
                {
                    Undo.RecordObject(menu, "Edit Menu Parameter");
                    parameter.name = selectedName;
                    EditorUtility.SetDirty(menu);
                    manualField.SetValueWithoutNotify(parameter.name);
                    RefreshFieldState();
                    onParameterChanged?.Invoke();
                }, includeNone: true);
            };
            manualField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Edit Menu Parameter");
                parameter.name = evt.newValue;
                EditorUtility.SetDirty(menu);
                RefreshFieldState();
                onParameterChanged?.Invoke();
            });

            RefreshFieldState();
            return row;
        }

        VisualElement BuildMenuAxisLabelRow(VRCExpressionsMenu menu, string label, VRCExpressionsMenu.Control.Label[] labels, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("ygdr-menu-property-row");
            var labelElement = new Label(label);
            labelElement.AddToClassList("ygdr-menu-property-label");
            row.Add(labelElement);

            var axisLabel = labels[index];
            var nameField = new TextField { value = axisLabel.name };
            nameField.AddToClassList("ygdr-menu-property-field");
            nameField.AddToClassList("u-flex-fill");
            var iconField = new ObjectField { objectType = typeof(Texture2D), value = axisLabel.icon };
            iconField.AddToClassList("ygdr-menu-icon-field");

            nameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Edit Axis Label");
                var updated = labels[index];
                updated.name = evt.newValue;
                labels[index] = updated;
                EditorUtility.SetDirty(menu);
            });
            iconField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(menu, "Edit Axis Label");
                var updated = labels[index];
                updated.icon = evt.newValue as Texture2D;
                labels[index] = updated;
                EditorUtility.SetDirty(menu);
            });

            row.Add(nameField);
            row.Add(iconField);
            return row;
        }

        static void EnsureSubParameterCount(VRCExpressionsMenu.Control control, int count)
        {
            if (control.subParameters != null && control.subParameters.Length == count) return;
            var newArray = new VRCExpressionsMenu.Control.Parameter[count];
            for (int i = 0; i < count; i++)
                newArray[i] = control.subParameters != null && i < control.subParameters.Length && control.subParameters[i] != null
                    ? control.subParameters[i]
                    : new VRCExpressionsMenu.Control.Parameter { name = "" };
            control.subParameters = newArray;
        }

        static void EnsureLabelCount(VRCExpressionsMenu.Control control, int count)
        {
            if (control.labels != null && control.labels.Length == count) return;
            var newArray = new VRCExpressionsMenu.Control.Label[count];
            for (int i = 0; i < count; i++)
                newArray[i] = control.labels != null && i < control.labels.Length
                    ? control.labels[i]
                    : new VRCExpressionsMenu.Control.Label { name = "" };
            control.labels = newArray;
        }

        static VRCExpressionsMenu CreateSubMenuAsset(VRCExpressionsMenu parentMenu, string controlName)
        {
            string parentPath = AssetDatabase.GetAssetPath(parentMenu);
            string folder = string.IsNullOrEmpty(parentPath) ? "Assets" : Path.GetDirectoryName(parentPath);
            string baseName = string.IsNullOrEmpty(controlName) ? L10n.Get("controller.menus.new_submenu") : controlName;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}.asset");

            var newMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            newMenu.controls = new List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(newMenu, assetPath);
            AssetDatabase.SaveAssets();
            return newMenu;
        }
#else
        VisualElement BuildMenusBody()
        {
            var container = new VisualElement();
            var noVrcLabel = new Label(L10n.Get("controller.menus.no_vrcsdk"));
            _controllerRelabelActions.Add(() => noVrcLabel.text = L10n.Get("controller.menus.no_vrcsdk"));
            noVrcLabel.AddToClassList("ygdr-empty-label");
            container.Add(noVrcLabel);
            return container;
        }

        void RefreshMenusBody() { }
#endif
    }
}
#endif
