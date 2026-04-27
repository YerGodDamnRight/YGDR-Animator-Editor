#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using HarmonyLib;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class AnimatorGridBackgroundPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
                "DrawGrid");

        static Material _coloredMat;
        static Material ColoredMat => _coloredMat ??=
            new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };

        [HarmonyPostfix]
        static void Postfix(Rect gridRect, float zoomLevel)
        {
            var settings = AnimatorDefaultSettings.Load();
            if (!settings.graphGridOverride || Event.current.type != EventType.Repaint)
                return;

            float majorGridAlpha = Mathf.InverseLerp(0.25f, 1f, zoomLevel);
            float minorGridAlpha = Mathf.InverseLerp(0f, 1f, zoomLevel * 0.5f);

            if (settings.graphGridUseImage && settings.graphGridBackgroundImage != null)
            {
                // All GUI — DrawTexture then DrawRect lines, both deferred, render in call order
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, settings.graphGridBackgroundImageOpacity);
                GUI.DrawTexture(gridRect, settings.graphGridBackgroundImage, ScaleMode.ScaleAndCrop);
                GUI.color = previousColor;
                DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * 100f,
                    WithAlpha(settings.graphGridColorMajor, settings.graphGridColorMajor.a * majorGridAlpha));
                DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor),
                    WithAlpha(settings.graphGridColorMinor, settings.graphGridColorMinor.a * minorGridAlpha));
            }
            else
            {
                // All GL — solid color background + lines
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

                GL.Begin(GL.LINES);
                GL.Color(Color.Lerp(Color.clear, settings.graphGridColorMajor, majorGridAlpha));
                DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * 100f);
                GL.Color(Color.Lerp(Color.clear, settings.graphGridColorMinor, minorGridAlpha));
                DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor));
                GL.End();

                GL.PopMatrix();
            }
        }

        /* Emits GL.LINES for a uniform grid of vertical and horizontal lines at gridSize spacing within gridRect. */
        static void DrawGridLinesGL(Rect gridRect, float gridSize)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
            {
                GL.Vertex3(currentX, gridRect.yMin, 0);
                GL.Vertex3(currentX, gridRect.yMax, 0);
            }
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
            {
                GL.Vertex3(gridRect.xMin, currentY, 0);
                GL.Vertex3(gridRect.xMax, currentY, 0);
            }
        }

        /* Draws a uniform grid of 1px-wide vertical and horizontal rects at gridSize spacing within gridRect using EditorGUI.DrawRect. Used in image-background mode where GL cannot be mixed with GUI texture calls. */
        static void DrawGridRectsGUI(Rect gridRect, float gridSize, Color color)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
                EditorGUI.DrawRect(new Rect(currentX, gridRect.yMin, 1, gridRect.height), color);
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
                EditorGUI.DrawRect(new Rect(gridRect.xMin, currentY, gridRect.width, 1), color);
        }

        static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    }
}
#endif
