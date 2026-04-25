#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        bool _graphGridOpen;
        bool _nodeIconsOpen;
        bool _transitionOverlayOpen;
        bool _nodeColorsOpen;
        bool _transitionDefaultsOpen;
        bool _stateDefaultsOpen;

        void DrawSettingsTab()
        {
            var settings = AnimatorDefaultSettings.Load();
            DrawGraphGridSection(settings);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawOverlaySection(settings);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawTransitionOverlaySection(settings);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawNodeColorsSection(settings);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawTransitionDefaultsSection(settings);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawStateDefaultsSection(settings);
        }

        // ── Graph background + grid ───────────────────────────────────────────

        void DrawGraphGridSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_graphGridOpen ? "▼ " : "▶ ") + "Graph Background", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _graphGridOpen = !_graphGridOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.graphGridOverride, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.graphGridOverride = enabled;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48), GUILayout.Height(24)))
                {
                    settings.ResetGraphGrid();
                    settings.Save();
                }
            }

            if (!_graphGridOpen) return;

            using (new EditorGUI.DisabledScope(!settings.graphGridOverride))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Background", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool useImage = EditorGUILayout.ToggleLeft("Color", !settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && useImage) { settings.graphGridUseImage = false; settings.Save(); }
                    EditorGUI.BeginChangeCheck();
                    bool imageSelected = EditorGUILayout.ToggleLeft("Image", settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && imageSelected) { settings.graphGridUseImage = true; settings.Save(); }
                }

                if (!settings.graphGridUseImage)
                {
                    DrawGraphGridColorRow("  Color", ref settings.graphGridBackgroundColor, new Color(0.18f, 0.18f, 0.18f, 1f), settings);
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("  Image", GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        var texture = (UnityEngine.Texture2D)EditorGUILayout.ObjectField(settings.graphGridBackgroundImage, typeof(UnityEngine.Texture2D), false, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImage = texture; settings.Save(); }
                        EditorGUI.BeginChangeCheck();
                        float opacity = EditorGUILayout.Slider(settings.graphGridBackgroundImageOpacity, 0f, 1f, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImageOpacity = opacity; settings.Save(); }
                    }
                }

                DrawGraphGridColorRow("Major Grid",  ref settings.graphGridColorMajor,      new Color(0.30f, 0.30f, 0.30f, 1f), settings);
                DrawGraphGridColorRow("Minor Grid",  ref settings.graphGridColorMinor,      new Color(0.22f, 0.22f, 0.22f, 1f), settings);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Grid Scale", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float scale = EditorGUILayout.Slider(settings.graphGridScalingMajor, 0.25f, 3f);
                    if (EditorGUI.EndChangeCheck()) { settings.graphGridScalingMajor = scale; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Minor Divisions", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    int div = EditorGUILayout.IntSlider(settings.graphGridDivisorMinor, 2, 10);
                    if (EditorGUI.EndChangeCheck()) { settings.graphGridDivisorMinor = div; settings.Save(); }
                }
            }
        }

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
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Node icon indicators ──────────────────────────────────────────────

        void DrawOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_nodeIconsOpen ? "▼ " : "▶ ") + "Node Icons", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeIconsOpen = !_nodeIconsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.overlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.overlayEnabled = enabled; settings.Save(); }
            }

            if (!_nodeIconsOpen) return;

            using (new EditorGUI.DisabledScope(!settings.overlayEnabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool showEmpty = EditorGUILayout.ToggleLeft("! Empty", settings.overlayShowEmpty, GUILayout.Width(65));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowEmpty = showEmpty; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showLoop = EditorGUILayout.ToggleLeft("↻ Loop", settings.overlayShowLoop, GUILayout.Width(65));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowLoop = showLoop; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showWD = EditorGUILayout.ToggleLeft("WD", settings.overlayShowWD, GUILayout.Width(42));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowWD = showWD; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showB = EditorGUILayout.ToggleLeft("Behaviors", settings.overlayShowB, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowB = showB; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool showSpeed = EditorGUILayout.ToggleLeft("Speed", settings.overlayShowSpeed, GUILayout.Width(72));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowSpeed = showSpeed; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showMotion = EditorGUILayout.ToggleLeft("Motion", settings.overlayShowMotion, GUILayout.Width(78));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowMotion = showMotion; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showMotionName = EditorGUILayout.ToggleLeft("Clip Name", settings.overlayShowMotionName, GUILayout.Width(85));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowMotionName = showMotionName; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool showCoords = EditorGUILayout.ToggleLeft("Coords", settings.overlayShowCoords, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) { settings.overlayShowCoords = showCoords; settings.Save(); }
                }

                DrawNodeColorRow("Active",   ref settings.overlayActiveColor,   Color.white,                          settings);
                DrawNodeColorRow("Inactive", ref settings.overlayInactiveColor, new Color(0.45f, 0.45f, 0.45f, 1f),  settings);
            }
        }

        // ── Transition overlay ────────────────────────────────────────────────

        void DrawTransitionOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_transitionOverlayOpen ? "▼ " : "▶ ") + "Transition Overlay", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionOverlayOpen = !_transitionOverlayOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.transitionOverlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.transitionOverlayEnabled = enabled; settings.Save(); }
            }

            if (!_transitionOverlayOpen) return;

            using (new EditorGUI.DisabledScope(!settings.transitionOverlayEnabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool showLabel = EditorGUILayout.ToggleLeft("Labels", settings.transitionShowLabel, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionShowLabel = showLabel; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool arrows = EditorGUILayout.ToggleLeft("Indicator Arrows", settings.transitionIndicatorArrowsEnabled, GUILayout.Width(115));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionIndicatorArrowsEnabled = arrows; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool animate = EditorGUILayout.ToggleLeft("Animate", settings.transitionAnimateSelected, GUILayout.Width(72));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionAnimateSelected = animate; settings.Save(); }
                }

                DrawNodeColorRow("Transition Line",    ref settings.transitionOverlayColor,         new Color(1.0f, 1.0f, 1.0f, 1.0f), settings);
                DrawNodeColorRow("No Condition ▶", ref settings.transitionArrowNoConditionColor, new Color(1.0f, 0.28f, 0.0f, 1.0f),  settings);
                DrawNodeColorRow("Instant ▶", ref settings.transitionArrowInstantColor,     new Color(0.0f, 0.25f, 0.66f, 1.0f), settings);
            }
        }

        // ── Node colors ───────────────────────────────────────────────────────

        void DrawNodeColorsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_nodeColorsOpen ? "▼ " : "▶ ") + "Node Colors", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeColorsOpen = !_nodeColorsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.nodeColorEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.nodeColorEnabled = enabled;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48), GUILayout.Height(24)))
                {
                    settings.ResetNodeColors();
                    settings.Save();
                }
            }

            if (!_nodeColorsOpen) return;

            using (new EditorGUI.DisabledScope(!settings.nodeColorEnabled))
            {
                DrawNodeColorRow("State Nodes",       ref settings.stateNodeColor,       new(0.30f, 0.30f, 0.30f, 1f), settings);
                DrawNodeColorRow("Default State",     ref settings.defaultStateColor,    new(0.60f, 0.35f, 0.10f, 1f), settings);
                DrawNodeColorRow("Sub State Machine", ref settings.subStateMachineColor, new(0.35f, 0.25f, 0.50f, 1f), settings);
                DrawNodeColorRow("Entry Node",        ref settings.entryNodeColor,       new(0.20f, 0.55f, 0.20f, 1f), settings);
                DrawNodeColorRow("Exit Node",         ref settings.exitNodeColor,        new(0.55f, 0.15f, 0.15f, 1f), settings);
                DrawNodeColorRow("Any State",         ref settings.anyStateNodeColor,    new(0.15f, 0.40f, 0.50f, 1f), settings);
            }
        }

        void DrawNodeColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(115));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = newColor;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Transition defaults ───────────────────────────────────────────────

        void DrawTransitionDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_transitionDefaultsOpen ? "▼ " : "▶ ") + "Transition Defaults", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionDefaultsOpen = !_transitionDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft("Apply on Create", settings.applyToTransitions, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToTransitions = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_transitionDefaultsOpen) return;

            using (new EditorGUI.DisabledScope(!settings.applyToTransitions))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Has Exit Time", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasExit = EditorGUILayout.Toggle(settings.transHasExitTime, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasExitTime = hasExit; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Exit Time", GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float exitTime = EditorGUILayout.FloatField(settings.transExitTime);
                    if (EditorGUI.EndChangeCheck()) { settings.transExitTime = exitTime; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Has Fixed Duration", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasFixed = EditorGUILayout.Toggle(settings.transHasFixedDuration, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasFixedDuration = hasFixed; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Transition Duration", GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float duration = EditorGUILayout.FloatField(settings.transDuration);
                    if (EditorGUI.EndChangeCheck()) { settings.transDuration = duration; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Transition Offset", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    float offset = EditorGUILayout.FloatField(settings.transOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.transOffset = offset; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Interruption Source", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    var interruptionSource = (TransitionInterruptionSource)EditorGUILayout.EnumPopup(settings.transInterruptionSource);
                    if (EditorGUI.EndChangeCheck()) { settings.transInterruptionSource = interruptionSource; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Ordered Interruption", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool ordered = EditorGUILayout.Toggle(settings.transOrderedInterruption, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transOrderedInterruption = ordered; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Mute", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool mute = EditorGUILayout.Toggle(settings.transMute, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transMute = mute; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Can Transition To Self", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool canTransitionToSelf = EditorGUILayout.Toggle(settings.transCanTransitionToSelf, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transCanTransitionToSelf = canTransitionToSelf; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Solo", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool solo = EditorGUILayout.Toggle(settings.transSolo, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transSolo = solo; settings.Save(); }
                }
            }
        }

        // ── State defaults ────────────────────────────────────────────────────

        void DrawStateDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                if (CursorBtn((_stateDefaultsOpen ? "▼ " : "▶ ") + "State Defaults", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _stateDefaultsOpen = !_stateDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft("Apply on Create", settings.applyToStates, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToStates = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_stateDefaultsOpen) return;

            using (new EditorGUI.DisabledScope(!settings.applyToStates))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Tag", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    string tag = EditorGUILayout.TextField(settings.stateTag);
                    if (EditorGUI.EndChangeCheck()) { settings.stateTag = tag; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Speed", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float speed = EditorGUILayout.FloatField(settings.stateSpeed);
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeed = speed; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool speedParamActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateSpeedParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameterActive = speedParamActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(!settings.stateSpeedParameterActive))
                {
                    EditorGUILayout.LabelField("Multiplier", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    string speedParam = EditorGUILayout.TextField(settings.stateSpeedParameter);
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameter = speedParam; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Parameter", GUILayout.Width(90));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Motion Time", GUILayout.Width(110));
                    if (settings.stateTimeParameterActive)
                    {
                        EditorGUI.BeginChangeCheck();
                        string timeParam = EditorGUILayout.TextField(settings.stateTimeParameter);
                        if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameter = timeParam; settings.Save(); }
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool timeActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateTimeParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameterActive = timeActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Mirror", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool mirror = EditorGUILayout.Toggle(settings.stateMirror, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirror = mirror; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool mirrorActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateMirrorParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirrorParameterActive = mirrorActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Cycle Offset", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float cycleOffset = EditorGUILayout.FloatField(settings.stateCycleOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffset = cycleOffset; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool cycleActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateCycleOffsetParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffsetParameterActive = cycleActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Write Defaults", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool writeDefaults = EditorGUILayout.Toggle(settings.stateWriteDefaultValues, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateWriteDefaultValues = writeDefaults; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Foot IK", GUILayout.Width(55));
                    EditorGUI.BeginChangeCheck();
                    bool footIK = EditorGUILayout.Toggle(settings.stateIKOnFeet, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateIKOnFeet = footIK; settings.Save(); }
                }
            }
        }
    }
}
#endif
