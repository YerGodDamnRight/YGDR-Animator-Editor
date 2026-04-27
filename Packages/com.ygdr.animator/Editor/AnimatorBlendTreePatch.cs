#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
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
            if (PatchGraphDoubleClickCreate.AnimWindow != null)
                PatchGraphDoubleClickCreate.AnimWindow.wantsMouseMove = false;
        }
    }

    // NodeGUI fires per-node before HandleNodeInput (which calls Event.Use()).
    // Prefix captures MouseDown; postfix draws custom name label and rename field.
    // InNodeGUI gates GetNodeStyle color patch to blend tree context only.
    [HarmonyPatch]
    internal static class PatchBlendTreeNodeGUI
    {
        internal static bool InNodeGUI { get; private set; }
        internal static object SelectedNode;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var graphGUIType = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            if (graphGUIType == null) return null;
            var method = AccessTools.Method(graphGUIType, "NodeGUI");
            return method;
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

        static GUIStyle _nameLabelStyle;
        static Color _nameLabelColor;

        /* Returns a centered label style for the node title, rebuilding the cached instance only when color changes. */
        static GUIStyle GetNameLabelStyle(Color color)
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
        static GUIStyle GetBlendTypeLabelStyle(Color color)
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

        /* Returns a short display string for a blend tree type (e.g. "1D", "2D Simple", "Direct"). */
        static string BlendTypeLabel(BlendTreeType blendType) => blendType switch
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
            try
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    SelectedNode = n;
                    var parentNode = Traverse.Create(n).Property("parent").GetValue();
                    if (parentNode != null)
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
            try
            {
                var motion = Traverse.Create(n).Field("motion").GetValue() as Motion;
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

        /* Draws an inline TextField over the node title for rename input, committing on Enter and cancelling on Escape. */
        static void DrawRenameField(Motion motion, Rect titleRect)
        {
            const string controlName = "BlendTreeRenameField";
            var currentEvent = Event.current;

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
            BlendTreeRenameState.RenameText = EditorGUI.TextField(titleRect, BlendTreeRenameState.RenameText, RenameFieldStyle);

            if (BlendTreeRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                BlendTreeRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                EditorApplication.delayCall += BlendTreeRenameState.Apply;
            _renameFieldHadFocus = hasFocus;
        }
    }

    // OnGraphGUI prefix+postfix: drives drag, drop, and sets blend tree GUI context flag for GetNodeStyle.
    [HarmonyPatch]
    internal static class PatchBlendTreeOnGraphGUI
    {
        internal static bool InBlendTreeGUI { get; private set; }

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
        static Color[] OverrideSlotTextColors(GUIStyle style, Color color)
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
        static void Prefix()
        {
            InBlendTreeGUI = true;

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

            var settings = AnimatorDefaultSettings.Load();
            if (settings.nodeColorEnabled)
            {
                var color = settings.overlayActiveColor;
                _savedVarPinInColors    = OverrideSlotTextColors(ResolveStyleField(ref _varPinInField,  "varPinIn"),  color);
                _savedVarPinOutColors   = OverrideSlotTextColors(ResolveStyleField(ref _varPinOutField, "varPinOut"), color);
                _savedEditorLabelColors = OverrideSlotTextColors(EditorStyles.label, color);
            }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            InBlendTreeGUI = false;
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
                    if (PatchGraphDoubleClickCreate.AnimWindow != null)
                        PatchGraphDoubleClickCreate.AnimWindow.wantsMouseMove = true;
                }

                if (!BlendTreeReparentState.IsDragging) return;

                PatchGraphDoubleClickCreate.AnimWindow?.Repaint();

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
            var nodes = PatchGraphDoubleClickCreate.GetNodes(graph);
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
            var nodes = PatchGraphDoubleClickCreate.GetNodes(graph);
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                if (!IsValidDropTarget(node)) continue;
                var rect = Traverse.Create(node).Field("position").GetValue<Rect>();
                EditorGUI.DrawRect(rect, new Color(0f, 1f, 0f, 0.2f));
            }
        }

        // --- Clip drag-drop ---

        /* Updates DragAndDrop.visualMode to Copy when the mouse is over a node, or Rejected otherwise. */
        static void HandleClipDragUpdated(object graphGUI, Vector2 mousePos)
        {
            if (GetDraggedClip() == null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }
            var node = FindNodeUnderMouse(graphGUI, mousePos);
            DragAndDrop.visualMode = node != null
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
        }

        /* Performs the clip drop: adds as a new child if the target is a blend tree node, or replaces motion if target is a leaf. */
        static void HandleClipDragPerform(object graphGUI, Vector2 mousePos)
        {
            var clip = GetDraggedClip();
            if (clip == null) return;
            var node = FindNodeUnderMouse(graphGUI, mousePos);
            if (node == null) return;

            var nodeMotion = Traverse.Create(node).Field("motion").GetValue() as Motion;
            if (nodeMotion is BlendTree targetBlendTree)
                AddClipToBlendTree(graphGUI, targetBlendTree, clip);
            else
                ReplaceLeafMotion(graphGUI, node, clip);

            DragAndDrop.AcceptDrag();
        }

        static AnimationClip GetDraggedClip()
        {
            foreach (var obj in DragAndDrop.objectReferences)
                if (obj is AnimationClip clip) return clip;
            return null;
        }

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
        static void SetNewThresholdOnLastChild(BlendTree blendTree)
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
        static void RebuildGraph(object graphGUI)
        {
            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return;
            var rootBlendTree = Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
            AccessTools.Method(graph.GetType(), "BuildFromBlendTree")?.Invoke(graph, new object[] { rootBlendTree });
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
            if (!PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return;
            var settings = AnimatorDefaultSettings.Load();
            if (!settings.nodeColorEnabled) return;
            var color = settings.overlayActiveColor;
            var copy = new GUIStyle(__result);
            // Set all states — Unity uses focused/active for selected windows
            SetTextColor(ref copy, copy.normal,    color, out var n);  copy.normal    = n;
            SetTextColor(ref copy, copy.onNormal,  color, out var on); copy.onNormal  = on;
            SetTextColor(ref copy, copy.hover,     color, out var h);  copy.hover     = h;
            SetTextColor(ref copy, copy.onHover,   color, out var oh); copy.onHover   = oh;
            SetTextColor(ref copy, copy.active,    color, out var a);  copy.active    = a;
            SetTextColor(ref copy, copy.onActive,  color, out var oa); copy.onActive  = oa;
            SetTextColor(ref copy, copy.focused,   color, out var f);  copy.focused   = f;
            SetTextColor(ref copy, copy.onFocused, color, out var of_); copy.onFocused = of_;
            __result = copy;
        }

        static void SetTextColor(ref GUIStyle _, GUIStyleState state, Color color, out GUIStyleState result)
        {
            state.textColor = color;
            result = state;
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
            if (AssetDatabase.IsMainAsset(RenameTarget))
            {
                var path = AssetDatabase.GetAssetPath(RenameTarget);
                AssetDatabase.RenameAsset(path, RenameText);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Undo.RecordObject(RenameTarget, "Rename Blend Tree Node");
                RenameTarget.name = RenameText;
                EditorUtility.SetDirty(RenameTarget);
            }
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
}
#endif
