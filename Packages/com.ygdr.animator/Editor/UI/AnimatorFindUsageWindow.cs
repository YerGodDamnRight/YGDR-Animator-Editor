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
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
#endif

namespace YGDR.Editor.Animation
{
    internal class AnimatorFindUsageWindow : EditorWindow
    {
        struct UsageRow
        {
            internal string transitionLabel;
            internal string conditionLabel;
            internal AnimatorStateTransition transition;
            internal AnimatorState state;
        }

        const int TabTransitions = 0;
        const int TabBehaviors   = 1;
        const int TabAapClips    = 2;
        const int TabObjects     = 3;

        AnimatorController _controller;
        string _parameterName;
        AnimatorControllerParameterType _parameterType;
        string _relativePath;
        string _gameObjectName;
        string _controllerPath;
        List<UsageRow> _transitionRows = new();
        List<UsageRow> _behaviorRows   = new();
        HashSet<int> _knownTransitionIds = new();
        List<AnimatorState> _clipStates = new();
        List<AnimationClip> _clipAssets = new();
        List<GameObject> _effectingObjects = new();
        string _effectingComponentTypeName = "";
        string _headerText = "";
        Vector2 _scrollPosition;
        bool    _rowsScrollEnabled;
        int _activeTab;



        static GUIStyle s_tabLabelStyle;
        static GUIStyle TabLabelStyle => s_tabLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 11
        };

