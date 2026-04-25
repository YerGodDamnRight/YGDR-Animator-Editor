#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;


namespace YGDR.Editor.Animation
{
    internal static class WindowPatchReflection
    {
        // Layer view
        internal static readonly Type LayerControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.LayerControllerView");
        internal static readonly FieldInfo LayerScrollField =
            AccessTools.Field(LayerControllerViewType, "m_LayerScroll");
        internal static readonly FieldInfo LayerListField =
            AccessTools.Field(LayerControllerViewType, "m_LayerList");
        internal static readonly FieldInfo LayerViewHostField =
            AccessTools.Field(LayerControllerViewType, "m_Host");

        // Parameter view
        internal static readonly Type ParameterControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.ParameterControllerView");
        internal static readonly Type ParameterControllerViewElementType =
            AccessTools.Inner(ParameterControllerViewType, "Element");

        // ReorderableList scroll helpers
        internal static readonly MethodInfo GetElementHeightMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementHeight", new Type[] { typeof(int) });
        internal static readonly MethodInfo GetElementYOffsetMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementYOffset", new Type[] { typeof(int) });

        // AnimatorControllerTool access
        internal static readonly MethodInfo AnimatorControllerGetter =
            AccessTools.PropertyGetter(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimatorControllerTool"),
                "animatorController");
        internal static UnityEditor.Animations.AnimatorController GetOpenController()
        {
            var windows = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
            if (windows.Length == 0) return null;
            return AnimatorControllerGetter?.Invoke(windows[0], null)
                as UnityEditor.Animations.AnimatorController;
        }
    }

