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
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class FrameInteractionPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.OnGraphGUIMethod;

        enum DragState { None, Moving, Resizing }

        static DragState _dragState;
        static Vector2 _dragStartMouse;
        static Rect _dragStartBounds;
        static int _dragHandleIndex;
        static readonly Dictionary<FrameRect, Rect> _dragStartBoundsAll = new Dictionary<FrameRect, Rect>();
        static readonly List<FrameRect> _draggedFrames = new List<FrameRect>();
        static readonly HashSet<FrameRect> _dragCascadeFrames = new HashSet<FrameRect>();

        struct NodeSnapshot
        {
            public int[] stateIndices;
            public Vector3[] stateStartPositions;
            public int[] subSMIndices;
            public Vector3[] subSMStartPositions;
            public bool anyStateInFrame;
            public Vector3 anyStateStartPosition;
            public object entryNode;
            public Rect entryStartRect;
            public object exitNode;
            public Rect exitStartRect;
        }

        static readonly Dictionary<FrameRect, NodeSnapshot> _dragNodeSnapshots = new Dictionary<FrameRect, NodeSnapshot>();
        static object _lastGraphGUI;
        static FieldInfo _nodePositionField;
        static readonly HashSet<int> _movedStateIndices = new HashSet<int>();
        static readonly HashSet<int> _movedSubSMIndices = new HashSet<int>();
        static bool _movedAnyState;
        static bool _movedEntry;
        static bool _movedExit;

        static readonly List<FrameRect> _copiedFrames = new List<FrameRect>();

        internal static bool IsRenaming;
        static bool _renameJustStarted;
        static bool _renameFieldHadFocus;
        internal static string RenameBuffer;
        internal static bool IsPickingColor;
        internal static FrameRect ColorPickerTarget;
        internal static bool IsEditingComments;
        internal static FrameRect CommentsTarget;
        internal static string CommentsBuffer;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            _lastGraphGUI = __instance;
            var frameData = FrameRenderer.LastFrameData;
            var currentEvent = Event.current;
            var scrollPosition = FrameRenderer.LastScrollPosition;

            bool createdFrameData = false;
            if (frameData == null)
            {
                bool isPasteEvent = currentEvent.type == EventType.KeyDown && currentEvent.control
                    && currentEvent.keyCode == KeyCode.V && _copiedFrames.Count > 0;
                if (!isPasteEvent || FrameRenderer.LastController == null) return;
                frameData = FrameLayoutData.GetOrCreate(FrameRenderer.LastController, out createdFrameData);
            }

            // Inline rename text field
            if (IsRenaming && FrameRenderer.SingleSelected != null)
            {
                var selectedFrame = FrameRenderer.SingleSelected;
                var frameScreenRect = FrameRenderer.GraphToScreen(selectedFrame.bounds, scrollPosition);
                var renameRect = new Rect(frameScreenRect.x + 24, frameScreenRect.y - 8, frameScreenRect.width - 28, 36f);

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
                {
                    IsRenaming = false;
                    currentEvent.Use();
                    return;
                }

                // Enter commits; Shift+Enter passes through to TextArea as newline
                if (currentEvent.type == EventType.KeyDown &&
                    (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter) &&
                    !currentEvent.shift)
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Rename Frame");
                    selectedFrame.title = RenameBuffer;
                    EditorUtility.SetDirty(frameData);
                    IsRenaming = false;
                    currentEvent.Use();
                    return;
                }

                if (currentEvent.type == EventType.MouseDown && !renameRect.Contains(currentEvent.mousePosition))
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Rename Frame");
                    selectedFrame.title = RenameBuffer;
                    EditorUtility.SetDirty(frameData);
                    IsRenaming = false;
                    // fall through — let click deselect/select normally
                }
                else
                {
                    GUI.SetNextControlName("FrameRename");
                    RenameBuffer = GUI.TextArea(renameRect, RenameBuffer, EditorStyles.boldLabel);
                    if (_renameJustStarted)
                    {
                        EditorGUI.FocusTextInControl("FrameRename");
                        _renameJustStarted = false;
                        _renameFieldHadFocus = false;
                        return;
                    }
                    bool hasFocus = GUI.GetNameOfFocusedControl() == "FrameRename";
                    if (!_renameFieldHadFocus && hasFocus)
                    {
                        var textEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                        textEditor?.SelectAll();
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    }
                    _renameFieldHadFocus = hasFocus;
                    return;
                }
            }

            // Color picker overlay
            if (IsPickingColor && ColorPickerTarget != null)
            {
                var colorFrame = ColorPickerTarget;
                var colorFrameRect = FrameRenderer.GraphToScreen(colorFrame.bounds, scrollPosition);
                var colorFieldRect = new Rect(colorFrameRect.x, colorFrameRect.y - 24, 180, 18);

                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUI.ColorField(colorFieldRect, colorFrame.color);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Change Frame Color");
                    colorFrame.color = newColor;
                    EditorUtility.SetDirty(frameData);
                }

                if (currentEvent.type == EventType.MouseDown && !colorFieldRect.Contains(currentEvent.mousePosition))
                {
                    IsPickingColor = false;
                    ColorPickerTarget = null;
                    // fall through
                }
                else
                {
                    return;
                }
            }

            // Inline comments text area — below title area inside frame
            if (IsEditingComments && CommentsTarget != null)
            {
                var commentsFrame = CommentsTarget;
                var commentsFrameRect = FrameRenderer.GraphToScreen(commentsFrame.bounds, scrollPosition);
                var commentsEditRect = new Rect(commentsFrameRect.x + 24, commentsFrameRect.y + 24, commentsFrameRect.width - 28, 64f);

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
                {
                    IsEditingComments = false;
                    CommentsTarget = null;
                    currentEvent.Use();
                    return;
                }

                // Enter commits; Shift+Enter passes through to TextArea as newline
                if (currentEvent.type == EventType.KeyDown &&
                    (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter) &&
                    !currentEvent.shift)
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Edit Frame Comments");
                    commentsFrame.comments = CommentsBuffer;
                    EditorUtility.SetDirty(frameData);
                    IsEditingComments = false;
                    CommentsTarget = null;
                    currentEvent.Use();
                    return;
                }

                if (currentEvent.type == EventType.MouseDown && !commentsEditRect.Contains(currentEvent.mousePosition))
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Edit Frame Comments");
                    commentsFrame.comments = CommentsBuffer;
                    EditorUtility.SetDirty(frameData);
                    IsEditingComments = false;
                    CommentsTarget = null;
                    // fall through
                }
                else
                {
                    GUI.SetNextControlName("FrameComments");
                    CommentsBuffer = GUI.TextArea(commentsEditRect, CommentsBuffer);
                    EditorGUI.FocusTextInControl("FrameComments");
                    return;
                }
            }

            // Delete key — delete all selected
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Delete
                && FrameRenderer.SelectedFrames.Count > 0)
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Delete Frame");
                foreach (var frame in FrameRenderer.SelectedFrames.ToList())
                    frameData.frames.Remove(frame);
                FrameRenderer.SelectedFrames.Clear();
                EditorUtility.SetDirty(frameData);
                FrameLayoutData.RemoveIfEmpty(FrameRenderer.LastController);
                FrameRenderer.InvalidateCache();
                currentEvent.Use();
                return;
            }

            // Ctrl+C — copy all selected
            if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.C
                && FrameRenderer.SelectedFrames.Count > 0)
            {
                _copiedFrames.Clear();
                _copiedFrames.AddRange(FrameRenderer.SelectedFrames);
                currentEvent.Use();
                return;
            }

            // Ctrl+V — paste all copied, offset so top-left corner of group lands at cursor
            if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.V
                && _copiedFrames.Count > 0)
            {
                float minX = _copiedFrames.Min(frame => frame.bounds.x);
                float minY = _copiedFrames.Min(frame => frame.bounds.y);
                float offsetX = currentEvent.mousePosition.x + FrameRenderer.LastScrollPosition.x - minX;
                float offsetY = currentEvent.mousePosition.y + FrameRenderer.LastScrollPosition.y - minY;

                Undo.RegisterCompleteObjectUndo(frameData, "Paste Frames");
                FrameRenderer.SelectedFrames.Clear();
                foreach (var copiedFrame in _copiedFrames)
                {
                    var pastedFrame = new FrameRect
                    {
                        title = copiedFrame.title,
                        comments = copiedFrame.comments,
                        color = copiedFrame.color,
                        locked = copiedFrame.locked,
                        moveContentsWithFrame = copiedFrame.moveContentsWithFrame,
                        zLayer = copiedFrame.zLayer,
                        bounds = new Rect(
                            copiedFrame.bounds.x + offsetX,
                            copiedFrame.bounds.y + offsetY,
                            copiedFrame.bounds.width,
                            copiedFrame.bounds.height),
                        layerStateMachine = FrameRenderer.LastRootLayerSM,
                        activeSM = FrameRenderer.LastActiveSM,
                    };
                    frameData.frames.Add(pastedFrame);
                    FrameRenderer.SelectedFrames.Add(pastedFrame);
                }
                EditorUtility.SetDirty(frameData);
                if (createdFrameData) AssetDatabase.SaveAssets();
                FrameRenderer.InvalidateCache();
                EditorWindow.GetWindow(AnimatorEditorInit.AnimatorControllerToolType)?.Repaint();
                currentEvent.Use();
                return;
            }

            // F2 — rename selected frame title
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F2
                && FrameRenderer.SingleSelected != null && !FrameRenderer.SingleSelected.locked)
            {
                IsRenaming = true;
                _renameJustStarted = true;
                _renameFieldHadFocus = false;
                RenameBuffer = FrameRenderer.SingleSelected.title ?? "";
                currentEvent.Use();
                return;
            }

            // F3 — edit comments on selected frame
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F3
                && FrameRenderer.SingleSelected != null && !FrameRenderer.SingleSelected.locked)
            {
                IsEditingComments = true;
                CommentsTarget = FrameRenderer.SingleSelected;
                CommentsBuffer = FrameRenderer.SingleSelected.comments ?? "";
                currentEvent.Use();
                return;
            }

            // Ctrl+[ / Ctrl+] — z-layer order  (Shift = move to extreme)
            if (currentEvent.type == EventType.KeyDown && currentEvent.control
                && FrameRenderer.SelectedFrames.Count > 0
                && (currentEvent.keyCode == KeyCode.LeftBracket || currentEvent.keyCode == KeyCode.RightBracket))
            {
                bool moveUp = currentEvent.keyCode == KeyCode.RightBracket;
                bool extreme = currentEvent.shift;

                Undo.RegisterCompleteObjectUndo(frameData, "Move Frame Z-Layer");
                if (extreme)
                {
                    int nonSelectedMaxZ = frameData.frames
                        .Where(f => !FrameRenderer.SelectedFrames.Contains(f))
                        .Select(f => f.zLayer)
                        .DefaultIfEmpty(-1)
                        .Max();
                    foreach (var selectedFrame in FrameRenderer.SelectedFrames)
                        selectedFrame.zLayer = moveUp ? nonSelectedMaxZ + 1 : 0;
                }
                else
                {
                    foreach (var selectedFrame in FrameRenderer.SelectedFrames)
                        selectedFrame.zLayer = moveUp ? selectedFrame.zLayer + 1 : Mathf.Max(0, selectedFrame.zLayer - 1);
                }
                EditorUtility.SetDirty(frameData);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                currentEvent.Use();
                return;
            }

            // Drag continuation
            if (_dragState != DragState.None)
            {
                if (currentEvent.type == EventType.MouseDrag)
                {
                    HandleDragUpdate(currentEvent.mousePosition);
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.type == EventType.MouseUp)
                {
                    EditorUtility.SetDirty(frameData);
                    if (_dragNodeSnapshots.Count > 0 && FrameRenderer.LastActiveSM != null)
                    {
                        SyncSpecialNodePositionsToSM(FrameRenderer.LastActiveSM);
                        EditorUtility.SetDirty(FrameRenderer.LastActiveSM);
                    }
                    _dragNodeSnapshots.Clear();
                    _dragState = DragState.None;
                    currentEvent.Use();
                    return;
                }
            }

            if (currentEvent.type != EventType.MouseDown) return;

            var mousePosition = currentEvent.mousePosition;
            bool isShift = currentEvent.shift;

            // Hit-test frames — highest z-layer first, then list order as tiebreaker
            var framesInHitTestOrder = frameData.frames
                .Select((f, i) => (frame: f, listIndex: i))
                .OrderByDescending(pair => pair.frame.zLayer)
                .ThenByDescending(pair => pair.listIndex)
                .Select(pair => pair.frame)
                .ToList();

            foreach (var frame in framesInHitTestOrder)
            {
                if (frame.activeSM != FrameRenderer.LastActiveSM) continue;

                var screenRect = FrameRenderer.GraphToScreen(frame.bounds, scrollPosition);

                // Lock icon
                var lockIconRect = new Rect(screenRect.x + 2, screenRect.y - 2, 18, 18);
                if (lockIconRect.Contains(mousePosition))
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Toggle Frame Lock");
                    frame.locked = !frame.locked;
                    CascadeLockState(frameData, frame, frame.locked);
                    EditorUtility.SetDirty(frameData);
                    currentEvent.Use();
                    return;
                }

                if (frame.locked) continue;

                // Resize handles — only when this frame is the single selected frame
                if (FrameRenderer.SingleSelected == frame)
                {
                    var handleRects = FrameRenderer.GetHandleRects(screenRect);
                    for (int handleIndex = 0; handleIndex < handleRects.Length; handleIndex++)
                    {
                        if (!handleRects[handleIndex].Contains(mousePosition)) continue;
                        FrameRenderer.SelectedFrames.Clear();
                        FrameRenderer.SelectedFrames.Add(frame);
                        Undo.RegisterCompleteObjectUndo(frameData, "Resize Frame");
                        _dragState = DragState.Resizing;
                        _dragStartMouse = mousePosition;
                        _dragStartBounds = frame.bounds;
                        _dragHandleIndex = handleIndex;
                        currentEvent.Use();
                        return;
                    }
                }

                // Full frame — move if no node at cursor; yield to node clicks
                if (screenRect.Contains(mousePosition))
                {
                    var graphMouse = mousePosition + scrollPosition;
                    var activeSM = FrameRenderer.LastActiveSM;
                    bool nodeAtMouse = activeSM != null && (
                        activeSM.states.Any(childState =>
                            new Rect(childState.position.x, childState.position.y, 200, 44).Contains(graphMouse)) ||
                        activeSM.stateMachines.Any(childSM =>
                            new Rect(childSM.position.x, childSM.position.y, 200, 44).Contains(graphMouse)) ||
                        new Rect(activeSM.anyStatePosition.x, activeSM.anyStatePosition.y, 160, 30).Contains(graphMouse) ||
                        new Rect(activeSM.entryPosition.x, activeSM.entryPosition.y, 160, 30).Contains(graphMouse) ||
                        new Rect(activeSM.exitPosition.x, activeSM.exitPosition.y, 160, 30).Contains(graphMouse));
                    bool transitionAtMouse = activeSM != null && IsTransitionAtMouse(graphMouse, activeSM);

                    if (!nodeAtMouse && !transitionAtMouse)
                    {
                        if (currentEvent.button == 1)
                        {
                            FrameRenderer.SelectedFrames.Clear();
                            FrameRenderer.SelectedFrames.Add(frame);
                            var preContextSelectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
                            var preContextSelectedSubSMs = Selection.objects.OfType<AnimatorStateMachine>()
                                .Where(sm => sm != FrameRenderer.LastActiveSM)
                                .ToArray();
                            var preContextSpecialNodePositions = CaptureSpecialNodePositions();
                            Selection.objects = Array.Empty<UnityEngine.Object>();
                            ShowContextMenu(frame, frameData, preContextSelectedStates, preContextSelectedSubSMs, preContextSpecialNodePositions);
                            currentEvent.Use();
                            return;
                        }

                        if (isShift)
                        {
                            if (FrameRenderer.IsSelected(frame))
                                FrameRenderer.SelectedFrames.Remove(frame);
                            else
                                FrameRenderer.SelectedFrames.Add(frame);
                            currentEvent.Use();
                            return;
                        }

                        if (currentEvent.button == 0)
                        {
                            if (!FrameRenderer.IsSelected(frame))
                            {
                                FrameRenderer.SelectedFrames.Clear();
                                FrameRenderer.SelectedFrames.Add(frame);
                            }
                            Selection.objects = Array.Empty<UnityEngine.Object>();

                            Undo.RegisterCompleteObjectUndo(frameData, "Move Frame");
                            _dragState = DragState.Moving;
                            _dragStartMouse = mousePosition;
                            _dragStartBoundsAll.Clear();
                            foreach (var otherFrame in frameData.frames)
                                _dragStartBoundsAll[otherFrame] = otherFrame.bounds;
                            _draggedFrames.Clear();
                            _draggedFrames.AddRange(FrameRenderer.SelectedFrames);
                            _dragCascadeFrames.Clear();
                            BuildCascadeSet(frameData, _draggedFrames, _dragCascadeFrames);
                            SnapshotNodesForDrag();
                            currentEvent.Use();
                            return;
                        }
                    }
                    else if (nodeAtMouse || transitionAtMouse)
                    {
                        FrameRenderer.SelectedFrames.Clear();
                        IsRenaming = false;
                    }
                    return; // don't consume — let Unity handle node/transition/empty click
                }
            }

            // Empty space or node — deselect all
            FrameRenderer.SelectedFrames.Clear();
            IsRenaming = false;
        }

        static void SyncSpecialNodePositionsToSM(AnimatorStateMachine activeSM)
        {
            try
            {
                object entryNode = null, exitNode = null;
                foreach (var snapshot in _dragNodeSnapshots.Values)
                {
                    if (snapshot.entryNode != null) entryNode = snapshot.entryNode;
                    if (snapshot.exitNode != null) exitNode = snapshot.exitNode;
                }
                if (entryNode == null && exitNode == null) return;

                var serializedSM = new SerializedObject(activeSM);

                if (entryNode != null && _nodePositionField != null)
                {
                    var rect = (Rect)_nodePositionField.GetValue(entryNode);
                    var prop = serializedSM.FindProperty("m_EntryPosition");
                    if (prop != null) prop.vector3Value = new Vector3(rect.x, rect.y, 0);
                }
                if (exitNode != null && _nodePositionField != null)
                {
                    var rect = (Rect)_nodePositionField.GetValue(exitNode);
                    var prop = serializedSM.FindProperty("m_ExitPosition");
                    if (prop != null) prop.vector3Value = new Vector3(rect.x, rect.y, 0);
                }
                serializedSM.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] SyncSpecialNodePositions error: {e}"); }
        }

        static void SnapshotNodesForDrag()
        {
            _dragNodeSnapshots.Clear();
            var activeSM = FrameRenderer.LastActiveSM;
            if (activeSM == null) return;
            var frameData = FrameRenderer.LastFrameData;
            if (frameData == null) return;

            bool anyFrameMovesNodes = false;
            foreach (var frame in frameData.frames)
                if (frame.moveContentsWithFrame) { anyFrameMovesNodes = true; break; }
            if (!anyFrameMovesNodes) return;

            Undo.RegisterCompleteObjectUndo(activeSM, "Move Frame");

            foreach (var selectedFrame in frameData.frames)
            {
                if (!selectedFrame.moveContentsWithFrame) continue;

                var stateMatches = activeSM.states
                    .Select((childState, index) => (childState, index))
                    .Where(pair => selectedFrame.bounds.Contains(new Vector2(pair.childState.position.x, pair.childState.position.y)))
                    .ToArray();

                var subSMMatches = activeSM.stateMachines
                    .Select((childSM, index) => (childSM, index))
                    .Where(pair => selectedFrame.bounds.Contains(new Vector2(pair.childSM.position.x, pair.childSM.position.y)))
                    .ToArray();

                object foundEntryNode = null, foundExitNode = null;
                Rect entryStartRect = default, exitStartRect = default;

                var graphObj = _lastGraphGUI != null
                    ? (Traverse.Create(_lastGraphGUI).Property("graph").GetValue()
                       ?? Traverse.Create(_lastGraphGUI).Field("graph").GetValue()
                       ?? Traverse.Create(_lastGraphGUI).Field("m_Graph").GetValue())
                    : null;
                if (graphObj != null)
                {
                    var nodes = (Traverse.Create(graphObj).Property("nodes").GetValue()
                                 ?? Traverse.Create(graphObj).Field("nodes").GetValue()) as System.Collections.IEnumerable;
                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            _nodePositionField ??= AccessTools.Field(node.GetType(), "position");
                            var nodeRect = _nodePositionField != null ? (Rect)_nodePositionField.GetValue(node) : default;
                            if (AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(node) ?? false)
                            {
                                if (selectedFrame.bounds.Contains(new Vector2(nodeRect.x, nodeRect.y)))
                                { foundEntryNode = node; entryStartRect = nodeRect; }
                            }
                            else if (AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(node) ?? false)
                            {
                                if (selectedFrame.bounds.Contains(new Vector2(nodeRect.x, nodeRect.y)))
                                { foundExitNode = node; exitStartRect = nodeRect; }
                            }
                        }
                    }
                }

                _dragNodeSnapshots[selectedFrame] = new NodeSnapshot
                {
                    stateIndices = stateMatches.Select(pair => pair.index).ToArray(),
                    stateStartPositions = stateMatches.Select(pair => pair.childState.position).ToArray(),
                    subSMIndices = subSMMatches.Select(pair => pair.index).ToArray(),
                    subSMStartPositions = subSMMatches.Select(pair => pair.childSM.position).ToArray(),
                    anyStateInFrame = selectedFrame.bounds.Contains(new Vector2(activeSM.anyStatePosition.x, activeSM.anyStatePosition.y)),
                    anyStateStartPosition = activeSM.anyStatePosition,
                    entryNode = foundEntryNode,
                    entryStartRect = entryStartRect,
                    exitNode = foundExitNode,
                    exitStartRect = exitStartRect,
                };
            }
        }

        static void HandleDragUpdate(Vector2 mousePosition)
        {
            var graphDelta = mousePosition - _dragStartMouse;

            if (_dragState == DragState.Moving)
            {
                var activeSM = FrameRenderer.LastActiveSM;
                ChildAnimatorState[] states = null;
                ChildAnimatorStateMachine[] subSMs = null;
                _movedStateIndices.Clear();
                _movedSubSMIndices.Clear();
                _movedAnyState = false;
                _movedEntry = false;
                _movedExit = false;

                // Every frame in the cascade set (directly dragged + carried children) moves by the
                // identical snapped delta, which preserves each frame's relative position inside its carrier.
                foreach (var cascadeFrame in _dragCascadeFrames)
                {
                    if (!_dragStartBoundsAll.TryGetValue(cascadeFrame, out var startBounds)) continue;
                    float snappedFrameX = Mathf.Round((startBounds.x + graphDelta.x) / 10f) * 10f;
                    float snappedFrameY = Mathf.Round((startBounds.y + graphDelta.y) / 10f) * 10f;
                    cascadeFrame.bounds = new Rect(snappedFrameX, snappedFrameY, startBounds.width, startBounds.height);
                }

                foreach (var frame in _dragCascadeFrames)
                {
                    if (!_dragStartBoundsAll.TryGetValue(frame, out var startBounds)) continue;

                    if (!frame.moveContentsWithFrame) continue;
                    if (activeSM == null) continue;
                    if (!_dragNodeSnapshots.TryGetValue(frame, out var snapshot)) continue;

                    states ??= activeSM.states;
                    subSMs ??= activeSM.stateMachines;
                    var offset = new Vector3(frame.bounds.x - startBounds.x, frame.bounds.y - startBounds.y, 0);

                    for (int i = 0; i < snapshot.stateIndices.Length; i++)
                    {
                        int stateIndex = snapshot.stateIndices[i];
                        if (!_movedStateIndices.Add(stateIndex)) continue;
                        var childState = states[stateIndex];
                        childState.position = snapshot.stateStartPositions[i] + offset;
                        states[stateIndex] = childState;
                    }

                    for (int i = 0; i < snapshot.subSMIndices.Length; i++)
                    {
                        int smIndex = snapshot.subSMIndices[i];
                        if (!_movedSubSMIndices.Add(smIndex)) continue;
                        var childSM = subSMs[smIndex];
                        childSM.position = snapshot.subSMStartPositions[i] + offset;
                        subSMs[smIndex] = childSM;
                    }

                    if (snapshot.anyStateInFrame && !_movedAnyState)
                    {
                        _movedAnyState = true;
                        activeSM.anyStatePosition = snapshot.anyStateStartPosition + offset;
                    }
                    if (snapshot.entryNode != null && !_movedEntry)
                    {
                        _movedEntry = true;
                        _nodePositionField?.SetValue(snapshot.entryNode, new Rect(
                            snapshot.entryStartRect.x + offset.x,
                            snapshot.entryStartRect.y + offset.y,
                            snapshot.entryStartRect.width,
                            snapshot.entryStartRect.height));
                    }
                    if (snapshot.exitNode != null && !_movedExit)
                    {
                        _movedExit = true;
                        _nodePositionField?.SetValue(snapshot.exitNode, new Rect(
                            snapshot.exitStartRect.x + offset.x,
                            snapshot.exitStartRect.y + offset.y,
                            snapshot.exitStartRect.width,
                            snapshot.exitStartRect.height));
                    }
                }

                if (states != null && activeSM != null) activeSM.states = states;
                if (subSMs != null && activeSM != null) activeSM.stateMachines = subSMs;
            }
            else if (_dragState == DragState.Resizing)
            {
                var frame = FrameRenderer.SingleSelected;
                if (frame == null) return;
                var newBounds = _dragStartBounds;
                switch (_dragHandleIndex)
                {
                    case 0: newBounds.xMin += graphDelta.x; newBounds.yMin += graphDelta.y; break;
                    case 1: newBounds.xMax += graphDelta.x; newBounds.yMin += graphDelta.y; break;
                    case 2: newBounds.xMin += graphDelta.x; newBounds.yMax += graphDelta.y; break;
                    case 3: newBounds.xMax += graphDelta.x; newBounds.yMax += graphDelta.y; break;
                    case 4: newBounds.yMin += graphDelta.y; break;
                    case 5: newBounds.yMax += graphDelta.y; break;
                    case 6: newBounds.xMin += graphDelta.x; break;
                    case 7: newBounds.xMax += graphDelta.x; break;
                }
                newBounds.xMin = Mathf.Round(newBounds.xMin / 10f) * 10f;
                newBounds.yMin = Mathf.Round(newBounds.yMin / 10f) * 10f;
                newBounds.xMax = Mathf.Round(newBounds.xMax / 10f) * 10f;
                newBounds.yMax = Mathf.Round(newBounds.yMax / 10f) * 10f;
                if (newBounds.width < 120f) { if (_dragHandleIndex == 0 || _dragHandleIndex == 2 || _dragHandleIndex == 6) newBounds.xMin = newBounds.xMax - 120f; else newBounds.xMax = newBounds.xMin + 120f; }
                if (newBounds.height < 80f) { if (_dragHandleIndex == 0 || _dragHandleIndex == 1 || _dragHandleIndex == 4) newBounds.yMin = newBounds.yMax - 80f; else newBounds.yMax = newBounds.yMin + 80f; }
                frame.bounds = newBounds;
            }
        }

        // Shared BFS over frames strictly-higher-zLayer and contained (top-left corner inside bounds) within
        // the current frame. canDescendFrom gates whether a dequeued frame's own contents get walked;
        // isEligible filters which contained candidates count as hits; onVisit fires once per hit.
        static void CascadeWalk(FrameLayoutData frameData, IEnumerable<FrameRect> seeds, HashSet<FrameRect> visited,
            Func<FrameRect, bool> canDescendFrom, Func<FrameRect, bool> isEligible, Action<FrameRect> onVisit)
        {
            var queue = new Queue<FrameRect>(seeds);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!canDescendFrom(current)) continue;

                foreach (var candidate in frameData.frames)
                {
                    if (visited.Contains(candidate)) continue;
                    if (candidate.zLayer <= current.zLayer) continue;
                    if (!current.bounds.Contains(new Vector2(candidate.bounds.x, candidate.bounds.y))) continue;
                    if (!isEligible(candidate)) continue;

                    visited.Add(candidate);
                    onVisit(candidate);
                    queue.Enqueue(candidate);
                }
            }
        }

        // Frames a mover carries along like a container: strictly-higher-zLayer, unlocked frames whose
        // top-left corner sits inside the mover's bounds. Recursive (a carried frame carries its own
        // contents in turn) but only descends through a level if that level has moveContentsWithFrame set.
        // All frames in the resulting set later move by the identical delta, which is what keeps each
        // carried frame's position relative to its carrier unchanged.
        static void BuildCascadeSet(FrameLayoutData frameData, List<FrameRect> initialMovers, HashSet<FrameRect> result)
        {
            foreach (var mover in initialMovers)
                result.Add(mover);

            CascadeWalk(frameData, initialMovers, result,
                canDescendFrom: mover => mover.moveContentsWithFrame,
                isEligible: candidate => !candidate.locked,
                onVisit: _ => { });
        }

        // Propagates a lock/unlock to strictly-higher-zLayer frames contained within the toggled frame,
        // so the user doesn't have to lock every frame stacked on top of it individually.
        static void CascadeLockState(FrameLayoutData frameData, FrameRect frame, bool locked)
        {
            CascadeWalk(frameData, new[] { frame }, new HashSet<FrameRect> { frame },
                canDescendFrom: _ => true,
                isEligible: _ => true,
                onVisit: candidate => candidate.locked = locked);
        }

        internal static Vector3[] CaptureSpecialNodePositions()
        {
            var activeSM = FrameRenderer.LastActiveSM;
            if (activeSM == null) return Array.Empty<Vector3>();
            var positions = new List<Vector3>();
            foreach (var selectedObject in Selection.objects)
            {
                if (AnimatorEditorInit.AnyStateNodeType?.IsInstanceOfType(selectedObject) ?? false)
                    positions.Add(activeSM.anyStatePosition);
                else if (AnimatorEditorInit.EntryNodeType?.IsInstanceOfType(selectedObject) ?? false)
                    positions.Add(activeSM.entryPosition);
                else if (AnimatorEditorInit.ExitNodeType?.IsInstanceOfType(selectedObject) ?? false)
                    positions.Add(activeSM.exitPosition);
            }
            return positions.ToArray();
        }

        static void ShowContextMenu(FrameRect frame, FrameLayoutData frameData,
            AnimatorState[] selectedStates = null, AnimatorStateMachine[] selectedSubSMs = null,
            Vector3[] specialNodePositions = null)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(L10n.Get("context_menu.frame_rename")), false, () =>
            {
                IsRenaming = true;
                RenameBuffer = frame.title;
            });
            menu.AddItem(new GUIContent(L10n.Get("context_menu.frame_edit_comments")), false, () =>
            {
                IsEditingComments = true;
                CommentsTarget = frame;
                CommentsBuffer = frame.comments ?? "";
            });
            menu.AddItem(new GUIContent(L10n.Get("context_menu.frame_color")), false, () =>
            {
                IsPickingColor = true;
                ColorPickerTarget = frame;
            });
            int maxZLayerAmongOthers = frameData.frames
                .Where(f => f != frame)
                .Select(f => f.zLayer)
                .DefaultIfEmpty(-1)
                .Max();

            string zLayerTop = $"{L10n.Get("context_menu.frame_zlayer")}/{L10n.Get("context_menu.frame_zlayer_top")}";
            string zLayerUp = $"{L10n.Get("context_menu.frame_zlayer")}/{L10n.Get("context_menu.frame_zlayer_up")}";
            string zLayerDown = $"{L10n.Get("context_menu.frame_zlayer")}/{L10n.Get("context_menu.frame_zlayer_down")}";
            string zLayerBottom = $"{L10n.Get("context_menu.frame_zlayer")}/{L10n.Get("context_menu.frame_zlayer_bottom")}";

            if (frame.zLayer > maxZLayerAmongOthers)
                menu.AddDisabledItem(new GUIContent(zLayerTop));
            else
                menu.AddItem(new GUIContent(zLayerTop), false, () =>
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Move Frame Z-Layer to Top");
                    frame.zLayer = maxZLayerAmongOthers + 1;
                    EditorUtility.SetDirty(frameData);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });

            menu.AddItem(new GUIContent(zLayerUp), false, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Move Frame Z-Layer Up");
                frame.zLayer++;
                EditorUtility.SetDirty(frameData);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            });

            if (frame.zLayer > 0)
                menu.AddItem(new GUIContent(zLayerDown), false, () =>
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Move Frame Z-Layer Down");
                    frame.zLayer--;
                    EditorUtility.SetDirty(frameData);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });
            else
                menu.AddDisabledItem(new GUIContent(zLayerDown));

            if (frame.zLayer > 0)
                menu.AddItem(new GUIContent(zLayerBottom), false, () =>
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Move Frame Z-Layer to Bottom");
                    frame.zLayer = 0;
                    EditorUtility.SetDirty(frameData);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });
            else
                menu.AddDisabledItem(new GUIContent(zLayerBottom));

            selectedStates ??= Array.Empty<AnimatorState>();
            selectedSubSMs ??= Array.Empty<AnimatorStateMachine>();
            specialNodePositions ??= Array.Empty<Vector3>();
            bool hasNodeSelection = selectedStates.Length > 0 || selectedSubSMs.Length > 0 || specialNodePositions.Length > 0;

            if (hasNodeSelection)
            {
                menu.AddItem(new GUIContent(L10n.Get("context_menu.frame_fit_selected")), false, () =>
                    FitFrameToSelected(frame, frameData, selectedStates, selectedSubSMs, specialNodePositions));
            }

            menu.AddItem(new GUIContent(L10n.Get("context_menu.frame_move_nodes")), frame.moveContentsWithFrame, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Toggle Move Contents with Frame");
                frame.moveContentsWithFrame = !frame.moveContentsWithFrame;
                EditorUtility.SetDirty(frameData);
            });

            menu.AddItem(new GUIContent(L10n.Get(frame.locked ? "context_menu.frame_unlock" : "context_menu.frame_lock")), false, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Toggle Frame Lock");
                frame.locked = !frame.locked;
                CascadeLockState(frameData, frame, frame.locked);
                EditorUtility.SetDirty(frameData);
            });

            int selectedCount = FrameRenderer.SelectedFrames.Count;
            string deleteLabel = selectedCount > 1
                ? string.Format(L10n.Get("context_menu.frame_delete_multi"), selectedCount)
                : L10n.Get("context_menu.frame_delete");
            menu.AddItem(new GUIContent(deleteLabel), false, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Delete Frame");
                if (selectedCount > 1)
                {
                    foreach (var selectedFrame in FrameRenderer.SelectedFrames.ToList())
                        frameData.frames.Remove(selectedFrame);
                    FrameRenderer.SelectedFrames.Clear();
                }
                else
                {
                    frameData.frames.Remove(frame);
                    FrameRenderer.SelectedFrames.Remove(frame);
                }
                EditorUtility.SetDirty(frameData);
                FrameLayoutData.RemoveIfEmpty(FrameRenderer.LastController);
                FrameRenderer.InvalidateCache();
            });
            menu.ShowAsContext();
        }

        internal static bool TryComputeSelectionBounds(AnimatorState[] selectedStates,
            AnimatorStateMachine[] selectedSubSMs, Vector3[] specialNodePositions, out Rect bounds)
        {
            const float nodeWidth = 200f;
            const float nodeHeight = 44f;
            const float padding = 30f;

            var activeSM = FrameRenderer.LastActiveSM;
            if (activeSM == null) { bounds = default; return false; }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var state in selectedStates)
            {
                var childState = activeSM.states.FirstOrDefault(cs => cs.state == state);
                if (childState.state == null) continue;
                minX = Mathf.Min(minX, childState.position.x);
                minY = Mathf.Min(minY, childState.position.y);
                maxX = Mathf.Max(maxX, childState.position.x + nodeWidth);
                maxY = Mathf.Max(maxY, childState.position.y + nodeHeight);
            }

            foreach (var subSM in selectedSubSMs)
            {
                if (subSM == activeSM) continue;
                var childSM = activeSM.stateMachines.FirstOrDefault(cs => cs.stateMachine == subSM);
                if (childSM.stateMachine == null) continue;
                minX = Mathf.Min(minX, childSM.position.x);
                minY = Mathf.Min(minY, childSM.position.y);
                maxX = Mathf.Max(maxX, childSM.position.x + nodeWidth);
                maxY = Mathf.Max(maxY, childSM.position.y + nodeHeight);
            }

            const float specialNodeWidth = 160f;
            const float specialNodeHeight = 30f;

            foreach (var specialNodePosition in specialNodePositions)
            {
                minX = Mathf.Min(minX, specialNodePosition.x);
                minY = Mathf.Min(minY, specialNodePosition.y);
                maxX = Mathf.Max(maxX, specialNodePosition.x + specialNodeWidth);
                maxY = Mathf.Max(maxY, specialNodePosition.y + specialNodeHeight);
            }

            if (minX == float.MaxValue) { bounds = default; return false; }

            bounds = new Rect(minX - padding, minY - padding, (maxX - minX) + padding * 2f, (maxY - minY) + padding * 2f);
            return true;
        }

        static bool IsTransitionAtMouse(Vector2 graphMouse, AnimatorStateMachine activeSM)
        {
            const float hitDistance = 10f;
            const float nodeW = 200f, nodeH = 44f;
            const float specialW = 160f, specialH = 30f;

            Vector2 NodeCenter(Vector3 pos) => new Vector2(pos.x + nodeW * 0.5f, pos.y + nodeH * 0.5f);
            Vector2 SpecialCenter(Vector3 pos) => new Vector2(pos.x + specialW * 0.5f, pos.y + specialH * 0.5f);

            var stateCenters = new Dictionary<AnimatorState, Vector2>();
            foreach (var childState in activeSM.states)
                stateCenters[childState.state] = NodeCenter(childState.position);

            var subSMCenters = new Dictionary<AnimatorStateMachine, Vector2>();
            foreach (var childSM in activeSM.stateMachines)
                subSMCenters[childSM.stateMachine] = NodeCenter(childSM.position);

            var exitCenter = SpecialCenter(activeSM.exitPosition);
            var anyStateCenter = SpecialCenter(activeSM.anyStatePosition);
            var entryCenter = SpecialCenter(activeSM.entryPosition);

            bool SegmentHit(Vector2 from, Vector2 to) =>
                DistancePointToSegment(graphMouse, from, to) < hitDistance;

            bool TryGetTransitionDest(AnimatorStateTransition transition, out Vector2 dest)
            {
                if (transition.destinationState != null && stateCenters.TryGetValue(transition.destinationState, out dest)) return true;
                if (transition.destinationStateMachine != null && subSMCenters.TryGetValue(transition.destinationStateMachine, out dest)) return true;
                if (transition.isExit) { dest = exitCenter; return true; }
                dest = default;
                return false;
            }

            foreach (var childState in activeSM.states)
            {
                var sourceCenter = stateCenters[childState.state];
                foreach (var transition in childState.state.transitions)
                    if (TryGetTransitionDest(transition, out var dest) && SegmentHit(sourceCenter, dest))
                        return true;
            }

            foreach (var anyTransition in activeSM.anyStateTransitions)
                if (TryGetTransitionDest(anyTransition, out var dest) && SegmentHit(anyStateCenter, dest))
                    return true;

            foreach (var entryTransition in activeSM.entryTransitions)
            {
                if (entryTransition.destinationState != null
                    && stateCenters.TryGetValue(entryTransition.destinationState, out var dest)
                    && SegmentHit(entryCenter, dest))
                    return true;
                if (entryTransition.destinationStateMachine != null
                    && subSMCenters.TryGetValue(entryTransition.destinationStateMachine, out dest)
                    && SegmentHit(entryCenter, dest))
                    return true;
            }

            return false;
        }

        static float DistancePointToSegment(Vector2 point, Vector2 segA, Vector2 segB)
        {
            var ab = segB - segA;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 0.0001f) return (point - segA).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - segA, ab) / sqrLen);
            return (point - (segA + t * ab)).magnitude;
        }

        static void FitFrameToSelected(FrameRect frame, FrameLayoutData frameData,
            AnimatorState[] selectedStates, AnimatorStateMachine[] selectedSubSMs, Vector3[] specialNodePositions)
        {
            if (!TryComputeSelectionBounds(selectedStates, selectedSubSMs, specialNodePositions, out var fitBounds)) return;
            Undo.RegisterCompleteObjectUndo(frameData, "Fit Frame to Selected");
            frame.bounds = fitBounds;
            EditorUtility.SetDirty(frameData);
        }
    }

}
#endif
