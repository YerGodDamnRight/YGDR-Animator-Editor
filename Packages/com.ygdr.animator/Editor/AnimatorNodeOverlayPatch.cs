#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // ── State nodes ───────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorStateNodeOverlayPatch
    {
        internal static readonly Dictionary<AnimatorState, Rect> NodeRects = new();
        internal static readonly Dictionary<AnimatorState, Vector2> NodeScreenCenters = new();

        static GUIStyle _indicatorStyle;
        static GUIStyle _loopStyle;
        static GUIStyle _motionNameStyle;

        static GUIStyle IndicatorStyle => _indicatorStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle LoopStyle => _loopStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle MotionNameStyle => _motionNameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _coordsStyle;
        static GUIStyle CoordsStyle => _coordsStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 9,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var state = GetState(__instance);
                if (state == null) return;
                var stateRect    = GUILayoutUtility.GetLastRect();
                var currentEvent = Event.current;
                bool isRenaming = StateRenameState.RenameTarget == state;
                bool isRenamingMotion = MotionRenameState.RenameTargetState == state;

                if (currentEvent.type != EventType.Repaint)
                {
                    if (isRenaming) DrawRenameField(state, stateRect);
                    if (isRenamingMotion) DrawMotionRenameField(state, stateRect);
                    return;
                }

                NodeRects[state] = stateRect;
                NodeScreenCenters[state] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
                var settings = AnimatorDefaultSettings.Load();
                if (!isRenaming)
                    DrawNodeNameLabel(state, stateRect, settings);
                var graphPosition = Vector2.zero;
                if (settings.overlayEnabled && settings.overlayShowCoords)
                {
                    _nodeGraphGetter ??= AccessTools.Method(__instance.GetType(), "get_graph");
                    var graph = _nodeGraphGetter?.Invoke(__instance, null);
                    if (graph != null)
                    {
                        _activeStateMachineGetter ??= AccessTools.Method(graph.GetType(), "get_activeStateMachine");
                        var activeSM = _activeStateMachineGetter?.Invoke(graph, null) as AnimatorStateMachine;
                        if (activeSM != null)
                        {
                            foreach (var childState in activeSM.states)
                            {
                                if (childState.state == state)
                                {
                                    graphPosition = new Vector2(childState.position.x, childState.position.y);
                                    break;
                                }
                            }
                        }
                    }
                }
                if (settings.overlayEnabled)
                    DrawIndicators(state, stateRect, settings, graphPosition);
                if (isRenaming)
                    DrawRenameField(state, stateRect);
                if (isRenamingMotion)
                    DrawMotionRenameField(state, stateRect);
            }
            catch (Exception e) { Debug.LogError($"[YGDR] State node overlay error: {e}"); }
        }

        static void DrawIndicators(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings, Vector2 graphPosition)
        {
            var previousContentColor = GUI.contentColor;

            // Left-anchored  — Rect(nodeRect.x + offsetX, nodeRect.y + offsetY, width, height)
            bool hasMotion = state.motion != null;

            if (settings.overlayShowLoop && hasMotion)
            {
                GUI.contentColor = IsLooping(state.motion) ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f,  nodeRect.y + -26f, 16f, 15f), "↻", LoopStyle);
            }

            if (settings.overlayShowEmpty && !hasMotion)
            {
                GUI.contentColor = settings.overlayActiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.y + -28f, 14f, 15f), "!", IndicatorStyle);
            }

            // Right-anchored — Rect(nodeRect.x + nodeRect.width + offsetX, nodeRect.y + offsetY, width, height)  (offsetX is negative)
            if (settings.overlayShowB)
            {
                GUI.contentColor = state.behaviours.Length > 0 ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -28f, 13f, 15f), "B",  IndicatorStyle);
            }

            if (settings.overlayShowWD)
            {
                GUI.contentColor = state.writeDefaultValues ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -28f, 22f, 15f), "WD", IndicatorStyle);
            }

            if (settings.overlayShowSpeed)
            {
                GUI.contentColor = state.speedParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -5f, 13f, 15f), "S",  IndicatorStyle);
            }

            if (settings.overlayShowMotion)
            {
                GUI.contentColor = state.timeParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -5f, 22f, 15f), "M",  IndicatorStyle);
            }

            if (settings.overlayShowMotionName && MotionRenameState.RenameTargetState != state)
            {
                string label = state.motion != null ? $"[{state.motion.name}]" : "[none]";
                GUI.contentColor = state.motion != null ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x, nodeRect.y + -6f, nodeRect.width, 13f), label, MotionNameStyle);
            }

            if (settings.overlayShowCoords)
            {
                GUI.contentColor = settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.yMax - 13f, nodeRect.width - 4f, 13f),
                    $"({(int)graphPosition.x},{(int)graphPosition.y})", CoordsStyle);
            }

            GUI.contentColor = previousContentColor;
        }

        static bool IsLooping(Motion motion)
        {
            if (motion is AnimationClip clip) return clip.isLooping;
            if (motion is BlendTree blendTree)
            {
                var children = blendTree.children;
                return children.Length > 0 && children.All(x => x.motion != null && IsLooping(x.motion));
            }
            return false;
        }

        static FieldInfo  _stateField;
        static MethodInfo _nodeGraphGetter;
        static MethodInfo _activeStateMachineGetter;

        static AnimatorState GetState(object node)
        {
            _stateField ??= AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
            return _stateField?.GetValue(node) as AnimatorState;
        }

        static GUIStyle _nameLabelStyle;
        static GUIStyle NameLabelStyle => _nameLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(2, 2, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
            normal    = { textColor = Color.white },
        };

        static void DrawNodeNameLabel(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings)
        {
            var previousContentColor = GUI.contentColor;
            GUI.contentColor = settings.overlayActiveColor;
            GUI.Label(new Rect(nodeRect.x, nodeRect.y - 25f, nodeRect.width, 20f), state.name, NameLabelStyle);
            GUI.contentColor = previousContentColor;
        }

        static GUIStyle _renameFieldStyle;
        static GUIStyle RenameFieldStyle => _renameFieldStyle ??= new GUIStyle(EditorStyles.textField)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = null },
            focused   = { background = null },
            hover     = { background = null },
            active    = { background = null },
        };

        static bool _renameFieldHadFocus;

        static void DrawRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "StateRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 24f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            // Check Enter/Escape before TextField so Unity's internal handling can't consume them
            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    StateRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    StateRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            StateRenameState.RenameText = EditorGUI.TextField(fieldRect, StateRenameState.RenameText, RenameFieldStyle);

            if (StateRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                StateRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                StateRenameState.Apply();
            _renameFieldHadFocus = hasFocus;
        }

        static bool _motionRenameFieldHadFocus;

        static void DrawMotionRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "MotionRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 6f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    MotionRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    MotionRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            MotionRenameState.RenameText = EditorGUI.TextField(fieldRect, MotionRenameState.RenameText, RenameFieldStyle);

            if (MotionRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                MotionRenameState.JustStarted = false;
                _motionRenameFieldHadFocus = false;
                return;
            }

            bool hasFocusMotion = GUI.GetNameOfFocusedControl() == controlName;
            if (_motionRenameFieldHadFocus && !hasFocusMotion)
                MotionRenameState.Apply();
            _motionRenameFieldHadFocus = hasFocusMotion;
        }
    }

    // ── Drag-and-drop clip onto existing node ─────────────────────────────────

    // Intercepts AnimatorStateMachine.AddState(name, position) during drag-and-drop.
    // If the drop position lands within an existing node's rect, assigns the clip
    // to that state and skips creating a new one.
    [HarmonyPatch(typeof(AnimatorStateMachine), "AddState", new[] { typeof(string), typeof(Vector3) })]
    internal static class PatchAddStateDrop
    {
        [HarmonyPrefix]
        static bool Prefix(AnimatorStateMachine __instance, Vector3 position, ref AnimatorState __result)
        {
            try
            {
                var clip = DragAndDrop.objectReferences.OfType<AnimationClip>().FirstOrDefault();
                if (clip == null) return true;

                const float nodeW = 200f, nodeH = 40f;
                foreach (var childState in __instance.states)
                {
                    var nodeRect = new Rect(childState.position.x, childState.position.y, nodeW, nodeH);
                    if (!nodeRect.Contains(new Vector2(position.x, position.y))) continue;

                    Undo.RegisterCompleteObjectUndo(childState.state, "Assign Motion Clip");
                    childState.state.motion = clip;
                    EditorUtility.SetDirty(childState.state);
                    __result = childState.state;
                    return false;
                }
            }
            catch (Exception e) { Debug.LogError($"[YGDR] AddState drop error: {e}"); }
            return true;
        }
    }

    // ── Node background texture replacement ──────────────────────────────────
    // Patches Styles.GetNodeStyle to swap the background Texture2D with a
    // tinted copy of the RATS node PNGs, preserving rounded corners and shape.

    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class PatchNodeStyles
    {
        static readonly Dictionary<string, GUIStyle> _styleCache = new();
        static readonly Dictionary<(Color, bool, bool), Texture2D> _texCache = new();

        static Texture2D _baseNode;
        static Texture2D _baseNodeActive;
        static Texture2D _baseSubSM;
        static Texture2D _baseSubSMActive;

        static (Color state, Color def, Color subSM, Color entry, Color exit, Color any) _cachedColors;

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(AccessTools.TypeByName("UnityEditor.Graphs.Styles"), "GetNodeStyle");
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

                string styleKey = $"{styleName}|{color}|{on}";
                if (_styleCache.TryGetValue(styleKey, out var cached))
                {
                    __result = cached;
                    return;
                }

                bool isSubStateMachine = styleName == "node hex";
                var nodeColor          = ResolveColor(styleName, color, settings);
                var texKey             = (nodeColor, isSubStateMachine, on);

                if (!_texCache.TryGetValue(texKey, out var nodeTexture))
                {
                    var baseNormal       = isSubStateMachine ? _baseSubSM       : _baseNode;
                    var baseActiveTexture = isSubStateMachine ? _baseSubSMActive : _baseNodeActive;
                    nodeTexture = on ? CompositeClone(baseNormal, nodeColor, baseActiveTexture) : TintClone(baseNormal, nodeColor);
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

        static Color ResolveColor(string styleName, int colorIndex, AnimatorDefaultSettings s)
        {
            if (styleName == "node hex") return s.subStateMachineColor;
            return colorIndex switch
            {
                5 => s.defaultStateColor,
                3 => s.entryNodeColor,
                6 => s.exitNodeColor,
                2 => s.anyStateNodeColor,
                _ => s.stateNodeColor
            };
        }

        static bool ColorsChanged(AnimatorDefaultSettings s)
        {
            var current = (s.stateNodeColor, s.defaultStateColor, s.subStateMachineColor,
                           s.entryNodeColor, s.exitNodeColor, s.anyStateNodeColor);
            if (_cachedColors == current) return false;
            _cachedColors = current;
            return true;
        }

        internal static void HandleTextures() => Rebuild();
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

        // Tints base then alpha-composites overlay on top (selection highlight over tinted node).
        static Texture2D CompositeClone(Texture2D baseTexture, Color tint, Texture2D overlay)
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
                    float a = overlayPixels[i].a;
                    pixels[i] = pixels[i] * (1f - a) + overlayPixels[i] * a;
                }
            }

            resultTexture.SetPixels(pixels);
            resultTexture.Apply(false, false);
            resultTexture.hideFlags = HideFlags.HideAndDontSave;
            return resultTexture;
        }

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

    // ── Special node rect storage (for transition overlay) ───────────────────

    internal static class SpecialNodeRects
    {
        internal static Rect AnyState;
        internal static Rect Entry;
        internal static Rect Exit;
        internal static readonly Dictionary<AnimatorStateMachine, Rect> SubSMs = new();

        internal static Vector2 AnyStateScreen;
        internal static Vector2 EntryScreen;
        internal static Vector2 ExitScreen;
        internal static readonly Dictionary<AnimatorStateMachine, Vector2> SubSMScreens = new();
    }

    // ── Entry / Exit / Any State nodes ───────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorEntryNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.EntryNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorEntryNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Entry = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.EntryScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    [HarmonyPatch]
    internal static class AnimatorExitNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.ExitNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorExitNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Exit = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.ExitScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    [HarmonyPatch]
    internal static class AnimatorAnyStateNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorAnyStateNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.AnyState = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.AnyStateScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    // ── Sub state machine nodes ───────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorSubSMNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateMachineNodeType, "NodeUI");

        static GUIStyle _renameFieldStyle;
        static GUIStyle RenameFieldStyle => _renameFieldStyle ??= new GUIStyle(EditorStyles.textField)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = null },
            focused   = { background = null },
            hover     = { background = null },
            active    = { background = null },
        };

        static bool _renameFieldHadFocus;

        static FieldInfo _stateMachineField;

        static AnimatorStateMachine GetStateMachine(object node)
        {
            _stateMachineField ??= AccessTools.Field(AnimatorEditorInit.StateMachineNodeType, "stateMachine");
            return _stateMachineField?.GetValue(node) as AnimatorStateMachine;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            var sm = GetStateMachine(__instance);
            if (sm == null) return;

            var nodeRect = GUILayoutUtility.GetLastRect();
            SpecialNodeRects.SubSMs[sm] = nodeRect;
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.SubSMScreens[sm] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));

            if (SubSMRenameState.RenameTarget != sm) return;
            DrawRenameField();
        }

        static void DrawRenameField()
        {
            const string controlName = "SubSMRenameField";
            // NodeUI has no GUILayout content — draw in local window coords, title bar area at y < 0, content at y >= 0
            var fieldRect = new Rect(2f, 10f, 196f, 17f);
            var currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    SubSMRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    SubSMRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            SubSMRenameState.RenameText = EditorGUI.TextField(fieldRect, SubSMRenameState.RenameText, RenameFieldStyle);

            if (SubSMRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                SubSMRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                SubSMRenameState.Apply();
            _renameFieldHadFocus = hasFocus;
        }
    }

    // ── Transition line color + animated arrow ────────────────────────────────

    [HarmonyPatch]
    internal static class PatchDrawEdge
    {
        static readonly Type EdgeGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EdgeGUI");
        static readonly Type EdgeType =
            AccessTools.TypeByName("UnityEditor.Graphs.Edge");

        const float LabelOffsetAbove = 10f;
        const float LabelOffsetBelow = -25f;

        static GUIStyle _labelStyle;
        static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter };

        static MethodInfo _getEdgePoints;
        static MethodInfo _drawArrow;
        static MethodInfo _edgeSizeMultiplier;
        static MethodInfo _fromSlotGetter;
        static MethodInfo _toSlotGetter;
        static MethodInfo _slotNodeGetter;
        static FieldInfo  _stateField;
        static FieldInfo  _labelTransitionsField;
        static FieldInfo  _labelTransitionContextField;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(EdgeGUIType, "DrawEdge");

        // __state: 0 = entry (skip postfix), 1 = selected, 2 = normal
        [HarmonyPrefix]
        static void Prefix(object edge, ref Color color, ref int __state)
        {
            __state = 0;
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.transitionOverlayEnabled) return;
                if (IsEntryEdge(edge)) return;
                bool selected = color.b > color.r + 0.15f;
                __state = selected ? 1 : 2;
                if (!selected) color = settings.transitionOverlayColor;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge prefix error: {e}"); }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, object edge, object info, int __state)
        {
            if (__state == 0) return;
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                bool animate = settings.transitionAnimateSelected && (__state == 1 || IsNodeSelected(edge));

                if (!settings.transitionShowLabel && !animate) return;

                _getEdgePoints ??= AccessTools.Method(EdgeGUIType, "GetEdgePoints",
                    new[] { EdgeType, typeof(Vector3).MakeByRefType() });
                var args = new object[] { edge, Vector3.zero };
                var points = _getEdgePoints?.Invoke(__instance, args) as Vector3[];
                if (points == null || points.Length < 2) return;
                var cross = (Vector3)args[1];

                var sourcePoint      = points[0];
                var destinationPoint = points[points.Length - 1];
                var midPoint         = Vector3.Lerp(sourcePoint, destinationPoint, 0.5f);
                var direction        = (destinationPoint - sourcePoint).normalized;

                if (settings.transitionShowLabel)
                {
                    var label = BuildLabel(info);
                    if (label != null) DrawLabel((Vector2)midPoint, (Vector2)direction, label);
                }

                if (!animate) return;

                 _edgeSizeMultiplier ??= AccessTools.PropertyGetter(EdgeGUIType, "edgeSizeMultiplier");
                 float mult         = _edgeSizeMultiplier != null ? (float)_edgeSizeMultiplier.Invoke(__instance, null) : 1f;
                 float arrowSize    = 5f * mult;
                 float outlineWidth = 2f * mult;

                 var arrowColor = settings.transitionIndicatorArrowsEnabled
                     ? PatchDrawArrows.ResolveArrowColor(info, settings) ?? settings.transitionOverlayColor
                     : settings.transitionOverlayColor;

                 float animationProgress = (float)(EditorApplication.timeSinceStartup * 0.5 % 1.0);
                 var animatedPosition = animationProgress < 0.5f
                     ? Vector3.Lerp(midPoint, destinationPoint, animationProgress * 2f)
                     : Vector3.Lerp(sourcePoint, midPoint, (animationProgress - 0.5f) * 2f);

                 _drawArrow ??= AccessTools.Method(EdgeGUIType, "DrawArrow");
                 _drawArrow?.Invoke(null, new object[] { arrowColor, cross, direction, animatedPosition, arrowSize, outlineWidth });
                
                 foreach (var w in Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType))
                     ((EditorWindow)(object)w).Repaint();
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawEdge postfix error: {e}"); }
        }

        static string BuildLabel(object info)
        {
            if (info == null) return null;
            _labelTransitionsField ??= AccessTools.Field(info.GetType(), "transitions");
            var transitions = _labelTransitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;

            var stateTransitions = new List<AnimatorStateTransition>();
            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                _labelTransitionContextField ??= AccessTools.Field(transitionContext.GetType(), "transition");
                if (_labelTransitionContextField?.GetValue(transitionContext) is AnimatorStateTransition stateTransition)
                    stateTransitions.Add(stateTransition);
            }
            if (stateTransitions.Count == 0) return null;

            if (stateTransitions.Any(x => !x.hasExitTime && (x.conditions == null || x.conditions.Length == 0)))
                return "Invalid";

            if (stateTransitions.Count == 1 && stateTransitions[0].conditions?.Length == 1)
                return FormatCondition(stateTransitions[0].conditions[0]);

            int total = stateTransitions.Sum(x => x.conditions?.Length ?? 0);
            return $"{total} Conditions";
        }

        static readonly string[] GestureNames =
        {
            "Neutral", "Fist", "OpenHand", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp"
        };

        static string FormatCondition(AnimatorCondition animatorCondition)
        {
            var parameterLabel = animatorCondition.parameter.Length > 16 ? animatorCondition.parameter[..16] + "\u2026" : animatorCondition.parameter;
            return animatorCondition.mode switch
            {
                AnimatorConditionMode.If       => $"{parameterLabel} = True",
                AnimatorConditionMode.IfNot    => $"{parameterLabel} = False",
                AnimatorConditionMode.Greater  => $"{parameterLabel} > {animatorCondition.threshold:0.##}",
                AnimatorConditionMode.Less     => $"{parameterLabel} < {animatorCondition.threshold:0.##}",
                AnimatorConditionMode.Equals   => $"{parameterLabel} = {FormatIntThreshold(animatorCondition)}",
                AnimatorConditionMode.NotEqual => $"{parameterLabel} \u2260 {FormatIntThreshold(animatorCondition)}",
                _ => parameterLabel
            };
        }

        static string FormatIntThreshold(AnimatorCondition animatorCondition)
        {
            int intValue = (int)animatorCondition.threshold;
            if ((animatorCondition.parameter == "GestureLeft" || animatorCondition.parameter == "GestureRight")
                && intValue >= 0 && intValue < GestureNames.Length)
                return $"{intValue} ({GestureNames[intValue]})";
            return intValue.ToString();
        }

        static void DrawLabel(Vector2 mid, Vector2 dir, string text)
        {
            float yOffset = dir.x >= 0f ? LabelOffsetAbove : LabelOffsetBelow;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle > 90f)  angle -= 180f;
            if (angle < -90f) angle += 180f;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, mid);
            GUI.Label(new Rect(mid.x - 75f, mid.y + yOffset, 150f, 14f), text, LabelStyle);
            GUI.matrix = matrix;
        }

        static bool IsEntryEdge(object edge)
        {
            _fromSlotGetter ??= AccessTools.PropertyGetter(EdgeType, "fromSlot")
                             ?? AccessTools.Method(EdgeType, "get_fromSlot");
            var slot = _fromSlotGetter?.Invoke(edge, null);
            if (slot == null) return false;
            _slotNodeGetter ??= AccessTools.PropertyGetter(slot.GetType(), "node")
                             ?? AccessTools.Method(slot.GetType(), "get_node");
            var node = _slotNodeGetter?.Invoke(slot, null);
            return node != null && AnimatorEditorInit.EntryNodeType.IsInstanceOfType(node);
        }

        static bool IsNodeSelected(object edge)
        {
            try
            {
                _fromSlotGetter ??= AccessTools.PropertyGetter(EdgeType, "fromSlot")
                                 ?? AccessTools.Method(EdgeType, "get_fromSlot");
                _toSlotGetter   ??= AccessTools.PropertyGetter(EdgeType, "toSlot")
                                 ?? AccessTools.Method(EdgeType, "get_toSlot");
                _slotNodeGetter ??= AccessTools.PropertyGetter(
                    _fromSlotGetter?.Invoke(edge, null)?.GetType() ?? typeof(object), "node")
                    ?? AccessTools.Method(
                    _fromSlotGetter?.Invoke(edge, null)?.GetType() ?? typeof(object), "get_node");
                _stateField ??= AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");

                var selected = Selection.objects;
                foreach (var slot in new[] { _fromSlotGetter?.Invoke(edge, null), _toSlotGetter?.Invoke(edge, null) })
                {
                    if (slot == null) continue;
                    var node = _slotNodeGetter?.Invoke(slot, null);
                    if (node == null || !AnimatorEditorInit.StateNodeType.IsInstanceOfType(node)) continue;
                    var state = _stateField?.GetValue(node) as AnimatorState;
                    if (state != null && System.Array.IndexOf(selected, state) >= 0) return true;
                }
            }
            catch { }
            return false;
        }
    }

    // ── Transition arrow color ────────────────────────────────────────────────
    // Intercepts DrawArrows to apply condition-based arrow color independently
    // from the line color. Reflects into EdgeInfo.transitions to read each
    // AnimatorStateTransition — entry edges (AnimatorTransition only) are skipped
    // naturally. Color persists through selection.
    //   Red   — any transition has no conditions AND no exit time
    //   Green — any transition has duration == 0
    //   Default — transitionOverlayArrowColor

    [HarmonyPatch]
    internal static class PatchDrawArrows
    {
        static readonly Type EdgeGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EdgeGUI");

        static readonly Dictionary<Type, FieldInfo> _transitionsFields = new();
        static readonly Dictionary<Type, FieldInfo> _transitionFields = new();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(EdgeGUIType, "DrawArrows");

        [HarmonyPrefix]
        static void Prefix(ref Color color, object info)
        {
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.transitionOverlayEnabled || !settings.transitionIndicatorArrowsEnabled || info == null) return;
                var resolved = ResolveArrowColor(info, settings);
                if (resolved.HasValue)
                    color = resolved.Value;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] DrawArrows prefix error: {e}"); }
        }

        internal static Color? ResolveArrowColor(object info, AnimatorDefaultSettings s)
        {
            if (info == null) return null;
            var infoType = info.GetType();
            if (!_transitionsFields.TryGetValue(infoType, out var transitionsField))
                _transitionsFields[infoType] = transitionsField = AccessTools.Field(infoType, "transitions");
            var transitions = transitionsField?.GetValue(info) as System.Collections.IList;
            if (transitions == null || transitions.Count == 0) return null;

            bool anyRed  = false;
            bool allGreen = true;
            bool hasStateTransition = false;

            foreach (var transitionContext in transitions)
            {
                if (transitionContext == null) continue;
                var transitionContextType = transitionContext.GetType();
                if (!_transitionFields.TryGetValue(transitionContextType, out var transitionField))
                    _transitionFields[transitionContextType] = transitionField = AccessTools.Field(transitionContextType, "transition");
                if (transitionField?.GetValue(transitionContext) is not AnimatorStateTransition stateTransition) continue;

                hasStateTransition = true;
                bool hasConditions = stateTransition.conditions != null && stateTransition.conditions.Length > 0;
                bool isValid = stateTransition.hasExitTime || hasConditions;
                if (!isValid) anyRed = true;
                if (stateTransition.duration != 0f) allGreen = false;
            }

            if (!hasStateTransition) return null;
            if (anyRed) return s.transitionArrowNoConditionColor;
            if (allGreen) return s.transitionArrowInstantColor;
            return s.transitionOverlayColor;
        }
    }

    // ── State rename state ────────────────────────────────────────────────────

    internal static class StateRenameState
    {
        internal static AnimatorState RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;

        internal static void Begin(AnimatorState state)
        {
            RenameTarget = state;
            RenameText   = state.name;
            JustStarted  = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            Undo.RecordObject(RenameTarget, "Rename State");
            RenameTarget.name = RenameText;
            EditorUtility.SetDirty(RenameTarget);
            RenameTarget = null;
            RenameText   = null;
        }

        internal static void Cancel()
        {
            RenameTarget = null;
            RenameText   = null;
        }
    }

    internal static class SubSMRenameState
    {
        internal static AnimatorStateMachine RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;

        internal static void Begin(AnimatorStateMachine stateMachine)
        {
            RenameTarget = stateMachine;
            RenameText   = stateMachine.name;
            JustStarted  = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            Undo.RecordObject(RenameTarget, "Rename Sub-State Machine");
            RenameTarget.name = RenameText;
            EditorUtility.SetDirty(RenameTarget);
            RenameTarget = null;
            RenameText   = null;
        }

        internal static void Cancel()
        {
            RenameTarget = null;
            RenameText   = null;
        }
    }

    internal static class MotionRenameState
    {
        internal static Motion RenameTarget;
        internal static AnimatorState RenameTargetState;
        internal static string RenameText;
        internal static bool JustStarted;

        internal static void Begin(Motion motion, AnimatorState state)
        {
            RenameTarget      = motion;
            RenameTargetState = state;
            RenameText        = motion.name;
            JustStarted       = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            if (AssetDatabase.IsMainAsset(RenameTarget))
            {
                var path = AssetDatabase.GetAssetPath(RenameTarget);
                AssetDatabase.RenameAsset(path, RenameText);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Undo.RecordObject(RenameTarget, "Rename Motion Clip");
                RenameTarget.name = RenameText;
                EditorUtility.SetDirty(RenameTarget);
            }
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }

        internal static void Cancel()
        {
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }
    }

    // ── Suppress built-in title label ─────────────────────────────────────────

    [HarmonyPatch]
    internal static class PatchStateNodeTitle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "get_title");

        [HarmonyPostfix]
        static void Postfix(ref string __result) => __result = "";
    }

    // ── Shared utilities ──────────────────────────────────────────────────────

    internal static class NodeOverlayUtils
    {
        static readonly Dictionary<Type, MethodInfo> _positionGetters = new();

        internal static Vector2 GetNodeSize(object node)
        {
            var type = node.GetType();
            if (!_positionGetters.TryGetValue(type, out var getter))
                _positionGetters[type] = getter = AccessTools.Method(type, "get_position");
            if (getter?.Invoke(node, null) is Rect nodeRect) return new Vector2(nodeRect.width, nodeRect.height);
            return new Vector2(160f, 40f);
        }

        internal static IEnumerable<CodeInstruction> InjectColorDraw(
            IEnumerable<CodeInstruction> instructions, MethodInfo method)
        {
            var list = instructions.ToList();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].opcode != OpCodes.Ret) continue;
                list.Insert(i, new CodeInstruction(OpCodes.Call, method));
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
            }
            return list;
        }
    }
}
#endif