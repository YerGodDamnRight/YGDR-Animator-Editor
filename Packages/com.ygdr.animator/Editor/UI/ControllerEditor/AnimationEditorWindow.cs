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
using System.Linq;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
#if YGDR_MDV
using YGDR.MDV;
#endif

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow : EditorWindow
    {
        [SerializeField] bool[] _tabOpen = { true, false, false };
        [SerializeField] bool _settingsOpen;

        AnimatorStateTransition[] _selectedTransitions = Array.Empty<AnimatorStateTransition>();
        AnimatorTransition[] _selectedEntryTransitions = Array.Empty<AnimatorTransition>();
        AnimatorState[] _selectedStates = Array.Empty<AnimatorState>();
        AnimatorController _controller;
        AnimatorStateMachine _activeStateMachine;
        string _controllerName = "—";
        string _layerName = "—";
        string[] _subContextPath;
        UnityEngine.Object _cachedGraph;
        UnityEngine.Object _cachedBlendTreeGraphGUI;
        bool _showSharedConditions = true;
        bool _matchConditionName = true;
        bool _matchConditionMode = true;
        bool _matchConditionValue = false;
        bool _paletteApplied;

        Action _helpTransitions;
        Action _helpStates;
        Action _helpController;
        Action _helpSettings;
        static Action _helpDocs;

        VisualElement _layerBarRoot;
        ScrollView _sectionScrollView;
        VisualElement _footerRoot;
        VisualElement _footerBar;
        Label _footerVersionLabel;
        Button _docsButton;

        [MenuItem("YGDR/Animator Editor/Open", priority = 0)]
        static void Open()
        {
            var window = GetWindow<AnimationEditorWindow>("YGDR Animator Editor");
            window.minSize = new Vector2(540, 320);
            window.Show();
        }

        void OnEnable()
        {
            _cachedVersion    = null;
            _paletteApplied   = false;
            _helpTransitions  = MdvHelpAction("Transitions", 62, 84);
            _helpStates       = MdvHelpAction("States", 87, 134);
            _helpController   = MdvHelpAction("Controller", 137, 188);
            _helpSettings     = MdvHelpAction("Settings", 191, 289);
            _helpDocs         = MdvHelpAction("Tool Docs", -1, -1);
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += PollAnimatorWindow;
            ObjectChangeEvents.changesPublished += OnAssetChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += OnHierarchyChangedRefresh;
            L10n.OnLanguageChanged += RefreshLocalizedLabels;
            wantsMouseMove = true;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= PollAnimatorWindow;
            ObjectChangeEvents.changesPublished -= OnAssetChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= OnHierarchyChangedRefresh;
            SharedWindowStyles.UnregisterPaletteRefresh(RefreshPaletteColors);
            L10n.OnLanguageChanged -= RefreshLocalizedLabels;
            SetAutoRepathEnabled(false);
        }

        void OnUndoRedo()
        {
            EditorApplication.delayCall += () => { InvalidateConditionCache(); RefreshTransitionsTab(); RefreshStatesTab(); RefreshControllerSubTabVisibility(); Repaint(); };
        }

        void OnSelectionChanged()
        {
            _selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            _selectedEntryTransitions = Selection.objects.OfType<AnimatorTransition>().Where(t => !((t as object) is AnimatorStateTransition)).ToArray();
            _selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
            _conditionCacheDirty = true;
            UpdateSelectedClipIds();
            RefreshTransitionsTab();
            RefreshStatesTab();
            if (_controllerSubTab == 2) RefreshSubAssetsBody();
            if (_controllerSubTab == 3) RefreshMenusBody();
            ApplyInspectorModeTabs();
            Repaint();
        }

        void PollAnimatorWindow()
        {
            if (AnimatorEditorInit.GraphType == null || AnimatorEditorInit.GetActiveStateMachineMethod == null) return;

            AnimatorStateMachine activeStateMachine = null;
            if (_cachedGraph != null)
                activeStateMachine = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(_cachedGraph, null) as AnimatorStateMachine;

            if (activeStateMachine == null)
            {
                _cachedGraph = null;
                var graphs = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.GraphType);
                foreach (var graph in graphs)
                {
                    activeStateMachine = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null) as AnimatorStateMachine;
                    if (activeStateMachine != null) { _cachedGraph = graph; break; }
                }
            }

            if (activeStateMachine == null)
            {
                // Fallback: blend tree view — SM graph returns null activeStateMachine.
                // Derive controller from the blend tree graph's rootBlendTree asset path.
                var blendTreeController = TryGetControllerFromBlendTreeGraph();
                if (blendTreeController != null)
                {
                    var rootBlendTree         = TryGetRootBlendTree();
                    string blendTreeLayerName = rootBlendTree != null ? FindLayerForRootBlendTree(blendTreeController, rootBlendTree) : "—";
                    string blendTreeName      = rootBlendTree?.name;
                    bool subContextUnchanged  = blendTreeName == null ? _subContextPath == null : _subContextPath != null && _subContextPath.Length == 1 && _subContextPath[0] == blendTreeName;
                    if (_controller == blendTreeController && _layerName == blendTreeLayerName && subContextUnchanged) return;
                    _controller     = blendTreeController;
                    _controllerName = blendTreeController.name;
                    _layerName      = blendTreeLayerName;
                    _subContextPath = blendTreeName != null ? new[] { blendTreeName } : null;
                    RefreshLayerBar();
                    Repaint();
                    return;
                }

                var selectionController = TryGetControllerFromSelection();
                if (selectionController != null)
                {
                    if (_controller == selectionController) return;
                    var firstLayer = selectionController.layers.Length > 0 ? selectionController.layers[0] : default;
                    _controller = selectionController;
                    _activeStateMachine = firstLayer.stateMachine;
                    _controllerName = selectionController.name;
                    _layerName = firstLayer.stateMachine != null ? firstLayer.name : "—";
                    _subContextPath = null;
                    RefreshLayerBar();
                    Repaint();
                    return;
                }

                if (_controller != null) { _controller = null; _activeStateMachine = null; _controllerName = "—"; _layerName = "—"; _subContextPath = null; RefreshLayerBar(); Repaint(); }
                return;
            }

            var path = AssetDatabase.GetAssetPath(activeStateMachine);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return;

            string layerName = "—";
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == activeStateMachine || SMContainsOrIs(layer.stateMachine, activeStateMachine)) { layerName = layer.name; break; }
            }

            string controllerName = controller.name;
            if (_controller == controller && _controllerName == controllerName && _layerName == layerName && _activeStateMachine == activeStateMachine) return;

            _controller = controller;
            _activeStateMachine = activeStateMachine;
            _controllerName = controllerName;
            _layerName = layerName;
            _subContextPath = BuildSubSMPath(controller, layerName, activeStateMachine);
            RefreshLayerBar();
            Repaint();
        }

        static AnimatorController TryGetControllerFromSelection()
        {
            if (Selection.activeObject is AnimatorController selectedController) return selectedController;
            var animator = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Animator>() : null;
            return animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        }

        AnimatorController TryGetControllerFromBlendTreeGraph()
        {
            if (AnimatorEditorInit.BlendTreeGraphGUIType == null) return null;

            if (_cachedBlendTreeGraphGUI != null)
            {
                var controller = ControllerFromBlendTreeGraphGUI(_cachedBlendTreeGraphGUI);
                if (controller != null) return controller;
                _cachedBlendTreeGraphGUI = null;
            }

            foreach (var graphGUI in Resources.FindObjectsOfTypeAll(AnimatorEditorInit.BlendTreeGraphGUIType))
            {
                var controller = ControllerFromBlendTreeGraphGUI(graphGUI);
                if (controller != null) { _cachedBlendTreeGraphGUI = graphGUI; return controller; }
            }

            return null;
        }

        static AnimatorController ControllerFromBlendTreeGraphGUI(UnityEngine.Object graphGUI)
        {
            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return null;
            var rootBlendTree = Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
            if (rootBlendTree == null) return null;
            var assetPath = AssetDatabase.GetAssetPath(rootBlendTree);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }

        /* Returns true if sm is target or recursively contains target as a nested sub state machine. */
        static bool SMContainsOrIs(AnimatorStateMachine sm, AnimatorStateMachine target)
        {
            if (sm == target) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsOrIs(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        BlendTree TryGetRootBlendTree()
        {
            if (_cachedBlendTreeGraphGUI == null) return null;
            var graph = Traverse.Create(_cachedBlendTreeGraphGUI).Property("graph").GetValue();
            if (graph == null) return null;
            return Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
        }

        static string FindLayerForRootBlendTree(AnimatorController controller, BlendTree rootBlendTree)
        {
            foreach (var layer in controller.layers)
                if (SMContainsMotion(layer.stateMachine, rootBlendTree)) return layer.name;
            return "—";
        }

        static bool SMContainsMotion(AnimatorStateMachine sm, Motion target)
        {
            foreach (var childState in sm.states)
                if (childState.state.motion == target) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsMotion(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        static string[] BuildSubSMPath(AnimatorController controller, string layerName, AnimatorStateMachine target)
        {
            if (controller == null || target == null || layerName == "—") return null;
            var layer = System.Array.Find(controller.layers, l => l.name == layerName);
            if (layer == null || layer.stateMachine == target) return null;
            var pathSegments = new System.Collections.Generic.List<string>();
            if (FindSMPath(layer.stateMachine, target, pathSegments)) return pathSegments.ToArray();
            return null;
        }

        static bool FindSMPath(AnimatorStateMachine current, AnimatorStateMachine target, System.Collections.Generic.List<string> pathSegments)
        {
            foreach (var childStateMachine in current.stateMachines)
            {
                if (childStateMachine.stateMachine == target)
                {
                    pathSegments.Add(target.name);
                    return true;
                }
                if (FindSMPath(childStateMachine.stateMachine, target, pathSegments))
                {
                    pathSegments.Insert(0, childStateMachine.stateMachine.name);
                    return true;
                }
            }
            return false;
        }

        // ── Accordion sections (Phase 2) ────────────────────────────────────────
        Label _transitionsRightLabel, _statesRightLabel, _controllerRightLabel;
        Label _settingsTitleLabel, _transitionsTitleLabel, _statesTitleLabel, _controllerTitleLabel;
        VisualElement _settingsHeader, _transitionsHeader, _statesHeader, _controllerHeader;
        VisualElement _settingsBody, _transitionsBody, _statesBody, _controllerBody;
        Button _transitionsTabButton, _statesTabButton, _controllerTabButton;

        /* 4 collapsible sections each wrap one IMGUIContainer for still-IMGUI body content. */
        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.EnableInClassList("ygdr-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("ygdr-light", !EditorGUIUtility.isProSkin);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ygdr.animator/Editor/UI/SharedWindowStyles.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            // Unity's builtin Button:focus stylesheet rule outranks our USS override on specificity,
            // so the blue focus ring persists after clicks unless we blur the button ourselves.
            root.RegisterCallback<ClickEvent>(evt => { if (evt.target is Button button) button.Blur(); });

            SharedWindowStyles.RegisterPaletteRefresh(RefreshPaletteColors);

            if (!_paletteApplied)
            {
                _paletteApplied = true;
                var settings = AnimatorDefaultSettings.Load();
                SharedWindowStyles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
            }

            var tabStrip = new VisualElement();
            tabStrip.AddToClassList("ygdr-tab-strip");
            root.Add(tabStrip);
            _transitionsTabButton = AddTabStripButton(tabStrip, L10n.Get("tabs.transitions"), 0);
            _statesTabButton      = AddTabStripButton(tabStrip, L10n.Get("tabs.states"), 1);
            _controllerTabButton  = AddTabStripButton(tabStrip, L10n.Get("tabs.controller"), 2);

            _layerBarRoot = new VisualElement();
            _layerBarRoot.AddToClassList("ygdr-layer-bar");
            root.Add(_layerBarRoot);
            RefreshLayerBar();

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible };
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.contentContainer.style.maxWidth = Length.Percent(100);
            scrollView.AddToClassList("ygdr-section-scroll");
            root.Add(scrollView);
            _sectionScrollView = scrollView;

            (_transitionsHeader, _transitionsBody, _transitionsTitleLabel) = BuildAccordionShell(scrollView, L10n.Get("tabs.transitions"),
                () => _tabOpen[0], _helpTransitions, out _transitionsRightLabel, BuildTransitionsBody());
            RefreshTransitionsTab();
            (_statesHeader, _statesBody, _statesTitleLabel) = BuildAccordionShell(scrollView, L10n.Get("tabs.states"),
                () => _tabOpen[1], _helpStates, out _statesRightLabel, BuildStatesBody());
            RefreshStatesTab();
            (_controllerHeader, _controllerBody, _controllerTitleLabel) = BuildAccordionShell(scrollView, L10n.Get("tabs.controller"),
                () => _tabOpen[2], _helpController, out _controllerRightLabel, BuildControllerBody());
            RefreshControllerSubTabVisibility();
            (_settingsHeader, _settingsBody, _settingsTitleLabel) = BuildAccordionShell(scrollView, L10n.Get("tabs.settings"),
                () => _settingsOpen, _helpSettings, out _, BuildSettingsBody());

            _footerRoot = new VisualElement();
            _footerRoot.AddToClassList("ygdr-footer");
            root.Add(_footerRoot);
            BuildFooter(_footerRoot);

            RefreshTabStrip();
            RefreshPaletteColors();
        }

        /* Second entry point onto the same _tabOpen[] state the accordion headers use. */
        Button AddTabStripButton(VisualElement parent, string label, int index)
        {
            var button = new Button(() => ToggleTabStripSection(index)) { text = label };
            button.AddToClassList("ygdr-tab-strip-button");
            parent.Add(button);
            return button;
        }

        void ToggleTabStripSection(int index) => SetTabOpen(index, !_tabOpen[index]);

        void SetTabOpen(int index, bool open)
        {
            if (_tabOpen[index] == open) return;
            PreserveScrollOffset(() =>
            {
                _tabOpen[index] = open;
                var body = index == 0 ? _transitionsBody : index == 1 ? _statesBody : _controllerBody;
                var header = index == 0 ? _transitionsHeader : index == 1 ? _statesHeader : _controllerHeader;
                if (body != null) body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (header != null) header.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshTabStrip();
            });
        }

        /* Inspector Mode: opens only the tab relevant to selection, Controller as fallback. */
        void ApplyInspectorModeTabs()
        {
            if (!AnimatorDefaultSettings.Load().inspectorModeEnabled) return;

            bool stateSelected = _selectedStates.Length > 0;
            bool transitionSelected = _selectedTransitions.Length > 0;

            SetTabOpen(0, transitionSelected);
            SetTabOpen(1, stateSelected);
            SetTabOpen(2, !stateSelected && !transitionSelected);
        }

        void ToggleSettingsSection()
        {
            _settingsOpen = !_settingsOpen;
            if (_settingsBody == null) return;

            if (_settingsOpen)
            {
                if (_settingsHeader != null) _settingsHeader.style.display = DisplayStyle.Flex;
                AnimateSectionBody(_settingsHeader, _settingsBody, true, SnapScrollToBottom);
            }
            else
            {
                AnimateSectionBody(_settingsHeader, _settingsBody, false, SnapScrollToBottom);
                _settingsBody.schedule.Execute(() => { if (!_settingsOpen && _settingsHeader != null) _settingsHeader.style.display = DisplayStyle.None; })
                    .StartingIn((long)(SectionAnimDurationSeconds * 1000));
            }
        }

        // Settings sits at the bottom of the scroll (just above the footer). Keeping the scroll
        // pinned to the bottom every tween frame makes the section read as rising up out of the
        // footer rather than unrolling downward from the top.
        void SnapScrollToBottom()
        {
            if (_sectionScrollView == null) return;
            float maxScroll = Mathf.Max(0f, _sectionScrollView.contentContainer.resolvedStyle.height - _sectionScrollView.resolvedStyle.height);
            _sectionScrollView.scrollOffset = new Vector2(_sectionScrollView.scrollOffset.x, maxScroll);
        }

        /* Section toggle changes content height, which makes ScrollView reclamp and jump — restore offset after. */
        void PreserveScrollOffset(Action toggle)
        {
            if (_sectionScrollView == null) { toggle(); return; }
            var offset = _sectionScrollView.scrollOffset;
            toggle();
            _sectionScrollView.schedule.Execute(() => _sectionScrollView.scrollOffset = offset);
        }

        void RefreshTabStrip()
        {
            _transitionsTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _tabOpen[0]);
            _statesTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _tabOpen[1]);
            _controllerTabButton?.EnableInClassList("ygdr-tab-strip-button-active", _tabOpen[2]);
        }

        /* Header is not clickable — toggling only happens via AddTabStripButton / ToggleSettingsSection. */
        static (VisualElement header, VisualElement body, Label titleLabel) BuildAccordionShell(VisualElement parent, string title, Func<bool> getOpen, Action helpAction, out Label rightLabel, VisualElement bodyElement)
        {
            var section = new VisualElement();
            section.AddToClassList("ygdr-accordion-section");

            var header = new VisualElement();
            header.AddToClassList("ygdr-accordion-header");

            var body = bodyElement;
            body.style.marginBottom = 10;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ygdr-accordion-title");
            header.Add(titleLabel);

            var localRightLabel = new Label();
            localRightLabel.AddToClassList("ygdr-accordion-right-label");
            header.Add(localRightLabel);
            rightLabel = localRightLabel;

            if (helpAction != null)
            {
                var helpButton = new Button(helpAction);
                helpButton.AddToClassList("ygdr-accordion-help-button");
                helpButton.AddToClassList("ygdr-icon-btn-base");
                helpButton.style.backgroundImage = new StyleBackground(HelpIcon);
                helpButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                header.Add(helpButton);
            }

            section.Add(header);

            body.style.display = getOpen() ? DisplayStyle.Flex : DisplayStyle.None;
            header.style.display = getOpen() ? DisplayStyle.Flex : DisplayStyle.None;
            section.Add(body);

            parent.Add(section);
            return (header, body, titleLabel);
        }

        void RefreshLayerBar()
        {
            RefreshControllerSubTabVisibility();
            if (_layerBarRoot == null) return;
            _layerBarRoot.Clear();

            bool hasLayer      = _layerName != "—";
            bool hasSubContext = _subContextPath != null && _subContextPath.Length > 0;

            AddBreadcrumbSegment(_controllerName, isLeaf: !hasLayer && !hasSubContext);

            if (hasLayer)
            {
                AddBreadcrumbSeparator();
                AddBreadcrumbSegment(_layerName, isLeaf: !hasSubContext);
            }

            if (hasSubContext)
            {
                for (int i = 0; i < _subContextPath.Length; i++)
                {
                    AddBreadcrumbSeparator();
                    AddBreadcrumbSegment(_subContextPath[i], isLeaf: i == _subContextPath.Length - 1);
                }
            }
        }

        void AddBreadcrumbSegment(string text, bool isLeaf)
        {
            var label = new Label(text);
            label.AddToClassList("ygdr-breadcrumb-segment");
            label.EnableInClassList("ygdr-breadcrumb-leaf", isLeaf);
            _layerBarRoot.Add(label);
        }

        void AddBreadcrumbSeparator()
        {
            var label = new Label(" > ");
            label.AddToClassList("ygdr-breadcrumb-separator");
            _layerBarRoot.Add(label);
        }

        static Texture2D s_helpIcon;
        static Texture2D HelpIcon => s_helpIcon ??= EditorGUIUtility.IconContent("d__Help@2x").image as Texture2D;

        static Texture2D s_settingsIcon;
        static Texture2D SettingsIcon => s_settingsIcon ??= EditorGUIUtility.IconContent("d_SettingsIcon@2x").image as Texture2D;

        static Texture2D _discordIcon;
        static Texture2D _twitterIcon;
        static Texture2D _gumroadIcon;
        static Texture2D _jinxxyIcon;
        static Texture2D _boothIcon;
        static Texture2D DiscordIcon  => _discordIcon  ??= Resources.Load<Texture2D>("Discord-Icon");
        static Texture2D TwitterIcon  => _twitterIcon  ??= Resources.Load<Texture2D>("Twitter-Icon");
        static Texture2D GumroadIcon  => _gumroadIcon  ??= Resources.Load<Texture2D>("Gumroad-Icon");
        static Texture2D JinxxyIcon   => _jinxxyIcon   ??= Resources.Load<Texture2D>("Jinxxy-icon");
        static Texture2D BoothIcon   => _boothIcon   ??= Resources.Load<Texture2D>("Booth-icon");

        const string MdvDocGuid = "2dba3511e1633094a83bbdb970508e8f";

        static Action MdvHelpAction(string title, int lineMin, int lineMax)
        {
#if YGDR_MDV
            return () => YGDR.MDV.MDViewer.Open(MdvDocGuid, null, title, lineMin, lineMax, false);
#else
            return () => EditorApplication.delayCall += () => EditorUtility.DisplayDialog(
                "YGDR Markdown Viewer not installed",
                "Install YGDR Markdown Viewer (com.ygdr.mdv) via Package Manager/VCC to view help documentation.",
                "OK");
#endif
        }

        static string _cachedVersion;
        static string GetVersion()
        {
            if (_cachedVersion != null) return _cachedVersion;
            _cachedVersion = "V" + (UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AnimationEditorWindow).Assembly)?.version ?? "?");
            return _cachedVersion;
        }

        /* Colors/labels pushed in by RefreshPaletteColors/RefreshFooterLabels, not recomputed per frame. */
        void BuildFooter(VisualElement parent)
        {
            var separator = new VisualElement();
            separator.AddToClassList("ygdr-footer-separator");
            parent.Add(separator);

            _footerBar = new VisualElement();
            _footerBar.AddToClassList("ygdr-footer-bar");
            parent.Add(_footerBar);

            var settingsButton = new Button(ToggleSettingsSection);
            settingsButton.AddToClassList("ygdr-footer-icon-button");
            settingsButton.AddToClassList("ygdr-icon-btn-base");
            settingsButton.style.backgroundImage = new StyleBackground(SettingsIcon);
            _footerBar.Add(settingsButton);

            var createdByLabel = new Label("Created by YerGodDamnRight");
            createdByLabel.AddToClassList("ygdr-footer-text");
            _footerBar.Add(createdByLabel);

            var spacer = new VisualElement();
            spacer.AddToClassList("ygdr-footer-spacer");
            _footerBar.Add(spacer);

            AddFooterIconButton(_footerBar, DiscordIcon, "Discord",     "https://discord.gg/s8gTEk8xFb");
            AddFooterIconButton(_footerBar, TwitterIcon, "Twitter / X", "https://x.com/YerGodDamnRight");
            AddFooterIconButton(_footerBar, GumroadIcon, "Gumroad",     "https://yergoddamnright.gumroad.com");
            AddFooterIconButton(_footerBar, JinxxyIcon,  "Jinxxy",      "https://jinxxy.com/YerGodDamnRight");
            AddFooterIconButton(_footerBar, BoothIcon,   "Booth",       "https://yergoddamnright.booth.pm/");

            _docsButton = new Button(() => _helpDocs?.Invoke());
            _docsButton.AddToClassList("ygdr-footer-docs-button");
            _footerBar.Add(_docsButton);

            _footerVersionLabel = new Label();
            _footerVersionLabel.AddToClassList("ygdr-footer-text");
            _footerBar.Add(_footerVersionLabel);

            RefreshFooterLabels();
        }

        /// Shared row-container primitive: a VisualElement tagged with <paramref name="ussClass"/>, with
        /// <paramref name="children"/> appended and, if <paramref name="parent"/> is given, added to it.
        static VisualElement BuildRow(string ussClass, VisualElement parent = null, params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList(ussClass);
            foreach (var child in children) row.Add(child);
            parent?.Add(row);
            return row;
        }

        static Image BuildWarningIcon(Texture icon, string tooltip, string ussClass)
        {
            var image = new Image { image = icon, tooltip = tooltip };
            image.AddToClassList(ussClass);
            return image;
        }

        static void AddFooterIconButton(VisualElement parent, Texture2D icon, string tooltip, string url)
        {
            if (icon == null) return;
            var button = new Button(() => Application.OpenURL(url)) { tooltip = tooltip };
            button.AddToClassList("ygdr-footer-icon-button");
            button.AddToClassList("ygdr-icon-btn-base");
            button.style.backgroundImage = new StyleBackground(icon);
            parent.Add(button);
        }

        /* Text content that only changes on language switch or package version resolve — not palette. */
        void RefreshFooterLabels()
        {
            if (_docsButton != null) _docsButton.text = L10n.Get("footer.docs");
            if (_footerVersionLabel != null) _footerVersionLabel.text = GetVersion();
        }

        /* Wired to L10n.OnLanguageChanged so switching language relabels instantly. */
        void RefreshLocalizedLabels()
        {
            RefreshFooterLabels();
            if (_transitionsTabButton != null) _transitionsTabButton.text = L10n.Get("tabs.transitions");
            if (_statesTabButton != null) _statesTabButton.text = L10n.Get("tabs.states");
            if (_controllerTabButton != null) _controllerTabButton.text = L10n.Get("tabs.controller");
            if (_settingsTitleLabel != null) _settingsTitleLabel.text = L10n.Get("tabs.settings");
            if (_transitionsTitleLabel != null) _transitionsTitleLabel.text = L10n.Get("tabs.transitions");
            if (_statesTitleLabel != null) _statesTitleLabel.text = L10n.Get("tabs.states");
            if (_controllerTitleLabel != null) _controllerTitleLabel.text = L10n.Get("tabs.controller");
            RefreshTransitionsLocalizedLabels();
            RebuildTransitionTags();
            RebuildConditionRows();

            RefreshStatesLocalizedLabels();
            RebuildStateRows();

            RefreshControllerSubTabLabels();

            RefreshSettingsLocalizedLabels();
        }

        /* Registered via SharedWindowStyles.RegisterPaletteRefresh so live palette edits in Settings re-theme these too. */
        void RefreshPaletteColors()
        {
            if (_layerBarRoot != null) _layerBarRoot.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            if (_footerBar != null) _footerBar.style.backgroundColor = SharedWindowStyles.FooterBg;
            if (_settingsHeader != null) _settingsHeader.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            if (_transitionsHeader != null) _transitionsHeader.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            if (_statesHeader != null) _statesHeader.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            if (_controllerHeader != null) _controllerHeader.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            RefreshTransitionsPaletteColors();
            RefreshStatesPaletteColors();
            RefreshControllerPaletteColors();
            RefreshSettingsPaletteColors();
#if VRC_SDK_VRCSDK3
            RefreshSharedBehaviorsPaletteColors();
#endif
        }
    }
}
#endif