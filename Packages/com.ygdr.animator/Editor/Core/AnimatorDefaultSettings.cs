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
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [Serializable]
    internal struct KeyBinding
    {
        [SerializeField] internal KeyCode key;
        [SerializeField] internal bool ctrl;
        [SerializeField] internal bool shift;
        [SerializeField] internal bool alt;

        internal KeyBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            this.key   = key;
            this.ctrl  = ctrl;
            this.shift = shift;
            this.alt   = alt;
        }

        internal bool Matches(Event e) =>
            key != KeyCode.None &&
            e.type == EventType.KeyDown &&
            e.keyCode == key &&
            e.control == ctrl &&
            e.shift   == shift &&
            e.alt     == alt;

        internal bool IsHeld(Event e)
        {
            if (e == null || key != KeyCode.None) return false;
            if (ctrl  && !e.control) return false;
            if (shift && !e.shift)   return false;
            if (alt   && !e.alt)     return false;
            return ctrl || shift || alt;
        }

        internal string Label()
        {
            if (key == KeyCode.None && !ctrl && !shift && !alt) return "—";
            var sb = new System.Text.StringBuilder();
            if (ctrl)  sb.Append("Ctrl+");
            if (shift) sb.Append("Shift+");
            if (alt)   sb.Append("Alt+");
            if (key != KeyCode.None) sb.Append(key.ToString());
            else if (sb.Length > 0) sb.Length--;
            return sb.ToString();
        }
    }

    [Serializable]
    internal class AnimatorColorTag
    {
        public string tagName = "New Tag";
        public Color  color   = Color.white;
    }

    [Serializable]
    internal class AnimatorPalette
    {
        public string slotName     = "Palette";
        public string encodedColors = "";
    }

    [Serializable]
    internal class AnimatorDefaultSettings
    {
        const string PrefsKey = "YGDR.AnimatorTools.Settings";

        // Window behavior
        [SerializeField] internal bool scrollToNewParameter = true;
        [SerializeField] internal bool newLayerWeightOne    = true;
        [SerializeField] internal bool showGraphFooter       = true;

        // Parameter list overlays
        [SerializeField] internal bool  showParamTypeIcons   = true;
        [SerializeField] internal bool  showParamVrcIcons    = true;
        [SerializeField] internal bool  showParamAapIcons    = true;
        [SerializeField] internal bool  showParamVrcComponentIcons = true;
        [SerializeField] internal bool  showParamBudget           = false;
        [SerializeField] internal bool  showParamUnusedIcon       = true;
        [SerializeField] internal Color paramColorFloat    = new Color(0.35f, 0.75f, 0.35f, 1f);
        [SerializeField] internal Color paramColorInt      = new Color(0.35f, 0.60f, 1.00f, 1f);
        [SerializeField] internal Color paramColorBool     = new Color(1.00f, 0.55f, 0.20f, 1f);
        [SerializeField] internal Color paramColorTrigger  = new Color(0.85f, 0.30f, 0.85f, 1f);
        [SerializeField] internal Color paramColorVrcLabel = Color.cyan;

        // Layer list overlays
        [SerializeField] internal bool  showLayerWDIndicator = true;
        [SerializeField] internal Color layerWDColor    = new Color(0.30f, 0.90f, 0.40f, 1f);
        [SerializeField] internal Color layerEmptyColor = new Color(1.00f, 0.40f, 0.20f, 1f);

        // Overlay indicators
        [SerializeField] internal bool overlayEnabled    = true;
        [SerializeField] internal bool overlayShowWD     = true;
        [SerializeField] internal bool overlayShowB      = true;
        [SerializeField] internal bool overlayShowLoopEmpty = true;
        [SerializeField] internal bool overlayShowClipTime   = true;
        [SerializeField] internal bool overlayShowSpeed      = true;
        [SerializeField] internal bool overlayShowMotion     = true;
        [SerializeField] internal bool overlayShowMotionName = true;
        [SerializeField] internal bool overlayShowCoords    = false;
        [SerializeField] internal Color overlayActiveColor = Color.white;
        [SerializeField] internal Color overlayInactiveColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        // Color tags
        [SerializeField] internal List<AnimatorColorTag> colorTags = new List<AnimatorColorTag>();

        // User palette slots
        [SerializeField] internal List<AnimatorPalette> savedPalettes = new List<AnimatorPalette>();

        internal static Color? GetTagColor(string tagName, AnimatorDefaultSettings settings)
        {
            if (string.IsNullOrEmpty(tagName) || settings.colorTags.Count == 0) return null;
            foreach (var colorTag in settings.colorTags)
                if (colorTag.tagName == tagName) return colorTag.color;
            return null;
        }

        // Transition overlay
        [SerializeField] internal bool  transitionOverlayEnabled          = false;
        [SerializeField] internal bool  transitionIndicatorArrowsEnabled  = true;
        [SerializeField] internal Color transitionOverlayColor            = new Color(1.0f, 1.0f, 1.0f, 1.0f);
        [SerializeField] internal bool  transitionSelectionColorEnabled    = true;
        [SerializeField] internal Color transitionIncomingColor           = new Color(0.0f, 1.0f, 1.0f, 1.0f);
        [SerializeField] internal Color transitionOutgoingColor           = new Color(1.0f, 0.0f, 1.0f, 1.0f);
        [SerializeField] internal Color transitionOverlayArrowColor       = new Color(0.6f, 0.6f, 0.6f, 1.0f);
        [SerializeField] internal Color transitionArrowNoConditionColor   = new Color(1.0f, 0.28f, 0.0f, 1.0f);
        [SerializeField] internal Color transitionArrowInstantColor       = new Color(0.0f, 0.25f, 0.66f, 1.0f);
        [SerializeField] internal float transitionOverlayWidth            = 3f;
        [SerializeField] internal bool  transitionShowLabel               = true;
        [SerializeField] internal bool  transitionAnimateSelected         = true;
        [SerializeField] internal bool  transitionGradientEnabled         = false;
        [SerializeField] internal Color transitionGradientInColorA        = new Color(0.0f, 1.0f, 1.0f, 1.0f);
        [SerializeField] internal Color transitionGradientInColorB        = new Color(0.45f, 0.0f, 1.0f, 1.0f);
        [SerializeField] internal float transitionGradientInSpeed         = 0.15f;
        [SerializeField] internal Color transitionGradientOutColorA       = new Color(1.0f, 0.0f, 1.0f, 1.0f);
        [SerializeField] internal Color transitionGradientOutColorB       = new Color(1.0f, 0.0f, 0.0f, 1.0f);
        [SerializeField] internal float transitionGradientOutSpeed        = 0.15f;

        // Graph background + grid
        [SerializeField] internal bool   graphGridOverride        = false;
        [SerializeField] internal bool   graphGridUseImage        = false;
        [SerializeField] internal string graphGridBackgroundImagePath = "";
        [NonSerialized]  internal Texture2D graphGridBackgroundImage = null;
        [SerializeField] internal float  graphGridBackgroundImageOpacity = 1f;
        [SerializeField] internal Color  graphGridBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        [SerializeField] internal Color  graphGridColorMajor      = new Color(0.30f, 0.30f, 0.30f, 1f);
        [SerializeField] internal Color  graphGridColorMinor      = new Color(0.22f, 0.22f, 0.22f, 1f);
        [SerializeField] internal float  graphGridScalingMajor    = 1f;
        [SerializeField] internal int    graphGridDivisorMinor    = 5;
        [SerializeField] internal bool   graphGridDrawLines       = true;

        internal void ResetGraphGrid()
        {
            graphGridBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            graphGridColorMajor      = new Color(0.30f, 0.30f, 0.30f, 1f);
            graphGridColorMinor      = new Color(0.22f, 0.22f, 0.22f, 1f);
            graphGridScalingMajor    = 1f;
            graphGridDivisorMinor    = 5;
            graphGridDrawLines       = true;
        }

        // Node colors
        [SerializeField] internal bool  nodeColorEnabled      = false;
        [SerializeField] internal bool  nodeColor3DEnabled    = false;
        [SerializeField] internal Color nodeSelectionColor      = new(1f, 1f, 1f, 1f);
        [SerializeField] internal Color stateNodeColor        = new(0.30f, 0.30f, 0.30f, 1f);
        [SerializeField] internal Color defaultStateColor     = new(0.60f, 0.35f, 0.10f, 1f);
        [SerializeField] internal Color subStateMachineColor  = new(0.35f, 0.25f, 0.50f, 1f);
        [SerializeField] internal Color entryNodeColor        = new(0.20f, 0.55f, 0.20f, 1f);
        [SerializeField] internal Color exitNodeColor         = new(0.55f, 0.15f, 0.15f, 1f);
        [SerializeField] internal Color anyStateNodeColor     = new(0.15f, 0.40f, 0.50f, 1f);
        [SerializeField] internal Color blendTreeDirectNodeColor = new(0.70f, 0.37f, 0.20f, 1f);
        [SerializeField] internal Color blendTree1DNodeColor     = new(0.24f, 0.50f, 0.60f, 1f);
        [SerializeField] internal Color blendTree2DNodeColor     = new(0.24f, 0.60f, 0.45f, 1f);

        // Analysis highlight
        [SerializeField] internal Color analysisHighlightColor = Color.red;

        internal void ResetNodeColors()
        {
            nodeSelectionColor      = new(1f, 1f, 1f, 1f);
            stateNodeColor          = new(0.30f, 0.30f, 0.30f, 1f);
            defaultStateColor       = new(0.60f, 0.35f, 0.10f, 1f);
            subStateMachineColor    = new(0.35f, 0.25f, 0.50f, 1f);
            entryNodeColor          = new(0.20f, 0.55f, 0.20f, 1f);
            exitNodeColor           = new(0.55f, 0.15f, 0.15f, 1f);
            anyStateNodeColor       = new(0.15f, 0.40f, 0.50f, 1f);
            blendTreeDirectNodeColor = new(0.70f, 0.37f, 0.20f, 1f);
            blendTree1DNodeColor    = new(0.24f, 0.50f, 0.60f, 1f);
            blendTree2DNodeColor    = new(0.24f, 0.60f, 0.45f, 1f);
        }

        // ── Palette capture / apply ───────────────────────────────────────────

        internal const int PaletteColorCount = 30;

        // Order matches visual section order in settings UI:
        // Interface → Graph Grid → Node Colors → Node Overlay → Transition Overlay
        internal static Color[] CapturePaletteColors(AnimatorDefaultSettings settings) => new[]
        {
            // Interface [0–8]
            settings.paletteColorPrimary,   settings.paletteColorSecondary, settings.paletteColorAccent,
            settings.paramColorFloat,        settings.paramColorInt,         settings.paramColorBool,
            settings.paramColorTrigger,      settings.paramColorVrcLabel,    settings.analysisHighlightColor,
            // Graph Grid [9–11]
            settings.graphGridBackgroundColor, settings.graphGridColorMajor, settings.graphGridColorMinor,
            // Node Colors [12–21]
            settings.nodeSelectionColor,     settings.stateNodeColor,        settings.defaultStateColor,
            settings.subStateMachineColor,   settings.entryNodeColor,        settings.exitNodeColor,
            settings.anyStateNodeColor,      settings.blendTreeDirectNodeColor, settings.blendTree1DNodeColor,
            settings.blendTree2DNodeColor,
            // Node Overlay [22–23]
            settings.overlayActiveColor,     settings.overlayInactiveColor,
            // Transition Overlay [24–29]
            settings.transitionOverlayColor, settings.transitionIncomingColor, settings.transitionOutgoingColor,
            settings.transitionOverlayArrowColor, settings.transitionArrowNoConditionColor, settings.transitionArrowInstantColor,
        };

        internal static void ApplyPaletteColors(AnimatorDefaultSettings settings, Color[] colors)
        {
            // Interface [0–8]
            settings.paletteColorPrimary          = ClampPaletteColor(colors[0]);
            settings.paletteColorSecondary        = ClampPaletteColor(colors[1]);
            settings.paletteColorAccent           = ClampPaletteColor(colors[2]);
            settings.paramColorFloat              = colors[3];
            settings.paramColorInt                = colors[4];
            settings.paramColorBool               = colors[5];
            settings.paramColorTrigger            = colors[6];
            settings.paramColorVrcLabel           = colors[7];
            settings.analysisHighlightColor       = colors[8];
            // Graph Grid [9–11]
            settings.graphGridBackgroundColor     = colors[9];
            settings.graphGridColorMajor          = colors[10];
            settings.graphGridColorMinor          = colors[11];
            // Node Colors [12–21]
            settings.nodeSelectionColor           = colors[12];
            settings.stateNodeColor               = colors[13];
            settings.defaultStateColor            = colors[14];
            settings.subStateMachineColor         = colors[15];
            settings.entryNodeColor               = colors[16];
            settings.exitNodeColor                = colors[17];
            settings.anyStateNodeColor            = colors[18];
            settings.blendTreeDirectNodeColor     = colors[19];
            settings.blendTree1DNodeColor         = colors[20];
            settings.blendTree2DNodeColor         = colors[21];
            // Node Overlay [22–23]
            settings.overlayActiveColor           = colors[22];
            settings.overlayInactiveColor         = colors[23];
            // Transition Overlay [24–29]
            settings.transitionOverlayColor       = colors[24];
            settings.transitionIncomingColor      = colors[25];
            settings.transitionOutgoingColor      = colors[26];
            settings.transitionOverlayArrowColor  = colors[27];
            settings.transitionArrowNoConditionColor = colors[28];
            settings.transitionArrowInstantColor  = colors[29];
        }

        internal static string EncodePalette(Color[] colors)
        {
            var tokens = new string[PaletteColorCount];
            for (int i = 0; i < PaletteColorCount; i++)
                tokens[i] = ColorUtility.ToHtmlStringRGBA(colors[i]);
            return string.Join("|", tokens);
        }

        internal static bool TryDecodePalette(string encoded, out Color[] colors)
        {
            colors = null;
            if (string.IsNullOrEmpty(encoded)) return false;
            var tokens = encoded.Split('|');
            if (tokens.Length != PaletteColorCount)
            {
                Debug.LogWarning($"[AnimatorTools] Palette string has {tokens.Length} tokens, expected {PaletteColorCount}.");
                return false;
            }
            colors = new Color[PaletteColorCount];
            for (int i = 0; i < PaletteColorCount; i++)
            {
                if (!ColorUtility.TryParseHtmlString("#" + tokens[i], out colors[i]))
                {
                    Debug.LogWarning($"[AnimatorTools] Palette token {i} could not be parsed: '{tokens[i]}'.");
                    colors = null;
                    return false;
                }
            }
            return true;
        }

        static Color ClampPaletteColor(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            value = EditorGUIUtility.isProSkin ? Mathf.Min(value, 0.40f) : Mathf.Max(value, 0.70f);
            var clamped = Color.HSVToRGB(hue, saturation, value);
            clamped.a = color.a;
            return clamped;
        }

        // Editor palette
        [SerializeField] internal Color paletteColorPrimary   = new(0.25f, 0.25f, 0.25f, 1f);
        [SerializeField] internal Color paletteColorSecondary = new(0.30f, 0.30f, 0.30f, 1f);
        [SerializeField] internal Color paletteColorAccent    = new(0.20f, 0.20f, 0.20f, 1f);

        internal static Color DefaultPrimary   => EditorGUIUtility.isProSkin ? new(0.25f, 0.25f, 0.25f, 1f) : new(0.82f, 0.82f, 0.82f, 1f);
        internal static Color DefaultSecondary => EditorGUIUtility.isProSkin ? new(0.30f, 0.30f, 0.30f, 1f) : new(0.76f, 0.76f, 0.76f, 1f);
        internal static Color DefaultAccent    => EditorGUIUtility.isProSkin ? new(0.20f, 0.20f, 0.20f, 1f) : new(0.70f, 0.70f, 0.70f, 1f);

        internal void ResetPalette()
        {
            paletteColorPrimary    = DefaultPrimary;
            paletteColorSecondary  = DefaultSecondary;
            paletteColorAccent     = DefaultAccent;
            paramColorFloat        = new Color(0.35f, 0.75f, 0.35f, 1f);
            paramColorInt          = new Color(0.35f, 0.60f, 1.00f, 1f);
            paramColorBool         = new Color(1.00f, 0.55f, 0.20f, 1f);
            paramColorTrigger      = new Color(0.85f, 0.30f, 0.85f, 1f);
            paramColorVrcLabel     = Color.cyan;
            analysisHighlightColor = Color.red;
        }

        // Transition defaults
        [SerializeField] internal bool applyToTransitions = true;
        [SerializeField] internal bool transHasExitTime = false;
        [SerializeField] internal float transExitTime = 1f;
        [SerializeField] internal bool transHasFixedDuration = true;
        [SerializeField] internal float transDuration = 0f;
        [SerializeField] internal float transOffset = 0f;
        [SerializeField] internal TransitionInterruptionSource transInterruptionSource = TransitionInterruptionSource.None;
        [SerializeField] internal bool transOrderedInterruption = true;
        [SerializeField] internal bool transMute = false;
        [SerializeField] internal bool transSolo = false;
        [SerializeField] internal bool transCanTransitionToSelfAnyState = false;

        // Miscellaneous
        [SerializeField] internal bool framesEnabled              = true;
        [SerializeField] internal bool inspectorModeEnabled       = false;
        [SerializeField] internal bool wdIncludeBlendTreeStates  = false;
        [SerializeField] internal bool preventLayerScroll        = true;
        [SerializeField] internal bool preventParameterScroll    = true;
        [SerializeField] internal bool clipMenuNestingEnabled  = true;
        [SerializeField] internal char clipMenuNestingDelimiter = '.';
        [SerializeField] internal bool layerTemplateButtonEnabled = true;
        [SerializeField] internal bool parameterAddMenuEnabled   = true;

        // Keybindings
        [SerializeField] internal KeyBinding kbSelectIncoming       = new(KeyCode.I);
        [SerializeField] internal KeyBinding kbSelectOutgoing       = new(KeyCode.O);
        [SerializeField] internal KeyBinding kbSelectBoth           = new(KeyCode.P);
        [SerializeField] internal KeyBinding kbSelectAll            = new(KeyCode.A, ctrl: true);
        [SerializeField] internal KeyBinding kbSelectAllTransitions = new(KeyCode.A, ctrl: true, shift: true);
        [SerializeField] internal KeyBinding kbCopy                 = new(KeyCode.C, ctrl: true);
        [SerializeField] internal KeyBinding kbPaste                = new(KeyCode.V, ctrl: true);
        [SerializeField] internal KeyBinding kbDuplicate            = new(KeyCode.D, ctrl: true);
        [SerializeField] internal KeyBinding kbChainMode            = new(KeyCode.None);
        [SerializeField] internal KeyBinding kbFanMode              = new(KeyCode.None);
        [SerializeField] internal KeyBinding kbMultiTransition      = new(KeyCode.None);
        [SerializeField] internal KeyBinding kbReverseTransitions   = new(KeyCode.None);
        [SerializeField] internal KeyBinding kbReplicate            = new(KeyCode.None);
        [SerializeField] internal KeyBinding kbRedirect             = new(KeyCode.None);

        internal void ResetKeybinds()
        {
            kbSelectIncoming       = new(KeyCode.I);
            kbSelectOutgoing       = new(KeyCode.O);
            kbSelectBoth           = new(KeyCode.P);
            kbSelectAll            = new(KeyCode.A, ctrl: true);
            kbSelectAllTransitions = new(KeyCode.A, ctrl: true, shift: true);
            kbCopy                 = new(KeyCode.C, ctrl: true);
            kbPaste                = new(KeyCode.V, ctrl: true);
            kbDuplicate            = new(KeyCode.D, ctrl: true);
            kbChainMode            = new(KeyCode.None);
            kbFanMode              = new(KeyCode.None);
            kbMultiTransition      = new(KeyCode.None);
            kbReverseTransitions   = new(KeyCode.None);
            kbReplicate            = new(KeyCode.None);
            kbRedirect             = new(KeyCode.None);
        }

        // State defaults
        [SerializeField] internal bool applyToStates = true;
        [SerializeField] internal string stateTag = "";
        [SerializeField] internal float stateSpeed = 1f;
        [SerializeField] internal bool stateSpeedParameterActive = false;
        [SerializeField] internal string stateSpeedParameter = "";
        [SerializeField] internal bool stateTimeParameterActive = false;
        [SerializeField] internal string stateTimeParameter = "";
        [SerializeField] internal bool stateMirror = false;
        [SerializeField] internal bool stateMirrorParameterActive = false;
        [SerializeField] internal float stateCycleOffset = 0f;
        [SerializeField] internal bool stateCycleOffsetParameterActive = false;
        [SerializeField] internal bool stateWriteDefaultValues = true;
        [SerializeField] internal bool stateIKOnFeet = false;

        // ── Static access ─────────────────────────────────────────────────────

        static AnimatorDefaultSettings _instance;

        internal static AnimatorDefaultSettings Load()
        {
            if (_instance != null) return _instance;
            _instance = new AnimatorDefaultSettings();
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
                JsonUtility.FromJsonOverwrite(json, _instance);
            if (!string.IsNullOrEmpty(_instance.graphGridBackgroundImagePath))
                _instance.graphGridBackgroundImage = AssetDatabase.LoadAssetAtPath<Texture2D>(_instance.graphGridBackgroundImagePath);
            return _instance;
        }

        internal void Save()
        {
            graphGridBackgroundImagePath = graphGridBackgroundImage != null
                ? AssetDatabase.GetAssetPath(graphGridBackgroundImage) : "";
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PatchNodeStyles.Invalidate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ── Creation defaults ─────────────────────────────────────────────────

        /* Applies all configured transition defaults (exit time, duration, interruption, etc.) to the given transition. */
        internal static void ApplyTransitionDefaults(AnimatorStateTransition transition, bool isAnyStateSource = false)
        {
            var settings = Load();
            transition.hasExitTime         = settings.transHasExitTime;
            transition.exitTime            = settings.transExitTime;
            transition.hasFixedDuration    = settings.transHasFixedDuration;
            transition.duration            = settings.transDuration;
            transition.offset              = settings.transOffset;
            transition.interruptionSource  = settings.transInterruptionSource;
            transition.orderedInterruption = settings.transOrderedInterruption;
            transition.mute                = settings.transMute;
            transition.solo                = settings.transSolo;
            if (isAnyStateSource) transition.canTransitionToSelf = settings.transCanTransitionToSelfAnyState;
        }

        /* Applies all configured state defaults (tag, speed, mirror, WD, IK, etc.) to the given state. */
        internal static void ApplyStateDefaults(AnimatorState state)
        {
            var settings = Load();
            state.tag                        = settings.stateTag;
            state.speed                      = settings.stateSpeed;
            state.speedParameterActive       = settings.stateSpeedParameterActive;
            state.speedParameter             = settings.stateSpeedParameter;
            state.timeParameterActive        = settings.stateTimeParameterActive;
            state.timeParameter              = settings.stateTimeParameter;
            state.mirror                     = settings.stateMirror;
            state.mirrorParameterActive      = settings.stateMirrorParameterActive;
            state.cycleOffset                = settings.stateCycleOffset;
            state.cycleOffsetParameterActive = settings.stateCycleOffsetParameterActive;
            state.writeDefaultValues         = settings.stateWriteDefaultValues;
            state.iKOnFeet                   = settings.stateIKOnFeet;
        }
    }
}
#endif
