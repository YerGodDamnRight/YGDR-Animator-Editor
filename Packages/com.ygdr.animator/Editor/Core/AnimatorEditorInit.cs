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
        internal static readonly Type BlendTreeGraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
        internal static readonly Type AnimatorControllerToolType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimatorControllerTool");

        internal static readonly MethodInfo GetGraphMethod =
            AccessTools.Method(StateNodeType, "get_graph");
        internal static readonly MethodInfo GetActiveStateMachineMethod =
            AccessTools.Method(GraphType, "get_activeStateMachine");
        internal static readonly MethodInfo GetActiveStateMachineFromGraphGUIMethod =
            AccessTools.Method(GraphGUIType, "get_activeStateMachine");
        internal static readonly FieldInfo SMNodeStateMachineField =
            AccessTools.Field(StateMachineNodeType, "stateMachine");

        static int _patchWait = 0;

        static AnimatorEditorInit()
        {
            // Layer 1 crash guard: if a feature's PendingEnable flag survived shutdown it caused a lockup
            foreach (var featureId in FeatureHarmony.AllFeatureIds)
            {
                if (EditorPrefs.GetBool($"AnimatorTools.PendingEnable.{featureId}", false))
                {
                    Debug.LogWarning($"[AnimatorTools] {featureId} may have caused a lockup — auto-disabled. Re-enable in Compatibility settings.");
                    EditorPrefs.SetBool($"AnimatorTools.Feature.{featureId}", false);
                    EditorPrefs.DeleteKey($"AnimatorTools.PendingEnable.{featureId}");
                }
            }

            AssemblyReloadEvents.beforeAssemblyReload += FeatureHarmony.UnpatchAll;

            EditorApplication.update -= DoPatches;
            EditorApplication.update += DoPatches;
        }

        static void DoPatches()
        {
            _patchWait++;
            if (_patchWait <= 2) return;

            EditorApplication.update -= DoPatches;

            Selection.selectionChanged -= OnAnalysisSelectionChanged;
            Selection.selectionChanged += OnAnalysisSelectionChanged;

            // Per-feature patches — default enabled, persisted via EditorPrefs
            foreach (var featureId in FeatureHarmony.AllFeatureIds)
            {
                if (EditorPrefs.GetBool($"AnimatorTools.Feature.{featureId}", true))
                    FeatureHarmony.SetEnabled(featureId, true);
            }

            // Warn on high-collision methods after patching so foreign patches are visible
            FeatureHarmony.WarnIfConflict(GraphPatchReflection.HandleContextMenuMethod, "ContextMenu (HandleContextMenu)");
            FeatureHarmony.WarnIfConflict(GraphPatchReflection.DoEdgesMethod, "GraphInteraction (DoEdges)");
            FeatureHarmony.WarnIfConflict(GraphPatchReflection.DrawEdgeMethod, "TransitionOverlay (DrawEdge)");

            PatchNodeStyles.HandleTextures();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            EditorApplication.update -= TextureWatchdog;
            EditorApplication.update += TextureWatchdog;

            // Layer 1: clear pending flags next frame — proves patches survived one frame without crash
            EditorApplication.update -= ClearPendingFlagsDeferred;
            EditorApplication.update += ClearPendingFlagsDeferred;
        }

        static void ClearPendingFlagsDeferred()
        {
            EditorApplication.update -= ClearPendingFlagsDeferred;
            FeatureHarmony.ClearPendingFlags();
        }

        static void TextureWatchdog()
        {
            if (!PatchNodeStyles.HasTextures())
                PatchNodeStyles.HandleTextures();
        }

        static void OnAnalysisSelectionChanged()
        {
            if (AnimatorGraphAnalyzer.SuppressNextSelectionClear)
            {
                AnimatorGraphAnalyzer.SuppressNextSelectionClear = false;
                return;
            }
            if (!AnimatorGraphAnalyzer.HasResults) return;
            AnimatorGraphAnalyzer.ClearResults();
            (Resources.FindObjectsOfTypeAll(AnimatorControllerToolType).FirstOrDefault() as EditorWindow)?.Repaint();
        }

        // Layer 3: emergency recovery — reverts all IL in place if the Animator window is frozen
        [MenuItem("YGDR/Animator Editor/Emergency: Unpatch All")]
        static void EmergencyUnpatch()
        {
            FeatureHarmony.UnpatchAll();
            Debug.Log("[AnimatorTools] All patches Disabled. Re-enable features via YGDR Editor Window Settings Tab → Compatibility.");
        }
    }
}
#endif