        static GUIStyle s_rowLabelStyle;
        static GUIStyle RowLabelStyle => s_rowLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize  = 11,
            padding   = new RectOffset(4, 4, 0, 0)
        };

        static GUIStyle s_clickableRowStyle;
        static GUIStyle ClickableRowStyle
        {
            get
            {
                if (s_clickableRowStyle != null) return s_clickableRowStyle;
                var hoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                hoverTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.07f));
                hoverTex.Apply();
                s_clickableRowStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize  = 11,
                    padding   = new RectOffset(4, 4, 0, 0),
                    hover     = { background = hoverTex, textColor = Color.white }
                };
                return s_clickableRowStyle;
            }
        }

        internal static void Open(AnimatorControllerParameter parameter, AnimatorController controller)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._parameterName = parameter.name;
            window._parameterType = parameter.type;
            window._relativePath = null;
            window._gameObjectName = null;
            window._controllerPath = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            window.RebuildCache();
            window.SelectDefaultTab();
            window.Show();
        }

        internal static void Open(string relativePath, AnimatorController controller, string gameObjectName)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._relativePath = relativePath;
            window._gameObjectName = gameObjectName;
            window._parameterName = null;
            window._controllerPath = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            window.RebuildCache();
            window.Show();
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            ObjectChangeEvents.changesPublished += OnAssetChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnAssetChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            RebuildCache();
            Repaint();
        }

        void OnAssetChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_controller == null || _controllerPath == null) return;

            for (int i = 0; i < stream.length; i++)
            {
                bool relevant = false;
                var kind = stream.GetEventType(i);

                if (kind == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                    var changedObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = changedObj != null && AssetDatabase.GetAssetPath(changedObj) == _controllerPath;
                }
                else if (kind == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var args);
                    var createdObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = createdObj != null && AssetDatabase.GetAssetPath(createdObj) == _controllerPath;
                }
                else if (kind == ObjectChangeKind.DestroyAssetObject)
                {
                    stream.GetDestroyAssetObjectEvent(i, out var args);
                    relevant = _knownTransitionIds.Contains(args.instanceId);
                }

                if (!relevant) continue;
                RebuildCache();
                Repaint();
                return;
            }
        }

        void RebuildCache()
        {
            _transitionRows.Clear();
            _behaviorRows.Clear();
            _knownTransitionIds.Clear();
            _clipStates.Clear();
            _clipAssets.Clear();
            _effectingObjects.Clear();
            _effectingComponentTypeName = "";

            if (_controller == null) return;

            if (_parameterName != null)
            {
                foreach (var layer in _controller.layers)
                    SearchSMForParameter(layer.stateMachine);

                var seenStateIds = new HashSet<int>();
                var seenClipIds  = new HashSet<int>();
                foreach (var layer in _controller.layers)
                    SearchSMForAapClips(layer.stateMachine, seenStateIds, seenClipIds);

#if VRC_SDK_VRCSDK3
                SearchSceneForEffectingObjects();
#endif
                var settings = AnimatorDefaultSettings.Load();
                string typeHex = ColorUtility.ToHtmlStringRGB(_parameterType switch
                {
                    AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                    AnimatorControllerParameterType.Int     => settings.paramColorInt,
                    AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                    AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                    _                                       => new Color(0.65f, 0.65f, 0.65f)
                });
                _headerText = $"{_parameterName}  <color=#{typeHex}>{_parameterType}</color>";
            }
            else
            {
                if (_relativePath == null) return;
                var seenStateIds = new HashSet<int>();
                var seenClipIds  = new HashSet<int>();
                foreach (var layer in _controller.layers)
                    SearchSMForClips(layer.stateMachine, seenStateIds, seenClipIds);

                string displayName = _gameObjectName ?? "";
                string counts = L10n.Get("find_usage.count.nodes_clips")
                    .Replace("{n}", _clipStates.Count.ToString())
                    .Replace("{m}", _clipAssets.Count.ToString());
                _headerText = $"{displayName}  —  {counts}";
            }
        }

        void SelectDefaultTab()
        {
            if (_transitionRows.Count > 0) { _activeTab = TabTransitions; return; }
            if (_behaviorRows.Count > 0)   { _activeTab = TabBehaviors;   return; }
            if (_clipStates.Count > 0)     { _activeTab = TabAapClips;    return; }
#if VRC_SDK_VRCSDK3
            if (_effectingObjects.Count > 0) { _activeTab = TabObjects; return; }
#endif
            _activeTab = TabTransitions;
        }

        // ── Parameter search ──────────────────────────────────────────────────

        void SearchSMForParameter(AnimatorStateMachine sm)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
            {
                _knownTransitionIds.Add(anyStateTransition.GetInstanceID());
                CheckTransition(anyStateTransition, "Any State", ResolveDestinationName(anyStateTransition));
            }

            foreach (var childState in sm.states)
            {
                foreach (var stateTransition in childState.state.transitions)
                {
                    _knownTransitionIds.Add(stateTransition.GetInstanceID());
                    CheckTransition(stateTransition, childState.state.name, ResolveDestinationName(stateTransition));
                }
#if VRC_SDK_VRCSDK3
                foreach (var behaviour in childState.state.behaviours)
                {
                    if (behaviour is VRCAvatarParameterDriver driver)
                    {
                        foreach (var driverParameter in driver.parameters)
                        {
                            bool matchesDestination = driverParameter.name == _parameterName;
                            bool matchesSource = driverParameter.type == VRC_AvatarParameterDriver.ChangeType.Copy
                                                 && driverParameter.source == _parameterName;
                            if (!matchesDestination && !matchesSource) continue;
                            _behaviorRows.Add(new UsageRow
                            {
                                transitionLabel = $"{childState.state.name}  →  Parameter Driver",
                                conditionLabel  = matchesSource
                                                 ? $"Copy (source) → {driverParameter.name}"
                                                 : driverParameter.type switch
                                                 {
                                                     VRC_AvatarParameterDriver.ChangeType.Set    => $"Set {driverParameter.value:0.###}",
                                                     VRC_AvatarParameterDriver.ChangeType.Add    => $"Add {driverParameter.value:0.###}",
                                                     VRC_AvatarParameterDriver.ChangeType.Random => $"Random {driverParameter.valueMin:0.###}–{driverParameter.valueMax:0.###}",
                                                     VRC_AvatarParameterDriver.ChangeType.Copy   => $"Copy {driverParameter.source}",
                                                     _                                           => driverParameter.type.ToString()
                                                 },
                                state           = childState.state
                            });
                            break;
                        }
                    }
                    if (behaviour is VRCAnimatorPlayAudio playAudio && playAudio.ParameterName == _parameterName)
                    {
                        _behaviorRows.Add(new UsageRow
                        {
                            transitionLabel = $"{childState.state.name}  →  Play Audio",
                            conditionLabel  = "clip select",
                            state           = childState.state
                        });
                    }
                }
#endif
            }

            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForParameter(childStateMachine.stateMachine);
        }

        void CheckTransition(AnimatorStateTransition transition, string sourceName, string destinationName)
        {
            foreach (var condition in transition.conditions)
            {
                if (condition.parameter != _parameterName) continue;
                _transitionRows.Add(new UsageRow
                {
                    transitionLabel = $"{sourceName}  →  {destinationName}",
                    conditionLabel  = FormatCondition(condition),
                    transition      = transition
                });
            }
        }

        string ResolveDestinationName(AnimatorStateTransition transition)
        {
            if (transition.isExit) return "Exit";
            if (transition.destinationState != null) return transition.destinationState.name;
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine.name;
            return "?";
        }

        string FormatCondition(AnimatorCondition condition)
        {
            return _parameterType switch
            {
                AnimatorControllerParameterType.Bool    => condition.mode == AnimatorConditionMode.If ? "True" : "False",
                AnimatorControllerParameterType.Trigger => "",
                AnimatorControllerParameterType.Float   => $"{condition.mode} {condition.threshold:0.###}",
                AnimatorControllerParameterType.Int     => $"{condition.mode} {(int)condition.threshold}",
                _                                       => condition.mode.ToString()
            };
        }

        // ── Clip search ───────────────────────────────────────────────────────

        void SearchSMForClips(AnimatorStateMachine sm, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var childState in sm.states)
                CheckStateForClips(childState.state, seenStateIds, seenClipIds);
            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForClips(childStateMachine.stateMachine, seenStateIds, seenClipIds);
        }

        void CheckStateForClips(AnimatorState state, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var clip in CollectClips(state.motion))
            {
                if (!ClipContainsPath(clip)) continue;
                if (seenStateIds.Add(state.GetInstanceID()))
                    _clipStates.Add(state);
                if (seenClipIds.Add(clip.GetInstanceID()))
                    _clipAssets.Add(clip);
            }
        }

        static IEnumerable<AnimationClip> CollectClips(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                    foreach (var childClip in CollectClips(child.motion))
                        yield return childClip;
            }
        }

        // ── AAP search ────────────────────────────────────────────────────────

        void SearchSMForAapClips(AnimatorStateMachine sm, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var childState in sm.states)
                CheckStateForAapClips(childState.state, seenStateIds, seenClipIds);
            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForAapClips(childStateMachine.stateMachine, seenStateIds, seenClipIds);
        }

        void CheckStateForAapClips(AnimatorState state, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var clip in CollectClips(state.motion))
            {
                if (!ClipDrivesAapParam(clip)) continue;
                if (seenStateIds.Add(state.GetInstanceID()))
                    _clipStates.Add(state);
                if (seenClipIds.Add(clip.GetInstanceID()))
                    _clipAssets.Add(clip);
            }
        }

        bool ClipDrivesAapParam(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type == typeof(UnityEngine.Animator) && binding.propertyName == _parameterName)
                    return true;
            return false;
        }

        bool ClipContainsPath(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path == _relativePath) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (binding.path == _relativePath) return true;
            return false;
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawHeader();
            DrawColumns();
        }

        void DrawHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var fullWidthRect = headerRect;
                fullWidthRect.x = 0;
                fullWidthRect.width = EditorGUIUtility.currentViewWidth;
                EditorGUI.DrawRect(fullWidthRect, AnimationEditorWindow.Styles.SectionHeaderBg);
            }

            GUI.Label(headerRect, _headerText, AnimationEditorWindow.Styles.FindUsesHeader);
        }

        void DrawTabStrip()
        {
            const float stripHeight = 28f;
            const float sectionPad  = 12f; // matches SectionPadded.padding
            var layoutRect = EditorGUILayout.GetControlRect(false, stripHeight);
            // Expand to full section width and shift up to cancel top padding — flush with section top edge
            var stripRect = new Rect(layoutRect.x - sectionPad, layoutRect.y - sectionPad, layoutRect.width + sectionPad * 2f, stripHeight);
            GUILayout.Space(8f); // gap between strip and scroll content

#if VRC_SDK_VRCSDK3
            const int tabCount = 4;
#else
            const int tabCount = 3;
#endif
            float tabWidth = stripRect.width / tabCount;

            DrawTabRect(new Rect(stripRect.x,                 stripRect.y, tabWidth, stripRect.height), TabTransitions, L10n.Get("find_usage.tab.transitions").Replace("{n}", _transitionRows.Count.ToString()),    _transitionRows.Count > 0);
            DrawTabRect(new Rect(stripRect.x + tabWidth,      stripRect.y, tabWidth, stripRect.height), TabBehaviors,   L10n.Get("find_usage.tab.behaviors").Replace("{n}",   _behaviorRows.Count.ToString()),      _behaviorRows.Count > 0);
            DrawTabRect(new Rect(stripRect.x + tabWidth * 2f, stripRect.y, tabWidth, stripRect.height), TabAapClips,    L10n.Get("find_usage.tab.aap_clips").Replace("{n}",   _clipStates.Count.ToString()),         _clipStates.Count > 0);
#if VRC_SDK_VRCSDK3
            DrawTabRect(new Rect(stripRect.x + tabWidth * 3f, stripRect.y, tabWidth, stripRect.height), TabObjects,     L10n.Get("find_usage.tab.objects").Replace("{n}",     _effectingObjects.Count.ToString()),   _effectingObjects.Count > 0);
#endif
        }

        void DrawTabRect(Rect rect, int tabIndex, string label, bool enabled)
        {
            bool isActive = _activeTab == tabIndex;

            if (Event.current.type == EventType.Repaint)
            {
                var accent = AnimationEditorWindow.Styles.AccentColor;
                var bgColor = isActive
                    ? new Color(accent.r + 0.16f, accent.g + 0.16f, accent.b + 0.16f, 1f)
                    : accent;
                EditorGUI.DrawRect(rect, bgColor);

                var previousColor = GUI.color;
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                GUI.Label(rect, label, TabLabelStyle);
                GUI.color = previousColor;
            }

            if (enabled)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
                {
                    _activeTab = tabIndex;
                    Event.current.Use();
                    Repaint();
                }
                if (!isActive)
                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
        }

        void DrawColumns()
        {
            const float middleGap          = 8f;
            const float columnHeaderHeight = 24f;
            const float rowPad             = 2f;
            float rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            bool isPathMode = _parameterName == null;

            int displayRows;
            string leftHeader;
            string rightHeader;
            bool isEmpty;
            string emptyMessage;

            if (isPathMode)
            {
                displayRows  = Mathf.Max(_clipStates.Count, _clipAssets.Count, 1);
                leftHeader   = L10n.Get("find_usage.col.state_node");
                rightHeader  = L10n.Get("find_usage.col.animation_clip");
                isEmpty      = _clipStates.Count == 0 && _clipAssets.Count == 0;
                emptyMessage = L10n.Get("find_usage.empty.no_references");
            }
            else
            {
                switch (_activeTab)
                {
                    case TabTransitions:
                        displayRows  = Mathf.Max(_transitionRows.Count, 1);
                        leftHeader   = L10n.Get("find_usage.col.transition");
                        rightHeader  = L10n.Get("find_usage.col.condition");
                        isEmpty      = _transitionRows.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_transitions");
                        break;
                    case TabBehaviors:
                        displayRows  = Mathf.Max(_behaviorRows.Count, 1);
                        leftHeader   = L10n.Get("find_usage.col.state_node");
                        rightHeader  = L10n.Get("find_usage.col.behavior");
                        isEmpty      = _behaviorRows.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_behaviors");
                        break;
                    case TabAapClips:
                        displayRows  = Mathf.Max(_clipStates.Count, _clipAssets.Count, 1);
                        leftHeader   = L10n.Get("find_usage.col.state_node");
                        rightHeader  = L10n.Get("find_usage.col.animation_clip");
                        isEmpty      = _clipStates.Count == 0 && _clipAssets.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_clips");
                        break;
                    default: // TabObjects
                        displayRows  = Mathf.Max(_effectingObjects.Count, 1);
                        leftHeader   = _effectingComponentTypeName;
                        rightHeader  = L10n.Get("find_usage.col.effecting_object");
                        isEmpty      = _effectingObjects.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_objects");
                        break;
                }
            }

            float rowsHeight = rowPad + displayRows * rowHeight;

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            var outerRect = EditorGUILayout.BeginVertical(AnimationEditorWindow.Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, AnimationEditorWindow.Styles.PrimaryColor);

            if (_parameterName != null)
                DrawTabStrip();

            // Column headers — fixed, outside scroll view
            float pillW     = AnimationEditorWindow.Styles.k_pillW;
            var headerRect  = EditorGUILayout.GetControlRect(false, columnHeaderHeight);
            float halfWidth = (headerRect.width - middleGap) / 2f;

            var leftHeaderRect  = new Rect(headerRect.x,                         headerRect.y, halfWidth, columnHeaderHeight);
            var rightHeaderRect = new Rect(headerRect.x + halfWidth + middleGap, headerRect.y, halfWidth, columnHeaderHeight);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(leftHeaderRect,  AnimationEditorWindow.Styles.AccentColor);
                EditorGUI.DrawRect(rightHeaderRect, AnimationEditorWindow.Styles.AccentColor);
            }
            GUI.Label(leftHeaderRect,  leftHeader,  AnimationEditorWindow.Styles.FindUsesHeader);
            GUI.Label(rightHeaderRect, rightHeader, AnimationEditorWindow.Styles.FindUsesHeader);

            // Rows — pill toggles height clamp, shared scroll position
            float maxVisibleRowsH = 8f * rowHeight + rowPad * 2;
            float clampedH        = _rowsScrollEnabled ? Mathf.Min(rowsHeight, maxVisibleRowsH) : rowsHeight;

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            var rowsAreaRect = EditorGUILayout.GetControlRect(false, clampedH);
            var viewRect     = new Rect(rowsAreaRect.x, rowsAreaRect.y, rowsAreaRect.width - pillW, rowsAreaRect.height);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(viewRect.x,                         viewRect.y, halfWidth, viewRect.height), AnimationEditorWindow.Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(viewRect.x + halfWidth + middleGap, viewRect.y, halfWidth, viewRect.height), AnimationEditorWindow.Styles.SecondaryColor);
            }

            if (_rowsScrollEnabled && rowsHeight > maxVisibleRowsH)
            {
                var contentRect = new Rect(0, 0, viewRect.width, rowsHeight);
                _scrollPosition = GUI.BeginScrollView(viewRect, _scrollPosition, contentRect, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawRowsContent(0, halfWidth, middleGap, rowPad, rowHeight, isEmpty, isPathMode, emptyMessage);
                GUI.EndScrollView();
            }
            else
            {
                DrawRowsContent(viewRect.x, halfWidth, middleGap, viewRect.y + rowPad, rowHeight, isEmpty, isPathMode, emptyMessage);
            }

            // Draw pill last so it renders on top of row content
            var rowsPillRect    = new Rect(rowsAreaRect.xMax - pillW, rowsAreaRect.y, pillW, rowsAreaRect.height);
            bool newRowsEnabled = GUI.Toggle(rowsPillRect, _rowsScrollEnabled, "", AnimationEditorWindow.Styles.ScrollToggleBtn);
            if (newRowsEnabled != _rowsScrollEnabled) { _rowsScrollEnabled = newRowsEnabled; _scrollPosition = Vector2.zero; }
            EditorGUIUtility.AddCursorRect(rowsPillRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseMove) Repaint();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
        }

        void DrawRowsContent(float x, float halfWidth, float middleGap, float rowY, float rowHeight, bool isEmpty, bool isPathMode, string emptyMessage)
        {
            if (isEmpty)
            {
                GUI.Label(new Rect(x, rowY, halfWidth, rowHeight), emptyMessage, AnimationEditorWindow.Styles.EmptyLabel);
            }
            else if (isPathMode)
            {
                DrawClipRows(x, halfWidth, middleGap, rowY, rowHeight);
            }
            else
            {
                switch (_activeTab)
                {
                    case TabTransitions: DrawParameterRows(_transitionRows, x, halfWidth, middleGap, rowY, rowHeight); break;
                    case TabBehaviors:   DrawParameterRows(_behaviorRows,   x, halfWidth, middleGap, rowY, rowHeight); break;
                    case TabAapClips:    DrawClipRows(x, halfWidth, middleGap, rowY, rowHeight);                       break;
                    default:             DrawEffectingObjectRows(x, halfWidth, middleGap, rowY, rowHeight);            break;
                }
            }
        }

        void DrawParameterRows(List<UsageRow> rows, float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            float rowY = startY;
            for (int i = 0; i < rows.Count; i++, rowY += rowHeight)
            {
                var row       = rows[i];
                var leftRect  = new Rect(x,                         rowY, halfWidth, rowHeight);
                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);

                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                {
                    EditorGUI.DrawRect(leftRect,  AnimationEditorWindow.Styles.RowAltColor);
                    EditorGUI.DrawRect(rightRect, AnimationEditorWindow.Styles.RowAltColor);
                }

                if (GUI.Button(leftRect, row.transitionLabel, ClickableRowStyle))
                {
                    if (row.transition != null)
                        AnimationEditorWindow.FocusTransition(row.transition, _controller);
                    else if (row.state != null)
                        AnimationEditorWindow.FocusState(row.state, _controller);
                }
                EditorGUIUtility.AddCursorRect(leftRect, MouseCursor.Link);

                GUI.Label(rightRect, row.conditionLabel, RowLabelStyle);
            }
        }

        // ── Effecting objects search ──────────────────────────────────────────

