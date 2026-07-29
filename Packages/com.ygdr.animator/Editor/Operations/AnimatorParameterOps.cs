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
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
#endif

namespace YGDR.Editor.Animation
{
    internal static class AnimatorParameterOps
    {
#if VRC_SDK_VRCSDK3
        internal static VRCExpressionParameters.ValueType MapToVrcValueType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Float => VRCExpressionParameters.ValueType.Float,
            AnimatorControllerParameterType.Int   => VRCExpressionParameters.ValueType.Int,
            _                                      => VRCExpressionParameters.ValueType.Bool
        };
#endif

        internal static void InsertParameterAtIndex(AnimatorController controller,
            int index, string paramName, AnimatorControllerParameterType type)
        {
            Undo.RegisterCompleteObjectUndo(controller, $"Add {type} Parameter");
            controller.AddParameter(paramName, type);

            var serializedObject = new SerializedObject(controller);
            serializedObject.Update();
            var parametersProperty = serializedObject.FindProperty("m_AnimatorParameters");
            parametersProperty.MoveArrayElement(parametersProperty.arraySize - 1, index);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        internal static void ConvertParameter(AnimatorController controller, int index,
            AnimatorControllerParameterType newType)
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
            // Inspector reads parameter type mid-frame; defer rebuild to avoid stale display after type change.
            EditorApplication.delayCall += () => ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        static void FixConditionsForConversion(AnimatorStateMachine sm, string paramName,
            AnimatorControllerParameterType sourceType, AnimatorControllerParameterType newType)
        {
            var allTransitions = new List<AnimatorStateTransition>(sm.anyStateTransitions);
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

        internal static bool TryConvertCondition(AnimatorCondition condition,
            AnimatorControllerParameterType sourceType, AnimatorControllerParameterType newType,
            out AnimatorCondition result)
        {
            result = condition;
            var mode = condition.mode;
            float threshold = condition.threshold;

            AnimatorConditionMode newMode;
            float newThreshold;

            var Int      = AnimatorControllerParameterType.Int;
            var Bool     = AnimatorControllerParameterType.Bool;
            var Float    = AnimatorControllerParameterType.Float;
            var Equals   = AnimatorConditionMode.Equals;
            var NotEqual = AnimatorConditionMode.NotEqual;
            var Greater  = AnimatorConditionMode.Greater;
            var Less     = AnimatorConditionMode.Less;
            var If       = AnimatorConditionMode.If;
            var IfNot    = AnimatorConditionMode.IfNot;

            if (sourceType == Int && newType == Bool)
            {
                if (mode == Equals)        { newMode = If;      newThreshold = 0f; }
                else if (mode == NotEqual) { newMode = IfNot;   newThreshold = 0f; }
                else return false;
            }
            else if (sourceType == Int && newType == Float)
            {
                if (mode == Equals)        { newMode = Greater; newThreshold = threshold; }
                else if (mode == NotEqual) { newMode = Less;    newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Bool && (newType == Int || newType == Float))
            {
                if (newType == Int)
                {
                    if (mode == If)        { newMode = Equals;   newThreshold = 1f; }
                    else if (mode == IfNot){ newMode = NotEqual; newThreshold = 1f; }
                    else return false;
                }
                else
                {
                    if (mode == If)        { newMode = Greater; newThreshold = 0f; }
                    else if (mode == IfNot){ newMode = Less;    newThreshold = 1f; }
                    else return false;
                }
            }
            else if (sourceType == Float && newType == Int)
            {
                if (mode == Greater)  { newMode = Equals;   newThreshold = threshold; }
                else if (mode == Less){ newMode = NotEqual; newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Float && newType == Bool)
            {
                if (mode == Greater)  { newMode = If;    newThreshold = 0f; }
                else if (mode == Less){ newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else return false;

            result = new AnimatorCondition
            {
                mode      = newMode,
                parameter = condition.parameter,
                threshold = newThreshold
            };
            return true;
        }

        internal static void RemoveUnusedParameters(AnimatorController controller)
        {
            var usedParamNames = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectUsedParameters(layer.stateMachine, usedParamNames);
#if VRC_SDK_VRCSDK3
            CollectParameterDriverNames(controller, usedParamNames);
#endif

            var unusedParamNames = controller.parameters
                .Where(parameter => !usedParamNames.Contains(parameter.name))
                .Select(parameter => parameter.name)
                .ToArray();

            if (unusedParamNames.Length == 0) return;

            Undo.RegisterCompleteObjectUndo(controller, "Remove Unused Parameters");
            foreach (var unusedParamName in unusedParamNames)
            {
                int paramIndex = Array.FindIndex(controller.parameters, parameter => parameter.name == unusedParamName);
                if (paramIndex >= 0)
                    controller.RemoveParameter(paramIndex);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static void CollectUsedParameters(AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                foreach (var condition in transition.conditions)
                    result.Add(condition.parameter);

            foreach (var childState in stateMachine.states)
            {
                foreach (var transition in childState.state.transitions)
                    foreach (var condition in transition.conditions)
                        result.Add(condition.parameter);

                CollectMotionParameters(childState.state.motion, result);
                CollectStateParameters(childState.state, result);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectUsedParameters(childStateMachine.stateMachine, result);
        }

        /* Collects params driven by per-state fields (speed/mirror/cycleOffset/time), which don't show up in transition conditions or motions. */
        static void CollectStateParameters(AnimatorState state, HashSet<string> result)
        {
            if (state.speedParameterActive && !string.IsNullOrEmpty(state.speedParameter))
                result.Add(state.speedParameter);
            if (state.mirrorParameterActive && !string.IsNullOrEmpty(state.mirrorParameter))
                result.Add(state.mirrorParameter);
            if (state.cycleOffsetParameterActive && !string.IsNullOrEmpty(state.cycleOffsetParameter))
                result.Add(state.cycleOffsetParameter);
            if (state.timeParameterActive && !string.IsNullOrEmpty(state.timeParameter))
                result.Add(state.timeParameter);
        }

        static void CollectMotionParameters(UnityEngine.Motion motion, HashSet<string> result)
        {
            if (motion is not BlendTree blendTree) return;
            result.Add(blendTree.blendParameter);
            result.Add(blendTree.blendParameterY);
            foreach (var childMotion in blendTree.children)
            {
                if (!string.IsNullOrEmpty(childMotion.directBlendParameter))
                    result.Add(childMotion.directBlendParameter);
                CollectMotionParameters(childMotion.motion, result);
            }
        }

#if VRC_SDK_VRCSDK3
        internal static void CollectParameterDriverNames(AnimatorController controller, HashSet<string> result)
        {
            foreach (var layer in controller.layers)
                CollectVrcBehaviourNames(layer.stateMachine, result);
        }

        static void CollectVrcBehaviourNames(AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            CollectBehaviourNames(stateMachine.behaviours, result);
            foreach (var childState in stateMachine.states)
                CollectBehaviourNames(childState.state.behaviours, result);
            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectVrcBehaviourNames(childStateMachine.stateMachine, result);
        }

        static void CollectBehaviourNames(StateMachineBehaviour[] behaviours, HashSet<string> result)
        {
            foreach (var driver in behaviours.OfType<VRCAvatarParameterDriver>())
                foreach (var driverParameter in driver.parameters)
                {
                    if (!string.IsNullOrEmpty(driverParameter.name))
                        result.Add(driverParameter.name);
                    if (driverParameter.type == VRC_AvatarParameterDriver.ChangeType.Copy && !string.IsNullOrEmpty(driverParameter.source))
                        result.Add(driverParameter.source);
                }
            foreach (var playAudio in behaviours.OfType<VRCAnimatorPlayAudio>())
                if (!string.IsNullOrEmpty(playAudio.ParameterName))
                    result.Add(playAudio.ParameterName);
        }
#endif

        internal static void DeleteParameterAndClean(AnimatorController controller, string paramName)
        {
            Undo.RegisterCompleteObjectUndo(controller, "Delete and Clean Parameter");

            foreach (var layer in controller.layers)
                DeleteTransitionsReferencingParam(layer.stateMachine, paramName);

            int paramIndex = Array.FindIndex(controller.parameters, parameter => parameter.name == paramName);
            if (paramIndex >= 0)
                controller.RemoveParameter(paramIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static void DeleteTransitionsReferencingParam(AnimatorStateMachine stateMachine, string paramName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                StripConditionsForParam(transition, paramName);

            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    StripConditionsForParam(transition, paramName);

            foreach (var childStateMachine in stateMachine.stateMachines)
                DeleteTransitionsReferencingParam(childStateMachine.stateMachine, paramName);
        }

        static void StripConditionsForParam(AnimatorStateTransition transition, string paramName)
        {
            if (!transition.conditions.Any(condition => condition.parameter == paramName)) return;
            Undo.RecordObject(transition, "Delete and Clean Parameter");
            transition.conditions = transition.conditions
                .Where(condition => condition.parameter != paramName).ToArray();
        }

        internal static void RemapParameter(AnimatorController controller, string fromParamName, string toParamName)
        {
            foreach (var layer in controller.layers)
                RemapConditionsInStateMachine(layer.stateMachine, fromParamName, toParamName);
            RemapParameterReferences(controller, fromParamName, toParamName);
            EditorUtility.SetDirty(controller);
        }

        internal static void RemapParameterReferences(AnimatorController controller, string fromParamName, string toParamName)
        {
            foreach (var layer in controller.layers)
                RemapBehavioursInStateMachine(layer.stateMachine, fromParamName, toParamName);
            AnimatorClipRemapper.RemapAapParameter(controller, fromParamName, toParamName);
#if VRC_SDK_VRCSDK3
            RemapVrcParameters(fromParamName, toParamName);
#endif
        }

        static void RemapConditionsInStateMachine(AnimatorStateMachine stateMachine, string fromParamName, string toParamName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                RemapConditions(transition, fromParamName, toParamName);
            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    RemapConditions(transition, fromParamName, toParamName);
            foreach (var childStateMachine in stateMachine.stateMachines)
                RemapConditionsInStateMachine(childStateMachine.stateMachine, fromParamName, toParamName);
        }

        static void RemapConditions(AnimatorStateTransition transition, string fromParamName, string toParamName)
        {
            var conditions = transition.conditions;
            bool modified = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != fromParamName) continue;
                var condition = conditions[i];
                condition.parameter = toParamName;
                conditions[i] = condition;
                modified = true;
            }
            if (!modified) return;
            Undo.RecordObject(transition, "Remap Parameter");
            transition.conditions = conditions;
        }

        static void RemapBehavioursInStateMachine(AnimatorStateMachine stateMachine, string fromParamName, string toParamName)
        {
#if VRC_SDK_VRCSDK3
            RemapBehaviours(stateMachine.behaviours, fromParamName, toParamName);
            foreach (var childState in stateMachine.states)
                RemapBehaviours(childState.state.behaviours, fromParamName, toParamName);
            foreach (var childStateMachine in stateMachine.stateMachines)
                RemapBehavioursInStateMachine(childStateMachine.stateMachine, fromParamName, toParamName);
#endif
        }

#if VRC_SDK_VRCSDK3
        static void RemapBehaviours(StateMachineBehaviour[] behaviours, string fromParamName, string toParamName)
        {
            foreach (var driver in behaviours.OfType<VRCAvatarParameterDriver>())
            {
                bool modified = false;
                for (int i = 0; i < driver.parameters.Count; i++)
                {
                    var driverParam = driver.parameters[i];
                    bool nameMatches   = driverParam.name == fromParamName;
                    bool sourceMatches = driverParam.type == VRC_AvatarParameterDriver.ChangeType.Copy
                                        && driverParam.source == fromParamName;
                    if (!nameMatches && !sourceMatches) continue;
                    if (!modified) { Undo.RecordObject(driver, "Remap Parameter"); modified = true; }
                    driver.parameters[i] = new VRC_AvatarParameterDriver.Parameter
                    {
                        name           = nameMatches   ? toParamName : driverParam.name,
                        source         = sourceMatches ? toParamName : driverParam.source,
                        type           = driverParam.type,
                        value          = driverParam.value,
                        valueMin       = driverParam.valueMin,
                        valueMax       = driverParam.valueMax,
                        chance         = driverParam.chance,
                        convertRange   = driverParam.convertRange,
                        sourceMin      = driverParam.sourceMin,
                        sourceMax      = driverParam.sourceMax,
                        destMin        = driverParam.destMin,
                        destMax        = driverParam.destMax,
                        preventRepeats = driverParam.preventRepeats
                    };
                }
                if (modified) EditorUtility.SetDirty(driver);
            }
            foreach (var playAudio in behaviours.OfType<VRCAnimatorPlayAudio>())
            {
                if (playAudio.ParameterName != fromParamName) continue;
                Undo.RecordObject(playAudio, "Remap Parameter");
                playAudio.ParameterName = toParamName;
                EditorUtility.SetDirty(playAudio);
            }
        }

        internal static (List<string> toAdd, List<string> toRemove) PreviewVrcParameterSync(
            VRCExpressionParameters expressionParameters, AnimatorController controller,
            HashSet<string> excludedNames = null)
        {
            var controllerNames = new HashSet<string>(
                controller.parameters.Select(animatorParameter => animatorParameter.name));
            var existingNames = new HashSet<string>(
                expressionParameters.parameters.Select(expressionParameter => expressionParameter.name));

            var toAdd = controller.parameters
                .Select(animatorParameter => animatorParameter.name)
                .Where(name => !existingNames.Contains(name) && !(excludedNames != null && excludedNames.Contains(name)))
                .ToList();
            var toRemove = expressionParameters.parameters
                .Select(expressionParameter => expressionParameter.name)
                .Where(name => !controllerNames.Contains(name) || (excludedNames != null && excludedNames.Contains(name)))
                .ToList();

            return (toAdd, toRemove);
        }

        internal static void SyncVrcParameters(VRCExpressionParameters expressionParameters,
            AnimatorController controller, HashSet<string> excludedNames = null)
        {
            Undo.RecordObject(expressionParameters, "Sync VRC Parameters Asset");
            var existingByName = expressionParameters.parameters
                .ToDictionary(expressionParameter => expressionParameter.name);

            var paramsList = new List<VRCExpressionParameters.Parameter>(controller.parameters.Length);
            foreach (var animatorParameter in controller.parameters)
            {
                if (excludedNames != null && excludedNames.Contains(animatorParameter.name)) continue;
                if (existingByName.TryGetValue(animatorParameter.name, out var existingParameter))
                {
                    paramsList.Add(existingParameter);
                    continue;
                }
                paramsList.Add(new VRCExpressionParameters.Parameter
                {
                    name          = animatorParameter.name,
                    valueType     = MapToVrcValueType(animatorParameter.type),
                    networkSynced = false,
                    saved         = false,
                    defaultValue  = 0f
                });
            }

            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        internal static void AddToVrcParameters(VRCExpressionParameters expressionParameters,
            string paramName, AnimatorControllerParameterType paramType)
        {
            Undo.RecordObject(expressionParameters, "Add VRC Parameter");
            var newParam = new VRCExpressionParameters.Parameter
            {
                name          = paramName,
                valueType     = MapToVrcValueType(paramType),
                networkSynced = true,
                saved         = false,
                defaultValue  = 0f
            };
            var paramsList = expressionParameters.parameters.ToList();
            paramsList.Add(newParam);
            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        internal static void RemapVrcParameters(string oldName, string newName)
        {
            var expressionParameters = VRCSyncCache.GetExpressionParameters();
            if (expressionParameters?.parameters != null)
            {
                bool modified = false;
                foreach (var expressionParameter in expressionParameters.parameters)
                    if (expressionParameter.name == oldName) { modified = true; break; }
                if (modified)
                {
                    Undo.RecordObject(expressionParameters, "Rename VRC Parameter");
                    foreach (var expressionParameter in expressionParameters.parameters)
                        if (expressionParameter.name == oldName) { expressionParameter.name = newName; break; }
                    EditorUtility.SetDirty(expressionParameters);
                }
            }

            var visited = new HashSet<int>();
            var expressionsMenu = VRCSyncCache.GetExpressionsMenu();
            if (expressionsMenu != null)
                RenameInMenu(expressionsMenu, oldName, newName, visited);

            foreach (var vrcFuryMenu in VRCSyncCache.GetVrcFuryExpressionsMenus())
                RenameInMenu(vrcFuryMenu, oldName, newName, visited);

            RenameVrcFuryFeatureParams(oldName, newName);
        }

        static readonly Dictionary<(string typeName, string fieldName), FieldInfo> _vrcFuryFieldCache = new();

        static void RenameVrcFuryFeatureParams(string oldName, string newName)
        {
            var componentHost = VRCSyncCache.GetVrcFuryComponentHost();
            if (componentHost == null) return;

            var vrcfuryType = AccessTools.TypeByName("VF.Model.VRCFury");
            if (vrcfuryType == null) return;

            var getAllFeaturesMethod = AccessTools.Method(vrcfuryType, "GetAllFeatures");
            if (getAllFeaturesMethod == null) return;

            foreach (var component in componentHost.GetComponents(vrcfuryType))
            {
                var features = getAllFeaturesMethod.Invoke(component, null) as System.Collections.IEnumerable;
                if (features == null) continue;

                bool componentModified = false;
                foreach (var feature in features)
                {
                    if (feature == null) continue;
                    var featureType = feature.GetType();
                    string typeName = featureType.FullName;

                    string[] fieldsToCheck = typeName switch
                    {
                        "VF.Model.Feature.Toggle"           => new[] { "driveGlobalParam", "globalParam" },
                        "VF.Model.Feature.FullController"   => new[] { "toggleParam", "injectSpsDepthParam", "injectSpsVelocityParam" },
                        "VF.Model.Feature.MmdCompatibility" => new[] { "globalParam" },
                        _ => null
                    };

                    if (fieldsToCheck == null) continue;

                    foreach (var fieldName in fieldsToCheck)
                    {
                        var key = (typeName, fieldName);
                        if (!_vrcFuryFieldCache.TryGetValue(key, out var field))
                        {
                            field = AccessTools.Field(featureType, fieldName);
                            _vrcFuryFieldCache[key] = field;
                        }
                        if (field == null) continue;
                        if (field.GetValue(feature) as string != oldName) continue;

                        if (!componentModified)
                        {
                            Undo.RecordObject(component as UnityEngine.Object, "Rename VRC Parameter");
                            componentModified = true;
                        }
                        field.SetValue(feature, newName);
                    }
                }

                if (componentModified)
                    EditorUtility.SetDirty(component as UnityEngine.Object);
            }
        }

        static void RenameInMenu(VRCExpressionsMenu menu, string oldName, string newName, HashSet<int> visited)
        {
            if (!visited.Add(menu.GetInstanceID())) return;

            bool modified = false;
            foreach (var control in menu.controls)
            {
                if (control.parameter?.name == oldName) { modified = true; break; }
                if (control.subParameters != null)
                    foreach (var subParameter in control.subParameters)
                        if (subParameter?.name == oldName) { modified = true; break; }
                if (modified) break;
            }

            if (modified)
            {
                Undo.RecordObject(menu, "Rename VRC Parameter");
                foreach (var control in menu.controls)
                {
                    if (control.parameter?.name == oldName)
                        control.parameter.name = newName;
                    if (control.subParameters != null)
                        foreach (var subParameter in control.subParameters)
                            if (subParameter?.name == oldName)
                                subParameter.name = newName;
                }
                EditorUtility.SetDirty(menu);
            }

            foreach (var control in menu.controls)
                if (control.subMenu != null)
                    RenameInMenu(control.subMenu, oldName, newName, visited);
        }

        internal static void ConvertVrcParameter(VRCExpressionParameters expressionParameters,
            string paramName, AnimatorControllerParameterType newType)
        {
            foreach (var expressionParameter in expressionParameters.parameters)
            {
                if (expressionParameter.name != paramName) continue;
                Undo.RecordObject(expressionParameters, "Convert VRC Parameter");
                expressionParameter.valueType = MapToVrcValueType(newType);
                EditorUtility.SetDirty(expressionParameters);
                break;
            }
        }

        internal static void SetVrcSynced(VRCExpressionParameters expressionParameters,
            string paramName, bool synced)
        {
            Undo.RecordObject(expressionParameters,
                synced ? "Set VRC Parameter Synced" : "Set VRC Parameter Not Synced");
            foreach (var expressionParameter in expressionParameters.parameters)
            {
                if (expressionParameter.name == paramName)
                {
                    expressionParameter.networkSynced = synced;
                    break;
                }
            }
            EditorUtility.SetDirty(expressionParameters);
        }
#endif
    }
}
#endif
