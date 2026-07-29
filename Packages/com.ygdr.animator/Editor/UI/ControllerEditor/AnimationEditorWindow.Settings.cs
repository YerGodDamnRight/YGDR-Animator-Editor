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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        bool _interfaceOpen;
        bool _graphGridOpen;
        bool _nodeIconsOpen;
        bool _transitionOverlayOpen;
        bool _nodeColorsOpen;
        bool _transitionDefaultsOpen;
        bool _miscOpen;
        bool _keybindsOpen;
        string _recordingActionId;
        string _paletteImportText = "";

        VisualElement _settingsBodyRoot;
        VisualElement _keybindsBodyRef;
        VisualElement _savedPalettesListContainer;
        VisualElement _colorTagsListContainer;
        readonly Dictionary<char, Button> _delimiterButtons = new();

        const float SettingsRowDeleteButtonWidth = 24f;

        // Order matches AnimatorDefaultSettings.CapturePaletteColors/ApplyPaletteColors (30 palette-tracked colors,
        // spanning Interface/GraphGrid/NodeColors/NodeOverlay/TransitionOverlay). Kept so a bulk palette apply
        // (reset / saved-palette / import) can push new values into every live ColorField without rebuilding bodies.
        readonly ColorField[] _paletteColorFields = new ColorField[AnimatorDefaultSettings.PaletteColorCount];

        VisualElement BuildSettingsBody()
        {
            var root = new VisualElement();
            root.AddToClassList("ygdr-settings-root");
            _settingsBodyRoot = root;

            PopulateSettingsBody(root);

            return root;
        }

        void PopulateSettingsBody(VisualElement root)
        {
            var settings = AnimatorDefaultSettings.Load();
            BuildInterfaceSection(root, settings);
            BuildGraphGridSection(root, settings);
            BuildNodeColorsSection(root, settings);
            BuildOverlaySection(root, settings);
            BuildTransitionOverlaySection(root, settings);
            BuildTransitionDefaultsSection(root, settings);
            BuildKeybindsSection(root, settings);
            BuildMiscellaneousSection(root, settings);
        }

        /* No per-field refresh path, so language switch clears and repopulates the body in place — keeps accordion open/closed state. */
        void RefreshSettingsLocalizedLabels()
        {
            if (_settingsBodyRoot == null) return;
            _settingsBodyRoot.Clear();
            PopulateSettingsBody(_settingsBodyRoot);
            RefreshSettingsPaletteColors();
        }

        void RefreshSettingsPaletteColors()
        {
            if (_settingsBodyRoot == null) return;
            _settingsBodyRoot.Query<VisualElement>(className: "ygdr-settings-section-header").ForEach(h => h.style.backgroundColor = SharedWindowStyles.AccentColor);
            _settingsBodyRoot.Query<VisualElement>(className: "ygdr-settings-section-body").ForEach(b => b.style.backgroundColor = SharedWindowStyles.PrimaryColor);
            _settingsBodyRoot.Query<Button>(className: "ygdr-settings-accent-btn").ForEach(b => b.style.backgroundColor = SharedWindowStyles.AccentColor);
            RefreshDelimiterButtonColors();

            var settings = AnimatorDefaultSettings.Load();
            var colors = AnimatorDefaultSettings.CapturePaletteColors(settings);
            for (int i = 0; i < _paletteColorFields.Length; i++)
                _paletteColorFields[i]?.SetValueWithoutNotify(colors[i]);
        }

        // ── Shared native helpers (Settings) ────────────────────────────────────

        VisualElement BuildSettingsSectionHeader(VisualElement parent, VisualElement body, string title, Func<bool> getOpen, Action<bool> setOpen)
        {
            var header = new VisualElement();
            header.AddToClassList("ygdr-behavior-section-header");
            header.AddToClassList("ygdr-settings-section-header");
            header.style.backgroundColor = SharedWindowStyles.AccentColor;
            parent.Add(header);

            var titleLabel = new Label();
            titleLabel.AddToClassList("ygdr-behavior-section-label");
            header.Add(titleLabel);

            void RefreshOpen(bool animate)
            {
                bool open = getOpen();
                titleLabel.text = (open ? "▼ " : "▶ ") + title;
                if (animate)
                    AnimateSectionBody(header, body, open, SnapScrollToBottom);
                else
                    body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RefreshOpen(false);

            header.AddManipulator(new Clickable(() => { setOpen(!getOpen()); RefreshOpen(true); }));

            body.AddToClassList("ygdr-settings-section-body");
            body.style.backgroundColor = SharedWindowStyles.PrimaryColor;
            parent.Add(body);

            return header;
        }

        const float SectionAnimDurationSeconds = 0.15f;

        // Animates the body's height between 0 and its natural content height, since `display`
        // itself can't be animated; overflow is clipped to hidden while height is a fixed value.
        // Driven off EditorApplication.update with real elapsed time (not experimental.animation's
        // panel scheduler) because the panel scheduler's tick cadence is throttled/uneven when the
        // window is otherwise idle. Header+body picking is disabled for the duration (per-element,
        // restored from a snapshot so pre-existing Ignore placeholders stay Ignore) so mouse-move
        // over their buttons/toggles doesn't fire hover callbacks in the same frame as the tween's
        // own style write.
        void AnimateSectionBody(VisualElement header, VisualElement body, bool open, Action onTick = null)
        {
            var savedPickingModes = new Dictionary<VisualElement, PickingMode>();
            void SuppressPicking(VisualElement e)
            {
                savedPickingModes[e] = e.pickingMode;
                e.pickingMode = PickingMode.Ignore;
                foreach (var child in e.Children())
                    SuppressPicking(child);
            }
            void RestorePicking()
            {
                foreach (var kv in savedPickingModes)
                    kv.Key.pickingMode = kv.Value;
            }
            SuppressPicking(header);
            SuppressPicking(body);

            body.style.overflow = Overflow.Hidden;
            if (open)
            {
                body.style.visibility = Visibility.Hidden;
                body.style.display = DisplayStyle.Flex;
                body.style.height = StyleKeyword.Auto;

                // GeometryChangedEvent only fires once UI Toolkit has actually finished a layout pass,
                // so resolvedStyle.height here is guaranteed fresh — unlike a fixed-frame deferral,
                // which can read a value from before the display/height change above was laid out.
                void OnMeasured(GeometryChangedEvent evt)
                {
                    body.UnregisterCallback<GeometryChangedEvent>(OnMeasured);
                    float targetHeight = body.resolvedStyle.height;
                    body.style.height = 0;
                    body.style.visibility = Visibility.Visible;
                    RunHeightTween(body, 0f, targetHeight, () => { body.style.height = StyleKeyword.Auto; body.style.overflow = Overflow.Visible; RestorePicking(); }, onTick);
                }
                body.RegisterCallback<GeometryChangedEvent>(OnMeasured);
            }
            else
            {
                float startHeight = body.resolvedStyle.height;
                body.style.height = startHeight;
                RunHeightTween(body, startHeight, 0f, () => { body.style.display = DisplayStyle.None; RestorePicking(); }, onTick);
            }
        }

        // userData doubles as a generation token: if a second tween starts on the same body before
        // the first finishes (fast double-click), the stale callback detects it's been superseded
        // and unsubscribes instead of fighting the new tween for control of style.height.
        void RunHeightTween(VisualElement body, float from, float to, Action onComplete, Action onTick = null)
        {
            var token = new object();
            body.userData = token;
            double startTime = EditorApplication.timeSinceStartup;
            bool IsCurrent() => ReferenceEquals(body.userData, token);

            // onTick (e.g. SnapScrollToBottom) reads resolvedStyle off other elements, which only
            // reflects THIS tick's body.style.height write once UI Toolkit finishes its layout pass —
            // calling it inline in Tick() would read last frame's stale layout. GeometryChangedEvent
            // fires post-layout, so hook onTick there instead, scoped to this tween via the token.
            void OnGeometryChanged(GeometryChangedEvent evt)
            {
                if (IsCurrent())
                    onTick?.Invoke();
                else
                    body.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }
            if (onTick != null)
                body.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // onComplete itself further mutates body.style (height:Auto / display:None), which can
            // resolve a different — for nested/pre-opened content, taller — layout than the tween's
            // last lerped pixel value. Firing onTick's final scroll snap before that mutation settles
            // leaves it short. So: stop ticking, run onComplete, THEN wait for one more post-onComplete
            // geometry settle before the last onTick call and final unregister.
            void Finish()
            {
                onComplete?.Invoke();
                if (onTick == null)
                    return;

                void OnSettled(GeometryChangedEvent evt)
                {
                    body.UnregisterCallback<GeometryChangedEvent>(OnSettled);
                    body.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                    if (IsCurrent())
                        onTick();
                }
                body.RegisterCallback<GeometryChangedEvent>(OnSettled);
            }

            void Tick()
            {
                if (!IsCurrent())
                {
                    EditorApplication.update -= Tick;
                    if (onTick != null) body.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                    return;
                }

                double elapsed = EditorApplication.timeSinceStartup - startTime;
                float t = Mathf.Clamp01((float)(elapsed / SectionAnimDurationSeconds));
                body.style.height = Mathf.Lerp(from, to, t);
                Repaint();

                if (t >= 1f)
                {
                    EditorApplication.update -= Tick;
                    Finish();
                }
            }

            EditorApplication.update += Tick;
        }

        static void StopHeaderClickBubble(VisualElement element)
        {
            element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            element.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        }

        const float SectionHeaderToggleLabelWidth = 110f;
        const float SectionHeaderResetButtonWidth = 24f;

        // Shared layout for accordion-header toggles ("Enable" / "Apply on Create") so every section's
        // checkbox lands in the same column regardless of label text length.
        static Toggle AddSectionHeaderToggle(VisualElement header, string label, bool initial)
        {
            var toggle = new Toggle(label) { value = initial };
            PutCheckboxBeforeLabel(toggle);
            toggle.style.flexGrow = 0;
            toggle.style.flexShrink = 0;
            toggle.style.marginRight = 4;

            var toggleLabel = toggle.Q<Label>(className: "unity-base-field__label");
            if (toggleLabel != null)
            {
                toggleLabel.style.width = SectionHeaderToggleLabelWidth;
                toggleLabel.style.flexGrow = 0;
                toggleLabel.style.flexShrink = 0;
                toggleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                toggleLabel.style.marginLeft = 4;
            }

            StopHeaderClickBubble(toggle);
            header.Add(toggle);
            return toggle;
        }

        // Invisible stand-in matching the reset button's footprint, so headers without a reset
        // button still end at the same right edge as ones that have it.
        static void AddResetButtonPlaceholder(VisualElement header)
        {
            var placeholder = new Button { text = string.Empty };
            placeholder.AddToClassList("ygdr-settings-accent-btn");
            placeholder.AddToClassList("ygdr-settings-reset-btn");
            placeholder.style.width = SectionHeaderResetButtonWidth;
            placeholder.style.visibility = Visibility.Hidden;
            placeholder.pickingMode = PickingMode.Ignore;
            header.Add(placeholder);
        }

        Button MakeSettingsAccentButton(VisualElement parent, string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("ygdr-settings-accent-btn");
            StyleAccentButton(button);
            StopHeaderClickBubble(button);
            parent.Add(button);
            return button;
        }

        Button MakeSettingsResetButton(VisualElement parent, Action onClick)
        {
            var button = MakeSettingsAccentButton(parent, "↺", onClick);
            button.AddToClassList("ygdr-settings-reset-btn");
            return button;
        }

        static VisualElement MakeRow(VisualElement parent) => BuildRow("ygdr-settings-row", parent);

        // Chunked-row grid: each row holds exactly `columns` items with equal flexGrow, so the
        // column count is guaranteed regardless of label length — flex-wrap+percent-width was tried
        // and kept losing to Unity's internal base-field label min-width recompute.
        static VisualElement MakeToggleGrid(VisualElement parent, int columns)
        {
            var grid = new VisualElement();
            grid.AddToClassList("ygdr-settings-toggle-grid");
            grid.style.flexDirection = FlexDirection.Column;
            grid.style.width = Length.Percent(100);
            parent.Add(grid);
            return grid;
        }

        static VisualElement GetOrCreateGridRow(VisualElement grid, int columns)
        {
            var lastRow = grid.childCount > 0 ? grid[grid.childCount - 1] : null;
            if (lastRow != null && lastRow.childCount < columns)
                return lastRow;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.width = Length.Percent(100);
            grid.Add(row);
            return row;
        }

        static void PutCheckboxBeforeLabel(Toggle toggle)
        {
            var input = toggle.Q<VisualElement>(className: "unity-base-field__input");
            if (input == null)
                input = toggle.Q<VisualElement>(className: "unity-toggle__input");
            if (input != null)
            {
                toggle.Insert(0, input);
                input.style.flexGrow = 0;
                input.style.flexShrink = 0;
                input.style.flexBasis = StyleKeyword.Auto;
                input.style.minWidth = 0;
            }
        }

        static void StyleGridToggle(Toggle toggle)
        {
            PutCheckboxBeforeLabel(toggle);
            toggle.style.flexBasis = 0;
            toggle.style.minWidth = 0;
            toggle.style.flexGrow = 1;
            toggle.style.flexShrink = 1;
            toggle.style.overflow = Overflow.Hidden;
            toggle.style.marginRight = 6;
            var toggleLabel = toggle.Q<Label>(className: "unity-base-field__label");
            if (toggleLabel != null)
            {
                toggleLabel.style.minWidth = 0;
                toggleLabel.style.flexGrow = 0;
                toggleLabel.style.flexShrink = 1;
                toggleLabel.style.overflow = Overflow.Hidden;
                toggleLabel.style.textOverflow = TextOverflow.Ellipsis;
                toggleLabel.style.whiteSpace = WhiteSpace.NoWrap;
                toggleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                toggleLabel.style.marginLeft = 2;
            }
        }

        static Toggle AddGridToggle(VisualElement grid, int columns, string label, bool initial, Action<bool> onChanged)
        {
            var toggle = new Toggle(label) { value = initial };
            StyleGridToggle(toggle);
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            GetOrCreateGridRow(grid, columns).Add(toggle);
            return toggle;
        }

        /* Resetting sets the field's value, firing the same callback as a manual edit — onChanged is the single source of truth for applying a color. */
        ColorField MakeColorRow(VisualElement parent, string label, float labelWidth, Color initial, Color defaultColor, Action<Color> onChanged, Func<Color, Color> normalize = null)
        {
            var row = MakeRow(parent);
            row.style.width = Length.Percent(100);
            var labelElement = new Label(label);
            labelElement.style.width = labelWidth;
            labelElement.style.flexShrink = 0;
            row.Add(labelElement);

            var field = new ColorField { value = initial, showAlpha = false };
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            field.RegisterValueChangedCallback(evt =>
            {
                var applied = normalize != null ? normalize(evt.newValue) : evt.newValue;
                onChanged(applied);
                if (normalize != null) field.SetValueWithoutNotify(applied);
            });
            row.Add(field);

            MakeSettingsResetButton(row, () => field.value = defaultColor);
            return field;
        }

        // ── Interface palette ─────────────────────────────────────────────────

        void BuildInterfaceSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.interface"), () => _interfaceOpen, open => _interfaceOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            MakeSettingsResetButton(header, () =>
            {
                settings.ResetPalette();
                SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                settings.Save();
            });

            var localizationLabel = new Label(L10n.Get("settings.localization_label"));
            localizationLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(localizationLabel);

            var langRow = MakeRow(body);
            langRow.style.width = Length.Percent(100);
            var langLabel = new Label(L10n.Get("settings.language")); langLabel.style.width = 150;
            langLabel.style.flexShrink = 0;
            langRow.Add(langLabel);
            var langChoices = L10n.SupportedLanguageLabels.ToList();
            var langPopup = new PopupField<string>(langChoices, L10n.LanguageIndex);
            langPopup.style.flexGrow = 1;
            langPopup.style.minWidth = 0;
            langPopup.style.flexShrink = 1;
            langPopup.RegisterValueChangedCallback(evt =>
            {
                L10n.LanguageIndex = langChoices.IndexOf(evt.newValue);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            });
            langRow.Add(langPopup);

            var toggleGrid1 = MakeToggleGrid(body, 4);
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.layer_indicators"), settings.showLayerWDIndicator, v => { settings.showLayerWDIndicator = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.type_icons"),        settings.showParamTypeIcons,   v => { settings.showParamTypeIcons = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.vrc_icons"),         settings.showParamVrcIcons,    v => { settings.showParamVrcIcons = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.aap_icons"),         settings.showParamAapIcons,    v => { settings.showParamAapIcons = v; settings.Save(); });

            var toggleGrid2 = MakeToggleGrid(body, 4);
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.graph_footer"),   settings.showGraphFooter,            v => { settings.showGraphFooter = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.vrc_comp_icons"), settings.showParamVrcComponentIcons, v => { settings.showParamVrcComponentIcons = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.param_budget"),   settings.showParamBudget,            v => { settings.showParamBudget = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.empty_params"),   settings.showParamUnusedIcon,        v => { settings.showParamUnusedIcon = v; settings.Save(); });

            _paletteColorFields[0] = MakeColorRow(body, L10n.Get("settings.palette.primary"), 150, settings.paletteColorPrimary, AnimatorDefaultSettings.DefaultPrimary,
                c => { settings.paletteColorPrimary = c; SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent); settings.Save(); },
                ClampPaletteColor);
            _paletteColorFields[1] = MakeColorRow(body, L10n.Get("settings.palette.secondary"), 150, settings.paletteColorSecondary, AnimatorDefaultSettings.DefaultSecondary,
                c => { settings.paletteColorSecondary = c; SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent); settings.Save(); },
                ClampPaletteColor);
            _paletteColorFields[2] = MakeColorRow(body, L10n.Get("settings.palette.accent"), 150, settings.paletteColorAccent, AnimatorDefaultSettings.DefaultAccent,
                c => { settings.paletteColorAccent = c; SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent); settings.Save(); },
                ClampPaletteColor);

            var typeColorsLabel = new Label(L10n.Get("settings.palette.param_type_vrc_colors"));
            typeColorsLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(typeColorsLabel);

            var typeColorsGroup = new VisualElement();
            typeColorsGroup.SetEnabled(settings.showParamTypeIcons);
            body.Add(typeColorsGroup);
            _paletteColorFields[3] = MakeColorRow(typeColorsGroup, "Float",   150, settings.paramColorFloat,   new Color(0.35f, 0.75f, 0.35f, 1f), c => { settings.paramColorFloat = c; settings.Save(); });
            _paletteColorFields[4] = MakeColorRow(typeColorsGroup, "Int",     150, settings.paramColorInt,     new Color(0.35f, 0.60f, 1.00f, 1f), c => { settings.paramColorInt = c; settings.Save(); });
            _paletteColorFields[5] = MakeColorRow(typeColorsGroup, "Bool",    150, settings.paramColorBool,    new Color(1.00f, 0.55f, 0.20f, 1f), c => { settings.paramColorBool = c; settings.Save(); });
            _paletteColorFields[6] = MakeColorRow(typeColorsGroup, "Trigger", 150, settings.paramColorTrigger, new Color(0.85f, 0.30f, 0.85f, 1f), c => { settings.paramColorTrigger = c; settings.Save(); });

            var vrcColorGroup = new VisualElement();
            vrcColorGroup.SetEnabled(settings.showParamVrcIcons);
            body.Add(vrcColorGroup);
            _paletteColorFields[7] = MakeColorRow(vrcColorGroup, L10n.Get("settings.palette.vrc_label"), 150, settings.paramColorVrcLabel, Color.cyan, c => { settings.paramColorVrcLabel = c; settings.Save(); });

            var typeToggle = toggleGrid1[0].Children().ElementAt(1) as Toggle; // showParamTypeIcons toggle, re-hook to also gate the group
            typeToggle?.RegisterValueChangedCallback(evt => typeColorsGroup.SetEnabled(evt.newValue));
            var vrcToggle = toggleGrid1[0].Children().ElementAt(2) as Toggle; // showParamVrcIcons toggle
            vrcToggle?.RegisterValueChangedCallback(evt => vrcColorGroup.SetEnabled(evt.newValue));

            var analysisLabel = new Label(L10n.Get("settings.palette.graph_analysis"));
            analysisLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(analysisLabel);
            _paletteColorFields[8] = MakeColorRow(body, L10n.Get("settings.palette.analysis_highlight"), 150, settings.analysisHighlightColor, Color.red, c => { settings.analysisHighlightColor = c; settings.Save(); });
        }

        static Color ClampPaletteColor(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            value = EditorGUIUtility.isProSkin ? Mathf.Min(value, 0.40f) : Mathf.Max(value, 0.70f);
            var clamped = Color.HSVToRGB(hue, saturation, value);
            clamped.a = color.a;
            return clamped;
        }

        // ── Graph background + grid ───────────────────────────────────────────

        void BuildGraphGridSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.graph_background"), () => _graphGridOpen, open => _graphGridOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var enableToggle = AddSectionHeaderToggle(header, L10n.Get("settings.enable"), settings.graphGridOverride);
            MakeSettingsResetButton(header, () =>
            {
                settings.ResetGraphGrid();
                settings.Save();
                RebuildGraphGridBody(body, settings);
            });

            enableToggle.RegisterValueChangedCallback(evt =>
            {
                settings.graphGridOverride = evt.newValue;
                settings.Save();
                body.SetEnabled(evt.newValue);
            });

            RebuildGraphGridBody(body, settings);
            body.SetEnabled(settings.graphGridOverride);
        }

        void RebuildGraphGridBody(VisualElement body, AnimatorDefaultSettings settings)
        {
            body.Clear();

            var bgRow = MakeRow(body);
            var bgLabel = new Label(L10n.Get("settings.bg.background")); bgLabel.style.width = 110;
            bgRow.Add(bgLabel);

            var colorModeToggle = new Toggle(L10n.Get("settings.bg.color")) { value = !settings.graphGridUseImage };
            var imageModeToggle = new Toggle(L10n.Get("settings.bg.image")) { value = settings.graphGridUseImage };
            colorModeToggle.style.width = 55; imageModeToggle.style.width = 55;
            bgRow.Add(colorModeToggle);
            bgRow.Add(imageModeToggle);

            var bgFieldsContainer = new VisualElement();
            bgFieldsContainer.style.flexDirection = FlexDirection.Row;
            bgFieldsContainer.style.flexGrow = 1;
            bgFieldsContainer.style.minWidth = 0;
            bgRow.Add(bgFieldsContainer);

            void RebuildBgFields()
            {
                bgFieldsContainer.Clear();
                if (!settings.graphGridUseImage)
                {
                    _paletteColorFields[9] = MakeColorRow(bgFieldsContainer, "", 0, settings.graphGridBackgroundColor, new Color(0.18f, 0.18f, 0.18f, 1f),
                        c => { settings.graphGridBackgroundColor = c; settings.Save(); });
                }
                else
                {
                    _paletteColorFields[9] = null;
                    var texField = new ObjectField { objectType = typeof(Texture2D), value = settings.graphGridBackgroundImage };
                    texField.style.flexGrow = 1;
                    texField.style.minWidth = 0;
                    texField.RegisterValueChangedCallback(evt => { settings.graphGridBackgroundImage = evt.newValue as Texture2D; settings.Save(); });
                    bgFieldsContainer.Add(texField);

                    var opacitySlider = new Slider(0f, 1f) { value = settings.graphGridBackgroundImageOpacity };
                    opacitySlider.style.flexGrow = 1;
                    opacitySlider.style.minWidth = 0;
                    opacitySlider.RegisterValueChangedCallback(evt => { settings.graphGridBackgroundImageOpacity = evt.newValue; settings.Save(); });
                    bgFieldsContainer.Add(opacitySlider);
                }
            }
            RebuildBgFields();

            colorModeToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue) return;
                settings.graphGridUseImage = false;
                settings.Save();
                imageModeToggle.SetValueWithoutNotify(false);
                RebuildBgFields();
            });
            imageModeToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue) return;
                settings.graphGridUseImage = true;
                settings.Save();
                colorModeToggle.SetValueWithoutNotify(false);
                RebuildBgFields();
            });

            var gridRow = MakeRow(body);
            var gridLabel = new Label(L10n.Get("settings.bg.grid")); gridLabel.style.width = 110;
            gridRow.Add(gridLabel);
            var drawLinesToggle = new Toggle { value = settings.graphGridDrawLines };
            gridRow.Add(drawLinesToggle);

            var linesGroup = new VisualElement();
            linesGroup.SetEnabled(settings.graphGridDrawLines);
            body.Add(linesGroup);

            drawLinesToggle.RegisterValueChangedCallback(evt =>
            {
                settings.graphGridDrawLines = evt.newValue;
                settings.Save();
                linesGroup.SetEnabled(evt.newValue);
            });

            _paletteColorFields[10] = MakeColorRow(linesGroup, L10n.Get("settings.bg.major_grid"), 110, settings.graphGridColorMajor, new Color(0.30f, 0.30f, 0.30f, 1f), c => { settings.graphGridColorMajor = c; settings.Save(); });
            _paletteColorFields[11] = MakeColorRow(linesGroup, L10n.Get("settings.bg.minor_grid"), 110, settings.graphGridColorMinor, new Color(0.22f, 0.22f, 0.22f, 1f), c => { settings.graphGridColorMinor = c; settings.Save(); });

            var scaleRow = MakeRow(linesGroup);
            var scaleLabel = new Label(L10n.Get("settings.bg.grid_scale")); scaleLabel.style.width = 110;
            scaleRow.Add(scaleLabel);
            var scaleSlider = new Slider(1f, 3f) { value = settings.graphGridScalingMajor };
            scaleSlider.style.flexGrow = 1;
            scaleSlider.style.minWidth = 0;
            scaleSlider.RegisterValueChangedCallback(evt => { settings.graphGridScalingMajor = evt.newValue; settings.Save(); });
            scaleRow.Add(scaleSlider);

            var divisorRow = MakeRow(linesGroup);
            var divisorLabel = new Label(L10n.Get("settings.bg.minor_divisions")); divisorLabel.style.width = 110;
            divisorRow.Add(divisorLabel);
            var divisorSlider = new SliderInt(2, 10) { value = settings.graphGridDivisorMinor };
            divisorSlider.style.flexGrow = 1;
            divisorSlider.style.minWidth = 0;
            divisorSlider.RegisterValueChangedCallback(evt => { settings.graphGridDivisorMinor = evt.newValue; settings.Save(); });
            divisorRow.Add(divisorSlider);
        }

        // ── Node icon indicators ──────────────────────────────────────────────

        void BuildOverlaySection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.node_icons"), () => _nodeIconsOpen, open => _nodeIconsOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var enableToggle = AddSectionHeaderToggle(header, L10n.Get("settings.enable"), settings.overlayEnabled);
            AddResetButtonPlaceholder(header);

            var toggleGrid1 = MakeToggleGrid(body, 4);
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.overlay.loop_empty"), settings.overlayShowLoopEmpty, v => { settings.overlayShowLoopEmpty = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.overlay.clip_time"), settings.overlayShowClipTime,  v => { settings.overlayShowClipTime = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.overlay.wd"),        settings.overlayShowWD,        v => { settings.overlayShowWD = v; settings.Save(); });
            AddGridToggle(toggleGrid1, 4, L10n.Get("settings.overlay.behaviors"), settings.overlayShowB,         v => { settings.overlayShowB = v; settings.Save(); });

            var toggleGrid2 = MakeToggleGrid(body, 4);
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.overlay.coords"),    settings.overlayShowCoords,     v => { settings.overlayShowCoords = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.overlay.clip_name"), settings.overlayShowMotionName, v => { settings.overlayShowMotionName = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.overlay.motion"),    settings.overlayShowMotion,     v => { settings.overlayShowMotion = v; settings.Save(); });
            AddGridToggle(toggleGrid2, 4, L10n.Get("settings.overlay.speed"),     settings.overlayShowSpeed,      v => { settings.overlayShowSpeed = v; settings.Save(); });

            _paletteColorFields[22] = MakeColorRow(body, L10n.Get("settings.overlay.active"),   150, settings.overlayActiveColor,   Color.white, c => { settings.overlayActiveColor = c; settings.Save(); });
            _paletteColorFields[23] = MakeColorRow(body, L10n.Get("settings.overlay.inactive"), 150, settings.overlayInactiveColor, new Color(0.45f, 0.45f, 0.45f, 1f), c => { settings.overlayInactiveColor = c; settings.Save(); });

            body.SetEnabled(settings.overlayEnabled);
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                settings.overlayEnabled = evt.newValue;
                settings.Save();
                body.SetEnabled(evt.newValue);
            });
        }

        // ── Transition overlay ────────────────────────────────────────────────

        void BuildTransitionOverlaySection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.transition_overlay"), () => _transitionOverlayOpen, open => _transitionOverlayOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var enableToggle = AddSectionHeaderToggle(header, L10n.Get("settings.enable"), settings.transitionOverlayEnabled);
            AddResetButtonPlaceholder(header);

            var toggleGrid = MakeToggleGrid(body, 4);
            AddGridToggle(toggleGrid, 4, L10n.Get("settings.trans_overlay.labels"),           settings.transitionShowLabel,             v => { settings.transitionShowLabel = v; settings.Save(); });
            AddGridToggle(toggleGrid, 4, L10n.Get("settings.trans_overlay.selection_colors"), settings.transitionSelectionColorEnabled,  v => { settings.transitionSelectionColorEnabled = v; settings.Save(); });
            AddGridToggle(toggleGrid, 4, L10n.Get("settings.trans_overlay.indicator_arrows"), settings.transitionIndicatorArrowsEnabled, v => { settings.transitionIndicatorArrowsEnabled = v; settings.Save(); });
            AddGridToggle(toggleGrid, 4, L10n.Get("settings.trans_overlay.animate"),          settings.transitionAnimateSelected,        v => { settings.transitionAnimateSelected = v; settings.Save(); });

            _paletteColorFields[24] = MakeColorRow(body, L10n.Get("settings.trans_overlay.transition_line"), 150, settings.transitionOverlayColor, new Color(1f, 1f, 1f, 1f), c => { settings.transitionOverlayColor = c; settings.Save(); });

            var selectionGroup = new VisualElement();
            selectionGroup.SetEnabled(settings.transitionSelectionColorEnabled);
            body.Add(selectionGroup);
            _paletteColorFields[25] = MakeColorRow(selectionGroup, L10n.Get("settings.trans_overlay.selection_in"),  150, settings.transitionIncomingColor, new Color(0f, 1f, 1f, 1f), c => { settings.transitionIncomingColor = c; settings.Save(); });
            _paletteColorFields[26] = MakeColorRow(selectionGroup, L10n.Get("settings.trans_overlay.selection_out"), 150, settings.transitionOutgoingColor, new Color(1f, 0f, 1f, 1f), c => { settings.transitionOutgoingColor = c; settings.Save(); });

            var arrowGroup = new VisualElement();
            arrowGroup.SetEnabled(settings.transitionIndicatorArrowsEnabled);
            body.Add(arrowGroup);
            _paletteColorFields[27] = MakeColorRow(arrowGroup, L10n.Get("settings.trans_overlay.default_arrow"),      150, settings.transitionOverlayArrowColor,     new Color(0.6f, 0.6f, 0.6f, 1f),  c => { settings.transitionOverlayArrowColor = c; settings.Save(); });
            _paletteColorFields[28] = MakeColorRow(arrowGroup, L10n.Get("settings.trans_overlay.no_condition_arrow"), 150, settings.transitionArrowNoConditionColor, new Color(1f, 0.28f, 0f, 1f),     c => { settings.transitionArrowNoConditionColor = c; settings.Save(); });
            _paletteColorFields[29] = MakeColorRow(arrowGroup, L10n.Get("settings.trans_overlay.instant_arrow"),      150, settings.transitionArrowInstantColor,     new Color(0f, 0.25f, 0.66f, 1f), c => { settings.transitionArrowInstantColor = c; settings.Save(); });

            var selectionToggle = toggleGrid[0].Children().ElementAt(1) as Toggle;
            selectionToggle?.RegisterValueChangedCallback(evt => selectionGroup.SetEnabled(evt.newValue));
            var arrowsToggle = toggleGrid[0].Children().ElementAt(2) as Toggle;
            arrowsToggle?.RegisterValueChangedCallback(evt => arrowGroup.SetEnabled(evt.newValue));

            body.SetEnabled(settings.transitionOverlayEnabled);
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                settings.transitionOverlayEnabled = evt.newValue;
                settings.Save();
                body.SetEnabled(evt.newValue);
            });
        }

        // ── Node colors ───────────────────────────────────────────────────────

        void BuildNodeColorsSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.node_colors"), () => _nodeColorsOpen, open => _nodeColorsOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var enableToggle = AddSectionHeaderToggle(header, L10n.Get("settings.enable"), settings.nodeColorEnabled);
            MakeSettingsResetButton(header, () =>
            {
                settings.ResetNodeColors();
                settings.Save();
            });

            var styleRow = MakeRow(body);
            var styleLabel = new Label(L10n.Get("settings.node_colors.visual_style")); styleLabel.style.width = 150;
            styleRow.Add(styleLabel);
            var flat3DToggle = new Toggle(L10n.Get("settings.node_colors.flat_3d")) { value = settings.nodeColor3DEnabled };
            flat3DToggle.RegisterValueChangedCallback(evt =>
            {
                settings.nodeColor3DEnabled = evt.newValue;
                settings.Save();
                PatchNodeStyles.Invalidate();
            });
            styleRow.Add(flat3DToggle);

            _paletteColorFields[12] = MakeColorRow(body, L10n.Get("settings.node_colors.selection_highlight"), 150, settings.nodeSelectionColor, new(1f, 1f, 1f, 1f), c => { settings.nodeSelectionColor = c; settings.Save(); });
            _paletteColorFields[13] = MakeColorRow(body, L10n.Get("settings.node_colors.state_nodes"),       150, settings.stateNodeColor,       new(0.30f, 0.30f, 0.30f, 1f), c => { settings.stateNodeColor = c; settings.Save(); });
            _paletteColorFields[14] = MakeColorRow(body, L10n.Get("settings.node_colors.default_state"),     150, settings.defaultStateColor,    new(0.60f, 0.35f, 0.10f, 1f), c => { settings.defaultStateColor = c; settings.Save(); });
            _paletteColorFields[15] = MakeColorRow(body, L10n.Get("settings.node_colors.sub_state_machine"), 150, settings.subStateMachineColor, new(0.35f, 0.25f, 0.50f, 1f), c => { settings.subStateMachineColor = c; settings.Save(); });
            _paletteColorFields[16] = MakeColorRow(body, L10n.Get("settings.node_colors.entry_node"),        150, settings.entryNodeColor,       new(0.20f, 0.55f, 0.20f, 1f), c => { settings.entryNodeColor = c; settings.Save(); });
            _paletteColorFields[17] = MakeColorRow(body, L10n.Get("settings.node_colors.exit_node"),         150, settings.exitNodeColor,        new(0.55f, 0.15f, 0.15f, 1f), c => { settings.exitNodeColor = c; settings.Save(); });
            _paletteColorFields[18] = MakeColorRow(body, L10n.Get("settings.node_colors.any_state"),         150, settings.anyStateNodeColor,    new(0.15f, 0.40f, 0.50f, 1f), c => { settings.anyStateNodeColor = c; settings.Save(); });
            _paletteColorFields[19] = MakeColorRow(body, L10n.Get("settings.node_colors.blend_tree_direct"), 150, settings.blendTreeDirectNodeColor, new(0.70f, 0.37f, 0.20f, 1f), c => { settings.blendTreeDirectNodeColor = c; settings.Save(); });
            _paletteColorFields[20] = MakeColorRow(body, L10n.Get("settings.node_colors.blend_tree_1d"),     150, settings.blendTree1DNodeColor,     new(0.24f, 0.50f, 0.60f, 1f), c => { settings.blendTree1DNodeColor = c; settings.Save(); });
            _paletteColorFields[21] = MakeColorRow(body, L10n.Get("settings.node_colors.blend_tree_2d"),     150, settings.blendTree2DNodeColor,     new(0.24f, 0.60f, 0.45f, 1f), c => { settings.blendTree2DNodeColor = c; settings.Save(); });

            body.SetEnabled(settings.nodeColorEnabled);
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                settings.nodeColorEnabled = evt.newValue;
                settings.Save();
                body.SetEnabled(evt.newValue);
            });
        }

        // ── Transition defaults ───────────────────────────────────────────────

        void BuildTransitionDefaultsSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.transition_defaults"), () => _transitionDefaultsOpen, open => _transitionDefaultsOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var applyToggle = AddSectionHeaderToggle(header, L10n.Get("settings.apply_on_create"), settings.applyToTransitions);
            AddResetButtonPlaceholder(header);

            var exitRow = MakeRow(body);
            var hasExitLabel = new Label(L10n.Get("transitions.has_exit_time")); hasExitLabel.style.width = 160;
            exitRow.Add(hasExitLabel);
            var hasExitToggle = new Toggle { value = settings.transHasExitTime };
            hasExitToggle.RegisterValueChangedCallback(evt => { settings.transHasExitTime = evt.newValue; settings.Save(); });
            exitRow.Add(hasExitToggle);
            var exitFlex = new VisualElement(); exitFlex.style.flexGrow = 1; exitRow.Add(exitFlex);
            var exitTimeLabel = new Label(L10n.Get("transitions.exit_time")); exitTimeLabel.style.width = 120;
            exitRow.Add(exitTimeLabel);
            var exitTimeField = new FloatField { value = settings.transExitTime };
            exitTimeField.RegisterValueChangedCallback(evt => { settings.transExitTime = evt.newValue; settings.Save(); });
            exitRow.Add(exitTimeField);

            var durationRow = MakeRow(body);
            var hasFixedLabel = new Label(L10n.Get("transitions.has_fixed_duration")); hasFixedLabel.style.width = 160;
            durationRow.Add(hasFixedLabel);
            var hasFixedToggle = new Toggle { value = settings.transHasFixedDuration };
            hasFixedToggle.RegisterValueChangedCallback(evt => { settings.transHasFixedDuration = evt.newValue; settings.Save(); });
            durationRow.Add(hasFixedToggle);
            var durationFlex = new VisualElement(); durationFlex.style.flexGrow = 1; durationRow.Add(durationFlex);
            var durationLabel = new Label(L10n.Get("transitions.duration")); durationLabel.style.width = 120;
            durationRow.Add(durationLabel);
            var durationField = new FloatField { value = settings.transDuration };
            durationField.RegisterValueChangedCallback(evt => { settings.transDuration = evt.newValue; settings.Save(); });
            durationRow.Add(durationField);

            var offsetRow = MakeRow(body);
            var offsetLabel = new Label(L10n.Get("transitions.offset")); offsetLabel.style.width = 160;
            offsetRow.Add(offsetLabel);
            var offsetField = new FloatField { value = settings.transOffset };
            offsetField.style.flexGrow = 1;
            offsetField.RegisterValueChangedCallback(evt => { settings.transOffset = evt.newValue; settings.Save(); });
            offsetRow.Add(offsetField);

            var interruptionRow = MakeRow(body);
            var interruptionLabel = new Label(L10n.Get("transitions.interruption_source")); interruptionLabel.style.width = 160;
            interruptionRow.Add(interruptionLabel);
            var interruptionChoices = new List<string>
            {
                L10n.Get("transitions.interruption.none"), L10n.Get("transitions.interruption.source"),
                L10n.Get("transitions.interruption.destination"), L10n.Get("transitions.interruption.source_then_destination"),
                L10n.Get("transitions.interruption.destination_then_source")
            };
            var interruptionPopup = new PopupField<string>(interruptionChoices, (int)settings.transInterruptionSource);
            interruptionPopup.style.flexGrow = 1;
            interruptionPopup.RegisterValueChangedCallback(evt => { settings.transInterruptionSource = (TransitionInterruptionSource)interruptionChoices.IndexOf(evt.newValue); settings.Save(); });
            interruptionRow.Add(interruptionPopup);

            var orderedRow = MakeRow(body);
            var orderedLabel = new Label(L10n.Get("transitions.ordered_interruption")); orderedLabel.style.width = 160;
            orderedRow.Add(orderedLabel);
            var orderedToggle = new Toggle { value = settings.transOrderedInterruption };
            orderedToggle.RegisterValueChangedCallback(evt => { settings.transOrderedInterruption = evt.newValue; settings.Save(); });
            orderedRow.Add(orderedToggle);
            var orderedFlex = new VisualElement(); orderedFlex.style.flexGrow = 1; orderedRow.Add(orderedFlex);
            var muteLabel = new Label(L10n.Get("transitions.mute")); muteLabel.style.width = 80;
            orderedRow.Add(muteLabel);
            var muteToggle = new Toggle { value = settings.transMute };
            muteToggle.RegisterValueChangedCallback(evt => { settings.transMute = evt.newValue; settings.Save(); });
            orderedRow.Add(muteToggle);

            var soloRow = MakeRow(body);
            var soloLabel = new Label(L10n.Get("transitions.solo")); soloLabel.style.width = 160;
            soloRow.Add(soloLabel);
            var soloToggle = new Toggle { value = settings.transSolo };
            soloToggle.RegisterValueChangedCallback(evt => { settings.transSolo = evt.newValue; settings.Save(); });
            soloRow.Add(soloToggle);

            var writeDefaultsRow = MakeRow(body);
            var writeDefaultsLabel = new Label($"{L10n.Get("states.write_defaults")} ({L10n.Get("settings.section.state_defaults")})"); writeDefaultsLabel.style.width = 160;
            writeDefaultsRow.Add(writeDefaultsLabel);
            var writeDefaultsToggle = new Toggle { value = settings.stateWriteDefaultValues };
            writeDefaultsToggle.RegisterValueChangedCallback(evt => { settings.stateWriteDefaultValues = evt.newValue; settings.Save(); });
            writeDefaultsRow.Add(writeDefaultsToggle);

            body.SetEnabled(settings.applyToTransitions);
            applyToggle.RegisterValueChangedCallback(evt =>
            {
                settings.applyToTransitions = evt.newValue;
                settings.Save();
                body.SetEnabled(evt.newValue);
            });
        }

        // ── Miscellaneous ─────────────────────────────────────────────────────

        static readonly char[] ClipMenuNestingDelimiters = { '-', '.', '_' };

        void BuildMiscellaneousSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.miscellaneous"), () => _miscOpen, open => _miscOpen = open);

            var grid1 = MakeToggleGrid(body, 3);
            AddGridToggle(grid1, 3, L10n.Get("settings.misc.wd_blend_trees"),       settings.wdIncludeBlendTreeStates, v => { settings.wdIncludeBlendTreeStates = v; settings.Save(); });
            AddGridToggle(grid1, 3, L10n.Get("settings.misc.prevent_layer_scroll"), settings.preventLayerScroll,       v => { settings.preventLayerScroll = v; settings.Save(); });
            AddGridToggle(grid1, 3, L10n.Get("settings.misc.prevent_param_scroll"), settings.preventParameterScroll,   v => { settings.preventParameterScroll = v; settings.Save(); });

            var grid2 = MakeToggleGrid(body, 3);
            AddGridToggle(grid2, 3, L10n.Get("settings.misc.layer_weight_1"),   settings.newLayerWeightOne,          v => { settings.newLayerWeightOne = v; settings.Save(); });
            AddGridToggle(grid2, 3, L10n.Get("settings.misc.layer_templates"),  settings.layerTemplateButtonEnabled, v => { settings.layerTemplateButtonEnabled = v; settings.Save(); });
            AddGridToggle(grid2, 3, L10n.Get("settings.misc.param_add_menu"),   settings.parameterAddMenuEnabled,    v => { settings.parameterAddMenuEnabled = v; settings.Save(); });

            var grid3 = MakeToggleGrid(body, 3);
            AddGridToggle(grid3, 3, L10n.Get("settings.misc.frames"), settings.framesEnabled,
                v => { settings.framesEnabled = v; settings.Save(); });
            AddGridToggle(grid3, 3, L10n.Get("settings.misc.inspector_mode"), settings.inspectorModeEnabled,
                v => { settings.inspectorModeEnabled = v; settings.Save(); ApplyInspectorModeTabs(); });

            // Ghost 3rd column — same grid row, no toggle — so the 2 real toggles align with the
            // 3-column grids above (grid1/grid2) instead of splitting the row 50/50.
            var row3Ghost = new VisualElement();
            row3Ghost.style.flexBasis = 0;
            row3Ghost.style.flexGrow = 1;
            row3Ghost.style.flexShrink = 1;
            row3Ghost.style.minWidth = 0;
            row3Ghost.style.marginRight = 6;
            GetOrCreateGridRow(grid3, 3).Add(row3Ghost);

            var row4 = MakeRow(body);
            var clipGroup = new VisualElement();
            clipGroup.AddToClassList("ygdr-settings-clip-group-border");
            clipGroup.style.flexDirection = FlexDirection.Row;
            clipGroup.style.flexBasis = 0;
            clipGroup.style.flexGrow = 1;
            clipGroup.style.flexShrink = 1;
            clipGroup.style.minWidth = 0;
            row4.Add(clipGroup);

            var nestingToggle = new Toggle(L10n.Get("settings.misc.clip_menu_nesting")) { value = settings.clipMenuNestingEnabled };
            PutCheckboxBeforeLabel(nestingToggle);
            nestingToggle.style.flexGrow = 1;
            nestingToggle.style.flexShrink = 1;
            nestingToggle.style.minWidth = 0;
            nestingToggle.style.overflow = Overflow.Hidden;
            var nestingToggleLabel = nestingToggle.Q<Label>(className: "unity-base-field__label");
            if (nestingToggleLabel != null)
            {
                nestingToggleLabel.style.minWidth = 0;
                nestingToggleLabel.style.flexGrow = 0;
                nestingToggleLabel.style.flexShrink = 1;
                nestingToggleLabel.style.overflow = Overflow.Hidden;
                nestingToggleLabel.style.textOverflow = TextOverflow.Ellipsis;
                nestingToggleLabel.style.whiteSpace = WhiteSpace.NoWrap;
                nestingToggleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                nestingToggleLabel.style.marginLeft = 2;
            }
            clipGroup.Add(nestingToggle);

            var delimiterRow = new VisualElement();
            delimiterRow.style.flexDirection = FlexDirection.Row;
            delimiterRow.style.flexGrow = 0;
            delimiterRow.style.flexShrink = 0;
            delimiterRow.SetEnabled(settings.clipMenuNestingEnabled);
            clipGroup.Add(delimiterRow);

            nestingToggle.RegisterValueChangedCallback(evt =>
            {
                settings.clipMenuNestingEnabled = evt.newValue;
                settings.Save();
                delimiterRow.SetEnabled(evt.newValue);
            });

            _delimiterButtons.Clear();
            foreach (char delimiter in ClipMenuNestingDelimiters)
            {
                char capturedDelimiter = delimiter;
                var button = new Button(() =>
                {
                    settings.clipMenuNestingDelimiter = capturedDelimiter;
                    settings.Save();
                    RefreshDelimiterButtonColors();
                }) { text = capturedDelimiter.ToString() };
                button.AddToClassList("ygdr-settings-delimiter-btn");
                button.style.width = 24;
                button.style.marginRight = 4;
                delimiterRow.Add(button);
                _delimiterButtons[capturedDelimiter] = button;
            }
            RefreshDelimiterButtonColors();

            var palettesLabel = new Label(L10n.Get("settings.misc.palettes"));
            palettesLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(palettesLabel);

            var savePaletteButton = MakeSettingsAccentButton(body, L10n.Get("settings.misc.save_palette"), () =>
            {
                settings.savedPalettes.Add(new AnimatorPalette { encodedColors = AnimatorDefaultSettings.EncodePalette(AnimatorDefaultSettings.CapturePaletteColors(settings)) });
                settings.Save();
                RebuildSavedPalettesList(settings);
            });
            savePaletteButton.style.flexGrow = 1;
            savePaletteButton.style.marginLeft = 0;

            _savedPalettesListContainer = new VisualElement();
            body.Add(_savedPalettesListContainer);
            RebuildSavedPalettesList(settings);

            var colorTagsLabel = new Label(L10n.Get("settings.misc.color_tags"));
            colorTagsLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(colorTagsLabel);

            _colorTagsListContainer = new VisualElement();
            body.Add(_colorTagsListContainer);
            RebuildColorTagsList(settings);

            var addTagButton = MakeSettingsAccentButton(body, L10n.Get("settings.misc.add_color_tag"), () =>
            {
                settings.colorTags.Add(new AnimatorColorTag());
                settings.Save();
                RebuildColorTagsList(settings);
            });
            addTagButton.style.flexGrow = 1;
            addTagButton.style.marginLeft = 0;

            var compatLabel = new Label(L10n.Get("settings.misc.compatibility"));
            compatLabel.AddToClassList("ygdr-settings-bold-label");
            body.Add(compatLabel);
            var compatDesc = new Label(L10n.Get("settings.misc.compatibility_desc"));
            compatDesc.AddToClassList("ygdr-settings-desc-label");
            body.Add(compatDesc);

            var compatGrid = MakeToggleGrid(body, 2);
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.ContextMenuId,   L10n.Get("settings.misc.context_menus"),      L10n.Get("settings.misc.tt.context_menus"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.NodeOverlayId,   L10n.Get("settings.misc.node_overlay"),       L10n.Get("settings.misc.tt.node_overlay"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.NodeColorId,     L10n.Get("settings.misc.node_colors_feat"),   L10n.Get("settings.misc.tt.node_colors"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.TransitionId,    L10n.Get("settings.misc.transition_overlay"), L10n.Get("settings.misc.tt.transition_overlay"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.GraphInteractId, L10n.Get("settings.misc.graph_interaction"),  L10n.Get("settings.misc.tt.graph_interaction"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.GridBgId,        L10n.Get("settings.misc.grid_background"),   L10n.Get("settings.misc.tt.grid_background"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.LayerViewId,     L10n.Get("settings.misc.layer_view"),        L10n.Get("settings.misc.tt.layer_view"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.ParamViewId,     L10n.Get("settings.misc.parameter_view"),    L10n.Get("settings.misc.tt.parameter_view"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.BlendTreeId,     L10n.Get("settings.misc.blend_tree_feat"),   L10n.Get("settings.misc.tt.blend_tree"));
            AddFeatureToggle(compatGrid, 2, FeatureHarmony.BottomBarId,     L10n.Get("settings.misc.bottom_bar"),        L10n.Get("settings.misc.tt.bottom_bar"));
        }

        void RefreshDelimiterButtonColors()
        {
            if (_delimiterButtons.Count == 0) return;
            var settings = AnimatorDefaultSettings.Load();
            var accent = SharedWindowStyles.AccentColor;
            foreach (var pair in _delimiterButtons)
            {
                bool active = settings.clipMenuNestingDelimiter == pair.Key;
                pair.Value.style.backgroundColor = active ? accent : new Color(accent.r, accent.g, accent.b, 0.25f);
            }
        }

        void RebuildSavedPalettesList(AnimatorDefaultSettings settings)
        {
            if (_savedPalettesListContainer == null) return;
            _savedPalettesListContainer.Clear();

            for (int i = 0; i < settings.savedPalettes.Count; i++)
            {
                int index = i;
                var palette = settings.savedPalettes[index];
                var row = MakeRow(_savedPalettesListContainer);

                var nameField = new TextField { value = palette.slotName };
                nameField.style.width = Length.Percent(25);
                nameField.RegisterValueChangedCallback(evt => { palette.slotName = evt.newValue; settings.Save(); });
                row.Add(nameField);

                var swatch = new VisualElement();
                swatch.AddToClassList("ygdr-settings-palette-swatch");
                if (AnimatorDefaultSettings.TryDecodePalette(palette.encodedColors, out var previewColors))
                {
                    foreach (var previewColor in previewColors)
                    {
                        var cell = new VisualElement();
                        cell.AddToClassList("ygdr-settings-swatch-cell");
                        cell.style.backgroundColor = previewColor;
                        swatch.Add(cell);
                    }
                }
                else
                {
                    swatch.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                swatch.RegisterCallback<PointerDownEvent>(_ =>
                {
                    if (!AnimatorDefaultSettings.TryDecodePalette(palette.encodedColors, out var applyColors)) return;
                    AnimatorDefaultSettings.ApplyPaletteColors(settings, applyColors);
                    SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                });
                row.Add(swatch);

                var copyButton = MakeSettingsAccentButton(row, L10n.Get("settings.misc.copy_palette"), () => EditorGUIUtility.systemCopyBuffer = palette.encodedColors);
                copyButton.style.width = 90;

                var deleteButton = MakeSettingsAccentButton(row, "−", () =>
                {
                    settings.savedPalettes.RemoveAt(index);
                    settings.Save();
                    RebuildSavedPalettesList(settings);
                });
                deleteButton.style.width = SettingsRowDeleteButtonWidth;
            }

            var importRow = MakeRow(_savedPalettesListContainer);
            var importField = new TextField { value = _paletteImportText };
            importField.style.flexGrow = 1;
            importField.RegisterValueChangedCallback(evt => _paletteImportText = evt.newValue);
            importRow.Add(importField);

            var applyButton = MakeSettingsAccentButton(importRow, L10n.Get("settings.misc.apply_palette"), () =>
            {
                if (!AnimatorDefaultSettings.TryDecodePalette(_paletteImportText, out var importedColors)) return;
                AnimatorDefaultSettings.ApplyPaletteColors(settings, importedColors);
                SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                settings.Save();
                _paletteImportText = "";
                importField.SetValueWithoutNotify("");
            });
            applyButton.style.width = 90;

            // Dummy 3rd column matching the delete-button slot in the palette rows above, so
            // Apply Palette's right edge lines up with Copy's regardless of row/window width.
            var deleteButtonColumnSpacer = new Button { text = string.Empty };
            deleteButtonColumnSpacer.AddToClassList("ygdr-settings-accent-btn");
            deleteButtonColumnSpacer.style.width = SettingsRowDeleteButtonWidth;
            deleteButtonColumnSpacer.style.visibility = Visibility.Hidden;
            deleteButtonColumnSpacer.pickingMode = PickingMode.Ignore;
            importRow.Add(deleteButtonColumnSpacer);
        }

        void RebuildColorTagsList(AnimatorDefaultSettings settings)
        {
            if (_colorTagsListContainer == null) return;
            _colorTagsListContainer.Clear();

            for (int i = 0; i < settings.colorTags.Count; i++)
            {
                int index = i;
                var tag = settings.colorTags[index];
                var row = MakeRow(_colorTagsListContainer);
                row.style.width = Length.Percent(100);

                var nameField = new TextField { value = tag.tagName };
                nameField.style.flexGrow = 1;
                nameField.style.flexShrink = 1;
                nameField.style.minWidth = 0;
                nameField.RegisterValueChangedCallback(evt => { tag.tagName = evt.newValue; settings.Save(); });
                row.Add(nameField);

                var colorField = new ColorField { value = tag.color, showAlpha = false };
                colorField.style.flexGrow = 0;
                colorField.style.flexShrink = 0;
                colorField.style.width = 60;
                colorField.RegisterValueChangedCallback(evt => { tag.color = evt.newValue; settings.Save(); });
                row.Add(colorField);

                var removeButton = MakeSettingsAccentButton(row, "−", () =>
                {
                    settings.colorTags.RemoveAt(index);
                    settings.Save();
                    RebuildColorTagsList(settings);
                });
                removeButton.style.flexShrink = 0;
                removeButton.style.width = 24;
            }
        }

        static void AddFeatureToggle(VisualElement grid, int columns, string featureId, string label, string tooltip)
        {
            bool current = FeatureHarmony.IsEnabled(featureId);
            var toggle = new Toggle(label) { value = current, tooltip = tooltip };
            StyleGridToggle(toggle);
            toggle.RegisterValueChangedCallback(evt =>
            {
                FeatureHarmony.SetEnabled(featureId, evt.newValue);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            });

            // Patching is deferred a few frames past startup (see AnimatorEditorInit.DoPatches), so a
            // toggle built before that runs would otherwise freeze on a stale "off" snapshot forever.
            void OnFeatureChanged(string changedId)
            {
                if (changedId == featureId)
                    toggle.SetValueWithoutNotify(FeatureHarmony.IsEnabled(featureId));
            }
            toggle.RegisterCallback<AttachToPanelEvent>(_ => FeatureHarmony.Changed += OnFeatureChanged);
            toggle.RegisterCallback<DetachFromPanelEvent>(_ => FeatureHarmony.Changed -= OnFeatureChanged);

            GetOrCreateGridRow(grid, columns).Add(toggle);
        }

        // ── Keybindings ───────────────────────────────────────────────────────

        void BuildKeybindsSection(VisualElement parent, AnimatorDefaultSettings settings)
        {
            var body = new VisualElement();
            var header = BuildSettingsSectionHeader(parent, body, L10n.Get("settings.section.keybindings"), () => _keybindsOpen, open => _keybindsOpen = open);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            MakeSettingsResetButton(header, () =>
            {
                settings.ResetKeybinds();
                settings.Save();
                _recordingActionId = null;
                RebuildKeybindsBody();
            });

            _keybindsBodyRef = body;
            RebuildKeybindsBody();

            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeybindKeyDown, TrickleDown.TrickleDown);
        }

        void OnKeybindKeyDown(KeyDownEvent evt)
        {
            if (_recordingActionId == null) return;
            var settings = AnimatorDefaultSettings.Load();

            if (evt.keyCode == KeyCode.Escape)
            {
                _recordingActionId = null;
                evt.StopPropagation();
                RebuildKeybindsBody();
                return;
            }
            if (IsModifierKey(evt.keyCode)) return;

            ApplyKeybind(settings, _recordingActionId, new KeyBinding(evt.keyCode, evt.ctrlKey, evt.shiftKey, evt.altKey));
            settings.Save();
            _recordingActionId = null;
            evt.StopPropagation();
            RebuildKeybindsBody();
        }

        void RebuildKeybindsBody()
        {
            if (_keybindsBodyRef == null) return;
            var settings = AnimatorDefaultSettings.Load();
            _keybindsBodyRef.Clear();

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            _keybindsBodyRef.Add(columns);

            var col1 = new VisualElement();
            columns.Add(col1);
            AddBindingRow(col1, L10n.Get("settings.kb.select_incoming"),        "kbSelectIncoming",       settings.kbSelectIncoming,       settings);
            AddBindingRow(col1, L10n.Get("settings.kb.select_outgoing"),        "kbSelectOutgoing",       settings.kbSelectOutgoing,       settings);
            AddBindingRow(col1, L10n.Get("settings.kb.select_both"),            "kbSelectBoth",           settings.kbSelectBoth,           settings);
            AddBindingRow(col1, L10n.Get("settings.kb.select_all_nodes"),       "kbSelectAll",            settings.kbSelectAll,            settings);
            AddBindingRow(col1, L10n.Get("settings.kb.select_all_transitions"), "kbSelectAllTransitions", settings.kbSelectAllTransitions, settings);
            AddBindingRow(col1, L10n.Get("settings.kb.copy"),                   "kbCopy",                 settings.kbCopy,                 settings);
            AddBindingRow(col1, L10n.Get("settings.kb.paste"),                  "kbPaste",                settings.kbPaste,                settings);

            var col2 = new VisualElement();
            col2.style.marginLeft = 8;
            columns.Add(col2);
            AddBindingRow(col2, L10n.Get("settings.kb.duplicate"),           "kbDuplicate",          settings.kbDuplicate,          settings);
            AddBindingRow(col2, L10n.Get("settings.kb.chain_mode"),          "kbChainMode",          settings.kbChainMode,          settings);
            AddBindingRow(col2, L10n.Get("settings.kb.fan_mode"),            "kbFanMode",            settings.kbFanMode,            settings);
            AddBindingRow(col2, L10n.Get("settings.kb.multi_transition"),    "kbMultiTransition",    settings.kbMultiTransition,    settings);
            AddBindingRow(col2, L10n.Get("settings.kb.reverse_transitions"), "kbReverseTransitions", settings.kbReverseTransitions, settings);
            AddBindingRow(col2, L10n.Get("settings.kb.replicate"),           "kbReplicate",          settings.kbReplicate,          settings);
            AddBindingRow(col2, L10n.Get("settings.kb.redirect"),            "kbRedirect",           settings.kbRedirect,           settings);
        }

        void AddBindingRow(VisualElement parent, string label, string actionId, KeyBinding binding, AnimatorDefaultSettings settings, float labelWidth = 125, float btnWidth = 85)
        {
            var row = MakeRow(parent);
            var labelElement = new Label(label); labelElement.style.width = labelWidth;
            row.Add(labelElement);

            bool isRecording = _recordingActionId == actionId;
            var bindButton = MakeSettingsAccentButton(row, isRecording ? L10n.Get("settings.kb.press_key") : binding.Label(), () =>
            {
                bool startingRecording = !isRecording;
                _recordingActionId = startingRecording ? actionId : null;
                RebuildKeybindsBody();
                if (startingRecording)
                    _keybindsBodyRef?.Q<Button>(actionId)?.Focus();
            });
            bindButton.name = actionId;
            bindButton.style.width = btnWidth;

            if (!isRecording)
            {
                MakeSettingsResetButton(row, () =>
                {
                    var defaults = new AnimatorDefaultSettings();
                    ApplyKeybind(settings, actionId, GetDefaultBinding(defaults, actionId));
                    settings.Save();
                    RebuildKeybindsBody();
                });
            }
        }

        void ApplyKeybind(AnimatorDefaultSettings settings, string actionId, KeyBinding binding)
        {
            switch (actionId)
            {
                case "kbSelectIncoming":       settings.kbSelectIncoming       = binding; break;
                case "kbSelectOutgoing":       settings.kbSelectOutgoing       = binding; break;
                case "kbSelectBoth":           settings.kbSelectBoth           = binding; break;
                case "kbSelectAll":            settings.kbSelectAll            = binding; break;
                case "kbSelectAllTransitions": settings.kbSelectAllTransitions = binding; break;
                case "kbCopy":                 settings.kbCopy                 = binding; break;
                case "kbPaste":                settings.kbPaste                = binding; break;
                case "kbDuplicate":            settings.kbDuplicate            = binding; break;
                case "kbChainMode":            settings.kbChainMode            = binding; break;
                case "kbFanMode":              settings.kbFanMode              = binding; break;
                case "kbMultiTransition":      settings.kbMultiTransition      = binding; break;
                case "kbReverseTransitions":   settings.kbReverseTransitions   = binding; break;
                case "kbReplicate":            settings.kbReplicate            = binding; break;
                case "kbRedirect":             settings.kbRedirect             = binding; break;
            }
        }

        static bool IsModifierKey(KeyCode key) => key is
            KeyCode.LeftControl  or KeyCode.RightControl  or
            KeyCode.LeftAlt      or KeyCode.RightAlt      or
            KeyCode.LeftShift    or KeyCode.RightShift    or
            KeyCode.LeftCommand  or KeyCode.RightCommand  or
            KeyCode.LeftWindows  or KeyCode.RightWindows;

        KeyBinding GetDefaultBinding(AnimatorDefaultSettings defaults, string actionId) => actionId switch
        {
            "kbSelectIncoming"       => defaults.kbSelectIncoming,
            "kbSelectOutgoing"       => defaults.kbSelectOutgoing,
            "kbSelectBoth"           => defaults.kbSelectBoth,
            "kbSelectAll"            => defaults.kbSelectAll,
            "kbSelectAllTransitions" => defaults.kbSelectAllTransitions,
            "kbCopy"                 => defaults.kbCopy,
            "kbPaste"                => defaults.kbPaste,
            "kbDuplicate"            => defaults.kbDuplicate,
            "kbChainMode"            => defaults.kbChainMode,
            "kbFanMode"              => defaults.kbFanMode,
            "kbMultiTransition"      => defaults.kbMultiTransition,
            "kbReverseTransitions"   => defaults.kbReverseTransitions,
            "kbReplicate"            => defaults.kbReplicate,
            "kbRedirect"             => defaults.kbRedirect,
            _ => default,
        };
    }
}
#endif
