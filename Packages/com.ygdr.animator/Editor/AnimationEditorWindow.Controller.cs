#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        enum WDState { On, Off, Mixed }

        static GUIStyle _writeDefaultsHeaderStyle;
        static GUIStyle _writeDefaultsLabelStyle;
        static GUIStyle WDHeaderStyle => _writeDefaultsHeaderStyle ??= new GUIStyle(Styles.HeaderLabel) { alignment = TextAnchor.MiddleCenter };
        static GUIStyle WDLabelStyle  => _writeDefaultsLabelStyle  ??= new GUIStyle(Styles.SmallLabel)  { alignment = TextAnchor.MiddleCenter };

        void DrawControllerTab()
        {
            DrawWriteDefaultsSection();
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawNetworkSyncSection();
        }

        // ── Write Defaults ────────────────────────────────────────────────────

        void DrawWriteDefaultsSection()
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_controller == null))
                {
                    if (CursorBtn("Set All WD On", Styles.IconBtn, GUILayout.Height(24)))
                        SetAllLayersWD(true);
                }
                GUILayout.Space(30);
                GUILayout.Label("Write Defaults", Styles.HeaderLabel, GUILayout.Height(24));
                GUILayout.Space(30);
                using (new EditorGUI.DisabledScope(_controller == null))
                {
                    if (CursorBtn("Set All WD Off", Styles.IconBtn, GUILayout.Height(24)))
                        SetAllLayersWD(false);
                }
                GUILayout.FlexibleSpace();
            }

            if (_controller == null)
            {
                EditorGUILayout.LabelField("No controller selected", Styles.EmptyLabel);
                return;
            }

            var layers = _controller.layers;
            var onLayers    = layers.Where(layer => GetLayerWDState(layer) == WDState.On).ToArray();
            var offLayers   = layers.Where(layer => GetLayerWDState(layer) == WDState.Off).ToArray();
            var mixedLayers = layers.Where(layer => GetLayerWDState(layer) == WDState.Mixed).ToArray();

            // WD On / WD Off — 4-quarter grid: [name][→][←][name]
            EditorGUILayout.Space(4);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float rowHeight  = lineHeight + EditorGUIUtility.standardVerticalSpacing;
            int   maxRows    = Mathf.Max(onLayers.Length, offLayers.Length);
            float totalHeight = 28f + Mathf.Max(maxRows, 1) * rowHeight;

            var rect         = EditorGUILayout.GetControlRect(false, totalHeight);
            float halfWidth  = rect.width / 2f;
            float quarterWidth = halfWidth / 2f;

            // Headers
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 24f), GUIContent.none, Styles.SectionHeader);
            GUI.Label(new Rect(rect.x,             rect.y, halfWidth, 24f), "WD On",  WDHeaderStyle);
            GUI.Label(new Rect(rect.x + halfWidth, rect.y, halfWidth, 24f), "WD Off", WDHeaderStyle);

            float rowY = rect.y + 26f;

            if (maxRows == 0)
            {
                GUI.Label(new Rect(rect.x, rowY, rect.width, lineHeight), "—", Styles.EmptyLabel);
            }
            else
            {
                for (int i = 0; i < maxRows; i++, rowY += rowHeight)
                {
                    bool hasOn  = i < onLayers.Length;
                    bool hasOff = i < offLayers.Length;

                    if (hasOn)
                        GUI.Label(new Rect(rect.x, rowY, quarterWidth, lineHeight), onLayers[i].name, WDLabelStyle);

                    if (hasOn && CursorBtn(new Rect(rect.x + halfWidth - 24f, rowY, 24f, lineHeight), "→", Styles.IconBtn))
                        SetLayerWD(onLayers[i], false);

                    if (hasOff && CursorBtn(new Rect(rect.x + halfWidth, rowY, 24f, lineHeight), "←", Styles.IconBtn))
                        SetLayerWD(offLayers[i], true);

                    if (hasOff)
                        GUI.Label(new Rect(rect.x + halfWidth + quarterWidth, rowY, quarterWidth, lineHeight), offLayers[i].name, WDLabelStyle);
                }
            }

            // Mixed
            if (mixedLayers.Length > 0)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Mixed", WDHeaderStyle, GUILayout.Height(24));
                    GUILayout.FlexibleSpace();
                }

                foreach (var layer in mixedLayers)
                {
                    var rowRect    = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    float btnWidth  = 48f;
                    float gap       = 8f;
                    float nameWidth = WDLabelStyle.CalcSize(new GUIContent(layer.name)).x;
                    float groupWidth = btnWidth + gap + nameWidth + gap + btnWidth;
                    float groupX    = rowRect.x + (rowRect.width - groupWidth) / 2f;

                    if (CursorBtn(new Rect(groupX, rowRect.y, btnWidth, rowRect.height), "← On", Styles.IconBtn))
                        SetLayerWD(layer, true);
                    GUI.Label(new Rect(groupX + btnWidth + gap, rowRect.y, nameWidth, rowRect.height), layer.name, WDLabelStyle);
                    if (CursorBtn(new Rect(groupX + btnWidth + gap + nameWidth + gap, rowRect.y, btnWidth, rowRect.height), "→ Off", Styles.IconBtn))
                        SetLayerWD(layer, false);
                }
            }
        }

        // ── Network Sync ──────────────────────────────────────────────────────

        bool   _networkUseBool;
        string _networkParamName        = "network";
        string _networkStatesPrefix     = "{N}";
        bool   _networkRemoveParamDrivers;
        bool   _networkRemoveAudioPlay;
        bool   _networkRemoveTracking;
        bool   _networkAnyStateTransitions;
        bool   _networkPackIntoSubSM;

        void DrawNetworkSyncSection()
        {
            using (new EditorGUILayout.HorizontalScope(Styles.SectionHeader))
                GUILayout.Label("Network Sync", Styles.HeaderLabel, GUILayout.Height(24));

            if (_activeStateMachine == null)
            {
                EditorGUILayout.LabelField("No animator window open", Styles.EmptyLabel);
                return;
            }

            EditorGUILayout.Space(4);

            DrawNetworkToggleRow("Sync Param Type", ref _networkUseBool,             "Int",        "Bool");
            DrawNetworkToggleRow("Transitions",     ref _networkAnyStateTransitions,  "All-to-All", "Any State");

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Sync Param Name", Styles.SmallLabel, GUILayout.Width(164));
                _networkParamName = EditorGUILayout.TextField(_networkParamName);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Network States Prefix", Styles.SmallLabel, GUILayout.Width(164));
                _networkStatesPrefix = EditorGUILayout.TextField(_networkStatesPrefix);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Remove Network Behaviours", Styles.SmallLabel, GUILayout.Width(164));
                GUILayout.Label("Params", Styles.SmallLabel, GUILayout.Width(50));
                _networkRemoveParamDrivers = EditorGUILayout.Toggle(_networkRemoveParamDrivers, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label("Audio", Styles.SmallLabel, GUILayout.Width(36));
                _networkRemoveAudioPlay = EditorGUILayout.Toggle(_networkRemoveAudioPlay, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label("Tracking", Styles.SmallLabel, GUILayout.Width(52));
                _networkRemoveTracking = EditorGUILayout.Toggle(_networkRemoveTracking, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Pack into SubSM", Styles.SmallLabel, GUILayout.Width(164));
                _networkPackIntoSubSM = EditorGUILayout.Toggle(_networkPackIntoSubSM, GUILayout.Width(16));
            }

            EditorGUILayout.Space(6);

            bool hasStates = true;
            bool canRun    = hasStates && !string.IsNullOrWhiteSpace(_networkParamName) && !string.IsNullOrWhiteSpace(_networkStatesPrefix);

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (CursorBtn("Run Network Sync", Styles.IconBtn, GUILayout.Height(28)))
                {
                    AnimatorNetworkSync.NetworkSync(_activeStateMachine, new NetworkSyncConfig
                    {
                        useBool              = _networkUseBool,
                        paramName            = _networkParamName.Trim(),
                        statesPrefix         = _networkStatesPrefix,
                        removeParamDrivers   = _networkRemoveParamDrivers,
                        removeAudioPlay      = _networkRemoveAudioPlay,
                        removeTracking       = _networkRemoveTracking,
                        anyStateTransitions  = _networkAnyStateTransitions,
                        packIntoSubSM        = _networkPackIntoSubSM
                    });
                }
            }
        }

        /* Draws a two-button exclusive toggle row with a left-aligned label and cursor-rect on both buttons. */
        static void DrawNetworkToggleRow(string label, ref bool value, string falseLabel, string trueLabel)
        {
            var rect           = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float buttonWidth  = (rect.width - 164f) / 2f;
            float firstButtonX = rect.x + 164f;
            float secondButtonX = firstButtonX + buttonWidth;

            GUI.Label(new Rect(rect.x, rect.y, 164f, rect.height), label, Styles.SmallLabel);

            if (GUI.Toggle(new Rect(firstButtonX, rect.y, buttonWidth, rect.height), !value, falseLabel, Styles.IconBtn)) value = false;
            EditorGUIUtility.AddCursorRect(new Rect(firstButtonX, rect.y, buttonWidth, rect.height), MouseCursor.Link);

            if (GUI.Toggle(new Rect(secondButtonX, rect.y, buttonWidth, rect.height), value, trueLabel, Styles.IconBtn)) value = true;
            EditorGUIUtility.AddCursorRect(new Rect(secondButtonX, rect.y, buttonWidth, rect.height), MouseCursor.Link);
        }

        // ── WD helpers ────────────────────────────────────────────────────────

        /* Returns On, Off, or Mixed depending on whether states in the layer have Write Defaults enabled, disabled, or both. */
        WDState GetLayerWDState(AnimatorControllerLayer layer)
        {
            bool hasOn = false, hasOff = false;
            CollectWDState(layer.stateMachine, ref hasOn, ref hasOff);
            if (hasOn && hasOff) return WDState.Mixed;
            return hasOn ? WDState.On : WDState.Off;
        }

        /* Recursively sets hasOn and hasOff flags based on writeDefaultValues across all states in sm and its sub SMs. */
        static void CollectWDState(AnimatorStateMachine sm, ref bool hasOn, ref bool hasOff)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.writeDefaultValues) hasOn = true;
                else hasOff = true;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CollectWDState(childStateMachine.stateMachine, ref hasOn, ref hasOff);
        }

        /* Sets Write Defaults on all states in a layer recursively and marks the controller dirty. */
        void SetLayerWD(AnimatorControllerLayer layer, bool value)
        {
            SetSMWD(layer.stateMachine, value);
            EditorUtility.SetDirty(_controller);
        }

        /* Recursively sets writeDefaultValues on all states in sm and its sub SMs, registering each for undo. */
        static void SetSMWD(AnimatorStateMachine sm, bool value)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Set Write Defaults");
            foreach (var childState in sm.states)
            {
                Undo.RecordObject(childState.state, "Set Write Defaults");
                childState.state.writeDefaultValues = value;
                EditorUtility.SetDirty(childState.state);
            }
            foreach (var childStateMachine in sm.stateMachines)
                SetSMWD(childStateMachine.stateMachine, value);
            EditorUtility.SetDirty(sm);
        }

        void SetAllLayersWD(bool value)
        {
            foreach (var layer in _controller.layers)
                SetLayerWD(layer, value);
        }
    }
}
#endif