    // Preserve layer scroll position when reordering or editing layers
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollReset
    {
        static Vector2 _scrollCache;
        static bool _refocusSelectedLayer;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "ResetUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            _scrollCache = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (!AnimatorDefaultSettings.Load().fixLayerScrollReset) return;

            var scrollpos = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
            if (scrollpos.y == 0)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, _scrollCache);
            _refocusSelectedLayer = true;
        }

        internal static bool ConsumeRefocus()
        {
            if (!_refocusSelectedLayer) return false;
            _refocusSelectedLayer = false;
            return true;
        }
    }

    // Scroll to keep selected layer visible after ResetUI
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollRefocus
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect)
        {
            if (!PatchLayerScrollReset.ConsumeRefocus()) return;

            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(__instance);
            var currentScroll = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
            float elementHeight = (float)WindowPatchReflection.GetElementHeightMethod.Invoke(reorderableList, new object[] { reorderableList.index }) + 20;
            float elementOffset = (float)WindowPatchReflection.GetElementYOffsetMethod.Invoke(reorderableList, new object[] { reorderableList.index });
            if (elementOffset < currentScroll.y)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, new Vector2(currentScroll.x, elementOffset));
            else if (elementOffset + elementHeight > currentScroll.y + rect.height)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, new Vector2(currentScroll.x, elementOffset + elementHeight - rect.height));
        }
    }

    // Scroll parameter list to bottom when adding a new parameter
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchNewParameterScroll
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "AddParameterMenu");

        [HarmonyPostfix]
        static void Postfix(object __instance, object value)
        {
            if (!AnimatorDefaultSettings.Load().scrollToNewParameter) return;
            Traverse.Create(__instance).Field("m_ScrollPosition").SetValue(new Vector2(0, 9001));
        }
    }

    // Default weight of newly added layers to 1
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWeightDefault
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(UnityEditor.Animations.AnimatorController), "AddLayer",
                new Type[] { typeof(UnityEditor.Animations.AnimatorControllerLayer) });

        [HarmonyPrefix]
        static void Prefix(ref UnityEditor.Animations.AnimatorControllerLayer layer)
        {
            if (!AnimatorDefaultSettings.Load().defaultLayerWeight1) return;
            layer.defaultWeight = 1.0f;
        }
    }

    // Parameter row: type label overlay + right-click convert menu
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterRow
    {
        static GUIStyle _typeStyle;
        static GUIStyle TypeStyle => _typeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewElementType, "OnGUI");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            try
            {
                var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
                if (parameter == null) return;

                var settings = AnimatorDefaultSettings.Load();

                if (settings.showParamTypeLabels)
                {
                    TypeStyle.normal.textColor = parameter.type switch
                    {
                        UnityEngine.AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                        UnityEngine.AnimatorControllerParameterType.Int     => settings.paramColorInt,
                        UnityEngine.AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                        UnityEngine.AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                        _ => Color.white
                    };

                    const float labelWidth = 66f;
                    var labelRect = new Rect(rect.xMax - labelWidth * 2f, rect.y, labelWidth - 6f, rect.height);
                    GUI.Label(labelRect, parameter.type.ToString(), TypeStyle);
                }

            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter row error: {e}");
            }
        }
    }

    // Right-click convert menu on ParameterControllerView.OnGUI (Element.OnGUI is Repaint-only)
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterContextMenu
    {
        // Find the ReorderableList field by type since the name is internal
        static ReorderableList FindParamList(object instance)
        {
            foreach (var field in instance.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType == typeof(ReorderableList))
                {
                    if (field.GetValue(instance) is ReorderableList reorderableList)
                        return reorderableList;
                }
            }
            return null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;

            var reorderableList = FindParamList(__instance);
            if (reorderableList == null || reorderableList.index < 0) return;

            var controller = WindowPatchReflection.GetOpenController();
            if (controller == null || reorderableList.index >= controller.parameters.Length) return;

            var parameter = controller.parameters[reorderableList.index];
            var capturedIndex = reorderableList.index;

            currentEvent.Use();
            var menu = new GenericMenu();
            foreach (UnityEngine.AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(UnityEngine.AnimatorControllerParameterType)))
            {
                if (type == parameter.type) continue;
                var capturedType = type;
                menu.AddItem(new GUIContent($"Convert to {type}"), false, () =>
                    ConvertParameter(controller, capturedIndex, capturedType));
            }
            menu.ShowAsContext();
        }

        static void ConvertParameter(UnityEditor.Animations.AnimatorController controller, int index,
            UnityEngine.AnimatorControllerParameterType newType)
        {
            string paramName = controller.parameters[index].name;
            var sourceType = controller.parameters[index].type;
            Undo.RegisterCompleteObjectUndo(controller, "Convert Parameter");
            var serializedObject = new SerializedObject(controller);
            serializedObject.Update();
            var parametersProperty = serializedObject.FindProperty("m_AnimatorParameters");
            if (parametersProperty == null) return;
            parametersProperty.GetArrayElementAtIndex(index).FindPropertyRelative("m_Type").intValue = (int)newType;
            serializedObject.ApplyModifiedProperties();

            foreach (var layer in controller.layers)
                FixConditionsForConversion(layer.stateMachine, paramName, sourceType, newType);
        }

        static void FixConditionsForConversion(UnityEditor.Animations.AnimatorStateMachine sm, string paramName,
            UnityEngine.AnimatorControllerParameterType sourceType, UnityEngine.AnimatorControllerParameterType newType)
        {
            var allTransitions = new List<UnityEditor.Animations.AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var childState in sm.states)
                allTransitions.AddRange(childState.state.transitions);

            foreach (var transition in allTransitions)
            {
                var conditions = transition.conditions;
                bool modified = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter != paramName) continue;
                    if (!TryConvertCondition(conditions[i], sourceType, newType, out var converted)) continue;
                    conditions[i] = converted;
                    modified = true;
                }
                if (modified)
                {
                    Undo.RecordObject(transition, "Convert Parameter");
                    transition.conditions = conditions;
                }
            }

            foreach (var childStateMachine in sm.stateMachines)
                FixConditionsForConversion(childStateMachine.stateMachine, paramName, sourceType, newType);
        }

        static bool TryConvertCondition(UnityEditor.Animations.AnimatorCondition condition,
            UnityEngine.AnimatorControllerParameterType sourceType, UnityEngine.AnimatorControllerParameterType newType,
            out UnityEditor.Animations.AnimatorCondition result)
        {
            result = condition;
            var mode = condition.mode;
            float threshold = condition.threshold;

            UnityEditor.Animations.AnimatorConditionMode newMode;
            float newThreshold;

            var Int     = UnityEngine.AnimatorControllerParameterType.Int;
            var Bool    = UnityEngine.AnimatorControllerParameterType.Bool;
            var Float   = UnityEngine.AnimatorControllerParameterType.Float;
            var Equals  = UnityEditor.Animations.AnimatorConditionMode.Equals;
            var NotEqual= UnityEditor.Animations.AnimatorConditionMode.NotEqual;
            var Greater = UnityEditor.Animations.AnimatorConditionMode.Greater;
            var Less    = UnityEditor.Animations.AnimatorConditionMode.Less;
            var If      = UnityEditor.Animations.AnimatorConditionMode.If;
            var IfNot   = UnityEditor.Animations.AnimatorConditionMode.IfNot;

            if (sourceType == Int && newType == Bool)
            {
                if (mode == Equals)  { newMode = If;    newThreshold = 0f; }
                else if (mode == NotEqual) { newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else if (sourceType == Int && newType == Float)
            {
                if (mode == Equals)  { newMode = Greater; newThreshold = threshold; }
                else if (mode == NotEqual) { newMode = Less;    newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Bool && (newType == Int || newType == Float))
            {
                if (newType == Int)
                {
                    if (mode == If)    { newMode = Equals;   newThreshold = 1f; }
                    else if (mode == IfNot) { newMode = NotEqual; newThreshold = 1f; }
                    else return false;
                }
                else
                {
                    if (mode == If)    { newMode = Greater; newThreshold = 0f; }
                    else if (mode == IfNot) { newMode = Less;    newThreshold = 1f; }
                    else return false;
                }
            }
            else if (sourceType == Float && newType == Int)
            {
                if (mode == Greater) { newMode = Equals;   newThreshold = threshold; }
                else if (mode == Less)    { newMode = NotEqual; newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Float && newType == Bool)
            {
                if (mode == Greater) { newMode = If;    newThreshold = 0f; }
                else if (mode == Less)    { newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else return false;

            result = new UnityEditor.Animations.AnimatorCondition
            {
                mode = newMode,
                parameter = condition.parameter,
                threshold = newThreshold
            };
            return true;
        }
    }


    // Layer copy/paste via right-click context menu on each layer row
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCopyPaste
    {
        static UnityEditor.Animations.AnimatorControllerLayer _layerClipboard;
        static UnityEditor.Animations.AnimatorController _controllerClipboard;

        static UnityEditor.Animations.AnimatorController GetController(object layerView) =>
            Traverse.Create(layerView).Field("m_Host").Property("animatorController")
                .GetValue<UnityEditor.Animations.AnimatorController>();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseUp || evt.button != 1 || !rect.Contains(evt.mousePosition)) return;

            evt.Use();
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy layer"), false,
                static data => CopyLayer(data), __instance);

            if (_layerClipboard != null)
            {
                menu.AddItem(new GUIContent("Paste layer"), false,
                    static data => PasteLayer(data), __instance);
                menu.AddItem(new GUIContent("Paste layer settings"), false,
                    static data => PasteLayerSettings(data), __instance);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste layer"));
                menu.AddDisabledItem(new GUIContent("Paste layer settings"));
            }

            menu.AddItem(new GUIContent("Delete layer"), false,
                static data => Traverse.Create(data).Method("DeleteLayer").GetValue(null), __instance);

            menu.ShowAsContext();
        }

        static void CopyLayer(object layerView)
        {
            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            _layerClipboard = reorderableList.list[reorderableList.index] as UnityEditor.Animations.AnimatorControllerLayer;
            _controllerClipboard = controller;
            Unsupported.CopyStateMachineDataToPasteboard(_layerClipboard.stateMachine, controller, reorderableList.index);
        }

        static void PasteLayer(object layerView)
        {
            if (_layerClipboard == null) return;

            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            int targetIndex = reorderableList.index + 1;
            string newName = controller.MakeUniqueLayerName(_layerClipboard.name);
            Undo.FlushUndoRecordObjects();

            controller.AddLayer(newName);
            var layers = controller.layers;
            int pastedIndex = layers.Length - 1;
            var pastedLayer = layers[pastedIndex];
            Unsupported.PasteToStateMachineFromPasteboard(pastedLayer.stateMachine, controller, pastedIndex, Vector3.zero);

            // Promote pasted SM from child wrapper to top-level
            var pastedSM = pastedLayer.stateMachine.stateMachines[0].stateMachine;
            pastedSM.name = newName;
            pastedLayer.stateMachine.stateMachines = new UnityEditor.Animations.ChildAnimatorStateMachine[0];
            UnityEngine.Object.DestroyImmediate(pastedLayer.stateMachine, true);
            pastedLayer.stateMachine = pastedSM;
            PasteLayerProperties(pastedLayer, _layerClipboard);

            // Move to just below source layer
            for (int i = layers.Length - 1; i > targetIndex; i--)
                layers[i] = layers[i - 1];
            layers[targetIndex] = pastedLayer;
            controller.layers = layers;

            // Prevent undo from leaving dangling sub-assets
            Undo.ClearUndo(controller);

            // Cross-controller paste: sync referenced parameters
            if (controller != _controllerClipboard)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.RecordObject(controller, "Sync pasted layer parameters");

                var destParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(controller.parameters.Length);
                foreach (var parameter in controller.parameters) destParams[parameter.name] = parameter;

                var srcParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
                foreach (var parameter in _controllerClipboard.parameters) srcParams[parameter.name] = parameter;

                var queued = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
                GatherSmParams(pastedSM, ref srcParams, ref queued);

                foreach (var parameter in queued.Values)
                    if (!destParams.ContainsKey(parameter.name))
                        controller.AddParameter(parameter);

                Undo.CollapseUndoOperations(group);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Traverse.Create(layerView).Property("selectedLayerIndex").SetValue(targetIndex);
        }

        static void PasteLayerSettings(object layerView)
        {
            if (_layerClipboard == null) return;
            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            var layers = controller.layers;
            PasteLayerProperties(layers[reorderableList.index], _layerClipboard);
            controller.layers = layers;
        }

        static void PasteLayerProperties(UnityEditor.Animations.AnimatorControllerLayer destinationLayer, UnityEditor.Animations.AnimatorControllerLayer sourceLayer)
        {
            destinationLayer.avatarMask                = sourceLayer.avatarMask;
            destinationLayer.blendingMode              = sourceLayer.blendingMode;
            destinationLayer.defaultWeight             = sourceLayer.defaultWeight;
            destinationLayer.iKPass                    = sourceLayer.iKPass;
            destinationLayer.syncedLayerAffectsTiming  = sourceLayer.syncedLayerAffectsTiming;
            destinationLayer.syncedLayerIndex          = sourceLayer.syncedLayerIndex;
        }

        static void GatherSmParams(UnityEditor.Animations.AnimatorStateMachine sm,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.mirrorParameterActive      && src.ContainsKey(state.mirrorParameter))      queued[state.mirrorParameter]      = src[state.mirrorParameter];
                if (state.speedParameterActive       && src.ContainsKey(state.speedParameter))       queued[state.speedParameter]       = src[state.speedParameter];
                if (state.timeParameterActive        && src.ContainsKey(state.timeParameter))        queued[state.timeParameter]        = src[state.timeParameter];
                if (state.cycleOffsetParameterActive && src.ContainsKey(state.cycleOffsetParameter)) queued[state.cycleOffsetParameter] = src[state.cycleOffsetParameter];

                if (state.motion is UnityEditor.Animations.BlendTree blendTree)
                    GatherBtParams(blendTree, ref src, ref queued);
            }

            var transitions = new List<UnityEditor.Animations.AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var childState in sm.states)
                transitions.AddRange(childState.state.transitions);
            foreach (var transition in transitions)
                foreach (var cond in transition.conditions)
                    if (src.ContainsKey(cond.parameter))
                        queued[cond.parameter] = src[cond.parameter];

            foreach (var childStateMachine in sm.stateMachines)
                GatherSmParams(childStateMachine.stateMachine, ref src, ref queued);
        }

        static void GatherBtParams(UnityEditor.Animations.BlendTree blendTree,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            if (src.ContainsKey(blendTree.blendParameter))  queued[blendTree.blendParameter]  = src[blendTree.blendParameter];
            if (src.ContainsKey(blendTree.blendParameterY)) queued[blendTree.blendParameterY] = src[blendTree.blendParameterY];

            foreach (var childMotion in blendTree.children)
            {
                if (src.ContainsKey(childMotion.directBlendParameter))
                    queued[childMotion.directBlendParameter] = src[childMotion.directBlendParameter];
                if (childMotion.motion is UnityEditor.Animations.BlendTree childBlendTree)
                    GatherBtParams(childBlendTree, ref src, ref queued);
            }
        }
    }

    // Layer list: WD indicator if all states have Write Defaults on, ! if empty
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWDIndicator
    {
        static GUIStyle _labelStyle;
        static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 9 };

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            if (EditorApplication.isPlaying) return;

            var settings = AnimatorDefaultSettings.Load();
            if (!settings.showLayerWDIndicator) return;

            try
            {
                var layerViewHost = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                var controller = Traverse.Create(layerViewHost).Field("m_AnimatorController")
                    .GetValue<UnityEditor.Animations.AnimatorController>();
                if (controller == null || index >= controller.layers.Length) return;

                var stateMachine = controller.layers[index].stateMachine;
                var labelRect = new Rect(rect.x - 19f, rect.y + 15f, 18f, 18f);

                if (stateMachine.states.Length == 0 && stateMachine.stateMachines.Length == 0)
                {
                    LabelStyle.normal.textColor = settings.layerEmptyColor;
                    EditorGUI.LabelField(labelRect, "   !", LabelStyle);
                    return;
                }

                int writeDefaultsOnCount = 0, writeDefaultsOffCount = 0;
                CountWD(stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount);

                if (writeDefaultsOnCount > 0 && writeDefaultsOffCount == 0)
                {
                    LabelStyle.normal.textColor = settings.layerWDColor;
                    EditorGUI.LabelField(labelRect, "WD", LabelStyle);
                }
                else if (writeDefaultsOnCount > 0 && writeDefaultsOffCount > 0)
                {
                    LabelStyle.normal.textColor = Color.cyan;
                    EditorGUI.LabelField(labelRect, "WD", LabelStyle);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Layer WD indicator error: {e}");
            }
        }

        static void CountWD(UnityEditor.Animations.AnimatorStateMachine sm, ref int writeDefaultsOnCount, ref int writeDefaultsOffCount)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.writeDefaultValues) writeDefaultsOnCount++;
                else writeDefaultsOffCount++;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CountWD(childStateMachine.stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount);
        }
    }

    // Bottom bar: selection count, active mode label, clickable controller path
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchBottomBar
    {
        static GUIStyle _barLabelStyle;
        static GUIStyle BarLabelStyle => _barLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "DoGraphBottomBar");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect nameRect)
        {
            try
            {
                var controller = WindowPatchReflection.AnimatorControllerGetter?.Invoke(__instance, null)
                    as UnityEditor.Animations.AnimatorController;
                if (controller == null) return;

                // Make existing controller path label clickable
                string controllerPath = AssetDatabase.GetAssetPath(controller);
                float controllerLabelWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(controllerPath)).x + 18f;
                var controllerRect = new Rect(nameRect.xMax - controllerLabelWidth, nameRect.y, controllerLabelWidth, nameRect.height);
                EditorGUIUtility.AddCursorRect(controllerRect, MouseCursor.Link);

                var currentEvent = Event.current;
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && controllerRect.Contains(currentEvent.mousePosition))
                {
                    EditorGUIUtility.PingObject(controller);
                    if (currentEvent.clickCount == 2) Selection.activeObject = controller;
                    currentEvent.Use();
                }

                // Selection count label
                int nodeCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorState>().Count();
                int transitionCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorStateTransition>().Count();
                var selectionContent = new GUIContent($"  {nodeCount} Nodes / {transitionCount} Transitions Selected");
                float selectionWidth = BarLabelStyle.CalcSize(selectionContent).x;
                DrawBarLabel(new Rect(nameRect.x, nameRect.y, selectionWidth, nameRect.height), selectionContent);

                // Active mode label (centered)
                string modeText = GetModeText();
                if (!string.IsNullOrEmpty(modeText))
                {
                    var modeContent = new GUIContent(modeText);
                    float modeWidth = BarLabelStyle.CalcSize(modeContent).x;
                    float modeX = nameRect.x + (nameRect.width - modeWidth) * 0.5f;
                    DrawBarLabel(new Rect(modeX, nameRect.y, modeWidth, nameRect.height), modeContent);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Bottom bar error: {e}");
            }
        }

        static void DrawBarLabel(Rect rect, GUIContent content)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField(content, BarLabelStyle);
            GUILayout.EndArea();
        }

        static string GetModeText()
        {
            if (PatchStateChainTransition.ChainActive)              return "Chain Mode";
            if (PatchTransitionCopyPaste.PasteActive)               return $"Paste {PatchTransitionCopyPaste.ClipboardCount} Transition{(PatchTransitionCopyPaste.ClipboardCount == 1 ? "" : "s")}";
            if (PatchStateNodeMenu._multiTransitionSources != null) return "Multi Transition — click destination";
            if (PatchStateNodeMenu._redirectTransitions != null)    return "Redirect Transitions — click destination";
            if (PatchStateNodeMenu._replicateTransitions != null)   return "Replicate Transitions — click sources";
            return null;
        }
    }

}
#endif
