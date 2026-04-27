#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [Serializable]
    internal class AnimatorDefaultSettings
    {
        const string PrefsKey = "YGDR.AnimatorTools.Settings";

        // Window behavior
        [SerializeField] internal bool fixLayerScrollReset  = true;
        [SerializeField] internal bool scrollToNewParameter = true;
        [SerializeField] internal bool defaultLayerWeight1  = true;

        // Parameter list overlays
        [SerializeField] internal bool  showParamTypeLabels  = true;
        [SerializeField] internal Color paramColorFloat   = new Color(0.35f, 0.75f, 0.35f, 1f);
        [SerializeField] internal Color paramColorInt     = new Color(0.35f, 0.60f, 1.00f, 1f);
        [SerializeField] internal Color paramColorBool    = new Color(1.00f, 0.55f, 0.20f, 1f);
        [SerializeField] internal Color paramColorTrigger = new Color(0.85f, 0.30f, 0.85f, 1f);

        // Layer list overlays
        [SerializeField] internal bool  showLayerWDIndicator = true;
        [SerializeField] internal Color layerWDColor    = new Color(0.30f, 0.90f, 0.40f, 1f);
        [SerializeField] internal Color layerEmptyColor = new Color(1.00f, 0.40f, 0.20f, 1f);

        // Overlay indicators
        [SerializeField] internal bool overlayEnabled    = true;
        [SerializeField] internal bool overlayShowWD     = true;
        [SerializeField] internal bool overlayShowB      = true;
        [SerializeField] internal bool overlayShowLoop   = true;
        [SerializeField] internal bool overlayShowEmpty      = true;
        [SerializeField] internal bool overlayShowSpeed      = true;
        [SerializeField] internal bool overlayShowMotion     = true;
        [SerializeField] internal bool overlayShowMotionName = true;
        [SerializeField] internal bool overlayShowCoords    = false;
        [SerializeField] internal Color overlayActiveColor = Color.white;
        [SerializeField] internal Color overlayInactiveColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        // Transition overlay
        [SerializeField] internal bool  transitionOverlayEnabled          = false;
        [SerializeField] internal bool  transitionIndicatorArrowsEnabled  = true;
        [SerializeField] internal Color transitionOverlayColor            = new Color(1.0f, 1.0f, 1.0f, 1.0f);
        [SerializeField] internal bool  transitionSelectionColorEnabled    = true;
        [SerializeField] internal Color transitionIncomingColor           = new Color(0.2f, 0.9f, 0.3f, 1.0f);
        [SerializeField] internal Color transitionOutgoingColor           = new Color(1.0f, 0.45f, 0.1f, 1.0f);
        [SerializeField] internal Color transitionOverlayArrowColor       = new Color(0.6f, 0.6f, 0.6f, 1.0f);
        [SerializeField] internal Color transitionArrowNoConditionColor   = new Color(1.0f, 0.28f, 0.0f, 1.0f);
        [SerializeField] internal Color transitionArrowInstantColor       = new Color(0.0f, 0.25f, 0.66f, 1.0f);
        [SerializeField] internal float transitionOverlayWidth            = 3f;
        [SerializeField] internal bool  transitionShowLabel               = true;
        [SerializeField] internal bool  transitionAnimateSelected         = true;

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

        internal void ResetGraphGrid()
        {
            graphGridBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            graphGridColorMajor      = new Color(0.30f, 0.30f, 0.30f, 1f);
            graphGridColorMinor      = new Color(0.22f, 0.22f, 0.22f, 1f);
            graphGridScalingMajor    = 1f;
            graphGridDivisorMinor    = 5;
        }

        // Node colors
        [SerializeField] internal bool  nodeColorEnabled      = false;
        [SerializeField] internal bool  nodeColor3DEnabled    = false;
        [SerializeField] internal Color stateNodeColor        = new(0.30f, 0.30f, 0.30f, 1f);
        [SerializeField] internal Color defaultStateColor     = new(0.60f, 0.35f, 0.10f, 1f);
        [SerializeField] internal Color subStateMachineColor  = new(0.35f, 0.25f, 0.50f, 1f);
        [SerializeField] internal Color entryNodeColor        = new(0.20f, 0.55f, 0.20f, 1f);
        [SerializeField] internal Color exitNodeColor         = new(0.55f, 0.15f, 0.15f, 1f);
        [SerializeField] internal Color anyStateNodeColor     = new(0.15f, 0.40f, 0.50f, 1f);

        internal void ResetNodeColors()
        {
            stateNodeColor       = new(0.30f, 0.30f, 0.30f, 1f);
            defaultStateColor    = new(0.60f, 0.35f, 0.10f, 1f);
            subStateMachineColor = new(0.35f, 0.25f, 0.50f, 1f);
            entryNodeColor       = new(0.20f, 0.55f, 0.20f, 1f);
            exitNodeColor        = new(0.55f, 0.15f, 0.15f, 1f);
            anyStateNodeColor    = new(0.15f, 0.40f, 0.50f, 1f);
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
        [SerializeField] internal bool transCanTransitionToSelf = false;

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
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ── Creation defaults ─────────────────────────────────────────────────

        /* Applies all configured transition defaults (exit time, duration, interruption, etc.) to the given transition. */
        internal static void ApplyTransitionDefaults(AnimatorStateTransition transition)
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
            transition.canTransitionToSelf = settings.transCanTransitionToSelf;
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
