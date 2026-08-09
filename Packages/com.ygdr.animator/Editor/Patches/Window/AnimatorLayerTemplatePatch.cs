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
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ReorderableList = UnityEditorInternal.ReorderableList;
using Label = UnityEngine.UIElements.Label;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerToolbar
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnToolbarGUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var interceptMethod = AccessTools.Method(typeof(PatchLayerToolbar), nameof(InterceptAddLayer));

            int addLayerIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(WindowPatchReflection.AddNewLayerMethod))
                {
                    addLayerIdx = i;
                    break;
                }
            }

            if (addLayerIdx < 0)
            {
                Debug.LogError("[AnimatorTools] PatchLayerToolbar: AddNewLayer call not found in OnToolbarGUI");
                return list;
            }

            // Find next leave/leave.s to exit the try block (OnToolbarGUI wraps in using EditorGUI.DisabledScope)
            CodeInstruction leaveInstruction = null;
            for (int i = addLayerIdx + 1; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Leave || list[i].opcode == OpCodes.Leave_S)
                {
                    leaveInstruction = list[i];
                    break;
                }
            }

            if (leaveInstruction == null)
                Debug.LogError("[AnimatorTools] PatchLayerToolbar: leave instruction not found — transpiler incomplete");

            var result = new List<CodeInstruction>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (i == addLayerIdx)
                {
                    // Stack before: animatorControllerTool (from preceding ldloc)
                    // Inject ldarg.0 so InterceptAddLayer receives (tool, layerControllerView)
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, interceptMethod));
                    if (leaveInstruction != null)
                        result.Add(new CodeInstruction(leaveInstruction.opcode, leaveInstruction.operand));
                    continue;
                }
                result.Add(list[i]);
            }
            return result;
        }

        internal static void InterceptAddLayer(object animatorControllerTool, object layerControllerView)
        {
            try
            {
                if (!AnimatorDefaultSettings.Load().layerTemplateButtonEnabled)
                {
                    WindowPatchReflection.AddNewLayerMethod.Invoke(animatorControllerTool, null);
                    return;
                }

                var menu = new GenericMenu();

                menu.AddItem(new GUIContent(L10n.Get("layer_template.new_layer")), false, () =>
                {
                    WindowPatchReflection.AddNewLayerMethod.Invoke(animatorControllerTool, null);
                    UpdateListAndBeginRename(animatorControllerTool, layerControllerView);
                });

                menu.AddItem(new GUIContent(L10n.Get("toggle.menu_item")), false, () =>
                {
                    var capturedController = WindowPatchReflection.GetOpenController();
                    if (capturedController != null)
                        AnimatorGameObjectToggleWindow.Open(capturedController);
                });

                menu.AddSeparator("");

                var templateControllers = LoadTemplateControllers();
                var packageTemplates    = templateControllers.Where(t => !t.isUser).ToList();
                var userTemplates       = templateControllers.Where(t => t.isUser).ToList();

                if (packageTemplates.Count == 0 && userTemplates.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent(L10n.Get("layer_template.no_templates")));
                }
                else
                {
                    foreach (var (templateName, templateController, _) in packageTemplates)
                    {
                        var capturedController = templateController;
                        var capturedLayerView  = layerControllerView;
                        menu.AddItem(new GUIContent(templateName.Replace('.', '/')), false, () =>
                            AnimatorTemplateParameterWindow.Open(capturedController, capturedLayerView));
                    }

                    if (userTemplates.Count > 0)
                    {
                        if (packageTemplates.Count > 0) menu.AddSeparator("");
                        foreach (var (templateName, templateController, _) in userTemplates)
                        {
                            var capturedController = templateController;
                            var capturedLayerView  = layerControllerView;
                            menu.AddItem(new GUIContent($"User/{templateName.Replace('.', '/')}"), false, () =>
                                AnimatorTemplateParameterWindow.Open(capturedController, capturedLayerView));
                        }
                        menu.AddSeparator("");
                        foreach (var (templateName, templateController, _) in userTemplates)
                        {
                            string capturedDir  = System.IO.Path.GetDirectoryName(
                                AssetDatabase.GetAssetPath(templateController)).Replace('\\', '/');
                            string capturedName = templateName;
                            menu.AddItem(new GUIContent($"{L10n.Get("layer_template.delete_template")}/{templateName.Replace('.', '/')}"), false, () =>
                            {
                                if (EditorUtility.DisplayDialog(L10n.Get("layer_template.delete_confirm_title"),
                                    string.Format(L10n.Get("layer_template.delete_confirm_body"), capturedName),
                                    L10n.Get("layer_template.delete_confirm_ok"),
                                    L10n.Get("layer_template.delete_confirm_cancel")))
                                    AssetDatabase.DeleteAsset(capturedDir);
                            });
                        }
                    }
                }

                menu.ShowAsContext();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerToolbar: {e}");
            }
        }

        static void UpdateListAndBeginRename(object animatorControllerTool, object layerControllerView)
        {
            try
            {
                var animController = WindowPatchReflection.AnimatorControllerGetter
                    .Invoke(animatorControllerTool, null) as AnimatorController;
                var layerList = WindowPatchReflection.LayerListField.GetValue(layerControllerView) as ReorderableList;
                int newIndex = (int)WindowPatchReflection.SelectedLayerIndexProperty.GetValue(animatorControllerTool);

                layerList.list = animController.layers;
                layerList.index = newIndex;
                WindowPatchReflection.LayerSelectedIndexField?.SetValue(layerControllerView, newIndex);

                var renameOverlay = WindowPatchReflection.LayerRenameOverlayProperty.GetValue(layerControllerView);
                if ((bool)WindowPatchReflection.RenameOverlayIsRenamingMethod.Invoke(renameOverlay, null))
                    WindowPatchReflection.LayerRenameEndMethod.Invoke(layerControllerView, null);
                WindowPatchReflection.RenameOverlayBeginRenameMethod.Invoke(renameOverlay,
                    new object[] { animController.layers[newIndex].name, newIndex, 0.1f });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerToolbar.UpdateListAndBeginRename: {e}");
            }
        }

        internal const string UserLayerTemplatesPath     = "Assets/YGDR Animator/User Templates/Layer Templates";
        internal const string UserBlendTreeTemplatesPath = "Assets/YGDR Animator/User Templates/Blend Tree Templates";

        internal static List<(string name, AnimatorController controller, bool isUser)> _templateCache;
        internal static List<(string name, BlendTree blendTree)> _blendTreeTemplateCache;

        [InitializeOnLoadMethod]
        static void ClearTemplateCache()
        {
            _templateCache = null;
            _blendTreeTemplateCache = null;
        }

        static List<(string name, AnimatorController controller, bool isUser)> LoadTemplateControllers()
        {
            if (_templateCache != null) return _templateCache;

            var result = new List<(string, AnimatorController, bool)>();

            var packageGuids = AssetDatabase.FindAssets("t:AnimatorController",
                new[] { "Packages/com.ygdr.animator/Templates" });
            foreach (var guid in packageGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller != null)
                    result.Add((controller.name, controller, false));
            }

            if (AssetDatabase.IsValidFolder(UserLayerTemplatesPath))
            {
                var userGuids = AssetDatabase.FindAssets("t:AnimatorController", new[] { UserLayerTemplatesPath });
                foreach (var guid in userGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    if (controller != null)
                        result.Add((controller.name, controller, true));
                }
            }

            _templateCache = result;
            return _templateCache;
        }

        internal static List<(string name, BlendTree blendTree)> LoadBlendTreeTemplateAssets()
        {
            if (_blendTreeTemplateCache != null) return _blendTreeTemplateCache;

            var result = new List<(string, BlendTree)>();
            if (AssetDatabase.IsValidFolder(UserBlendTreeTemplatesPath))
            {
                var guids = AssetDatabase.FindAssets("t:BlendTree", new[] { UserBlendTreeTemplatesPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var blendTree = AssetDatabase.LoadAssetAtPath<BlendTree>(path);
                    if (blendTree != null && AssetDatabase.IsMainAsset(blendTree))
                        result.Add((blendTree.name, blendTree));
                }
            }

            _blendTreeTemplateCache = result;
            return _blendTreeTemplateCache;
        }

    }
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerRightClick
    {
        static readonly MethodInfo _deleteLayerMethod =
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "DeleteLayer");

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect)
        {
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.KeyDown || currentEvent.keyCode != KeyCode.Delete) return;

                var reorderableList = WindowPatchReflection.LayerListField?.GetValue(__instance)
                    as UnityEditorInternal.ReorderableList;
                if (reorderableList == null || !reorderableList.HasKeyboardControl()) return;

                _deleteLayerMethod?.Invoke(__instance, null);
                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerRightClick delete: {e}");
            }
        }
    }

    internal class AnimatorTemplateParameterWindow : EditorWindow
    {
        static AnimatorTemplateParameterWindow s_activeWindow;

        AnimatorController _templateController;
        object _targetLayerView;
        string[] _renamedParameterNames;
        string[] _cachedParamLabels;
        string _targetControllerPath;
        string _importedLayerName;

        // Create mode
        bool               _isCreateMode;
        AnimatorController _sourceController;
        int                _sourceLayerIndex;
        string             _templateName;
        AnimatorControllerParameter[] _createModeParams;

        // Blend tree mode (create or import)
        bool               _isBlendTreeMode;
        BlendTree          _sourceBlendTree;
        BlendTree          _templateBlendTree;
        BlendTree          _targetBlendTree;
        AnimatorController _targetControllerForBT;
        AnimatorControllerParameter[] _blendTreeTemplateParams;
        string             _importedBlendTreeName;

        VisualElement _panel;
        Label _leftHeaderLabel;
        Label _rightHeaderLabel;
        ScrollView _rowsScroll;
        VisualElement _footerContainer;
        Button _confirmButton;

        static AnimatorTemplateParameterWindow GetOrCreate()
        {
            s_activeWindow = GetWindow<AnimatorTemplateParameterWindow>("Template");
            s_activeWindow.minSize = new Vector2(400, 280);
            return s_activeWindow;
        }

        internal static void OpenCreate(AnimatorController controller, int layerIndex)
        {
            var window = GetOrCreate();
            window.titleContent       = new GUIContent(L10n.Get("layer_template.create_template"));
            window._isCreateMode      = true;
            window._isBlendTreeMode   = false;
            window._sourceController  = controller;
            window._sourceLayerIndex  = layerIndex;
            window._templateName      = controller.layers[layerIndex].name;

            var qualifiedNames         = CollectLayerParams(controller, layerIndex);
            var parameters             = controller.parameters.Where(p => qualifiedNames.Contains(p.name)).ToArray();
            window._createModeParams       = parameters;
            window._renamedParameterNames  = parameters.Select(p => p.name).ToArray();
            window._cachedParamLabels      = BuildParamLabels(parameters);
            window.RefreshAll();
            window.Focus();
        }

        internal static void Open(AnimatorController templateController, object targetLayerView)
        {
            var window = GetOrCreate();
            window.titleContent        = new GUIContent(L10n.Get("layer_template.import_template"));
            window._isCreateMode       = false;
            window._isBlendTreeMode    = false;
            window._templateController = templateController;
            window._targetLayerView    = targetLayerView;

            var parameters                = templateController.parameters;
            window._renamedParameterNames = parameters.Select(p => p.name).ToArray();
            window._cachedParamLabels     = BuildParamLabels(parameters);

            var targetController = Traverse.Create(targetLayerView)
                .Field("m_Host").Property("animatorController").GetValue<AnimatorController>();
            window._targetControllerPath = targetController != null
                ? AssetDatabase.GetAssetPath(targetController) : "";
            window._importedLayerName = templateController.layers.Length > 0
                ? templateController.layers[0].name : "";
            window.RefreshAll();
            window.Focus();
        }

        internal static void OpenCreateBlendTree(BlendTree sourceBlendTree, AnimatorController sourceController)
        {
            var window = GetOrCreate();
            window.titleContent     = new GUIContent(L10n.Get("layer_template.create_blendtree"));
            window._isCreateMode    = true;
            window._isBlendTreeMode = true;
            window._sourceBlendTree = sourceBlendTree;
            window._templateName    = sourceBlendTree.name;

            var paramNames = new HashSet<string>();
            CollectMotionParamNames(sourceBlendTree, paramNames);
            paramNames.Remove("");
            var parameters = sourceController != null
                ? sourceController.parameters.Where(p => paramNames.Contains(p.name)).ToArray()
                : paramNames.Select(name => new AnimatorControllerParameter { name = name }).ToArray();

            window._createModeParams      = parameters;
            window._renamedParameterNames = parameters.Select(p => p.name).ToArray();
            window._cachedParamLabels     = BuildParamLabels(parameters);
            window.RefreshAll();
            window.Focus();
        }

        internal static void OpenImportBlendTree(BlendTree templateBlendTree, BlendTree targetBlendTree, AnimatorController targetController)
        {
            var window = GetOrCreate();
            window.titleContent           = new GUIContent(L10n.Get("layer_template.import_blendtree"));
            window._isCreateMode          = false;
            window._isBlendTreeMode       = true;
            window._templateBlendTree     = templateBlendTree;
            window._targetBlendTree       = targetBlendTree;
            window._targetControllerForBT = targetController;
            window._importedBlendTreeName = templateBlendTree.name;

            var paramNames = new HashSet<string>();
            CollectMotionParamNames(templateBlendTree, paramNames);
            paramNames.Remove("");
            var parameters = paramNames
                .Select(name => new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float })
                .ToArray();

            window._blendTreeTemplateParams   = parameters;
            window._renamedParameterNames     = parameters.Select(p => p.name).ToArray();
            window._cachedParamLabels         = BuildParamLabels(parameters);
            window.RefreshAll();
            window.Focus();
        }

        void OnEnable()
        {
            L10n.OnLanguageChanged += OnLanguageChanged;
        }

        void OnDestroy()
        {
            SharedWindowStyles.UnregisterPaletteRefresh(RefreshPaletteColors);
            L10n.OnLanguageChanged -= OnLanguageChanged;
            s_activeWindow = null;
        }

        void OnLanguageChanged()
        {
            titleContent = new GUIContent(_isCreateMode
                ? (_isBlendTreeMode ? L10n.Get("layer_template.create_blendtree") : L10n.Get("layer_template.create_template"))
                : (_isBlendTreeMode ? L10n.Get("layer_template.import_blendtree") : L10n.Get("layer_template.import_template")));
            RefreshAll();
        }

        // ── GUI (native UI Toolkit) ──────────────────────────────────────────

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.EnableInClassList("ygdr-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("ygdr-light", !EditorGUIUtility.isProSkin);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ygdr.animator/Editor/UI/SharedWindowStyles.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            BuildLayout(root);

            SharedWindowStyles.RegisterPaletteRefresh(RefreshPaletteColors);
            RefreshAll();
        }

        void BuildLayout(VisualElement root)
        {
            var body = new VisualElement();
            body.AddToClassList("ygdr-tpl-body");
            root.Add(body);

            _panel = new VisualElement();
            _panel.AddToClassList("ygdr-tpl-panel");
            body.Add(_panel);

            _rowsScroll = SharedWindowStyles.BuildColumnHeaderAndScroll(_panel, "ygdr-tpl-col-header-row",
                "ygdr-tpl-col-header", "ygdr-tpl-rows-scroll", out _leftHeaderLabel, out _rightHeaderLabel);

            _footerContainer = new VisualElement();
            _panel.Add(_footerContainer);
        }

        void RefreshPaletteColors()
        {
            if (_panel == null) return;
            SharedWindowStyles.ApplyStandardPanelPalette(_panel, _leftHeaderLabel, _rightHeaderLabel, _rowsScroll);
            if (_confirmButton != null) _confirmButton.style.backgroundColor = SharedWindowStyles.AccentColor;
        }

        void RefreshAll()
        {
            if (_panel == null) return;
            RefreshParamList();
            RefreshFooter();
        }

        AnimatorControllerParameter[] GetParameters() => _isCreateMode
            ? (_createModeParams ?? System.Array.Empty<AnimatorControllerParameter>())
            : _isBlendTreeMode
                ? (_blendTreeTemplateParams ?? System.Array.Empty<AnimatorControllerParameter>())
                : (_templateController != null ? _templateController.parameters : System.Array.Empty<AnimatorControllerParameter>());

        void RefreshParamList()
        {
            var parameters = GetParameters();

            _leftHeaderLabel.text = L10n.Get("layer_template.parameter");
            _rightHeaderLabel.text = _isCreateMode ? L10n.Get("layer_template.export_as") : L10n.Get("layer_template.import_as");

            _rowsScroll.Clear();

            if (parameters.Length == 0)
            {
                var emptyLabel = new Label(L10n.Get("layer_template.no_params"));
                emptyLabel.AddToClassList("ygdr-fu-empty");
                _rowsScroll.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                int rowIndex = i;

                var rowElement = SharedWindowStyles.MakeStripedRow("ygdr-tpl-row", rowIndex);

                var nameLabel = new Label(_cachedParamLabels[rowIndex]) { enableRichText = true };
                nameLabel.AddToClassList("ygdr-tpl-cell-label");
                rowElement.Add(nameLabel);

                var renameField = new TextField { value = _renamedParameterNames[rowIndex] };
                renameField.AddToClassList("ygdr-tpl-cell-field");
                renameField.RegisterValueChangedCallback(evt => _renamedParameterNames[rowIndex] = evt.newValue);
                rowElement.Add(renameField);

                _rowsScroll.Add(rowElement);
            }
        }

        void RefreshFooter()
        {
            _footerContainer.Clear();

            if (_isCreateMode)
            {
                AddFooterLabel(L10n.Get("layer_template.template_name"));
                var nameField = new TextField { value = _templateName };
                nameField.AddToClassList("ygdr-tpl-name-field");
                nameField.RegisterValueChangedCallback(evt =>
                {
                    _templateName = evt.newValue;
                    UpdateConfirmButtonState();
                });
                _footerContainer.Add(nameField);
            }
            else if (_isBlendTreeMode && _targetControllerForBT != null)
            {
                string targetDir = System.IO.Path.GetDirectoryName(
                    AssetDatabase.GetAssetPath(_targetControllerForBT)).Replace('\\', '/');
                if (!string.IsNullOrEmpty(targetDir))
                    AddFooterLabel($"Clips copied to {targetDir}");

                AddFooterLabel(L10n.Get("layer_template.blend_tree_name"));
                var nameField = new TextField { value = _importedBlendTreeName };
                nameField.AddToClassList("ygdr-tpl-name-field");
                nameField.RegisterValueChangedCallback(evt => _importedBlendTreeName = evt.newValue);
                _footerContainer.Add(nameField);
            }
            else if (!string.IsNullOrEmpty(_targetControllerPath))
            {
                AddFooterLabel($"Template clips copied to {System.IO.Path.GetDirectoryName(_targetControllerPath)}");

                AddFooterLabel(L10n.Get("layer_template.layer_name"));
                var nameField = new TextField { value = _importedLayerName };
                nameField.AddToClassList("ygdr-tpl-name-field");
                nameField.RegisterValueChangedCallback(evt => _importedLayerName = evt.newValue);
                _footerContainer.Add(nameField);
            }

            _confirmButton = new Button(OnConfirmClicked)
            {
                text = _isCreateMode ? L10n.Get("layer_template.create_template") : L10n.Get("layer_template.confirm")
            };
            _confirmButton.AddToClassList("ygdr-tpl-confirm-button");
            _confirmButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            var hoverColor = new Color(
                SharedWindowStyles.AccentColor.r + 0.1f,
                SharedWindowStyles.AccentColor.g + 0.1f,
                SharedWindowStyles.AccentColor.b + 0.1f, 1f);
            _confirmButton.RegisterCallback<MouseEnterEvent>(_ => _confirmButton.style.backgroundColor = hoverColor);
            _confirmButton.RegisterCallback<MouseLeaveEvent>(_ => _confirmButton.style.backgroundColor = SharedWindowStyles.AccentColor);
            _footerContainer.Add(_confirmButton);

            UpdateConfirmButtonState();
        }

        void AddFooterLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ygdr-tpl-footer-label");
            _footerContainer.Add(label);
        }

        void UpdateConfirmButtonState()
        {
            bool canConfirm = !_isCreateMode || !string.IsNullOrWhiteSpace(_templateName);
            _confirmButton.SetEnabled(canConfirm);
        }

        void OnConfirmClicked()
        {
            if (_isCreateMode) ConfirmCreate();
            else               ConfirmImport();
            Close();
        }

        static string[] BuildParamLabels(AnimatorControllerParameter[] parameters)
        {
            var settings = AnimatorDefaultSettings.Load();
            var labels   = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string typeHex = ColorUtility.ToHtmlStringRGB(parameters[i].type switch
                {
                    AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                    AnimatorControllerParameterType.Int     => settings.paramColorInt,
                    AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                    AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                    _                                       => new Color(0.65f, 0.65f, 0.65f)
                });
                labels[i] = $"{parameters[i].name}  <color=#{typeHex}>{parameters[i].type}</color>";
            }
            return labels;
        }

        void ConfirmCreate()
        {
            try
            {
                if (_isBlendTreeMode) { ConfirmCreateBlendTree(); return; }
                string safeName    = _templateName.Trim().Replace('/', '.');
                string templateDir = $"{PatchLayerToolbar.UserLayerTemplatesPath}/{safeName}";
                EnsureAssetFolder(templateDir);

                string sourcePath = AssetDatabase.GetAssetPath(_sourceController);
                string destPath   = AssetDatabase.GenerateUniqueAssetPath($"{templateDir}/{safeName}.controller");
                if (!AssetDatabase.CopyAsset(sourcePath, destPath))
                {
                    Debug.LogError($"[AnimatorTools] CreateTemplate: CopyAsset failed to {destPath}");
                    return;
                }

                var templateController = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);
                if (templateController == null) return;

                int layerCount = templateController.layers.Length;
                for (int i = layerCount - 1; i > _sourceLayerIndex; i--)
                    templateController.RemoveLayer(i);
                for (int i = 0; i < _sourceLayerIndex; i++)
                    templateController.RemoveLayer(0);

                var qualifiedNames = CollectLayerParams(templateController, 0);
                foreach (var parameter in templateController.parameters.ToArray())
                    if (!qualifiedNames.Contains(parameter.name))
                        templateController.RemoveParameter(parameter);

                if (_createModeParams != null)
                {
                    for (int i = 0; i < _createModeParams.Length && i < _renamedParameterNames.Length; i++)
                    {
                        string oldName = _createModeParams[i].name;
                        string newName = _renamedParameterNames[i];
                        if (string.IsNullOrEmpty(newName) || oldName == newName) continue;
                        UpdateParamRefsInSM(templateController.layers[0].stateMachine, oldName, newName);
                        var paramToRemove = System.Array.Find(templateController.parameters, p => p.name == oldName);
                        if (paramToRemove != null)
                        {
                            templateController.RemoveParameter(paramToRemove);
                            if (!templateController.parameters.Any(p => p.name == newName))
                                templateController.AddParameter(newName, _createModeParams[i].type);
                        }
                    }
                }

                var clipCache = new Dictionary<string, AnimationClip>();
                CopyClipsInSM(templateController.layers[0].stateMachine, templateDir, clipCache);

                EditorUtility.SetDirty(templateController);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmCreate: {e}");
            }
        }

        void ConfirmCreateBlendTree()
        {
            try
            {
                if (_sourceBlendTree == null) return;

                string safeName    = _templateName.Trim().Replace('/', '.');
                string templateDir = $"{PatchLayerToolbar.UserBlendTreeTemplatesPath}/{safeName}";
                EnsureAssetFolder(templateDir);

                var clipCache  = new Dictionary<string, AnimationClip>();
                var copiedRoot = DeepCopyBlendTree(_sourceBlendTree, templateDir, clipCache);
                copiedRoot.name = safeName;

                if (_createModeParams != null)
                {
                    for (int i = 0; i < _createModeParams.Length && i < _renamedParameterNames.Length; i++)
                    {
                        string oldName = _createModeParams[i].name;
                        string newName = _renamedParameterNames[i];
                        if (string.IsNullOrEmpty(newName) || oldName == newName) continue;
                        RenameInBlendTreeDirect(copiedRoot, oldName, newName);
                    }
                }

                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{templateDir}/{safeName}.asset");
                AssetDatabase.CreateAsset(copiedRoot, assetPath);
                foreach (var subTree in CollectSubBlendTrees(copiedRoot))
                    AssetDatabase.AddObjectToAsset(subTree, assetPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmCreateBlendTree: {e}");
            }
        }

        void ConfirmImportBlendTree()
        {
            if (_templateBlendTree == null || _targetBlendTree == null || _targetControllerForBT == null) return;

            try
            {
                string controllerPath = AssetDatabase.GetAssetPath(_targetControllerForBT);
                string controllerDir  = System.IO.Path.GetDirectoryName(controllerPath).Replace('\\', '/');

                var clipCache  = new Dictionary<string, AnimationClip>();
                var copiedRoot = DeepCopyBlendTree(_templateBlendTree, controllerDir, clipCache);

                if (!string.IsNullOrWhiteSpace(_importedBlendTreeName))
                    copiedRoot.name = _importedBlendTreeName.Trim();

                if (_blendTreeTemplateParams != null)
                {
                    for (int i = 0; i < _blendTreeTemplateParams.Length && i < _renamedParameterNames.Length; i++)
                    {
                        string oldName = _blendTreeTemplateParams[i].name;
                        string newName = _renamedParameterNames[i];
                        if (string.IsNullOrEmpty(newName) || oldName == newName) continue;
                        RenameInBlendTreeDirect(copiedRoot, oldName, newName);
                    }
                }

                var subTrees = CollectSubBlendTrees(copiedRoot).ToList();
                copiedRoot.hideFlags = HideFlags.HideInHierarchy;
                foreach (var subTree in subTrees)
                    subTree.hideFlags = HideFlags.HideInHierarchy;

                Undo.SetCurrentGroupName("Import Blend Tree Template");
                int undoGroup = Undo.GetCurrentGroup();

                Undo.RegisterCreatedObjectUndo(copiedRoot, "Import Blend Tree Template");
                AssetDatabase.AddObjectToAsset(copiedRoot, _targetControllerForBT);
                foreach (var subTree in subTrees)
                {
                    Undo.RegisterCreatedObjectUndo(subTree, "Import Blend Tree Template");
                    AssetDatabase.AddObjectToAsset(subTree, _targetControllerForBT);
                }

                AddMissingBlendTreeParamsToController(copiedRoot, _targetControllerForBT);

                Undo.RecordObject(_targetBlendTree, "Import Blend Tree Template");
                _targetBlendTree.children = _targetBlendTree.children
                    .Append(new ChildMotion { motion = copiedRoot, timeScale = 1f })
                    .ToArray();
                EditorUtility.SetDirty(_targetBlendTree);
                EditorUtility.SetDirty(_targetControllerForBT);

                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmImportBlendTree: {e}");
            }
        }

        static BlendTree DeepCopyBlendTree(BlendTree source, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            var copy = new BlendTree
            {
                name                   = source.name,
                blendType              = source.blendType,
                blendParameter         = source.blendParameter,
                blendParameterY        = source.blendParameterY,
                minThreshold           = source.minThreshold,
                maxThreshold           = source.maxThreshold,
                useAutomaticThresholds = source.useAutomaticThresholds
            };

            var children    = source.children;
            var newChildren = new ChildMotion[children.Length];
            for (int i = 0; i < children.Length; i++)
            {
                Motion newMotion = children[i].motion;
                if (children[i].motion is BlendTree childBT)
                    newMotion = DeepCopyBlendTree(childBT, destDir, clipCache);
                else if (children[i].motion is AnimationClip clip && AssetDatabase.IsMainAsset(clip))
                {
                    var copied = CopyClipToDir(clip, destDir, clipCache);
                    if (copied != null) newMotion = copied;
                }
                newChildren[i] = new ChildMotion
                {
                    motion               = newMotion,
                    threshold            = children[i].threshold,
                    position             = children[i].position,
                    timeScale            = children[i].timeScale,
                    cycleOffset          = children[i].cycleOffset,
                    directBlendParameter = children[i].directBlendParameter,
                    mirror               = children[i].mirror
                };
            }
            copy.children = newChildren;
            return copy;
        }

        static IEnumerable<BlendTree> CollectSubBlendTrees(BlendTree root)
        {
            foreach (var child in root.children)
            {
                if (child.motion is BlendTree subTree)
                {
                    yield return subTree;
                    foreach (var nested in CollectSubBlendTrees(subTree))
                        yield return nested;
                }
            }
        }

        static void RenameInBlendTreeDirect(BlendTree blendTree, string oldName, string newName)
        {
            if (blendTree.blendParameter  == oldName) blendTree.blendParameter  = newName;
            if (blendTree.blendParameterY == oldName) blendTree.blendParameterY = newName;

            var children = blendTree.children;
            bool modified = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].directBlendParameter == oldName)
                {
                    var child = children[i];
                    children[i] = new ChildMotion
                    {
                        motion               = child.motion,
                        threshold            = child.threshold,
                        position             = child.position,
                        timeScale            = child.timeScale,
                        cycleOffset          = child.cycleOffset,
                        directBlendParameter = newName,
                        mirror               = child.mirror
                    };
                    modified = true;
                }
                if (children[i].motion is BlendTree childBT)
                    RenameInBlendTreeDirect(childBT, oldName, newName);
            }
            if (modified) blendTree.children = children;
        }

        static void AddMissingBlendTreeParamsToController(BlendTree blendTree, AnimatorController controller)
        {
            var paramNames = new HashSet<string>();
            CollectMotionParamNames(blendTree, paramNames);
            paramNames.Remove("");

            var existingNames = new HashSet<string>(controller.parameters.Select(p => p.name));
            bool recorded = false;
            foreach (var name in paramNames)
            {
                if (existingNames.Contains(name)) continue;
                if (!recorded) { Undo.RecordObject(controller, "Add Blend Tree Template Parameters"); recorded = true; }
                controller.AddParameter(name, AnimatorControllerParameterType.Float);
                existingNames.Add(name);
            }
        }

        static void EnsureAssetFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static HashSet<string> CollectLayerParams(AnimatorController controller, int layerIndex)
        {
            var paramNames = new HashSet<string>();
            CollectSMParams(controller.layers[layerIndex].stateMachine, paramNames);
            paramNames.Remove("");
            return paramNames;
        }

        static void CollectSMParams(AnimatorStateMachine sm, HashSet<string> paramNames)
        {
            foreach (var transition in sm.anyStateTransitions)
                foreach (var condition in transition.conditions)
                    paramNames.Add(condition.parameter);
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                foreach (var transition in state.transitions)
                    foreach (var condition in transition.conditions)
                        paramNames.Add(condition.parameter);
                if (state.speedParameterActive)       paramNames.Add(state.speedParameter);
                if (state.timeParameterActive)        paramNames.Add(state.timeParameter);
                if (state.mirrorParameterActive)      paramNames.Add(state.mirrorParameter);
                if (state.cycleOffsetParameterActive) paramNames.Add(state.cycleOffsetParameter);
                CollectMotionParamNames(state.motion, paramNames);
                CollectBehaviourParamNames(state.behaviours, paramNames);
            }
            foreach (var childSM in sm.stateMachines)
                CollectSMParams(childSM.stateMachine, paramNames);
        }

        static void CollectMotionParamNames(UnityEngine.Motion motion, HashSet<string> paramNames)
        {
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(Animator))
                        paramNames.Add(binding.propertyName);
                return;
            }
            if (motion is BlendTree blendTree)
            {
                if (!string.IsNullOrEmpty(blendTree.blendParameter))  paramNames.Add(blendTree.blendParameter);
                if (!string.IsNullOrEmpty(blendTree.blendParameterY)) paramNames.Add(blendTree.blendParameterY);
                foreach (var child in blendTree.children)
                {
                    if (!string.IsNullOrEmpty(child.directBlendParameter)) paramNames.Add(child.directBlendParameter);
                    CollectMotionParamNames(child.motion, paramNames);
                }
            }
        }

        static void CollectBehaviourParamNames(StateMachineBehaviour[] behaviours, HashSet<string> paramNames)
        {
#if VRC_SDK_VRCSDK3
            AnimatorParameterOps.CollectBehaviourNames(behaviours, paramNames);
#endif
        }

        static void CopyClipsInSM(AnimatorStateMachine sm, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.motion is AnimationClip clip && AssetDatabase.IsMainAsset(clip))
                {
                    var copied = CopyClipToDir(clip, destDir, clipCache);
                    if (copied != null) { state.motion = copied; EditorUtility.SetDirty(state); }
                }
                else if (state.motion is BlendTree blendTree)
                    CopyClipsInBlendTree(blendTree, destDir, clipCache);
            }
            foreach (var childSM in sm.stateMachines)
                CopyClipsInSM(childSM.stateMachine, destDir, clipCache);
        }

        static void CopyClipsInBlendTree(BlendTree blendTree, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            var children = blendTree.children;
            bool modified = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip && AssetDatabase.IsMainAsset(clip))
                {
                    var copied = CopyClipToDir(clip, destDir, clipCache);
                    if (copied != null) { children[i].motion = copied; modified = true; }
                }
                else if (children[i].motion is BlendTree childBT)
                    CopyClipsInBlendTree(childBT, destDir, clipCache);
            }
            if (modified) { blendTree.children = children; EditorUtility.SetDirty(blendTree); }
        }

        static bool IsEmptyClip(AnimationClip clip) =>
            AnimationUtility.GetCurveBindings(clip).Length == 0 &&
            AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0 &&
            clip.events.Length == 0;

        static AnimationClip GetOrCreateBufferClip(string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            const string key = "__buffer__";
            if (clipCache.TryGetValue(key, out var cached)) return cached;
            string bufferPath = $"{destDir}/BufferClip.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(bufferPath);
            if (existing == null)
            {
                existing = new AnimationClip();
                AssetDatabase.CreateAsset(existing, bufferPath);
            }
            clipCache[key] = existing;
            return existing;
        }

        static AnimationClip CopyClipToDir(AnimationClip clip, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            if (IsEmptyClip(clip)) return GetOrCreateBufferClip(destDir, clipCache);
            string sourcePath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            if (clipCache.TryGetValue(sourcePath, out var cached)) return cached;
            string destPath = AssetDatabase.GenerateUniqueAssetPath($"{destDir}/{clip.name}.anim");
            if (!AssetDatabase.CopyAsset(sourcePath, destPath)) return null;
            var copied = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);
            clipCache[sourcePath] = copied;
            return copied;
        }

        void ConfirmImport()
        {
            try
            {
                if (_isBlendTreeMode) { ConfirmImportBlendTree(); return; }
                ConfirmImportImpl();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmImport: {e}");
            }
        }

        void ConfirmImportImpl()
        {
            if (_templateController == null || _targetLayerView == null) return;

            var targetController = Traverse.Create(_targetLayerView)
                .Field("m_Host").Property("animatorController")
                .GetValue<AnimatorController>();
            if (targetController == null) return;

            var existingParamNames = new HashSet<string>(
                targetController.parameters.Select(parameter => parameter.name));
            int layerCountBefore = targetController.layers.Length;

            Undo.SetCurrentGroupName("Import Template Layers");
            int undoGroup = Undo.GetCurrentGroup();

            PatchLayerCopyPaste.ImportAllLayersFromTemplate(_templateController, _targetLayerView);

            var newLayers = targetController.layers.Skip(layerCountBefore).ToArray();

            if (!string.IsNullOrWhiteSpace(_importedLayerName) && newLayers.Length > 0)
            {
                var allLayers = targetController.layers;
                allLayers[layerCountBefore].name = _importedLayerName.Trim();
                targetController.layers = allLayers;
                newLayers = targetController.layers.Skip(layerCountBefore).ToArray();
            }

            CreateLocalClipsForNewLayers(targetController, newLayers);
            SyncClipAAPParams(targetController, _templateController, newLayers);
            SyncBehaviourParams(targetController, _templateController, newLayers);

            var templateParameters = _templateController.parameters;
            for (int i = 0; i < templateParameters.Length && i < _renamedParameterNames.Length; i++)
            {
                string oldName = templateParameters[i].name;
                string newName = _renamedParameterNames[i];
                if (string.IsNullOrEmpty(newName) || oldName == newName) continue;

                foreach (var newLayer in newLayers)
                    UpdateParamRefsInSM(newLayer.stateMachine, oldName, newName);

                Undo.RecordObject(targetController, "Rename Template Parameter");

                bool oldParamIsImportOnly = !existingParamNames.Contains(oldName);
                if (oldParamIsImportOnly)
                {
                    var paramToRemove = System.Array.Find(targetController.parameters,
                        parameter => parameter.name == oldName);
                    if (paramToRemove != null)
                        targetController.RemoveParameter(paramToRemove);
                }

                if (!targetController.parameters.Any(parameter => parameter.name == newName))
                    targetController.AddParameter(newName, templateParameters[i].type);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(targetController);
        }

        static void CreateLocalClipsForNewLayers(AnimatorController targetController,
            AnimatorControllerLayer[] newLayers)
        {
            string controllerPath = AssetDatabase.GetAssetPath(targetController);
            string controllerDir = System.IO.Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            string controllerName = targetController.name;
            var clipCache = new Dictionary<string, AnimationClip>();

            foreach (var layer in newLayers)
                ReplaceClipsInSM(layer.stateMachine, controllerDir, controllerName, clipCache);
        }

        static void ReplaceClipsInSM(AnimatorStateMachine sm, string controllerDir, string controllerName,
            Dictionary<string, AnimationClip> clipCache)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.motion is AnimationClip clip)
                {
                    var localClip = CopyClipToControllerDir(clip, controllerDir, controllerName, clipCache);
                    if (localClip != null)
                    {
                        Undo.RecordObject(state, "Create Local Clip");
                        state.motion = localClip;
                        EditorUtility.SetDirty(state);
                    }
                }
                else if (state.motion is BlendTree blendTree)
                    ReplaceClipsInBlendTree(blendTree, controllerDir, controllerName, clipCache);
            }
            foreach (var childStateMachine in sm.stateMachines)
                ReplaceClipsInSM(childStateMachine.stateMachine, controllerDir, controllerName, clipCache);
        }

        static void ReplaceClipsInBlendTree(BlendTree blendTree, string controllerDir, string controllerName,
            Dictionary<string, AnimationClip> clipCache)
        {
            var children = blendTree.children;
            bool modified = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip)
                {
                    var localClip = CopyClipToControllerDir(clip, controllerDir, controllerName, clipCache);
                    if (localClip != null)
                    {
                        if (!modified) { Undo.RecordObject(blendTree, "Create Local Clip"); modified = true; }
                        children[i].motion = localClip;
                    }
                }
                else if (children[i].motion is BlendTree childBT)
                    ReplaceClipsInBlendTree(childBT, controllerDir, controllerName, clipCache);
            }
            if (modified)
            {
                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }
        }

        static AnimationClip CopyClipToControllerDir(AnimationClip sourceClip, string controllerDir,
            string controllerName, Dictionary<string, AnimationClip> clipCache)
        {
            if (IsEmptyClip(sourceClip)) return GetOrCreateBufferClip(controllerDir, clipCache);
            string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
            if (string.IsNullOrEmpty(sourcePath)) return null;

            if (clipCache.TryGetValue(sourcePath, out var cached)) return cached;

            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{controllerDir}/{controllerName}.{sourceClip.name}.anim");

            if (!AssetDatabase.CopyAsset(sourcePath, newPath)) return null;
            var localClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(newPath);
            clipCache[sourcePath] = localClip;
            return localClip;
        }

        static void SyncClipAAPParams(AnimatorController targetController,
            AnimatorController templateController, AnimatorControllerLayer[] newLayers)
        {
            var existingParamNames = new HashSet<string>(targetController.parameters.Select(parameter => parameter.name));
            var templateParamMap = templateController.parameters.ToDictionary(parameter => parameter.name, parameter => parameter);

            foreach (var layer in newLayers)
                SyncClipAAPParamsInSM(layer.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void SyncClipAAPParamsInSM(AnimatorStateMachine sm, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.motion is AnimationClip clip)
                    AddMissingClipAAPParams(clip, targetController, templateParamMap, existingParamNames);
                else if (childState.state.motion is BlendTree blendTree)
                    SyncClipAAPParamsInBlendTree(blendTree, targetController, templateParamMap, existingParamNames);
            }
            foreach (var childStateMachine in sm.stateMachines)
                SyncClipAAPParamsInSM(childStateMachine.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void SyncClipAAPParamsInBlendTree(BlendTree blendTree, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            foreach (var childMotion in blendTree.children)
            {
                if (childMotion.motion is AnimationClip clip)
                    AddMissingClipAAPParams(clip, targetController, templateParamMap, existingParamNames);
                else if (childMotion.motion is BlendTree childBT)
                    SyncClipAAPParamsInBlendTree(childBT, targetController, templateParamMap, existingParamNames);
            }
        }

        static void AddMissingClipAAPParams(AnimationClip clip, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            bool recorded = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Animator)) continue;
                string paramName = binding.propertyName;
                if (existingParamNames.Contains(paramName)) continue;

                var paramType = templateParamMap.TryGetValue(paramName, out var templateParam)
                    ? templateParam.type
                    : AnimatorControllerParameterType.Float;

                if (!recorded) { Undo.RecordObject(targetController, "Add Template AAP"); recorded = true; }
                targetController.AddParameter(paramName, paramType);
                existingParamNames.Add(paramName);
            }
        }

        static void SyncBehaviourParams(AnimatorController targetController,
            AnimatorController templateController, AnimatorControllerLayer[] newLayers)
        {
            var existingParamNames = new HashSet<string>(targetController.parameters.Select(parameter => parameter.name));
            var templateParamMap = templateController.parameters.ToDictionary(parameter => parameter.name, parameter => parameter);

            foreach (var layer in newLayers)
                SyncBehaviourParamsInSM(layer.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void SyncBehaviourParamsInSM(AnimatorStateMachine sm, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            AddMissingBehaviourParams(sm.behaviours, targetController, templateParamMap, existingParamNames);
            foreach (var childState in sm.states)
                AddMissingBehaviourParams(childState.state.behaviours, targetController, templateParamMap, existingParamNames);
            foreach (var childStateMachine in sm.stateMachines)
                SyncBehaviourParamsInSM(childStateMachine.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void AddMissingBehaviourParams(StateMachineBehaviour[] behaviours, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
#if VRC_SDK_VRCSDK3
            var names = new HashSet<string>();
            AnimatorParameterOps.CollectBehaviourNames(behaviours, names);

            bool recorded = false;
            foreach (var paramName in names)
            {
                if (existingParamNames.Contains(paramName)) continue;

                var paramType = templateParamMap.TryGetValue(paramName, out var templateParam)
                    ? templateParam.type
                    : AnimatorControllerParameterType.Float;

                if (!recorded) { Undo.RecordObject(targetController, "Add Template Behaviour Parameters"); recorded = true; }
                targetController.AddParameter(paramName, paramType);
                existingParamNames.Add(paramName);
            }
#endif
        }

        static void UpdateParamRefsInSM(AnimatorStateMachine sm, string oldName, string newName)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                UpdateTransitionConditions(anyStateTransition, oldName, newName);

            foreach (var childState in sm.states)
            {
                var state = childState.state;

                foreach (var transition in state.transitions)
                    UpdateTransitionConditions(transition, oldName, newName);

                bool speedNeedsUpdate       = state.speedParameter       == oldName;
                bool timeNeedsUpdate        = state.timeParameter        == oldName;
                bool mirrorNeedsUpdate      = state.mirrorParameter      == oldName;
                bool cycleOffsetNeedsUpdate = state.cycleOffsetParameter == oldName;

                if (speedNeedsUpdate || timeNeedsUpdate || mirrorNeedsUpdate || cycleOffsetNeedsUpdate)
                {
                    Undo.RecordObject(state, "Rename Template Parameter");
                    if (speedNeedsUpdate)       state.speedParameter       = newName;
                    if (timeNeedsUpdate)        state.timeParameter        = newName;
                    if (mirrorNeedsUpdate)      state.mirrorParameter      = newName;
                    if (cycleOffsetNeedsUpdate) state.cycleOffsetParameter = newName;
                }

                if (state.motion is BlendTree blendTree)
                    UpdateBlendTreeParams(blendTree, oldName, newName);
                else if (state.motion is AnimationClip stateClip)
                    UpdateClipAAPBinding(stateClip, oldName, newName);

                UpdateBehaviourParamRefs(state.behaviours, oldName, newName);
            }

            foreach (var childStateMachine in sm.stateMachines)
                UpdateParamRefsInSM(childStateMachine.stateMachine, oldName, newName);
        }

        static void UpdateBehaviourParamRefs(StateMachineBehaviour[] behaviours, string oldName, string newName)
        {
#if VRC_SDK_VRCSDK3
            AnimatorParameterOps.RemapBehaviours(behaviours, oldName, newName);
#endif
        }

        static void UpdateTransitionConditions(AnimatorStateTransition transition, string oldName, string newName)
        {
            var conditions = transition.conditions;
            bool modified = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != oldName) continue;
                conditions[i] = new AnimatorCondition
                {
                    mode      = conditions[i].mode,
                    parameter = newName,
                    threshold = conditions[i].threshold
                };
                modified = true;
            }
            if (!modified) return;
            Undo.RecordObject(transition, "Rename Template Parameter");
            transition.conditions = conditions;
        }

        static void UpdateBlendTreeParams(BlendTree blendTree, string oldName, string newName)
        {
            if (blendTree.blendParameter == oldName || blendTree.blendParameterY == oldName)
            {
                Undo.RecordObject(blendTree, "Rename Template Parameter");
                if (blendTree.blendParameter  == oldName) blendTree.blendParameter  = newName;
                if (blendTree.blendParameterY == oldName) blendTree.blendParameterY = newName;
            }

            if (blendTree.children.Any(childMotion => childMotion.directBlendParameter == oldName))
            {
                var serializedBT = new SerializedObject(blendTree);
                serializedBT.Update();
                var childrenProperty = serializedBT.FindProperty("m_Childs");
                if (childrenProperty != null)
                {
                    bool modified = false;
                    for (int i = 0; i < childrenProperty.arraySize; i++)
                    {
                        var directParamProperty = childrenProperty.GetArrayElementAtIndex(i)
                            .FindPropertyRelative("m_DirectBlendParameter");
                        if (directParamProperty != null && directParamProperty.stringValue == oldName)
                        {
                            directParamProperty.stringValue = newName;
                            modified = true;
                        }
                    }
                    if (modified) serializedBT.ApplyModifiedProperties();
                }
            }

            foreach (var childMotion in blendTree.children)
            {
                if (childMotion.motion is BlendTree childBT)
                    UpdateBlendTreeParams(childBT, oldName, newName);
                else if (childMotion.motion is AnimationClip childClip)
                    UpdateClipAAPBinding(childClip, oldName, newName);
            }
        }

        static void UpdateClipAAPBinding(AnimationClip clip, string oldName, string newName)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(Animator) || binding.propertyName != oldName) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Undo.RecordObject(clip, "Rename Template AAP");
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = new EditorCurveBinding
                {
                    type = typeof(Animator),
                    path = binding.path,
                    propertyName = newName
                };
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                EditorUtility.SetDirty(clip);
            }
        }
    }

    internal class TemplateControllerCachePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in importedAssets.Concat(deletedAssets).Concat(movedAssets))
            {
                string normalizedPath = asset.Replace('\\', '/');
                if (normalizedPath.StartsWith(PatchLayerToolbar.UserLayerTemplatesPath) ||
                    normalizedPath.StartsWith(PatchLayerToolbar.UserBlendTreeTemplatesPath) ||
                    normalizedPath.StartsWith("Packages/com.ygdr.animator/Templates"))
                {
                    PatchLayerToolbar._templateCache = null;
                    PatchLayerToolbar._blendTreeTemplateCache = null;
                    return;
                }
            }
        }
    }
}
#endif
