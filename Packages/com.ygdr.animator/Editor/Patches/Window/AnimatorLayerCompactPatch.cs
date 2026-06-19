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
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Shared state and draw logic for compact layer mode
    internal static class PatchLayerCompact
    {
        internal static bool IsCompact;

        static readonly FieldInfo _stylesField =
            AccessTools.Field(WindowPatchReflection.LayerControllerViewType, "s_Styles");
        internal static readonly FieldInfo RenameOverlayField =
            AccessTools.Field(WindowPatchReflection.LayerControllerViewType, "renameOverlay");
        internal static readonly MethodInfo RenameEndMethod =
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "RenameEnd");
        static readonly MethodInfo _deleteLayerMethod =
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "DeleteLayer");
        static readonly MethodInfo _showLayerSettingsMethod =
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.LayerSettingsWindow"),
                "ShowAtPosition",
                new[] { typeof(Rect), typeof(AnimatorControllerLayer), typeof(int), typeof(AnimatorController) });

        static bool _stylesLoaded;
        internal static GUIContent _addIcon;
        static GUIContent _settingsIcon;
        static GUIContent _sync, _syncTime, _ik, _additive, _mask;
        static GUIStyle _layerLabelStyle, _labelStyle;

        static GUIContent _compactIcon;
        internal static GUIContent CompactIcon => _compactIcon ??= EditorGUIUtility.IconContent("center@2x");

        internal static void EnsureStyles()
        {
            if (_stylesLoaded) return;
            var stylesObj = _stylesField?.GetValue(null);
            if (stylesObj == null) return;
            var stylesType   = stylesObj.GetType();
            _addIcon         = AccessTools.Field(stylesType, "addIcon")?.GetValue(stylesObj) as GUIContent;
            _settingsIcon    = AccessTools.Field(stylesType, "settingsIcon")?.GetValue(stylesObj) as GUIContent;
            _sync            = AccessTools.Field(stylesType, "sync")?.GetValue(stylesObj) as GUIContent;
            _syncTime        = AccessTools.Field(stylesType, "syncTime")?.GetValue(stylesObj) as GUIContent;
            _ik              = AccessTools.Field(stylesType, "ik")?.GetValue(stylesObj) as GUIContent;
            _additive        = AccessTools.Field(stylesType, "additive")?.GetValue(stylesObj) as GUIContent;
            _mask            = AccessTools.Field(stylesType, "mask")?.GetValue(stylesObj) as GUIContent;
            _layerLabelStyle = AccessTools.Field(stylesType, "layerLabel")?.GetValue(stylesObj) as GUIStyle;
            _labelStyle      = AccessTools.Field(stylesType, "label")?.GetValue(stylesObj) as GUIStyle;
            _stylesLoaded    = true;
        }

        internal static void DrawCompactLayer(Rect rect, int index, bool isActive, bool isFocused, object instance)
        {
            try
            {
                EnsureStyles();
                var currentEvent = Event.current;

                if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 && rect.Contains(currentEvent.mousePosition))
                {
                    currentEvent.Use();
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Copy layer"), false, () => PatchLayerCopyPaste.CopyLayer(instance));
                    if (PatchLayerCopyPaste._layerClipboard != null)
                    {
                        menu.AddItem(new GUIContent("Paste layer"), false,
                            () => PatchLayerCopyPaste.PasteLayer(instance));
                        menu.AddItem(new GUIContent("Paste layer settings"), false,
                            () => PatchLayerCopyPaste.PasteLayerSettings(instance));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Paste layer"));
                        menu.AddDisabledItem(new GUIContent("Paste layer settings"));
                    }
                    menu.AddItem(new GUIContent("Delete layer"), false,
                        () => _deleteLayerMethod?.Invoke(instance, null));
                    if (AnimatorDefaultSettings.Load().layerTemplateButtonEnabled)
                    {
                        var controller   = WindowPatchReflection.GetOpenController();
                        int capturedIndex = index;
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Create Template"), false, () =>
                            AnimatorTemplateParameterWindow.OpenCreate(controller, capturedIndex));
                    }
                    menu.ShowAsContext();
                }

                var reorderableList = (UnityEditorInternal.ReorderableList)WindowPatchReflection.LayerListField.GetValue(instance);
                var layer = reorderableList?.list[index] as AnimatorControllerLayer;
                if (layer == null) return;

                var labelStyle      = _labelStyle      ?? EditorStyles.label;
                var layerLabelStyle = _layerLabelStyle ?? EditorStyles.miniLabel;

                rect.yMin += 2f;
                rect.yMax -= 2f;

                // Settings button
                Vector2 settingsSize = EditorStyles.iconButton.CalcSize(_settingsIcon ?? GUIContent.none);
                var settingsRect = new Rect(rect.xMax - settingsSize.x - 4f, rect.yMin, settingsSize.x, rect.height);
                if (GUI.Button(settingsRect, _settingsIcon, EditorStyles.iconButton))
                {
                    var popupRect = settingsRect;
                    popupRect.x += 15f;
                    var controller = WindowPatchReflection.GetOpenController();
                    bool shown = _showLayerSettingsMethod != null &&
                        (bool)(_showLayerSettingsMethod.Invoke(null, new object[] { popupRect, layer, index, controller }) ?? false);
                    if (shown) GUIUtility.ExitGUI();
                }

                // WD indicator — overlay left of gear, no badge chain participation
                if (!EditorApplication.isPlaying && Event.current.type == EventType.Repaint)
                {
                    var wdSettings = AnimatorDefaultSettings.Load();
                    if (wdSettings.showLayerWDIndicator)
                    {
                        var stateMachine = layer.stateMachine;
                        bool isEmpty = PatchLayerWDIndicator.IsEmpty(stateMachine);

                        var (wdOn, wdOff) = isEmpty ? (0, 0)
                            : PatchLayerWDIndicator.GetOrComputeWD(stateMachine, wdSettings.wdIncludeBlendTreeStates);
                        bool showWD = !isEmpty && wdOn > 0;

                        var controller = WindowPatchReflection.GetOpenController();
                        bool hasFrameData = controller != null && PatchLayerWDIndicator.HasFrameData(stateMachine, controller);

                        var wdStyle = PatchLayerWDIndicator.LabelStyle;
                        float cursorX = settingsRect.xMin - 4f;

                        if (showWD)
                        {
                            float wdWidth = wdStyle.CalcSize(PatchLayerWDIndicator.WdContent).x;
                            var wdRect = new Rect(cursorX - wdWidth, rect.yMin, wdWidth, rect.height);
                            wdStyle.normal.textColor = wdOff == 0 ? wdSettings.layerWDColor : Color.cyan;
                            GUI.Label(wdRect, "WD", wdStyle);
                            cursorX = wdRect.x - 2f;
                        }

                        if (hasFrameData)
                        {
                            var frameIconRect = new Rect(cursorX - 16f, rect.yMin + (rect.height - 16f) * 0.5f, 16f, 16f);
                            GUI.Label(frameIconRect, PatchLayerWDIndicator.FrameIcon);
                            cursorX = frameIconRect.x - 4f;
                        }

                        if (isEmpty)
                        {
                            float emptyWidth = wdStyle.CalcSize(PatchLayerWDIndicator.EmptyContent).x;
                            var emptyRect = new Rect(cursorX - emptyWidth, rect.yMin, emptyWidth, rect.height);
                            wdStyle.normal.textColor = PatchLayerWDIndicator.EmptyColor;
                            GUI.Label(emptyRect, "empty", wdStyle);
                        }
                    }
                }

                // Badges — stack left of settings
                var badgeCursor = settingsRect;
                if (layer.syncedLayerIndex != -1)
                {
                    var badgeContent = layer.syncedLayerAffectsTiming ? _syncTime : _sync;
                    if (badgeContent != null)
                    {
                        Vector2 size = layerLabelStyle.CalcSize(badgeContent);
                        badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                        GUI.Label(badgeCursor, badgeContent, layerLabelStyle);
                    }
                }
                if (layer.iKPass && _ik != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_ik);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _ik, layerLabelStyle);
                }
                if (layer.blendingMode == AnimatorLayerBlendingMode.Additive && _additive != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_additive);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _additive, layerLabelStyle);
                }
                if (layer.avatarMask != null && _mask != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_mask);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _mask, layerLabelStyle);
                }

                // Name label
                var nameRect = Rect.MinMaxRect(rect.xMin, rect.yMin, badgeCursor.xMin - 4f, rect.yMax);

                // Rename overlay — use Property accessor (field accessor may be null if Unity exposes as property)
                var renameOverlay = WindowPatchReflection.LayerRenameOverlayProperty?.GetValue(instance)
                    ?? RenameOverlayField?.GetValue(instance);
                if (renameOverlay != null)
                {
                    var overlayTraverse  = Traverse.Create(renameOverlay);
                    bool isRenaming      = overlayTraverse.Method("IsRenaming").GetValue<bool>();
                    int  userData        = overlayTraverse.Property("userData").GetValue<int>();
                    bool waitingForDelay = overlayTraverse.Property("isWaitingForDelay").GetValue<bool>();

                    if (isRenaming && userData == index && !waitingForDelay)
                    {
                        if (nameRect.width >= 0f && nameRect.height >= 0f)
                        {
                            nameRect.x -= 2f;
                            overlayTraverse.Property("editFieldRect").SetValue(nameRect);
                        }
                        if (!overlayTraverse.Method("OnGUI").GetValue<bool>())
                            RenameEndMethod?.Invoke(instance, null);
                        return;
                    }
                }

                if (currentEvent.type == EventType.Repaint)
                    labelStyle.Draw(nameRect, layer.name, false, false, isActive, isFocused);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Compact layer draw error: {e}");
            }
        }
    }

    // Draws compact-mode toggle button left of the + button in the layer toolbar
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCompactButton
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnToolbarGUI");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                PatchLayerCompact.EnsureStyles();
                var toolbarRect      = GUILayoutUtility.GetLastRect();
                var icon             = PatchLayerCompact.CompactIcon;
                Vector2 iconSize     = EditorStyles.iconButton.CalcSize(icon);
                float addButtonWidth = PatchLayerCompact._addIcon != null
                    ? EditorStyles.iconButton.CalcSize(PatchLayerCompact._addIcon).x
                    : iconSize.x;
                var buttonRect = new Rect(
                    toolbarRect.xMax - addButtonWidth - 10f - 4f - iconSize.x,
                    toolbarRect.y + (int)((toolbarRect.height - iconSize.y) * 0.5f),
                    iconSize.x, iconSize.y);

                bool newCompact = GUI.Toggle(buttonRect, PatchLayerCompact.IsCompact, icon, EditorStyles.iconButton);
                if (newCompact != PatchLayerCompact.IsCompact)
                {
                    PatchLayerCompact.IsCompact = newCompact;
                    EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Layer compact button error: {e}");
            }
        }
    }

    // Applies compact element height and draw callback after ResetUI rebuilds the list
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCompactDraw
    {
        internal static UnityEditorInternal.ReorderableList.ElementCallbackDelegate OriginalDrawCallback;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var reorderableList = WindowPatchReflection.LayerListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                if (reorderableList == null) return;

                if (PatchLayerCompact.IsCompact)
                {
                    reorderableList.elementHeight = 22f;
                    if (OriginalDrawCallback == null)
                    {
                        OriginalDrawCallback = reorderableList.drawElementCallback;
                        var capturedInstance = __instance;
                        reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
                            PatchLayerCompact.DrawCompactLayer(rect, index, isActive, isFocused, capturedInstance);
                    }
                }
                else if (OriginalDrawCallback != null)
                {
                    reorderableList.elementHeight = 40f;
                    reorderableList.drawElementCallback = OriginalDrawCallback;
                    OriginalDrawCallback = null;
                }
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerCompactDraw.Prefix: {e}");
            }
        }
    }
}
#endif
