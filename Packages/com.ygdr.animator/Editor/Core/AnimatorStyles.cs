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
    internal static class AnimatorStyles
    {
        // ── Graph node overlay indicators ─────────────────────────────────────

        static GUIStyle _indicatorStyle;
        internal static GUIStyle IndicatorStyle => _indicatorStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _loopStyle;
        internal static GUIStyle LoopStyle => _loopStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _motionNameStyle;
        internal static GUIStyle MotionNameStyle => _motionNameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _coordsStyle;
        internal static GUIStyle CoordsStyle => _coordsStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 9,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _nodeNameStyle;
        internal static GUIStyle NodeNameStyle => _nodeNameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(2, 2, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
            normal    = { textColor = Color.white },
        };

        static GUIStyle _clipTimeStyle;
        internal static GUIStyle ClipTimeStyle => _clipTimeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 9,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        // ── Inline rename field (state + sub-SM) ──────────────────────────────

        static GUIStyle _renameFieldStyle;
        internal static GUIStyle RenameFieldStyle => _renameFieldStyle ??= new GUIStyle(EditorStyles.textField)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = null },
            focused   = { background = null },
            hover     = { background = null },
            active    = { background = null },
        };

        // ── Transition edge label ─────────────────────────────────────────────

        static GUIStyle _transitionEdgeLabelStyle;
        internal static GUIStyle TransitionEdgeLabelStyle => _transitionEdgeLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter };

        // ── Bottom bar ────────────────────────────────────────────────────────

        static GUIStyle _bottomBarLabelStyle;
        internal static GUIStyle BottomBarLabelStyle => _bottomBarLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
    }
}
#endif
