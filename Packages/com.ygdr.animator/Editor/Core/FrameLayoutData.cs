#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [Serializable]
    public class FrameRect
    {
        public string title;
        public string comments;
        public AnimatorStateMachine layerStateMachine;
        public AnimatorStateMachine activeSM;
        public Rect bounds;
        public Color color = new Color(0.35f, 0.35f, 0.35f, 0.75f);
        public bool locked;
        public bool moveContentsWithFrame;
        public int zLayer;
    }

    public class FrameLayoutData : ScriptableObject
    {
        public List<FrameRect> frames = new();

        public static FrameLayoutData Get(AnimatorController controller)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<FrameLayoutData>().FirstOrDefault();
        }

        public static FrameLayoutData GetOrCreate(AnimatorController controller, out bool created)
        {
            var existing = Get(controller);
            if (existing != null) { created = false; return existing; }

            var data = CreateInstance<FrameLayoutData>();
            data.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(data, controller);
            created = true;
            return data;
        }

        public static void RemoveIfEmpty(AnimatorController controller)
        {
            if (controller == null) return;
            var existing = Get(controller);
            if (existing == null || existing.frames.Count > 0) return;
            AssetDatabase.RemoveObjectFromAsset(existing);
            DestroyImmediate(existing, true);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }
    }
}
#endif
