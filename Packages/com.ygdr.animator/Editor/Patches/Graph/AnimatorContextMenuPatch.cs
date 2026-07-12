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
using System.Reflection.Emit;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using HarmonyLib;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#endif

namespace YGDR.Editor.Animation
{
    // Patches StateNode, AnyStateNode, EntryNode, ExitNode — adds Pack and Delete Transitions.
    [HarmonyPatch]
    internal static class PatchStateNodeMenu
    {
        // Behavior clipboard. _copiedInstanceName == null means "all instances of type";
        // otherwise the clipboard holds exactly one named instance (upsert-by-name on paste).
        static Type _copiedBehaviorType;
        static string _copiedInstanceName;
        static readonly List<string> _copiedBehaviorJsons = new List<string>();
#if VRC_SDK_VRCSDK3
        static readonly (string label, Type type)[] _behaviorTypes =
        {
            ("Param Drivers", typeof(VRCAvatarParameterDriver)),
            ("Audio",         typeof(VRCAnimatorPlayAudio)),
            ("Tracking",      typeof(VRCAnimatorTrackingControl)),
            ("Layer Control", typeof(VRCAnimatorLayerControl)),
            ("Locomotion",    typeof(VRCAnimatorLocomotionControl)),
            ("Pose Space",    typeof(VRCAnimatorTemporaryPoseSpace)),
            ("Playable Layer",typeof(VRCPlayableLayerControl)),
        };
#else
        static readonly (string label, Type type)[] _behaviorTypes = System.Array.Empty<(string, Type)>();
#endif

        // Step-1 state for two-phase operations.
        internal static AnimatorState[] _multiTransitionSources;
        internal static AnimatorStateMachine _multiTransitionSM;
        internal static AnimatorStateTransition[] _redirectTransitions;
        internal static AnimatorStateMachine _redirectSM;
        internal static AnimatorStateTransition[] _replicateTransitions;
        internal static AnimatorStateMachine _replicateSM;
        internal static bool _multiTransitionFromAnyState;
        internal static bool _multiTransitionFromEntry;
        internal static AnimatorTransition[] _redirectEntryTransitions;
        internal static AnimatorStateMachine _redirectEntrySM;
        internal static AnimatorTransition[] _replicateEntryTransitions;
        internal static AnimatorStateMachine _replicateEntrySM;

