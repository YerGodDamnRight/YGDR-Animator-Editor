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
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class AnimatorGridBackgroundPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DrawGridMethod;

        static Material _coloredMat;
        static Material ColoredMat
        {
            get
            {
                if (_coloredMat == null)
                    _coloredMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                return _coloredMat;
            }
        }

        [HarmonyPrefix]
        static bool Prefix(Rect gridRect, float zoomLevel)
        {
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.graphGridOverride || Event.current.type != EventType.Repaint)
                    return true;

                float t = Mathf.InverseLerp(0.1f, 1f, zoomLevel);
                Color minorColor = Color.Lerp(Color.clear, settings.graphGridColorMinor, t);
                Color majorColor = Color.Lerp(settings.graphGridColorMinor, settings.graphGridColorMajor, t);

                if (settings.graphGridUseImage && settings.graphGridBackgroundImage != null)
                {
                    var previousColor = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, settings.graphGridBackgroundImageOpacity);
                    GUI.DrawTexture(gridRect, settings.graphGridBackgroundImage, ScaleMode.ScaleAndCrop);
                    GUI.color = previousColor;
                    if (settings.graphGridDrawLines)
                    {
                        float lineWidth = Mathf.Max(1f, 1f / zoomLevel);
                        DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor), minorColor, lineWidth);
                        DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * 100f, majorColor, lineWidth);
                    }
                }
                else
                {
                    ColoredMat.SetPass(0);
                    GL.PushMatrix();

                    GL.Begin(GL.QUADS);
                    var backgroundColor = settings.graphGridBackgroundColor;
                    backgroundColor.a = 1f;
                    GL.Color(backgroundColor);
                    GL.Vertex3(gridRect.xMin, gridRect.yMin, 0);
                    GL.Vertex3(gridRect.xMax, gridRect.yMin, 0);
                    GL.Vertex3(gridRect.xMax, gridRect.yMax, 0);
                    GL.Vertex3(gridRect.xMin, gridRect.yMax, 0);
                    GL.End();

                    if (settings.graphGridDrawLines)
                    {
                        GL.Begin(GL.LINES);
                        GL.Color(minorColor);
                        DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor));
                        GL.Color(majorColor);
                        DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * 100f);
                        GL.End();
                    }

                    GL.PopMatrix();
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorGridBackgroundPatch.Prefix: {e}");
                return true;
            }
        }

        static void DrawGridLinesGL(Rect gridRect, float gridSize)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
            {
                if (currentX < gridRect.xMin) continue;
                GL.Vertex3(currentX, gridRect.yMin, 0);
                GL.Vertex3(currentX, gridRect.yMax, 0);
            }
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
            {
                if (currentY < gridRect.yMin) continue;
                GL.Vertex3(gridRect.xMin, currentY, 0);
                GL.Vertex3(gridRect.xMax, currentY, 0);
            }
        }

        static void DrawGridRectsGUI(Rect gridRect, float gridSize, Color color, float lineWidth)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
            {
                if (currentX < gridRect.xMin) continue;
                EditorGUI.DrawRect(new Rect(currentX, gridRect.yMin, lineWidth, gridRect.height), color);
            }
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
            {
                if (currentY < gridRect.yMin) continue;
                EditorGUI.DrawRect(new Rect(gridRect.xMin, currentY, gridRect.width, lineWidth), color);
            }
        }
    }

}
#endif
