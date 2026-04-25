#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using HarmonyLib;


namespace YGDR.Editor.Animation
{
    [InitializeOnLoad]
    internal sealed class AnimatorEditorInit
    {
        internal static readonly Type StateNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateNode");
        internal static readonly Type StateMachineNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.StateMachineNode");
        internal static readonly Type AnyStateNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.AnyStateNode");
        internal static readonly Type EntryNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EntryNode");
        internal static readonly Type ExitNodeType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.ExitNode");
        internal static readonly Type GraphType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.Graph");
        internal static readonly Type GraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI");
        internal static readonly Type AnimatorControllerToolType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimatorControllerTool");

        internal static readonly MethodInfo GetGraphMethod =
            AccessTools.Method(StateNodeType, "get_graph");
        internal static readonly MethodInfo GetActiveStateMachineMethod =
            AccessTools.Method(GraphType, "get_activeStateMachine");
        internal static readonly MethodInfo GetActiveStateMachineFromGraphGUIMethod =
            AccessTools.Method(GraphGUIType, "get_activeStateMachine");
        internal static readonly MethodInfo GetSMNodeStateMachineMethod =
            AccessTools.PropertyGetter(StateMachineNodeType, "stateMachine");

        static int _patchWait = 0;
        static Harmony _harmony;

        static AnimatorEditorInit()
        {
            _harmony = new Harmony("com.animatortools");
            // Mirror RATS: wait >2 update cycles before patching so all static
            // initializers (including UnityEditor.Graphs.Styles..cctor) have run.
            EditorApplication.update -= DoPatches;
            EditorApplication.update += DoPatches;
        }

        static void DoPatches()
        {
            _patchWait++;
            if (_patchWait > 2)
            {
                EditorApplication.update -= DoPatches;
                _harmony.PatchAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                PatchNodeStyles.HandleTextures();
                EditorApplication.update -= TextureWatchdog;
                EditorApplication.update += TextureWatchdog;
            }
        }

        static void TextureWatchdog()
        {
            if (!PatchNodeStyles.HasTextures())
                PatchNodeStyles.HandleTextures();
        }
    }
}
#endif
