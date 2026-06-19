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
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [System.Flags]
    internal enum ObjectBindingType
    {
        None          = 0,
        GameObject    = 1,
        Renderer      = 2,
        ParticleSystem = 4,
        AudioSource   = 8,
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
            List<(string relativePath, string objectName, ObjectBindingType bindingType)> objectEntries)
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
            List<(string relativePath, string objectName, ObjectBindingType bindingType)> objectEntries,
            float activeValue)
        {
            var clip  = new AnimationClip { name = clipName };
            var curve = AnimationCurve.Constant(0f, 0f, activeValue);
            foreach (var (relativePath, _, bindingType) in objectEntries)
                foreach (var (flag, componentType, propertyName) in s_bindingDefs)
                    if ((bindingType & flag) != 0)
                        AnimationUtility.SetEditorCurve(clip,
                            new EditorCurveBinding { path = relativePath, type = componentType, propertyName = propertyName },
                            curve);
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
            internal ObjectBindingType bindingType;
        }

        AnimatorController _controller;
        GameObject         _avatarRoot;
        List<ObjectEntry>  _objectEntries         = new();
        string             _parameterName         = "Toggle";
        string             _layerName             = "Toggle";
        bool               _layerNameManuallyEdited;
        bool               _writeDefaults         = false;
        Vector2            _scrollPosition;


        // ── Styles ───────────────────────────────────────────────────────────────

        static GUIStyle s_titleStyle;
        static GUIStyle TitleStyle => s_titleStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 11,
            padding   = new RectOffset(4, 4, 0, 0),
            alignment = TextAnchor.MiddleLeft
        };

        static GUIStyle s_objectNameStyle;
        static GUIStyle ObjectNameStyle => s_objectNameStyle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize  = 11,
            padding   = new RectOffset(4, 4, 0, 0),
            alignment = TextAnchor.MiddleLeft
        };

        static GUIStyle s_emptyHintStyle;
        static GUIStyle EmptyHintStyle
        {
            get
            {
                if (s_emptyHintStyle != null) return s_emptyHintStyle;
                s_emptyHintStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter
                };
                s_emptyHintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                return s_emptyHintStyle;
            }
        }

        static GUIStyle s_miniLabelStyle;
        static GUIStyle MiniLabelStyle => s_miniLabelStyle ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 10,
            normal    = { textColor = Color.white }
        };

        static GUIStyle s_confirmLabelStyle;
        static GUIStyle ConfirmLabelStyle => s_confirmLabelStyle ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize  = 11,
            normal    = { textColor = Color.white }
        };

        static Color GetHoverColor()
        {
            var accent = AnimationEditorWindow.Styles.AccentColor;
            return new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        void OnEnable() => wantsMouseMove = true;

        internal static void Open(AnimatorController controller)
        {
            var window = GetWindow<AnimatorGameObjectToggleWindow>();
            window.titleContent = new GUIContent(L10n.Get("toggle.title"));
            window.minSize                   = new Vector2(420f, 360f);
            window._controller              = controller;
            window._avatarRoot              = ResolveAvatarRoot(controller);
            window._objectEntries.Clear();
            window._parameterName           = "Toggle";
            window._layerName               = "Toggle";
            window._layerNameManuallyEdited  = false;
            window.wantsMouseMove           = true;
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

        // ── OnGUI ────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove) Repaint();
            DrawHeader();

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            var outerRect = EditorGUILayout.BeginVertical(AnimationEditorWindow.Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, AnimationEditorWindow.Styles.PrimaryColor);

            DrawForm();
            DrawObjectsSection();
            DrawFooter();

            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);

            HandleObjectDrop();
        }

        void DrawHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(
                    new Rect(0f, headerRect.y, EditorGUIUtility.currentViewWidth, headerRect.height),
                    AnimationEditorWindow.Styles.SectionHeaderBg);
            GUI.Label(
                new Rect(headerRect.x + 4f, headerRect.y, headerRect.width, headerRect.height),
                $"{L10n.Get("toggle.header")}  ·  {_objectEntries.Count} {(_objectEntries.Count == 1 ? L10n.Get("toggle.object") : L10n.Get("toggle.objects"))}",
                TitleStyle);
        }

        void DrawForm()
        {
            EditorGUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            _parameterName = EditorGUILayout.TextField(L10n.Get("toggle.form.parameter"), _parameterName);
            if (EditorGUI.EndChangeCheck() && !_layerNameManuallyEdited)
                _layerName = _parameterName;

            EditorGUI.BeginChangeCheck();
            _layerName = EditorGUILayout.TextField(L10n.Get("toggle.form.layer_name"), _layerName);
            if (EditorGUI.EndChangeCheck())
                _layerNameManuallyEdited = true;

            _writeDefaults = EditorGUILayout.Toggle(L10n.Get("states.write_defaults"), _writeDefaults);

            EditorGUILayout.Space(6f);
        }

        void DrawObjectsSection()
        {
            bool dragHovering = Event.current.type == EventType.DragUpdated
                && DragAndDrop.objectReferences.OfType<GameObject>().Any(IsValidDropTarget);

            // Object rows inside secondary background
            var listRect = EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint && listRect.height > 0)
            {
                EditorGUI.DrawRect(listRect, AnimationEditorWindow.Styles.SecondaryColor);
                if (dragHovering)
                    DrawDropHighlight(listRect);
            }

            const float rowHeight = 22f;

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_objectEntries.Count == 0)
            {
                var emptyRect = EditorGUILayout.GetControlRect(false, 40f);
                GUI.Label(emptyRect, L10n.Get("toggle.empty_hint"), EmptyHintStyle);
            }
            else
            {
                var   allRowsRect  = EditorGUILayout.GetControlRect(false, _objectEntries.Count * rowHeight);
                float rowY         = allRowsRect.y;
                float rowWidth     = allRowsRect.width + 3.5f; // extend into SectionPadded right padding
                int   removeIndex  = -1;

                for (int i = 0; i < _objectEntries.Count; i++, rowY += rowHeight)
                {
                    var rowRect = new Rect(allRowsRect.x, rowY, rowWidth, rowHeight);
                    if (Event.current.type == EventType.Repaint && i % 2 == 1)
                        EditorGUI.DrawRect(
                            new Rect(0f, rowY, rowRect.xMax, rowHeight),
                            AnimationEditorWindow.Styles.RowAltColor);
                    if (DrawObjectRow(i, rowRect))
                        removeIndex = i;
                }

                if (removeIndex >= 0)
                    _objectEntries.RemoveAt(removeIndex);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // Returns true if the remove button was clicked.
        bool DrawObjectRow(int index, Rect rowRect)
        {
            var entry = _objectEntries[index];

            const float removeWidth   = 22f;
            const float objectBtnW    = 56f;
            const float rendererBtnW  = 66f;
            const float particleBtnW  = 56f;
            const float audioBtnW     = 50f;
            const float nameGap       = 2f;
            const float pad           = 4f;

            float btnsWidth = objectBtnW
                + (entry.hasRenderer       ? rendererBtnW : 0f)
                + (entry.hasParticleSystem ? particleBtnW : 0f)
                + (entry.hasAudioSource    ? audioBtnW    : 0f);

            float nameWidth  = rowRect.width - removeWidth - btnsWidth - nameGap - pad;
            var   nameRect   = new Rect(rowRect.x + pad,            rowRect.y, nameWidth,   rowRect.height);
            var   removRect  = new Rect(rowRect.xMax - removeWidth, rowRect.y, removeWidth, rowRect.height);

            var   objBtnRect = new Rect(nameRect.xMax + nameGap, rowRect.y, objectBtnW, rowRect.height);
            float nextX      = objBtnRect.xMax;

            var renBtnRect = entry.hasRenderer       ? new Rect(nextX, rowRect.y, rendererBtnW, rowRect.height) : default;
            if (entry.hasRenderer) nextX = renBtnRect.xMax;

            var parBtnRect = entry.hasParticleSystem ? new Rect(nextX, rowRect.y, particleBtnW, rowRect.height) : default;
            if (entry.hasParticleSystem) nextX = parBtnRect.xMax;

            var audBtnRect = entry.hasAudioSource    ? new Rect(nextX, rowRect.y, audioBtnW, rowRect.height)    : default;

            GUI.Label(nameRect, entry.gameObject.name, ObjectNameStyle);

            var  mousePos    = Event.current.mousePosition;
            var  accent      = AnimationEditorWindow.Styles.AccentColor;
            var  accentHover = GetHoverColor();
            var  bindingType = entry.bindingType;

            if (Event.current.type == EventType.Repaint)
            {
                DrawModeBtn(objBtnRect, L10n.Get("toggle.bind.object"),   bindingType, ObjectBindingType.GameObject,    mousePos, accent, accentHover);
                if (entry.hasRenderer)       DrawModeBtn(renBtnRect, L10n.Get("toggle.bind.renderer"), bindingType, ObjectBindingType.Renderer,       mousePos, accent, accentHover);
                if (entry.hasParticleSystem) DrawModeBtn(parBtnRect, L10n.Get("toggle.bind.particle"), bindingType, ObjectBindingType.ParticleSystem,  mousePos, accent, accentHover);
                if (entry.hasAudioSource)    DrawModeBtn(audBtnRect, L10n.Get("toggle.bind.audio"),    bindingType, ObjectBindingType.AudioSource,     mousePos, accent, accentHover);

                EditorGUI.DrawRect(removRect, removRect.Contains(mousePos) ? accentHover : accent);
                GUI.Label(removRect, "−", MiniLabelStyle);
            }

            if (GUI.Button(objBtnRect, GUIContent.none, GUIStyle.none))
                ToggleBindingFlag(index, ObjectBindingType.GameObject);
            EditorGUIUtility.AddCursorRect(objBtnRect, MouseCursor.Link);

            if (entry.hasRenderer)
            {
                if (GUI.Button(renBtnRect, GUIContent.none, GUIStyle.none))
                    ToggleBindingFlag(index, ObjectBindingType.Renderer);
                EditorGUIUtility.AddCursorRect(renBtnRect, MouseCursor.Link);
            }

            if (entry.hasParticleSystem)
            {
                if (GUI.Button(parBtnRect, GUIContent.none, GUIStyle.none))
                    ToggleBindingFlag(index, ObjectBindingType.ParticleSystem);
                EditorGUIUtility.AddCursorRect(parBtnRect, MouseCursor.Link);
            }

            if (entry.hasAudioSource)
            {
                if (GUI.Button(audBtnRect, GUIContent.none, GUIStyle.none))
                    ToggleBindingFlag(index, ObjectBindingType.AudioSource);
                EditorGUIUtility.AddCursorRect(audBtnRect, MouseCursor.Link);
            }

            bool remove = GUI.Button(removRect, GUIContent.none, GUIStyle.none);
            EditorGUIUtility.AddCursorRect(removRect, MouseCursor.Link);

            return remove;
        }

        void ToggleBindingFlag(int index, ObjectBindingType flag)
        {
            var entry = _objectEntries[index];
            var newType = entry.bindingType ^ flag;
            entry.bindingType = newType == ObjectBindingType.None ? flag : newType;
            _objectEntries[index] = entry;
        }

        static void DrawModeBtn(Rect rect, string label, ObjectBindingType current, ObjectBindingType flag, Vector2 mousePos, Color accent, Color accentHover)
        {
            bool selected = (current & flag) != 0;
            EditorGUI.DrawRect(rect, selected || rect.Contains(mousePos) ? accentHover : accent);
            GUI.Label(rect, label, MiniLabelStyle);
        }

        static void DrawDropHighlight(Rect rect)
        {
            var borderColor = new Color(0.4f, 0.7f, 1f, 0.6f);
            const float thickness = 1.5f;
            EditorGUI.DrawRect(new Rect(rect.x,              rect.y,              rect.width,  thickness),   borderColor);
            EditorGUI.DrawRect(new Rect(rect.x,              rect.yMax - thickness, rect.width, thickness),  borderColor);
            EditorGUI.DrawRect(new Rect(rect.x,              rect.y,              thickness,   rect.height), borderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y,            thickness,   rect.height), borderColor);
        }

        void HandleObjectDrop()
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform) return;

            var validGameObjects = DragAndDrop.objectReferences.OfType<GameObject>()
                .Where(go => go != null && IsValidDropTarget(go))
                .ToArray();

            if (currentEvent.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = validGameObjects.Length > 0
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                currentEvent.Use();
                Repaint();
                return;
            }

            if (validGameObjects.Length == 0) return;

            DragAndDrop.AcceptDrag();
            currentEvent.Use();

            foreach (var gameObject in validGameObjects)
            {
                if (_objectEntries.Any(entry => entry.gameObject == gameObject)) continue;

                var relativePath = _avatarRoot != null
                    ? AnimatorGameObjectToggleOps.GetRelativePath(_avatarRoot.transform, gameObject.transform) ?? gameObject.name
                    : AnimatorGameObjectToggleOps.GetRelativePath(AnimatorGameObjectToggleOps.FindAvatarRoot(gameObject, _controller), gameObject.transform) ?? gameObject.name;

                _objectEntries.Add(new ObjectEntry
                {
                    gameObject        = gameObject,
                    relativePath      = relativePath,
                    hasRenderer       = gameObject.GetComponent<Renderer>()       != null,
                    hasParticleSystem = gameObject.GetComponent<ParticleSystem>() != null,
                    hasAudioSource    = gameObject.GetComponent<AudioSource>()    != null,
                    bindingType       = ObjectBindingType.GameObject,
                });
            }

            Repaint();
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(6f);

            bool canCreate = !string.IsNullOrWhiteSpace(_parameterName)
                && !string.IsNullOrWhiteSpace(_layerName)
                && _objectEntries.Count > 0;

            var btnRect = EditorGUILayout.GetControlRect(false, 28f);
            using (new EditorGUI.DisabledGroupScope(!canCreate))
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(btnRect, btnRect.Contains(Event.current.mousePosition) && canCreate
                        ? GetHoverColor() : AnimationEditorWindow.Styles.AccentColor);
                    GUI.Label(btnRect, L10n.Get("toggle.create"), ConfirmLabelStyle);
                }
                if (GUI.Button(btnRect, GUIContent.none, GUIStyle.none) && canCreate)
                    ExecuteCreate();
            }
            EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
            EditorGUILayout.Space(4f);
        }

        void ExecuteCreate()
        {
            AnimatorGameObjectToggleOps.CreateToggleSetup(
                _controller,
                _layerName,
                _parameterName,
                _writeDefaults,
                _objectEntries.Select(entry => (entry.relativePath, entry.gameObject.name, entry.bindingType)).ToList());
            Close();
        }
    }
}
#endif
