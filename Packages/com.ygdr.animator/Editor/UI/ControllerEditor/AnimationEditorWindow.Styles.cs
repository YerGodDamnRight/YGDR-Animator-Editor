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
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        internal static class Styles
        {
            // ── Central palette ───────────────────────────────────────────────
            internal static Color PrimaryColor    = new(0.25f, 0.25f, 0.25f, 1f);
            internal static Color SecondaryColor  = new(0.30f, 0.30f, 0.30f, 1f);
            internal static Color AccentColor     = new(0.20f, 0.20f, 0.20f, 1f);
            internal static Color SectionHeaderBg => new Color(AccentColor.r * 0.8f, AccentColor.g * 0.8f, AccentColor.b * 0.8f, 1f);
            internal static Color FooterBg        => new Color(AccentColor.r * 0.55f, AccentColor.g * 0.55f, AccentColor.b * 0.55f, 1f);
            internal static Color RowAltColor => new Color(SecondaryColor.r * 0.80f, SecondaryColor.g * 0.80f, SecondaryColor.b * 0.80f, 1f);

            internal static void ApplyPalette(Color primaryColor, Color secondaryColor, Color accentColor)
            {
                PrimaryColor   = primaryColor;
                SecondaryColor = secondaryColor;
                AccentColor    = accentColor;
                InvalidatePaletteStyles();
            }

            internal static void InvalidatePaletteStyles()
            {
                s_accentTex                = null;
                s_accentHoverTex           = null;
                s_behaviorSectionHeader    = null;
                s_condModeBtn              = null;
                s_iconBtn                  = null;
                s_iconBtnActive            = null;
                s_condSwitchBtn            = null;
                s_condBtn                  = null;
                s_scrollToggleBtn          = null;
                s_controllerSubTabBtn      = null;
                s_controllerSubTabBtnActive = null;
                s_footerLinksBtn           = null;
                AnimatorTemplateParameterWindow.InvalidateStyles();
            }

            static GUIStyle s_boolBtnTrue;
            internal static GUIStyle BoolBtnTrue
            {
                get
                {
                    if (s_boolBtnTrue != null) return s_boolBtnTrue;
                    s_boolBtnTrue = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
                    s_boolBtnTrue.normal.textColor  = new Color(0.2f, 0.8f, 0.2f);
                    s_boolBtnTrue.hover.textColor   = new Color(0.2f, 0.8f, 0.2f);
                    s_boolBtnTrue.active.textColor  = new Color(0.2f, 0.8f, 0.2f);
                    s_boolBtnTrue.focused.textColor = new Color(0.2f, 0.8f, 0.2f);
                    return s_boolBtnTrue;
                }
            }

            static GUIStyle s_boolBtnFalse;
            internal static GUIStyle BoolBtnFalse
            {
                get
                {
                    if (s_boolBtnFalse != null) return s_boolBtnFalse;
                    s_boolBtnFalse = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
                    s_boolBtnFalse.normal.textColor  = new Color(0.8f, 0.2f, 0.2f);
                    s_boolBtnFalse.hover.textColor   = new Color(0.8f, 0.2f, 0.2f);
                    s_boolBtnFalse.active.textColor  = new Color(0.8f, 0.2f, 0.2f);
                    s_boolBtnFalse.focused.textColor = new Color(0.8f, 0.2f, 0.2f);
                    return s_boolBtnFalse;
                }
            }
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
            internal static readonly GUIStyle BreadcrumbParent = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = new Color(0.53f, 0.53f, 0.53f) }
            };
            internal static readonly GUIStyle BreadcrumbLeaf = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white }
            };
            internal static readonly GUIStyle SectionHeader = new(EditorStyles.toolbar)
            {
                fixedHeight = 24
            };

            static GUIStyle s_behaviorSectionHeader;
            internal static GUIStyle BehaviorSectionHeader
            {
                get
                {
                    if (s_behaviorSectionHeader != null) return s_behaviorSectionHeader;
                    var bgTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    bgTex.SetPixel(0, 0, AccentColor);
                    bgTex.Apply();
                    s_behaviorSectionHeader = new GUIStyle(GUIStyle.none)
                    {
                        fixedHeight = 24,
                        padding     = new RectOffset(8, 0, 0, 0),
                        normal      = { background = bgTex }
                    };
                    return s_behaviorSectionHeader;
                }
            }
            internal static readonly GUIStyle HeaderLabel = new(GUIStyle.none)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
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
            static Texture2D s_accentTex;
            static Texture2D AccentTex
            {
                get
                {
                    if (s_accentTex != null) return s_accentTex;
                    s_accentTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    s_accentTex.SetPixel(0, 0, AccentColor); s_accentTex.Apply();
                    return s_accentTex;
                }
            }

            static Texture2D s_accentHoverTex;
            static Texture2D AccentHoverTex
            {
                get
                {
                    if (s_accentHoverTex != null) return s_accentHoverTex;
                    s_accentHoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    s_accentHoverTex.SetPixel(0, 0, new Color(AccentColor.r + 0.1f, AccentColor.g + 0.1f, AccentColor.b + 0.1f, 1f)); s_accentHoverTex.Apply();
                    return s_accentHoverTex;
                }
            }

            static GUIStyle s_condModeBtn;
            internal static GUIStyle CondModeBtn
            {
                get
                {
                    if (s_condModeBtn != null) return s_condModeBtn;
                    s_condModeBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 11,
                        padding = new RectOffset(6, 0, 0, 0),
                        normal = { background = AccentTex,      textColor = EditorStyles.miniLabel.normal.textColor },
                        hover  = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        active = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor }
                    };
                    return s_condModeBtn;
                }
            }

            static GUIStyle s_iconBtn;
            internal static GUIStyle IconBtn
            {
                get
                {
                    if (s_iconBtn != null) return s_iconBtn;
                    s_iconBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding   = new RectOffset(2, 2, 2, 2),
                        normal    = { background = AccentTex,      textColor = EditorStyles.miniLabel.normal.textColor },
                        hover     = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        active    = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor }
                    };
                    return s_iconBtn;
                }
            }

            static GUIStyle s_iconBtnActive;
            internal static GUIStyle IconBtnActive
            {
                get
                {
                    if (s_iconBtnActive != null) return s_iconBtnActive;
                    s_iconBtnActive = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding   = new RectOffset(2, 2, 2, 2),
                        normal    = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        hover     = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        active    = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor }
                    };
                    return s_iconBtnActive;
                }
            }

            static GUIStyle s_condSwitchBtn;
            internal static GUIStyle CondSwitchBtn
            {
                get
                {
                    if (s_condSwitchBtn != null) return s_condSwitchBtn;
                    s_condSwitchBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding   = new RectOffset(2, 2, 2, 2),
                        fontSize  = 16,
                        normal    = { background = AccentTex,      textColor = EditorStyles.miniLabel.normal.textColor },
                        hover     = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor },
                        active    = { background = AccentHoverTex, textColor = EditorStyles.miniLabel.normal.textColor }
                    };
                    return s_condSwitchBtn;
                }
            }
            internal static readonly GUIStyle CloseBtn = new(GUIStyle.none)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 10,
                padding   = new RectOffset(0, 4, 0, 0),
                normal    = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover     = { textColor = Color.white }
            };

            static GUIStyle s_condBtn;
            internal static GUIStyle CondBtn
            {
                get
                {
                    if (s_condBtn != null) return s_condBtn;
                    var normalTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    normalTex.SetPixel(0, 0, SecondaryColor);
                    normalTex.Apply();
                    var hoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    hoverTex.SetPixel(0, 0, new Color(0.33f, 0.33f, 0.33f, 1f));
                    hoverTex.Apply();
                    s_condBtn = new GUIStyle(GUIStyle.none)
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
                    return s_condBtn;
                }
            }
            internal static readonly GUIStyle BehaviorSectionLabel = new(GUIStyle.none)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(8, 0, 0, 0),
                normal    = { textColor = EditorStyles.miniLabel.normal.textColor }
            };
            internal static readonly GUIStyle BehaviorHeaderBtn = new(EditorStyles.miniButton)
            {
                margin = new RectOffset(4, 4, 3, 3)
            };
            internal static readonly GUIStyle SectionPadded = new(GUIStyle.none)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };
            internal static readonly GUIStyle CondBody = new(GUIStyle.none)
            {
                padding = new RectOffset(6, 6, 6, 6)
            };
            internal static readonly GUIStyle FindUsesHeader = new(TabSectionLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                richText  = true
            };
            internal static readonly GUIStyle EmptyLabel = new(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize   = 11,
                fixedHeight = 30,
                alignment  = TextAnchor.MiddleCenter,
                padding    = new RectOffset(0, 0, 0, 8)
            };
            internal static readonly GUIStyle SmallLabel = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 11,
                padding   = new RectOffset(0, 0, 3, 3)
            };
            internal static readonly GUIStyle StateRowName = new(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            internal static readonly GUIStyle TransitionTagLabel = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            internal static readonly GUIStyle TransitionTagBtn = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
            };
            internal static readonly GUIStyle SectionHeaderCount = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding   = new RectOffset(0, 8, 0, 0),
                normal    = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            static GUIStyle s_footerLinksBtn;
            internal static GUIStyle FooterLinksBtn
            {
                get
                {
                    if (s_footerLinksBtn != null) return s_footerLinksBtn;
                    s_footerLinksBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize  = 10,
                        fontStyle = FontStyle.Bold,
                        normal    = { background = AccentTex,      textColor = new Color(0.75f, 0.75f, 0.75f) },
                        hover     = { background = AccentHoverTex, textColor = Color.white },
                        active    = { background = AccentHoverTex, textColor = Color.white }
                    };
                    return s_footerLinksBtn;
                }
            }

            internal static readonly GUIStyle FooterText = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(6, 0, 0, 0),
                normal    = { textColor = new Color(0.75f, 0.75f, 0.75f) }
            };
            internal static readonly GUIStyle FooterDocsBtn = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                hover     = { textColor = Color.white }
            };
            internal static readonly GUIStyle CondDuplicateLabel = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding   = new RectOffset(0, 20, 0, 0),
                normal    = { textColor = new Color(0.86f, 0.15f, 0.15f) }
            };
            internal static readonly GUIStyle MiniLabelRight = new(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding   = new RectOffset(0, 16, 0, 0)
            };
            internal static readonly GUIStyle SmallLabelCenter = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 11
            };
            internal static readonly GUIStyle SubAssetListLabel = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 11,
                richText  = true,
                padding   = new RectOffset(8, 2, 0, 0)
            };
            internal static readonly GUIStyle SubAssetSearchHint = new(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            // Single source of truth for the scroll-toggle pill width in pixels. All geometry derives from this.
            internal const int k_pillW = 8;

            static GUIStyle s_scrollToggleBtn;
            internal static GUIStyle ScrollToggleBtn
            {
                get
                {
                    if (s_scrollToggleBtn != null) return s_scrollToggleBtn;
                    int capH = Mathf.CeilToInt(k_pillW / 2f);
                    var hoverColor  = new Color(AccentColor.r + 0.08f, AccentColor.g + 0.08f, AccentColor.b + 0.08f);
                    var activeColor = new Color(AccentColor.r + 0.16f, AccentColor.g + 0.16f, AccentColor.b + 0.16f);
                    var offTex    = MakePillTex(AccentColor);
                    var offHovTex = MakePillTex(hoverColor);
                    var onTex     = MakePillTex(AccentColor);
                    var onHovTex  = MakePillTex(hoverColor);
                    s_scrollToggleBtn = new GUIStyle(GUIStyle.none)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize  = 11,
                        border    = new RectOffset(0, 0, capH, capH),
                        normal    = { background = offTex,    textColor = new Color(0.5f, 0.5f, 0.5f) },
                        hover     = { background = offHovTex, textColor = new Color(0.7f, 0.7f, 0.7f) },
                        active    = { background = MakePillTex(activeColor), textColor = Color.white },
                        onNormal  = { background = onTex,     textColor = Color.white },
                        onHover   = { background = onHovTex,  textColor = Color.white },
                        onActive  = { background = MakePillTex(activeColor), textColor = Color.white },
                    };
                    return s_scrollToggleBtn;
                }
            }

            // Pill texture: all dimensions derived from k_pillW. Top/bottom caps are semicircles; center band stretches vertically.
            static Texture2D MakePillTex(Color color)
            {
                float capRadius = k_pillW / 2f;
                int capHeight   = Mathf.CeilToInt(capRadius);
                int texW        = k_pillW;
                int texH        = capHeight * 2 + 2;
                float cx        = (texW - 1) / 2f;
                float topCY     = capHeight - 0.5f;
                float botCY     = texH - capHeight - 0.5f;
                var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < texH; y++)
                {
                    for (int x = 0; x < texW; x++)
                    {
                        float alpha;
                        if (y >= capHeight && y < capHeight + 2)
                        {
                            alpha = 1f;
                        }
                        else
                        {
                            float cy = y < capHeight ? topCY : botCY;
                            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                            alpha = Mathf.Clamp01(capRadius - dist + 0.5f);
                        }
                        tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                    }
                }
                tex.Apply();
                return tex;
            }

            static GUIStyle s_controllerSubTabBtn;
            internal static GUIStyle ControllerSubTabBtn
            {
                get
                {
                    if (s_controllerSubTabBtn != null) return s_controllerSubTabBtn;
                    s_controllerSubTabBtn = new GUIStyle(IconBtn) { fontSize = 10 };
                    return s_controllerSubTabBtn;
                }
            }

            static GUIStyle s_controllerSubTabBtnActive;
            internal static GUIStyle ControllerSubTabBtnActive
            {
                get
                {
                    if (s_controllerSubTabBtnActive != null) return s_controllerSubTabBtnActive;
                    s_controllerSubTabBtnActive = new GUIStyle(IconBtnActive) { fontSize = 10 };
                    return s_controllerSubTabBtnActive;
                }
            }
        }
    }
}
#endif
