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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class BlendTreeReparentState
    {
        internal static object DragCandidate;
        internal static object DraggingNode;
        internal static bool IsDragging;

        internal static void Clear()
        {
            DragCandidate = null;
            DraggingNode = null;
            IsDragging = false;
            if (PatchGraphInputHandler.AnimWindow != null)
                PatchGraphInputHandler.AnimWindow.wantsMouseMove = false;
        }
    }

    internal static class BlendTreeCopyPasteState
    {
        internal static Motion SourceMotion;
        internal static object PendingContextNode;

        internal static void ClearPendingContext() => PendingContextNode = null;
    }

    // Caches BlendTree.recursiveBlendParameterCount / GetRecursiveBlendParameter[Min/Max], which native
    // NodeGUI otherwise recomputes (full descendant subtree walk) on every call, every node, every repaint.
    // Wired in via PatchBlendTreeNodeGUICache's transpiler below.
    internal static class BlendTreeRecursiveParamCache
    {
        internal readonly struct Entry
        {
            internal readonly string Name;
            internal readonly float Min;
            internal readonly float Max;
            internal Entry(string name, float min, float max) { Name = name; Min = min; Max = max; }
        }

        static readonly Dictionary<BlendTree, Entry[]> _cache = new();

        static BlendTreeRecursiveParamCache() =>
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => Invalidate();

        internal static Entry[] Get(BlendTree blendTree)
        {
            if (blendTree == null) return System.Array.Empty<Entry>();
            if (_cache.TryGetValue(blendTree, out var cached)) return cached;

            int count = (int)(BlendTreePatchReflection.RecursiveBlendParameterCountGetter?.Invoke(blendTree, null) ?? 0);
            var entries = new Entry[count];
            for (int i = 0; i < count; i++)
            {
                var indexArg = new object[] { i };
                entries[i] = new Entry(
                    BlendTreePatchReflection.GetRecursiveBlendParameterMethod?.Invoke(blendTree, indexArg) as string,
                    (float)(BlendTreePatchReflection.GetRecursiveBlendParameterMinMethod?.Invoke(blendTree, indexArg) ?? 0f),
                    (float)(BlendTreePatchReflection.GetRecursiveBlendParameterMaxMethod?.Invoke(blendTree, indexArg) ?? 0f));
            }
            _cache[blendTree] = entries;
            return entries;
        }

        internal static void Invalidate() => _cache.Clear();
    }

    // NodeGUI fires per-node before HandleNodeInput (which calls Event.Use()).
    // Prefix captures MouseDown; postfix draws custom name label and rename field.
    // InNodeGUI gates GetNodeStyle color patch to blend tree context only.
    [HarmonyPatch]
    internal static class PatchBlendTreeNodeGUI
    {
        internal static bool InNodeGUI { get; private set; }
        internal static BlendTreeType? CurrentBlendType { get; private set; }
        internal static object SelectedNode;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var graphGUIType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            if (graphGUIType == null) return null;
            var method = AccessTools.Method(graphGUIType, "NodeGUI");
            return method;
        }

        static GUIStyle _nameLabelStyle;
        static Color _nameLabelColor;

        /* Returns a centered label style for the node title, rebuilding the cached instance only when color changes. */
        internal static GUIStyle GetNameLabelStyle(Color color)
        {
            if (_nameLabelStyle != null && _nameLabelColor == color) return _nameLabelStyle;
            _nameLabelColor = color;
            _nameLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = color },
                focused   = { textColor = color },
                hover     = { textColor = color },
            };
            return _nameLabelStyle;
        }

        static GUIStyle _blendTypeLabelStyle;
        static Color _blendTypeLabelColor;

        /* Returns a small bold label style for the blend type badge, rebuilding the cached instance only when color changes. */
        internal static GUIStyle GetBlendTypeLabelStyle(Color color)
        {
            if (_blendTypeLabelStyle != null && _blendTypeLabelColor == color) return _blendTypeLabelStyle;
            _blendTypeLabelColor = color;
            _blendTypeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = color },
            };
            return _blendTypeLabelStyle;
        }

        static GUIStyle _thresholdLabelStyle;
        static Color _thresholdLabelColor;

        internal static GUIStyle GetThresholdLabelStyle(Color color)
        {
            if (_thresholdLabelStyle != null && _thresholdLabelColor == color) return _thresholdLabelStyle;
            _thresholdLabelColor = color;
            _thresholdLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = color },
            };
            return _thresholdLabelStyle;
        }

        /* Returns a short display string for a blend tree type (e.g. "1D", "2D Simple", "Direct"). */
        internal static string BlendTypeLabel(BlendTreeType blendType) => blendType switch
        {
            BlendTreeType.Simple1D              => "1D",
            BlendTreeType.SimpleDirectional2D   => "2D Simple",
            BlendTreeType.FreeformDirectional2D => "Free Dir",
            BlendTreeType.FreeformCartesian2D   => "Free Cart",
            BlendTreeType.Direct                => "Direct",
            _                                   => blendType.ToString()
        };

        static bool _renameFieldHadFocus;

        [HarmonyPrefix]
        static void Prefix(object n)
        {
            InNodeGUI = true;
            var prefixMotion = new BlendTreePatchReflection.BlendTreeNodeProxy(n).Motion;
            CurrentBlendType = prefixMotion is BlendTree prefixBlendTree ? prefixBlendTree.blendType : (BlendTreeType?)null;
            try
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    SelectedNode = n;
                    if (new BlendTreePatchReflection.BlendTreeNodeProxy(n).Parent != null)
                        BlendTreeReparentState.DragCandidate = n;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] BlendTree NodeGUI prefix error: {e}");
            }
        }

        [HarmonyPostfix]
        static void Postfix(object n)
        {
            InNodeGUI = false;
            CurrentBlendType = null;
            try
            {
                var motion = new BlendTreePatchReflection.BlendTreeNodeProxy(n).Motion;
                if (motion == null) return;

                // NodeGUI runs inside GUILayout.Window — local coords, title bar is at y < 0.
                // GetLastRect gives the last slot rect; use its x/width, fix y to title bar.
                var lastRect  = GUILayoutUtility.GetLastRect();
                var titleRect = new Rect(lastRect.x, 5f, lastRect.width, 18f);

                bool isRenaming  = BlendTreeRenameState.RenameTargetNode == n;
                var currentEvent = Event.current;

                if (currentEvent.type != EventType.Repaint)
                {
                    if (isRenaming) DrawRenameField(motion, titleRect);
                    return;
                }

                if (!isRenaming)
                {
                    var settings = AnimatorDefaultSettings.Load();
                    GUI.Label(titleRect, motion.name, GetNameLabelStyle(settings.overlayActiveColor));
                    if (settings.overlayEnabled && motion is BlendTree blendTree)
                        GUI.Label(new Rect(lastRect.x + 2f, 3f, 70f, 11f), BlendTypeLabel(blendTree.blendType), GetBlendTypeLabelStyle(settings.overlayActiveColor));
                    if (settings.overlayEnabled)
                        TryDrawThresholdLabel(n, lastRect, settings.overlayActiveColor);
                }
                else
                {
                    DrawRenameField(motion, titleRect);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] BlendTree NodeGUI postfix error: {e}");
            }
        }

        /* Draws the threshold value in the upper-right corner for nodes that are direct children of a 1D blend tree. */
        static void TryDrawThresholdLabel(object node, Rect lastRect, Color color)
        {
            var proxy = new BlendTreePatchReflection.BlendTreeNodeProxy(node);
            var parentNode = proxy.Parent;
            if (parentNode == null) return;
            var parentBlendTree = new BlendTreePatchReflection.BlendTreeNodeProxy(parentNode).Motion as BlendTree;
            if (parentBlendTree == null || parentBlendTree.blendType != BlendTreeType.Simple1D) return;
            int childIndex = proxy.ChildIndex;
            if (childIndex < 0 || childIndex >= parentBlendTree.children.Length) return;
            float threshold = parentBlendTree.children[childIndex].threshold;
            var thresholdRect = new Rect(lastRect.xMax - 40f, 3f, 38f, 11f);
            GUI.Label(thresholdRect, threshold.ToString("0.###"), GetThresholdLabelStyle(color));
        }

        /* Draws an inline TextField over the node title for rename input, committing on Enter and cancelling on Escape. */
        static void DrawRenameField(Motion motion, Rect titleRect)
        {
            const string controlName = "BlendTreeRenameField";
            var currentEvent = Event.current;

            if (BlendTreeRenameState.JustStarted)
            {
                GUI.SetNextControlName(controlName);
                EditorGUI.TextField(titleRect, BlendTreeRenameState.RenameText, AnimatorStyles.RenameFieldStyle);
                EditorGUI.FocusTextInControl(controlName);
                BlendTreeRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    EditorApplication.delayCall += BlendTreeRenameState.Apply;
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    BlendTreeRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            BlendTreeRenameState.RenameText = EditorGUI.TextField(titleRect, BlendTreeRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                EditorApplication.delayCall += BlendTreeRenameState.Apply;
            _renameFieldHadFocus = hasFocus;
        }
    }

    // Transpiler on NodeGUI: redirects the 4 native recursive-blend-parameter calls to
    // BlendTreeRecursiveParamCache so they run once per structural change instead of every node every repaint.
    // No other IL is touched — slot layout, slider draw, and animator preview stay byte-identical to native.
    [HarmonyPatch]
    internal static class PatchBlendTreeNodeGUICache
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var graphGUIType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            if (graphGUIType == null) return null;
            return AccessTools.Method(graphGUIType, "NodeGUI");
        }

        internal static int CachedRecursiveBlendParameterCount(BlendTree blendTree) =>
            BlendTreeRecursiveParamCache.Get(blendTree).Length;

        internal static string CachedGetRecursiveBlendParameter(BlendTree blendTree, int index) =>
            BlendTreeRecursiveParamCache.Get(blendTree)[index].Name;

        internal static float CachedGetRecursiveBlendParameterMin(BlendTree blendTree, int index) =>
            BlendTreeRecursiveParamCache.Get(blendTree)[index].Min;

        internal static float CachedGetRecursiveBlendParameterMax(BlendTree blendTree, int index) =>
            BlendTreeRecursiveParamCache.Get(blendTree)[index].Max;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(BlendTreePatchReflection.RecursiveBlendParameterCountGetter))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(CachedRecursiveBlendParameterCount)));
                else if (instruction.Calls(BlendTreePatchReflection.GetRecursiveBlendParameterMethod))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(CachedGetRecursiveBlendParameter)));
                else if (instruction.Calls(BlendTreePatchReflection.GetRecursiveBlendParameterMinMethod))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(CachedGetRecursiveBlendParameterMin)));
                else if (instruction.Calls(BlendTreePatchReflection.GetRecursiveBlendParameterMaxMethod))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(CachedGetRecursiveBlendParameterMax)));
                else
                    yield return instruction;
            }
        }
    }

    // Same fix as PatchBlendTreeNodeGUICache, different call site: Graph.PopulateParameterValues walks
    // m_RootBlendTree's entire recursive parameter set (same 2 uncached native calls) once per OnGraphGUI
    // pass — confirmed via profiler as the single largest per-call cost (~56ms/call) once VRCFury's
    // uncached Animator.SetFloat hook was ruled out. Reuses the same cache and wrapper methods.
    [HarmonyPatch]
    internal static class PatchGraphPopulateParameterValuesCache
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => BlendTreePatchReflection.GraphPopulateParameterValuesMethod;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(BlendTreePatchReflection.RecursiveBlendParameterCountGetter))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(PatchBlendTreeNodeGUICache.CachedRecursiveBlendParameterCount)));
                else if (instruction.Calls(BlendTreePatchReflection.GetRecursiveBlendParameterMethod))
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PatchBlendTreeNodeGUICache), nameof(PatchBlendTreeNodeGUICache.CachedGetRecursiveBlendParameter)));
                else
                    yield return instruction;
            }
        }
    }

    // Graph.SetParameterValue triggers SetParameterValueRecursive, which walks EVERY node in the entire
    // tree pushing this one parameter's value down natively — called once per recursive parameter, every
    // PopulateParameterValues call (2x/repaint), regardless of whether the value actually changed.
    // Confirmed via profiler: dominant sustained per-repaint cost once cache thrashing was ruled out.
    // Dedupe here: skip the whole recursive walk when the value matches what we last pushed.
    [HarmonyPatch]
    internal static class PatchGraphSetParameterValueDedup
    {
        const float Epsilon = 0.0001f;
        static readonly Dictionary<object, Dictionary<string, float>> _lastValues = new();

        static PatchGraphSetParameterValueDedup() =>
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => _lastValues.Clear();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => BlendTreePatchReflection.GraphSetParameterValueMethod;

        [HarmonyPrefix]
        static bool Prefix(object __instance, string parameterName, float parameterValue)
        {
            if (!_lastValues.TryGetValue(__instance, out var perParam))
                _lastValues[__instance] = perParam = new Dictionary<string, float>();

            if (perParam.TryGetValue(parameterName, out var last) && Mathf.Abs(last - parameterValue) < Epsilon)
                return false;

            perParam[parameterName] = parameterValue;
            return true;
        }
    }

    // Native GetParameterValue logs "parameter name does not exist." and returns 0f for a key not (yet)
    // in m_ParameterValues — reproduces even through the fully native, unpatched OnGraphGUI path on trees
    // with Direct-type children, so it's a native structural quirk, not something our patches cause or
    // can otherwise fix. Mirror its own fallback (return 0f) without the log call.
    [HarmonyPatch]
    internal static class PatchGraphGetParameterValueSilentMissing
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => BlendTreePatchReflection.GraphGetParameterValueMethod;

        [HarmonyPrefix]
        static bool Prefix(object __instance, string parameterName, ref float __result)
        {
            if (BlendTreePatchReflection.GraphParameterValuesRef != null)
            {
                var values = BlendTreePatchReflection.GraphParameterValuesRef(__instance);
                if (values != null && !values.ContainsKey(parameterName))
                {
                    __result = 0f;
                    return false;
                }
            }
            return true;
        }
    }

    // Throttles UpdateAnimator (full native Animator.EvaluateController per non-leaf node) — native NodeGUI
    // runs it unconditionally every repaint for live edge-weight-color preview. Confirmed via diagnostic
    // counter: ~500 calls/repaint on a large tree, dominant native GC.Alloc source under CallWindowDelegate.
    // Preview coloring only needs to be visually smooth, not per-frame-exact — throttled to 15Hz per node.
    [HarmonyPatch]
    internal static class PatchBlendTreeUpdateAnimatorThrottle
    {
        const double ThrottleSeconds = 1.0 / 15.0;
        static readonly Dictionary<object, double> _lastEvalTime = new();

        static PatchBlendTreeUpdateAnimatorThrottle() =>
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => _lastEvalTime.Clear();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var nodeType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.Node");
            if (nodeType == null) return null;
            return AccessTools.Method(nodeType, "UpdateAnimator");
        }

        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            double now = EditorApplication.timeSinceStartup;
            if (_lastEvalTime.TryGetValue(__instance, out var last) && now - last < ThrottleSeconds)
                return false;
            _lastEvalTime[__instance] = now;
            return true;
        }
    }

    // OnGraphGUI prefix+postfix: drives drag, drop, and sets blend tree GUI context flag for GetNodeStyle.
    [HarmonyPatch]
    internal static class PatchBlendTreeOnGraphGUI
    {
        internal static bool InBlendTreeGUI { get; private set; }
        internal static object CurrentGraphGUI { get; private set; }
        internal static readonly Queue<BlendTreeType?> _blendTypeQueue = new Queue<BlendTreeType?>();

        // Explicit per-call override for the lightweight idle/active draw path: GetNodeStyle's FIFO
        // queue dequeue only lines up with the calling node when native OnGraphGUI's own node loop calls
        // it once per node in order — TryDrawLightweightGraph doesn't (idle nodes share one resolved
        // style, and the active node's window-chrome style is resolved before NodeGUI sets
        // InNodeGUI/CurrentBlendType), so both cases silently pick up the wrong node's queued type. Set
        // this immediately before invoking GetNodeStyleMethod so PatchNodeStyles.Postfix uses the correct
        // type for THAT specific call instead of guessing from queue order.
        internal static bool LightweightOverrideActive;
        internal static BlendTreeType? LightweightOverrideBlendType;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var graphGUIType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            if (graphGUIType == null) return null;
            var method = AccessTools.Method(graphGUIType, "OnGraphGUI");
            return method;
        }

        static FieldInfo _varPinInField;
        static FieldInfo _varPinOutField;
        static Color[] _savedVarPinInColors;
        static Color[] _savedVarPinOutColors;
        static Color[] _savedEditorLabelColors;

        /* Lazily resolves and caches a static GUIStyle field from UnityEditor.Graphs.Styles by name. */
        static GUIStyle ResolveStyleField(ref FieldInfo cache, string fieldName)
        {
            if (cache != null) return cache.GetValue(null) as GUIStyle;
            var stylesType = AccessTools.TypeByName("UnityEditor.Graphs.Styles");
            if (stylesType == null) return null;
            cache = AccessTools.Field(stylesType, fieldName);
            return cache?.GetValue(null) as GUIStyle;
        }

        /* Replaces text color in all 8 GUIStyleState slots of style with color, returning the originals for later restore. */
        internal static Color[] OverrideSlotTextColors(GUIStyle style, Color color)
        {
            if (style == null) return null;
            var saved = new Color[8];
            ApplyState(style.normal,    color, ref saved[0], out var s0); style.normal    = s0;
            ApplyState(style.onNormal,  color, ref saved[1], out var s1); style.onNormal  = s1;
            ApplyState(style.hover,     color, ref saved[2], out var s2); style.hover     = s2;
            ApplyState(style.onHover,   color, ref saved[3], out var s3); style.onHover   = s3;
            ApplyState(style.active,    color, ref saved[4], out var s4); style.active    = s4;
            ApplyState(style.onActive,  color, ref saved[5], out var s5); style.onActive  = s5;
            ApplyState(style.focused,   color, ref saved[6], out var s6); style.focused   = s6;
            ApplyState(style.onFocused, color, ref saved[7], out var s7); style.onFocused = s7;
            return saved;
        }

        /* Overwrites a single GUIStyleState's textColor with color and saves the original into savedColor. */
        static void ApplyState(GUIStyleState state, Color color, ref Color savedColor, out GUIStyleState result)
        {
            savedColor = state.textColor;
            state.textColor = color;
            result = state;
        }

        /* Restores all 8 GUIStyleState text colors on style from the array returned by OverrideSlotTextColors. */
        static void RestoreSlotTextColors(GUIStyle style, Color[] saved)
        {
            if (style == null || saved == null) return;
            RestoreState(style.normal,    saved[0], out var s0); style.normal    = s0;
            RestoreState(style.onNormal,  saved[1], out var s1); style.onNormal  = s1;
            RestoreState(style.hover,     saved[2], out var s2); style.hover     = s2;
            RestoreState(style.onHover,   saved[3], out var s3); style.onHover   = s3;
            RestoreState(style.active,    saved[4], out var s4); style.active    = s4;
            RestoreState(style.onActive,  saved[5], out var s5); style.onActive  = s5;
            RestoreState(style.focused,   saved[6], out var s6); style.focused   = s6;
            RestoreState(style.onFocused, saved[7], out var s7); style.onFocused = s7;
        }

        /* Restores a single GUIStyleState's textColor from a previously saved value. */
        static void RestoreState(GUIStyleState state, Color savedColor, out GUIStyleState result)
        {
            state.textColor = savedColor;
            result = state;
        }

        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            try
            {
            InBlendTreeGUI = true;
            CurrentGraphGUI = __instance;
            _blendTypeQueue.Clear();
            if (Event.current.type == EventType.Repaint)
            {
                var blendTreeGraph = Traverse.Create(__instance).Property("graph").GetValue();
                if (blendTreeGraph != null)
                {
                    var graphNodes = PatchGraphInputHandler.GetNodes(blendTreeGraph);
                    if (graphNodes != null)
                    {
                        foreach (var node in graphNodes)
                        {
                            var nodeMotion = Traverse.Create(node).Field("motion").GetValue() as Motion;
                            _blendTypeQueue.Enqueue(nodeMotion is BlendTree nodeBT ? nodeBT.blendType : (BlendTreeType?)null);
                        }
                    }
                }
            }

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F2)
            {
                var selectedNode = PatchBlendTreeNodeGUI.SelectedNode;
                if (selectedNode != null)
                {
                    var motion = Traverse.Create(selectedNode).Field("motion").GetValue() as Motion;
                    if (motion is BlendTree blendTreeMotion)
                    {
                        BlendTreeRenameState.Begin(blendTreeMotion, selectedNode);
                        currentEvent.Use();
                    }
                }
            }

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F3)
            {
                var selectedNode = PatchBlendTreeNodeGUI.SelectedNode;
                if (selectedNode != null)
                {
                    var motion = Traverse.Create(selectedNode).Field("motion").GetValue() as Motion;
                    if (motion is AnimationClip animationClip)
                    {
                        BlendTreeRenameState.Begin(animationClip, selectedNode);
                        currentEvent.Use();
                    }
                }
            }

            if (currentEvent.type == EventType.KeyDown && currentEvent.control)
            {
                var selectedNode = PatchBlendTreeNodeGUI.SelectedNode;
                if (selectedNode != null)
                {
                    if (currentEvent.keyCode == KeyCode.C)
                    {
                        ExecuteCopyNode(selectedNode);
                        currentEvent.Use();
                    }
                    else if (currentEvent.keyCode == KeyCode.V && BlendTreeCopyPasteState.SourceMotion != null)
                    {
                        var targetMotion = Traverse.Create(selectedNode).Field("motion").GetValue() as Motion;
                        if (targetMotion is BlendTree)
                        {
                            ExecutePasteToNode(__instance, selectedNode);
                            currentEvent.Use();
                        }
                    }
                }
            }

            var settings = AnimatorDefaultSettings.Load();
            if (settings.nodeColorEnabled)
            {
                var color = settings.overlayActiveColor;
                _savedVarPinInColors    = OverrideSlotTextColors(ResolveStyleField(ref _varPinInField,  "varPinIn"),  color);
                _savedVarPinOutColors   = OverrideSlotTextColors(ResolveStyleField(ref _varPinOutField, "varPinOut"), color);
                _savedEditorLabelColors = OverrideSlotTextColors(EditorStyles.label, color);
            }
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchBlendTreeOnGraphGUI.Prefix: {e}"); return true; }

            bool handled;
            try { handled = TryDrawLightweightGraph(__instance); }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchBlendTreeOnGraphGUI lightweight draw error: {e}"); handled = false; }
            return !handled;
        }

        const int LightweightNodeThreshold = 100;

        // Reimplements base GraphGUI.OnGraphGUI's node loop (decompiled reference: foreach node,
        // GUILayout.Window(id, position, NodeGUI, title, style)). GUILayout.Window carries fixed
        // per-call overhead (ID hashing, event routing, drag/focus state) that dominates cost on
        // large trees regardless of zoom — confirmed via profiler, unaffected by caching/throttling.
        // Only the active node (selected/dragging/hovered) gets a real window; everyone else gets
        // a cheap static box. Gated to trees above LightweightNodeThreshold; small trees are
        // unaffected. Any missing reflection member safely falls back to running native original.
        static bool TryDrawLightweightGraph(object graphGUI)
        {
            if (BlendTreePatchReflection.GraphGUIGraphGetter == null || BlendTreePatchReflection.NodeGUIMethod == null) return false;
            if (BlendTreePatchReflection.HostGetter == null || BlendTreePatchReflection.HostBeginWindowsMethod == null
                || BlendTreePatchReflection.HostEndWindowsMethod == null)
                return false;
            if (BlendTreePatchReflection.EdgeGUIGetter == null || BlendTreePatchReflection.EdgeGUIDoEdgesMethod == null
                || BlendTreePatchReflection.DragSelectionMethod == null || BlendTreePatchReflection.ShowContextMenuMethod == null
                || BlendTreePatchReflection.HandleMenuEventsMethod == null)
                return false;

            var graph = BlendTreePatchReflection.GraphGUIGraphGetter.Invoke(graphGUI, null);
            if (graph == null) return false;

            // Matches native OnGraphGUI's own first line — refreshes m_ParameterValues from
            // m_RootBlendTree's recursive set. We fully replace OnGraphGUI below, so without this
            // the dict is only ever populated once (at BuildFromBlendTree/breadcrumb-nav time).
            try
            {
                BlendTreePatchReflection.GraphPopulateParameterValuesMethod?.Invoke(graph, null);
            }
            catch (TargetInvocationException) { /* mirrors native: logs internally, doesn't throw through */ }

            var rawNodes = PatchGraphInputHandler.GetNodes(graph);
            if (rawNodes == null) return false;

            var nodes = new List<object>();
            foreach (var node in rawNodes) nodes.Add(node);
            if (nodes.Count < LightweightNodeThreshold) return false;

            var host = BlendTreePatchReflection.HostGetter(graphGUI);
            if (host == null) return false;

            var mousePos = Event.current.mousePosition;
            var settings = AnimatorDefaultSettings.Load();
            var idleStyleCache = new Dictionary<int, GUIStyle>();
            var paramValues = BuildParamValueCache(graph, nodes);

            BlendTreePatchReflection.HostBeginWindowsMethod.Invoke(host, null);
            foreach (var node in nodes)
            {
                var proxy = new BlendTreePatchReflection.BlendTreeNodeProxy(node);
                var rect = proxy.Position;
                bool isActive = ReferenceEquals(node, PatchBlendTreeNodeGUI.SelectedNode)
                    || ReferenceEquals(node, BlendTreeReparentState.DraggingNode)
                    || rect.Contains(mousePos);

                if (isActive)
                    proxy.Position = DrawActiveNodeWindow(graphGUI, node, rect);
                else
                    DrawLightweightNodeBox(node, proxy, rect, settings, mousePos, GetIdleNodeStyle(node, idleStyleCache), paramValues);
            }
            BlendTreePatchReflection.HostEndWindowsMethod.Invoke(host, null);

            var edgeGUI = BlendTreePatchReflection.EdgeGUIGetter(graphGUI);
            if (edgeGUI != null)
            {
                BlendTreePatchReflection.EdgeGUIDoEdgesMethod.Invoke(edgeGUI, null);
                BlendTreePatchReflection.EdgeGUIDoDraggedEdgeMethod?.Invoke(edgeGUI, null);
            }
            // Native DragSelection syncs Selection.activeObject to the owning (sub)BlendTree asset
            // whenever it sees an empty selection — pings it on every single background click since
            // our SelectedNode field bypasses graph.selection. Only forward double clicks (breadcrumb
            // back-navigation); handle single clicks (deselect) ourselves.
            var backgroundEvent = Event.current;
            if (backgroundEvent.type == EventType.MouseDown && backgroundEvent.button == 0)
            {
                if (backgroundEvent.clickCount >= 2)
                {
                    BlendTreePatchReflection.DragSelectionMethod.Invoke(graphGUI, null);
                }
                else
                {
                    PatchBlendTreeNodeGUI.SelectedNode = null;
                    backgroundEvent.Use();
                }
            }
            BlendTreePatchReflection.ShowContextMenuMethod.Invoke(graphGUI, null);
            BlendTreePatchReflection.HandleMenuEventsMethod.Invoke(graphGUI, null);
            return true;
        }

        /* Builds paramName -> currentValue once per call (values are graph-wide, not per-node) so idle
           slider rows don't re-fetch the same value once per node — O(distinct params), not O(nodes). */
        static Dictionary<string, float> BuildParamValueCache(object graph, List<object> nodes)
        {
            var cache = new Dictionary<string, float>();
            if (BlendTreePatchReflection.GraphParameterValuesRef == null) return cache;
            var rawValues = BlendTreePatchReflection.GraphParameterValuesRef(graph);
            if (rawValues == null) return cache;

            foreach (var node in nodes)
            {
                var motion = new BlendTreePatchReflection.BlendTreeNodeProxy(node).Motion;
                if (motion is not BlendTree blendTree) continue;
                foreach (var entry in BlendTreeRecursiveParamCache.Get(blendTree))
                {
                    if (entry.Name == null || cache.ContainsKey(entry.Name)) continue;

                    if (!rawValues.TryGetValue(entry.Name, out var value))
                    {
                        // graph's populated set (from m_RootBlendTree's own recursive walk) doesn't
                        // always cover a nested child's recursive params (Direct-type children in
                        // particular) — seed it via the real native setter so both our lookups and
                        // native NodeGUI's own GetParameterValue calls stop logging "parameter name
                        // does not exist." for it.
                        value = BlendTreePatchReflection.BlendTreeGetInputBlendValueMethod != null
                            ? (float)(BlendTreePatchReflection.BlendTreeGetInputBlendValueMethod.Invoke(blendTree, new object[] { entry.Name }) ?? 0f)
                            : 0f;
                        BlendTreePatchReflection.GraphSetParameterValueMethod?.Invoke(graph, new object[] { entry.Name, value });
                    }
                    cache[entry.Name] = value;
                }
            }
            return cache;
        }

        /* Cheap non-interactive replica of native LayoutSlot rows (input/output pins connecting edges to
           child/parent nodes) — label only, reusing the real Styles.varPinIn/varPinOut GUIStyle so it
           matches native look. Returns the y position after the drawn rows. */
        static GUIStyle _pinInRowStyle, _pinOutRowStyle;

        /* Returns a cached copy of the native pin GUIStyle with alignment forced — input pins read left
           (near the node's left edge), output pins read right (flush against the edge line's start). */
        static GUIStyle GetPinRowStyle(FieldInfo pinStyleField, TextAnchor alignment, ref GUIStyle cache)
        {
            if (cache != null) return cache;
            var baseStyle = pinStyleField?.GetValue(null) as GUIStyle;
            if (baseStyle == null) return null;
            cache = new GUIStyle(baseStyle) { alignment = alignment };
            return cache;
        }

        const float SlotRowHeight = 12f;
        // Shared with MeasureNodeContentHeight — keep these in sync with the draw math in
        // DrawLightweightNodeBox/DrawIdleSliderRows so the box stays sized to fit its content.
        const float TitleAreaHeight = 29f;
        const float SliderRowHeight = 18f, SliderRowSpacing = 2f, SliderSectionGap = 2f;

        /* Shortens text with a trailing "..." until it fits maxWidth under style — cheap binary search,
           only ever run over a handful of slot rows per node, never per-repaint over the whole tree. */
        static string TruncateToWidth(string text, float maxWidth, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || style == null) return text;
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            const string ellipsis = "...";
            int lo = 0, hi = text.Length;
            string result = ellipsis;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var candidate = text.Substring(0, mid) + ellipsis;
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) { result = candidate; lo = mid; }
                else hi = mid - 1;
            }
            return result;
        }

        static float DrawIdleSlotRows(object node, Rect nodeRect, float rowY, Func<object, object> slotsGetter, FieldInfo pinStyleField, TextAnchor alignment, ref GUIStyle styleCache)
        {
            if (slotsGetter == null) return rowY;
            if (slotsGetter(node) is not IEnumerable slots) return rowY;
            var pinStyle = GetPinRowStyle(pinStyleField, alignment, ref styleCache);
            foreach (var slot in slots)
            {
                var title = (BlendTreePatchReflection.SlotTitleRef != null ? BlendTreePatchReflection.SlotTitleRef(slot) : null) ?? "";
                var slotRect = new Rect(nodeRect.x, rowY, nodeRect.width, SlotRowHeight);
                title = TruncateToWidth(title, 180f, pinStyle); // matches native LimitStringWidth(title, 180f, pinStyle)
                GUI.Label(slotRect, title, pinStyle ?? EditorStyles.miniLabel);

                // Native DoSlot sets slot.m_Position every NodeGUI call; idle nodes skip NodeGUI entirely,
                // so without this, EdgeGUI reads a stale/zero rect and draws edges from the corner.
                if (BlendTreePatchReflection.SlotPositionRef != null)
                {
                    var unclipped = BlendTreePatchReflection.GUIClipUnclip != null
                        ? BlendTreePatchReflection.GUIClipUnclip(slotRect)
                        : slotRect;
                    BlendTreePatchReflection.SlotPositionRef(slot) = unclipped;
                }

                rowY += SlotRowHeight;
            }
            return rowY;
        }

        /* Cheap non-interactive replica of NodeGUI's per-parameter EditorGUILayout.Slider rows — label +
           track + thumb drawn from cached values, no live Slider control (that's the expensive part). */
        static GUIStyle _sliderLabelStyle;

        static void DrawIdleSliderRows(Rect nodeRect, float rowY, BlendTree blendTree, Dictionary<string, float> paramValues)
        {
            const float rowHeight = SliderRowHeight, rowSpacing = SliderRowSpacing, labelWidth = 50f, padding = 3f, valueWidth = 45f;
            _sliderLabelStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
            float thumbWidth = GUI.skin.horizontalSliderThumb.fixedWidth > 0f ? GUI.skin.horizontalSliderThumb.fixedWidth : 12f;
            float thumbHeight = GUI.skin.horizontalSliderThumb.fixedHeight > 0f ? GUI.skin.horizontalSliderThumb.fixedHeight : 12f;
            foreach (var entry in BlendTreeRecursiveParamCache.Get(blendTree))
            {
                if (entry.Name == null) continue;
                var rowRect   = new Rect(nodeRect.x + padding, rowY, nodeRect.width - padding * 2f, rowHeight);
                var labelRect = new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height);
                var valueRect = new Rect(rowRect.xMax - valueWidth, rowRect.y, valueWidth, rowRect.height);
                var trackRect = new Rect(rowRect.x + labelWidth, rowRect.y, rowRect.width - labelWidth - valueWidth, rowRect.height);

                GUI.Label(labelRect, entry.Name, _sliderLabelStyle);
                // Full row-height boxes: GUI.skin.horizontalSlider/horizontalSliderThumb bake their own
                // padding to center the thin bar/circle within whatever height they're given — squeezing
                // them into a manually shrunk rect breaks that built-in centering.
                GUI.Box(trackRect, GUIContent.none, GUI.skin.horizontalSlider);

                float value = paramValues.TryGetValue(entry.Name, out var v) ? v : 0f;
                float max = Mathf.Approximately(entry.Max, entry.Min) ? entry.Min + 1f : entry.Max;
                float t = Mathf.InverseLerp(entry.Min, max, value);
                var thumbRect = new Rect(trackRect.x + t * (trackRect.width - thumbWidth), trackRect.y + (trackRect.height - thumbHeight) / 2f, thumbWidth, thumbHeight);
                GUI.Box(thumbRect, GUIContent.none, GUI.skin.horizontalSliderThumb);

                GUI.Label(valueRect, value.ToString("0.#####"), _sliderLabelStyle);

                rowY += rowHeight + rowSpacing;
            }
        }

        /* Replica of PatchBlendTreeNodeGUI.Postfix's top-left blend-type badge and top-right threshold label,
           since idle nodes never run through NodeGUI (and thus never through that postfix) at all. */
        static void DrawIdleTypeAndThresholdLabels(BlendTreePatchReflection.BlendTreeNodeProxy proxy, Rect rect, BlendTree blendTree, Color color)
        {
            if (blendTree != null)
            {
                var typeRect = new Rect(rect.x + 2f, rect.y + 3f, 70f, 11f);
                GUI.Label(typeRect, PatchBlendTreeNodeGUI.BlendTypeLabel(blendTree.blendType), PatchBlendTreeNodeGUI.GetBlendTypeLabelStyle(color));
            }

            var parentNode = proxy.Parent;
            if (parentNode == null) return;
            var parentBlendTree = new BlendTreePatchReflection.BlendTreeNodeProxy(parentNode).Motion as BlendTree;
            if (parentBlendTree == null || parentBlendTree.blendType != BlendTreeType.Simple1D) return;
            int childIndex = proxy.ChildIndex;
            if (childIndex < 0 || childIndex >= parentBlendTree.children.Length) return;
            float threshold = parentBlendTree.children[childIndex].threshold;
            var thresholdRect = new Rect(rect.xMax - 40f, rect.y + 3f, 38f, 11f);
            GUI.Label(thresholdRect, threshold.ToString("0.###"), PatchBlendTreeNodeGUI.GetThresholdLabelStyle(color));
        }

        /* Fetches the native node GUIStyle once per repaint (unselected, from a representative node) so all
           idle boxes share the real background/border look instead of a flat default GUI.skin.box.
           Cached per blend type (not per node) — style/texture generation is already cached by
           PatchNodeStyles keyed on that type, so this just dedupes the reflection Invoke call. */
        static GUIStyle GetIdleNodeStyle(object node, Dictionary<int, GUIStyle> cache)
        {
            var blendType = GetNodeBlendType(node);
            int cacheKey = blendType.HasValue ? (int)blendType.Value : -1; // Dictionary<Nullable<T>,_> throws on a null key
            if (cache.TryGetValue(cacheKey, out var cached)) return cached;

            var result = ResolveNodeStyle(node, selected: false, blendType);
            cache[cacheKey] = result;
            return result;
        }

        static BlendTreeType? GetNodeBlendType(object node) =>
            new BlendTreePatchReflection.BlendTreeNodeProxy(node).Motion is BlendTree blendTree ? blendTree.blendType : (BlendTreeType?)null;

        /* Resolves the native node GUIStyle for one node, routing color through the lightweight-path
           override so idle/active nodes get their real per-blend-type color instead of falling through
           to native's FIFO queue (see AnimatorNodeColorPatch.PatchNodeStyles.Postfix). */
        static GUIStyle ResolveNodeStyle(object node, bool selected, BlendTreeType? blendType)
        {
            if (BlendTreePatchReflection.GetNodeStyleMethod == null) return null;
            object style = BlendTreePatchReflection.NodeStyleGetter?.Invoke(node);
            object color = BlendTreePatchReflection.NodeColorGetter?.Invoke(node);
            if (style == null || color == null) return null;

            PatchBlendTreeOnGraphGUI.LightweightOverrideActive    = true;
            PatchBlendTreeOnGraphGUI.LightweightOverrideBlendType = blendType;
            var result = BlendTreePatchReflection.GetNodeStyleMethod.Invoke(null, new[] { style, color, selected }) as GUIStyle;
            PatchBlendTreeOnGraphGUI.LightweightOverrideActive = false;
            return result;
        }

        /* Draws the real native window for the currently active node — same call shape as native OnGraphGUI. */
        static Rect DrawActiveNodeWindow(object graphGUI, object node, Rect rect)
        {
            bool selected = ReferenceEquals(node, PatchBlendTreeNodeGUI.SelectedNode);
            // Called before NodeGUI runs, so InNodeGUI/CurrentBlendType aren't set yet — resolve
            // this node's own type explicitly instead of falling through to the FIFO queue.
            var nodeStyle = ResolveNodeStyle(node, selected, GetNodeBlendType(node));

            return GUILayout.Window(node.GetHashCode(), rect, delegate
            {
                try { BlendTreePatchReflection.NodeGUIMethod.Invoke(graphGUI, new[] { node }); }
                catch (TargetInvocationException) { /* graph's param dict lags one frame after breadcrumb nav — self-heals next repaint */ }
            }, "", nodeStyle ?? GUI.skin.window, GUILayout.Width(0f), GUILayout.Height(0f));
        }

        /* Cheap static replica for an idle (non-interacted) node — box + name label, no GUILayout.Window.
           ponytail: no native focus ring / title-bar-only drag; click anywhere promotes to SelectedNode,
           real window takes over next repaint (one-frame hitch). Traded for skipping Window() on ~all nodes. */
        static void DrawLightweightNodeBox(object node, BlendTreePatchReflection.BlendTreeNodeProxy proxy, Rect rect, AnimatorDefaultSettings settings, Vector2 mousePos, GUIStyle idleNodeStyle, Dictionary<string, float> paramValues)
        {
            var currentEvent = Event.current;
            if (currentEvent.type == EventType.Repaint)
            {
                var motion = proxy.Motion;
                // proxy.Position.height only gets auto-sized by native when the node actually runs
                // real NodeGUI at least once (see DrawActiveNodeWindow) — idle nodes never do, so a
                // never-hovered node's stored height can be stale/short. Grow the box to fit what
                // we're about to draw instead of trusting it, so content never spills past the border.
                if (motion != null)
                {
                    float contentHeight = MeasureNodeContentHeight(node, motion as BlendTree);
                    if (contentHeight > rect.height) rect.height = contentHeight;
                }

                GUI.Box(rect, GUIContent.none, idleNodeStyle ?? GUI.skin.box);
                if (motion != null)
                {
                    var titleRect = new Rect(rect.x, rect.y + 5f, rect.width, 18f);
                    GUI.Label(titleRect, motion.name, PatchBlendTreeNodeGUI.GetNameLabelStyle(settings.overlayActiveColor));
                    float rowY = rect.y + TitleAreaHeight;

                    rowY = DrawIdleSlotRows(node, rect, rowY, BlendTreePatchReflection.NodeInputSlotsGetter, BlendTreePatchReflection.VarPinInField, TextAnchor.MiddleLeft, ref _pinInRowStyle);
                    rowY = DrawIdleSlotRows(node, rect, rowY, BlendTreePatchReflection.NodeOutputSlotsGetter, BlendTreePatchReflection.VarPinOutField, TextAnchor.MiddleRight, ref _pinOutRowStyle);

                    if (motion is BlendTree blendTree)
                        DrawIdleSliderRows(rect, rowY + SliderSectionGap, blendTree, paramValues);

                    if (settings.overlayEnabled)
                        DrawIdleTypeAndThresholdLabels(proxy, rect, motion as BlendTree, settings.overlayActiveColor);
                }
            }
            else if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rect.Contains(mousePos))
            {
                PatchBlendTreeNodeGUI.SelectedNode = node;
                if (proxy.Parent != null)
                    BlendTreeReparentState.DragCandidate = node;
                currentEvent.Use();
            }
        }

        /* Same row math as the draw calls below (title + pin rows + slider rows), just totalled up
           front instead of drawn, so the box can be sized to fit before anything is rendered into it. */
        static float MeasureNodeContentHeight(object node, BlendTree blendTree)
        {
            float height = TitleAreaHeight
                + CountSlots(BlendTreePatchReflection.NodeInputSlotsGetter, node) * SlotRowHeight
                + CountSlots(BlendTreePatchReflection.NodeOutputSlotsGetter, node) * SlotRowHeight;

            int paramCount = blendTree != null ? BlendTreeRecursiveParamCache.Get(blendTree).Length : 0;
            if (paramCount > 0) height += SliderSectionGap + paramCount * (SliderRowHeight + SliderRowSpacing);

            return height + 4f; // bottom margin
        }

        static int CountSlots(Func<object, object> slotsGetter, object node)
        {
            if (slotsGetter?.Invoke(node) is not IEnumerable slots) return 0;
            int count = 0;
            foreach (var _ in slots) count++;
            return count;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            InBlendTreeGUI = false;
            CurrentGraphGUI = null;
            RestoreSlotTextColors(ResolveStyleField(ref _varPinInField,  "varPinIn"),  _savedVarPinInColors);
            RestoreSlotTextColors(ResolveStyleField(ref _varPinOutField, "varPinOut"), _savedVarPinOutColors);
            RestoreSlotTextColors(EditorStyles.label, _savedEditorLabelColors);
            _savedVarPinInColors    = null;
            _savedVarPinOutColors   = null;
            _savedEditorLabelColors = null;
            try
            {
                var currentEvent = Event.current;

                // Clip drag-drop from Project window (independent of node reparent)
                if (currentEvent.type == EventType.DragUpdated)
                {
                    HandleClipDragUpdated(__instance, currentEvent.mousePosition);
                    return;
                }
                if (currentEvent.type == EventType.DragPerform)
                {
                    HandleClipDragPerform(__instance, currentEvent.mousePosition);
                    return;
                }

                // Clear stale candidate if user released without dragging
                if (currentEvent.type == EventType.MouseUp && !BlendTreeReparentState.IsDragging)
                {
                    BlendTreeReparentState.DragCandidate = null;
                    return;
                }

                // Promote to active drag on first MouseDrag
                if (currentEvent.type == EventType.MouseDrag
                    && BlendTreeReparentState.DragCandidate != null
                    && !BlendTreeReparentState.IsDragging)
                {
                    BlendTreeReparentState.DraggingNode = BlendTreeReparentState.DragCandidate;
                    BlendTreeReparentState.DragCandidate = null;
                    BlendTreeReparentState.IsDragging = true;
                    if (PatchGraphInputHandler.AnimWindow != null)
                        PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;
                }

                if (!BlendTreeReparentState.IsDragging) return;

                PatchGraphInputHandler.AnimWindow?.Repaint();

                if (currentEvent.type == EventType.Repaint)
                    DrawDragPreview(__instance, currentEvent.mousePosition);

                if (currentEvent.type == EventType.MouseUp)
                {
                    var destNode = FindNodeUnderMouse(__instance, currentEvent.mousePosition);
                    if (destNode != null && IsValidDropTarget(destNode))
                        ExecuteReparent(__instance, destNode);
                    BlendTreeReparentState.Clear();
                    currentEvent.Use();
                    return;
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
                {
                    BlendTreeReparentState.Clear();
                    currentEvent.Use();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] BlendTree OnGraphGUI error: {e}");
            }
        }

        /* Returns true if destNode is a blend tree node that is neither the dragging node nor one of its ancestors. */
        static bool IsValidDropTarget(object destNode)
        {
            if (ReferenceEquals(destNode, BlendTreeReparentState.DraggingNode)) return false;
            var parentNode = Traverse.Create(BlendTreeReparentState.DraggingNode).Property("parent").GetValue();
            if (ReferenceEquals(destNode, parentNode)) return false;
            // motion is a public field
            var motion = Traverse.Create(destNode).Field("motion").GetValue() as Motion;
            if (!(motion is BlendTree)) return false;
            if (IsAncestor(BlendTreeReparentState.DraggingNode, destNode)) return false;
            return true;
        }

        /* Returns true if potentialAncestor appears anywhere in node's parent chain, used to prevent reparent cycles. */
        static bool IsAncestor(object potentialAncestor, object node)
        {
            var cursor = Traverse.Create(node).Property("parent").GetValue();
            while (cursor != null)
            {
                if (ReferenceEquals(cursor, potentialAncestor)) return true;
                cursor = Traverse.Create(cursor).Property("parent").GetValue();
            }
            return false;
        }

        /* Returns the first graph node whose position rect contains mousePos, or null if none match. */
        static object FindNodeUnderMouse(object graphGUI, Vector2 mousePos)
        {
            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return null;
            var nodes = PatchGraphInputHandler.GetNodes(graph);
            if (nodes == null) return null;
            foreach (var node in nodes)
            {
                var rect = Traverse.Create(node).Field("position").GetValue<Rect>();
                if (rect.Contains(mousePos)) return node;
            }
            return null;
        }

        /* Moves the dragging node from its current parent blend tree to destNode's blend tree, preserving threshold and position. */
        static void ExecuteReparent(object graphGUI, object destNode)
        {
            var draggingNode = BlendTreeReparentState.DraggingNode;
            var parentNode = Traverse.Create(draggingNode).Property("parent").GetValue();
            // motion is a public field on Node
            var sourceParentBlendTree = Traverse.Create(parentNode).Field("motion").GetValue() as BlendTree;
            var destBlendTree = Traverse.Create(destNode).Field("motion").GetValue() as BlendTree;
            var draggedMotion = Traverse.Create(draggingNode).Field("motion").GetValue() as Motion;

            if (sourceParentBlendTree == null || destBlendTree == null || draggedMotion == null) return;

            int sourceIndex = FindMotionIndex(sourceParentBlendTree, draggedMotion);
            if (sourceIndex < 0) return;

            Undo.RegisterCompleteObjectUndo(sourceParentBlendTree, "Reparent Blend Tree Node");
            Undo.RegisterCompleteObjectUndo(destBlendTree, "Reparent Blend Tree Node");

            var sourceChildren = sourceParentBlendTree.children;
            var snapshot = sourceChildren[sourceIndex];

            sourceParentBlendTree.RemoveChild(sourceIndex);

            destBlendTree.AddChild(draggedMotion);
            var destChildren = destBlendTree.children;
            int lastIndex = destChildren.Length - 1;
            var restoredChild = destChildren[lastIndex];
            restoredChild.threshold            = snapshot.threshold;
            restoredChild.position             = snapshot.position;
            restoredChild.directBlendParameter = snapshot.directBlendParameter;
            destChildren[lastIndex] = restoredChild;
            destBlendTree.children = destChildren;

            EditorUtility.SetDirty(sourceParentBlendTree);
            EditorUtility.SetDirty(destBlendTree);
            RebuildGraph(graphGUI);
        }

        /* Returns the index of motion in blendTree.children, or -1 if not found. */
        static int FindMotionIndex(BlendTree blendTree, Motion motion)
        {
            var children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion == motion) return i;
            }
            return -1;
        }

        /* Draws a line from the dragging node to the mouse and highlights valid drop targets with a green overlay. */
        static void DrawDragPreview(object graphGUI, Vector2 mousePos)
        {
            var draggingRect = Traverse.Create(BlendTreeReparentState.DraggingNode).Field("position").GetValue<Rect>();
            var source = new Vector3(draggingRect.center.x, draggingRect.center.y, 0);
            var destination = new Vector3(mousePos.x, mousePos.y, 0);

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.8f);
            Handles.DrawAAPolyLine(2f, source, destination);
            Handles.EndGUI();

            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return;
            var nodes = PatchGraphInputHandler.GetNodes(graph);
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                if (!IsValidDropTarget(node)) continue;
                var rect = Traverse.Create(node).Field("position").GetValue<Rect>();
                EditorGUI.DrawRect(rect, new Color(0f, 1f, 0f, 0.2f));
            }
        }

        // --- Clip drag-drop ---

        /* Updates DragAndDrop.visualMode to Copy when the mouse is over a node, or Rejected otherwise.
           Multi-clip drags are only accepted when the target node is a blend tree. */
        static void HandleClipDragUpdated(object graphGUI, Vector2 mousePos)
        {
            var clips = GetDraggedClips();
            if (clips.Length == 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }
            var node = FindNodeUnderMouse(graphGUI, mousePos);
            if (node == null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }
            if (clips.Length > 1)
            {
                var nodeMotion = Traverse.Create(node).Field("motion").GetValue() as Motion;
                DragAndDrop.visualMode = nodeMotion is BlendTree
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        /* Performs the clip drop: adds as a new child if the target is a blend tree node, or replaces motion if target is a leaf.
           Multiple clips dropped onto a blend tree node are added one by one. */
        static void HandleClipDragPerform(object graphGUI, Vector2 mousePos)
        {
            var clips = GetDraggedClips();
            if (clips.Length == 0) return;
            var node = FindNodeUnderMouse(graphGUI, mousePos);
            if (node == null) return;

            var nodeMotion = Traverse.Create(node).Field("motion").GetValue() as Motion;
            if (nodeMotion is BlendTree targetBlendTree)
            {
                foreach (var clip in clips)
                    AddClipToBlendTree(graphGUI, targetBlendTree, clip);
            }
            else if (clips.Length == 1)
            {
                ReplaceLeafMotion(graphGUI, node, clips[0]);
            }

            DragAndDrop.AcceptDrag();
        }

        static AnimationClip[] GetDraggedClips() =>
            DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();

        /* Adds clip as a new child of blendTree, auto-sets its threshold via extrapolation, and rebuilds the graph. */
        static void AddClipToBlendTree(object graphGUI, BlendTree blendTree, AnimationClip clip)
        {
            Undo.RegisterCompleteObjectUndo(blendTree, "Add Motion to Blend Tree");
            blendTree.AddChild(clip);
            SetNewThresholdOnLastChild(blendTree);
            EditorUtility.SetDirty(blendTree);
            RebuildGraph(graphGUI);
        }

        /* Replaces the motion on a leaf node with clip by writing directly to the parent blend tree's children array. */
        static void ReplaceLeafMotion(object graphGUI, object node, AnimationClip clip)
        {
            var parentNode = Traverse.Create(node).Property("parent").GetValue();
            if (parentNode == null) return;
            var parentBlendTree = Traverse.Create(parentNode).Field("motion").GetValue() as BlendTree;
            if (parentBlendTree == null) return;

            // Node.childIndex maps directly to blendTree.children index
            int childIndex = Traverse.Create(node).Property("childIndex").GetValue<int>();
            if (childIndex < 0) return;

            Undo.RegisterCompleteObjectUndo(parentBlendTree, "Replace Leaf Motion");
            var children = parentBlendTree.children;
            var child = children[childIndex];
            child.motion = clip;
            children[childIndex] = child;
            parentBlendTree.children = children;
            EditorUtility.SetDirty(parentBlendTree);
            RebuildGraph(graphGUI);
        }

        /* Sets the last child's threshold by extrapolating from the two preceding children; uses 0 if there is only one child. */
        internal static void SetNewThresholdOnLastChild(BlendTree blendTree)
        {
            if (blendTree.useAutomaticThresholds) return;
            var children = blendTree.children;
            if (children.Length == 0) return;
            float threshold;
            if (children.Length < 3)
                threshold = children.Length != 1 ? children[^1].threshold + 1f : 0f;
            else
            {
                float prev2 = children[^3].threshold;
                float prev1 = children[^2].threshold;
                threshold = prev1 + (prev1 - prev2);
            }
            children[^1].threshold = threshold;
            blendTree.children = children;
        }

        /* Calls BuildFromBlendTree on the internal graph object to refresh blend tree node layout after a structural change. */
        internal static void RebuildGraph(object graphGUI)
        {
            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return;
            var rootBlendTree = Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
            AccessTools.Method(graph.GetType(), "BuildFromBlendTree")?.Invoke(graph, new object[] { rootBlendTree });
        }

        /* Recursively collects every parameter name referenced by tree and its descendant blend trees (blend axes + direct blend params). */
        internal static void CollectUsedParameters(BlendTree tree, HashSet<string> result)
        {
            if (tree == null) return;
            if (tree.blendType != BlendTreeType.Direct)
            {
                if (!string.IsNullOrEmpty(tree.blendParameter)) result.Add(tree.blendParameter);
                bool has2DAxis = tree.blendType == BlendTreeType.SimpleDirectional2D
                    || tree.blendType == BlendTreeType.FreeformDirectional2D
                    || tree.blendType == BlendTreeType.FreeformCartesian2D;
                if (has2DAxis && !string.IsNullOrEmpty(tree.blendParameterY)) result.Add(tree.blendParameterY);
            }
            foreach (var child in tree.children)
            {
                if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(child.directBlendParameter))
                    result.Add(child.directBlendParameter);
                if (child.motion is BlendTree childBlendTree)
                    CollectUsedParameters(childBlendTree, result);
            }
        }

        /* Recursively replaces every reference to fromParam with toParam on tree and its descendant blend trees. */
        internal static void RemapNodeParameters(BlendTree tree, string fromParam, string toParam)
        {
            if (tree == null) return;

            var children  = tree.children;
            bool axisMatch  = tree.blendParameter == fromParam || tree.blendParameterY == fromParam;
            bool childMatch = children.Any(c => c.directBlendParameter == fromParam);

            if (axisMatch || childMatch)
            {
                Undo.RecordObject(tree, "Remap Blend Tree Parameter");
                if (tree.blendParameter == fromParam) tree.blendParameter = toParam;
                if (tree.blendParameterY == fromParam) tree.blendParameterY = toParam;
                if (childMatch)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (children[i].directBlendParameter != fromParam) continue;
                        var child = children[i];
                        child.directBlendParameter = toParam;
                        children[i] = child;
                    }
                    tree.children = children;
                }
                EditorUtility.SetDirty(tree);
            }

            foreach (var child in tree.children)
                if (child.motion is BlendTree childBlendTree)
                    RemapNodeParameters(childBlendTree, fromParam, toParam);
        }

        /* Stores the motion of node as the copy source. Deep copy happens at paste time. */
        internal static void ExecuteCopyNode(object node)
        {
            var motion = Traverse.Create(node).Field("motion").GetValue() as Motion;
            if (motion == null) return;
            BlendTreeCopyPasteState.SourceMotion = motion;
        }

        /* Adds a deep copy (for BlendTree) or reference (for clip) of the copied motion as a new child of targetNode's blend tree. */
        internal static void ExecutePasteToNode(object graphGUI, object targetNode)
        {
            if (BlendTreeCopyPasteState.SourceMotion == null) return;
            var targetBlendTree = Traverse.Create(targetNode).Field("motion").GetValue() as BlendTree;
            if (targetBlendTree == null) return;

            var assetPath = AssetDatabase.GetAssetPath(targetBlendTree);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset == null) return;

            Undo.RegisterCompleteObjectUndo(targetBlendTree, "Paste Blend Tree Node");

            Motion pastedMotion = BlendTreeCopyPasteState.SourceMotion is BlendTree sourceBlendTree
                ? DeepCopyBlendTree(sourceBlendTree, mainAsset)
                : BlendTreeCopyPasteState.SourceMotion;

            targetBlendTree.AddChild(pastedMotion);
            SetNewThresholdOnLastChild(targetBlendTree);
            EditorUtility.SetDirty(targetBlendTree);
            AssetDatabase.SaveAssets();
            RebuildGraph(graphGUI);
        }

        /* Recursively deep-copies sourceBlendTree and all BlendTree descendants as new sub-assets of mainAsset. Clips are referenced, not copied. */
        internal static BlendTree DeepCopyBlendTree(BlendTree sourceBlendTree, UnityEngine.Object mainAsset)
        {
            var copy = new BlendTree
            {
                name                   = sourceBlendTree.name,
                blendType              = sourceBlendTree.blendType,
                blendParameter         = sourceBlendTree.blendParameter,
                blendParameterY        = sourceBlendTree.blendParameterY,
                useAutomaticThresholds = sourceBlendTree.useAutomaticThresholds,
                minThreshold           = sourceBlendTree.minThreshold,
                maxThreshold           = sourceBlendTree.maxThreshold,
            };
            Undo.RegisterCreatedObjectUndo(copy, "Paste Blend Tree Node");
            AssetDatabase.AddObjectToAsset(copy, mainAsset);

            var sourceChildren = sourceBlendTree.children;
            for (int i = 0; i < sourceChildren.Length; i++)
            {
                var sourceChild = sourceChildren[i];
                Motion childMotion = sourceChild.motion is BlendTree childBlendTree
                    ? DeepCopyBlendTree(childBlendTree, mainAsset)
                    : sourceChild.motion;

                copy.AddChild(childMotion);
                var destChildren = copy.children;
                var destChild = destChildren[i];
                destChild.threshold            = sourceChild.threshold;
                destChild.position             = sourceChild.position;
                destChild.directBlendParameter = sourceChild.directBlendParameter;
                destChild.cycleOffset          = sourceChild.cycleOffset;
                destChild.mirror               = sourceChild.mirror;
                destChild.timeScale            = sourceChild.timeScale;
                destChildren[i] = destChild;
                copy.children = destChildren;
            }

            EditorUtility.SetDirty(copy);
            return copy;
        }
    }

    // Applies overlayActiveColor to blend tree node title text only (not state machine nodes).
    [HarmonyPatch]
    internal static class PatchBlendTreeGetNodeStyle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.Styles"),
                "GetNodeStyle");

        [HarmonyPostfix]
        static void Postfix(ref GUIStyle __result)
        {
            try
            {
                if (!PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return;
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.nodeColorEnabled) return;
                var color = settings.overlayActiveColor;
                var copy = new GUIStyle(__result);
                // Set all states — Unity uses focused/active for selected windows
                PatchBlendTreeOnGraphGUI.OverrideSlotTextColors(copy, color);
                __result = copy;
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchBlendTreeGetNodeStyle.Postfix: {e}"); }
        }
    }

    // ── Suppress built-in title so our custom label replaces it ──────────────

    [HarmonyPatch]
    internal static class PatchBlendTreeNodeTitle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var nodeType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.Node");
            if (nodeType == null) return null;
            return AccessTools.Method(nodeType, "get_title");
        }

        [HarmonyPostfix]
        static void Postfix(ref string __result)
        {
            if (!PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return;
            __result = "";
        }
    }

    // Prefix on HandleNodeInput: records which node was right-clicked so ShowAsContext patch can append copy/paste items.
    [HarmonyPatch]
    internal static class PatchBlendTreeHandleNodeInput
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var graphGUIType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            return AccessTools.Method(graphGUIType, "HandleNodeInput");
        }

        [HarmonyPrefix]
        static void Prefix(object node)
        {
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                BlendTreeCopyPasteState.PendingContextNode = node;
        }
    }

    // Appends Copy and Paste as Child to the blend tree node context menu just before it shows.
    [HarmonyPatch(typeof(GenericMenu), "ShowAsContext")]
    internal static class PatchGenericMenuBlendTreeCopyPaste
    {
        [HarmonyPrefix]
        static void Prefix(GenericMenu __instance)
        {
            if (!PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return;
            var pendingNode = BlendTreeCopyPasteState.PendingContextNode;
            if (pendingNode == null) return;
            BlendTreeCopyPasteState.ClearPendingContext();

            try
            {
                var graphGUI = PatchBlendTreeOnGraphGUI.CurrentGraphGUI;
                var motion = Traverse.Create(pendingNode).Field("motion").GetValue() as Motion;
                if (motion == null) return;

                __instance.AddSeparator("");

                var capturedNode = pendingNode;
                var capturedGraphGUI = graphGUI;

                __instance.AddItem(new GUIContent(L10n.Get("blend_tree.copy")), false, () =>
                    PatchBlendTreeOnGraphGUI.ExecuteCopyNode(capturedNode));

                if (motion is BlendTree blendTreeMotion)
                {
                    if (BlendTreeCopyPasteState.SourceMotion != null)
                    {
                        __instance.AddItem(new GUIContent(L10n.Get("blend_tree.paste_as_child")), false, () =>
                            PatchBlendTreeOnGraphGUI.ExecutePasteToNode(capturedGraphGUI, capturedNode));
                    }
                    else
                    {
                        __instance.AddDisabledItem(new GUIContent(L10n.Get("blend_tree.paste_as_child")));
                    }

                    __instance.AddSeparator("");

                    AnimatorController capturedController = null;
                    try
                    {
                        var graph  = Traverse.Create(graphGUI).Property("graph").GetValue();
                        var rootBT = BlendTreePatchReflection.GraphRootBlendTreeGetter.Invoke(graph, null) as BlendTree;
                        if (rootBT != null)
                            capturedController = AssetDatabase.LoadMainAssetAtPath(
                                AssetDatabase.GetAssetPath(rootBT)) as AnimatorController;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[YGDR] BlendTree template menu: failed to get controller: {e}");
                    }

                    if (capturedController != null)
                    {
                        var capturedScreenRect = new Rect(
                            GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero),
                            Vector2.zero);
                        var capturedRemapBT   = blendTreeMotion;
                        var capturedRemapCtrl = capturedController;
                        __instance.AddItem(new GUIContent(L10n.Get("blend_tree.remap_parameter")), false, () =>
                        {
                            EditorApplication.delayCall += () =>
                                new BlendTreeRemapSourceDropdown(capturedRemapBT, capturedRemapCtrl, capturedScreenRect)
                                    .ShowCapped(capturedScreenRect);
                        });
                        __instance.AddSeparator("");
                    }

                    var capturedBT         = blendTreeMotion;
                    var capturedCtrl       = capturedController;
                    __instance.AddItem(new GUIContent(L10n.Get("blend_tree.save_template")), false, () =>
                        AnimatorTemplateParameterWindow.OpenCreateBlendTree(capturedBT, capturedCtrl));

                    var blendTreeTemplates = PatchLayerToolbar.LoadBlendTreeTemplateAssets();
                    if (blendTreeTemplates.Count == 0)
                    {
                        __instance.AddDisabledItem(new GUIContent($"{L10n.Get("layer_template.import_template")}/{L10n.Get("blend_tree.no_templates")}"));
                    }
                    else
                    {
                        foreach (var (templateName, templateBlendTree) in blendTreeTemplates)
                        {
                            var capturedTemplate = templateBlendTree;
                            var capturedTargetBT = blendTreeMotion;
                            var capturedTargetCtrl = capturedController;
                            __instance.AddItem(new GUIContent($"{L10n.Get("layer_template.import_template")}/{templateName.Replace('.', '/')}"), false, () =>
                                AnimatorTemplateParameterWindow.OpenImportBlendTree(capturedTemplate, capturedTargetBT, capturedTargetCtrl));
                        }

                        foreach (var (templateName, templateBlendTree) in blendTreeTemplates)
                        {
                            string capturedDir  = System.IO.Path.GetDirectoryName(
                                AssetDatabase.GetAssetPath(templateBlendTree)).Replace('\\', '/');
                            string capturedName = templateName;
                            __instance.AddItem(new GUIContent($"{L10n.Get("layer_template.delete_template")}/{capturedName.Replace('.', '/')}"), false, () =>
                            {
                                if (EditorUtility.DisplayDialog(L10n.Get("blend_tree.delete_template_title"),
                                    string.Format(L10n.Get("layer_template.delete_confirm_body"), capturedName),
                                    L10n.Get("layer_template.delete_confirm_ok"),
                                    L10n.Get("layer_template.delete_confirm_cancel")))
                                    AssetDatabase.DeleteAsset(capturedDir);
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] BlendTree copy-paste menu error: {e}");
            }
        }
    }

    // ── Blend tree node rename state ─────────────────────────────────────────

    internal static class BlendTreeRenameState
    {
        internal static Motion RenameTarget;
        internal static object RenameTargetNode;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Initializes the rename session for motion, storing the node reference and pre-filling the text field with the current name. */
        internal static void Begin(Motion motion, object node)
        {
            RenameTarget     = motion;
            RenameTargetNode = node;
            RenameText       = motion.name;
            JustStarted      = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameMotion(RenameTarget, RenameText);
            RenameTarget     = null;
            RenameTargetNode = null;
            RenameText       = null;
        }

        internal static void Cancel()
        {
            RenameTarget     = null;
            RenameTargetNode = null;
            RenameText       = null;
        }
    }

    // ── Remap parameter dropdowns ────────────────────────────────────────────

    /* First step of blend tree parameter remap: lists parameters actually used by rootNode and its descendants. */
    internal class BlendTreeRemapSourceDropdown : YgdrAdvancedDropdownBase
    {
        readonly BlendTree _rootNode;
        readonly AnimatorController _controller;
        readonly Rect _screenRect;

        internal BlendTreeRemapSourceDropdown(BlendTree rootNode, AnimatorController controller, Rect screenRect)
            : base(new Vector2(200, 250))
        {
            _rootNode   = rootNode;
            _controller = controller;
            _screenRect = screenRect;
        }

        internal void ShowCapped(Rect rect) => ShowCapped(rect, 350f);

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(L10n.Get("blend_tree.remap_parameter"));
            var used = new HashSet<string>();
            PatchBlendTreeOnGraphGUI.CollectUsedParameters(_rootNode, used);
            if (used.Count == 0)
            {
                var empty = new AdvancedDropdownItem(L10n.Get("blend_tree.no_used_parameters")) { enabled = false };
                root.AddChild(empty);
                return root;
            }
            foreach (var paramName in used.OrderBy(n => n, StringComparer.Ordinal))
                root.AddChild(new AdvancedDropdownItem(paramName));
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            var fromParam     = item.name;
            var capturedRoot  = _rootNode;
            var capturedCtrl  = _controller;
            var capturedRect  = _screenRect;
            EditorApplication.delayCall += () =>
                new BlendTreeRemapTargetDropdown(capturedRoot, capturedCtrl, fromParam)
                    .ShowCapped(capturedRect);
        }
    }

    /* Second step of blend tree parameter remap: lists all float parameters on the controller, then remaps rootNode's subtree on selection. */
    internal class BlendTreeRemapTargetDropdown : YgdrAdvancedDropdownBase
    {
        readonly BlendTree _rootNode;
        readonly AnimatorController _controller;
        readonly string _fromParam;

        internal BlendTreeRemapTargetDropdown(BlendTree rootNode, AnimatorController controller, string fromParam)
            : base(new Vector2(200, 250))
        {
            _rootNode   = rootNode;
            _controller = controller;
            _fromParam  = fromParam;
        }

        internal void ShowCapped(Rect rect) => ShowCapped(rect, 350f);

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(L10n.Get("blend_tree.remap_parameter_to"));
            var floatParams = _controller.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Float && p.name != _fromParam)
                .Select(p => p.name)
                .OrderBy(n => n, StringComparer.Ordinal);
            bool any = false;
            foreach (var paramName in floatParams)
            {
                any = true;
                root.AddChild(new AdvancedDropdownItem(paramName));
            }
            if (!any)
                root.AddChild(new AdvancedDropdownItem(L10n.Get("blend_tree.no_float_parameters")) { enabled = false });
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            PatchBlendTreeOnGraphGUI.RemapNodeParameters(_rootNode, _fromParam, item.name);
            EditorUtility.SetDirty(_controller);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