#if VRC_SDK_VRCSDK3
        internal static readonly string[] PhysBoneSuffixes =
        {
            "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish", "_Velocity", "_IsAnimated"
        };

        internal static readonly string[] RaycastSuffixes = { "_Hit", "_Ratio", "_Distance" };

        internal static bool MatchesSuffixList(string componentBase, string animatorParam, string[] suffixes)
        {
            foreach (var suffix in suffixes)
                if (animatorParam.Length == componentBase.Length + suffix.Length
                    && animatorParam.StartsWith(componentBase, System.StringComparison.Ordinal)
                    && animatorParam.EndsWith(suffix, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        void SearchSceneForEffectingObjects()
        {
            var seenIds = new HashSet<int>();
            var avatarRoot = VRCSyncCache.GetSearchRoot();
            if (avatarRoot == null) return;

#pragma warning disable CS0618
            foreach (var receiver in avatarRoot.GetComponentsInChildren<ContactReceiver>(true))
            {
                if (receiver.parameter != _parameterName) continue;
                if (seenIds.Add(receiver.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(receiver.gameObject);
                    _effectingComponentTypeName = "Contact";
                }
            }

            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                if (!MatchesSuffixList(physBone.parameter, _parameterName, PhysBoneSuffixes)) continue;
                if (seenIds.Add(physBone.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(physBone.gameObject);
                    _effectingComponentTypeName = "PhysBone";
                }
            }

            foreach (var raycast in avatarRoot.GetComponentsInChildren<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                if (!MatchesSuffixList(parameterProperty.stringValue, _parameterName, RaycastSuffixes)) continue;
                if (seenIds.Add(raycast.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(raycast.gameObject);
                    _effectingComponentTypeName = "Raycast";
                }
            }
#pragma warning restore CS0618
        }

        internal static void RemapVrcComponentParameters(string oldName, string newName)
        {
            var avatarRoot = VRCSyncCache.GetSearchRoot();
            if (avatarRoot == null) return;

#pragma warning disable CS0618
            foreach (var receiver in avatarRoot.GetComponentsInChildren<ContactReceiver>(true))
            {
                if (receiver.parameter != oldName) continue;
                Undo.RegisterCompleteObjectUndo(receiver, "Rename Parameter");
                receiver.parameter = newName;
                EditorUtility.SetDirty(receiver);
            }

            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                foreach (var suffix in PhysBoneSuffixes)
                {
                    if (oldName != physBone.parameter + suffix) continue;
                    if (!newName.EndsWith(suffix, System.StringComparison.Ordinal)) break;
                    string newBase = newName.Substring(0, newName.Length - suffix.Length);
                    Undo.RegisterCompleteObjectUndo(physBone, "Rename Parameter");
                    physBone.parameter = newBase;
                    EditorUtility.SetDirty(physBone);
                    break;
                }
            }

            foreach (var raycast in avatarRoot.GetComponentsInChildren<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                foreach (var suffix in RaycastSuffixes)
                {
                    if (oldName != parameterProperty.stringValue + suffix) continue;
                    if (!newName.EndsWith(suffix, System.StringComparison.Ordinal)) break;
                    string newBase = newName.Substring(0, newName.Length - suffix.Length);
                    parameterProperty.stringValue = newBase;
                    serializedRaycast.ApplyModifiedProperties();
                    break;
                }
            }
#pragma warning restore CS0618
        }

        internal static HashSet<string> BuildAllEffectingParamNames()
        {
            var result = new HashSet<string>();
            var avatarRoot = VRCSyncCache.GetSearchRoot();
            if (avatarRoot == null) return result;

#pragma warning disable CS0618
            foreach (var receiver in avatarRoot.GetComponentsInChildren<ContactReceiver>(true))
                if (!string.IsNullOrEmpty(receiver.parameter))
                    result.Add(receiver.parameter);

            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                foreach (var suffix in PhysBoneSuffixes)
                    result.Add(physBone.parameter + suffix);
            }

            foreach (var raycast in avatarRoot.GetComponentsInChildren<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                foreach (var suffix in RaycastSuffixes)
                    result.Add(parameterProperty.stringValue + suffix);
            }
#pragma warning restore CS0618
            return result;
        }
#endif

        void DrawEffectingObjectRows(float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            float rowY = startY;
            for (int i = 0; i < _effectingObjects.Count; i++, rowY += rowHeight)
            {
                var go = _effectingObjects[i];
                if (go == null) continue;

                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);
                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                    EditorGUI.DrawRect(rightRect, AnimationEditorWindow.Styles.RowAltColor);
                if (GUI.Button(rightRect, go.name, ClickableRowStyle))
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                }
                EditorGUIUtility.AddCursorRect(rightRect, MouseCursor.Link);
            }
        }

        void DrawClipRows(float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            int maxRows = Mathf.Max(_clipStates.Count, _clipAssets.Count);
            float rowY = startY;
            for (int i = 0; i < maxRows; i++, rowY += rowHeight)
            {
                bool hasState = i < _clipStates.Count;
                bool hasClip  = i < _clipAssets.Count;

                var leftRect  = new Rect(x,                         rowY, halfWidth, rowHeight);
                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);

                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                {
                    if (hasState) EditorGUI.DrawRect(leftRect,  AnimationEditorWindow.Styles.RowAltColor);
                    if (hasClip)  EditorGUI.DrawRect(rightRect, AnimationEditorWindow.Styles.RowAltColor);
                }

                if (hasState)
                {
                    if (GUI.Button(leftRect, _clipStates[i].name, ClickableRowStyle))
                        AnimationEditorWindow.FocusAsset(_clipStates[i], _controller);
                    EditorGUIUtility.AddCursorRect(leftRect, MouseCursor.Link);
                }

                if (hasClip)
                {
                    if (GUI.Button(rightRect, _clipAssets[i].name, ClickableRowStyle))
                    {
                        Selection.activeObject = _clipAssets[i];
                        EditorGUIUtility.PingObject(_clipAssets[i]);
                    }
                    EditorGUIUtility.AddCursorRect(rightRect, MouseCursor.Link);
                }
            }
        }
    }
}
#endif
