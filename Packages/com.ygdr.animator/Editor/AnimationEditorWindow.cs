#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow : EditorWindow
    {
        static readonly string[] _tabs = { "Transitions", "States", "Controller", "Settings" };
        bool[] _tabOpen = { true, false, false, false };
        Vector2 _scrollPosition;

        AnimatorStateTransition[] _selectedTransitions = Array.Empty<AnimatorStateTransition>();
        AnimatorState[] _selectedStates = Array.Empty<AnimatorState>();
        AnimatorController _controller;
        AnimatorStateMachine _activeStateMachine;
        string _controllerName = "—";
        string _layerName = "—";
        bool _showSharedConditions;

        [MenuItem("YGDR/YGDR Animator Editor")]
        static void Open()
        {
            var window = GetWindow<AnimationEditorWindow>("YGDR Animator Editor");
            window.minSize = new Vector2(540, 320);
            window.Show();
        }

        void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += PollAnimatorWindow;
            wantsMouseMove = true;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= PollAnimatorWindow;
        }

        void OnSelectionChanged()
        {
            _selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            _selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
            Repaint();
        }

        void PollAnimatorWindow()
        {
            if (AnimatorEditorInit.GraphType == null || AnimatorEditorInit.GetActiveStateMachineMethod == null) return;

            var graphs = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.GraphType);
            AnimatorStateMachine activeStateMachine = null;
            foreach (var graph in graphs)
            {
                activeStateMachine = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null) as AnimatorStateMachine;
                if (activeStateMachine != null) break;
            }

            if (activeStateMachine == null)
            {
                if (_controller != null) { _controller = null; _controllerName = "—"; _layerName = "—"; Repaint(); }
                return;
            }

            var path = AssetDatabase.GetAssetPath(activeStateMachine);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return;

            string layerName = "—";
            foreach (var layer in controller.layers)
            {
                if (SMContainsOrIs(layer.stateMachine, activeStateMachine)) { layerName = layer.name; break; }
            }

            string controllerName = controller.name;
            if (_controller == controller && _controllerName == controllerName && _layerName == layerName && _activeStateMachine == activeStateMachine) return;

            _controller = controller;
            _activeStateMachine = activeStateMachine;
            _controllerName = controllerName;
            _layerName = layerName;
            Repaint();
        }

        static bool SMContainsOrIs(AnimatorStateMachine sm, AnimatorStateMachine target)
        {
            if (sm == target) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsOrIs(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();
            DrawTabs();
            DrawSeparator();
            DrawLayerBar();
            EditorGUILayout.Space(1);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar);
            _scrollPosition.x = 0;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            if (_tabOpen[0]) { DrawSectionHeader("Transitions"); DrawTransitionsTab(); EditorGUILayout.Space(10); }
            if (_tabOpen[1]) { DrawSectionHeader("States");      DrawStatesTab();      EditorGUILayout.Space(10); }
            if (_tabOpen[2]) { DrawSectionHeader("Controller");  DrawControllerTab();  EditorGUILayout.Space(10); }
            if (_tabOpen[3]) { DrawSectionHeader("Settings");    DrawSettingsTab();    EditorGUILayout.Space(10); }
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        void DrawTabs()
        {
            using var _ = new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.ExpandWidth(true));
            for (int i = 0; i < _tabs.Length; i++)
            {
                var style = _tabOpen[i] ? Styles.TabActive : Styles.TabInactive;
                _tabOpen[i] = GUILayout.Toggle(_tabOpen[i], _tabs[i], style, GUILayout.ExpandWidth(true));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
        }

        void DrawLayerBar()
        {
            using var _ = new EditorGUILayout.HorizontalScope(Styles.LayerBar);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_controllerName} : {_layerName}", Styles.LayerName);
            GUILayout.FlexibleSpace();
        }

        static bool CursorBtn(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(text, style, options);
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            return clicked;
        }

        static bool CursorBtn(Rect rect, string text, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, text, style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return clicked;
        }

        static bool CursorBtn(Rect rect, GUIContent content, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, content, style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return clicked;
        }

        static void DrawSectionHeader(string label)
        {
            var rect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            var backgroundRect = rect;
            backgroundRect.x = 0;
            backgroundRect.width = EditorGUIUtility.currentViewWidth;
            EditorGUI.DrawRect(backgroundRect, Styles.SectionHeaderBg);
            GUI.Label(rect, label, Styles.TabSectionLabel);
        }

        static void DrawFooter()
        {
            var rect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.3f));
            GUI.Label(rect, "Created by YerGodDamnRight", Styles.FooterLabel);
            GUI.Label(rect, "V0.8.0", Styles.FooterVersion);
        }

        static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.4f));
        }
    }
}
#endif
