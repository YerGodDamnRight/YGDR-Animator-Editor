#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Double-click empty graph space → create state at cursor
    // Also tracks hovered node for chain-mode snap
    [HarmonyPatch]
    internal static class PatchGraphDoubleClickCreate
    {
        static FieldInfo _mGraphField;
        static Type _stateNodeType;
        static EditorWindow _animWindow;

        static Vector2 _lastMousePosition;
        static HashSet<AnimatorState> _prepasteStateSet;
        static AnimatorStateMachine _pasteSM;

        /* Lazily resolves and caches the m_Graph FieldInfo from the GraphGUI instance type. */
        static FieldInfo MGraphField(object instance) =>
            _mGraphField ??= AccessTools.Field(instance.GetType(), "m_Graph");

        internal static Type StateNodeType =>
            _stateNodeType ??= AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateNode");

        internal static EditorWindow AnimWindow
        {
            get
            {
                if (_animWindow == null)
                {
                    var arr = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
                    _animWindow = arr.Length > 0 ? arr[0] as EditorWindow : null;
                }
                return _animWindow;
            }
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
                "OnGraphGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;

                if (currentEvent.isMouse || currentEvent.type == EventType.MouseMove)
                    _lastMousePosition = currentEvent.mousePosition;

                if (currentEvent.type == EventType.ExecuteCommand && currentEvent.commandName == "Paste")
                {
                    var getActiveSM = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                    var activeSM = getActiveSM?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSM != null)
                    {
                        _pasteSM = activeSM;
                        _prepasteStateSet = new HashSet<AnimatorState>(activeSM.states.Select(childState => childState.state));
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F2)
                {
                    var selectedState = Selection.activeObject as AnimatorState;
                    if (selectedState != null)
                    {
                        StateRenameState.Begin(selectedState);
                        currentEvent.Use();
                        return;
                    }
                    var selectedSubSM = Selection.activeObject as AnimatorStateMachine;
                    if (selectedSubSM != null)
                    {
                        SubSMRenameState.Begin(selectedSubSM);
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F3)
                {
                    var selectedState = Selection.activeObject as AnimatorState;
                    if (selectedState != null && selectedState.motion != null)
                    {
                        MotionRenameState.Begin(selectedState.motion, selectedState);
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
                {
                    if (PatchStateChainTransition.ChainActive) { PatchStateChainTransition.Clear(); currentEvent.Use(); return; }
                    if (PatchTransitionCopyPaste.PasteActive) { PatchTransitionCopyPaste.ClearPaste(); currentEvent.Use(); return; }
                    if (PatchStateNodeMenu._multiTransitionSources != null || PatchStateNodeMenu._redirectTransitions != null || PatchStateNodeMenu._replicateTransitions != null)
                    {
                        PatchStateNodeMenu._multiTransitionSources = null;
                        PatchStateNodeMenu._multiTransitionSM = null;
                        PatchStateNodeMenu._redirectTransitions = null;
                        PatchStateNodeMenu._redirectSM = null;
                        PatchStateNodeMenu._replicateTransitions = null;
                        PatchStateNodeMenu._replicateSM = null;
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.C)
                {
                    var selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
                    if (selectedTransitions.Length > 0) { PatchTransitionCopyPaste.SetClipboard(selectedTransitions); currentEvent.Use(); return; }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.V
                    && PatchTransitionCopyPaste.HasClipboard)
                {
                    var pasteSource = Selection.activeObject as AnimatorState;
                    if (pasteSource != null)
                    {
                        var pasteGraph = MGraphField(__instance)?.GetValue(__instance);
                        foreach (var node in GetNodes(pasteGraph) ?? System.Array.Empty<object>())
                        {
                            if (node.GetType() != StateNodeType) continue;
                            var nodeState = AccessTools.Field(AnimatorEditorInit.StateNodeType, "state")?.GetValue(node) as AnimatorState;
                            if (nodeState != pasteSource) continue;
                            var sourceRect = Traverse.Create(node).Field("position").GetValue<Rect>();
                            PatchTransitionCopyPaste.BeginPaste(pasteSource, sourceRect);
                            if (AnimWindow != null) AnimWindow.wantsMouseMove = true;
                            currentEvent.Use();
                            break;
                        }
                    }
                    return;
                }

                if ((PatchStateChainTransition.ChainActive || PatchTransitionCopyPaste.PasteActive) && currentEvent.type == EventType.MouseMove)
                    UpdateSnapTarget(__instance, currentEvent.mousePosition);

                if (currentEvent.type != EventType.MouseDown || currentEvent.clickCount != 2 || currentEvent.button != 0 || currentEvent.control)
                    return;

                var mousePos = currentEvent.mousePosition;
                var graph = MGraphField(__instance)?.GetValue(__instance);
                if (graph == null) return;

                var nodes = GetNodes(graph);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var pos = Traverse.Create(node).Field("position").GetValue();
                        if (pos is Rect rect && rect.Contains(mousePos)) return;
                    }
                }

                var getActiveStateMachine = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                var activeStateMachine = getActiveStateMachine?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeStateMachine == null) return;

                Undo.RegisterCompleteObjectUndo(activeStateMachine, "Create State");
                var newState = activeStateMachine.AddState("New State");

                var states = activeStateMachine.states;
                for (int i = 0; i < states.Length; i++)
                {
                    if (states[i].state != newState) continue;
                    var childAnimatorState = states[i];
                    childAnimatorState.position = new Vector3(mousePos.x - 100, mousePos.y - 22, 0);
                    states[i] = childAnimatorState;
                    break;
                }
                activeStateMachine.states = states;
                EditorUtility.SetDirty(activeStateMachine);
                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Double-click create state error: {e}");
            }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (_pasteSM == null) return;
            try
            {
                const float nodeHalfWidth = 40f;
                const float nodeHalfHeight = 40f;

                var allChildStates = _pasteSM.states;
                var newStateIndices = new List<int>();
                for (int i = 0; i < allChildStates.Length; i++)
                {
                    if (!_prepasteStateSet.Contains(allChildStates[i].state))
                        newStateIndices.Add(i);
                }

                if (newStateIndices.Count == 0) return;

                Vector2 currentTopLeft = _lastMousePosition - new Vector2(nodeHalfWidth, nodeHalfHeight);
                for (int j = 0; j < newStateIndices.Count; j++)
                {
                    int index = newStateIndices[j];
                    var childState = allChildStates[index];
                    childState.position = new Vector3(currentTopLeft.x, currentTopLeft.y, 0);
                    allChildStates[index] = childState;
                    currentTopLeft += new Vector2(nodeHalfWidth, nodeHalfHeight);
                }

                _pasteSM.states = allChildStates;
                EditorUtility.SetDirty(_pasteSM);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Paste reposition error: {e}");
            }
            finally
            {
                _pasteSM = null;
                _prepasteStateSet = null;
            }
        }

        /* Updates PatchStateChainTransition.SnapTarget to the center of whichever state node the mouse is over, or null. */
        static void UpdateSnapTarget(object graphGUI, Vector2 mousePos)
        {
            var graph = MGraphField(graphGUI)?.GetValue(graphGUI);
            if (graph == null) { PatchStateChainTransition.SnapTarget = null; return; }

            foreach (var node in GetNodes(graph) ?? Array.Empty<object>())
            {
                if (node.GetType() != StateNodeType) continue;
                var pos = Traverse.Create(node).Field("position").GetValue();
                if (pos is Rect rect && rect.Contains(mousePos))
                {
                    PatchStateChainTransition.SnapTarget = rect.center;
                    return;
                }
            }
            PatchStateChainTransition.SnapTarget = null;
        }

        /* Returns the nodes collection from a graph object, trying the nodes property then the nodes field. */
        internal static IEnumerable GetNodes(object graph)
        {
            var traverse = Traverse.Create(graph);
            return traverse.Property("nodes").GetValue() as IEnumerable
                ?? traverse.Field("nodes").GetValue() as IEnumerable;
        }
    }

    // Draws chain-mode transition preview line on the same layer as real edges (under nodes)
    [HarmonyPatch]
    internal static class PatchEdgeGUIDoEdges
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EdgeGUI"),
                "DoEdges");

        [HarmonyPostfix]
        static void Postfix()
        {
            bool isActive = PatchStateChainTransition.ChainActive || PatchTransitionCopyPaste.PasteActive;
            if (!isActive) return;
            try
            {
                PatchGraphDoubleClickCreate.AnimWindow?.Repaint();

                if (Event.current.type != EventType.Repaint) return;

                var sourceRect = PatchStateChainTransition.ChainActive
                    ? PatchStateChainTransition.ChainSourceRect
                    : PatchTransitionCopyPaste.PasteSourceRect;
                if (sourceRect == Rect.zero) return;

                var source = new Vector3(sourceRect.center.x, sourceRect.center.y, 0);
                Vector3 destination;
                if (PatchStateChainTransition.SnapTarget.HasValue)
                {
                    var snap = PatchStateChainTransition.SnapTarget.Value;
                    destination = new Vector3(snap.x, snap.y, 0);
                }
                else
                {
                    destination = new Vector3(Event.current.mousePosition.x, Event.current.mousePosition.y, 0);
                }

                var direction = (destination - source).normalized;
                var perpendicular = new Vector3(-direction.y, direction.x, 0);

                Handles.BeginGUI();
                Handles.color = new Color(1f, 1f, 1f, 0.8f);
                Handles.DrawAAPolyLine(2f, source, destination);

                var midpoint = (source + destination) * 0.5f;
                Handles.DrawAAConvexPolygon(
                    midpoint + direction * 6f,
                    midpoint - direction * 6f + perpendicular * 5f,
                    midpoint - direction * 6f - perpendicular * 5f);
                Handles.EndGUI();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Chain line draw error: {e}");
            }
        }
    }

    // Ctrl+double-click state → begin transition chain; click next state to continue; Escape to stop
    [HarmonyPatch]
    internal static class PatchStateChainTransition
    {
        internal static bool ChainActive { get; private set; }
        internal static Rect ChainSourceRect { get; private set; }
        internal static Vector2? SnapTarget { get; set; }
        private static AnimatorState _chainSource;
        static FieldInfo _stateField;

        internal static void Clear()
        {
            ChainActive = false;
            _chainSource = null;
            ChainSourceRect = Rect.zero;
            SnapTarget = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateNode"),
                "NodeUI",
                new[] { AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI") });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;

                _stateField ??= AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
                var nodeState = _stateField?.GetValue(__instance) as AnimatorState;
                if (nodeState == null) return;

                if (currentEvent.control && currentEvent.clickCount == 2)
                {
                    ChainActive = true;
                    _chainSource = nodeState;
                    ChainSourceRect = Traverse.Create(__instance).Field("position").GetValue<Rect>();
                    SnapTarget = null;
                    // Enable MouseMove delivery once when chain starts
                    if (PatchGraphDoubleClickCreate.AnimWindow != null)
                        PatchGraphDoubleClickCreate.AnimWindow.wantsMouseMove = true;
                    currentEvent.Use();
                    return;
                }

                if (ChainActive && currentEvent.clickCount == 1 && !currentEvent.control && nodeState != _chainSource)
                {
                    Undo.RegisterCompleteObjectUndo(_chainSource, "Chain Transition");
                    _chainSource.AddTransition(nodeState);
                    EditorUtility.SetDirty(_chainSource);
                    _chainSource = nodeState;
                    ChainSourceRect = Traverse.Create(__instance).Field("position").GetValue<Rect>();
                    SnapTarget = null;
                    currentEvent.Use();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Chain transition error: {e}");
            }
        }
    }
    // Ctrl+C to copy selected transitions, Ctrl+V on source state, click destination to paste
    [HarmonyPatch]
    internal static class PatchTransitionCopyPaste
    {
        static AnimatorStateTransition[] _clipboard;
        static AnimatorState _pasteSource;
        static FieldInfo _stateField;

        internal static bool PasteActive { get; private set; }
        internal static Rect PasteSourceRect { get; private set; }
        internal static bool HasClipboard => _clipboard != null && _clipboard.Length > 0;
        internal static int ClipboardCount => _clipboard?.Length ?? 0;

        /* Stores the given transitions as the copy clipboard for later paste. */
        internal static void SetClipboard(AnimatorStateTransition[] transitions) =>
            _clipboard = transitions;

        /* Activates paste mode, recording the source state and its node rect for preview line drawing. */
        internal static void BeginPaste(AnimatorState source, Rect sourceRect)
        {
            PasteActive = true;
            _pasteSource = source;
            PasteSourceRect = sourceRect;
            PatchStateChainTransition.SnapTarget = null;
        }

        internal static void ClearPaste()
        {
            PasteActive = false;
            _pasteSource = null;
            PasteSourceRect = Rect.zero;
            PatchStateChainTransition.SnapTarget = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateNode"),
                "NodeUI",
                new[] { AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI") });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            if (!PasteActive) return;
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || currentEvent.clickCount != 1) return;

                _stateField ??= AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
                var destinationState = _stateField?.GetValue(__instance) as AnimatorState;
                if (destinationState == null || destinationState == _pasteSource) return;

                Undo.RegisterCompleteObjectUndo(_pasteSource, "Paste Transitions");
                foreach (var template in _clipboard)
                {
                    var newTransition = _pasteSource.AddTransition(destinationState);
                    CopyTransitionSettings(newTransition, template);
                }
                EditorUtility.SetDirty(_pasteSource);
                ClearPaste();
                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Paste transitions error: {e}");
            }
        }

        /* Copies all timing, interruption, flag, and condition settings from sourceTransition to destinationTransition. */
        static void CopyTransitionSettings(AnimatorStateTransition destinationTransition, AnimatorStateTransition sourceTransition)
        {
            destinationTransition.hasExitTime          = sourceTransition.hasExitTime;
            destinationTransition.exitTime             = sourceTransition.exitTime;
            destinationTransition.duration             = sourceTransition.duration;
            destinationTransition.offset               = sourceTransition.offset;
            destinationTransition.interruptionSource   = sourceTransition.interruptionSource;
            destinationTransition.orderedInterruption  = sourceTransition.orderedInterruption;
            destinationTransition.canTransitionToSelf  = sourceTransition.canTransitionToSelf;
            destinationTransition.conditions           = sourceTransition.conditions;
        }
    }
}
#endif
