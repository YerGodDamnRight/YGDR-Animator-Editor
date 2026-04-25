#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        static class Styles
        {
            internal static readonly Color PillBg = new(0.25f, 0.25f, 0.25f, 1f);
            internal static readonly Color CondSectionBg = new(0.28f, 0.28f, 0.28f, 1f);
            internal static readonly Color SectionHeaderBg = new(0.18f, 0.18f, 0.18f, 1f);

            internal static readonly GUIStyle CondTrue = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.2f, 0.8f, 0.2f) }
            };
            internal static readonly GUIStyle CondFalse = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.2f, 0.2f) }
            };
            internal static readonly GUIStyle TabActive = new(EditorStyles.toolbarButton)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 24,
                fontSize = 12
            };
            internal static readonly GUIStyle TabInactive = new(EditorStyles.toolbarButton)
            {
                fixedHeight = 24,
                fontSize = 12
            };
            internal static readonly GUIStyle LayerBar = new(EditorStyles.toolbar)
            {
                fixedHeight = 22,
                alignment = TextAnchor.MiddleCenter
            };
            internal static readonly GUIStyle LayerName = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            internal static readonly GUIStyle SectionHeader = new(EditorStyles.toolbar)
            {
                fixedHeight = 24
            };
            internal static readonly GUIStyle HeaderLabel = new(GUIStyle.none)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = EditorStyles.miniLabel.normal.textColor }
            };
            internal static readonly GUIStyle TabSectionLabel = new(GUIStyle.none)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(8, 0, 0, 0),
                normal    = { textColor = Color.white }
            };
            internal static readonly GUIStyle CondHeader = new(EditorStyles.toolbar)
            {
                fixedHeight = 22
            };
            internal static readonly GUIStyle CondDot = new(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 0.45f, 0.1f) }
            };

            static GUIStyle s_condModeBtn;
            internal static GUIStyle CondModeBtn
            {
                get
                {
                    if (s_condModeBtn != null) return s_condModeBtn;
                    var hoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    hoverTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.08f));
                    hoverTex.Apply();
                    s_condModeBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 11,
                        padding = new RectOffset(6, 0, 0, 0),
                        normal = { textColor = EditorStyles.miniLabel.normal.textColor },
                        hover  = { background = hoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        active = { background = hoverTex, textColor = EditorStyles.miniLabel.normal.textColor }
                    };
                    return s_condModeBtn;
                }
            }

            internal static readonly GUIStyle IconBtn = new(EditorStyles.toolbarButton)
            {
                padding = new RectOffset(2, 2, 2, 2)
            };
            internal static readonly GUIStyle CondSwitchBtn = new(EditorStyles.toolbarButton)
            {
                padding = new RectOffset(2, 2, 2, 2),
                fontSize = 16
            };
            internal static readonly GUIStyle HeaderCloseBtn = new(GUIStyle.none)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 10,
                padding   = new RectOffset(0, 4, 0, 0),
                normal    = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover     = { textColor = Color.white }
            };

            static GUIStyle s_condActionBtn;
            internal static GUIStyle CondActionBtn
            {
                get
                {
                    if (s_condActionBtn != null) return s_condActionBtn;
                    var normalTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    normalTex.SetPixel(0, 0, CondSectionBg);
                    normalTex.Apply();
                    var hoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    hoverTex.SetPixel(0, 0, new Color(0.33f, 0.33f, 0.33f, 1f));
                    hoverTex.Apply();
                    s_condActionBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        fontSize  = 13,
                        margin    = new RectOffset(0, 0, 0, 0),
                        padding   = new RectOffset(0, 0, 0, 0),
                        normal    = { background = normalTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        hover     = { background = hoverTex,  textColor = EditorStyles.miniLabel.normal.textColor },
                        active    = { background = hoverTex,  textColor = Color.white }
                    };
                    return s_condActionBtn;
                }
            }
            internal static readonly GUIStyle EmptyLabel = new(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 11,
                fixedHeight = 30,
                alignment = TextAnchor.MiddleCenter
            };
            internal static readonly GUIStyle SmallLabel = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };
            internal static readonly GUIStyle StateRowName = new(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            internal static readonly GUIStyle StateRowXBtn = new(GUIStyle.none)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 10,
                normal    = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover     = { textColor = Color.white }
            };
            internal static readonly GUIStyle PillLabel = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            internal static readonly GUIStyle PillBtn = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
            };
            internal static readonly GUIStyle FooterLabel = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding   = new RectOffset(0, 6, 0, 0),
                normal    = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            internal static readonly GUIStyle FooterVersion = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(6, 0, 0, 0),
                normal    = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
        }
    }
}
#endif
