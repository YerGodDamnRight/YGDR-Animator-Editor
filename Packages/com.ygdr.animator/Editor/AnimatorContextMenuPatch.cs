#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using HarmonyLib;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    // Patches StateNode, AnyStateNode, EntryNode, ExitNode — adds Pack and Delete Transitions.
    [HarmonyPatch]
    internal static class PatchStateNodeMenu
    {
        // Behavior clipboard.
        static Type _copiedBehaviorType;
        static readonly List<string> _copiedBehaviorJsons = new List<string>();
        static readonly (string label, Type type)[] _behaviorTypes =
        {
            ("Param Drivers", typeof(VRCAvatarParameterDriver)),
            ("Audio",         typeof(VRCAnimatorPlayAudio)),
            ("Tracking",      typeof(VRCAnimatorTrackingControl)),
        };

        // Step-1 state for two-phase operations.
        internal static AnimatorState[] _multiTransitionSources;
        internal static AnimatorStateMachine _multiTransitionSM;
        internal static AnimatorStateTransition[] _redirectTransitions;
        internal static AnimatorStateMachine _redirectSM;
        internal static AnimatorStateTransition[] _replicateTransitions;
        internal static AnimatorStateMachine _replicateSM;

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => new[]
        {
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI"),
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "NodeUI"),
            AccessTools.Method(AnimatorEditorInit.EntryNodeType, "NodeUI"),
            AccessTools.Method(AnimatorEditorInit.ExitNodeType, "NodeUI"),
        };

        /* Entry point for short NodeUI methods: builds the state node context menu from scratch and shows it.
           Receives the graph object via Ldarg_1 injection from the NodeUI IL. */
        internal static void CreateAndDisplay(object graph)
        {
            if (Event.current.type != EventType.ContextClick) return;
            var menu = new GenericMenu();
            AddMenuItems(graph, menu);
            if (menu.GetItemCount() == 0) return;
            menu.ShowAsContext();
            Event.current.Use();
        }

        /* Appends state-node context menu items to an existing GenericMenu based on current selection.
           Injected before ShowAsContext() in longer NodeUI methods via Ldarg_0 (the node instance). */
        internal static void AddMenuItems(object node, GenericMenu menu)
        {
            try
            {
                var graph = ResolveGraph(node);
                if (graph == null) return;

                var activeSM = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null)
                    as AnimatorStateMachine;
                if (activeSM == null) return;

                var selectedStates = Selection.objects
                    .Where(static x => x is AnimatorState)
                    .Cast<AnimatorState>()
                    .ToArray();

                if (menu.GetItemCount() > 0) menu.AddSeparator("");

                if (selectedStates.Length > 0)
                {
                    var capturedSM = activeSM;
                    var capturedStates = selectedStates;
                    bool loopOn = capturedStates
                        .SelectMany(state => CollectClips(state.motion))
                        .All(clip => AnimationUtility.GetAnimationClipSettings(clip).loopTime);
                    menu.AddItem(
                        new GUIContent("Looptime"),
                        loopOn,
                        static data =>
                        {
                            var (states, on) = ((AnimatorState[], bool))data;
                            SetClipLoopTime(states, !on);
                        },
                        (capturedStates, loopOn));
                    menu.AddItem(
                        new GUIContent("Pack into Sub-State Machine"),
                        false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorState[]))data;
                            AnimatorPackUnpack.Pack(pair.Item1, pair.Item2);
                        },
                        (capturedSM, capturedStates));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Pack into Sub-State Machine (select states first)"));
                }

                if (selectedStates.Length == 1)
                {
                    var capturedSM = activeSM;
                    var capturedState = selectedStates[0];
                    menu.AddItem(
                        new GUIContent("Select Transitions/Incoming"),
                        false,
                        static data =>
                        {
                            var (sm, state) = ((AnimatorStateMachine, AnimatorState))data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectIncomingTransitions(controller, new[] { state });
                        },
                        (capturedSM, capturedState));
                    menu.AddItem(
                        new GUIContent("Select Transitions/Outgoing"),
                        false,
                        static data => AnimationEditorWindow.SelectOutgoingTransitions(new[] { (AnimatorState)data }),
                        capturedState);
                }

                // Copy Behaviors (single state)
                if (selectedStates.Length == 1)
                {
                    var copyState = selectedStates[0];
                    foreach (var (label, type) in _behaviorTypes)
                    {
                        if (copyState.behaviours.Any(b => b.GetType() == type))
                            menu.AddItem(new GUIContent($"Copy Behaviors/{label}"), false,
                                static data => { var (t, s) = ((Type, AnimatorState))data; CopyBehavior(s, t); },
                                (type, copyState));
                        else
                            menu.AddDisabledItem(new GUIContent($"Copy Behaviors/{label}"));
                    }
                }

                // Paste Behaviors
                if (_copiedBehaviorType != null && selectedStates.Length > 0)
                {
                    var match = _behaviorTypes.FirstOrDefault(x => x.type == _copiedBehaviorType);
                    var typeName = match.label ?? _copiedBehaviorType.Name;
                    menu.AddItem(new GUIContent($"Paste Behaviors ({typeName})"), false,
                        static data => PasteBehaviors((AnimatorState[])data),
                        selectedStates);
                }

                var selectedTransitions = Selection.objects
                    .Where(static x => x is AnimatorStateTransition)
                    .Cast<AnimatorStateTransition>()
                    .ToArray();

                if (selectedTransitions.Length >= 1)
                {
                    var capturedSM = activeSM;
                    var capturedTransitions = selectedTransitions;
                    menu.AddItem(
                        new GUIContent("Reverse Transitions"),
                        false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorStateTransition[]))data;
                            AnimatorLayerOps.ReverseNegateTransitions(pair.Item1, pair.Item2);
                        },
                        (capturedSM, capturedTransitions));
                }

                menu.AddSeparator("");

                // Multi Transition
                if (_multiTransitionSources == null)
                {
                    if (selectedStates.Length > 0)
                        menu.AddItem(new GUIContent("Multi Transition"), false,
                            static data =>
                            {
                                var (states, sm) = ((AnimatorState[], AnimatorStateMachine))data;
                                _multiTransitionSources = states;
                                _multiTransitionSM = sm;
                            },
                            (selectedStates, activeSM));
                    else
                        menu.AddDisabledItem(new GUIContent("Multi Transition (select source states first)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Multi Transition"), true,
                        static data =>
                        {
                            var dests = (AnimatorState[])data;
                            var sources = _multiTransitionSources;
                            var sm = _multiTransitionSM;
                            _multiTransitionSources = null;
                            _multiTransitionSM = null;
                            if (dests.Length > 0)
                                AnimatorLayerOps.MultiTransition(sm, sources, dests);
                        },
                        selectedStates);
                }

                // Redirect Transitions
                if (_redirectTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent("Redirect Transitions"), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                _redirectTransitions = transitions;
                                _redirectSM = sm;
                            },
                            (selectedTransitions, activeSM));
                    else
                        menu.AddDisabledItem(new GUIContent("Redirect Transitions (select transitions first)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Redirect Transitions"), true,
                        static data =>
                        {
                            var dests = (AnimatorState[])data;
                            var transitions = _redirectTransitions;
                            var sm = _redirectSM;
                            _redirectTransitions = null;
                            _redirectSM = null;
                            if (dests.Length > 0)
                                AnimatorLayerOps.RedirectTransitions(sm, transitions, dests);
                        },
                        selectedStates);
                }

                // Replicate Transitions
                if (_replicateTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent("Replicate Transitions"), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                _replicateTransitions = transitions;
                                _replicateSM = sm;
                            },
                            (selectedTransitions, activeSM));
                    else
                        menu.AddDisabledItem(new GUIContent("Replicate Transitions (select transitions first)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Replicate Transitions"), true,
                        static data =>
                        {
                            var newSourceStates = (AnimatorState[])data;
                            var transitions = _replicateTransitions;
                            var sm = _replicateSM;
                            _replicateTransitions = null;
                            _replicateSM = null;
                            if (newSourceStates.Length > 0)
                                AnimatorLayerOps.ReplicateTransitions(sm, transitions, newSourceStates);
                        },
                        selectedStates);
                }

            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] State menu error: {e}.");
            }
        }

        /* Snapshots all behaviours of the given type from state into the JSON clipboard for later paste. */
        static void CopyBehavior(AnimatorState state, Type type)
        {
            _copiedBehaviorType = type;
            _copiedBehaviorJsons.Clear();
            foreach (var b in state.behaviours.Where(b => b.GetType() == type))
                _copiedBehaviorJsons.Add(EditorJsonUtility.ToJson(b));
        }

        /* Replaces all behaviours of the clipboard type on each state with JSON-deserialized copies of the clipboard data. */
        static void PasteBehaviors(AnimatorState[] states)
        {
            if (_copiedBehaviorType == null || _copiedBehaviorJsons.Count == 0) return;
            foreach (var state in states)
            {
                var existing = state.behaviours.Where(b => b.GetType() == _copiedBehaviorType).ToArray();
                Undo.RegisterCompleteObjectUndo(state, "Paste Behaviors");
                state.behaviours = state.behaviours.Where(b => b.GetType() != _copiedBehaviorType).ToArray();
                foreach (var b in existing) Undo.DestroyObjectImmediate(b);
                foreach (var json in _copiedBehaviorJsons)
                {
                    var newBehavior = state.AddStateMachineBehaviour(_copiedBehaviorType);
                    Undo.RegisterCreatedObjectUndo(newBehavior, "Paste Behaviors");
                    EditorJsonUtility.FromJsonOverwrite(json, newBehavior);
                    EditorUtility.SetDirty(newBehavior);
                }
                EditorUtility.SetDirty(state);
            }
        }

        /* Sets loop time on all animation clips referenced by the given states, recursing into blend trees. */
        static void SetClipLoopTime(AnimatorState[] states, bool loop)
        {
            foreach (var state in states)
                foreach (var clip in CollectClips(state.motion))
                {
                    Undo.RecordObject(clip, loop ? "Set Loop Time On" : "Set Loop Time Off");
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = loop;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                }
        }

        /* Recursively yields all AnimationClips reachable from a Motion, descending into BlendTree children. */
        static IEnumerable<AnimationClip> CollectClips(Motion motion)
        {
            if (motion is AnimationClip clip) { yield return clip; yield break; }
            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    foreach (var c in CollectClips(child.motion))
                        yield return c;
        }

        /* Returns the graph object: calls get_graph() if the input has that method, otherwise treats it as the graph itself. */
        static object ResolveGraph(object nodeOrGraph)
        {
            var type = nodeOrGraph?.GetType();
            if (type == null) return null;
            var getGraph = AccessTools.Method(type, "get_graph");
            return getGraph != null ? getGraph.Invoke(nodeOrGraph, null) : nodeOrGraph;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => MenuTranspilerHelper.Inject(
                instructions,
                AccessTools.Method(typeof(PatchStateNodeMenu), "AddMenuItems"),
                AccessTools.Method(typeof(PatchStateNodeMenu), "CreateAndDisplay"));
    }

    // Patches StateMachineNode — adds Unpack and Delete Transitions.
    [HarmonyPatch]
    internal static class PatchStateMachineNodeMenu
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => new[]
        {
            AccessTools.Method(AnimatorEditorInit.StateMachineNodeType, "NodeUI"),
        };

        /* Entry point for short StateMachineNode NodeUI methods: builds and shows the sub-state machine context menu. */
        internal static void CreateAndDisplay(object graph)
        {
            if (Event.current.type != EventType.ContextClick) return;
            var menu = new GenericMenu();
            AddMenuItems(graph, menu);
            if (menu.GetItemCount() == 0) return;
            menu.ShowAsContext();
            Event.current.Use();
        }

        /* Appends Unpack to an existing GenericMenu when the selected object is a direct child sub state machine. */
        internal static void AddMenuItems(object node, GenericMenu menu)
        {
            try
            {
                var type = node?.GetType();
                if (type == null) return;

                // Resolve the graph: if node has get_graph(), call it; otherwise node IS the graph.
                var getGraph = AccessTools.Method(type, "get_graph");
                var graph = getGraph != null ? getGraph.Invoke(node, null) : node;
                if (graph == null) return;

                var activeSM = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null)
                    as AnimatorStateMachine;
                if (activeSM == null) return;

                // Find which child sub-state machine is selected via ChildAnimatorStateMachine.
                var subStateMachine = Selection.activeObject as AnimatorStateMachine;
                var isDirectChild = subStateMachine != null &&
                    activeSM.stateMachines.Any(x => x.stateMachine == subStateMachine);

                if (menu.GetItemCount() > 0) menu.AddSeparator("");

                if (isDirectChild)
                {
                    var capturedParent = activeSM;
                    var capturedSub = subStateMachine;
                    menu.AddItem(
                        new GUIContent("Unpack Sub State Machine"),
                        false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorStateMachine))data;
                            AnimatorPackUnpack.Unpack(pair.Item1, pair.Item2);
                        },
                        (capturedParent, capturedSub));
                }

            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub state machine menu error: {e}.");
            }
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();

            // Short methods have no existing GenericMenu — inject CreateAndDisplay at the top.
            // Use Ldarg_0 (this = the StateMachineNode) so AddMenuItems can resolve the subSM.
            if (list.Count < 30)
            {
                list.Insert(0, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(1, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(PatchStateMachineNodeMenu), "CreateAndDisplay")));
                list.Insert(2, new CodeInstruction(OpCodes.Nop));
                return list;
            }

            // Longer methods already have a GenericMenu — inject AddMenuItems before ShowAsContext.
            return MenuTranspilerHelper.Inject(
                list,
                AccessTools.Method(typeof(PatchStateMachineNodeMenu), "AddMenuItems"),
                AccessTools.Method(typeof(PatchStateMachineNodeMenu), "CreateAndDisplay"));
        }
    }


    // Patches GraphGUI.HandleContextMenu — appends our items to Unity/RATS menu.
    // Priority.VeryLow runs after RATS (Priority.Low) so our transpiler sees RATS's menu.
    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class PatchTransitionContextMenu
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
                "HandleContextMenu");

        /* Appends transition operation items (Reverse, Redirect, Replicate, Delete All) to the HandleContextMenu GenericMenu.
           Receives the menu from the IL stack and the GraphGUI instance via Ldarg_0. */
        internal static GenericMenu AddItems(GenericMenu menu, object graphGUI)
        {
            try
            {
                var getActiveStateMachine = AccessTools.Method(graphGUI.GetType(), "get_activeStateMachine");
                var activeStateMachine = getActiveStateMachine?.Invoke(graphGUI, null) as AnimatorStateMachine;
                if (activeStateMachine == null) return menu;

                var selectedTransitions = Selection.objects
                    .Where(static x => x is AnimatorStateTransition)
                    .Cast<AnimatorStateTransition>()
                    .ToArray();
                var selectedStates = Selection.objects
                    .Where(static x => x is AnimatorState)
                    .Cast<AnimatorState>()
                    .ToArray();

                menu.AddSeparator("");

                if (selectedTransitions.Length > 0)
                {
                    var capturedSM = activeStateMachine;
                    var capturedTransitions = selectedTransitions;
                    menu.AddItem(new GUIContent("Reverse Transitions"), false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorStateTransition[]))data;
                            AnimatorLayerOps.ReverseNegateTransitions(pair.Item1, pair.Item2);
                        },
                        (capturedSM, capturedTransitions));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Reverse Transitions (select transitions first)"));
                }

                // Redirect Transitions
                if (PatchStateNodeMenu._redirectTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent("Redirect Transitions"), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu._redirectTransitions = transitions;
                                PatchStateNodeMenu._redirectSM = sm;
                            },
                            (selectedTransitions, activeStateMachine));
                    else
                        menu.AddDisabledItem(new GUIContent("Redirect Transitions (select transitions first)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Redirect Transitions"), true,
                        static data =>
                        {
                            var dests = (AnimatorState[])data;
                            var transitions = PatchStateNodeMenu._redirectTransitions;
                            var sm = PatchStateNodeMenu._redirectSM;
                            PatchStateNodeMenu._redirectTransitions = null;
                            PatchStateNodeMenu._redirectSM = null;
                            if (dests.Length > 0)
                                AnimatorLayerOps.RedirectTransitions(sm, transitions, dests);
                        },
                        selectedStates);
                }

                // Replicate Transitions
                if (PatchStateNodeMenu._replicateTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent("Replicate Transitions"), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu._replicateTransitions = transitions;
                                PatchStateNodeMenu._replicateSM = sm;
                            },
                            (selectedTransitions, activeStateMachine));
                    else
                        menu.AddDisabledItem(new GUIContent("Replicate Transitions (select transitions first)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Replicate Transitions"), true,
                        static data =>
                        {
                            var newSourceStates = (AnimatorState[])data;
                            var transitions = PatchStateNodeMenu._replicateTransitions;
                            var sm = PatchStateNodeMenu._replicateSM;
                            PatchStateNodeMenu._replicateTransitions = null;
                            PatchStateNodeMenu._replicateSM = null;
                            if (newSourceStates.Length > 0)
                                AnimatorLayerOps.ReplicateTransitions(sm, transitions, newSourceStates);
                        },
                        selectedStates);
                }

                menu.AddItem(
                    new GUIContent("Delete All Transitions in Layer"),
                    false,
                    static data => AnimatorLayerOps.DeleteAllTransitions((AnimatorStateMachine)data),
                    activeStateMachine);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Transition menu error: {e}.");
            }

            return menu;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var showAsContext = AccessTools.Method(typeof(GenericMenu), "ShowAsContext");
            var addItems = AccessTools.Method(typeof(PatchTransitionContextMenu), "AddItems");

            for (int i = 0; i < list.Count; i++)
            {
                var opcode = list[i].opcode;
                if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) &&
                    list[i].operand as MethodInfo == showAsContext)
                {
                    list.InsertRange(i, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, addItems),
                    });
                    break;
                }
            }

            return list;
        }
    }

    // Shared IL injection logic for both patch classes.
    internal static class MenuTranspilerHelper
    {
        /* Injects CreateAndDisplay at the top of short NodeUI methods, or AddMenuItems before ShowAsContext in longer ones.
           Short vs. long is determined by whether the method has more than 30 IL instructions. */
        internal static IEnumerable<CodeInstruction> Inject(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo addMenuItemsMethod,
            MethodInfo createAndDisplayMethod)
        {
            var list = instructions.ToList();

            // Short methods: no existing GenericMenu — inject CreateAndDisplay at the top.
            if (list.Count < 30)
            {
                list.Insert(0, new CodeInstruction(OpCodes.Ldarg_1));
                list.Insert(1, new CodeInstruction(OpCodes.Call, createAndDisplayMethod));
                list.Insert(2, new CodeInstruction(OpCodes.Nop));
                return list;
            }

            // Longer methods: find the GenericMenu local, inject AddMenuItems before ShowAsContext.
            int menuLocalIndex = -1;
            LocalBuilder menuLocalBuilder = null;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Newobj &&
                    (ConstructorInfo)list[i].operand == AccessTools.Constructor(typeof(GenericMenu), Type.EmptyTypes))
                {
                    if (i + 1 < list.Count)
                    {
                        var next = list[i + 1];
                        if (next.opcode == OpCodes.Stloc_1) menuLocalIndex = 1;
                        else if (next.opcode == OpCodes.Stloc_2) menuLocalIndex = 2;
                        else if (next.opcode == OpCodes.Stloc_3) menuLocalIndex = 3;
                        else if (next.opcode == OpCodes.Stloc_S)
                        {
                            menuLocalBuilder = (LocalBuilder)next.operand;
                            menuLocalIndex = menuLocalBuilder.LocalIndex;
                        }
                    }
                }

                if (list[i].opcode == OpCodes.Callvirt &&
                    (MethodInfo)list[i].operand == AccessTools.Method(typeof(GenericMenu), "ShowAsContext"))
                {
                    if (menuLocalIndex < 0) break;

                    // Use the specific short-form opcode for indices 1-3, Ldloc_S with LocalBuilder for higher.
                    CodeInstruction loadMenu = menuLocalIndex switch
                    {
                        1 => new CodeInstruction(OpCodes.Ldloc_1),
                        2 => new CodeInstruction(OpCodes.Ldloc_2),
                        3 => new CodeInstruction(OpCodes.Ldloc_3),
                        _ => new CodeInstruction(OpCodes.Ldloc_S, menuLocalBuilder),
                    };

                    list.InsertRange(i, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        loadMenu,
                        new CodeInstruction(OpCodes.Call, addMenuItemsMethod),
                    });
                    break;
                }
            }

            return list;
        }
    }
}
#endif
