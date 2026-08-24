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
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // ── Node background texture replacement ──────────────────────────────────
    // Patches Styles.GetNodeStyle to swap the background Texture2D with a
    // tinted copy of the RATS node PNGs, preserving rounded corners and shape.

    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class PatchNodeStyles
    {

        const string SubStateMachineStyleName = "node hex";

        static readonly Dictionary<string, GUIStyle> _styleCache = new();
        static readonly Dictionary<(Color, bool, bool, bool, Color), Texture2D> _texCache = new();

        static Texture2D _baseNode;
        static Texture2D _baseNodeActive;
        static Texture2D _baseSubSM;
        static Texture2D _baseSubSMActive;

        static (Color state, Color def, Color subSM, Color entry, Color exit, Color any, Color selection, Color bt1D, Color bt2D, Color btDirect) _cachedColors;
        static bool _cached3DEnabled;

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(GraphPatchReflection.StylesType, "GetNodeStyle");
        }

        [HarmonyPostfix]
        static void Postfix(ref GUIStyle __result, string styleName, int color, bool on)
        {
            try
            {
                if (__result == null) return;
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.nodeColorEnabled) return;

                EnsureBaseTextures();
                if (_baseNode == null) return;

                if (ColorsChanged(settings)) Rebuild();

                BlendTreeType? resolvedBlendType = null;
                if (PatchBlendTreeOnGraphGUI.InBlendTreeGUI && color == 0)
                {
                    if (PatchBlendTreeOnGraphGUI.LightweightOverrideActive)
                        resolvedBlendType = PatchBlendTreeOnGraphGUI.LightweightOverrideBlendType;
                    else if (PatchBlendTreeNodeGUI.InNodeGUI && PatchBlendTreeNodeGUI.CurrentBlendType.HasValue)
                        resolvedBlendType = PatchBlendTreeNodeGUI.CurrentBlendType;
                    else if (PatchBlendTreeOnGraphGUI._blendTypeQueue.Count > 0)
                        resolvedBlendType = PatchBlendTreeOnGraphGUI._blendTypeQueue.Dequeue();
                }
                string blendTypeSuffix = resolvedBlendType.HasValue ? $"|bt{(int)resolvedBlendType.Value}" : "";
                string styleKey = $"{styleName}|{color}|{on}{blendTypeSuffix}";
                if (_styleCache.TryGetValue(styleKey, out var cached))
                {
                    __result = cached;
                    return;
                }

                bool isSubStateMachine = styleName == SubStateMachineStyleName;
                var nodeColor          = ResolveColor(styleName, color, settings, resolvedBlendType);
                var texKey             = (nodeColor, isSubStateMachine, on, settings.nodeColor3DEnabled, settings.nodeSelectionColor);

                if (!_texCache.TryGetValue(texKey, out var nodeTexture))
                {
                    var baseNormal        = isSubStateMachine ? _baseSubSM       : _baseNode;
                    var baseActiveTexture = isSubStateMachine ? _baseSubSMActive : _baseNodeActive;
                    nodeTexture = settings.nodeColor3DEnabled
                        ? (on ? GradientCompositeClone(baseNormal, nodeColor, baseActiveTexture, settings.nodeSelectionColor) : GradientTintClone(baseNormal, nodeColor))
                        : (on ? CompositeClone(baseNormal, nodeColor, baseActiveTexture, settings.nodeSelectionColor)         : TintClone(baseNormal, nodeColor));
                    _texCache[texKey] = nodeTexture;
                }

                if (nodeTexture == null) return;

                var tinted = new GUIStyle(__result);
                tinted.normal.background        = nodeTexture;
                tinted.normal.scaledBackgrounds = null;
                _styleCache[styleKey] = tinted;
                __result = tinted;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] Node style error: {e}"); }
        }

        /* Maps the style name and Unity color index to the corresponding user-configured node color from settings. */
        static Color ResolveColor(string styleName, int colorIndex, AnimatorDefaultSettings settings, BlendTreeType? blendType = null)
        {
            if (styleName == SubStateMachineStyleName) return settings.subStateMachineColor;
            if (blendType.HasValue)
            {
                return blendType.Value switch
                {
                    BlendTreeType.Simple1D => settings.blendTree1DNodeColor,
                    BlendTreeType.Direct   => settings.blendTreeDirectNodeColor,
                    _                      => settings.blendTree2DNodeColor,
                };
            }
            return colorIndex switch
            {
                5 => settings.defaultStateColor,
                3 => settings.entryNodeColor,
                6 => settings.exitNodeColor,
                2 => settings.anyStateNodeColor,
                _ => settings.stateNodeColor
            };
        }

        /* Returns true if any node color or the 3D flag differs from the cached snapshot, and updates the cache. Used to decide when to rebuild the texture cache. */
        static bool ColorsChanged(AnimatorDefaultSettings settings)
        {
            var current = (settings.stateNodeColor, settings.defaultStateColor, settings.subStateMachineColor,
                           settings.entryNodeColor, settings.exitNodeColor, settings.anyStateNodeColor, settings.nodeSelectionColor,
                           settings.blendTree1DNodeColor, settings.blendTree2DNodeColor, settings.blendTreeDirectNodeColor);
            if (_cachedColors == current && _cached3DEnabled == settings.nodeColor3DEnabled) return false;
            _cachedColors    = current;
            _cached3DEnabled = settings.nodeColor3DEnabled;
            return true;
        }

        internal static void HandleTextures() { Rebuild(); EnsureBaseTextures(); }
        internal static bool HasTextures()    => _baseNode != null;
        internal static void Invalidate()     => Rebuild();

        static void EnsureBaseTextures()
        {
            if (_baseNode != null) return;
            _baseNode        = LoadPNG("NodeBackground");
            _baseNodeActive  = LoadPNG("NodeBackgroundActive");
            _baseSubSM       = LoadPNG("NodeBackground_StateMachine");
            _baseSubSMActive = LoadPNG("NodeBackground_StateMachineActive");
        }

        static void Rebuild()
        {
            foreach (var tex in _texCache.Values)
                UnityEngine.Object.DestroyImmediate(tex);
            _texCache.Clear();
            _styleCache.Clear();
        }

        /* Returns a new RGBA32 texture with every pixel of sourceTexture multiplied by tint. The result is not readable (Apply(false, false)) and is flagged HideAndDontSave. */
        static Texture2D TintClone(Texture2D sourceTexture, Color tint)
        {
            if (sourceTexture == null) return null;
            var resultTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            var pixels = sourceTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] *= tint;
            resultTexture.SetPixels(pixels);
            resultTexture.Apply(false, false);
            resultTexture.hideFlags = HideFlags.HideAndDontSave;
            return resultTexture;
        }

        /* Tints baseTexture by tint then alpha-composites overlay (tinted by selectionColor) on top, producing a selection-highlight texture over a tinted node. */
        static Texture2D CompositeClone(Texture2D baseTexture, Color tint, Texture2D overlay, Color selectionColor)
        {
            if (baseTexture == null) return null;
            var resultTexture = new Texture2D(baseTexture.width, baseTexture.height, TextureFormat.RGBA32, false);
            var pixels = baseTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] *= tint;

            if (overlay != null && overlay.width == baseTexture.width && overlay.height == baseTexture.height)
            {
                var overlayPixels = overlay.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var tintedOverlay = overlayPixels[i] * selectionColor;
                    float a = tintedOverlay.a;
                    pixels[i] = pixels[i] * (1f - a) + tintedOverlay * a;
                }
            }

            resultTexture.SetPixels(pixels);
            resultTexture.Apply(false, false);
            resultTexture.hideFlags = HideFlags.HideAndDontSave;
            return resultTexture;
        }

        /* Returns a new texture with tint applied and a vertical brightness gradient plus per-edge rim highlights baked in, giving nodes a 3D lit appearance. */
        static Texture2D GradientTintClone(Texture2D sourceTexture, Color tint)
        {
            if (sourceTexture == null) return null;
            int width = sourceTexture.width, height = sourceTexture.height;
            var resultTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // ── Gradient tuning ──────────────────────────────────────────
            const float GradientTopBrightness    = 1.0f;  // multiplier at top of node
            const float GradientBottomBrightness = 0.75f; // multiplier at bottom of node
            const float TopRimWidth              = 3f;   // pixels tall for top highlight
            const float LeftRimWidth             = 4f;    // pixels wide — narrower because texture bevel is wider on left
            const float RightRimWidth            = 6f;   // pixels wide for right highlight
            const float SideRimFadeHeight        = 0.55f; // fraction of height over which side rim fades (0=top, 1=bottom)
            const float TopRimStrength           = 0.65f; // top edge highlight strength (0–1)
            const float LeftRimStrength          = 1.0f;  // left edge strength
            const float RightRimStrength         = 1.0f;  // right edge strength
            const float RimHighlightBrightness   = 0.85f; // target brightness of highlight (0=black, 1=white)
            // ─────────────────────────────────────────────────────────────

            // Separate source/output so neighbor reads are unaffected by writes
            var sourcePixels = sourceTexture.GetPixels();
            var outputPixels = new Color[sourcePixels.Length];
            int topRimW   = Mathf.Max(1, (int)TopRimWidth);
            int leftRimW  = Mathf.Max(1, (int)LeftRimWidth);
            int rightRimW = Mathf.Max(1, (int)RightRimWidth);
            int maxRimW   = Mathf.Max(topRimW, Mathf.Max(leftRimW, rightRimW));

            // Unity GetPixels: row 0 = bottom, row height-1 = top
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                var sourcePixel = sourcePixels[i];
                if (sourcePixel.a < 0.02f) { outputPixels[i] = Color.clear; continue; }

                int x = i % width;
                int y = i / width;

                // t = 0 at top, 1 at bottom
                float t          = height > 1 ? 1f - (float)y / (height - 1) : 0f;
                float tSmooth    = t * t * (3f - 2f * t);
                float brightness = Mathf.Lerp(GradientTopBrightness, GradientBottomBrightness, tSmooth);

                var resultColor = new Color(
                    Mathf.Clamp01(tint.r * sourcePixel.r * brightness),
                    Mathf.Clamp01(tint.g * sourcePixel.g * brightness),
                    Mathf.Clamp01(tint.b * sourcePixel.b * brightness),
                    sourcePixel.a * tint.a
                );

                // Scan for nearest transparent pixel in each direction — works for any shape
                int distUp = topRimW + 1, distLeft = leftRimW + 1, distRight = rightRimW + 1;
                for (int d = 1; d <= maxRimW; d++)
                {
                    if (d <= topRimW   && distUp    > topRimW)   { int ny = y + d; if (ny >= height || sourcePixels[ny * width + x].a < 0.02f)  distUp    = d; }
                    if (d <= leftRimW  && distLeft  > leftRimW)  { int nx = x - d; if (nx < 0       || sourcePixels[y  * width + nx].a < 0.02f) distLeft  = d; }
                    if (d <= rightRimW && distRight > rightRimW) { int nx = x + d; if (nx >= width  || sourcePixels[y  * width + nx].a < 0.02f) distRight = d; }
                }

                float topRim   = Mathf.Clamp01(1f - (float)(distUp    - 1) / TopRimWidth);
                float sideFade = Mathf.Clamp01(1f - t / SideRimFadeHeight);
                float leftRim  = Mathf.Clamp01(1f - (float)(distLeft  - 1) / LeftRimWidth)  * sideFade;
                float rightRim = Mathf.Clamp01(1f - (float)(distRight - 1) / RightRimWidth) * sideFade;

                // Three separate lerp passes — independent strengths prevent baked-in
                // texture asymmetry from amplifying into uneven left/right highlights.
                float topAlpha   = topRim   * TopRimStrength   * sourcePixel.a;
                float leftAlpha  = leftRim  * LeftRimStrength  * sourcePixel.a;
                float rightAlpha = rightRim * RightRimStrength * sourcePixel.a;

                if (topAlpha > 0f)
                {
                    resultColor.r = Mathf.Lerp(resultColor.r, RimHighlightBrightness, topAlpha);
                    resultColor.g = Mathf.Lerp(resultColor.g, RimHighlightBrightness, topAlpha);
                    resultColor.b = Mathf.Lerp(resultColor.b, RimHighlightBrightness, topAlpha);
                }
                if (leftAlpha > 0f)
                {
                    resultColor.r = Mathf.Lerp(resultColor.r, RimHighlightBrightness, leftAlpha);
                    resultColor.g = Mathf.Lerp(resultColor.g, RimHighlightBrightness, leftAlpha);
                    resultColor.b = Mathf.Lerp(resultColor.b, RimHighlightBrightness, leftAlpha);
                }
                if (rightAlpha > 0f)
                {
                    resultColor.r = Mathf.Lerp(resultColor.r, RimHighlightBrightness, rightAlpha);
                    resultColor.g = Mathf.Lerp(resultColor.g, RimHighlightBrightness, rightAlpha);
                    resultColor.b = Mathf.Lerp(resultColor.b, RimHighlightBrightness, rightAlpha);
                }

                outputPixels[i] = resultColor;
            }

            resultTexture.SetPixels(outputPixels);
            resultTexture.Apply(false, false);
            resultTexture.hideFlags = HideFlags.HideAndDontSave;
            return resultTexture;
        }

        /* Applies GradientTintClone to baseTexture then alpha-composites overlay (tinted by selectionColor) on top for the selected/active node state. */
        static Texture2D GradientCompositeClone(Texture2D baseTexture, Color tint, Texture2D overlay, Color selectionColor)
        {
            var gradientTexture = GradientTintClone(baseTexture, tint);
            if (gradientTexture == null) return null;

            if (overlay != null && overlay.width == baseTexture.width && overlay.height == baseTexture.height)
            {
                var pixels        = gradientTexture.GetPixels();
                var overlayPixels = overlay.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var tintedOverlay = overlayPixels[i] * selectionColor;
                    float a  = tintedOverlay.a;
                    pixels[i] = pixels[i] * (1f - a) + tintedOverlay * a;
                }
                gradientTexture.SetPixels(pixels);
                gradientTexture.Apply(false, false);
            }

            return gradientTexture;
        }

        /* Finds a Texture2D asset named exactly name inside the package's Editor/Resources folder and loads it from disk into a new uncompressed texture. */
        static Texture2D LoadPNG(string name)
        {
            var guids = AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { "Packages/com.ygdr.animator/Editor/Resources" });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(assetPath) != name) continue;
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath).Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(fullPath)) return null;
                var loadedTexture = new Texture2D(2, 2);
                loadedTexture.LoadImage(File.ReadAllBytes(fullPath));
                loadedTexture.hideFlags = HideFlags.HideAndDontSave;
                return loadedTexture;
            }
            return null;
        }
    }
}
#endif
