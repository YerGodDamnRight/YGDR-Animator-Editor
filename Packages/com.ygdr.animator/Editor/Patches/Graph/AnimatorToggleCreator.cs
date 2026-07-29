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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace YGDR.Editor.Animation
{
    [System.Flags]
    internal enum ObjectBindingType
    {
        None           = 0,
        GameObject     = 1,
        Renderer       = 2,
        ParticleSystem = 4,
        AudioSource    = 8,
        Light          = 16,
        VRCPhysBone    = 32,
    }

    internal struct BlendshapeEntry
    {
        internal string shapeName;
        internal float  offValue;
        internal float  onValue;
    }

    // ─── Operations ──────────────────────────────────────────────────────────────
    internal static class AnimatorGameObjectToggleOps
    {
        static readonly (ObjectBindingType flag, System.Type componentType, string propertyName)[] s_bindingDefs =
        {
            (ObjectBindingType.GameObject,     typeof(GameObject),     "m_IsActive"),
            (ObjectBindingType.Renderer,       typeof(Renderer),       "m_Enabled"),
            (ObjectBindingType.ParticleSystem, typeof(ParticleSystem), "m_Enabled"),
            (ObjectBindingType.AudioSource,    typeof(AudioSource),    "m_Enabled"),
            (ObjectBindingType.Light,          typeof(Light),          "m_Enabled"),
#if VRC_SDK_VRCSDK3
            (ObjectBindingType.VRCPhysBone,    typeof(VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone), "m_Enabled"),
#endif
        };

        // Walks up the hierarchy to find an Animator whose controller matches.
        // Falls back to the topmost scene parent if no match is found.
        internal static Transform FindAvatarRoot(GameObject target, AnimatorController controller)
        {
            var current = target.transform;
            while (current != null)
            {
                var animator = current.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController as AnimatorController == controller)
                    return current;
                current = current.parent;
            }
            current = target.transform;
            while (current.parent != null)
                current = current.parent;
            return current;
        }

        internal static string GetRelativePath(Transform avatarRoot, Transform target)
        {
            if (target == avatarRoot) return "";
            var pathParts = new List<string>();
            var current   = target;
            while (current != null && current != avatarRoot)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }
            return current == avatarRoot ? string.Join("/", pathParts) : null;
        }

        internal static void CreateToggleSetup(
            AnimatorController controller,
            string layerName,
            string parameterName,
            bool writeDefaults,
            List<(string relativePath, string objectName, ObjectBindingType bindingType, List<BlendshapeEntry> blendshapeEntries)> objectEntries)
        {
            Undo.RegisterCompleteObjectUndo(controller, "Create Toggle Layer");

            var controllerDirectory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller))?.Replace("\\", "/") ?? "Assets";

            if (!controller.parameters.Any(p => p.name == parameterName))
                controller.AddParameter(parameterName, AnimatorControllerParameterType.Bool);

            var offClip = CreateActiveClip(controllerDirectory, $"{layerName}_Off", objectEntries, 0f);
            var onClip  = CreateActiveClip(controllerDirectory, $"{layerName}_On",  objectEntries, 1f);

            controller.AddLayer(layerName);
            var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1f;
            controller.layers = layers;

            var stateMachine = controller.layers[layers.Length - 1].stateMachine;
            Undo.RegisterCreatedObjectUndo(stateMachine, "Create Toggle Layer");

            const float nodeHeight      = 40f;
            const float specialSpacing  = nodeHeight;
            const float stateSpacing    = nodeHeight + 80f;
            const float nodeX           = 0f;

            float y = 0f;
            stateMachine.exitPosition     = new Vector3(nodeX, y, 0f);
            y += specialSpacing;
            stateMachine.anyStatePosition = new Vector3(nodeX, y, 0f);
            y += specialSpacing;
            stateMachine.entryPosition    = new Vector3(nodeX, y, 0f);
            y += specialSpacing;
            var offPosition = new Vector3(nodeX - 20f, y, 0f);
            y += stateSpacing;
            var onPosition  = new Vector3(nodeX - 20f, y, 0f);

            var offState = stateMachine.AddState($"{layerName}_Off", offPosition);
            Undo.RegisterCreatedObjectUndo(offState, "Create Toggle Layer");
            offState.motion             = offClip;
            offState.writeDefaultValues = writeDefaults;
            stateMachine.defaultState   = offState;

            var onState = stateMachine.AddState($"{layerName}_On", onPosition);
            Undo.RegisterCreatedObjectUndo(onState, "Create Toggle Layer");
            onState.motion             = onClip;
            onState.writeDefaultValues = writeDefaults;

            var toOnTransition  = offState.AddTransition(onState);
            var toOffTransition = onState.AddTransition(offState);

            ConfigureInstantTransition(toOnTransition);
            ConfigureInstantTransition(toOffTransition);
            toOnTransition.AddCondition(AnimatorConditionMode.If,    0f, parameterName);
            toOffTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static AnimationClip CreateActiveClip(
            string directory,
            string clipName,
            List<(string relativePath, string objectName, ObjectBindingType bindingType, List<BlendshapeEntry> blendshapeEntries)> objectEntries,
            float activeValue)
        {
            var clip      = new AnimationClip { name = clipName };
            var curve     = AnimationCurve.Constant(0f, 0f, activeValue);
            bool isOnClip = activeValue > 0.5f;
            foreach (var (relativePath, _, bindingType, blendshapeEntries) in objectEntries)
            {
                foreach (var (flag, componentType, propertyName) in s_bindingDefs)
                    if ((bindingType & flag) != 0)
                        AnimationUtility.SetEditorCurve(clip,
                            new EditorCurveBinding { path = relativePath, type = componentType, propertyName = propertyName },
                            curve);
                foreach (var blendshapeEntry in blendshapeEntries)
                    AnimationUtility.SetEditorCurve(clip,
                        new EditorCurveBinding
                        {
                            path         = relativePath,
                            type         = typeof(SkinnedMeshRenderer),
                            propertyName = $"blendShape.{blendshapeEntry.shapeName}"
                        },
                        AnimationCurve.Constant(0f, 0f, isOnClip ? blendshapeEntry.onValue : blendshapeEntry.offValue));
            }
            var uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{clipName}.anim");
            AssetDatabase.CreateAsset(clip, uniqueAssetPath);
            return clip;
        }

        static void ConfigureInstantTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration    = 0f;
            transition.offset      = 0f;
        }
    }

    // ─── Window ──────────────────────────────────────────────────────────────────
    internal class AnimatorGameObjectToggleWindow : EditorWindow
    {
        struct ObjectEntry
        {
            internal GameObject        gameObject;
            internal string            relativePath;
            internal bool              hasRenderer;
            internal bool              hasParticleSystem;
            internal bool              hasAudioSource;
            internal bool              hasLight;
            internal bool              hasVRCPhysBone;
            internal ObjectBindingType bindingType;
            internal bool                  hasSkinnedMeshRenderer;
            internal SkinnedMeshRenderer   skinnedMeshRenderer;
            internal bool                  blendshapeExpanded;
            internal List<BlendshapeEntry> blendshapeEntries;
        }

        AnimatorController _controller;
        GameObject         _avatarRoot;
        List<ObjectEntry>  _objectEntries = new();
        string             _parameterName = "Toggle";
        string             _layerName     = "Toggle";
        bool               _layerNameManuallyEdited;
        bool               _writeDefaults = false;

        Label       _headerLabel;
        VisualElement _panel;
        TextField   _parameterField;
        TextField   _layerNameField;
        VisualElement _objectsSection;
        ScrollView  _rowsScroll;
        Button      _createButton;

        static Color GetHoverColor()
        {
            var accent = SharedWindowStyles.AccentColor;
            return new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        void OnDisable() => SharedWindowStyles.UnregisterPaletteRefresh(RefreshPaletteColors);

        internal static void Open(AnimatorController controller)
        {
            var window = GetWindow<AnimatorGameObjectToggleWindow>();
            window.titleContent = new GUIContent(L10n.Get("toggle.title"));
            window.minSize                  = new Vector2(420f, 360f);
            window._controller              = controller;
            window._avatarRoot              = ResolveAvatarRoot(controller);
            window._objectEntries.Clear();
            window._parameterName           = "Toggle";
            window._layerName               = "Toggle";
            window._layerNameManuallyEdited = false;
            window.RefreshAll();
            window.Show();
        }

        static GameObject ResolveAvatarRoot(AnimatorController controller)
        {
#if VRC_SDK_VRCSDK3
            var vrcRoot = VRCSyncCache.GetSearchRoot();
            if (vrcRoot != null) return vrcRoot;
#endif
            foreach (var animator in Object.FindObjectsOfType<Animator>())
            {
                if (animator.runtimeAnimatorController as AnimatorController != controller) continue;
                var current = animator.transform;
                while (current.parent != null) current = current.parent;
                return current.gameObject;
            }
            return null;
        }

        bool IsValidDropTarget(GameObject go) =>
            _avatarRoot == null || go.transform.IsChildOf(_avatarRoot.transform);

        // ── Shell ────────────────────────────────────────────────────────────────

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.EnableInClassList("ygdr-dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("ygdr-light", !EditorGUIUtility.isProSkin);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ygdr.animator/Editor/UI/SharedWindowStyles.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            _headerLabel = new Label();
            _headerLabel.AddToClassList("ygdr-toggle-header");
            root.Add(_headerLabel);

            _panel = new VisualElement();
            _panel.AddToClassList("ygdr-toggle-panel");
            root.Add(_panel);

            BuildForm();
            BuildObjectsSection();
            BuildFooter();

            RegisterObjectDrop();

            SharedWindowStyles.RegisterPaletteRefresh(RefreshPaletteColors);
            RefreshAll();
        }

        void BuildForm()
        {
            var form = new VisualElement();
            form.AddToClassList("ygdr-toggle-form");
            _panel.Add(form);

            _parameterField = new TextField(L10n.Get("toggle.form.parameter"));
            _parameterField.RegisterValueChangedCallback(evt =>
            {
                _parameterName = evt.newValue;
                if (!_layerNameManuallyEdited)
                {
                    _layerName = _parameterName;
                    _layerNameField.SetValueWithoutNotify(_layerName);
                }
                RefreshFooterState();
            });
            form.Add(_parameterField);

            _layerNameField = new TextField(L10n.Get("toggle.form.layer_name"));
            _layerNameField.RegisterValueChangedCallback(evt =>
            {
                _layerName = evt.newValue;
                _layerNameManuallyEdited = true;
                RefreshFooterState();
            });
            form.Add(_layerNameField);

            var writeDefaultsToggle = new Toggle(L10n.Get("states.write_defaults"));
            writeDefaultsToggle.RegisterValueChangedCallback(evt => _writeDefaults = evt.newValue);
            form.Add(writeDefaultsToggle);
        }

        void BuildObjectsSection()
        {
            _objectsSection = new VisualElement();
            _objectsSection.AddToClassList("ygdr-toggle-objects-section");
            _panel.Add(_objectsSection);

            _rowsScroll = new ScrollView(ScrollViewMode.Vertical);
            _rowsScroll.AddToClassList("ygdr-toggle-rows-scroll");
            _objectsSection.Add(_rowsScroll);
        }

        void BuildFooter()
        {
            _createButton = new Button(ExecuteCreate) { text = L10n.Get("toggle.create") };
            _createButton.AddToClassList("ygdr-toggle-footer-btn");
            StyleToggleButton(_createButton, false);
            _panel.Add(_createButton);
        }

        void RegisterObjectDrop()
        {
            _objectsSection.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                bool valid = DragAndDrop.objectReferences.OfType<GameObject>().Any(IsValidDropTarget);
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                _objectsSection.EnableInClassList("ygdr-toggle-drop-highlight", valid);
            });
            _objectsSection.RegisterCallback<DragLeaveEvent>(_ =>
                _objectsSection.RemoveFromClassList("ygdr-toggle-drop-highlight"));
            _objectsSection.RegisterCallback<DragPerformEvent>(_ =>
            {
                _objectsSection.RemoveFromClassList("ygdr-toggle-drop-highlight");
                var validGameObjects = DragAndDrop.objectReferences.OfType<GameObject>()
                    .Where(go => go != null && IsValidDropTarget(go))
                    .ToArray();
                if (validGameObjects.Length == 0) return;
                DragAndDrop.AcceptDrag();
                AddDroppedObjects(validGameObjects);
            });
        }

        void AddDroppedObjects(GameObject[] gameObjects)
        {
            foreach (var gameObject in gameObjects)
            {
                if (_objectEntries.Any(entry => entry.gameObject == gameObject)) continue;

                var relativePath = _avatarRoot != null
                    ? AnimatorGameObjectToggleOps.GetRelativePath(_avatarRoot.transform, gameObject.transform) ?? gameObject.name
                    : AnimatorGameObjectToggleOps.GetRelativePath(AnimatorGameObjectToggleOps.FindAvatarRoot(gameObject, _controller), gameObject.transform) ?? gameObject.name;

                var smrComponent    = gameObject.GetComponent<SkinnedMeshRenderer>();
                bool hasBlendshapes = smrComponent != null && smrComponent.sharedMesh != null && smrComponent.sharedMesh.blendShapeCount > 0;

                _objectEntries.Add(new ObjectEntry
                {
                    gameObject             = gameObject,
                    relativePath           = relativePath,
                    hasRenderer            = gameObject.GetComponent<Renderer>()             != null,
                    hasParticleSystem      = gameObject.GetComponent<ParticleSystem>()       != null,
                    hasAudioSource         = gameObject.GetComponent<AudioSource>()          != null,
                    hasLight               = gameObject.GetComponent<Light>()                != null,
#if VRC_SDK_VRCSDK3
                    hasVRCPhysBone         = gameObject.GetComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>() != null,
#endif
                    hasSkinnedMeshRenderer = hasBlendshapes,
                    skinnedMeshRenderer    = smrComponent,
                    bindingType            = ObjectBindingType.GameObject,
                    blendshapeExpanded     = false,
                    blendshapeEntries      = new List<BlendshapeEntry>(),
                });
            }

            RebuildRows();
        }

        // ── Refresh / rebuild ───────────────────────────────────────────────────

        void RefreshAll()
        {
            if (_headerLabel == null) return;
            _parameterField.SetValueWithoutNotify(_parameterName);
            _layerNameField.SetValueWithoutNotify(_layerName);
            RebuildRows();
        }

        void RefreshPaletteColors()
        {
            if (_headerLabel == null) return;
            _headerLabel.style.backgroundColor = SharedWindowStyles.SectionHeaderBg;
            _panel.style.backgroundColor        = SharedWindowStyles.PrimaryColor;
            _rowsScroll.style.backgroundColor   = SharedWindowStyles.SecondaryColor;
            _createButton.style.backgroundColor = SharedWindowStyles.AccentColor;
            RebuildRows();
        }

        void RefreshHeader()
        {
            _headerLabel.text = $"{L10n.Get("toggle.header")}  ·  {_objectEntries.Count} {(_objectEntries.Count == 1 ? L10n.Get("toggle.object") : L10n.Get("toggle.objects"))}";
        }

        void RefreshFooterState()
        {
            bool canCreate = !string.IsNullOrWhiteSpace(_parameterName)
                && !string.IsNullOrWhiteSpace(_layerName)
                && _objectEntries.Count > 0;
            _createButton.SetEnabled(canCreate);
        }

        void RebuildRows()
        {
            RefreshHeader();
            RefreshFooterState();

            _rowsScroll.Clear();

            if (_objectEntries.Count == 0)
            {
                var emptyHint = new Label(L10n.Get("toggle.empty_hint"));
                emptyHint.AddToClassList("ygdr-toggle-empty-hint");
                _rowsScroll.Add(emptyHint);
                return;
            }

            for (int i = 0; i < _objectEntries.Count; i++)
            {
                var capturedIndex = i;
                var row = SharedWindowStyles.MakeStripedRow("ygdr-toggle-row", i);
                BuildObjectRow(row, capturedIndex);
                _rowsScroll.Add(row);

                if (_objectEntries[i].blendshapeExpanded)
                    BuildBlendshapeSubRows(capturedIndex);
            }
        }

        void BuildObjectRow(VisualElement row, int index)
        {
            var entry = _objectEntries[index];

            var nameLabel = new Label(entry.gameObject.name);
            nameLabel.AddToClassList("ygdr-toggle-name-label");
            row.Add(nameLabel);

            AddModeButton(row, index, L10n.Get("toggle.bind.object"), ObjectBindingType.GameObject, "ygdr-toggle-btn-object");
            if (entry.hasRenderer)       AddModeButton(row, index, L10n.Get("toggle.bind.renderer"), ObjectBindingType.Renderer,       "ygdr-toggle-btn-renderer");
            if (entry.hasParticleSystem) AddModeButton(row, index, L10n.Get("toggle.bind.particle"), ObjectBindingType.ParticleSystem, "ygdr-toggle-btn-particle");
            if (entry.hasAudioSource)    AddModeButton(row, index, L10n.Get("toggle.bind.audio"),    ObjectBindingType.AudioSource,    "ygdr-toggle-btn-audio");
            if (entry.hasLight)          AddModeButton(row, index, L10n.Get("toggle.bind.light"),    ObjectBindingType.Light,          "ygdr-toggle-btn-light");
            if (entry.hasVRCPhysBone)    AddModeButton(row, index, L10n.Get("toggle.bind.physbone"), ObjectBindingType.VRCPhysBone,    "ygdr-toggle-btn-physbone");

            if (entry.hasSkinnedMeshRenderer)
            {
                var blendshapeButton = new Button(() =>
                {
                    var updatedEntry = _objectEntries[index];
                    updatedEntry.blendshapeExpanded = !updatedEntry.blendshapeExpanded;
                    _objectEntries[index] = updatedEntry;
                    RebuildRows();
                }) { text = "Blendshape" };
                blendshapeButton.AddToClassList("ygdr-toggle-mode-btn");
                blendshapeButton.AddToClassList("ygdr-toggle-btn-blendshape");
                StyleToggleButton(blendshapeButton, entry.blendshapeExpanded);
                row.Add(blendshapeButton);
            }

            var removeButton = new Button(() =>
            {
                _objectEntries.RemoveAt(index);
                RebuildRows();
            }) { text = "−" };
            removeButton.AddToClassList("ygdr-toggle-mode-btn");
            removeButton.AddToClassList("ygdr-toggle-btn-remove");
            StyleToggleButton(removeButton, false);
            row.Add(removeButton);
        }

        void AddModeButton(VisualElement row, int index, string label, ObjectBindingType flag, string widthClass)
        {
            bool selected = (_objectEntries[index].bindingType & flag) != 0;
            var button = new Button(() => ToggleBindingFlag(index, flag)) { text = label };
            button.AddToClassList("ygdr-toggle-mode-btn");
            button.AddToClassList(widthClass);
            StyleToggleButton(button, selected);
            row.Add(button);
        }

        static void StyleToggleButton(VisualElement button, bool selected)
        {
            var accent      = SharedWindowStyles.AccentColor;
            var accentHover = GetHoverColor();
            button.style.backgroundColor = selected ? accentHover : accent;
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = accentHover);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = selected ? accentHover : accent);
        }

        void ToggleBindingFlag(int index, ObjectBindingType flag)
        {
            var entry = _objectEntries[index];
            var newType = entry.bindingType ^ flag;
            entry.bindingType = newType == ObjectBindingType.None ? flag : newType;
            _objectEntries[index] = entry;
            RebuildRows();
        }

        void BuildBlendshapeSubRows(int entryIndex)
        {
            var entry = _objectEntries[entryIndex];
            var smr   = entry.skinnedMeshRenderer;

            var subRows = new VisualElement();
            subRows.AddToClassList("ygdr-toggle-blendshape-rows");
            _rowsScroll.Add(subRows);

            for (int k = 0; k < entry.blendshapeEntries.Count; k++)
            {
                var capturedShapeIndex = k;
                var blendshapeEntry = entry.blendshapeEntries[k];

                var shapeRow = new VisualElement();
                shapeRow.AddToClassList("ygdr-toggle-blendshape-row");
                subRows.Add(shapeRow);

                var nameLabel = new Label(blendshapeEntry.shapeName);
                nameLabel.AddToClassList("ygdr-toggle-blendshape-name");
                shapeRow.Add(nameLabel);

                var offLabel = new Label("Off");
                offLabel.AddToClassList("ygdr-toggle-blendshape-field-label");
                shapeRow.Add(offLabel);

                var offField = new FloatField { value = blendshapeEntry.offValue };
                offField.AddToClassList("ygdr-toggle-blendshape-field");
                offField.RegisterValueChangedCallback(evt =>
                {
                    var clamped = Mathf.Clamp(evt.newValue, 0f, 100f);
                    var updated = entry.blendshapeEntries[capturedShapeIndex];
                    updated.offValue = clamped;
                    entry.blendshapeEntries[capturedShapeIndex] = updated;
                    if (!Mathf.Approximately(clamped, evt.newValue)) offField.SetValueWithoutNotify(clamped);
                });
                shapeRow.Add(offField);

                var onLabel = new Label("On");
                onLabel.AddToClassList("ygdr-toggle-blendshape-field-label");
                shapeRow.Add(onLabel);

                var onField = new FloatField { value = blendshapeEntry.onValue };
                onField.AddToClassList("ygdr-toggle-blendshape-field");
                onField.RegisterValueChangedCallback(evt =>
                {
                    var clamped = Mathf.Clamp(evt.newValue, 0f, 100f);
                    var updated = entry.blendshapeEntries[capturedShapeIndex];
                    updated.onValue = clamped;
                    entry.blendshapeEntries[capturedShapeIndex] = updated;
                    if (!Mathf.Approximately(clamped, evt.newValue)) onField.SetValueWithoutNotify(clamped);
                });
                shapeRow.Add(onField);

                var removeButton = new Button(() =>
                {
                    entry.blendshapeEntries.RemoveAt(capturedShapeIndex);
                    RebuildRows();
                }) { text = "−" };
                removeButton.AddToClassList("ygdr-toggle-mode-btn");
                removeButton.AddToClassList("ygdr-toggle-btn-remove");
                StyleToggleButton(removeButton, false);
                shapeRow.Add(removeButton);
            }

            var addRow = new VisualElement();
            addRow.AddToClassList("ygdr-toggle-blendshape-add-row");
            subRows.Add(addRow);

            var addButton = new Button { text = "+" };
            addButton.AddToClassList("ygdr-toggle-blendshape-add-btn");
            StyleToggleButton(addButton, false);
            addButton.clicked += () =>
            {
                if (smr == null || smr.sharedMesh == null) return;
                var capturedEntries = entry.blendshapeEntries;
                var existingNames   = new HashSet<string>(capturedEntries.Select(b => b.shapeName));
                int shapeCount      = smr.sharedMesh.blendShapeCount;
                var available       = new List<string>();
                for (int k = 0; k < shapeCount; k++)
                {
                    var shapeName = smr.sharedMesh.GetBlendShapeName(k);
                    if (!existingNames.Contains(shapeName))
                        available.Add(shapeName);
                }
                new BlendshapeDropdown(available.ToArray(), shapeName =>
                {
                    capturedEntries.Add(new BlendshapeEntry { shapeName = shapeName, offValue = 0f, onValue = 100f });
                    RebuildRows();
                }).ShowCapped(addButton.worldBound);
            };
            addRow.Add(addButton);
        }

        void ExecuteCreate()
        {
            AnimatorGameObjectToggleOps.CreateToggleSetup(
                _controller,
                _layerName,
                _parameterName,
                _writeDefaults,
                _objectEntries.Select(entry => (entry.relativePath, entry.gameObject.name, entry.bindingType, entry.blendshapeEntries)).ToList());
            Close();
        }
    }
    internal class BlendshapeDropdown : YgdrAdvancedDropdownBase
    {
        readonly string[]              _shapeNames;
        readonly System.Action<string> _onSelected;

        internal BlendshapeDropdown(string[] shapeNames, System.Action<string> onSelected)
            : base(new Vector2(200f, 250f))
        {
            _shapeNames = shapeNames;
            _onSelected = onSelected;
        }

        internal void ShowCapped(Rect rect) => ShowCapped(rect, 350f, 200f);

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Blendshapes");
            foreach (var shapeName in _shapeNames)
                root.AddChild(new AdvancedDropdownItem(shapeName));
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
            => _onSelected?.Invoke(item.name);
    }
}
#endif
