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


#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YGDR.Editor.Animation
{
    internal static class VRCBehaviourInspectorMenu
    {
        // ── VRCAvatarParameterDriver ──────────────────────────────────────────
        [MenuItem("CONTEXT/VRCAvatarParameterDriver/Copy Values")]
        static void CopyParamDriver(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAvatarParameterDriver/Paste Values")]
        static void PasteParamDriver(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAvatarParameterDriver/Paste Values", true)]
        static bool PasteParamDriverValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAvatarParameterDriver));

        // ── VRCAnimatorPlayAudio ──────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCAnimatorPlayAudio/Copy Values")]
        static void CopyPlayAudio(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAnimatorPlayAudio/Paste Values")]
        static void PastePlayAudio(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAnimatorPlayAudio/Paste Values", true)]
        static bool PastePlayAudioValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAnimatorPlayAudio));

        // ── VRCAnimatorTrackingControl ────────────────────────────────────────
        [MenuItem("CONTEXT/VRCAnimatorTrackingControl/Copy Values")]
        static void CopyTracking(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAnimatorTrackingControl/Paste Values")]
        static void PasteTracking(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAnimatorTrackingControl/Paste Values", true)]
        static bool PasteTrackingValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAnimatorTrackingControl));

        // ── VRCAnimatorLayerControl ───────────────────────────────────────────
        [MenuItem("CONTEXT/VRCAnimatorLayerControl/Copy Values")]
        static void CopyLayerControl(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAnimatorLayerControl/Paste Values")]
        static void PasteLayerControl(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAnimatorLayerControl/Paste Values", true)]
        static bool PasteLayerControlValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAnimatorLayerControl));

        // ── VRCAnimatorLocomotionControl ──────────────────────────────────────
        [MenuItem("CONTEXT/VRCAnimatorLocomotionControl/Copy Values")]
        static void CopyLocomotion(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAnimatorLocomotionControl/Paste Values")]
        static void PasteLocomotion(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAnimatorLocomotionControl/Paste Values", true)]
        static bool PasteLocomotionValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAnimatorLocomotionControl));

        // ── VRCAnimatorTemporaryPoseSpace ─────────────────────────────────────
        [MenuItem("CONTEXT/VRCAnimatorTemporaryPoseSpace/Copy Values")]
        static void CopyPoseSpace(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCAnimatorTemporaryPoseSpace/Paste Values")]
        static void PastePoseSpace(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCAnimatorTemporaryPoseSpace/Paste Values", true)]
        static bool PastePoseSpaceValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCAnimatorTemporaryPoseSpace));

        // ── VRCPlayableLayerControl ───────────────────────────────────────────
        [MenuItem("CONTEXT/VRCPlayableLayerControl/Copy Values")]
        static void CopyPlayableLayer(MenuCommand cmd) =>
            PatchStateNodeMenu.CopyBehaviourDirect((StateMachineBehaviour)cmd.context);

        [MenuItem("CONTEXT/VRCPlayableLayerControl/Paste Values")]
        static void PastePlayableLayer(MenuCommand cmd) =>
            PatchStateNodeMenu.PasteBehavioursToActiveState();

        [MenuItem("CONTEXT/VRCPlayableLayerControl/Paste Values", true)]
        static bool PastePlayableLayerValidate() =>
            PatchStateNodeMenu.CanPaste(typeof(VRCPlayableLayerControl));
    }
}
#endif
