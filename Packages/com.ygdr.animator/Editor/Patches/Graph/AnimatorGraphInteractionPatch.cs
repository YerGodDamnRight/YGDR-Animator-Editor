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
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    /* Double-click empty graph space → create state at cursor; assigns _buffer.anim as motion if found in package.
       Also tracks hovered node for chain-mode snap. */
    [HarmonyPatch]
    internal static class PatchGraphInputHandler
    {
        static FieldInfo _mGraphField;
        static EditorWindow _animWindow;

        internal static Vector2 _lastMousePosition;
        static HashSet<AnimatorState> _prepasteStateSet;
        static HashSet<AnimatorStateMachine> _prepasteSubSMSet;
        static AnimatorStateMachine _pasteSM;
        static AnimationClip _bufferClip;
        static bool _pasteCommandFromKeybind;
        static bool _duplicateCommandFromKeybind;
        static bool _suppressNextContextClick;

        /* Lazily resolves and caches the m_Graph FieldInfo from the GraphGUI instance type. */
        static FieldInfo MGraphField(object instance) =>
            _mGraphField ??= AccessTools.Field(instance.GetType(), "m_Graph");

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
        static MethodBase TargetMethod() => GraphPatchReflection.OnGraphGUIMethod;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;

                UpdateLastMousePosition(currentEvent);

                if (TryBeginChainFanFromSpecialNode(__instance, currentEvent)) return;
                if (TryBeginRightDrag(__instance, currentEvent)) return;
                if (TryContinueRightDrag(__instance, currentEvent)) return;
                if (TryReleaseRightDrag(__instance, currentEvent)) return;
                if (TrySuppressContextClick(currentEvent)) return;

                bool isAnyRenameActive = IsAnyRenameActive();

                if (TryGateDuplicateExecuteCommand(currentEvent)) return;
                if (TryHandlePasteExecuteCommand(__instance, currentEvent)) return;
                if (TryHandleF2Rename(__instance, currentEvent, isAnyRenameActive)) return;
                if (TryHandleF3Rename(currentEvent, isAnyRenameActive)) return;
                if (TryHandleEscape(currentEvent)) return;
                if (TryHandleEnterFinalize(currentEvent)) return;

                if (isAnyRenameActive) return;

                if (TryHandleSelectAll(__instance, currentEvent)) return;
                if (TryHandleSelectAllTransitions(__instance, currentEvent)) return;
                if (TryHandleKeyDownSelectionAndBulkOps(__instance, currentEvent)) return;
                if (TryHandleCopy(__instance, currentEvent)) return;
                if (TryGateCopyExecuteCommand(currentEvent)) return;
                if (TryHandlePasteWithClipboard(__instance, currentEvent)) return;
                if (TryForwardPasteToExecuteCommand(__instance, currentEvent)) return;
                if (TryForwardDuplicateToExecuteCommand(currentEvent)) return;

                UpdateSnapTargetOnMouseMove(__instance, currentEvent);

                if (TryHandlePasteExitClick(__instance, currentEvent)) return;

                HandleDoubleClickOnGraph(__instance, currentEvent);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Double-click create state error: {e}");
            }
        }

        static void UpdateLastMousePosition(Event currentEvent)
        {
            if (currentEvent.isMouse || currentEvent.type == EventType.MouseMove)
            {
                _lastMousePosition = currentEvent.mousePosition;
            }
        }

        /* Ctrl/Shift-doubleclick or chain/fan keybind while hovering an AnyState/Entry node begins chain/fan mode anchored on that SM hub. */
        static bool TryBeginChainFanFromSpecialNode(object __instance, Event currentEvent)
        {
            if (!((currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentEvent.clickCount == 2 && (currentEvent.control || currentEvent.shift))
                || currentEvent.type == EventType.KeyDown))
                return false;

            var kbSpecial = AnimatorDefaultSettings.Load();
            bool specialEligible = !PatchStateChainTransition.ChainActive && !PatchStateChainTransition.FanActive;
            bool wantsChainBegin = (currentEvent.type == EventType.MouseDown && currentEvent.control && currentEvent.clickCount == 2)
                || (currentEvent.type == EventType.KeyDown && specialEligible && kbSpecial.kbChainMode.Matches(currentEvent));
            bool wantsFanBegin = (currentEvent.type == EventType.MouseDown && currentEvent.shift && currentEvent.clickCount == 2)
                || (currentEvent.type == EventType.KeyDown && specialEligible && kbSpecial.kbFanMode.Matches(currentEvent));

            if (!wantsChainBegin && !wantsFanBegin) return false;

            var hitPos = currentEvent.type == EventType.MouseDown ? currentEvent.mousePosition : _lastMousePosition;
            var specialGraph = MGraphField(__instance)?.GetValue(__instance);
            var specialNodes = GetNodes(specialGraph);
            if (specialNodes == null) return false;

            foreach (var node in specialNodes)
            {
                var specialNodeType = node.GetType();
                bool isAnyStateNode = specialNodeType == AnimatorEditorInit.AnyStateNodeType;
                bool isEntryNode = specialNodeType == AnimatorEditorInit.EntryNodeType;
                if (!isAnyStateNode && !isEntryNode) continue;
                var specialNodePosition = GraphPatchReflection.NodePositionField?.GetValue(node);
                if (specialNodePosition is not Rect specialNodeRect || !specialNodeRect.Contains(hitPos)) continue;
                var specialActiveSM = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                var specialSM = isAnyStateNode ? PatchStateNodeMenu.ResolveRootStateMachine(specialActiveSM) : specialActiveSM;
                if (specialSM == null) break;
                if (wantsChainBegin)
                    PatchStateChainTransition.BeginChainSpecial(specialSM, specialNodeRect, isAnyStateNode);
                else
                    PatchStateChainTransition.BeginFanSpecial(specialSM, specialNodeRect, isAnyStateNode);
                currentEvent.Use();
                return true;
            }
            return false;
        }

        static bool TryBeginRightDrag(object __instance, Event currentEvent)
        {
            if (!(currentEvent.type == EventType.MouseDown && currentEvent.button == 1
                && !PatchStateChainTransition.ChainActive && !PatchStateChainTransition.FanActive
                && !PatchTransitionCopyPaste.PasteActive && PatchStateNodeMenu._multiTransitionSources == null))
                return false;

            _suppressNextContextClick = false;
            var rightDragGraph = MGraphField(__instance)?.GetValue(__instance);
            var rightDragNodes = GetNodes(rightDragGraph);
            if (rightDragNodes == null) return false;

            foreach (var node in rightDragNodes)
            {
                var rightDownNodeType = node.GetType();
                if (rightDownNodeType != AnimatorEditorInit.StateNodeType && rightDownNodeType != AnimatorEditorInit.AnyStateNodeType && rightDownNodeType != AnimatorEditorInit.EntryNodeType) continue;
                var nodePosition = GraphPatchReflection.NodePositionField?.GetValue(node);
                if (nodePosition is not Rect nodeRect || !nodeRect.Contains(currentEvent.mousePosition)) continue;
                var activeSMRightDrag = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                if (rightDownNodeType == AnimatorEditorInit.AnyStateNodeType)
                {
                    var rootSMRightDrag = PatchStateNodeMenu.ResolveRootStateMachine(activeSMRightDrag);
                    PatchRightDragTransition.BeginPendingAnyState(nodeRect, rootSMRightDrag, currentEvent.mousePosition);
                }
                else if (rightDownNodeType == AnimatorEditorInit.EntryNodeType)
                {
                    PatchRightDragTransition.BeginPendingEntry(nodeRect, activeSMRightDrag, currentEvent.mousePosition);
                }
                else
                {
                    var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                    if (nodeState == null) continue;
                    PatchRightDragTransition.BeginPending(nodeState, nodeRect, activeSMRightDrag, currentEvent.mousePosition);
                }
                if (AnimWindow != null) AnimWindow.wantsMouseMove = true;
                return true;
            }
            return false;
        }

        static bool TryContinueRightDrag(object __instance, Event currentEvent)
        {
            if (!(currentEvent.type == EventType.MouseDrag && currentEvent.button == 1 && PatchRightDragTransition.IsPending))
                return false;

            const float dragThreshold = 8f;
            if (!PatchRightDragTransition.DragActive &&
                (currentEvent.mousePosition - PatchRightDragTransition.PendingStartPos).sqrMagnitude > dragThreshold * dragThreshold)
                PatchRightDragTransition.ActivateDrag();
            if (PatchRightDragTransition.DragActive)
            {
                UpdateSnapTarget(__instance, currentEvent.mousePosition);
                AnimWindow?.Repaint();
            }
            currentEvent.Use();
            return true;
        }

        static bool TryReleaseRightDrag(object __instance, Event currentEvent)
        {
            if (!(currentEvent.type == EventType.MouseUp && currentEvent.button == 1 && PatchRightDragTransition.IsPending))
                return false;

            if (PatchRightDragTransition.DragActive)
            {
                var rightDragGraph = MGraphField(__instance)?.GetValue(__instance);
                var rightDragNodes = GetNodes(rightDragGraph);
                AnimatorState rightDragDestination = null;
                AnimatorStateMachine rightDragDestSM = null;
                bool rightDragToExit = false;
                bool isAnyStateSrc = PatchRightDragTransition.IsAnyStateSource;
                bool isEntrySrc = PatchRightDragTransition.IsEntrySource;
                if (rightDragNodes != null)
                {
                    foreach (var node in rightDragNodes)
                    {
                        var destNodeType = node.GetType();
                        if (destNodeType != AnimatorEditorInit.StateNodeType
                            && destNodeType != AnimatorEditorInit.ExitNodeType
                            && destNodeType != AnimatorEditorInit.StateMachineNodeType) continue;
                        if (destNodeType == AnimatorEditorInit.ExitNodeType && (isAnyStateSrc || isEntrySrc)) continue;
                        var nodePosition = GraphPatchReflection.NodePositionField?.GetValue(node);
                        if (nodePosition is not Rect nodeRect || !nodeRect.Contains(currentEvent.mousePosition)) continue;
                        if (destNodeType == AnimatorEditorInit.ExitNodeType)
                            rightDragToExit = true;
                        else if (destNodeType == AnimatorEditorInit.StateMachineNodeType)
                            rightDragDestSM = AnimatorEditorInit.SMNodeStateMachineField?.GetValue(node) as AnimatorStateMachine;
                        else
                            rightDragDestination = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                        break;
                    }
                }
                var rightDragSourceState = PatchRightDragTransition._pendingSourceState;
                var rightDragSourceSM = PatchRightDragTransition._pendingSM;
                bool rightDragIsAnyState = PatchRightDragTransition.IsAnyStateSource;
                bool rightDragIsEntry = PatchRightDragTransition.IsEntrySource;
                PatchRightDragTransition.Clear();
                _suppressNextContextClick = true;
                if (rightDragIsAnyState)
                {
                    if (rightDragDestination != null)
                        AnimatorBulkTransitionOps.AddAnyStateChainTransition(rightDragSourceSM, rightDragDestination);
                }
                else if (rightDragIsEntry)
                {
                    if (rightDragDestination != null)
                    {
                        AnimatorBulkTransitionOps.AddEntryChainTransition(rightDragSourceSM, rightDragDestination);
                    }
                    else if (rightDragDestSM != null)
                    {
                        Undo.RegisterCompleteObjectUndo(rightDragSourceSM, "Entry Sub-State Machine Transition");
                        rightDragSourceSM.AddEntryTransition(rightDragDestSM);
                        EditorUtility.SetDirty(rightDragSourceSM);
                        AnimatorBulkTransitionOps.RebuildAnimatorGraph();
                    }
                }
                else if (rightDragSourceState != null)
                {
                    if (rightDragToExit)
                    {
                        Undo.RegisterCompleteObjectUndo(rightDragSourceState, "Exit Transition");
                        rightDragSourceState.AddExitTransition();
                        EditorUtility.SetDirty(rightDragSourceState);
                    }
                    else if (rightDragDestSM != null)
                    {
                        Undo.RegisterCompleteObjectUndo(rightDragSourceState, "Sub-State Machine Transition");
                        rightDragSourceState.AddTransition(rightDragDestSM);
                        EditorUtility.SetDirty(rightDragSourceState);
                    }
                    else if (rightDragDestination != null && rightDragDestination != rightDragSourceState)
                    {
                        AnimatorBulkTransitionOps.AddChainTransition(rightDragSourceState, rightDragDestination);
                    }
                }
                currentEvent.Use();
                return true;
            }
            PatchRightDragTransition.Clear();
            return true;
        }

        static bool TrySuppressContextClick(Event currentEvent)
        {
            if (currentEvent.type != EventType.ContextClick || !_suppressNextContextClick) return false;
            _suppressNextContextClick = false;
            currentEvent.Use();
            return true;
        }

        static bool IsAnyRenameActive() =>
            StateRenameState.RenameTarget != null
            || SubSMRenameState.RenameTarget != null
            || MotionRenameState.RenameTargetState != null;

        static bool TryGateDuplicateExecuteCommand(Event currentEvent)
        {
            if (currentEvent.type != EventType.ExecuteCommand || currentEvent.commandName != "Duplicate") return false;
            var kbD = AnimatorDefaultSettings.Load();
            bool isDefaultDuplicate = kbD.kbDuplicate.key == KeyCode.D && kbD.kbDuplicate.ctrl && !kbD.kbDuplicate.shift && !kbD.kbDuplicate.alt;
            if (!isDefaultDuplicate && !_duplicateCommandFromKeybind) { currentEvent.Use(); return true; }
            _duplicateCommandFromKeybind = false;
            return false;
        }

        static bool TryHandlePasteExecuteCommand(object __instance, Event currentEvent)
        {
            if (currentEvent.type != EventType.ExecuteCommand || currentEvent.commandName != "Paste") return false;

            var kbP = AnimatorDefaultSettings.Load();
            bool isDefaultPaste = kbP.kbPaste.key == KeyCode.V && kbP.kbPaste.ctrl && !kbP.kbPaste.shift && !kbP.kbPaste.alt;
            if (!isDefaultPaste && !_pasteCommandFromKeybind) { currentEvent.Use(); return true; }
            _pasteCommandFromKeybind = false;

            if (PatchStateChainTransition.FanActive || PatchStateChainTransition.ChainActive || PatchStateNodeMenu._multiTransitionSources != null)
            {
                currentEvent.Use();
                return true;
            }

            var getActiveSM = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
            var activeSM = getActiveSM?.Invoke(__instance, null) as AnimatorStateMachine;
            if (activeSM != null)
            {
                _pasteSM = activeSM;
                _prepasteStateSet = new HashSet<AnimatorState>(activeSM.states.Select(childState => childState.state));
                _prepasteSubSMSet = new HashSet<AnimatorStateMachine>(activeSM.stateMachines.Select(childStateMachine => childStateMachine.stateMachine));
            }
            return false;
        }

        static bool TryHandleF2Rename(object __instance, Event currentEvent, bool isAnyRenameActive)
        {
            if (isAnyRenameActive || currentEvent.type != EventType.KeyDown || currentEvent.keyCode != KeyCode.F2) return false;

            var selectedState = Selection.activeObject as AnimatorState;
            if (selectedState != null)
            {
                MotionRenameState.Cancel();
                SubSMRenameState.Cancel();
                var additionalStates = Selection.objects.OfType<AnimatorState>()
                    .Where(state => state != selectedState).ToArray();
                var renameActiveSM = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod
                    ?.Invoke(__instance, null) as AnimatorStateMachine;
                StateRenameState.Begin(selectedState, additionalStates.Length > 0 ? additionalStates : null, renameActiveSM);
                currentEvent.Use();
                return true;
            }
            var selectedSubSM = Selection.activeObject as AnimatorStateMachine;
            if (selectedSubSM != null)
            {
                MotionRenameState.Cancel();
                StateRenameState.Cancel();
                SubSMRenameState.Begin(selectedSubSM);
                currentEvent.Use();
                return true;
            }
            return false;
        }

        static bool TryHandleF3Rename(Event currentEvent, bool isAnyRenameActive)
        {
            if (isAnyRenameActive || currentEvent.type != EventType.KeyDown || currentEvent.keyCode != KeyCode.F3) return false;

            var selectedState = Selection.activeObject as AnimatorState;
            if (selectedState != null && selectedState.motion != null)
            {
                StateRenameState.Cancel();
                SubSMRenameState.Cancel();
                MotionRenameState.Begin(selectedState.motion, selectedState);
                currentEvent.Use();
                return true;
            }
            return false;
        }

        static bool TryHandleEscape(Event currentEvent)
        {
            if (currentEvent.type != EventType.KeyDown || currentEvent.keyCode != KeyCode.Escape) return false;

            if (PatchStateChainTransition.ChainActive) { PatchStateChainTransition.Clear(); currentEvent.Use(); return true; }
            if (PatchStateChainTransition.FanActive) { PatchStateChainTransition.ClearFan(); currentEvent.Use(); return true; }
            if (PatchTransitionCopyPaste.PasteActive) { PatchTransitionCopyPaste.ClearPaste(); currentEvent.Use(); return true; }
            if (PatchRightDragTransition.IsPending) { PatchRightDragTransition.Clear(); currentEvent.Use(); return true; }
            if (PatchStateNodeMenu._multiTransitionSources != null || PatchStateNodeMenu._redirectTransitions != null || PatchStateNodeMenu._replicateTransitions != null || PatchStateNodeMenu._redirectEntryTransitions != null)
            {
                PatchStateNodeMenu.CancelPending();
                currentEvent.Use();
                return true;
            }
            return false;
        }

        static bool TryHandleEnterFinalize(Event currentEvent)
        {
            if (currentEvent.type != EventType.KeyDown || (currentEvent.keyCode != KeyCode.Return && currentEvent.keyCode != KeyCode.KeypadEnter)) return false;

            if (PatchStateNodeMenu._multiTransitionSources != null)
            {
                var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                var multiSources = PatchStateNodeMenu._multiTransitionSources;
                var multiSM = PatchStateNodeMenu._multiTransitionSM;
                var fromAnyState = PatchStateNodeMenu._multiTransitionFromAnyState;
                var fromEntry = PatchStateNodeMenu._multiTransitionFromEntry;
                PatchStateNodeMenu._multiTransitionSources = null;
                PatchStateNodeMenu._multiTransitionSM = null;
                PatchStateNodeMenu._multiTransitionFromAnyState = false;
                PatchStateNodeMenu._multiTransitionFromEntry = false;
                if (destinationStates.Length > 0)
                {
                    if (fromAnyState)
                        AnimatorBulkTransitionOps.MultiTransitionFromAnyState(multiSM, destinationStates);
                    else if (fromEntry)
                        AnimatorBulkTransitionOps.MultiTransitionFromEntry(multiSM, destinationStates);
                    else
                        AnimatorBulkTransitionOps.MultiTransition(multiSM, multiSources, destinationStates);
                }
                currentEvent.Use();
                return true;
            }
            if (PatchStateNodeMenu._redirectTransitions != null)
            {
                var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                bool isExitSelected = Selection.objects.Any(o => AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(o) ?? false);
                var redirectTransitions = PatchStateNodeMenu._redirectTransitions;
                var redirectSM = PatchStateNodeMenu._redirectSM;
                PatchStateNodeMenu._redirectTransitions = null;
                PatchStateNodeMenu._redirectSM = null;
                if (isExitSelected) AnimatorBulkTransitionOps.RedirectTransitionsToExit(redirectSM, redirectTransitions);
                else if (destinationStates.Length > 0) AnimatorBulkTransitionOps.RedirectTransitions(redirectSM, redirectTransitions, destinationStates);
                currentEvent.Use();
                return true;
            }
            if (PatchStateNodeMenu._redirectEntryTransitions != null)
            {
                var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                var redirectTransitions = PatchStateNodeMenu._redirectEntryTransitions;
                var redirectSM = PatchStateNodeMenu._redirectEntrySM;
                PatchStateNodeMenu._redirectEntryTransitions = null;
                PatchStateNodeMenu._redirectEntrySM = null;
                if (destinationStates.Length > 0) AnimatorBulkTransitionOps.RedirectEntryTransitions(redirectSM, redirectTransitions, destinationStates);
                currentEvent.Use();
                return true;
            }
            if (PatchStateNodeMenu._replicateTransitions != null)
            {
                var newSourceStates = Selection.objects.OfType<AnimatorState>().ToArray();
                bool isAnyStateSelected = Selection.objects.Any(o => AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(o) ?? false);
                var replicateTransitions = PatchStateNodeMenu._replicateTransitions;
                var replicateSM = PatchStateNodeMenu._replicateSM;
                PatchStateNodeMenu._replicateTransitions = null;
                PatchStateNodeMenu._replicateSM = null;
                if (isAnyStateSelected) AnimatorBulkTransitionOps.ReplicateTransitionsFromAnyState(PatchStateNodeMenu.ResolveRootStateMachine(replicateSM), replicateTransitions);
                else if (newSourceStates.Length > 0) AnimatorBulkTransitionOps.ReplicateTransitions(replicateSM, replicateTransitions, newSourceStates);
                currentEvent.Use();
                return true;
            }
            return false;
        }

        static bool TryHandleSelectAll(object __instance, Event currentEvent)
        {
            if (!AnimatorDefaultSettings.Load().kbSelectAll.Matches(currentEvent)) return false;
            var activeStateMachineA = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
            if (activeStateMachineA != null)
            {
                var allStates = activeStateMachineA.states.Select(childState => (UnityEngine.Object)childState.state);
                var allSubSMs = activeStateMachineA.stateMachines.Select(childStateMachine => (UnityEngine.Object)childStateMachine.stateMachine);
                Selection.objects = allStates.Concat(allSubSMs).ToArray();
                currentEvent.Use();
            }
            return true;
        }

        static bool TryHandleSelectAllTransitions(object __instance, Event currentEvent)
        {
            if (!AnimatorDefaultSettings.Load().kbSelectAllTransitions.Matches(currentEvent)) return false;
            var activeStateMachineA = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
            if (activeStateMachineA != null)
            {
                var allTransitions = activeStateMachineA.states
                    .SelectMany(childState => childState.state.transitions)
                    .Concat<UnityEngine.Object>(activeStateMachineA.anyStateTransitions)
                    .Concat(activeStateMachineA.entryTransitions)
                    .ToArray();
                Selection.objects = allTransitions;
                currentEvent.Use();
            }
            return true;
        }

        /* Selection-scoped keyboard shortcuts: select-incoming/outgoing/both, multi-transition, reverse, replicate, redirect. */
        static bool TryHandleKeyDownSelectionAndBulkOps(object __instance, Event currentEvent)
        {
            if (currentEvent.type != EventType.KeyDown) return false;

            var selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
            var kb = AnimatorDefaultSettings.Load();

            if (selectedStates.Length > 0)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(selectedStates[0]));
                if (kb.kbSelectIncoming.Matches(currentEvent)) { AnimationEditorWindow.SelectIncomingTransitions(controller, selectedStates); currentEvent.Use(); return true; }
                if (kb.kbSelectOutgoing.Matches(currentEvent)) { AnimationEditorWindow.SelectOutgoingTransitions(selectedStates); currentEvent.Use(); return true; }
                if (kb.kbSelectBoth.Matches(currentEvent))     { AnimationEditorWindow.SelectBothTransitions(controller, selectedStates); currentEvent.Use(); return true; }
            }

            bool isAnyStateKb = Selection.objects.Any(o => AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(o) ?? false);
            bool isExitKb     = Selection.objects.Any(o => AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(o) ?? false);
            bool isEntryKb    = Selection.objects.Any(o => AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(o) ?? false);
            if (isAnyStateKb || isExitKb || isEntryKb)
            {
                var activeSMKb = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeSMKb != null)
                {
                    if (isAnyStateKb && kb.kbSelectOutgoing.Matches(currentEvent)) { AnimationEditorWindow.SelectOutgoingFromAnyState(PatchStateNodeMenu.ResolveRootStateMachine(activeSMKb), activeSMKb); currentEvent.Use(); return true; }
                    if (isExitKb     && kb.kbSelectIncoming.Matches(currentEvent)) { AnimationEditorWindow.SelectIncomingToExit(activeSMKb);        currentEvent.Use(); return true; }
                    if (isEntryKb    && kb.kbSelectOutgoing.Matches(currentEvent)) { AnimationEditorWindow.SelectOutgoingFromEntry(activeSMKb);     currentEvent.Use(); return true; }
                }
            }

            if (kb.kbMultiTransition.Matches(currentEvent))
            {
                if (PatchStateNodeMenu._multiTransitionSources == null)
                {
                    var activeSMmt = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSMmt != null)
                    {
                        if (selectedStates.Length > 0)
                        {
                            PatchStateNodeMenu.CancelPending();
                            PatchStateNodeMenu._multiTransitionSources      = selectedStates;
                            PatchStateNodeMenu._multiTransitionSM           = activeSMmt;
                            PatchStateNodeMenu._multiTransitionFromAnyState = false;
                            PatchStateNodeMenu._multiTransitionFromEntry    = false;
                        }
                        else if (isAnyStateKb)
                        {
                            PatchStateNodeMenu.CancelPending();
                            PatchStateNodeMenu._multiTransitionSources      = System.Array.Empty<AnimatorState>();
                            PatchStateNodeMenu._multiTransitionSM           = PatchStateNodeMenu.ResolveRootStateMachine(activeSMmt);
                            PatchStateNodeMenu._multiTransitionFromAnyState = true;
                            PatchStateNodeMenu._multiTransitionFromEntry    = false;
                        }
                        else if (isEntryKb)
                        {
                            PatchStateNodeMenu.CancelPending();
                            PatchStateNodeMenu._multiTransitionSources      = System.Array.Empty<AnimatorState>();
                            PatchStateNodeMenu._multiTransitionSM           = activeSMmt;
                            PatchStateNodeMenu._multiTransitionFromAnyState = false;
                            PatchStateNodeMenu._multiTransitionFromEntry    = true;
                        }
                    }
                }
                else if (!(PatchStateNodeMenu._multiTransitionFromAnyState && isExitKb)
                      && !(PatchStateNodeMenu._multiTransitionFromEntry    && isExitKb))
                {
                    var multiSources = PatchStateNodeMenu._multiTransitionSources;
                    var multiSM      = PatchStateNodeMenu._multiTransitionSM;
                    var fromAnyState = PatchStateNodeMenu._multiTransitionFromAnyState;
                    var fromEntry    = PatchStateNodeMenu._multiTransitionFromEntry;
                    PatchStateNodeMenu._multiTransitionSources      = null;
                    PatchStateNodeMenu._multiTransitionSM           = null;
                    PatchStateNodeMenu._multiTransitionFromAnyState = false;
                    PatchStateNodeMenu._multiTransitionFromEntry    = false;
                    if (isExitKb && !fromAnyState && !fromEntry)
                        AnimatorBulkTransitionOps.MultiTransitionToExit(multiSM, multiSources);
                    else if (fromAnyState && selectedStates.Length > 0)
                        AnimatorBulkTransitionOps.MultiTransitionFromAnyState(multiSM, selectedStates);
                    else if (fromEntry && selectedStates.Length > 0)
                        AnimatorBulkTransitionOps.MultiTransitionFromEntry(multiSM, selectedStates);
                    else if (selectedStates.Length > 0)
                        AnimatorBulkTransitionOps.MultiTransition(multiSM, multiSources, selectedStates);
                }
                currentEvent.Use();
                return true;
            }

            var selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            if (selectedTransitions.Length > 0 && kb.kbReverseTransitions.Matches(currentEvent))
            {
                var activeSMrt = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeSMrt != null) AnimatorBulkTransitionOps.ReverseNegateTransitions(activeSMrt, selectedTransitions);
                currentEvent.Use();
                return true;
            }

            if (kb.kbReplicate.Matches(currentEvent))
            {
                if (PatchStateNodeMenu._replicateTransitions != null)
                {
                    var newSourceStates = Selection.objects.OfType<AnimatorState>().ToArray();
                    bool isAnyStateSelected = Selection.objects.Any(o => AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(o) ?? false);
                    bool isEntrySelected    = Selection.objects.Any(o => AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(o) ?? false);
                    var replicateTransitions = PatchStateNodeMenu._replicateTransitions;
                    var replicateSM = PatchStateNodeMenu._replicateSM;
                    PatchStateNodeMenu._replicateTransitions = null;
                    PatchStateNodeMenu._replicateSM = null;
                    if (isAnyStateSelected)               AnimatorBulkTransitionOps.ReplicateTransitionsFromAnyState(PatchStateNodeMenu.ResolveRootStateMachine(replicateSM), replicateTransitions);
                    else if (isEntrySelected)             AnimatorBulkTransitionOps.ReplicateTransitionsFromEntry(replicateSM, replicateTransitions);
                    else if (newSourceStates.Length > 0)  AnimatorBulkTransitionOps.ReplicateTransitions(replicateSM, replicateTransitions, newSourceStates);
                }
                else if (PatchStateNodeMenu._replicateEntryTransitions != null)
                {
                    var newSourceStates = Selection.objects.OfType<AnimatorState>().ToArray();
                    var templates = PatchStateNodeMenu._replicateEntryTransitions;
                    var replicateSM = PatchStateNodeMenu._replicateEntrySM;
                    PatchStateNodeMenu._replicateEntryTransitions = null;
                    PatchStateNodeMenu._replicateEntrySM = null;
                    if (newSourceStates.Length > 0) AnimatorBulkTransitionOps.ReplicateTransitionsFromEntryTransitions(replicateSM, templates, newSourceStates);
                }
                else if (selectedTransitions.Length > 0)
                {
                    var activeSMrep = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSMrep != null)
                    {
                        PatchStateNodeMenu.CancelPending();
                        PatchStateNodeMenu._replicateTransitions = selectedTransitions;
                        PatchStateNodeMenu._replicateSM = activeSMrep;
                    }
                }
                else
                {
                    var entryTransitionsKb = Selection.objects.OfType<AnimatorTransition>().ToArray();
                    if (entryTransitionsKb.Length > 0)
                    {
                        var activeSMrep = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                        if (activeSMrep != null)
                        {
                            PatchStateNodeMenu.CancelPending();
                            PatchStateNodeMenu._replicateEntryTransitions = entryTransitionsKb;
                            PatchStateNodeMenu._replicateEntrySM = activeSMrep;
                        }
                    }
                }
                currentEvent.Use();
                return true;
            }

            var selectedEntryTransitions = Selection.objects.OfType<AnimatorTransition>().ToArray();
            if (kb.kbRedirect.Matches(currentEvent))
            {
                if (PatchStateNodeMenu._redirectTransitions != null)
                {
                    var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                    bool isExitSelected = Selection.objects.Any(o => AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(o) ?? false);
                    var redirectTransitions = PatchStateNodeMenu._redirectTransitions;
                    var redirectSM = PatchStateNodeMenu._redirectSM;
                    PatchStateNodeMenu._redirectTransitions = null;
                    PatchStateNodeMenu._redirectSM = null;
                    if (isExitSelected) AnimatorBulkTransitionOps.RedirectTransitionsToExit(redirectSM, redirectTransitions);
                    else if (destinationStates.Length > 0) AnimatorBulkTransitionOps.RedirectTransitions(redirectSM, redirectTransitions, destinationStates);
                }
                else if (PatchStateNodeMenu._redirectEntryTransitions != null)
                {
                    var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                    var redirectTransitions = PatchStateNodeMenu._redirectEntryTransitions;
                    var redirectSM = PatchStateNodeMenu._redirectEntrySM;
                    PatchStateNodeMenu._redirectEntryTransitions = null;
                    PatchStateNodeMenu._redirectEntrySM = null;
                    if (destinationStates.Length > 0) AnimatorBulkTransitionOps.RedirectEntryTransitions(redirectSM, redirectTransitions, destinationStates);
                }
                else if (selectedTransitions.Length > 0)
                {
                    var activeSMred = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSMred != null)
                    {
                        PatchStateNodeMenu.CancelPending();
                        PatchStateNodeMenu._redirectTransitions = selectedTransitions;
                        PatchStateNodeMenu._redirectSM = activeSMred;
                    }
                }
                else if (selectedEntryTransitions.Length > 0)
                {
                    var activeSMred = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSMred != null)
                    {
                        PatchStateNodeMenu.CancelPending();
                        PatchStateNodeMenu._redirectEntryTransitions = selectedEntryTransitions;
                        PatchStateNodeMenu._redirectEntrySM = activeSMred;
                    }
                }
                currentEvent.Use();
                return true;
            }

            return false;
        }

        static bool TryHandleCopy(object __instance, Event currentEvent)
        {
            if (!AnimatorDefaultSettings.Load().kbCopy.Matches(currentEvent)) return false;
            var selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            var selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
            if (selectedTransitions.Length > 0 && selectedStates.Length == 0) { PatchTransitionCopyPaste.SetClipboard(selectedTransitions); PatchCopySelectionToPasteboard.ClearCopy(); currentEvent.Use(); return true; }
            GraphPatchReflection.CopySelectionToPasteboardMethod?.Invoke(__instance, null);
            currentEvent.Use();
            return true;
        }

        // Swallow native ExecuteCommand("Copy") when binding is not Ctrl+C (we handled it via KeyDown above)
        static bool TryGateCopyExecuteCommand(Event currentEvent)
        {
            if (currentEvent.type != EventType.ExecuteCommand || currentEvent.commandName != "Copy") return false;
            var kbC = AnimatorDefaultSettings.Load();
            if (!(kbC.kbCopy.key == KeyCode.C && kbC.kbCopy.ctrl && !kbC.kbCopy.shift && !kbC.kbCopy.alt))
            { currentEvent.Use(); return true; }
            return false;
        }

        static bool TryHandlePasteWithClipboard(object __instance, Event currentEvent)
        {
            if (!(AnimatorDefaultSettings.Load().kbPaste.Matches(currentEvent)
                && PatchTransitionCopyPaste.HasClipboard))
                return false;

            if (PatchStateChainTransition.FanActive)
            {
                PatchStateChainTransition.ToggleSeededFan();
                currentEvent.Use();
                return true;
            }

            if (PatchStateNodeMenu._multiTransitionSources != null)
            {
                var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                bool isExitSelected = Selection.objects.Any(o => AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(o) ?? false);
                var multiSources = PatchStateNodeMenu._multiTransitionSources;
                var multiSM = PatchStateNodeMenu._multiTransitionSM;
                var fromAnyState = PatchStateNodeMenu._multiTransitionFromAnyState;
                PatchStateNodeMenu._multiTransitionSources = null;
                PatchStateNodeMenu._multiTransitionSM = null;
                PatchStateNodeMenu._multiTransitionFromAnyState = false;
                var clipboard = PatchTransitionCopyPaste.GetClipboard();
                if (isExitSelected && !fromAnyState)
                {
                    AnimatorTransitionOps.PasteExitTransitions(multiSM, multiSources, clipboard);
                }
                else if (!isExitSelected && fromAnyState && destinationStates.Length > 0)
                {
                    foreach (var destinationState in destinationStates)
                        AnimatorTransitionOps.PasteAnyStateTransitions(multiSM, destinationState, clipboard);
                }
                else if (!isExitSelected && !fromAnyState && destinationStates.Length > 0)
                {
                    foreach (var sourceState in multiSources)
                        foreach (var destinationState in destinationStates)
                            AnimatorTransitionOps.PasteTransitions(sourceState, destinationState, clipboard);
                }
                currentEvent.Use();
                return true;
            }

            var pasteSource = Selection.activeObject as AnimatorState;
            if (pasteSource != null)
            {
                var pasteGraph = MGraphField(__instance)?.GetValue(__instance);
                foreach (var node in GetNodes(pasteGraph) ?? System.Array.Empty<object>())
                {
                    if (node.GetType() != AnimatorEditorInit.StateNodeType) continue;
                    var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                    if (nodeState != pasteSource) continue;
                    var sourceRect = (Rect)(GraphPatchReflection.NodePositionField?.GetValue(node) ?? default(Rect));
                    var getActiveSMForPaste = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                    var activeSMForPaste = getActiveSMForPaste?.Invoke(__instance, null) as AnimatorStateMachine;
                    PatchTransitionCopyPaste.BeginPaste(pasteSource, sourceRect, activeSMForPaste);
                    if (AnimWindow != null) AnimWindow.wantsMouseMove = true;
                    currentEvent.Use();
                    break;
                }
            }
            else
            {
                bool isAnyStatePaste = Selection.objects.Any(o => AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(o) ?? false);
                if (isAnyStatePaste)
                {
                    var pasteGraph = MGraphField(__instance)?.GetValue(__instance);
                    foreach (var node in GetNodes(pasteGraph) ?? System.Array.Empty<object>())
                    {
                        if (node.GetType() != AnimatorEditorInit.AnyStateNodeType) continue;
                        var sourceRect = (Rect)(GraphPatchReflection.NodePositionField?.GetValue(node) ?? default(Rect));
                        var getActiveSMForPaste = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                        var activeSMForPaste = getActiveSMForPaste?.Invoke(__instance, null) as AnimatorStateMachine;
                        if (activeSMForPaste == null) break;
                        PatchTransitionCopyPaste.BeginAnyStatePaste(activeSMForPaste, sourceRect);
                        if (AnimWindow != null) AnimWindow.wantsMouseMove = true;
                        currentEvent.Use();
                        break;
                    }
                }
            }
            return true;
        }

        static bool TryForwardPasteToExecuteCommand(object __instance, Event currentEvent)
        {
            if (!(AnimatorDefaultSettings.Load().kbPaste.Matches(currentEvent) && !PatchTransitionCopyPaste.HasClipboard
                && !PatchStateChainTransition.FanActive && !PatchStateChainTransition.ChainActive
                && PatchStateNodeMenu._multiTransitionSources == null))
                return false;

            var getActiveSMK = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
            var activeSMK = getActiveSMK?.Invoke(__instance, null) as AnimatorStateMachine;
            if (activeSMK != null)
            {
                _pasteSM = activeSMK;
                _prepasteStateSet = new HashSet<AnimatorState>(activeSMK.states.Select(cs => cs.state));
                _prepasteSubSMSet = new HashSet<AnimatorStateMachine>(activeSMK.stateMachines.Select(csm => csm.stateMachine));
            }
            _pasteCommandFromKeybind = true;
            AnimWindow?.SendEvent(EditorGUIUtility.CommandEvent("Paste"));
            currentEvent.Use();
            return true;
        }

        static bool TryForwardDuplicateToExecuteCommand(Event currentEvent)
        {
            if (!AnimatorDefaultSettings.Load().kbDuplicate.Matches(currentEvent)) return false;
            _duplicateCommandFromKeybind = true;
            AnimWindow?.SendEvent(EditorGUIUtility.CommandEvent("Duplicate"));
            currentEvent.Use();
            return true;
        }

        static void UpdateSnapTargetOnMouseMove(object __instance, Event currentEvent)
        {
            if ((PatchStateChainTransition.ChainActive || PatchStateChainTransition.FanActive || PatchTransitionCopyPaste.PasteActive || PatchRightDragTransition.DragActive)
                && currentEvent.type == EventType.MouseMove)
                UpdateSnapTarget(__instance, currentEvent.mousePosition);
        }

        static bool TryHandlePasteExitClick(object __instance, Event currentEvent)
        {
            if (!(PatchTransitionCopyPaste.PasteActive && !PatchTransitionCopyPaste.PasteFromAnyState
                && currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentEvent.clickCount == 1))
                return false;

            var exitGraph = MGraphField(__instance)?.GetValue(__instance);
            foreach (var node in GetNodes(exitGraph) ?? System.Array.Empty<object>())
            {
                if (node.GetType() != AnimatorEditorInit.ExitNodeType) continue;
                var pos = GraphPatchReflection.NodePositionField?.GetValue(node);
                if (!(pos is Rect exitRect) || !exitRect.Contains(currentEvent.mousePosition)) continue;
                var sm = PatchTransitionCopyPaste.PasteSM;
                var source = PatchTransitionCopyPaste.PasteSource;
                if (sm == null || source == null) break;
                AnimatorTransitionOps.PasteExitTransitions(sm, new[] { source }, PatchTransitionCopyPaste.GetClipboard());
                PatchTransitionCopyPaste.ClearPaste();
                currentEvent.Use();
                return true;
            }
            return false;
        }

        /* Ctrl/Alt-doubleclick on an existing node makes a transition from it; Ctrl-doubleclick on empty space creates a new state. */
        static void HandleDoubleClickOnGraph(object __instance, Event currentEvent)
        {
            if (currentEvent.type != EventType.MouseDown || currentEvent.clickCount != 2 || currentEvent.button != 0 || currentEvent.shift || (!currentEvent.control && !currentEvent.alt))
                return;

            var mousePos = currentEvent.mousePosition;
            var graph = MGraphField(__instance)?.GetValue(__instance);
            if (graph == null) return;

            var nodes = GetNodes(graph);
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var pos = GraphPatchReflection.NodePositionField?.GetValue(node);
                    if (pos is Rect rect && rect.Contains(mousePos))
                    {
                        if (!PatchStateChainTransition.ChainActive && !PatchTransitionCopyPaste.PasteActive && currentEvent.alt && !currentEvent.control)
                        {
                            var nodeType = node.GetType();
                            MethodInfo makeTransitionCallback = null;
                            if (nodeType == AnimatorEditorInit.StateNodeType)
                            {
                                var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                                if (nodeState?.motion is not BlendTree)
                                    makeTransitionCallback = GraphPatchReflection.MakeTransitionCallbackMethod;
                            }
                            else if (nodeType == AnimatorEditorInit.AnyStateNodeType)
                                makeTransitionCallback = GraphPatchReflection.AnyStateMakeTransitionCallbackMethod;
                            else if (nodeType == AnimatorEditorInit.EntryNodeType)
                                makeTransitionCallback = GraphPatchReflection.EntryMakeTransitionCallbackMethod;
                            if (makeTransitionCallback != null)
                            {
                                makeTransitionCallback.Invoke(node, null);
                                currentEvent.Use();
                            }
                        }
                        return;
                    }
                }
            }

            if (!currentEvent.control) return;

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

            var bufferClip = FindBufferClip();
            if (bufferClip != null)
            {
                Undo.RegisterCompleteObjectUndo(newState, "Create State");
                newState.motion = bufferClip;
                EditorUtility.SetDirty(newState);
            }

            currentEvent.Use();
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (_pasteSM == null) return;
            try
            {
                var allChildStates = _pasteSM.states;
                var allChildSMs = _pasteSM.stateMachines;

                var newStateIndices = new List<int>();
                for (int i = 0; i < allChildStates.Length; i++)
                {
                    if (!_prepasteStateSet.Contains(allChildStates[i].state))
                        newStateIndices.Add(i);
                }

                var newSubSMIndices = new List<int>();
                if (_prepasteSubSMSet != null)
                {
                    for (int i = 0; i < allChildSMs.Length; i++)
                    {
                        if (!_prepasteSubSMSet.Contains(allChildSMs[i].stateMachine))
                            newSubSMIndices.Add(i);
                    }
                }

                if (newStateIndices.Count == 0 && newSubSMIndices.Count == 0) return;

                Vector2 centroid = Vector2.zero;
                int totalNodes = newStateIndices.Count + newSubSMIndices.Count;
                foreach (int index in newStateIndices)
                    centroid += new Vector2(allChildStates[index].position.x, allChildStates[index].position.y);
                foreach (int index in newSubSMIndices)
                    centroid += new Vector2(allChildSMs[index].position.x, allChildSMs[index].position.y);
                centroid /= totalNodes;

                Vector2 offset = _lastMousePosition - centroid;

                for (int j = 0; j < newStateIndices.Count; j++)
                {
                    int index = newStateIndices[j];
                    var childState = allChildStates[index];
                    childState.position = new Vector3(
                        childState.position.x + offset.x,
                        childState.position.y + offset.y,
                        childState.position.z);
                    allChildStates[index] = childState;
                }

                for (int j = 0; j < newSubSMIndices.Count; j++)
                {
                    int index = newSubSMIndices[j];
                    var childSM = allChildSMs[index];
                    childSM.position = new Vector3(
                        childSM.position.x + offset.x,
                        childSM.position.y + offset.y,
                        childSM.position.z);
                    allChildSMs[index] = childSM;
                }

                _pasteSM.states = allChildStates;
                _pasteSM.stateMachines = allChildSMs;
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
                _prepasteSubSMSet = null;
            }
        }

        /* Updates PatchStateChainTransition.SnapTarget to the center of whichever state/Exit node the mouse is over, or null. */
        static void UpdateSnapTarget(object graphGUI, Vector2 mousePos)
        {
            var graph = MGraphField(graphGUI)?.GetValue(graphGUI);
            if (graph == null) { PatchStateChainTransition.SnapTarget = null; return; }

            bool rightDragFromState = PatchRightDragTransition.DragActive && !PatchRightDragTransition.IsAnyStateSource && !PatchRightDragTransition.IsEntrySource;
            bool rightDragFromEntry = PatchRightDragTransition.DragActive && PatchRightDragTransition.IsEntrySource;
            bool canSnapToExit  = (PatchTransitionCopyPaste.PasteActive && !PatchTransitionCopyPaste.PasteFromAnyState) || rightDragFromState;
            bool canSnapToSubSM = rightDragFromState || rightDragFromEntry;
            foreach (var node in GetNodes(graph) ?? Array.Empty<object>())
            {
                var nodeType = node.GetType();
                bool isStateNode = nodeType == AnimatorEditorInit.StateNodeType;
                bool isExitNode  = canSnapToExit  && nodeType == AnimatorEditorInit.ExitNodeType;
                bool isSubSMNode = canSnapToSubSM && nodeType == AnimatorEditorInit.StateMachineNodeType;
                if (!isStateNode && !isExitNode && !isSubSMNode) continue;
                var pos = GraphPatchReflection.NodePositionField?.GetValue(node);
                if (pos is Rect rect && rect.Contains(mousePos))
                {
                    PatchStateChainTransition.SnapTarget = rect.center;
                    return;
                }
            }
            PatchStateChainTransition.SnapTarget = null;
        }

        static AnimationClip FindBufferClip()
        {
            if (_bufferClip != null) return _bufferClip;
            var guids = AssetDatabase.FindAssets("BufferClip t:AnimationClip", new[] { "Packages/com.ygdr.animator/Templates" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == "BufferClip")
                {
                    _bufferClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    return _bufferClip;
                }
            }
            return null;
        }

        /* Returns the nodes collection from a graph object, trying the nodes property then the nodes field. */
        internal static IEnumerable GetNodes(object graph)
        {
            var traverse = Traverse.Create(graph);
            return traverse.Property("nodes").GetValue() as IEnumerable
                ?? traverse.Field("nodes").GetValue() as IEnumerable;
        }

    }

    // Draws chain/fan-mode transition preview line on the same layer as real edges (under nodes)
    [HarmonyPatch]
    internal static class PatchEdgeGUIDoEdges
    {
        static FastInvokeHandler _drawArrowInvoker;
        static FastInvokeHandler _edgeSizeMultiplierInvoker;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DoEdgesMethod;

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            bool isActive = PatchStateChainTransition.ChainActive || PatchStateChainTransition.FanActive || PatchTransitionCopyPaste.PasteActive || PatchRightDragTransition.DragActive;
            if (!isActive) return;
            try
            {
                PatchGraphInputHandler.AnimWindow?.Repaint();

                if (Event.current.type != EventType.Repaint) return;

                var sourceRect = PatchStateChainTransition.ChainActive
                    ? PatchStateChainTransition.ChainSourceRect
                    : PatchStateChainTransition.FanActive
                        ? PatchStateChainTransition.FanSourceRect
                        : PatchRightDragTransition.DragActive
                            ? PatchRightDragTransition._pendingSourceRect
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

                var direction     = (destination - source).normalized;
                var perpendicular = new Vector3(-direction.y, direction.x, 0);
                var midpoint      = (source + destination) * 0.5f;
                _edgeSizeMultiplierInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.EdgeSizeMultiplierGetter);
                float mult = _edgeSizeMultiplierInvoker != null ? (float)_edgeSizeMultiplierInvoker(__instance) : 1f;
                var previewSettings = AnimatorDefaultSettings.Load();
                var basePreviewColor = previewSettings.transitionOverlayEnabled
                    ? previewSettings.transitionOverlayColor
                    : Color.white;
                var previewColor = new Color(basePreviewColor.r, basePreviewColor.g, basePreviewColor.b, 0.8f);

                Handles.BeginGUI();
                Handles.color = previewColor;
                Handles.DrawAAPolyLine(4f * mult, source, destination);
                Handles.EndGUI();

                _drawArrowInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.DrawArrowMethod);
                _drawArrowInvoker?.Invoke(null, previewColor, perpendicular, direction, midpoint,
                    5f * mult, 2f * mult);

                if (AnimatorDefaultSettings.Load().transitionAnimateSelected)
                {
                    var animatedPosition = PatchDrawEdge.GetAnimatedArrowPosition(source, midpoint, destination);
                    _drawArrowInvoker?.Invoke(null, previewColor, perpendicular, direction, animatedPosition,
                        5f * mult, 2f * mult);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Chain line draw error: {e}");
            }
        }

        // Layer 2: swallow exceptions from conflicting transpilers on this hot path to prevent GUI lockup
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Debug.LogError($"[AnimatorTools] Exception in DoEdges — disable conflicting feature in Compatibility settings: {__exception.Message}");
            return null;
        }
    }

    // Ctrl+double-click → chain transitions (source advances); Shift+double-click → fan transitions (source fixed); Escape to stop
    [HarmonyPatch]
    internal static class PatchStateChainTransition
    {
        private enum SourceKind { State, AnyState, Entry }

        internal static bool ChainActive { get; private set; }
        internal static Rect ChainSourceRect { get; private set; }
        internal static Vector2? SnapTarget { get; set; }
        private static AnimatorState _chainSource;
        private static AnimatorStateMachine _chainSourceSM;
        private static SourceKind _chainSourceKind;

        internal static bool FanActive { get; private set; }
        internal static Rect FanSourceRect { get; private set; }
        internal static bool SeededFanActive { get; private set; }
        private static AnimatorState _fanSource;
        private static AnimatorStateMachine _fanSourceSM;
        private static SourceKind _fanSourceKind;

        internal static void Clear()
        {
            ChainActive = false;
            _chainSource = null;
            _chainSourceSM = null;
            _chainSourceKind = SourceKind.State;
            ChainSourceRect = Rect.zero;
            SnapTarget = null;
        }

        internal static void ClearFan()
        {
            FanActive = false;
            SeededFanActive = false;
            _fanSource = null;
            _fanSourceSM = null;
            _fanSourceKind = SourceKind.State;
            FanSourceRect = Rect.zero;
            SnapTarget = null;
        }

        internal static void BeginChain(AnimatorState source, Rect sourceRect)
        {
            ClearFan();
            ChainActive = true;
            _chainSource = source;
            _chainSourceSM = null;
            _chainSourceKind = SourceKind.State;
            ChainSourceRect = sourceRect;
            SnapTarget = null;
            if (PatchGraphInputHandler.AnimWindow != null)
                PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;
        }

        /* Begins chain mode anchored on AnyState/Entry; first click transitions from the SM hub, then advances to a normal state-to-state chain. */
        internal static void BeginChainSpecial(AnimatorStateMachine sm, Rect sourceRect, bool isAnyState)
        {
            ClearFan();
            ChainActive = true;
            _chainSource = null;
            _chainSourceSM = sm;
            _chainSourceKind = isAnyState ? SourceKind.AnyState : SourceKind.Entry;
            ChainSourceRect = sourceRect;
            SnapTarget = null;
            if (PatchGraphInputHandler.AnimWindow != null)
                PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;
        }

        internal static void BeginFan(AnimatorState source, Rect sourceRect)
        {
            Clear();
            FanActive = true;
            _fanSource = source;
            _fanSourceSM = null;
            _fanSourceKind = SourceKind.State;
            FanSourceRect = sourceRect;
            SnapTarget = null;
            if (PatchGraphInputHandler.AnimWindow != null)
                PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;
        }

        /* Begins fan mode anchored on AnyState/Entry; source stays fixed at the SM hub for every subsequent click. */
        internal static void BeginFanSpecial(AnimatorStateMachine sm, Rect sourceRect, bool isAnyState)
        {
            Clear();
            FanActive = true;
            _fanSource = null;
            _fanSourceSM = sm;
            _fanSourceKind = isAnyState ? SourceKind.AnyState : SourceKind.Entry;
            FanSourceRect = sourceRect;
            SnapTarget = null;
            if (PatchGraphInputHandler.AnimWindow != null)
                PatchGraphInputHandler.AnimWindow.wantsMouseMove = true;
        }

        internal static void ToggleSeededFan()
        {
            SeededFanActive = !SeededFanActive;
        }

        /* Shared dispatch for chain/fan "add transition" clicks, keyed on which kind of node the source is. */
        private static void DispatchAddTransition(SourceKind kind, AnimatorStateMachine sm, AnimatorState source, AnimatorState destination)
        {
            switch (kind)
            {
                case SourceKind.AnyState:
                    AnimatorBulkTransitionOps.AddAnyStateChainTransition(sm, destination);
                    break;
                case SourceKind.Entry:
                    AnimatorBulkTransitionOps.AddEntryChainTransition(sm, destination);
                    break;
                default:
                    AnimatorBulkTransitionOps.AddChainTransition(source, destination);
                    break;
            }
        }

        /* Shared dispatch for seeded-fan paste clicks, keyed on which kind of node the source is. */
        private static void DispatchPasteTransition(SourceKind kind, AnimatorStateMachine sm, AnimatorState source, AnimatorState destination, AnimatorTransitionOps.TransitionData[] clipboard)
        {
            switch (kind)
            {
                case SourceKind.AnyState:
                    AnimatorTransitionOps.PasteAnyStateTransitions(sm, destination, clipboard);
                    break;
                case SourceKind.Entry:
                    AnimatorTransitionOps.PasteEntryTransitions(sm, destination, clipboard);
                    break;
                default:
                    AnimatorTransitionOps.PasteTransitions(source, destination, clipboard);
                    break;
            }
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI",
                new[] { GraphPatchReflection.GraphGUIType });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;
                bool isClick = currentEvent.type == EventType.MouseDown && currentEvent.button == 0;
                bool isKey   = currentEvent.type == EventType.KeyDown;
                if (!isClick && !isKey) return;

                var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(__instance) as AnimatorState;
                if (nodeState == null) return;

                var nodeRect = (Rect)(GraphPatchReflection.NodePositionField?.GetValue(__instance) ?? default(Rect));
                var kb = AnimatorDefaultSettings.Load();

                if ((isClick && currentEvent.control && currentEvent.clickCount == 2)
                    || (isKey && !ChainActive && !FanActive && kb.kbChainMode.Matches(currentEvent) && nodeRect.Contains(PatchGraphInputHandler._lastMousePosition)))
                {
                    BeginChain(nodeState, nodeRect);
                    currentEvent.Use();
                    return;
                }

                if (isClick && ChainActive && currentEvent.clickCount == 1 && !currentEvent.control && !currentEvent.shift)
                {
                    DispatchAddTransition(_chainSourceKind, _chainSourceSM, _chainSource, nodeState);
                    _chainSource = nodeState;
                    _chainSourceSM = null;
                    _chainSourceKind = SourceKind.State;
                    ChainSourceRect = nodeRect;
                    SnapTarget = null;
                    currentEvent.Use();
                    return;
                }

                if ((isClick && currentEvent.shift && currentEvent.clickCount == 2)
                    || (isKey && !ChainActive && !FanActive && kb.kbFanMode.Matches(currentEvent) && nodeRect.Contains(PatchGraphInputHandler._lastMousePosition)))
                {
                    BeginFan(nodeState, nodeRect);
                    currentEvent.Use();
                    return;
                }

                if (isClick && FanActive && currentEvent.clickCount == 1 && !currentEvent.control && !currentEvent.shift)
                {
                    if (SeededFanActive)
                        DispatchPasteTransition(_fanSourceKind, _fanSourceSM, _fanSource, nodeState, PatchTransitionCopyPaste.GetClipboard());
                    else
                        DispatchAddTransition(_fanSourceKind, _fanSourceSM, _fanSource, nodeState);
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
    // Right-drag from state node → release on destination to create transition; short right-click still opens context menu
    internal static class PatchRightDragTransition
    {
        internal static bool DragActive { get; private set; }
        internal static bool IsAnyStateSource { get; private set; }
        internal static bool IsEntrySource { get; private set; }

        internal static AnimatorState _pendingSourceState;
        internal static AnimatorStateMachine _pendingSM;
        internal static Rect _pendingSourceRect;
        static Vector2 _pendingStartPos;

        internal static bool IsPending => _pendingSourceState != null || IsAnyStateSource || IsEntrySource;
        internal static Vector2 PendingStartPos => _pendingStartPos;

        internal static void BeginPending(AnimatorState source, Rect sourceRect, AnimatorStateMachine sm, Vector2 startPos)
        {
            _pendingSourceState = source;
            _pendingSourceRect = sourceRect;
            _pendingSM = sm;
            _pendingStartPos = startPos;
        }

        internal static void BeginPendingAnyState(Rect sourceRect, AnimatorStateMachine sm, Vector2 startPos)
        {
            IsAnyStateSource = true;
            _pendingSourceRect = sourceRect;
            _pendingSM = sm;
            _pendingStartPos = startPos;
        }

        internal static void BeginPendingEntry(Rect sourceRect, AnimatorStateMachine sm, Vector2 startPos)
        {
            IsEntrySource = true;
            _pendingSourceRect = sourceRect;
            _pendingSM = sm;
            _pendingStartPos = startPos;
        }

        internal static void ActivateDrag()
        {
            DragActive = true;
            PatchStateChainTransition.SnapTarget = null;
        }

        internal static void Clear()
        {
            DragActive = false;
            IsAnyStateSource = false;
            IsEntrySource = false;
            _pendingSourceState = null;
            _pendingSM = null;
            _pendingSourceRect = Rect.zero;
            _pendingStartPos = Vector2.zero;
            PatchStateChainTransition.SnapTarget = null;
        }
    }

    // Ctrl+C to copy selected transitions, Ctrl+V on source state/AnyState, click destination/Exit to paste
    [HarmonyPatch]
    internal static class PatchTransitionCopyPaste
    {
        static AnimatorTransitionOps.TransitionData[] _clipboard;
        static AnimatorState _pasteSource;
        static AnimatorStateMachine _pasteSM;
        static bool _pasteFromAnyState;

        internal static bool PasteActive { get; private set; }
        internal static Rect PasteSourceRect { get; private set; }
        internal static bool HasClipboard => _clipboard != null && _clipboard.Length > 0;
        internal static int ClipboardCount => _clipboard?.Length ?? 0;
        internal static bool PasteFromAnyState => _pasteFromAnyState;
        internal static AnimatorState PasteSource => _pasteSource;
        internal static AnimatorStateMachine PasteSM => _pasteSM;
        internal static AnimatorTransitionOps.TransitionData[] GetClipboard() => _clipboard;

        /* Snapshots transition data at copy time so clipboard survives deletion of the originals. */
        internal static void SetClipboard(AnimatorStateTransition[] transitions) =>
            _clipboard = transitions?.Select(AnimatorTransitionOps.TransitionData.From).ToArray();

        /* Activates paste mode from a state source, recording the source state and its node rect for preview line drawing. */
        internal static void BeginPaste(AnimatorState source, Rect sourceRect, AnimatorStateMachine sm)
        {
            PasteActive = true;
            _pasteFromAnyState = false;
            _pasteSource = source;
            _pasteSM = sm;
            PasteSourceRect = sourceRect;
            PatchStateChainTransition.SnapTarget = null;
        }

        /* Activates paste mode from AnyState, recording the active SM and its node rect for preview line drawing. */
        internal static void BeginAnyStatePaste(AnimatorStateMachine sm, Rect sourceRect)
        {
            PasteActive = true;
            _pasteFromAnyState = true;
            _pasteSource = null;
            _pasteSM = sm;
            PasteSourceRect = sourceRect;
            PatchStateChainTransition.SnapTarget = null;
        }

        internal static void ClearPaste()
        {
            PasteActive = false;
            _pasteFromAnyState = false;
            _pasteSource = null;
            _pasteSM = null;
            PasteSourceRect = Rect.zero;
            PatchStateChainTransition.SnapTarget = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI",
                new[] { GraphPatchReflection.GraphGUIType });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            if (!PasteActive) return;
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || currentEvent.clickCount != 1) return;

                var destinationState = GraphPatchReflection.StateNodeStateField?.GetValue(__instance) as AnimatorState;
                if (destinationState == null) return;

                if (_pasteFromAnyState)
                    AnimatorTransitionOps.PasteAnyStateTransitions(_pasteSM, destinationState, _clipboard);
                else
                    AnimatorTransitionOps.PasteTransitions(_pasteSource, destinationState, _clipboard);
                ClearPaste();
                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Paste transitions error: {e}");
            }
        }

    }


    // Captures source sub-SM on copy; uses ObjectChangeEvents to detect paste at any time
    [HarmonyPatch]
    internal static class PatchCopySelectionToPasteboard
    {
        static AnimatorStateMachine _sourceSM;
        static AnimatorStateMachine _monitorActiveSM;
        static ChildAnimatorStateMachine[] _monitorSnapshot;

        internal static void ClearCopy()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            _sourceSM = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.CopySelectionToPasteboardMethod;

        [HarmonyPostfix]
        static void Postfix(object __instance, bool __result)
        {
            if (!__result) return;

            try
            {
                ObjectChangeEvents.changesPublished -= OnChangesPublished;
                _sourceSM = null;

                _sourceSM = Selection.objects
                    .OfType<AnimatorStateMachine>()
                    .FirstOrDefault();
                if (_sourceSM == null) return;

                PatchTransitionCopyPaste.SetClipboard(null);

                var getActiveSM = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                _monitorActiveSM = getActiveSM?.Invoke(__instance, null) as AnimatorStateMachine;
                _monitorSnapshot = _monitorActiveSM?.stateMachines.ToArray()
                    ?? new ChildAnimatorStateMachine[0];

                ObjectChangeEvents.changesPublished += OnChangesPublished;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub-SM copy frame capture failed: {e}");
            }
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_sourceSM == null || _monitorActiveSM == null) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateAssetObject) continue;
                stream.GetCreateAssetObjectEvent(i, out var eventData);
                if (EditorUtility.InstanceIDToObject(eventData.instanceId) is not AnimatorStateMachine) continue;

                var currentChildSMs = _monitorActiveSM.stateMachines;
                if (currentChildSMs.Length == _monitorSnapshot.Length) continue;

                var newChildSMs = currentChildSMs
                    .Where(childSM => !_monitorSnapshot.Any(snapshot => snapshot.stateMachine == childSM.stateMachine))
                    .ToArray();
                if (newChildSMs.Length == 0) continue;

                ObjectChangeEvents.changesPublished -= OnChangesPublished;
                ApplyFrames(newChildSMs[0].stateMachine);
                return;
            }
        }

        static void ApplyFrames(AnimatorStateMachine destinationSM)
        {
            try
            {
                var smMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
                PatchLayerCopyPaste.BuildSMMap(_sourceSM, destinationSM, smMap);

                var sourceController = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(_sourceSM));
                var destinationController = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(destinationSM));
                if (sourceController == null || destinationController == null) return;

                var sourceData = FrameLayoutData.GetOrCreate(sourceController, out _);
                var destinationData = FrameLayoutData.GetOrCreate(destinationController, out _);

                bool dirty = false;
                foreach (var frame in sourceData.frames.ToArray())
                {
                    if (!smMap.TryGetValue(frame.activeSM, out var mappedActiveSM)) continue;
                    destinationData.frames.Add(new FrameRect
                    {
                        title                 = frame.title,
                        comments              = frame.comments,
                        layerStateMachine     = FrameRenderer.LastRootLayerSM,
                        activeSM              = mappedActiveSM,
                        bounds                = frame.bounds,
                        color                 = frame.color,
                        locked                = frame.locked,
                        moveContentsWithFrame = frame.moveContentsWithFrame,
                        zLayer                = frame.zLayer,
                    });
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(destinationData);
                    AssetDatabase.SaveAssets();
                    FrameRenderer.InvalidateCache();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub-SM paste frame copy failed: {e}");
            }
            finally
            {
                _sourceSM = null;
            }
        }

    }
    // ─── Drag-and-drop clip onto existing node ────────────────────────────────────────────────────────────────────

    // Intercepts AnimatorStateMachine.AddState(name, position) during drag-and-drop.
    // Single clip on existing node: assigns clip without creating a new state.
    // Multiple clips: creates one state per clip, cascaded diagonally from drop position.
    [HarmonyPatch(typeof(AnimatorStateMachine), "AddState", new[] { typeof(string), typeof(Vector3) })]
    internal static class PatchAddStateDrop
    {
        static int[]   _activeDropClipIds   = Array.Empty<int>();
        static int     _activeDropCallIndex  = 0;
        static bool    _handlingDrop         = false;
        static Vector3 _dropBasePosition;
        internal static bool DropIntercepted = false;

        [HarmonyPrefix]
        static bool Prefix(AnimatorStateMachine __instance, Vector3 position, ref AnimatorState __result)
        {
            if (_handlingDrop) return true;
            if (PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return true;
            try
            {
                var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();
                if (clips.Length == 0) return true;

                // Single clip on existing node: assign without creating new state; otherwise create with sanitized name
                if (clips.Length == 1)
                {
                    const float nodeW = 200f, nodeH = 40f;
                    foreach (var childState in __instance.states)
                    {
                        var nodeRect = new Rect(childState.position.x, childState.position.y, nodeW, nodeH);
                        if (!nodeRect.Contains(new Vector2(position.x, position.y))) continue;

                        Undo.RegisterCompleteObjectUndo(childState.state, "Assign Motion Clip");
                        childState.state.motion = clips[0];
                        EditorUtility.SetDirty(childState.state);
                        __result = childState.state;
                        DropIntercepted = true;
                        return false;
                    }

                    var sanitizedSingleName = clips[0].name.Replace('.', '_');
                    _handlingDrop = true;
                    try
                    {
                        var newState = __instance.AddState(sanitizedSingleName, position);
                        if (newState != null)
                        {
                            Undo.RegisterCompleteObjectUndo(newState, "Drag Drop Clip");
                            newState.motion = clips[0];
                            EditorUtility.SetDirty(newState);
                        }
                        __result = newState;
                    }
                    finally { _handlingDrop = false; }
                    return false;
                }

                // Multiple clips: track call index per drop operation
                var clipIds = clips.Select(c => c.GetInstanceID()).ToArray();
                bool isSameDrop = clipIds.SequenceEqual(_activeDropClipIds) && _activeDropCallIndex < clips.Length;
                if (!isSameDrop)
                {
                    _activeDropClipIds  = clipIds;
                    _activeDropCallIndex = 0;
                }

                int callIndex = _activeDropCallIndex++;
                if (callIndex >= clips.Length) return true;
                if (callIndex == 0) _dropBasePosition = position;

                const float cascadeStepX = 40f;
                const float cascadeStepY = 65f;
                var cascadePosition = _dropBasePosition + new Vector3(callIndex * cascadeStepX, callIndex * cascadeStepY, 0f);

                _handlingDrop = true;
                try
                {
                    var newState = __instance.AddState(clips[callIndex].name.Replace('.', '_'), cascadePosition);
                    if (newState != null)
                    {
                        Undo.RegisterCompleteObjectUndo(newState, "Drag Drop Clips");
                        newState.motion = clips[callIndex];
                        EditorUtility.SetDirty(newState);
                    }
                    __result = newState;
                }
                finally { _handlingDrop = false; }
                return false;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] AddState drop error: {e}"); }
            return true;
        }
    }

    // ─── Ctrl+D duplicate with smart naming ──────────────────────────────────────────────────────────────────────────

    // Prefix snapshots SM state IDs; Postfix diffs and renames new states.
    // Unity unconditionally appends " 0" to duplicated names — we strip it and assign " N" (N >= 1) instead.
    [HarmonyPatch]
    internal static class PatchDuplicateSmartNaming
    {
        static HashSet<int> _snapshotIDs;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(GraphPatchReflection.GraphGUIType, "HandleEvents");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.ExecuteCommand || currentEvent.commandName != "Duplicate")
            {
                if (_snapshotIDs != null) _snapshotIDs = null;
                return;
            }
            try
            {
                var activeStateMachine = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod
                    ?.Invoke(__instance, null) as AnimatorStateMachine;
                _snapshotIDs = activeStateMachine == null
                    ? null
                    : new HashSet<int>(activeStateMachine.states.Select(childState => childState.state.GetInstanceID()));
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Duplicate snapshot error: {e}");
                _snapshotIDs = null;
            }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (_snapshotIDs == null) return;
            var snapshot = _snapshotIDs;
            _snapshotIDs = null;

            try
            {
                var activeStateMachine = AnimatorEditorInit.GetActiveStateMachineFromGraphGUIMethod
                    ?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeStateMachine == null) return;

                var newStates = activeStateMachine.states
                    .Where(childState => !snapshot.Contains(childState.state.GetInstanceID()))
                    .Select(childState => childState.state)
                    .ToList();
                if (newStates.Count == 0) return;

                var allCurrentNames = new HashSet<string>(
                    activeStateMachine.states.Select(childState => childState.state.name)
                );

                foreach (var newState in newStates)
                {
                    string baseName = StripTrailingNumber(StripUnityDuplicateSuffix(newState.name));
                    allCurrentNames.Remove(newState.name);
                    int n = 1;
                    string candidate;
                    do { candidate = baseName + " " + n++; } while (allCurrentNames.Contains(candidate));
                    Undo.RegisterCompleteObjectUndo(newState, "Rename Duplicated States");
                    newState.name = candidate;
                    allCurrentNames.Add(candidate);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Duplicate rename error: {e}");
            }
        }

        static string StripUnityDuplicateSuffix(string name) =>
            name.EndsWith(" 0") ? name.Substring(0, name.Length - 2) : name;

        static string StripTrailingNumber(string name)
        {
            int lastSpace = name.LastIndexOf(' ');
            if (lastSpace >= 0 && int.TryParse(name.Substring(lastSpace + 1), out _))
                return name.Substring(0, lastSpace);
            return name;
        }
    }
}
#endif
