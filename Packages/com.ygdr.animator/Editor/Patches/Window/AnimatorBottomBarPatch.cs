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
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Bottom bar: selection count, active mode label, clickable controller path
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchBottomBar
    {
        static readonly GUIContent _tempContent = new GUIContent();


        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "DoGraphBottomBar");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect nameRect)
        {
            try
            {
                var controller = WindowPatchReflection.AnimatorControllerGetter?.Invoke(__instance, null)
                    as AnimatorController;
                if (controller == null) return;

                // Make existing controller path label clickable
                string controllerPath = AssetDatabase.GetAssetPath(controller);
                _tempContent.text = controllerPath;
                float controllerLabelWidth = EditorStyles.miniLabel.CalcSize(_tempContent).x + 18f;
                var controllerRect = new Rect(nameRect.xMax - controllerLabelWidth, nameRect.y, controllerLabelWidth, nameRect.height);
                EditorGUIUtility.AddCursorRect(controllerRect, MouseCursor.Link);

                var currentEvent = Event.current;
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && controllerRect.Contains(currentEvent.mousePosition))
                {
                    EditorGUIUtility.PingObject(controller);
                    if (currentEvent.clickCount == 2) Selection.activeObject = controller;
                    currentEvent.Use();
                }

                // Selection count label
                var bottomBarSettings = AnimatorDefaultSettings.Load();
                if (bottomBarSettings.showGraphFooter)
                {
                    int nodeCount = Selection.objects.OfType<AnimatorState>().Count();
                    int transitionCount = Selection.objects.OfType<AnimatorStateTransition>().Count();
                    _tempContent.text = "  " + string.Format(L10n.Get("bottom_bar.selection"), nodeCount, transitionCount);
                    float selectionWidth = AnimatorStyles.BottomBarLabelStyle.CalcSize(_tempContent).x;
                    DrawBarLabel(new Rect(nameRect.x, nameRect.y, selectionWidth, nameRect.height), _tempContent);
                }

                // Active mode label (centered)
                string modeText = GetModeText();
                if (!string.IsNullOrEmpty(modeText))
                {
                    _tempContent.text = modeText;
                    float modeWidth = AnimatorStyles.BottomBarLabelStyle.CalcSize(_tempContent).x;
                    float modeX = nameRect.x + (nameRect.width - modeWidth) * 0.5f;
                    DrawBarLabel(new Rect(modeX, nameRect.y, modeWidth, nameRect.height), _tempContent);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Bottom bar error: {e}");
            }
        }

        static void DrawBarLabel(Rect rect, GUIContent content)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField(content, AnimatorStyles.BottomBarLabelStyle);
            GUILayout.EndArea();
        }

        static string GetModeText()
        {
            var pasteKey = AnimatorDefaultSettings.Load().kbPaste.Label();
            if (PatchStateChainTransition.FanActive && PatchStateChainTransition.SeededFanActive) return L10n.Get("bottom_bar.fan_seeded");
            if (PatchStateChainTransition.FanActive && PatchTransitionCopyPaste.HasClipboard)    return string.Format(L10n.Get("bottom_bar.fan_with_paste"), pasteKey);
            if (PatchStateChainTransition.FanActive)                                             return L10n.Get("bottom_bar.fan");
            if (PatchStateChainTransition.ChainActive)              return L10n.Get("bottom_bar.chain");
            if (PatchTransitionCopyPaste.PasteActive)
            {
                int count = PatchTransitionCopyPaste.ClipboardCount;
                string key = count == 1 ? "bottom_bar.paste_transition" : "bottom_bar.paste_transitions";
                return string.Format(L10n.Get(key), count);
            }
            if (PatchStateNodeMenu._multiTransitionSources != null && PatchTransitionCopyPaste.HasClipboard) return string.Format(L10n.Get("bottom_bar.multi_with_paste"), pasteKey);
            if (PatchStateNodeMenu._multiTransitionSources != null) return L10n.Get("bottom_bar.multi");
            if (PatchStateNodeMenu._redirectTransitions != null)      return L10n.Get("bottom_bar.redirect");
            if (PatchStateNodeMenu._redirectEntryTransitions != null) return L10n.Get("bottom_bar.redirect");
            if (PatchStateNodeMenu._replicateTransitions != null)      return L10n.Get("bottom_bar.replicate");
            if (PatchStateNodeMenu._replicateEntryTransitions != null) return L10n.Get("bottom_bar.replicate");
            return null;
        }
    }
}
#endif
