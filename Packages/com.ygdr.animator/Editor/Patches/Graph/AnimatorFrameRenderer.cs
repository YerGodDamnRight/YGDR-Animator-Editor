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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class FrameRenderer
    {
        internal static HashSet<FrameRect> SelectedFrames = new HashSet<FrameRect>();
        internal static bool IsSelected(FrameRect frame) => SelectedFrames.Contains(frame);
        internal static FrameRect SingleSelected => SelectedFrames.Count == 1 ? SelectedFrames.First() : null;

        internal static Rect LastGridRect;
        internal static float LastZoomLevel;
        internal static Vector2 LastScrollPosition;
        internal static FrameLayoutData LastFrameData;
        internal static AnimatorStateMachine LastRootLayerSM;
        internal static AnimatorStateMachine LastActiveSM;

        static GUIStyle _wrappedBoldLabel;
        static GUIStyle _wrappedCommentLabel;
        static GUIStyle _zLayerIndicatorStyle;

        static Type _cachedGraphGUIType;
        static PropertyInfo _scrollPositionProperty;
        static MethodInfo _getActiveStateMachineMethod;

        static AnimatorController _cachedController;
        static FrameLayoutData _cachedFrameData;
        static bool _cacheValid;
        static readonly HashSet<AnimatorController> _cleanedControllers = new HashSet<AnimatorController>();

        [InitializeOnLoadMethod]
        static void RegisterCacheInvalidation()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed -= InvalidateCache;
            Undo.undoRedoPerformed += InvalidateCache;
        }

        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (var mod in modifications)
            {
                if (mod.currentValue?.target is AnimatorController || mod.currentValue?.target is AnimatorStateMachine)
                {
                    _cacheValid = false;
                    _cleanedControllers.Clear();
                    break;
                }
            }
            return modifications;
        }

        static void InvalidateCache() { _cacheValid = false; _cleanedControllers.Clear(); }

        static void EnsureReflection(object graphGUI)
        {
            var type = graphGUI.GetType();
            if (type == _cachedGraphGUIType) return;
            _cachedGraphGUIType = type;
            _scrollPositionProperty = AccessTools.Property(type, "scrollPosition");
            _getActiveStateMachineMethod = AccessTools.Method(type, "get_activeStateMachine");
        }

        static FrameLayoutData GetFrameData(AnimatorController controller)
        {
            if (_cacheValid && controller == _cachedController && _cachedFrameData != null)
                return _cachedFrameData;
            _cachedController = controller;
            _cachedFrameData = FrameLayoutData.GetOrCreate(controller);
            _cacheValid = true;
            return _cachedFrameData;
        }

        internal static AnimatorStateMachine GetRootLayerSM(AnimatorController controller, AnimatorStateMachine activeSM)
        {
            foreach (var layer in controller.layers)
                if (ActiveSMReachable(layer.stateMachine, activeSM))
                    return layer.stateMachine;
            return null;
        }

        internal static bool ActiveSMReachable(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (target == null) return false;
            if (root == target) return true;
            return root.stateMachines.Any(childSM => ActiveSMReachable(childSM.stateMachine, target));
        }

        static void CleanupDeletedLayers(AnimatorController controller, FrameLayoutData frameData)
        {
            var validRoots = controller.layers
                .Select(layer => layer.stateMachine)
                .ToHashSet();

            bool changed = false;
            for (int i = frameData.frames.Count - 1; i >= 0; --i)
            {
                var frame = frameData.frames[i];
                if (frame.layerStateMachine == null || !validRoots.Contains(frame.layerStateMachine)
                    || !ActiveSMReachable(frame.layerStateMachine, frame.activeSM))
                {
                    SelectedFrames.Remove(frame);
                    frameData.frames.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(frameData);
        }

        internal static void DrawFrames(object graphGUI, Rect gridRect, float zoomLevel)
        {
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.framesEnabled) return;

                EnsureReflection(graphGUI);

                var scrollPosition = (Vector2)_scrollPositionProperty.GetValue(graphGUI);
                var activeSM = _getActiveStateMachineMethod?.Invoke(graphGUI, null) as AnimatorStateMachine;
                if (activeSM == null) return;

                var controllerPath = AssetDatabase.GetAssetPath(activeSM);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null) return;

                var frameData = GetFrameData(controller);
                if (frameData == null) return;

                if (_cleanedControllers.Add(controller))
                    CleanupDeletedLayers(controller, frameData);

                var rootLayerSM = GetRootLayerSM(controller, activeSM);

                LastGridRect = gridRect;
                LastZoomLevel = zoomLevel;
                LastScrollPosition = scrollPosition;
                LastFrameData = frameData;
                LastRootLayerSM = rootLayerSM;
                LastActiveSM = activeSM;

                if (frameData.frames.Count == 0) return;

                var framesToDraw = frameData.frames
                    .Where(frame => frame.activeSM == activeSM)
                    .OrderBy(frame => frame.zLayer);

                foreach (var frame in framesToDraw)
                {
                    var screenRect = GraphToScreen(frame.bounds, scrollPosition);
                    DrawFrame(frame, screenRect);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorTools] FrameRenderer error: {exception}");
            }
        }

        // GUI.matrix already handles zoom — no zoom multiplication needed here.
        // scrollPosition shifts the graph viewport; subtract to get GUI-space position.
        internal static Rect GraphToScreen(Rect graphRect, Vector2 scrollPosition)
        {
            return new Rect(
                graphRect.x - scrollPosition.x,
                graphRect.y - scrollPosition.y,
                graphRect.width,
                graphRect.height);
        }

        internal static Rect ScreenToGraph(Rect screenRect, Vector2 scrollPosition)
        {
            return new Rect(
                screenRect.x + scrollPosition.x,
                screenRect.y + scrollPosition.y,
                screenRect.width,
                screenRect.height);
        }

        static void DrawFrame(FrameRect frame, Rect screenRect)
        {
            bool isDarkSkin = EditorGUIUtility.isProSkin;
            Color textColor = isDarkSkin ? Color.white : new Color(0.1f, 0.1f, 0.1f);
            bool isSelected = IsSelected(frame);

            _wrappedBoldLabel ??= new GUIStyle(EditorStyles.boldLabel) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            _wrappedCommentLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true, alignment = TextAnchor.UpperLeft };

            float labelX = screenRect.x + 24;
            float labelWidth = screenRect.width - 28;
            var titleContent = new GUIContent(frame.title ?? "");
            float titleHeight = _wrappedBoldLabel.CalcHeight(titleContent, labelWidth) + _wrappedBoldLabel.lineHeight;
            float headerHeight = titleHeight - 6f;

            // Main fill
            EditorGUI.DrawRect(screenRect, frame.color);

            // Header strip overlay
            var headerRect = new Rect(screenRect.x, screenRect.y, screenRect.width, headerHeight);
            float headerAlpha = isSelected
                ? Mathf.Min(0.35f, frame.color.a * 0.5f + 0.18f)
                : Mathf.Min(0.18f, frame.color.a * 0.25f + 0.07f);
            EditorGUI.DrawRect(headerRect, new Color(1f, 1f, 1f, headerAlpha));

            // Border — 1f normal, 2f selected
            float borderThickness = isSelected ? 2f : 1f;
            Color borderColor = Color.Lerp(frame.color, Color.white, isSelected ? 0.5f : 0.3f);
            borderColor.a = Mathf.Min(1f, frame.color.a + (isSelected ? 0.4f : 0.2f));
            EditorGUI.DrawRect(new Rect(screenRect.x, screenRect.y, screenRect.width, borderThickness), borderColor);
            EditorGUI.DrawRect(new Rect(screenRect.x, screenRect.yMax - borderThickness, screenRect.width, borderThickness), borderColor);
            EditorGUI.DrawRect(new Rect(screenRect.x, screenRect.y, borderThickness, screenRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(screenRect.xMax - borderThickness, screenRect.y, borderThickness, screenRect.height), borderColor);

            var previousContentColor = GUI.contentColor;
            var titleRect = new Rect(labelX, screenRect.y + 4, labelWidth, titleHeight);

            if (!(FrameInteractionPatch.IsRenaming && SingleSelected == frame))
            {
                GUI.contentColor = textColor;
                GUI.Label(titleRect, titleContent, _wrappedBoldLabel);
            }

            if (!string.IsNullOrEmpty(frame.comments))
            {
                var commentsRect = new Rect(labelX, screenRect.y + headerHeight + 10, labelWidth, screenRect.height - headerHeight - 14);
                GUI.contentColor = textColor;
                GUI.Label(commentsRect, frame.comments, _wrappedCommentLabel);
            }

            GUI.contentColor = previousContentColor;

            var lockIconRect = new Rect(screenRect.x + 2, screenRect.y + 2, 18, 18);
            var previousColor = GUI.color;
            GUI.color = frame.locked
                ? (EditorGUIUtility.isProSkin ? Color.white : new Color(0.1f, 0.1f, 0.1f))
                : new Color(1f, 1f, 1f, 0.3f);
            GUI.Label(lockIconRect, EditorGUIUtility.IconContent("locked@2x"));
            GUI.color = previousColor;

            // Z-layer indicator top-right corner
            _zLayerIndicatorStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.55f) }
            };
            var zIndicatorRect = new Rect(screenRect.xMax - 34, screenRect.y + headerHeight * 0.5f - 7f, 32, 14);
            GUI.Label(zIndicatorRect, $"z{frame.zLayer}", _zLayerIndicatorStyle);

            if (SingleSelected == frame && !frame.locked)
                DrawResizeHandles(screenRect);
        }

        static void DrawResizeHandles(Rect screenRect)
        {
            foreach (var handleRect in GetHandleRects(screenRect))
                EditorGUI.DrawRect(handleRect, new Color(1f, 1f, 1f, 0.8f));
        }

        internal static Rect[] GetHandleRects(Rect screenRect)
        {
            const float handleSize = 8f;
            float half = handleSize * 0.5f;
            float centerX = screenRect.x + screenRect.width * 0.5f;
            float centerY = screenRect.y + screenRect.height * 0.5f;
            float right = screenRect.xMax;
            float bottom = screenRect.yMax;

            return new[]
            {
                new Rect(screenRect.x - half, screenRect.y - half, handleSize, handleSize), // top-left
                new Rect(right - half,         screenRect.y - half, handleSize, handleSize), // top-right
                new Rect(screenRect.x - half,  bottom - half,       handleSize, handleSize), // bottom-left
                new Rect(right - half,         bottom - half,       handleSize, handleSize), // bottom-right
                new Rect(centerX - half,       screenRect.y - half, handleSize, handleSize), // top-mid
                new Rect(centerX - half,       bottom - half,       handleSize, handleSize), // bottom-mid
                new Rect(screenRect.x - half,  centerY - half,      handleSize, handleSize), // left-mid
                new Rect(right - half,         centerY - half,      handleSize, handleSize), // right-mid
            };
        }
    }

    // Draws frames on the graph — separate from GridBackground so it stays active regardless of that feature's toggle
    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class FrameDrawPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DrawGridMethod;

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect gridRect, float zoomLevel)
        {
            try
            {
                if (Event.current.type != EventType.Repaint) return;
                FrameRenderer.DrawFrames(__instance, gridRect, zoomLevel);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] FrameDrawPatch: {e}"); }
        }
    }
}
#endif