        internal static void CancelPending()
        {
            _multiTransitionSources      = null;
            _multiTransitionSM           = null;
            _multiTransitionFromAnyState = false;
            _multiTransitionFromEntry    = false;
            _redirectTransitions         = null;
            _redirectSM                  = null;
            _replicateTransitions        = null;
            _replicateSM                 = null;
            _redirectEntryTransitions    = null;
            _redirectEntrySM             = null;
            _replicateEntryTransitions   = null;
            _replicateEntrySM            = null;
        }

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
            try
            {
                if (Event.current.type != EventType.ContextClick) return;
                var menu = new GenericMenu();
                AddMenuItems(graph, menu);
                if (menu.GetItemCount() == 0) return;
                menu.ShowAsContext();
                Event.current.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] State node context menu error: {e}");
            }
        }

        /* Appends state-node context menu items to an existing GenericMenu based on current selection.
           Injected before ShowAsContext() in longer NodeUI methods via Ldarg_0 (the node instance). */
        internal static void AddMenuItems(object node, GenericMenu menu)
        {
            try
            {
                var graph = ResolveGraph(node);
                if (graph == null) return;

                if (AnimatorEditorInit.GetActiveStateMachineMethod == null)
                {
                    Debug.LogError("[AnimatorTools] GetActiveStateMachineMethod is null — Unity internal API may have changed. State node context menu unavailable.");
                    return;
                }

                var activeSM = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null)
                    as AnimatorStateMachine;
                if (activeSM == null) return;

                var selectedStates = Selection.objects
                    .Where(static x => x is AnimatorState)
                    .Cast<AnimatorState>()
                    .ToArray();

                var selectedTransitions = Selection.objects
                    .Where(static x => x is AnimatorStateTransition)
                    .Cast<AnimatorStateTransition>()
                    .ToArray();

                bool isAnyStateSelected = (AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(node) ?? false)
                                       || Selection.objects.Any(o => AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(o) ?? false);
                bool isExitSelected    = (AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(node) ?? false)
                                       || Selection.objects.Any(o => AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(o) ?? false);
                bool isEntrySelected   = (AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(node) ?? false)
                                       || Selection.objects.Any(o => AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(o) ?? false);

                if (menu.GetItemCount() > 0) menu.AddSeparator("");

                // State ops — only when states selected
                if (selectedStates.Length > 0)
                {
                    var capturedSM = activeSM;
                    var capturedStates = selectedStates;
                    bool loopOn = capturedStates
                        .SelectMany(state => CollectClips(state.motion))
                        .All(clip => AnimationUtility.GetAnimationClipSettings(clip).loopTime);
                    menu.AddItem(
                        new GUIContent(L10n.Get("context_menu.looptime")),
                        loopOn,
                        static data =>
                        {
                            var (states, on) = ((AnimatorState[], bool))data;
                            SetClipLoopTime(states, !on);
                        },
                        (capturedStates, loopOn));
                    menu.AddItem(
                        new GUIContent(L10n.Get("context_menu.pack_subsm")),
                        false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorState[]))data;
                            AnimatorPackUnpack.Pack(pair.Item1, pair.Item2);
                        },
                        (capturedSM, capturedStates));
                    menu.AddItem(
                        new GUIContent($"{L10n.Get("context_menu.select_transitions")}/{L10n.Get("context_menu.select_incoming")}"),
                        false,
                        static data =>
                        {
                            var (sm, states) = ((AnimatorStateMachine, AnimatorState[]))data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectIncomingTransitions(controller, states);
                        },
                        (capturedSM, capturedStates));
                    menu.AddItem(
                        new GUIContent($"{L10n.Get("context_menu.select_transitions")}/{L10n.Get("context_menu.select_outgoing")}"),
                        false,
                        static data => AnimationEditorWindow.SelectOutgoingTransitions((AnimatorState[])data),
                        capturedStates);
                    menu.AddItem(
                        new GUIContent($"{L10n.Get("context_menu.select_transitions")}/{L10n.Get("context_menu.select_both")}"),
                        false,
                        static data =>
                        {
                            var (sm, states) = ((AnimatorStateMachine, AnimatorState[]))data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectBothTransitions(controller, states);
                        },
                        (capturedSM, capturedStates));
                }

                if (isAnyStateSelected)
                {
                    var capturedSM = activeSM;
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.select_outgoing_all")), false,
                        static data =>
                        {
                            var sm = (AnimatorStateMachine)data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectOutgoingFromAnyState(controller);
                        },
                        capturedSM);
                }

                if (isEntrySelected)
                {
                    var capturedSM = activeSM;
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.select_outgoing_all")), false,
                        static data =>
                        {
                            var sm = (AnimatorStateMachine)data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectOutgoingFromEntry(controller);
                        },
                        capturedSM);
                }

                if (isExitSelected)
                {
                    var capturedSM = activeSM;
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.select_incoming_all")), false,
                        static data =>
                        {
                            var sm = (AnimatorStateMachine)data;
                            var path = AssetDatabase.GetAssetPath(sm);
                            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                            AnimationEditorWindow.SelectIncomingToExit(controller);
                        },
                        capturedSM);
                }

                // Behaviors — only when states selected
                if (selectedStates.Length == 1)
                {
                    var copyState = selectedStates[0];
                    foreach (var (label, type) in _behaviorTypes)
                    {
                        var instances = copyState.behaviours.Where(b => b.GetType() == type).ToArray();
                        if (instances.Length == 0) continue;

                        if (instances.Length == 1)
                        {
                            menu.AddItem(new GUIContent($"{L10n.Get("context_menu.copy_behaviors")}/{label}"), false,
                                static data => { var (t, s) = ((Type, AnimatorState))data; CopyBehavior(s, t); },
                                (type, copyState));
                        }
                        else
                        {
                            foreach (var instance in instances)
                                menu.AddItem(new GUIContent($"{L10n.Get("context_menu.copy_behaviors")}/{label}/{instance.name}"), false,
                                    static data => { var (t, b) = ((Type, StateMachineBehaviour))data; CopyBehaviorInstance(t, b); },
                                    (type, instance));

                            menu.AddItem(new GUIContent($"{L10n.Get("context_menu.copy_behaviors")}/{label}/{L10n.Get("context_menu.all_instances")}"), false,
                                static data => { var (t, s) = ((Type, AnimatorState))data; CopyBehavior(s, t); },
                                (type, copyState));
                        }
                    }
                }

                if (_copiedBehaviorType != null && selectedStates.Length > 0)
                {
                    var match = _behaviorTypes.FirstOrDefault(x => x.type == _copiedBehaviorType);
                    var typeName = match.label ?? _copiedBehaviorType.Name;
                    var pasteLabel = _copiedInstanceName != null
                        ? $"{L10n.Get("context_menu.paste_behaviors")} ({typeName} — {_copiedInstanceName})"
                        : $"{L10n.Get("context_menu.paste_behaviors")} ({typeName})";
                    menu.AddItem(new GUIContent(pasteLabel), false,
                        static data => PasteBehaviors((AnimatorState[])data),
                        selectedStates);
                }

                // Multi Transition — only when states/AnyState/Entry selected or phase-2 active
                bool showMultiTransition = selectedStates.Length > 0 || isAnyStateSelected || isEntrySelected || _multiTransitionSources != null;
                if (showMultiTransition)
                {

                    if (_multiTransitionSources == null)
                    {
                        if (selectedStates.Length > 0)
                            menu.AddItem(new GUIContent(L10n.Get("context_menu.multi_transition")), false,
                                static data =>
                                {
                                    var (states, sm) = ((AnimatorState[], AnimatorStateMachine))data;
                                    _redirectTransitions = null;
                                    _redirectSM = null;
                                    _replicateTransitions = null;
                                    _replicateSM = null;
                                    _multiTransitionFromAnyState = false;
                                    _multiTransitionSources = states;
                                    _multiTransitionSM = sm;
                                },
                                (selectedStates, activeSM));
                        else if (isAnyStateSelected)
                            menu.AddItem(new GUIContent(L10n.Get("context_menu.multi_transition")), false,
                                static data =>
                                {
                                    var sm = (AnimatorStateMachine)data;
                                    _redirectTransitions = null;
                                    _redirectSM = null;
                                    _replicateTransitions = null;
                                    _replicateSM = null;
                                    _multiTransitionFromAnyState = true;
                                    _multiTransitionFromEntry = false;
                                    _multiTransitionSources = System.Array.Empty<AnimatorState>();
                                    _multiTransitionSM = sm;
                                },
                                activeSM);
                        else if (isEntrySelected)
                            menu.AddItem(new GUIContent(L10n.Get("context_menu.multi_transition")), false,
                                static data =>
                                {
                                    var sm = (AnimatorStateMachine)data;
                                    _redirectTransitions = null;
                                    _redirectSM = null;
                                    _replicateTransitions = null;
                                    _replicateSM = null;
                                    _multiTransitionFromAnyState = false;
                                    _multiTransitionFromEntry = true;
                                    _multiTransitionSources = System.Array.Empty<AnimatorState>();
                                    _multiTransitionSM = sm;
                                },
                                activeSM);
                    }
                    else if (_multiTransitionFromAnyState && isExitSelected)
                    {
                        menu.AddDisabledItem(new GUIContent($"{L10n.Get("context_menu.multi_transition")} (AnyState cannot target Exit)"));
                    }
                    else if (_multiTransitionFromEntry && isExitSelected)
                    {
                        menu.AddDisabledItem(new GUIContent($"{L10n.Get("context_menu.multi_transition")} (Entry cannot target Exit)"));
                    }
                    else
                    {
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.multi_transition")), true,
                            static data =>
                            {
                                var (dests, toExit, fromAnyState, fromEntry) = ((AnimatorState[], bool, bool, bool))data;
                                var sources = _multiTransitionSources;
                                var sm = _multiTransitionSM;
                                _multiTransitionSources = null;
                                _multiTransitionSM = null;
                                _multiTransitionFromAnyState = false;
                                _multiTransitionFromEntry = false;
                                if (toExit && !fromAnyState && !fromEntry)
                                    AnimatorBulkTransitionOps.MultiTransitionToExit(sm, sources);
                                else if (fromAnyState && dests.Length > 0)
                                    AnimatorBulkTransitionOps.MultiTransitionFromAnyState(sm, dests);
                                else if (fromEntry && dests.Length > 0)
                                    AnimatorBulkTransitionOps.MultiTransitionFromEntry(sm, dests);
                                else if (dests.Length > 0)
                                    AnimatorBulkTransitionOps.MultiTransition(sm, sources, dests);
                            },
                            (selectedStates, isExitSelected, _multiTransitionFromAnyState, _multiTransitionFromEntry));
                    }
                }

                // Transition ops — only when transitions selected or phase-2 active
                bool hasTransitionOps = selectedTransitions.Length > 0 || _redirectTransitions != null || _replicateTransitions != null || _redirectEntryTransitions != null || _replicateEntryTransitions != null;
                if (hasTransitionOps)
                {
                    menu.AddSeparator("");

                    if (selectedTransitions.Length > 0)
                    {
                        var capturedSM = activeSM;
                        var capturedTransitions = selectedTransitions;
                        menu.AddItem(
                            new GUIContent(L10n.Get("context_menu.reverse_transitions")),
                            false,
                            static data =>
                            {
                                var pair = ((AnimatorStateMachine, AnimatorStateTransition[]))data;
                                AnimatorBulkTransitionOps.ReverseNegateTransitions(pair.Item1, pair.Item2);
                            },
                            (capturedSM, capturedTransitions));
                    }

                    if (_redirectTransitions == null)
                    {
                        if (selectedTransitions.Length > 0)
                            menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), false,
                                static data =>
                                {
                                    var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                    _multiTransitionSources = null;
                                    _multiTransitionSM = null;
                                    _replicateTransitions = null;
                                    _replicateSM = null;
                                    _redirectTransitions = transitions;
                                    _redirectSM = sm;
                                },
                                (selectedTransitions, activeSM));
                    }
                    else
                    {
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), true,
                            static data =>
                            {
                                var (dests, toExit) = ((AnimatorState[], bool))data;
                                var transitions = _redirectTransitions;
                                var sm = _redirectSM;
                                _redirectTransitions = null;
                                _redirectSM = null;
                                if (toExit)
                                    AnimatorBulkTransitionOps.RedirectTransitionsToExit(sm, transitions);
                                else if (dests.Length > 0)
                                    AnimatorBulkTransitionOps.RedirectTransitions(sm, transitions, dests);
                            },
                            (selectedStates, isExitSelected));
                    }

                    if (_replicateTransitions == null && _replicateEntryTransitions == null)
                    {
                        if (selectedTransitions.Length > 0)
                            menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), false,
                                static data =>
                                {
                                    var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                    _multiTransitionSources = null;
                                    _multiTransitionSM = null;
                                    _redirectTransitions = null;
                                    _redirectSM = null;
                                    _replicateTransitions = transitions;
                                    _replicateSM = sm;
                                },
                                (selectedTransitions, activeSM));
                    }
                    else if (_replicateTransitions != null)
                    {
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), true,
                            static data =>
                            {
                                var (newSourceStates, fromAnyState, fromEntry) = ((AnimatorState[], bool, bool))data;
                                var transitions = _replicateTransitions;
                                var sm = _replicateSM;
                                _replicateTransitions = null;
                                _replicateSM = null;
                                if (fromAnyState)
                                    AnimatorBulkTransitionOps.ReplicateTransitionsFromAnyState(sm, transitions);
                                else if (fromEntry)
                                    AnimatorBulkTransitionOps.ReplicateTransitionsFromEntry(sm, transitions);
                                else if (newSourceStates.Length > 0)
                                    AnimatorBulkTransitionOps.ReplicateTransitions(sm, transitions, newSourceStates);
                            },
                            (selectedStates, isAnyStateSelected, isEntrySelected));
                    }
                    else if (_replicateEntryTransitions != null)
                    {
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), true,
                            static data =>
                            {
                                var (newSourceStates, sm) = ((AnimatorState[], AnimatorStateMachine))data;
                                var templates = _replicateEntryTransitions;
                                _replicateEntryTransitions = null;
                                _replicateEntrySM = null;
                                if (newSourceStates.Length > 0)
                                    AnimatorBulkTransitionOps.ReplicateTransitionsFromEntryTransitions(sm, templates, newSourceStates);
                            },
                            (selectedStates, activeSM));
                    }

                    if (_redirectEntryTransitions != null)
                    {
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), true,
                            static data =>
                            {
                                var dests = (AnimatorState[])data;
                                var transitions = _redirectEntryTransitions;
                                var sm = _redirectEntrySM;
                                _redirectEntryTransitions = null;
                                _redirectEntrySM = null;
                                if (dests.Length > 0)
                                    AnimatorBulkTransitionOps.RedirectEntryTransitions(sm, transitions, dests);
                            },
                            selectedStates);
                    }
                }

                var colorTagSettings = AnimatorDefaultSettings.Load();
                if (colorTagSettings.colorTags.Count > 0 && (selectedStates.Length > 0 || selectedTransitions.Length > 0))
                {
                    menu.AddSeparator("");
                    var capturedTagStates = selectedStates;
                    var capturedTagTransitions = selectedTransitions;
                    foreach (var colorTag in colorTagSettings.colorTags)
                    {
                        var capturedTagName = colorTag.tagName;
                        bool allStatesChecked = capturedTagStates.Length == 0 || capturedTagStates.All(state => state.tag == capturedTagName);
                        bool allTransitionsChecked = capturedTagTransitions.Length == 0 || capturedTagTransitions.All(transition => transition.name == capturedTagName);
                        bool allChecked = allStatesChecked && allTransitionsChecked;
                        menu.AddItem(
                            new GUIContent($"{L10n.Get("context_menu.tag")}/{capturedTagName}"),
                            allChecked,
                            static data =>
                            {
                                var (states, transitions, tagName, wasChecked) = ((AnimatorState[], AnimatorStateTransition[], string, bool))data;
                                var allObjects = states.Cast<UnityEngine.Object>().Concat(transitions).ToArray();
                                Undo.RecordObjects(allObjects, "Apply Tag");
                                foreach (var state in states)
                                    state.tag = wasChecked ? "" : tagName;
                                foreach (var transition in transitions)
                                    transition.name = wasChecked ? "" : tagName;
                                foreach (var obj in allObjects)
                                    EditorUtility.SetDirty(obj);
                            },
                            (capturedTagStates, capturedTagTransitions, capturedTagName, allChecked));
                    }
                    menu.AddItem(
                        new GUIContent($"{L10n.Get("context_menu.tag")}/{L10n.Get("context_menu.remove_tags")}"),
                        false,
                        static data =>
                        {
                            var (states, transitions) = ((AnimatorState[], AnimatorStateTransition[]))data;
                            var allObjects = states.Cast<UnityEngine.Object>().Concat(transitions).ToArray();
                            Undo.RecordObjects(allObjects, "Remove Tag");
                            foreach (var state in states)
                                state.tag = "";
                            foreach (var transition in transitions)
                                transition.name = "";
                            foreach (var obj in allObjects)
                                EditorUtility.SetDirty(obj);
                        },
                        (capturedTagStates, capturedTagTransitions));
                }

            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] State menu error: {e}.");
            }
        }

        internal static AnimatorStateMachine ResolveRootStateMachine(AnimatorStateMachine activeSM)
        {
            if (activeSM == null) return null;
            var assetPath  = AssetDatabase.GetAssetPath(activeSM);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null) return activeSM;
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == activeSM) return layer.stateMachine;
                if (ContainsSubStateMachine(layer.stateMachine, activeSM)) return layer.stateMachine;
            }
            return activeSM;
        }

        static bool ContainsSubStateMachine(AnimatorStateMachine candidateRoot, AnimatorStateMachine target)
        {
            if (candidateRoot == null) return false;
            foreach (var childStateMachine in candidateRoot.stateMachines)
            {
                if (childStateMachine.stateMachine == target) return true;
                if (ContainsSubStateMachine(childStateMachine.stateMachine, target)) return true;
            }
            return false;
        }

        /* Snapshots all behaviours of the given type from state into the JSON clipboard for later paste ("all instances" mode). */
        static void CopyBehavior(AnimatorState state, Type type)
        {
            _copiedBehaviorType = type;
            _copiedInstanceName = null;
            _copiedBehaviorJsons.Clear();
            foreach (var b in state.behaviours.Where(b => b.GetType() == type))
                _copiedBehaviorJsons.Add(EditorJsonUtility.ToJson(b));
        }

        /* Snapshots a single named instance into the clipboard (named-instance mode — paste upserts by name only). */
        static void CopyBehaviorInstance(Type type, StateMachineBehaviour behaviour)
        {
            _copiedBehaviorType = type;
            _copiedInstanceName = behaviour.name;
            _copiedBehaviorJsons.Clear();
            _copiedBehaviorJsons.Add(EditorJsonUtility.ToJson(behaviour));
        }

        /* Pastes the clipboard onto each state. "All instances" mode replaces every behaviour of the clipboard
           type on the target; named-instance mode only touches the instance matching the copied name (updates
           it if present, otherwise adds it) — other instances of the same type on the target are left alone. */
        static void PasteBehaviors(AnimatorState[] states)
        {
            if (_copiedBehaviorType == null || _copiedBehaviorJsons.Count == 0) return;

            if (_copiedInstanceName != null)
            {
                foreach (var state in states)
                {
                    var existing = state.behaviours.FirstOrDefault(b => b.GetType() == _copiedBehaviorType && b.name == _copiedInstanceName);
                    if (existing != null)
                    {
                        PasteBehaviourValuesOnto(existing);
                        continue;
                    }

                    Undo.RegisterCompleteObjectUndo(state, "Paste Behavior Instance");
                    var newBehavior = state.AddStateMachineBehaviour(_copiedBehaviorType);
                    Undo.RegisterCreatedObjectUndo(newBehavior, "Paste Behavior Instance");
                    newBehavior.name = _copiedInstanceName;
                    PasteBehaviourValuesOnto(newBehavior);
                    EditorUtility.SetDirty(state);
                }
                return;
            }

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

        internal static void CopyBehaviourDirect(StateMachineBehaviour behaviour)
            => CopyBehaviorInstance(behaviour.GetType(), behaviour);

        /* Overwrites target's own values from the clipboard directly, regardless of name —
           used by the inspector CONTEXT menu where target is the exact component right-clicked. */
        internal static void PasteBehaviourValuesOnto(StateMachineBehaviour target)
        {
            if (target == null || _copiedBehaviorType == null || _copiedBehaviorJsons.Count == 0) return;
            if (target.GetType() != _copiedBehaviorType) return;

            var originalName = target.name;
            Undo.RecordObject(target, "Paste Behavior Values");
            EditorJsonUtility.FromJsonOverwrite(_copiedBehaviorJsons[0], target);
            target.name = originalName;
            EditorUtility.SetDirty(target);
        }

        internal static bool CanPaste(Type type) =>
            _copiedBehaviorType == type && _copiedBehaviorJsons.Count > 0;

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
        internal static object ResolveGraph(object nodeOrGraph)
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
            try
            {
                if (Event.current.type != EventType.ContextClick) return;
                var menu = new GenericMenu();
                AddMenuItems(graph, menu);
                if (menu.GetItemCount() == 0) return;
                menu.ShowAsContext();
                Event.current.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub-SM context menu error: {e}");
            }
        }

        /* Appends Unpack to an existing GenericMenu when the selected object is a direct child sub state machine. */
        internal static void AddMenuItems(object node, GenericMenu menu)
        {
            try
            {
                var graph = PatchStateNodeMenu.ResolveGraph(node);
                if (graph == null) return;

                if (AnimatorEditorInit.GetActiveStateMachineMethod == null)
                {
                    Debug.LogError("[AnimatorTools] GetActiveStateMachineMethod is null — Unity internal API may have changed. Sub-SM context menu unavailable.");
                    return;
                }

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
                        new GUIContent(L10n.Get("context_menu.unpack_subsm")),
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


    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchTransitionContextMenu
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.HandleContextMenuMethod;

        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            try
            {
                if (Event.current.type != EventType.ContextClick) return true;

                var selectedTransitions = Selection.objects
                    .Where(static x => x is AnimatorStateTransition)
                    .Cast<AnimatorStateTransition>()
                    .ToArray();

                var selectedEntryTransitions = Selection.objects
                    .OfType<AnimatorTransition>()
                    .ToArray();

                if (selectedTransitions.Length == 0 && selectedEntryTransitions.Length == 0
                    && PatchStateNodeMenu._redirectEntryTransitions == null) return true;

                var menu = new GenericMenu();
                AddItems(menu, __instance);
                if (menu.GetItemCount() > 0)
                {
                    menu.ShowAsContext();
                    Event.current.Use();
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Transition menu prefix error: {e}");
                return true;
            }
        }

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
                var selectedEntryTransitions = Selection.objects
                    .OfType<AnimatorTransition>()
                    .ToArray();
                var selectedStates = Selection.objects
                    .Where(static x => x is AnimatorState)
                    .Cast<AnimatorState>()
                    .ToArray();

                if (menu.GetItemCount() > 0) menu.AddSeparator("");

                // Transition ops — only when transitions selected or phase-2 active
                if (selectedTransitions.Length > 0)
                {
                    var capturedSM = activeStateMachine;
                    var capturedTransitions = selectedTransitions;
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.reverse_transitions")), false,
                        static data =>
                        {
                            var pair = ((AnimatorStateMachine, AnimatorStateTransition[]))data;
                            AnimatorBulkTransitionOps.ReverseNegateTransitions(pair.Item1, pair.Item2);
                        },
                        (capturedSM, capturedTransitions));
                }

                if (PatchStateNodeMenu._redirectTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu._multiTransitionSources = null;
                                PatchStateNodeMenu._multiTransitionSM = null;
                                PatchStateNodeMenu._replicateTransitions = null;
                                PatchStateNodeMenu._replicateSM = null;
                                PatchStateNodeMenu._redirectTransitions = transitions;
                                PatchStateNodeMenu._redirectSM = sm;
                            },
                            (selectedTransitions, activeStateMachine));
                }
                else
                {
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), true,
                        static data =>
                        {
                            var dests = (AnimatorState[])data;
                            var transitions = PatchStateNodeMenu._redirectTransitions;
                            var sm = PatchStateNodeMenu._redirectSM;
                            PatchStateNodeMenu._redirectTransitions = null;
                            PatchStateNodeMenu._redirectSM = null;
                            if (dests.Length > 0)
                                AnimatorBulkTransitionOps.RedirectTransitions(sm, transitions, dests);
                        },
                        selectedStates);
                }

                if (PatchStateNodeMenu._replicateTransitions == null && PatchStateNodeMenu._replicateEntryTransitions == null)
                {
                    if (selectedTransitions.Length > 0)
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorStateTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu._multiTransitionSources = null;
                                PatchStateNodeMenu._multiTransitionSM = null;
                                PatchStateNodeMenu._redirectTransitions = null;
                                PatchStateNodeMenu._redirectSM = null;
                                PatchStateNodeMenu._replicateTransitions = transitions;
                                PatchStateNodeMenu._replicateSM = sm;
                            },
                            (selectedTransitions, activeStateMachine));
                }
                else if (PatchStateNodeMenu._replicateTransitions != null)
                {
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), true,
                        static data =>
                        {
                            var newSourceStates = (AnimatorState[])data;
                            var transitions = PatchStateNodeMenu._replicateTransitions;
                            var sm = PatchStateNodeMenu._replicateSM;
                            PatchStateNodeMenu._replicateTransitions = null;
                            PatchStateNodeMenu._replicateSM = null;
                            if (newSourceStates.Length > 0)
                                AnimatorBulkTransitionOps.ReplicateTransitions(sm, transitions, newSourceStates);
                        },
                        selectedStates);
                }

                if (PatchStateNodeMenu._redirectEntryTransitions == null)
                {
                    if (selectedEntryTransitions.Length > 0)
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu.CancelPending();
                                PatchStateNodeMenu._redirectEntryTransitions = transitions;
                                PatchStateNodeMenu._redirectEntrySM = sm;
                            },
                            (selectedEntryTransitions, activeStateMachine));
                }
                else
                {
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.redirect_transitions")), true,
                        static data =>
                        {
                            var dests = (AnimatorState[])data;
                            var transitions = PatchStateNodeMenu._redirectEntryTransitions;
                            var sm = PatchStateNodeMenu._redirectEntrySM;
                            PatchStateNodeMenu._redirectEntryTransitions = null;
                            PatchStateNodeMenu._redirectEntrySM = null;
                            if (dests.Length > 0)
                                AnimatorBulkTransitionOps.RedirectEntryTransitions(sm, transitions, dests);
                        },
                        selectedStates);
                }

                if (PatchStateNodeMenu._replicateEntryTransitions == null && PatchStateNodeMenu._replicateTransitions == null)
                {
                    if (selectedEntryTransitions.Length > 0)
                        menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), false,
                            static data =>
                            {
                                var (transitions, sm) = ((AnimatorTransition[], AnimatorStateMachine))data;
                                PatchStateNodeMenu.CancelPending();
                                PatchStateNodeMenu._replicateEntryTransitions = transitions;
                                PatchStateNodeMenu._replicateEntrySM = sm;
                            },
                            (selectedEntryTransitions, activeStateMachine));
                }
                else if (PatchStateNodeMenu._replicateEntryTransitions != null)
                {
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.replicate_transitions")), true,
                        static data =>
                        {
                            var newSourceStates = (AnimatorState[])data;
                            var templates = PatchStateNodeMenu._replicateEntryTransitions;
                            var sm = PatchStateNodeMenu._replicateEntrySM;
                            PatchStateNodeMenu._replicateEntryTransitions = null;
                            PatchStateNodeMenu._replicateEntrySM = null;
                            if (newSourceStates.Length > 0)
                                AnimatorBulkTransitionOps.ReplicateTransitionsFromEntryTransitions(sm, templates, newSourceStates);
                        },
                        selectedStates);
                }

                // Always visible
                menu.AddItem(
                    new GUIContent(L10n.Get("context_menu.delete_all_transitions")),
                    false,
                    static data => AnimatorBulkTransitionOps.DeleteAllTransitions((AnimatorStateMachine)data),
                    activeStateMachine);

                if (selectedTransitions.Length == 0 && selectedEntryTransitions.Length == 0)
                {
                    menu.AddSeparator("");

                    menu.AddItem(new GUIContent(L10n.Get("context_menu.find_unreachable")), false,
                        static data =>
                        {
                            var rootSM = PatchStateNodeMenu.ResolveRootStateMachine((AnimatorStateMachine)data);
                            AnimatorGraphAnalyzer.FindUnreachableStates(rootSM);
                            EditorWindow.GetWindow(AnimatorEditorInit.AnimatorControllerToolType)?.Repaint();
                        }, activeStateMachine);

                    menu.AddItem(new GUIContent(L10n.Get("context_menu.find_terminal")), false,
                        static data =>
                        {
                            var rootSM = PatchStateNodeMenu.ResolveRootStateMachine((AnimatorStateMachine)data);
                            AnimatorGraphAnalyzer.FindTerminalStates(rootSM);
                            EditorWindow.GetWindow(AnimatorEditorInit.AnimatorControllerToolType)?.Repaint();
                        }, activeStateMachine);
                }

                menu.AddSeparator("");

                var capturedMousePosition = Event.current.mousePosition;
                var capturedGraphGUI = graphGUI;
                var capturedSelectedStates = selectedStates;
                var capturedSelectedSubSMs = Selection.objects
                    .OfType<AnimatorStateMachine>()
                    .Where(sm => sm != activeStateMachine)
                    .ToArray();
                var capturedSpecialNodePositions = FrameInteractionPatch.CaptureSpecialNodePositions();
                menu.AddItem(new GUIContent(L10n.Get("context_menu.create_frame")), false, () =>
                {
                    var getActiveSM = AccessTools.Method(capturedGraphGUI.GetType(), "get_activeStateMachine");
                    var sm = getActiveSM?.Invoke(capturedGraphGUI, null) as AnimatorStateMachine;
                    if (sm == null) return;

                    Rect frameBounds;
                    if (FrameInteractionPatch.TryComputeSelectionBounds(capturedSelectedStates, capturedSelectedSubSMs, capturedSpecialNodePositions, out var fitBounds))
                        frameBounds = fitBounds;
                    else
                    {
                        float graphX = capturedMousePosition.x + FrameRenderer.LastScrollPosition.x;
                        float graphY = capturedMousePosition.y + FrameRenderer.LastScrollPosition.y;
                        frameBounds = new Rect(graphX, graphY, 300f, 200f);
                    }

                    var controllerPath = AssetDatabase.GetAssetPath(sm);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                    if (controller == null) return;
                    var frameData = FrameLayoutData.GetOrCreate(controller, out bool createdFrameData);
                    var newFrame = new FrameRect
                    {
                        title = "New Frame",
                        layerStateMachine = FrameRenderer.LastRootLayerSM,
                        activeSM = FrameRenderer.LastActiveSM,
                        bounds = frameBounds,
                    };
                    Undo.RegisterCompleteObjectUndo(frameData, "Create Frame");
                    frameData.frames.Add(newFrame);
                    EditorUtility.SetDirty(frameData);
                    if (createdFrameData) AssetDatabase.SaveAssets();
                    FrameRenderer.InvalidateCache();
                    EditorWindow.GetWindow(AnimatorEditorInit.AnimatorControllerToolType)?.Repaint();

                    FrameRenderer.SelectedFrames.Clear();
                    FrameRenderer.SelectedFrames.Add(newFrame);
                    FrameInteractionPatch.IsRenaming = true;
                    FrameInteractionPatch.RenameBuffer = newFrame.title;
                });

                var capturedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    AssetDatabase.GetAssetPath(FrameRenderer.LastActiveSM));
                var capturedFrameData = capturedController != null ? FrameLayoutData.Get(capturedController) : null;
                if (capturedFrameData != null)
                {
                    menu.AddItem(new GUIContent(L10n.Get("context_menu.delete_all_frames")), false, () =>
                    {
                        var capturedActiveSM = FrameRenderer.LastActiveSM;
                        var framesToRemove = capturedFrameData.frames
                            .Where(f => f.activeSM == capturedActiveSM)
                            .ToList();
                        if (framesToRemove.Count == 0) return;
                        Undo.RegisterCompleteObjectUndo(capturedFrameData, "Delete All Frames");
                        foreach (var removedFrame in framesToRemove)
                        {
                            capturedFrameData.frames.Remove(removedFrame);
                            FrameRenderer.SelectedFrames.Remove(removedFrame);
                        }
                        EditorUtility.SetDirty(capturedFrameData);
                        FrameLayoutData.RemoveIfEmpty(capturedController);
                        FrameRenderer.InvalidateCache();
                    });
                }

                var colorTagSettings = AnimatorDefaultSettings.Load();
                if (colorTagSettings.colorTags.Count > 0 && selectedTransitions.Length > 0)
                {
                    menu.AddSeparator("");
                    var capturedTagTransitions = selectedTransitions;
                    foreach (var colorTag in colorTagSettings.colorTags)
                    {
                        var capturedTagName = colorTag.tagName;
                        bool allChecked = capturedTagTransitions.All(transition => transition.name == capturedTagName);
                        menu.AddItem(
                            new GUIContent($"{L10n.Get("context_menu.tag")}/{capturedTagName}"),
                            allChecked,
                            static data =>
                            {
                                var (transitions, tagName, wasChecked) = ((AnimatorStateTransition[], string, bool))data;
                                Undo.RecordObjects(transitions, "Apply Tag");
                                foreach (var transition in transitions)
                                    transition.name = wasChecked ? "" : tagName;
                                foreach (var transition in transitions)
                                    EditorUtility.SetDirty(transition);
                            },
                            (capturedTagTransitions, capturedTagName, allChecked));
                    }
                    menu.AddItem(
                        new GUIContent($"{L10n.Get("context_menu.tag")}/{L10n.Get("context_menu.remove_tags")}"),
                        false,
                        static data =>
                        {
                            var transitions = (AnimatorStateTransition[])data;
                            Undo.RecordObjects(transitions, "Remove Tag");
                            foreach (var transition in transitions)
                                transition.name = "";
                            foreach (var transition in transitions)
                                EditorUtility.SetDirty(transition);
                        },
                        capturedTagTransitions);
                }
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
