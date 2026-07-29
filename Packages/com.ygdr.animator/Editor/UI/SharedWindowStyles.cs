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
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YGDR.Editor.Animation
{
    internal static class SharedWindowStyles
    {
        // ── Central palette ───────────────────────────────────────────────
        internal static Color PrimaryColor    = new(0.25f, 0.25f, 0.25f, 1f);
        internal static Color SecondaryColor  = new(0.30f, 0.30f, 0.30f, 1f);
        internal static Color AccentColor     = new(0.20f, 0.20f, 0.20f, 1f);
        internal static Color SectionHeaderBg => new Color(AccentColor.r * 0.8f, AccentColor.g * 0.8f, AccentColor.b * 0.8f, 1f);
        internal static Color FooterBg        => new Color(AccentColor.r * 0.55f, AccentColor.g * 0.55f, AccentColor.b * 0.55f, 1f);
        internal static Color RowAltColor => new Color(SecondaryColor.r * 0.80f, SecondaryColor.g * 0.80f, SecondaryColor.b * 0.80f, 1f);

        static readonly System.Collections.Generic.List<Action> s_paletteRefreshers = new();

        /* owner overload auto-unsubscribes on detach (safe for rows/buttons that get rebuilt). */
        internal static void RegisterPaletteRefresh(VisualElement owner, Action refresh)
        {
            s_paletteRefreshers.Add(refresh);
            owner.RegisterCallback<DetachFromPanelEvent>(_ => s_paletteRefreshers.Remove(refresh));
            refresh();
        }

        // Plain overload for whole-window dispatchers that unregister manually on window close.
        internal static void RegisterPaletteRefresh(Action refresh)
        {
            s_paletteRefreshers.Add(refresh);
            refresh();
        }

        internal static void UnregisterPaletteRefresh(Action refresh) => s_paletteRefreshers.Remove(refresh);

        internal static void ApplyPalette(Color primaryColor, Color secondaryColor, Color accentColor)
        {
            PrimaryColor   = primaryColor;
            SecondaryColor = secondaryColor;
            AccentColor    = accentColor;
            foreach (var refresh in s_paletteRefreshers.ToArray()) refresh();
        }

        static Texture2D s_resizeGripTex;
        internal static Texture2D ResizeGripTex => s_resizeGripTex ??= EditorGUIUtility.IconContent("avatarblendtrianglelefta").image as Texture2D;

        // ── Shared satellite-window UI scaffolding (AnimatorFindUsageWindow, AnimatorTemplateParameterWindow) ──

        /// Column-header row (two Labels) + a rows ScrollView, appended to <paramref name="parent"/>.
        internal static ScrollView BuildColumnHeaderAndScroll(VisualElement parent, string headerRowClass, string headerClass,
            string scrollClass, out Label leftHeaderLabel, out Label rightHeaderLabel)
        {
            var columnHeaderRow = new VisualElement();
            columnHeaderRow.AddToClassList(headerRowClass);
            parent.Add(columnHeaderRow);

            leftHeaderLabel = new Label();
            leftHeaderLabel.AddToClassList(headerClass);
            leftHeaderLabel.AddToClassList("u-flex-fill");
            leftHeaderLabel.AddToClassList("u-col-header-label");
            columnHeaderRow.Add(leftHeaderLabel);

            rightHeaderLabel = new Label();
            rightHeaderLabel.AddToClassList(headerClass);
            rightHeaderLabel.AddToClassList("u-flex-fill");
            rightHeaderLabel.AddToClassList("u-col-header-label");
            columnHeaderRow.Add(rightHeaderLabel);

            var rowsScroll = new ScrollView(ScrollViewMode.Vertical);
            rowsScroll.AddToClassList(scrollClass);
            parent.Add(rowsScroll);
            return rowsScroll;
        }

        /// A row VisualElement with alt-row background applied on odd indices.
        internal static VisualElement MakeStripedRow(string rowClass, int index)
        {
            var row = new VisualElement();
            row.AddToClassList(rowClass);
            if (index % 2 == 1) row.style.backgroundColor = RowAltColor;
            return row;
        }

        /// Assigns the standard Primary/Accent/Accent/Secondary palette to a satellite window's panel/header/scroll.
        internal static void ApplyStandardPanelPalette(VisualElement panel, Label leftHeaderLabel, Label rightHeaderLabel, ScrollView rowsScroll)
        {
            panel.style.backgroundColor = PrimaryColor;
            leftHeaderLabel.style.backgroundColor = AccentColor;
            rightHeaderLabel.style.backgroundColor = AccentColor;
            rowsScroll.style.backgroundColor = SecondaryColor;
        }
    }
}
#endif
