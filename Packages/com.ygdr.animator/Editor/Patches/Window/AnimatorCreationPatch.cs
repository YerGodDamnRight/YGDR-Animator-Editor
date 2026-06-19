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
    internal static class AnimatorStateCreationPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new[]
            {
                AccessTools.Method(typeof(AnimatorStateMachine), "AddState", new[] { typeof(string) }),
                AccessTools.Method(typeof(AnimatorStateMachine), "AddState", new[] { typeof(string), typeof(Vector3) }),
            };
            return methods.Where(m => m != null);
        }

        [HarmonyPostfix]
        static void Postfix(AnimatorState __result)
        {
            try
            {
                if (PatchAddStateDrop.DropIntercepted) { PatchAddStateDrop.DropIntercepted = false; return; }
                if (__result == null || !AssetDatabase.Contains(__result)) return;
                var settings = AnimatorDefaultSettings.Load();
                if (settings.applyToStates) AnimatorDefaultSettings.ApplyStateDefaults(__result);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] State defaults patch error: {e}");
            }
        }
    }

    [HarmonyPatch]
    internal static class AnimatorTransitionCreationPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(AnimatorState).GetMethods()
                .Where(m => m.Name is "AddTransition" or "AddExitTransition")
                .Cast<MethodBase>()
                .Concat(typeof(AnimatorStateMachine).GetMethods()
                    .Where(m => m.Name == "AddAnyStateTransition")
                    .Cast<MethodBase>())
                .Where(m => m.GetParameters().All(p => p.ParameterType != typeof(AnimatorStateTransition)));
        }

        [HarmonyPostfix]
        static void Postfix(AnimatorStateTransition __result)
        {
            try
            {
                if (__result == null || !AssetDatabase.Contains(__result)) return;
                var settings = AnimatorDefaultSettings.Load();
                if (settings.applyToTransitions) AnimatorDefaultSettings.ApplyTransitionDefaults(__result);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Transition defaults patch error: {e}");
            }
        }
    }
}
#endif
