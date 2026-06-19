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
        public bool moveNodesWithFrame;
        public int zLayer;
    }

    public class FrameLayoutData : ScriptableObject
    {
        public List<FrameRect> frames = new();

        public static FrameLayoutData GetOrCreate(AnimatorController controller)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            var existing = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<FrameLayoutData>()
                .FirstOrDefault();
            if (existing != null) return existing;

            var data = CreateInstance<FrameLayoutData>();
            data.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(data, controller);
            AssetDatabase.SaveAssets();
            return data;
        }
    }
}
#endif
