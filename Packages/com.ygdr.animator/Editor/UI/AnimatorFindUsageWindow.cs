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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
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
            internal BlendTree blendTree;
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
        int _activeTab;

        Label _headerLabel;
        VisualElement _panel;
        VisualElement _tabStripContainer;
        Button[] _tabButtons;
        Label _leftHeaderLabel;
        Label _rightHeaderLabel;
        ScrollView _rowsScroll;

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
            window.RefreshAll();
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
            window.RefreshAll();
            window.Show();
        }

        void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnAssetChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
            L10n.OnLanguageChanged += OnLanguageChanged;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnAssetChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedo;
            L10n.OnLanguageChanged -= OnLanguageChanged;
            SharedWindowStyles.UnregisterPaletteRefresh(RefreshPaletteColors);
        }

        void OnLanguageChanged()
        {
            RebuildCache();
            RefreshAll();
        }

        void OnUndoRedo()
        {
            RebuildCache();
            RefreshAll();
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
                RefreshAll();
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
                                transitionLabel = $"{childState.state.name}  →  {L10n.Get("find_usage.behavior.parameter_driver")}",
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
                            transitionLabel = $"{childState.state.name}  →  {L10n.Get("find_usage.behavior.play_audio")}",
                            conditionLabel  = L10n.Get("find_usage.behavior.clip_select"),
                            state           = childState.state
                        });
                    }
                }
