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

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        enum WDState { On, Off, Mixed }


        int _controllerSubTab;

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

        void DrawControllerTab()
        {
            var panelRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && panelRect.height > 0)
                EditorGUI.DrawRect(panelRect, Styles.PrimaryColor);
            DrawControllerSubTabs();
            EditorGUILayout.Space(8);
            if (_controllerSubTab == 0)      DrawWriteDefaultsSection();
            else if (_controllerSubTab == 1) DrawNetworkSyncSection();
            else                             DrawSubAssetsSection();
            EditorGUILayout.EndVertical();
        }

        void DrawControllerSubTabs()
        {
            var rowRect      = EditorGUILayout.GetControlRect(false, 24f);
            float tabsWidth  = rowRect.width / 2f;
            float cleanWidth = rowRect.width / 4f;

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, tabsWidth, rowRect.height), Styles.PrimaryColor);

            string[] labels = { L10n.Get("controller.subtab.wd"), L10n.Get("controller.subtab.network_sync"), L10n.Get("controller.subtab.sub_assets") };
            float tabWidth   = tabsWidth / 3f;
            for (int i = 0; i < labels.Length; i++)
            {
                var tabRect = new Rect(rowRect.x + i * tabWidth, rowRect.y, tabWidth, 24f);
                var style   = _controllerSubTab == i ? Styles.ControllerSubTabBtnActive : Styles.ControllerSubTabBtn;
                if (GUI.Toggle(tabRect, _controllerSubTab == i, labels[i], style))
                    _controllerSubTab = i;
                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }

            int orphanCount  = _orphanedAssets?.Length ?? 0;
            var cleanBtnRect = new Rect(rowRect.xMax - cleanWidth, rowRect.y, cleanWidth, 24f);
            if (CursorBtn(cleanBtnRect, L10n.Get("controller.clean").Replace("{n}", orphanCount.ToString()), Styles.ControllerSubTabBtn) && orphanCount > 0)
                CleanOrphanedAssets();
        }

        // ── Write Defaults ────────────────────────────────────────────────────

        void DrawWriteDefaultsSection()
        {
            if (_controller == null)
            {
                EditorGUILayout.LabelField(L10n.Get("controller.no_controller"), Styles.EmptyLabel);
                return;
            }

            var layers      = _controller.layers;
            var onLayers    = layers.Where(layer => GetLayerWDState(layer) == WDState.On).ToArray();
            var offLayers   = layers.Where(layer => GetLayerWDState(layer) == WDState.Off).ToArray();
            var mixedLayers = layers.Where(layer => GetLayerWDState(layer) == WDState.Mixed).ToArray();

            const float middleGap = 8f;

            var btnRowRect  = EditorGUILayout.GetControlRect(false, 24f);
            float pillW     = Styles.k_pillW;
            float halfWidth = (btnRowRect.width - middleGap) / 2f;

            if (CursorBtn(new Rect(btnRowRect.x,                         btnRowRect.y, halfWidth, 24f), L10n.Get("controller.wd.set_all_on"),  Styles.IconBtn))
                SetAllLayersWD(true);
            if (CursorBtn(new Rect(btnRowRect.x + halfWidth + middleGap, btnRowRect.y, halfWidth, 24f), L10n.Get("controller.wd.set_all_off"), Styles.IconBtn))
                SetAllLayersWD(false);

            float lineHeight   = EditorGUIUtility.singleLineHeight;
            float rowHeight    = lineHeight + 2f;
            int   maxRows      = Mathf.Max(onLayers.Length, offLayers.Length);
            float maxVisibleH  = 8f * rowHeight;
            float onTotalRowH  = Mathf.Max(onLayers.Length,  1) * rowHeight;
            float offTotalRowH = Mathf.Max(offLayers.Length, 1) * rowHeight;
            float onDisplayH   = _wdScrollEnabled ? Mathf.Min(onTotalRowH,  maxVisibleH) : onTotalRowH;
            float offDisplayH  = _wdScrollEnabled ? Mathf.Min(offTotalRowH, maxVisibleH) : offTotalRowH;
            float rowsDisplayH = Mathf.Max(onDisplayH, offDisplayH);
            float totalHeight  = 24f + rowsDisplayH;

            var rect = EditorGUILayout.GetControlRect(false, totalHeight);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, rect.height), Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, rect.height), Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, 24f), Styles.AccentColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, 24f), Styles.AccentColor);
            }

            GUI.Label(new Rect(rect.x,                         rect.y, halfWidth, 24f), L10n.Get("controller.wd.on_col"),  Styles.HeaderLabel);
            GUI.Label(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, 24f), L10n.Get("controller.wd.off_col"), Styles.HeaderLabel);

            // Single pill on right edge of right column toggles both columns
            float offX      = rect.x + halfWidth + middleGap;
            var pillRect    = new Rect(rect.xMax - pillW, rect.y + 24f, pillW, rowsDisplayH);
            bool newEnabled = GUI.Toggle(pillRect, _wdScrollEnabled, "", Styles.ScrollToggleBtn);
            if (newEnabled != _wdScrollEnabled) { _wdScrollEnabled = newEnabled; _wdOnScrollPos = Vector2.zero; _wdOffScrollPos = Vector2.zero; }
            EditorGUIUtility.AddCursorRect(pillRect, MouseCursor.Link);

            // On column rows
            var onViewRect = new Rect(rect.x, rect.y + 24f, halfWidth, onDisplayH);
            if (_wdScrollEnabled && onTotalRowH > maxVisibleH)
            {
                var contentRect = new Rect(0, 0, onViewRect.width, onTotalRowH);
                _wdOnScrollPos = GUI.BeginScrollView(onViewRect, _wdOnScrollPos, contentRect, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawWDOnRows(contentRect, onLayers, rowHeight);
                GUI.EndScrollView();
            }
            else
                DrawWDOnRows(onViewRect, onLayers, rowHeight);

            // Off column rows
            var offViewRect = new Rect(offX, rect.y + 24f, halfWidth - pillW, offDisplayH);
            if (_wdScrollEnabled && offTotalRowH > maxVisibleH)
            {
                var contentRect = new Rect(0, 0, offViewRect.width, offTotalRowH);
                _wdOffScrollPos = GUI.BeginScrollView(offViewRect, _wdOffScrollPos, contentRect, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawWDOffRows(contentRect, offLayers, rowHeight);
                GUI.EndScrollView();
            }
            else
                DrawWDOffRows(offViewRect, offLayers, rowHeight);

            if (mixedLayers.Length > 0)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(L10n.Get("controller.wd.mixed"), Styles.HeaderLabel, GUILayout.Height(24));
                    GUILayout.FlexibleSpace();
                }

                float mixedRowHeight  = EditorGUIUtility.singleLineHeight + 2f;
                var   mixedRowsRect   = EditorGUILayout.GetControlRect(false, mixedLayers.Length * mixedRowHeight);

                if (Event.current.type == EventType.Repaint && mixedRowsRect.height > 0)
                    EditorGUI.DrawRect(mixedRowsRect, Styles.SecondaryColor);

                float mixedInnerX     = mixedRowsRect.x;
                float mixedInnerWidth = mixedRowsRect.width;
                float mixedInnerY     = mixedRowsRect.y;

                for (int i = 0; i < mixedLayers.Length; i++)
                {
                    var   layer      = mixedLayers[i];
                    var   rowRect    = new Rect(mixedInnerX, mixedInnerY + i * mixedRowHeight, mixedInnerWidth, mixedRowHeight);

                    if (Event.current.type == EventType.Repaint && i % 2 == 1)
                        EditorGUI.DrawRect(rowRect, Styles.RowAltColor);
                    float btnWidth   = 48f;
                    float gap        = 8f;
                    float nameWidth  = Styles.SmallLabelCenter.CalcSize(new GUIContent(layer.name)).x;
                    float groupWidth = btnWidth + gap + nameWidth + gap + btnWidth;
                    float groupX     = rowRect.x + (rowRect.width - groupWidth) / 2f;

                    if (CursorBtn(new Rect(groupX, rowRect.y, btnWidth, rowRect.height), "← On", Styles.IconBtn))
                        SetLayerWD(layer, true);
                    GUI.Label(new Rect(groupX + btnWidth + gap, rowRect.y, nameWidth, rowRect.height), layer.name, Styles.SmallLabelCenter);
                    if (CursorBtn(new Rect(groupX + btnWidth + gap + nameWidth + gap, rowRect.y, btnWidth, rowRect.height), "→ Off", Styles.IconBtn))
                        SetLayerWD(layer, false);
                }
            }
        }

        void DrawWDOnRows(Rect area, AnimatorControllerLayer[] onLayers, float rowHeight)
        {
            if (onLayers.Length == 0)
            {
                GUI.Label(new Rect(area.x, area.y, area.width, rowHeight), "—", Styles.EmptyLabel);
                return;
            }
            for (int i = 0; i < onLayers.Length; i++)
            {
                float rowY = area.y + i * rowHeight;
                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                    EditorGUI.DrawRect(new Rect(area.x, rowY, area.width, rowHeight), Styles.RowAltColor);
                GUI.Label(new Rect(area.x, rowY, area.width - 24f, rowHeight), onLayers[i].name, Styles.SmallLabelCenter);
                if (CursorBtn(new Rect(area.xMax - 24f, rowY, 24f, rowHeight), "→", Styles.IconBtn))
                    SetLayerWD(onLayers[i], false);
            }
        }

        void DrawWDOffRows(Rect area, AnimatorControllerLayer[] offLayers, float rowHeight)
        {
            if (offLayers.Length == 0)
            {
                GUI.Label(new Rect(area.x, area.y, area.width, rowHeight), "—", Styles.EmptyLabel);
                return;
            }
            for (int i = 0; i < offLayers.Length; i++)
            {
                float rowY = area.y + i * rowHeight;
                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                    EditorGUI.DrawRect(new Rect(area.x, rowY, area.width, rowHeight), Styles.RowAltColor);
                if (CursorBtn(new Rect(area.x, rowY, 24f, rowHeight), "←", Styles.IconBtn))
                    SetLayerWD(offLayers[i], true);
                GUI.Label(new Rect(area.x + 24f, rowY, area.width - 24f, rowHeight), offLayers[i].name, Styles.SmallLabelCenter);
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
        bool   _networkPreserveTransitionProperties;
        int    _networkLayerIndex;
#endif

        void DrawNetworkSyncSection()
        {
#if VRC_SDK_VRCSDK3
            if (_activeStateMachine == null)
            {
                EditorGUILayout.LabelField(L10n.Get("controller.network.no_window"), Styles.EmptyLabel);
                return;
            }

            var smAssetPath = AssetDatabase.GetAssetPath(_activeStateMachine);
            var activeController = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(smAssetPath);

            // Layer dropdown
            var layers = activeController != null ? activeController.layers : System.Array.Empty<UnityEditor.Animations.AnimatorControllerLayer>();
            _networkLayerIndex = Mathf.Clamp(_networkLayerIndex, 0, Mathf.Max(0, layers.Length - 1));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.target_layer"), Styles.SmallLabel, GUILayout.Width(160));
                if (layers.Length > 0)
                {
                    var layerNames = layers.Select(layer => layer.name).ToArray();
                    string currentLayerName = layerNames[_networkLayerIndex];
                    var buttonRect = GUILayoutUtility.GetRect(new GUIContent(currentLayerName), EditorStyles.popup, GUILayout.ExpandWidth(true));
                    if (CursorBtn(buttonRect, currentLayerName, EditorStyles.popup))
                        ShowLayerDropdown(buttonRect, layerNames, _networkLayerIndex, index => _networkLayerIndex = index);
                }
                else
                {
                    EditorGUILayout.LabelField("—", Styles.SmallLabel);
                }
            }

            var targetSM = (activeController == null || layers.Length == 0)
                ? _activeStateMachine
                : activeController.layers[_networkLayerIndex].stateMachine;

            DrawNetworkToggleRow(L10n.Get("controller.network.sync_param_type"), ref _networkUseBool,            "Int",        "Bool");
            DrawNetworkToggleRow(L10n.Get("controller.network.transitions"),     ref _networkAnyStateTransitions, "All-to-All", "Any State");

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.preserve_props"), Styles.SmallLabel, GUILayout.Width(164));
                _networkPreserveTransitionProperties = EditorGUILayout.Toggle(_networkPreserveTransitionProperties, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            string trimmedNetworkParamName = _networkParamName.Trim();
            bool isDuplicateName = activeController != null
                && !string.IsNullOrWhiteSpace(trimmedNetworkParamName)
                && activeController.parameters.Any(parameter =>
                    parameter.name == trimmedNetworkParamName
                    || (parameter.name.StartsWith(trimmedNetworkParamName)
                        && parameter.name.Length > trimmedNetworkParamName.Length
                        && parameter.name[trimmedNetworkParamName.Length..].All(char.IsDigit)));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.sync_param_name"), Styles.SmallLabel, GUILayout.Width(164));
                _networkParamName = EditorGUILayout.TextField(_networkParamName);
                if (isDuplicateName && Event.current.type == EventType.Repaint)
                {
                    var textFieldRect = GUILayoutUtility.GetLastRect();
                    float iconSize = 16f;
                    var warningRect = new Rect(textFieldRect.xMax - iconSize - 2f, textFieldRect.y + (textFieldRect.height - iconSize) * 0.5f, iconSize, iconSize);
                    GUI.Label(warningRect, new GUIContent(EditorGUIUtility.IconContent("warning@2x").image, "Duplicate Name"), GUIStyle.none);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.states_prefix"), Styles.SmallLabel, GUILayout.Width(164));
                _networkStatesPrefix = EditorGUILayout.TextField(_networkStatesPrefix);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.remove_behaviours"), Styles.SmallLabel, GUILayout.Width(164));
                GUILayout.Label(L10n.Get("controller.network.params"), Styles.SmallLabel, GUILayout.Width(50));
                _networkRemoveParamDrivers = EditorGUILayout.Toggle(_networkRemoveParamDrivers, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label(L10n.Get("controller.network.audio"), Styles.SmallLabel, GUILayout.Width(36));
                _networkRemoveAudioPlay = EditorGUILayout.Toggle(_networkRemoveAudioPlay, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label(L10n.Get("controller.network.tracking"), Styles.SmallLabel, GUILayout.Width(52));
                _networkRemoveTracking = EditorGUILayout.Toggle(_networkRemoveTracking, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.network.pack_subsm"), Styles.SmallLabel, GUILayout.Width(164));
                _networkPackIntoSubSM = EditorGUILayout.Toggle(_networkPackIntoSubSM, GUILayout.Width(16));
            }

            EditorGUILayout.Space(6);

            bool canRun = !string.IsNullOrWhiteSpace(_networkParamName) && !string.IsNullOrWhiteSpace(_networkStatesPrefix) && !isDuplicateName;

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (CursorBtn(L10n.Get("controller.network.run"), Styles.IconBtn, GUILayout.Height(28)))
                {
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
                        preserveTransitionProperties = _networkPreserveTransitionProperties
                    });
                }
            }
#else
            EditorGUILayout.LabelField(L10n.Get("controller.network.no_vrcsdk"), Styles.EmptyLabel);
#endif
        }

        /* Draws a two-button exclusive toggle row with a left-aligned label and cursor-rect on both buttons. */
        static void DrawNetworkToggleRow(string label, ref bool value, string falseLabel, string trueLabel)
        {
            var rect            = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float buttonWidth   = (rect.width - 164f) / 2f;
            float firstButtonX  = rect.x + 164f;
            float secondButtonX = firstButtonX + buttonWidth;

            GUI.Label(new Rect(rect.x, rect.y, 164f, rect.height), label, Styles.SmallLabel);

            var falseRect  = new Rect(firstButtonX,  rect.y, buttonWidth, rect.height);
            var trueRect   = new Rect(secondButtonX, rect.y, buttonWidth, rect.height);

            if (GUI.Button(falseRect, falseLabel, !value ? Styles.IconBtnActive : Styles.IconBtn)) value = false;
            EditorGUIUtility.AddCursorRect(falseRect, MouseCursor.Link);

            if (GUI.Button(trueRect, trueLabel, value ? Styles.IconBtnActive : Styles.IconBtn)) value = true;
            EditorGUIUtility.AddCursorRect(trueRect, MouseCursor.Link);
        }

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

        void DrawClipRemapperUI()
        {
            DrawAvatarRootField();
            DrawInvalidSlotWarning();
            DrawAutoRepathButton();
            DrawScanResultLabel();
            EditorGUILayout.Space(4);
            DrawRemapPathFields();
            EditorGUILayout.Space(6);
            DrawRemapConfirmButton();
        }

        void DrawAvatarRootField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.repath.avatar_root"), Styles.SmallLabel, GUILayout.Width(100));
                EditorGUI.BeginChangeCheck();
                var newRoot = (GameObject)EditorGUILayout.ObjectField(_clipRemapAvatarRoot, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
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
                }
                bool slotInvalid = _clipRemapAvatarRoot != null && _slotController == null;
                using (new EditorGUI.DisabledScope(_clipRemapAvatarRoot == null || slotInvalid))
                {
                    if (CursorBtn(L10n.Get("controller.repath.scan"), Styles.IconBtn, GUILayout.Width(48)))
                    {
                        _clipScanResult = AnimatorClipRemapper.ScanBrokenPaths(ClipController, _clipRemapAvatarRoot);
                        _clipScanned = true;
                        if (_clipScanResult.brokenSegments != null && _clipScanResult.brokenSegments.Length > 0)
                            _clipRemapFromPath = _clipScanResult.brokenSegments[0].segment;
                    }
                }
            }
        }

        void DrawInvalidSlotWarning()
        {
            if (_clipRemapAvatarRoot == null || _slotController != null) return;
            string warningMsg = _slotHasNoAnimator
                ? "No Animator component on this GameObject"
                : "Animator has no AnimatorController assigned";
            EditorGUILayout.HelpBox(warningMsg, MessageType.Warning);
        }

        void DrawAutoRepathButton()
        {
            using (new EditorGUI.DisabledScope(_clipRemapAvatarRoot == null || (_clipRemapAvatarRoot != null && _slotController == null)))
            {
                Color prevBg = GUI.backgroundColor;
                // Sky-blue when enabled — deliberately different hue from row selection green to avoid palette conflicts.
                if (_autoRepathEnabled) GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
                if (CursorBtn(_autoRepathEnabled ? L10n.Get("controller.repath.auto_on") : L10n.Get("controller.repath.auto_off"), Styles.IconBtn, GUILayout.Height(24)))
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
                }
                GUI.backgroundColor = prevBg;
            }
        }

        void DrawScanResultLabel()
        {
            if (!_clipScanned) return;
            bool hasNone = _clipScanResult.brokenSegments == null || _clipScanResult.brokenSegments.Length == 0;
            EditorGUILayout.LabelField(hasNone ? L10n.Get("controller.repath.no_broken") : $"{_clipScanResult.totalBrokenCount} {L10n.Get("controller.repath.broken_bindings")}", Styles.EmptyLabel);
            if (hasNone) return;
            int displayCount = Mathf.Min(_clipScanResult.brokenSegments.Length, 5);
            for (int i = 0; i < displayCount; i++)
            {
                var (segment, count) = _clipScanResult.brokenSegments[i];
                if (CursorBtn($"  {segment}  ({count})", Styles.IconBtn, GUILayout.Height(20)))
                    _clipRemapFromPath = segment;
            }
            if (_clipScanResult.brokenSegments.Length > 5)
                EditorGUILayout.LabelField($"and {_clipScanResult.brokenSegments.Length - 5} more…", Styles.EmptyLabel);
        }

        void DrawRemapPathFields()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.repath.from_path"), Styles.SmallLabel, GUILayout.Width(100));
                _clipRemapFromPath = EditorGUILayout.TextField(_clipRemapFromPath);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L10n.Get("controller.repath.to_path"), Styles.SmallLabel, GUILayout.Width(100));
                _clipRemapToPath = EditorGUILayout.TextField(_clipRemapToPath);
            }
        }

        void DrawRemapConfirmButton()
        {
            string trimmedFrom = _clipRemapFromPath.TrimEnd('/');
            string trimmedTo   = _clipRemapToPath.TrimEnd('/');
            bool canRemap      = !string.IsNullOrEmpty(trimmedFrom) && trimmedFrom != trimmedTo;
            bool hasSelection  = _selectedClipIds.Count > 0;

            using (new EditorGUI.DisabledScope(!canRemap))
            {
                string remapLabel = hasSelection ? $"{L10n.Get("controller.repath.remap_selected")} ({_selectedClipIds.Count})" : L10n.Get("controller.repath.remap_clips");
                if (CursorBtn(remapLabel, Styles.IconBtn, GUILayout.Height(28)))
                {
                    if (hasSelection)
                        AnimatorClipRemapper.RemapSelectedClips(ClipController, _selectedClipIds, trimmedFrom, trimmedTo);
                    else
                        AnimatorClipRemapper.RemapAll(ClipController, trimmedFrom, trimmedTo);
                    _clipScanned = false;
                    _subAssetsByType = null;
                }
            }
        }

        // ── Sub-Assets ────────────────────────────────────────────────────────

        static Texture2D[] _subAssetFilterIcons;
        static Texture2D[] SubAssetFilterIcons => _subAssetFilterIcons ??= new[]
        {
            EditorGUIUtility.IconContent("d_AnimatorController Icon").image as Texture2D,
            EditorGUIUtility.IconContent("AnimatorState Icon").image as Texture2D,
            EditorGUIUtility.IconContent("d_BlendTree Icon").image as Texture2D,
            EditorGUIUtility.IconContent("AnimationClip Icon").image as Texture2D,
        };
        static GUIContent[] SubAssetFilterContents => new[]
        {
            new GUIContent(L10n.Get("controller.subassets.state_machines"), SubAssetFilterIcons[0]),
            new GUIContent(L10n.Get("controller.subassets.states"),         SubAssetFilterIcons[1]),
            new GUIContent(L10n.Get("controller.subassets.blend_trees"),    SubAssetFilterIcons[2]),
            new GUIContent(L10n.Get("controller.subassets.clips"),          SubAssetFilterIcons[3]),
        };

        Vector2 _subAssetScrollPos;
        bool    _subAssetScrollEnabled;

        Vector2 _wdOnScrollPos;
        Vector2 _wdOffScrollPos;
        bool    _wdScrollEnabled;


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
                Repaint();
                return;
            }
        }

        void DrawSubAssetsSection()
        {
            if (_controller == null)
            {
                EditorGUILayout.LabelField(L10n.Get("controller.no_controller"), Styles.EmptyLabel);
                return;
            }

            if (_subAssetsByType == null || _subAssetCachedController != _controller)
                RebuildSubAssetCache();

            // Filter bar
            var filterBarRect  = EditorGUILayout.GetControlRect(false, 24f);
            float filterBtnWidth = filterBarRect.width / 4f;

            EditorGUIUtility.SetIconSize(new Vector2(18, 18));
            var filterContents = SubAssetFilterContents;
            for (int i = 0; i < filterContents.Length; i++)
            {
                bool isActive = _subAssetTypeFilter == i;
                var  btnRect  = new Rect(filterBarRect.x + i * filterBtnWidth, filterBarRect.y, filterBtnWidth, 24f);
                if (GUI.Toggle(btnRect, isActive, filterContents[i], isActive ? Styles.IconBtnActive : Styles.IconBtn))
                {
                    if (_subAssetTypeFilter != i) _subAssetScrollPos = Vector2.zero;
                    _subAssetTypeFilter = i;
                }
                EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
            }
            EditorGUIUtility.SetIconSize(Vector2.zero);

            if (_subAssetsByType == null)
                return;

            if (_subAssetTypeFilter == 3)
            {
                if (_brokenIdsDirty && Event.current.type == EventType.Layout)
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
                EditorGUILayout.Space(4);
                DrawClipRemapperUI();
                EditorGUILayout.Space(4);
            }

            // Search bar
            EditorGUILayout.Space(2);
            _subAssetSearch = EditorGUILayout.TextField(_subAssetSearch, EditorStyles.toolbarSearchField);
            if (string.IsNullOrEmpty(_subAssetSearch) && Event.current.type == EventType.Repaint)
            {
                var searchRect = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(searchRect.x + 18, searchRect.y, searchRect.width - 18, searchRect.height), L10n.Get("controller.subassets.search"), Styles.SubAssetSearchHint);
            }
            EditorGUILayout.Space(2);

            if (_subAssetsByType == null) return;
            var assets = _subAssetsByType[_subAssetTypeFilter];
            if (assets == null || assets.Length == 0)
            {
                EditorGUILayout.LabelField(L10n.Get("controller.subassets.none"), Styles.EmptyLabel);
                return;
            }

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
                GUI.Label(EditorGUILayout.GetControlRect(false, 20f), L10n.Get("controller.subassets.no_matches"), Styles.EmptyLabel);
                return;
            }

            float rowHeight      = EditorGUIUtility.singleLineHeight;
            float totalH         = filtered.Count * rowHeight;
            float maxVisibleH    = 10f * rowHeight;
            float displayH       = _subAssetScrollEnabled ? Mathf.Min(totalH, maxVisibleH) : totalH;
            float toggleW = Styles.k_pillW;

            var listArea = EditorGUILayout.GetControlRect(false, displayH);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(listArea, Styles.SecondaryColor);

            var toggleRect = new Rect(listArea.xMax - toggleW, listArea.y, toggleW, listArea.height);
            bool newEnabled = GUI.Toggle(toggleRect, _subAssetScrollEnabled, "", Styles.ScrollToggleBtn);
            if (newEnabled != _subAssetScrollEnabled) { _subAssetScrollEnabled = newEnabled; _subAssetScrollPos = Vector2.zero; }
            EditorGUIUtility.AddCursorRect(toggleRect, MouseCursor.Link);

            var viewRect = new Rect(listArea.x, listArea.y, listArea.width - toggleW, listArea.height);

            if (_subAssetScrollEnabled && totalH > maxVisibleH)
            {
                var contentRect = new Rect(0, 0, viewRect.width - 13f, totalH);
                _subAssetScrollPos = GUI.BeginScrollView(viewRect, _subAssetScrollPos, contentRect, false, true);
                DrawSubAssetRows(filtered, contentRect, rowHeight);
                GUI.EndScrollView();
            }
            else
                DrawSubAssetRows(filtered, viewRect, rowHeight);
        }

        void DrawSubAssetRows(List<UnityEngine.Object> filtered, Rect rect, float rowHeight)
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                var rowRect = new Rect(rect.x, rect.y + i * rowHeight, rect.width, rowHeight);
                DrawSubAssetRow(filtered[i], rowRect, i);
            }
        }

        void DrawSubAssetRow(UnityEngine.Object asset, Rect rowRect, int rowIndex)
        {
            bool isClips = _subAssetTypeFilter == 3;

            if (Event.current.type == EventType.Repaint && rowIndex % 2 == 1)
                EditorGUI.DrawRect(rowRect, Styles.RowAltColor);
            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            if (isClips)
            {
                int assetId = asset.GetInstanceID();
                if (Event.current.type == EventType.Repaint && _selectedClipIds.Contains(assetId))
                    EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.6f, 0.2f, 0.25f));
                if (GUI.Button(rowRect, asset.name, Styles.SubAssetListLabel))
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
                if (_clipsWithBrokenIds != null && _clipsWithBrokenIds.Contains(assetId))
                {
                    var warningIconContent = new GUIContent(EditorGUIUtility.IconContent("d_console.warnicon").image, L10n.Get("controller.subassets.warn_broken_bindings"));
                    GUI.Label(new Rect(rowRect.xMax - 18, rowRect.y + 1, 16, rowRect.height - 2), warningIconContent);
                }
                return;
            }

            string label                = asset.name;
            bool showEmptyWarning       = false;
            bool showInvalidWarning     = false;
            bool showEmptyMotionWarning = false;

            if (_subAssetTypeFilter == 0)
            {
                if (_rootSMIds != null && !_rootSMIds.Contains(asset.GetInstanceID()))
                    label += "  <color=#888888>(Sub State Machine)</color>";
                if (_emptySMIds != null && _emptySMIds.Contains(asset.GetInstanceID()))
                    showEmptyWarning = true;
            }
            else if (_subAssetTypeFilter == 1 &&
                _statesWithInvalidTransitions != null &&
                _statesWithInvalidTransitions.Contains(asset.GetInstanceID()))
                showInvalidWarning = true;
            else if (_subAssetTypeFilter == 1 &&
                _statesWithNoMotion != null &&
                _statesWithNoMotion.Contains(asset.GetInstanceID()))
                showEmptyMotionWarning = true;
            else if (_subAssetTypeFilter == 2 &&
                _blendTreesWithEmptyMotion != null &&
                _blendTreesWithEmptyMotion.Contains(asset.GetInstanceID()))
                showEmptyMotionWarning = true;

            if (GUI.Button(rowRect, label, Styles.SubAssetListLabel))
                NavigateToAsset(asset);

            if (showEmptyWarning || showInvalidWarning || showEmptyMotionWarning)
            {
                string warningTooltip      = showEmptyWarning ? L10n.Get("controller.subassets.warn_empty_layer")
                    : showEmptyMotionWarning ? L10n.Get("controller.subassets.warn_empty_motion")
                    : L10n.Get("controller.subassets.warn_invalid_transition");
                var warningIconContent = new GUIContent(EditorGUIUtility.IconContent("d_console.warnicon").image, warningTooltip);
                var warningIconRect    = new Rect(rowRect.xMax - 18, rowRect.y + 1, 16, rowRect.height - 2);
                GUI.Label(warningIconRect, warningIconContent);
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
                    if (asset is FrameLayoutData) continue;
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
                    if (state.motion == null)
                        _statesWithNoMotion.Add(asset.GetInstanceID());
                }
            }

            _emptySMIds = new HashSet<int>(
                stateMachines
                    .OfType<AnimatorStateMachine>()
                    .Where(sm => sm.states.Length == 0 && sm.stateMachines.Length == 0)
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
                CollectSMReferences(childStateMachine.stateMachine, ids);
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

        /* Returns the index of the first layer whose state machine hierarchy contains asset, or -1 if not found. */
        int FindLayerIndex(UnityEngine.Object asset)
        {
            var layers = _controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var sm = layers[i].stateMachine;
                if (asset is AnimatorState state        && SMContainsState(sm, state))         return i;
                if (asset is AnimatorStateMachine subSM && SMContainsOrIs(sm, subSM))          return i;
                if (asset is BlendTree blendTree        && SMContainsBlendTree(sm, blendTree)) return i;
            }
            return -1;
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
