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
using UnityEditor;
using UnityEditor.Animations;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorStateOps
    {
        internal static void RenameState(AnimatorState state, string newName)
        {
            Undo.RecordObject(state, "Rename State");
            state.name = newName;
            EditorUtility.SetDirty(state);
        }

        internal static void RenameStateMachine(AnimatorStateMachine stateMachine, string newName)
        {
            Undo.RecordObject(stateMachine, "Rename Sub-State Machine");
            stateMachine.name = newName;
            EditorUtility.SetDirty(stateMachine);
        }

        internal static void RenameMotion(UnityEngine.Motion motion, string newName)
        {
            if (AssetDatabase.IsMainAsset(motion))
            {
                var path = AssetDatabase.GetAssetPath(motion);
                AssetDatabase.RenameAsset(path, newName);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Undo.RecordObject(motion, "Rename Motion Clip");
                motion.name = newName;
                EditorUtility.SetDirty(motion);
            }
        }
    }
}
#endif
