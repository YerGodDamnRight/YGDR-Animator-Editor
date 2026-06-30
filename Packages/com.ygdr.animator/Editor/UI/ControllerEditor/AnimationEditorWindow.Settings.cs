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
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
        bool _stateDefaultsOpen;
        bool _miscOpen;
        bool _keybindsOpen;
        string _recordingActionId;
        string _paletteImportText = "";

        void DrawSettingsTab()
        {
            var settings = AnimatorDefaultSettings.Load();
            DrawInterfaceSection(settings);
            EditorGUILayout.Space(4);
            DrawGraphGridSection(settings);
            EditorGUILayout.Space(4);
            DrawNodeColorsSection(settings);
            EditorGUILayout.Space(4);
            DrawOverlaySection(settings);
            EditorGUILayout.Space(4);
            DrawTransitionOverlaySection(settings);
            EditorGUILayout.Space(4);
            DrawTransitionDefaultsSection(settings);
            EditorGUILayout.Space(4);
            DrawStateDefaultsSection(settings);
            EditorGUILayout.Space(4);
            DrawKeybindsSection(settings);
            EditorGUILayout.Space(4);
            DrawMiscellaneousSection(settings);
        }

        // ── Interface palette ─────────────────────────────────────────────────

        void DrawInterfaceSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_interfaceOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.interface"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _interfaceOpen = !_interfaceOpen;
                GUILayout.FlexibleSpace();
                if (DrawResetBtn(24f))
                {
                    settings.ResetPalette();
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
            }

            if (!_interfaceOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            EditorGUILayout.LabelField(L10n.Get("settings.localization_label"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(L10n.Get("settings.language"), GUILayout.Width(150));
                EditorGUI.BeginChangeCheck();
                int newLanguageIndex = EditorGUILayout.Popup(L10n.LanguageIndex, L10n.SupportedLanguageLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    L10n.LanguageIndex = newLanguageIndex;
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }
            EditorGUILayout.Space(6);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            var ifRow1Rect = EditorGUILayout.GetControlRect(false, lineHeight);
            var ifRow2Rect = EditorGUILayout.GetControlRect(false, lineHeight);
            float ifColWidth = ifRow1Rect.width / 4f;

            DrawOverlayToggle(new Rect(ifRow1Rect.x + 0 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), L10n.Get("settings.layer_indicators"), ref settings.showLayerWDIndicator,       settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 1 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), L10n.Get("settings.type_icons"),        ref settings.showParamTypeIcons,         settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 2 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), L10n.Get("settings.vrc_icons"),         ref settings.showParamVrcIcons,          settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 3 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), L10n.Get("settings.aap_icons"),         ref settings.showParamAapIcons,          settings);

            DrawOverlayToggle(new Rect(ifRow2Rect.x + 0 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), L10n.Get("settings.graph_footer"),      ref settings.showGraphFooter,            settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 1 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), L10n.Get("settings.vrc_comp_icons"),    ref settings.showParamVrcComponentIcons, settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 2 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), L10n.Get("settings.param_budget"),      ref settings.showParamBudget,            settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 3 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), L10n.Get("settings.empty_params"),      ref settings.showParamUnusedIcon,        settings);
            EditorGUILayout.Space(6);
            DrawPaletteColorRow(L10n.Get("settings.palette.primary"),   ref settings.paletteColorPrimary,   AnimatorDefaultSettings.DefaultPrimary,   settings);
            DrawPaletteColorRow(L10n.Get("settings.palette.secondary"), ref settings.paletteColorSecondary, AnimatorDefaultSettings.DefaultSecondary, settings);
            DrawPaletteColorRow(L10n.Get("settings.palette.accent"),    ref settings.paletteColorAccent,    AnimatorDefaultSettings.DefaultAccent,    settings);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(L10n.Get("settings.palette.param_type_vrc_colors"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!settings.showParamTypeIcons))
            {
                DrawNodeColorRow("Float",   ref settings.paramColorFloat,   new Color(0.35f, 0.75f, 0.35f, 1f), settings);
                DrawNodeColorRow("Int",     ref settings.paramColorInt,     new Color(0.35f, 0.60f, 1.00f, 1f), settings);
                DrawNodeColorRow("Bool",    ref settings.paramColorBool,    new Color(1.00f, 0.55f, 0.20f, 1f), settings);
                DrawNodeColorRow("Trigger", ref settings.paramColorTrigger, new Color(0.85f, 0.30f, 0.85f, 1f), settings);
            }
            using (new EditorGUI.DisabledScope(!settings.showParamVrcIcons))
            {
                DrawNodeColorRow(L10n.Get("settings.palette.vrc_label"), ref settings.paramColorVrcLabel, Color.cyan, settings);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(L10n.Get("settings.palette.graph_analysis"), EditorStyles.boldLabel);
            DrawNodeColorRow(L10n.Get("settings.palette.analysis_highlight"), ref settings.analysisHighlightColor, Color.red, settings);
            EditorGUILayout.EndVertical();
        }

        void DrawPaletteColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = ClampPaletteColor(newColor);
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
                if (DrawResetBtn())
                {
                    color = defaultColor;
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
            }
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

        void DrawGraphGridSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_graphGridOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.graph_background"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _graphGridOpen = !_graphGridOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(L10n.Get("settings.enable"), settings.graphGridOverride, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.graphGridOverride = enabled;
                    settings.Save();
                }
                if (DrawResetBtn(24f))
                {
                    settings.ResetGraphGrid();
                    settings.Save();
                }
            }

            if (!_graphGridOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.graphGridOverride))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("settings.bg.background"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool useImage = EditorGUILayout.ToggleLeft(L10n.Get("settings.bg.color"), !settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && useImage) { settings.graphGridUseImage = false; settings.Save(); }
                    EditorGUI.BeginChangeCheck();
                    bool imageSelected = EditorGUILayout.ToggleLeft(L10n.Get("settings.bg.image"), settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && imageSelected) { settings.graphGridUseImage = true; settings.Save(); }

                    if (!settings.graphGridUseImage)
                    {
                        EditorGUI.BeginChangeCheck();
                        var newColor = EditorGUILayout.ColorField(GUIContent.none, settings.graphGridBackgroundColor, true, false, false);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundColor = newColor; settings.Save(); }
                        if (DrawResetBtn())
                        {
                            settings.graphGridBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
                            settings.Save();
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        var texture = (UnityEngine.Texture2D)EditorGUILayout.ObjectField(settings.graphGridBackgroundImage, typeof(UnityEngine.Texture2D), false, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImage = texture; settings.Save(); }
                        EditorGUI.BeginChangeCheck();
                        float opacity = EditorGUILayout.Slider(settings.graphGridBackgroundImageOpacity, 0f, 1f, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImageOpacity = opacity; settings.Save(); }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("settings.bg.grid"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool drawLines = EditorGUILayout.ToggleLeft("", settings.graphGridDrawLines, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.graphGridDrawLines = drawLines; settings.Save(); }
                }

                using (new EditorGUI.DisabledScope(!settings.graphGridDrawLines))
                {
                    DrawGraphGridColorRow(L10n.Get("settings.bg.major_grid"), ref settings.graphGridColorMajor, new Color(0.30f, 0.30f, 0.30f, 1f), settings);
                    DrawGraphGridColorRow(L10n.Get("settings.bg.minor_grid"), ref settings.graphGridColorMinor, new Color(0.22f, 0.22f, 0.22f, 1f), settings);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(L10n.Get("settings.bg.grid_scale"), GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        float scale = EditorGUILayout.Slider(settings.graphGridScalingMajor, 1f, 3f);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridScalingMajor = scale; settings.Save(); }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(L10n.Get("settings.bg.minor_divisions"), GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        int div = EditorGUILayout.IntSlider(settings.graphGridDivisorMinor, 2, 10);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridDivisorMinor = div; settings.Save(); }
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        /* Draws a labeled color field row with a Reset button that restores defaultColor and auto-saves. */
        void DrawGraphGridColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = newColor;
                    settings.Save();
                }
                if (DrawResetBtn())
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Node icon indicators ──────────────────────────────────────────────

        void DrawOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_nodeIconsOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.node_icons"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeIconsOpen = !_nodeIconsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(L10n.Get("settings.enable"), settings.overlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.overlayEnabled = enabled; settings.Save(); }
            }

            if (!_nodeIconsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.overlayEnabled))
            {
                float lineHeight = EditorGUIUtility.singleLineHeight;
                var row1Rect = EditorGUILayout.GetControlRect(false, lineHeight);
                var row2Rect = EditorGUILayout.GetControlRect(false, lineHeight);
                float colWidth = row1Rect.width / 4f;

                DrawOverlayToggle(new Rect(row1Rect.x + 0 * colWidth, row1Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.loop_empty"), ref settings.overlayShowLoopEmpty,  settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 1 * colWidth, row1Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.clip_time"), ref settings.overlayShowClipTime,   settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 2 * colWidth, row1Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.wd"),        ref settings.overlayShowWD,         settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 3 * colWidth, row1Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.behaviors"), ref settings.overlayShowB,          settings);

                DrawOverlayToggle(new Rect(row2Rect.x + 0 * colWidth, row2Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.coords"),    ref settings.overlayShowCoords,     settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 1 * colWidth, row2Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.clip_name"), ref settings.overlayShowMotionName, settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 2 * colWidth, row2Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.motion"),    ref settings.overlayShowMotion,     settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 3 * colWidth, row2Rect.y, colWidth, lineHeight), L10n.Get("settings.overlay.speed"),     ref settings.overlayShowSpeed,      settings);
                EditorGUILayout.Space(4);
                DrawNodeColorRow(L10n.Get("settings.overlay.active"),   ref settings.overlayActiveColor,   Color.white,                         settings);
                DrawNodeColorRow(L10n.Get("settings.overlay.inactive"), ref settings.overlayInactiveColor, new Color(0.45f, 0.45f, 0.45f, 1f), settings);
            }
            EditorGUILayout.EndVertical();
        }

        // ── Transition overlay ────────────────────────────────────────────────

        void DrawTransitionOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_transitionOverlayOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.transition_overlay"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionOverlayOpen = !_transitionOverlayOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(L10n.Get("settings.enable"), settings.transitionOverlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.transitionOverlayEnabled = enabled; settings.Save(); }
            }

            if (!_transitionOverlayOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.transitionOverlayEnabled))
            {
                float transLineHeight = EditorGUIUtility.singleLineHeight;
                var transToggleRowRect = EditorGUILayout.GetControlRect(false, transLineHeight);
                float transColWidth = transToggleRowRect.width / 4f;

                DrawOverlayToggle(new Rect(transToggleRowRect.x + 0 * transColWidth, transToggleRowRect.y, transColWidth, transLineHeight), L10n.Get("settings.trans_overlay.labels"),           ref settings.transitionShowLabel,               settings);
                DrawOverlayToggle(new Rect(transToggleRowRect.x + 1 * transColWidth, transToggleRowRect.y, transColWidth, transLineHeight), L10n.Get("settings.trans_overlay.selection_colors"), ref settings.transitionSelectionColorEnabled,    settings);
                DrawOverlayToggle(new Rect(transToggleRowRect.x + 2 * transColWidth, transToggleRowRect.y, transColWidth, transLineHeight), L10n.Get("settings.trans_overlay.indicator_arrows"), ref settings.transitionIndicatorArrowsEnabled,   settings);
                DrawOverlayToggle(new Rect(transToggleRowRect.x + 3 * transColWidth, transToggleRowRect.y, transColWidth, transLineHeight), L10n.Get("settings.trans_overlay.animate"),          ref settings.transitionAnimateSelected,          settings);

                DrawNodeColorRow(L10n.Get("settings.trans_overlay.transition_line"), ref settings.transitionOverlayColor,         new Color(1.0f, 1.0f, 1.0f, 1.0f), settings);

                using (new EditorGUI.DisabledScope(!settings.transitionSelectionColorEnabled))
                {
                    DrawNodeColorRow(L10n.Get("settings.trans_overlay.selection_in"),  ref settings.transitionIncomingColor, new Color(0.0f, 1.0f, 1.0f, 1.0f), settings);
                    DrawNodeColorRow(L10n.Get("settings.trans_overlay.selection_out"), ref settings.transitionOutgoingColor, new Color(1.0f, 0.0f, 1.0f, 1.0f), settings);
                }

                using (new EditorGUI.DisabledScope(!settings.transitionIndicatorArrowsEnabled))
                {
                    DrawNodeColorRow(L10n.Get("settings.trans_overlay.default_arrow"),      ref settings.transitionOverlayArrowColor,    new Color(0.6f, 0.6f, 0.6f, 1.0f),  settings);
                    DrawNodeColorRow(L10n.Get("settings.trans_overlay.no_condition_arrow"), ref settings.transitionArrowNoConditionColor, new Color(1.0f, 0.28f, 0.0f, 1.0f), settings);
                    DrawNodeColorRow(L10n.Get("settings.trans_overlay.instant_arrow"),      ref settings.transitionArrowInstantColor,     new Color(0.0f, 0.25f, 0.66f, 1.0f), settings);
                }
            }
            EditorGUILayout.EndVertical();
        }

        // ── Node colors ───────────────────────────────────────────────────────

        void DrawNodeColorsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_nodeColorsOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.node_colors"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeColorsOpen = !_nodeColorsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(L10n.Get("settings.enable"), settings.nodeColorEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.nodeColorEnabled = enabled;
                    settings.Save();
                }
                if (DrawResetBtn(24f))
                {
                    settings.ResetNodeColors();
                    settings.Save();
                }
            }

            if (!_nodeColorsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.nodeColorEnabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("settings.node_colors.visual_style"), GUILayout.Width(115));
                    EditorGUI.BeginChangeCheck();
                    bool is3D = EditorGUILayout.ToggleLeft(L10n.Get("settings.node_colors.flat_3d"), settings.nodeColor3DEnabled);
                    if (EditorGUI.EndChangeCheck())
                    {
                        settings.nodeColor3DEnabled = is3D;
                        settings.Save();
                        PatchNodeStyles.Invalidate();
                    }
                }
                DrawNodeColorRow(L10n.Get("settings.node_colors.selection_highlight"), ref settings.nodeSelectionColor,      new(1f, 1f, 1f, 1f), settings);
                EditorGUILayout.Space(8);
                DrawNodeColorRow(L10n.Get("settings.node_colors.state_nodes"),       ref settings.stateNodeColor,       new(0.30f, 0.30f, 0.30f, 1f), settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.default_state"),     ref settings.defaultStateColor,    new(0.60f, 0.35f, 0.10f, 1f), settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.sub_state_machine"), ref settings.subStateMachineColor, new(0.35f, 0.25f, 0.50f, 1f), settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.entry_node"),        ref settings.entryNodeColor,       new(0.20f, 0.55f, 0.20f, 1f), settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.exit_node"),         ref settings.exitNodeColor,        new(0.55f, 0.15f, 0.15f, 1f), settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.any_state"),         ref settings.anyStateNodeColor,    new(0.15f, 0.40f, 0.50f, 1f), settings);
                EditorGUILayout.Space(8);
                DrawNodeColorRow(L10n.Get("settings.node_colors.blend_tree_direct"), ref settings.blendTreeDirectNodeColor, new(0.70f, 0.37f, 0.20f, 1f),  settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.blend_tree_1d"),     ref settings.blendTree1DNodeColor,    new(0.24f, 0.50f, 0.60f, 1f),   settings);
                DrawNodeColorRow(L10n.Get("settings.node_colors.blend_tree_2d"),     ref settings.blendTree2DNodeColor,    new(0.24f, 0.60f, 0.45f, 1f),   settings);
            }
            EditorGUILayout.EndVertical();
        }

        static void DrawOverlayToggle(Rect rect, string label, ref bool value, AnimatorDefaultSettings settings)
        {
            EditorGUI.BeginChangeCheck();
            bool newValue = EditorGUI.ToggleLeft(rect, label, value);
            if (EditorGUI.EndChangeCheck()) { value = newValue; settings.Save(); }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        static void DrawFeatureToggle(string featureId, string label, string tooltip)
        {
            bool current = FeatureHarmony.IsEnabled(featureId);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var content = string.IsNullOrEmpty(tooltip) ? new GUIContent(label) : new GUIContent(label, tooltip);
                bool newValue = EditorGUILayout.ToggleLeft(content, current);
                if (EditorGUI.EndChangeCheck())
                {
                    FeatureHarmony.SetEnabled(featureId, newValue);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }
        }

        static void DrawFeatureToggle(Rect rect, string featureId, string label, string tooltip)
        {
            bool current = FeatureHarmony.IsEnabled(featureId);
            EditorGUI.BeginChangeCheck();
            var content = string.IsNullOrEmpty(tooltip) ? new GUIContent(label) : new GUIContent(label, tooltip);
            bool newValue = EditorGUI.ToggleLeft(rect, content, current);
            if (EditorGUI.EndChangeCheck())
            {
                FeatureHarmony.SetEnabled(featureId, newValue);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        /* Draws a labeled color field row with a Reset button that restores defaultColor and auto-saves. Shared by node color and transition overlay color rows. */
        void DrawNodeColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = newColor;
                    settings.Save();
                }
                if (DrawResetBtn())
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Transition defaults ───────────────────────────────────────────────

        void DrawTransitionDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_transitionDefaultsOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.transition_defaults"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionDefaultsOpen = !_transitionDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft(L10n.Get("settings.apply_on_create"), settings.applyToTransitions, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToTransitions = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_transitionDefaultsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.applyToTransitions))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.has_exit_time"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasExit = EditorGUILayout.Toggle(settings.transHasExitTime, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasExitTime = hasExit; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(L10n.Get("transitions.exit_time"), GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float exitTime = EditorGUILayout.FloatField(settings.transExitTime);
                    if (EditorGUI.EndChangeCheck()) { settings.transExitTime = exitTime; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.has_fixed_duration"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasFixed = EditorGUILayout.Toggle(settings.transHasFixedDuration, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasFixedDuration = hasFixed; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(L10n.Get("transitions.duration"), GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float duration = EditorGUILayout.FloatField(settings.transDuration);
                    if (EditorGUI.EndChangeCheck()) { settings.transDuration = duration; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.offset"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    float offset = EditorGUILayout.FloatField(settings.transOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.transOffset = offset; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.interruption_source"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    var interruptionSource = (TransitionInterruptionSource)EditorGUILayout.Popup(
                        (int)settings.transInterruptionSource,
                        new[] { L10n.Get("transitions.interruption.none"), L10n.Get("transitions.interruption.source"), L10n.Get("transitions.interruption.destination"), L10n.Get("transitions.interruption.source_then_destination"), L10n.Get("transitions.interruption.destination_then_source") });
                    if (EditorGUI.EndChangeCheck()) { settings.transInterruptionSource = interruptionSource; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.ordered_interruption"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool ordered = EditorGUILayout.Toggle(settings.transOrderedInterruption, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transOrderedInterruption = ordered; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(L10n.Get("transitions.mute"), GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool mute = EditorGUILayout.Toggle(settings.transMute, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transMute = mute; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("transitions.can_transition_to_self"), GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool canTransitionToSelf = EditorGUILayout.Toggle(settings.transCanTransitionToSelf, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transCanTransitionToSelf = canTransitionToSelf; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(L10n.Get("transitions.solo"), GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool solo = EditorGUILayout.Toggle(settings.transSolo, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transSolo = solo; settings.Save(); }
                }
            }
            EditorGUILayout.EndVertical();
        }

        // ── State defaults ────────────────────────────────────────────────────

        void DrawStateDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_stateDefaultsOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.state_defaults"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _stateDefaultsOpen = !_stateDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft(L10n.Get("settings.apply_on_create"), settings.applyToStates, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToStates = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_stateDefaultsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.applyToStates))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.tag"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    string tag = EditorGUILayout.TextField(settings.stateTag);
                    if (EditorGUI.EndChangeCheck()) { settings.stateTag = tag; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.speed"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float speed = EditorGUILayout.FloatField(settings.stateSpeed);
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeed = speed; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!settings.stateSpeedParameterActive))
                    {
                        EditorGUILayout.LabelField(L10n.Get("states.multiplier"), GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        string speedParam = EditorGUILayout.TextField(settings.stateSpeedParameter);
                        if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameter = speedParam; settings.Save(); }
                        GUILayout.FlexibleSpace();
                    }
                    EditorGUI.BeginChangeCheck();
                    bool speedParamActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"), settings.stateSpeedParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameterActive = speedParamActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.motion_time"), GUILayout.Width(110));
                    if (settings.stateTimeParameterActive)
                    {
                        EditorGUI.BeginChangeCheck();
                        string timeParam = EditorGUILayout.TextField(settings.stateTimeParameter);
                        if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameter = timeParam; settings.Save(); }
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool timeActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"), settings.stateTimeParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameterActive = timeActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.mirror"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool mirror = EditorGUILayout.Toggle(settings.stateMirror, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirror = mirror; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool mirrorActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"), settings.stateMirrorParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirrorParameterActive = mirrorActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.cycle_offset"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float cycleOffset = EditorGUILayout.FloatField(settings.stateCycleOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffset = cycleOffset; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool cycleActive = EditorGUILayout.ToggleLeft(L10n.Get("states.parameter"), settings.stateCycleOffsetParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffsetParameterActive = cycleActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.foot_ik"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool footIK = EditorGUILayout.Toggle(settings.stateIKOnFeet, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateIKOnFeet = footIK; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L10n.Get("states.write_defaults"), GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool writeDefaults = EditorGUILayout.Toggle(settings.stateWriteDefaultValues, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateWriteDefaultValues = writeDefaults; settings.Save(); }
                }
            }
            EditorGUILayout.EndVertical();
        }
        // ── Miscellaneous ─────────────────────────────────────────────────────

        void DrawMiscellaneousSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_miscOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.miscellaneous"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _miscOpen = !_miscOpen;
                GUILayout.FlexibleSpace();
            }

            if (!_miscOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);

            float miscLineHeight = EditorGUIUtility.singleLineHeight;
            var miscRow1Rect = EditorGUILayout.GetControlRect(false, miscLineHeight);
            var miscRow2Rect = EditorGUILayout.GetControlRect(false, miscLineHeight);
            float miscColWidth = miscRow1Rect.width / 4f;

            DrawOverlayToggle(new Rect(miscRow1Rect.x + 0 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.wd_blend_trees"),       ref settings.wdIncludeBlendTreeStates,   settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 1 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.prevent_layer_scroll"), ref settings.preventLayerScroll,         settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 2 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.prevent_param_scroll"), ref settings.preventParameterScroll,     settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 3 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.layer_weight_1"),       ref settings.newLayerWeightOne,          settings);

            DrawOverlayToggle(new Rect(miscRow2Rect.x + 0 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.clip_menu_nesting"), ref settings.clipMenuNestingEnabled,     settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 1 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.layer_templates"),   ref settings.layerTemplateButtonEnabled, settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 2 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.param_add_menu"),    ref settings.parameterAddMenuEnabled,    settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 3 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), L10n.Get("settings.misc.frames"),            ref settings.framesEnabled,              settings);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(L10n.Get("settings.misc.palettes"), EditorStyles.boldLabel);

            var savePaletteBtnRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = savePaletteBtnRect.Contains(Event.current.mousePosition);
                var accent = Styles.AccentColor;
                EditorGUI.DrawRect(savePaletteBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                GUI.Label(savePaletteBtnRect, L10n.Get("settings.misc.save_palette"), BindingBtnLabelStyle);
            }
            EditorGUIUtility.AddCursorRect(savePaletteBtnRect, MouseCursor.Link);
            if (GUI.Button(savePaletteBtnRect, GUIContent.none, GUIStyle.none))
            {
                settings.savedPalettes.Add(new AnimatorPalette { encodedColors = AnimatorDefaultSettings.EncodePalette(AnimatorDefaultSettings.CapturePaletteColors(settings)) });
                settings.Save();
                GUIUtility.ExitGUI();
            }

            float paletteSlotLineHeight  = EditorGUIUtility.singleLineHeight;
            float paletteButtonSize      = paletteSlotLineHeight;
            float paletteActionBtnWidth  = paletteSlotLineHeight * 6f;
            float paletteSwatchGap       = EditorGUIUtility.standardVerticalSpacing * 2f;
            for (int i = 0; i < settings.savedPalettes.Count; i++)
            {
                var slotRowRect   = EditorGUILayout.GetControlRect(false, paletteSlotLineHeight);
                float nameWidth   = slotRowRect.width * 0.25f;

                var deleteBtnRect   = new Rect(slotRowRect.xMax - paletteButtonSize, slotRowRect.y, paletteButtonSize, paletteSlotLineHeight);
                var copyBtnRect     = new Rect(deleteBtnRect.x - paletteActionBtnWidth, slotRowRect.y, paletteActionBtnWidth, paletteSlotLineHeight);
                var nameFieldRect   = new Rect(slotRowRect.x, slotRowRect.y, nameWidth, paletteSlotLineHeight);
                var swatchBlockRect = new Rect(nameFieldRect.xMax + 2f, slotRowRect.y, copyBtnRect.x - nameFieldRect.xMax - 2f - paletteSwatchGap, paletteSlotLineHeight);

                EditorGUI.BeginChangeCheck();
                settings.savedPalettes[i].slotName = EditorGUI.TextField(nameFieldRect, settings.savedPalettes[i].slotName);
                if (EditorGUI.EndChangeCheck()) settings.Save();

                if (Event.current.type == EventType.Repaint)
                {
                    if (AnimatorDefaultSettings.TryDecodePalette(settings.savedPalettes[i].encodedColors, out var previewColors))
                    {
                        float swatchCellWidth = swatchBlockRect.width / AnimatorDefaultSettings.PaletteColorCount;
                        for (int j = 0; j < AnimatorDefaultSettings.PaletteColorCount; j++)
                            EditorGUI.DrawRect(new Rect(swatchBlockRect.x + j * swatchCellWidth, swatchBlockRect.y, swatchCellWidth, swatchBlockRect.height), previewColors[j]);
                    }
                    else
                    {
                        EditorGUI.DrawRect(swatchBlockRect, new Color(0.3f, 0.3f, 0.3f, 1f));
                    }
                }
                EditorGUIUtility.AddCursorRect(swatchBlockRect, MouseCursor.Link);
                if (GUI.Button(swatchBlockRect, GUIContent.none, GUIStyle.none))
                {
                    if (AnimatorDefaultSettings.TryDecodePalette(settings.savedPalettes[i].encodedColors, out var applyColors))
                    {
                        AnimatorDefaultSettings.ApplyPaletteColors(settings, applyColors);
                        Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                        settings.Save();
                    }
                }

                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = copyBtnRect.Contains(Event.current.mousePosition);
                    var accent = Styles.AccentColor;
                    EditorGUI.DrawRect(copyBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                    GUI.Label(copyBtnRect, L10n.Get("settings.misc.copy_palette"), BindingBtnLabelStyle);
                }
                EditorGUIUtility.AddCursorRect(copyBtnRect, MouseCursor.Link);
                if (GUI.Button(copyBtnRect, GUIContent.none, GUIStyle.none))
                    EditorGUIUtility.systemCopyBuffer = settings.savedPalettes[i].encodedColors;

                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = deleteBtnRect.Contains(Event.current.mousePosition);
                    var accent = Styles.AccentColor;
                    EditorGUI.DrawRect(deleteBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                    GUI.Label(deleteBtnRect, "−", BindingBtnLabelStyle);
                }
                EditorGUIUtility.AddCursorRect(deleteBtnRect, MouseCursor.Link);
                if (GUI.Button(deleteBtnRect, GUIContent.none, GUIStyle.none))
                {
                    settings.savedPalettes.RemoveAt(i);
                    settings.Save();
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _paletteImportText = EditorGUILayout.TextField(_paletteImportText, GUILayout.ExpandWidth(true));
                var applyPaletteBtnRect = GUILayoutUtility.GetRect(paletteActionBtnWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(paletteActionBtnWidth));
                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = applyPaletteBtnRect.Contains(Event.current.mousePosition);
                    var accent = Styles.AccentColor;
                    EditorGUI.DrawRect(applyPaletteBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                    GUI.Label(applyPaletteBtnRect, L10n.Get("settings.misc.apply_palette"), BindingBtnLabelStyle);
                }
                EditorGUIUtility.AddCursorRect(applyPaletteBtnRect, MouseCursor.Link);
                if (GUI.Button(applyPaletteBtnRect, GUIContent.none, GUIStyle.none))
                {
                    if (AnimatorDefaultSettings.TryDecodePalette(_paletteImportText, out var importedColors))
                    {
                        AnimatorDefaultSettings.ApplyPaletteColors(settings, importedColors);
                        Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                        settings.Save();
                        _paletteImportText = "";
                        GUI.FocusControl(null);
                    }
                }
                GUILayout.Space(paletteButtonSize);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(L10n.Get("settings.misc.color_tags"), EditorStyles.boldLabel);
            for (int i = 0; i < settings.colorTags.Count; i++)
            {
                var tagRowRect       = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                float removeBtnWidth = 24f;
                float availableWidth = tagRowRect.width - removeBtnWidth - 2f;
                float colorPickerWidth = availableWidth * 0.25f;
                float textFieldWidth   = availableWidth * 0.75f;

                var textFieldRect  = new Rect(tagRowRect.x, tagRowRect.y, textFieldWidth, tagRowRect.height);
                var colorFieldRect = new Rect(tagRowRect.x + textFieldWidth, tagRowRect.y, colorPickerWidth, tagRowRect.height);
                var removeBtnRect  = new Rect(tagRowRect.xMax - removeBtnWidth, tagRowRect.y, removeBtnWidth, tagRowRect.height);

                EditorGUI.BeginChangeCheck();
                settings.colorTags[i].tagName = EditorGUI.TextField(textFieldRect, settings.colorTags[i].tagName);
                settings.colorTags[i].color   = EditorGUI.ColorField(colorFieldRect, GUIContent.none, settings.colorTags[i].color, true, false, false);
                if (EditorGUI.EndChangeCheck()) settings.Save();

                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = removeBtnRect.Contains(Event.current.mousePosition);
                    var accent = Styles.AccentColor;
                    EditorGUI.DrawRect(removeBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                    GUI.Label(removeBtnRect, "−", BindingBtnLabelStyle);
                }
                EditorGUIUtility.AddCursorRect(removeBtnRect, MouseCursor.Link);
                if (GUI.Button(removeBtnRect, GUIContent.none, GUIStyle.none))
                {
                    settings.colorTags.RemoveAt(i);
                    settings.Save();
                    GUIUtility.ExitGUI();
                }
            }

            var addTagBtnRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = addTagBtnRect.Contains(Event.current.mousePosition);
                var accent = Styles.AccentColor;
                EditorGUI.DrawRect(addTagBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                GUI.Label(addTagBtnRect, L10n.Get("settings.misc.add_color_tag"), BindingBtnLabelStyle);
            }
            EditorGUIUtility.AddCursorRect(addTagBtnRect, MouseCursor.Link);
            if (GUI.Button(addTagBtnRect, GUIContent.none, GUIStyle.none))
            {
                settings.colorTags.Add(new AnimatorColorTag());
                settings.Save();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(L10n.Get("settings.misc.compatibility"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(L10n.Get("settings.misc.compatibility_desc"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2);
            float cLineH = EditorGUIUtility.singleLineHeight;
            var   cRow1  = EditorGUILayout.GetControlRect(false, cLineH);
            var   cRow2  = EditorGUILayout.GetControlRect(false, cLineH);
            var   cRow3  = EditorGUILayout.GetControlRect(false, cLineH);
            var   cRow4  = EditorGUILayout.GetControlRect(false, cLineH);
            var   cRow5  = EditorGUILayout.GetControlRect(false, cLineH);
            float cColW  = cRow1.width / 2f;

            DrawFeatureToggle(new Rect(cRow1.x,         cRow1.y, cColW, cLineH), FeatureHarmony.ContextMenuId,   L10n.Get("settings.misc.context_menus"),     L10n.Get("settings.misc.tt.context_menus"));
            DrawFeatureToggle(new Rect(cRow1.x + cColW, cRow1.y, cColW, cLineH), FeatureHarmony.NodeOverlayId,   L10n.Get("settings.misc.node_overlay"),      L10n.Get("settings.misc.tt.node_overlay"));
            DrawFeatureToggle(new Rect(cRow2.x,         cRow2.y, cColW, cLineH), FeatureHarmony.NodeColorId,     L10n.Get("settings.misc.node_colors_feat"),  L10n.Get("settings.misc.tt.node_colors"));
            DrawFeatureToggle(new Rect(cRow2.x + cColW, cRow2.y, cColW, cLineH), FeatureHarmony.TransitionId,    L10n.Get("settings.misc.transition_overlay"), L10n.Get("settings.misc.tt.transition_overlay"));
            DrawFeatureToggle(new Rect(cRow3.x,         cRow3.y, cColW, cLineH), FeatureHarmony.GraphInteractId, L10n.Get("settings.misc.graph_interaction"), L10n.Get("settings.misc.tt.graph_interaction"));
            DrawFeatureToggle(new Rect(cRow3.x + cColW, cRow3.y, cColW, cLineH), FeatureHarmony.GridBgId,        L10n.Get("settings.misc.grid_background"),   L10n.Get("settings.misc.tt.grid_background"));
            DrawFeatureToggle(new Rect(cRow4.x,         cRow4.y, cColW, cLineH), FeatureHarmony.LayerViewId,     L10n.Get("settings.misc.layer_view"),        L10n.Get("settings.misc.tt.layer_view"));
            DrawFeatureToggle(new Rect(cRow4.x + cColW, cRow4.y, cColW, cLineH), FeatureHarmony.ParamViewId,     L10n.Get("settings.misc.parameter_view"),    L10n.Get("settings.misc.tt.parameter_view"));
            DrawFeatureToggle(new Rect(cRow5.x,         cRow5.y, cColW, cLineH), FeatureHarmony.BlendTreeId,     L10n.Get("settings.misc.blend_tree_feat"),   L10n.Get("settings.misc.tt.blend_tree"));
            DrawFeatureToggle(new Rect(cRow5.x + cColW, cRow5.y, cColW, cLineH), FeatureHarmony.BottomBarId,     L10n.Get("settings.misc.bottom_bar"),        L10n.Get("settings.misc.tt.bottom_bar"));

            EditorGUILayout.EndVertical();
        }

        // ── Keybindings ───────────────────────────────────────────────────────

        void DrawKeybindsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_keybindsOpen ? "▼ " : "▶ ") + L10n.Get("settings.section.keybindings"), Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _keybindsOpen = !_keybindsOpen;
                GUILayout.FlexibleSpace();
                if (DrawResetBtn(24f))
                {
                    settings.ResetKeybinds();
                    settings.Save();
                    _recordingActionId = null;
                }
            }

            if (!_keybindsOpen) return;

            var ev = Event.current;
            if (_recordingActionId != null && ev.type == EventType.KeyDown && ev.keyCode != KeyCode.None)
            {
                if (ev.keyCode == KeyCode.Escape)
                {
                    _recordingActionId = null;
                    ev.Use();
                    Repaint();
                    return;
                }
                if (IsModifierKey(ev.keyCode)) return; // wait for non-modifier
                ApplyKeybind(settings, _recordingActionId, new KeyBinding(ev.keyCode, ev.control, ev.shift, ev.alt));
                settings.Save();
                _recordingActionId = null;
                ev.Use();
                Repaint();
                return;
            }

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            DrawBindingRow(L10n.Get("settings.kb.select_incoming"),        "kbSelectIncoming",       settings.kbSelectIncoming,       settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.select_outgoing"),        "kbSelectOutgoing",       settings.kbSelectOutgoing,       settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.select_both"),            "kbSelectBoth",           settings.kbSelectBoth,           settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.select_all_nodes"),       "kbSelectAll",            settings.kbSelectAll,            settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.select_all_transitions"), "kbSelectAllTransitions", settings.kbSelectAllTransitions, settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.copy"),                   "kbCopy",                 settings.kbCopy,                 settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.paste"),                  "kbPaste",                settings.kbPaste,                settings, 125, 85);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);

            EditorGUILayout.BeginVertical();
            DrawBindingRow(L10n.Get("settings.kb.duplicate"),           "kbDuplicate",          settings.kbDuplicate,          settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.chain_mode"),          "kbChainMode",          settings.kbChainMode,          settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.fan_mode"),            "kbFanMode",            settings.kbFanMode,            settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.multi_transition"),    "kbMultiTransition",    settings.kbMultiTransition,    settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.reverse_transitions"), "kbReverseTransitions", settings.kbReverseTransitions, settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.replicate"),           "kbReplicate",          settings.kbReplicate,          settings, 125, 85);
            DrawBindingRow(L10n.Get("settings.kb.redirect"),            "kbRedirect",           settings.kbRedirect,           settings, 125, 85);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        static GUIStyle s_bindingBtnLabelStyle;
        static GUIStyle BindingBtnLabelStyle => s_bindingBtnLabelStyle ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 11,
            normal    = { textColor = Color.white }
        };

        bool DrawResetBtn(float height = 0f)
        {
            float h = height > 0f ? height : EditorGUIUtility.singleLineHeight;
            var resetBtnRect = GUILayoutUtility.GetRect(24, h, GUILayout.Width(24));
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = resetBtnRect.Contains(Event.current.mousePosition);
                var accent = Styles.AccentColor;
                EditorGUI.DrawRect(resetBtnRect, hovered ? new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f) : accent);
                GUI.Label(resetBtnRect, "↺", BindingBtnLabelStyle);
            }
            EditorGUIUtility.AddCursorRect(resetBtnRect, MouseCursor.Link);
            return GUI.Button(resetBtnRect, GUIContent.none, GUIStyle.none);
        }

        void DrawBindingRow(string label, string actionId, KeyBinding binding, AnimatorDefaultSettings settings, int labelWidth = 160, int btnWidth = 180)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
                bool isRecording = _recordingActionId == actionId;
                string btnLabel = isRecording ? L10n.Get("settings.kb.press_key") : binding.Label();
                var btnRect = GUILayoutUtility.GetRect(btnWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(btnWidth));
                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = btnRect.Contains(Event.current.mousePosition);
                    var accent = Styles.AccentColor;
                    var hoverColor = new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
                    EditorGUI.DrawRect(btnRect, hovered ? hoverColor : accent);
                    GUI.Label(btnRect, btnLabel, BindingBtnLabelStyle);
                }
                if (GUI.Button(btnRect, GUIContent.none, GUIStyle.none))
                {
                    _recordingActionId = isRecording ? null : actionId;
                    Repaint();
                }
                EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
                if (!isRecording)
                {
                    GUILayout.Space(4f);
                    var resetRect = GUILayoutUtility.GetRect(24, EditorGUIUtility.singleLineHeight, GUILayout.Width(24));
                    if (Event.current.type == EventType.Repaint)
                    {
                        bool resetHovered = resetRect.Contains(Event.current.mousePosition);
                        var accent = Styles.AccentColor;
                        var hoverColor = new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
                        EditorGUI.DrawRect(resetRect, resetHovered ? hoverColor : accent);
                        GUI.Label(resetRect, "↺", BindingBtnLabelStyle);
                    }
                    if (GUI.Button(resetRect, GUIContent.none, GUIStyle.none))
                    {
                        var defaults = new AnimatorDefaultSettings();
                        ApplyKeybind(settings, actionId, GetDefaultBinding(defaults, actionId));
                        settings.Save();
                    }
                    EditorGUIUtility.AddCursorRect(resetRect, MouseCursor.Link);
                }
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
