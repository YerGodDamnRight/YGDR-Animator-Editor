#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#if YGDR_MDV
using YGDR.MDV;
#endif

namespace YGDR.Editor.Animation
{
    [InitializeOnLoad]
    internal class ChangelogWindow : EditorWindow
    {
        const string ChangelogGuid = "ae34a2b8190066e448a0480da4fe96be";
        const string AutoShowPrefKey = "YGDR.Animator.Changelog.AutoShow";
        const string SkinBasePath = "Packages/com.ygdr.mdv/Editor/Skin/";

#if YGDR_MDV
        MarkdownViewer _viewer;
#endif
        Vector2 _scroll;

        static ChangelogWindow()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(AutoShowPrefKey, true)) Open();
            };
        }

        [MenuItem("YGDR/Animator Editor/Changelog")]
        internal static void Open()
        {
            var window = GetWindow<ChangelogWindow>("YGDR Animator Editor Changelog");
            window.minSize = new Vector2(560, 300);
        }

        void OnEnable() => LoadViewer();

        void OnDisable()
        {
#if YGDR_MDV
            EditorApplication.update -= TickViewer;
#endif
        }

        void LoadViewer()
        {
#if YGDR_MDV
            var path = AssetDatabase.GUIDToAssetPath(ChangelogGuid);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) return;

            var skinPath = SkinBasePath + (MarkdownPreferences.DarkSkin ? "MarkdownSkinDark.guiskin" : "MarkdownSkinLight.guiskin");
            var skin = AssetDatabase.LoadAssetAtPath<GUISkin>(skinPath);
            _viewer = new MarkdownViewer(skin, path, asset.text) { Editable = false };
            EditorApplication.update += TickViewer;
#endif
        }

#if YGDR_MDV
        void TickViewer()
        {
            if (_viewer != null && _viewer.Update()) Repaint();
        }
#endif

        void OnGUI()
        {
            bool autoShow = EditorPrefs.GetBool(AutoShowPrefKey, true);
            bool newAutoShow = EditorGUILayout.ToggleLeft("Show on Unity startup", autoShow);
            if (newAutoShow != autoShow) EditorPrefs.SetBool(AutoShowPrefKey, newAutoShow);
            GUILayout.Label("Find this window later at YGDR > Animator Editor > Changelog\nOr by clicking the version # in the editor window's footer", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

#if YGDR_MDV
            if (_viewer == null)
            {
                EditorGUILayout.HelpBox("Could not load changelog asset.", MessageType.Warning);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _viewer.Draw();
            EditorGUILayout.EndScrollView();
#else
            EditorGUILayout.HelpBox(
                "Install YGDR Markdown Viewer (com.ygdr.mdv) via Package Manager/VCC to view the changelog.",
                MessageType.Info);
#endif
        }
    }
}
#endif