#endif
                var state = childState.state;
                if (state.speedParameterActive && state.speedParameter == _parameterName)
                    _behaviorRows.Add(new UsageRow
                    {
                        transitionLabel = $"{state.name}  →  {L10n.Get("states.multiplier")}",
                        conditionLabel  = L10n.Get("states.multiplier"),
                        state           = state
                    });
                if (state.timeParameterActive && state.timeParameter == _parameterName)
                    _behaviorRows.Add(new UsageRow
                    {
                        transitionLabel = $"{state.name}  →  {L10n.Get("states.motion_time")}",
                        conditionLabel  = L10n.Get("states.motion_time"),
                        state           = state
                    });
                if (state.mirrorParameterActive && state.mirrorParameter == _parameterName)
                    _behaviorRows.Add(new UsageRow
                    {
                        transitionLabel = $"{state.name}  →  {L10n.Get("states.mirror")}",
                        conditionLabel  = L10n.Get("states.mirror"),
                        state           = state
                    });
                if (state.cycleOffsetParameterActive && state.cycleOffsetParameter == _parameterName)
                    _behaviorRows.Add(new UsageRow
                    {
                        transitionLabel = $"{state.name}  →  {L10n.Get("states.cycle_offset")}",
                        conditionLabel  = L10n.Get("states.cycle_offset"),
                        state           = state
                    });
                if (state.motion is BlendTree blendTree)
                    SearchBlendTreeForParameter(blendTree, state);
            }

            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForParameter(childStateMachine.stateMachine);
        }

        void SearchBlendTreeForParameter(BlendTree blendTree, AnimatorState state)
        {
            string typeLabel = PatchBlendTreeNodeGUI.BlendTypeLabel(blendTree.blendType);
            bool isDirect = blendTree.blendType == BlendTreeType.Direct;

            void AddRow(string targetLabel) => _behaviorRows.Add(new UsageRow
            {
                transitionLabel = $"{state.name}  →  {blendTree.name}",
                conditionLabel  = $"{typeLabel}  →  {targetLabel}",
                state           = state,
                blendTree       = blendTree
            });

            if (!isDirect && blendTree.blendParameter == _parameterName)
                AddRow(L10n.Get("find_usage.behavior.blend_x"));
            if (!isDirect && blendTree.blendType != BlendTreeType.Simple1D && blendTree.blendParameterY == _parameterName)
                AddRow(L10n.Get("find_usage.behavior.blend_y"));

            foreach (var child in blendTree.children)
            {
                if (isDirect && child.directBlendParameter == _parameterName)
                    AddRow(child.motion != null ? child.motion.name : "?");
                if (child.motion is BlendTree childTree)
                    SearchBlendTreeForParameter(childTree, state);
            }
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

        // ── GUI (native UI Toolkit) ──────────────────────────────────────────

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.EnableInClassList("ygdr-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("ygdr-light", !EditorGUIUtility.isProSkin);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ygdr.animator/Editor/UI/SharedWindowStyles.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            BuildLayout(root);

            SharedWindowStyles.RegisterPaletteRefresh(RefreshPaletteColors);
            RefreshAll();
        }

        void BuildLayout(VisualElement root)
        {
            _headerLabel = new Label { enableRichText = true };
            _headerLabel.AddToClassList("ygdr-fu-header");
            root.Add(_headerLabel);

            _panel = new VisualElement();
            _panel.AddToClassList("ygdr-fu-body");
            root.Add(_panel);

            _tabStripContainer = new VisualElement();
            _tabStripContainer.AddToClassList("ygdr-tab-strip");
            _panel.Add(_tabStripContainer);

#if VRC_SDK_VRCSDK3
            const int tabCount = 4;
#else
            const int tabCount = 3;
#endif
            _tabButtons = new Button[tabCount];
            for (int i = 0; i < tabCount; i++)
            {
                int tabIndex = i;
                var button = new Button(() => SetActiveTab(tabIndex));
                button.AddToClassList("ygdr-tab-strip-button");
                AnimationEditorWindow.StyleHoverTint(button, () => _activeTab == tabIndex,
                    () => AnimationEditorWindow.AccentHoverColor, () => SharedWindowStyles.AccentColor);
                _tabStripContainer.Add(button);
                _tabButtons[i] = button;
            }

            _rowsScroll = SharedWindowStyles.BuildColumnHeaderAndScroll(_panel, "ygdr-fu-col-header-row",
                "ygdr-fu-col-header", "ygdr-fu-rows-scroll", out _leftHeaderLabel, out _rightHeaderLabel);
        }

        void RefreshPaletteColors()
        {
            if (_headerLabel == null) return;
            _headerLabel.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            SharedWindowStyles.ApplyStandardPanelPalette(_panel, _leftHeaderLabel, _rightHeaderLabel, _rowsScroll);
            RefreshTabStrip();
        }

        void RefreshAll()
        {
            if (_headerLabel == null) return;
            RefreshHeader();
            RefreshTabStrip();
            RefreshColumnsAndRows();
        }

        void RefreshHeader()
        {
            _headerLabel.text = _headerText;
        }

        void RefreshTabStrip()
        {
            bool show = _parameterName != null;
            _tabStripContainer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            SetTabButton(0, TabTransitions, L10n.Get("find_usage.tab.transitions").Replace("{n}", _transitionRows.Count.ToString()), _transitionRows.Count > 0);
            SetTabButton(1, TabBehaviors,   L10n.Get("find_usage.tab.behaviors").Replace("{n}", _behaviorRows.Count.ToString()),     _behaviorRows.Count > 0);
            SetTabButton(2, TabAapClips,    L10n.Get("find_usage.tab.aap_clips").Replace("{n}", _clipStates.Count.ToString()),        _clipStates.Count > 0);
#if VRC_SDK_VRCSDK3
            SetTabButton(3, TabObjects,     L10n.Get("find_usage.tab.objects").Replace("{n}", _effectingObjects.Count.ToString()),    _effectingObjects.Count > 0);
#endif
        }

        void SetTabButton(int uiIndex, int tabIndex, string label, bool enabled)
        {
            var button = _tabButtons[uiIndex];
            button.text = label;
            button.SetEnabled(enabled);
            button.EnableInClassList("ygdr-tab-strip-button-active", _activeTab == tabIndex);
            button.style.backgroundColor = _activeTab == tabIndex ? AnimationEditorWindow.AccentHoverColor : SharedWindowStyles.AccentColor;
        }

        void SetActiveTab(int tabIndex)
        {
            if (_activeTab == tabIndex) return;
            _activeTab = tabIndex;
            RefreshTabStrip();
            RefreshColumnsAndRows();
        }

        void RefreshColumnsAndRows()
        {
            bool isPathMode = _parameterName == null;
            string leftHeader;
            string rightHeader;
            bool isEmpty;
            string emptyMessage;

            if (isPathMode)
            {
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
                        leftHeader   = L10n.Get("find_usage.col.transition");
                        rightHeader  = L10n.Get("find_usage.col.condition");
                        isEmpty      = _transitionRows.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_transitions");
                        break;
                    case TabBehaviors:
                        leftHeader   = L10n.Get("find_usage.col.state_node");
                        rightHeader  = L10n.Get("find_usage.col.behavior");
                        isEmpty      = _behaviorRows.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_behaviors");
                        break;
                    case TabAapClips:
                        leftHeader   = L10n.Get("find_usage.col.state_node");
                        rightHeader  = L10n.Get("find_usage.col.animation_clip");
                        isEmpty      = _clipStates.Count == 0 && _clipAssets.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_clips");
                        break;
                    default: // TabObjects
                        leftHeader   = _effectingComponentTypeName;
                        rightHeader  = L10n.Get("find_usage.col.effecting_object");
                        isEmpty      = _effectingObjects.Count == 0;
                        emptyMessage = L10n.Get("find_usage.empty.no_objects");
                        break;
                }
            }

            _leftHeaderLabel.text = leftHeader;
            _rightHeaderLabel.text = rightHeader;

            _rowsScroll.Clear();

            if (isEmpty)
            {
                var emptyLabel = new Label(emptyMessage);
                emptyLabel.AddToClassList("ygdr-fu-empty");
                _rowsScroll.Add(emptyLabel);
                return;
            }

            if (isPathMode)
            {
                BuildClipRows();
                return;
            }

            switch (_activeTab)
            {
                case TabTransitions: BuildParameterRows(_transitionRows); break;
                case TabBehaviors:   BuildParameterRows(_behaviorRows);   break;
                case TabAapClips:    BuildClipRows();                     break;
                default:             BuildEffectingObjectRows();          break; // TabObjects
            }
        }

        VisualElement MakeRow(int index) => SharedWindowStyles.MakeStripedRow("ygdr-fu-row", index);

        static Button MakeClickableCell(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("ygdr-fu-cell");
            button.AddToClassList("ygdr-fu-cell-clickable");
            return button;
        }

        static VisualElement MakeEmptyCell()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("ygdr-fu-cell");
            return spacer;
        }

        void BuildParameterRows(List<UsageRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowElement = MakeRow(i);

                rowElement.Add(MakeClickableCell(row.transitionLabel, () =>
                {
                    if (row.transition != null)
                        AnimationEditorWindow.FocusTransition(row.transition, _controller);
                    else if (row.blendTree != null)
                        AnimationEditorWindow.FocusAsset(row.blendTree, _controller);
                    else if (row.state != null)
                        AnimationEditorWindow.FocusState(row.state, _controller);
                }));

                var conditionLabel = new Label(row.conditionLabel);
                conditionLabel.AddToClassList("ygdr-fu-cell");
                rowElement.Add(conditionLabel);

                _rowsScroll.Add(rowElement);
            }
        }

        void BuildClipRows()
        {
            int maxRows = Mathf.Max(_clipStates.Count, _clipAssets.Count);
            for (int i = 0; i < maxRows; i++)
            {
                var rowElement = MakeRow(i);

                if (i < _clipStates.Count)
                {
                    var state = _clipStates[i];
                    rowElement.Add(MakeClickableCell(state.name, () => AnimationEditorWindow.FocusAsset(state, _controller)));
                }
                else
                {
                    rowElement.Add(MakeEmptyCell());
                }

                if (i < _clipAssets.Count)
                {
                    var clip = _clipAssets[i];
                    rowElement.Add(MakeClickableCell(clip.name, () =>
                    {
                        Selection.activeObject = clip;
                        EditorGUIUtility.PingObject(clip);
                    }));
                }
                else
                {
                    rowElement.Add(MakeEmptyCell());
                }

                _rowsScroll.Add(rowElement);
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

        void BuildEffectingObjectRows()
        {
            for (int i = 0; i < _effectingObjects.Count; i++)
            {
                var go = _effectingObjects[i];
                if (go == null) continue;

                var rowElement = MakeRow(i);
                rowElement.Add(MakeEmptyCell());
                rowElement.Add(MakeClickableCell(go.name, () =>
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                }));

                _rowsScroll.Add(rowElement);
            }
        }
    }
}
#endif
