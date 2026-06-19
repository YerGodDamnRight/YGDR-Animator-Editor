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
using UnityEngine.Animations;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.Constraint.Components;
#endif

namespace YGDR.Editor.Animation
{
    internal static class ConstraintConvertMenu
    {
        // ── Unity PositionConstraint ──────────────────────────────────────────
        [MenuItem("CONTEXT/PositionConstraint/Convert to Rotation Constraint")]
        static void UnityPositionToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((PositionConstraint)cmd.context, typeof(RotationConstraint));

        [MenuItem("CONTEXT/PositionConstraint/Convert to Parent Constraint")]
        static void UnityPositionToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((PositionConstraint)cmd.context, typeof(ParentConstraint));

        // ── Unity RotationConstraint ──────────────────────────────────────────
        [MenuItem("CONTEXT/RotationConstraint/Convert to Position Constraint")]
        static void UnityRotationToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((RotationConstraint)cmd.context, typeof(PositionConstraint));

        [MenuItem("CONTEXT/RotationConstraint/Convert to Parent Constraint")]
        static void UnityRotationToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((RotationConstraint)cmd.context, typeof(ParentConstraint));

        // ── Unity ParentConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/ParentConstraint/Convert to Position Constraint")]
        static void UnityParentToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((ParentConstraint)cmd.context, typeof(PositionConstraint));

        [MenuItem("CONTEXT/ParentConstraint/Convert to Rotation Constraint")]
        static void UnityParentToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((ParentConstraint)cmd.context, typeof(RotationConstraint));

#if VRC_SDK_VRCSDK3
        // ── VRC PositionConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCPositionConstraint/Convert to VRC Rotation Constraint")]
        static void VRCPositionToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCPositionConstraint)cmd.context, typeof(VRCRotationConstraint));

        [MenuItem("CONTEXT/VRCPositionConstraint/Convert to VRC Parent Constraint")]
        static void VRCPositionToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCPositionConstraint)cmd.context, typeof(VRCParentConstraint));

        // ── VRC RotationConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCRotationConstraint/Convert to VRC Position Constraint")]
        static void VRCRotationToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCRotationConstraint)cmd.context, typeof(VRCPositionConstraint));

        [MenuItem("CONTEXT/VRCRotationConstraint/Convert to VRC Parent Constraint")]
        static void VRCRotationToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCRotationConstraint)cmd.context, typeof(VRCParentConstraint));

        // ── VRC ParentConstraint ──────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCParentConstraint/Convert to VRC Position Constraint")]
        static void VRCParentToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCParentConstraint)cmd.context, typeof(VRCPositionConstraint));

        [MenuItem("CONTEXT/VRCParentConstraint/Convert to VRC Rotation Constraint")]
        static void VRCParentToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCParentConstraint)cmd.context, typeof(VRCRotationConstraint));
#endif
    }
}
#endif
