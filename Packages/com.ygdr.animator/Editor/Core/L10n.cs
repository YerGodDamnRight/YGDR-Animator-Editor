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
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class L10n
    {
        const string PrefsKey = "YGDR.AnimatorTools.Language";

        static string _languageId;
        static Dictionary<string, string> _dict;

        /* Fired after the active language changes, so live (non-IMGUI) UI can relabel itself.
           IMGUI callers don't need this — they re-read L10n.Get() every OnGUI frame already. */
        internal static event Action OnLanguageChanged;

        static string LanguageId
        {
            get => _languageId ??= EditorPrefs.GetString(PrefsKey, "en");
            set
            {
                _languageId = value;
                EditorPrefs.SetString(PrefsKey, value);
                _dict = null;
                OnLanguageChanged?.Invoke();
            }
        }

        static Dictionary<string, string> Dict => _dict ??= Load(LanguageId);

        internal static string Get(string key) => Dict.TryGetValue(key, out var value) ? value : key;

        internal static readonly string[] SupportedLanguageIds    = { "en", "fr", "es" , "de", "ja", "ko", "zh-CN" };
        internal static readonly string[] SupportedLanguageLabels = { "English", "Français (French)", "Español (Spanish)", "Deutsch (German)", "日本語 (Japanese)", "한국어 (Korean)", "简体中文 (Chinese)" };

        internal static int LanguageIndex
        {
            get
            {
                var id = LanguageId;
                for (int i = 0; i < SupportedLanguageIds.Length; i++)
                    if (SupportedLanguageIds[i] == id) return i;
                return 0;
            }
            set
            {
                if (value >= 0 && value < SupportedLanguageIds.Length)
                    LanguageId = SupportedLanguageIds[value];
            }
        }

        static Dictionary<string, string> Load(string languageId)
        {
            var dict = BuildEnglishDict();
            if (languageId == "en") return dict;

            var guids = AssetDatabase.FindAssets($"{languageId} t:TextAsset", new[] { "Packages/com.ygdr.animator/Editor/Resources/Localization" });
            if (guids.Length == 0) return dict;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (asset == null) return dict;

            var data = JsonUtility.FromJson<LocalizationData>(asset.text);
            if (data?.entries == null) return dict;
            foreach (var entry in data.entries)
                if (!string.IsNullOrEmpty(entry.key))
                    dict[entry.key] = entry.value;
            return dict;
        }

        static Dictionary<string, string> BuildEnglishDict() => new()
        {
            // ── Tabs ─────────────────────────────────────────────────────────────
            ["tabs.transitions"] = "Transitions",
            ["tabs.states"]      = "States",
            ["tabs.controller"]  = "Controller",
            ["tabs.settings"]    = "Settings",

            // ── Transitions tab — properties ──────────────────────────────────────
            ["transitions.empty"]                  = "Select a transition to edit",
            ["transitions.tag_delete_tooltip"]     = "Delete transition",
            ["transitions.tag_deselect_tooltip"]   = "Deselect transition",
            ["transitions.has_exit_time"]          = "Has Exit Time",
            ["transitions.exit_time"]              = "Exit Time",
            ["transitions.has_fixed_duration"]     = "Has Fixed Duration",
            ["transitions.duration"]               = "Transition Duration",
            ["transitions.offset"]                 = "Transition Offset",
            ["transitions.interruption_source"]    = "Interruption Source",
            ["transitions.ordered_interruption"]   = "Ordered Interruption",
            // Interruption source options
            ["transitions.interruption.none"]                    = "None",
            ["transitions.interruption.source"]                  = "Current State",
            ["transitions.interruption.destination"]             = "Next State",
            ["transitions.interruption.source_then_destination"] = "Current State Then Next State",
            ["transitions.interruption.destination_then_source"] = "Next State Then Current State",
            ["transitions.mute"]                   = "Mute",
            ["transitions.can_transition_to_self"] = "Can Transition To Self",
            ["transitions.solo"]                   = "Solo",
            // Conditions
            ["transitions.tooltip.toggle_conditions"] = "Toggle All / Shared Conditions",
            ["transitions.tooltip.switch_modes"]      = "Switch Condition Modes",
            ["transitions.tooltip.merge"]             = "Merge Transitions",
            ["transitions.tooltip.separate"]          = "Separate Transitions",
            ["transitions.tooltip.match_name"]        = "Match Name",
            ["transitions.tooltip.match_mode"]        = "Match Mode",
            ["transitions.tooltip.match_value"]       = "Match Value",
            ["transitions.tooltip.select_matching"]   = "Select Matching Transitions",
            ["transitions.tooltip.move_up"]           = "Move condition up",
            ["transitions.tooltip.move_down"]         = "Move condition down",

            ["transitions.shared_conditions"] = "Shared Conditions",
            ["transitions.all_conditions"]    = "All Conditions",
            ["transitions.conditions_empty"]  = "List is Empty",
            ["transitions.bool_true"]         = "True",
            ["transitions.bool_false"]        = "False",
            // Condition mode labels
            ["transitions.mode_true"]      = "True",
            ["transitions.mode_false"]     = "False",
            ["transitions.mode_equals"]    = "Equals",
            ["transitions.mode_not_equal"] = "Not Equal",
            ["transitions.mode_greater"]              = "Greater",
            ["transitions.mode_less"]                 = "Less",
            ["transitions.duplicate_param_tooltip"]   = "Transition contains a duplicate parameter",

            // ── States tab ────────────────────────────────────────────────────────
            ["states.empty"]                 = "Select a state to edit",
            ["states.in"]                    = "In",
            ["states.out"]                   = "Out",
            ["states.align_vertical"]        = "Align Vertical",
            ["states.align_horizontal"]      = "Align Horizontal",
            ["states.distribute_vertical"]   = "Distribute Vertical",
            ["states.distribute_horizontal"] = "Distribute Horizontal",
            ["states.name"]                  = "Name",
            ["states.tag"]                   = "Tag",
            ["states.motion"]                = "Motion",
            ["states.speed"]                 = "Speed",
            ["states.multiplier"]            = "Multiplier",
            ["states.parameter"]             = "Parameter",
            ["states.motion_time"]           = "Motion Time",
            ["states.mirror"]                = "Mirror",
            ["states.cycle_offset"]          = "Cycle Offset",
            ["states.foot_ik"]               = "Foot IK",
            ["states.write_defaults"]        = "Write Defaults",
            ["states.no_int_float_params"]   = "No Int/Float parameters in Controller",
            ["states.shared_behaviors"]      = "Shared Behaviors",

            // ── VRC behaviours — shared ───────────────────────────────────────────
            ["states.add_behavior"] = "Add Behavior",
            ["vrc.add_to_all"]     = "Add to All",
            ["vrc.remove_all"]     = "Remove All",
            ["vrc.debug_string"]   = "Debug String",
            ["vrc.list_empty"]     = "List is Empty",
            ["vrc.local_only"]     = "Local Only",
            ["vrc.layer"]          = "Layer",
            ["vrc.goal_weight"]    = "Goal Weight",
            ["vrc.blend_duration"] = "Blend Duration",

            // ── VRC behaviour field tooltips ──────────────────────────────────────
            ["vrc.tooltip.debug_string"]         = "Message for debugging",
            ["vrc.tooltip.goal_weight"]          = "Goal weight 0–1",
            ["vrc.tooltip.blend_duration"]       = "Time to reach goal weight",
            ["vrc.tooltip.blend_duration_layer"] = "Time to reach goal weight, should be less than animation length",
            ["vrc.tooltip.playable_layer"]       = "Playable layer to affect",
            ["vrc.tooltip.sub_layer_index"]      = "Index of sub-layer to affect",
            ["vrc.tooltip.layer"]                = "Layer to affect",
            ["vrc.tooltip.pose_space"]           = "Enter or exit a pose space based on the avatar's current pose",
            ["vrc.tooltip.fixed_delay"]          = "Is the delay fixed or normalized",
            ["vrc.tooltip.delay_time"]           = "Delay before applying",

            // VRC Parameter Drivers
            ["vrc.param_driver"]               = "Shared VRC Parameter Drivers",
            ["vrc.param_driver.type"]          = "Type",
            ["vrc.param_driver.source"]        = "Source",
            ["vrc.param_driver.destination"]   = "Destination",
            ["vrc.param_driver.convert_range"] = "Convert Range",
            ["vrc.param_driver.value"]         = "Value",
            ["vrc.param_driver.min_value"]     = "Min Value",
            ["vrc.param_driver.max_value"]     = "Max Value",
            ["vrc.param_driver.set"]           = "Set",
            ["vrc.param_driver.add"]           = "Add",
            ["vrc.param_driver.random"]        = "Random",
            ["vrc.param_driver.copy"]          = "Copy",
            ["vrc.param_driver.chance"]          = "Chance",
            ["vrc.param_driver.prevent_repeats"] = "Prevent Repeats",
            ["vrc.param_driver.min"]           = "Min",
            ["vrc.param_driver.max"]           = "Max",

            // VRC Play Audio
            ["vrc.audio"]                       = "Shared VRC Play Audio",
            ["vrc.audio.source"]                = "AudioSource",
            ["vrc.audio.source_path"]           = "Source Path",
            ["vrc.audio.playback_order"]        = "Playback Order",
            ["vrc.audio.order.random"]          = "Random",
            ["vrc.audio.order.unique"]          = "Unique Random",
            ["vrc.audio.order.roundabout"]      = "Roundabout",
            ["vrc.audio.order.parameter"]       = "Parameter",
            ["vrc.audio.apply.never"]           = "Never Apply",
            ["vrc.audio.apply.always"]          = "Always Apply",
            ["vrc.audio.apply.if_stopped"]      = "Apply if Stopped",
            ["vrc.audio.param_name"]            = "Parameter Name",
            ["vrc.audio.volume"]                = "Random Volume",
            ["vrc.audio.pitch"]                 = "Random Pitch",
            ["vrc.audio.loop"]                  = "Loop",
            ["vrc.audio.on_enter"]              = "On Enter",
            ["vrc.audio.on_exit"]               = "On Exit",
            ["vrc.audio.stop"]                  = "Stop",
            ["vrc.audio.play"]                  = "Play",
            ["vrc.audio.delay"]                 = "Play On Enter Delay In Seconds",
            ["vrc.audio.clips"]                 = "Clips",

            // VRC Tracking Control
            ["vrc.tracking"]               = "Shared VRC Tracking Control",
            ["vrc.tracking.no_change"]     = "No Change",
            ["vrc.tracking.tracking"]      = "Tracking",
            ["vrc.tracking.animation"]     = "Animation",
            ["vrc.tracking.set_all"]       = "Set All",
            ["vrc.tracking.head"]          = "Head",
            ["vrc.tracking.left_hand"]     = "Left Hand",
            ["vrc.tracking.right_hand"]    = "Right Hand",
            ["vrc.tracking.hip"]           = "Hip",
            ["vrc.tracking.left_foot"]     = "Left Foot",
            ["vrc.tracking.right_foot"]    = "Right Foot",
            ["vrc.tracking.left_fingers"]  = "Left Fingers",
            ["vrc.tracking.right_fingers"] = "Right Fingers",
            ["vrc.tracking.eyes_eyelids"]  = "Eyes & Eyelids",
            ["vrc.tracking.mouth_jaw"]     = "Mouth & Jaw",

            // VRC Locomotion Control
            ["vrc.locomotion"]         = "Shared VRC Locomotion Control",
            ["vrc.locomotion.label"]   = "Locomotion",
            ["vrc.locomotion.disable"] = "Disable",
            ["vrc.locomotion.enable"]  = "Enable",

            // VRC Animator Layer Control
            ["vrc.layer_control"]          = "Shared VRC Animator Layer Control",
            ["vrc.layer_control.playable"] = "Playable",

            // VRC Playable Layer Control
            ["vrc.playable_layer"] = "Shared VRC Playable Layer Control",

            // VRC Temporary Pose Space
            ["vrc.pose_space"]                = "Shared VRC Temporary Pose Space",
            ["vrc.pose_space.pose_space"]     = "Pose Space",
            ["vrc.pose_space.enter"]          = "Enter",
            ["vrc.pose_space.exit"]           = "Exit",
            ["vrc.pose_space.fixed_delay"]    = "Fixed Delay",
            ["vrc.pose_space.delay_time_s"]   = "Delay Time (s)",
            ["vrc.pose_space.delay_time_pct"] = "Delay Time (%)",

            // ── Controller tab ────────────────────────────────────────────────────
            ["controller.subtab.wd"]           = "Write Defaults",
            ["controller.subtab.network_sync"] = "Network Sync",
            ["controller.subtab.sub_assets"]   = "Sub-Assets",
            ["controller.subtab.menus"]        = "Menus",
            ["controller.no_controller"]       = "No controller selected",
            // Write Defaults
            ["controller.wd.on_col"]    = "Write Defaults On",
            ["controller.wd.off_col"]   = "Write Defaults Off",
            ["controller.wd.mixed"]     = "Mixed",
            ["controller.wd.set_all_on"]  = "Set All On",
            ["controller.wd.set_all_off"] = "Set All Off",
            // Network Sync
            ["controller.network.target_layer"]       = "Target Layer",
            ["controller.network.sync_param_type"]    = "Sync Param Type",
            ["controller.network.transitions"]        = "Transitions",
            ["controller.network.preserve_props"]     = "Preserve",
            ["controller.network.preserve_exit_time"] = "Exit Time",
            ["controller.network.preserve_duration"]  = "Duration",
            ["controller.network.preserve_offset"]    = "Offset",
            ["controller.network.sync_param_name"]    = "Param Name",
            ["controller.network.states_prefix"]      = "States Prefix",
            ["controller.network.remove_behaviours"]         = "Remove",
            ["controller.network.remove_behaviours_tooltip"] = "Remove Network Behaviours",
            ["controller.network.params"]             = "Params",
            ["controller.network.audio"]              = "Audio",
            ["controller.network.tracking"]           = "Tracking",
            ["controller.network.layer_options"]      = "Options",
            ["controller.network.create_backup"]         = "Create Backup",
            ["controller.network.create_backup_tooltip"] = "Create Backup Layer (weight 0) Before Applying",
            ["controller.network.pack_subsm"]            = "Pack",
            ["controller.network.pack_subsm_tooltip"]     = "Pack into SubSM",
            ["controller.network.own_instance"]          = "Own Driver",
            ["controller.network.own_instance_tooltip"]   = "Own Driver Instance",
            ["controller.network.merge_tagged"]          = "Merge Duplicates",
            ["controller.network.merge_tagged_tooltip"]   = "States tagged \"network merge\" with the same clip share one sync value.",
            ["controller.network.run"]                = "Run Network Sync",
            ["controller.network.no_window"]          = "No animator window open",
            ["controller.network.no_vrcsdk"]          = "Network Sync not available without VRCSDK",

            ["keyframe_menu.double_time"]                    = "Double Time",
            ["keyframe_menu.half_time_floor"]                = "Half Time (Floor)",
            ["keyframe_menu.half_time_ceiling"]              = "Half Time (Ceiling)",
            ["keyframe_menu.reverse"]                        = "Reverse Keyframes",
            ["keyframe_menu.ping_pong"]                      = "Ping-Pong Keyframes",
            ["keyframe_menu.compress_to_playhead"]           = "Compress to Playhead",
            ["keyframe_menu.cascade_bindings"]               = "Cascade Bindings",
            ["keyframe_menu.cascade_by_component_index"]     = "By Component Index",
            ["keyframe_menu.cascade_by_selection_order"]     = "By Selection Order",
            ["controller.network.duplicate_name"]     = "Duplicate Name",
            // Clip Remapper
            ["controller.repath.avatar_root"]      = "Avatar",
            ["controller.repath.scan"]             = "Scan",
            ["controller.repath.auto_on"]          = "Auto-Repath: On",
            ["controller.repath.auto_off"]         = "Auto-Repath: Off",
            ["controller.repath.no_broken"]        = "No broken bindings found",
            ["controller.repath.broken_bindings"]  = "broken bindings",
            ["controller.repath.from_path"]        = "From Path",
            ["controller.repath.to_path"]          = "To Path",
            ["controller.repath.remap_selected"]   = "Remap Selected",
            ["controller.repath.remap_clips"]      = "Remap Clips",
            ["controller.repath.confirm_title"]    = "Enable Auto-Repath",
            ["controller.repath.confirm_body"]     = "Auto-Repath automatically rewrites animation clip binding paths when a bone or GameObject is renamed or moved in the hierarchy. \n\nChanges are applied immediately and cannot be reliably undone. Ensure your project is backed up before enabling this. \n\nEnable Auto-Repath?",
            ["controller.repath.confirm_ok"]       = "Activate",
            ["controller.repath.confirm_cancel"]   = "Cancel",
            // Sub-Assets
            ["controller.subassets.state_machines"]         = "State Machines",
            ["controller.subassets.states"]                 = "States",
            ["controller.subassets.blend_trees"]            = "Blend Trees",
            ["controller.subassets.clips"]                  = "Clips",
            ["controller.subassets.search"]                 = "Search",
            ["controller.subassets.none"]                   = "None",
            ["controller.subassets.no_matches"]             = "No matches",
            ["controller.subassets.warn_empty_layer"]       = "Layer is empty",
            ["controller.subassets.warn_empty_motion"]      = "Contains empty motion field",
            ["controller.subassets.warn_invalid_transition"] = "Contains invalid transition",
            ["controller.subassets.warn_broken_bindings"]   = "Contains broken bindings",
            // Menus
            ["controller.menus.no_vrcsdk"]      = "Expression Menu editing not available without VRCSDK",
            ["controller.menus.no_menu"]        = "No VRCExpressionsMenu found — select an avatar with one assigned",
            ["controller.menus.name"]           = "Name",
            ["controller.menus.type"]           = "Type",
            ["controller.menus.parameter"]      = "Parameter",
            ["controller.menus.rotation"]       = "Rotation",
            ["controller.menus.value"]          = "Value",
            ["controller.menus.value_bool_fixed"] = "On (1) — bool params ignore this value",
            ["controller.menus.horizontal"]     = "Horizontal",
            ["controller.menus.vertical"]       = "Vertical",
            ["controller.menus.up"]             = "Up",
            ["controller.menus.down"]           = "Down",
            ["controller.menus.left"]           = "Left",
            ["controller.menus.right"]          = "Right",
            ["controller.menus.enter_submenu"]  = "Submenu",
            ["controller.menus.new_submenu"]    = "New Submenu",
            ["controller.menus.max_controls"]   = "Menu is full (8 / 8 controls)",
            ["controller.menus.type_mismatch"]  = "Parameter type mismatch",
            ["controller.menus.param_not_found"] = "Parameter not found on controller",
            ["controller.menus.open_submenu"]   = "Open",

            // ── Context menu ──────────────────────────────────────────────────────
            ["context_menu.pack_subsm"]           = "Pack into Sub-State Machine",
            ["context_menu.select_transitions"]   = "Select Transitions",
            ["context_menu.select_incoming"]      = "Incoming",
            ["context_menu.select_outgoing"]      = "Outgoing",
            ["context_menu.select_both"]          = "Both",
            ["context_menu.select_outgoing_all"]  = "Select Outgoing Transitions",
            ["context_menu.select_incoming_all"]  = "Select Incoming Transitions",
            ["context_menu.copy_behaviors"]       = "Copy Behaviors",
            ["context_menu.paste_behaviors"]      = "Paste Behaviors",
            ["context_menu.all_instances"]        = "All Instances",
            ["context_menu.paste_driver_replace"]        = "Replace",
            ["context_menu.paste_driver_append"]         = "Append",
            ["context_menu.paste_driver_append_instance"] = "Append Instance",
            ["context_menu.multi_transition"]     = "Multi Transition",
            ["context_menu.multi_from_exit"]      = "(from Exit)",
            ["context_menu.multi_from_anystate"]  = "(from AnyState)",
            ["context_menu.reverse_transitions"]  = "Reverse Transitions",
            ["context_menu.redirect_transitions"] = "Redirect Transitions",
            ["context_menu.replicate_transitions"]= "Replicate Transitions",
            ["context_menu.toggle_manual_path"]   = "Toggle Custom Routing",
            ["context_menu.unpack_subsm"]         = "Unpack Sub State Machine",
            ["context_menu.delete_all_transitions"]= "Delete All Transitions in Layer",
            ["context_menu.find_unreachable"]     = "Find Unreachable States",
            ["context_menu.find_terminal"]        = "Find Terminal States",
            ["context_menu.create_frame"]         = "Create Frame",
            ["context_menu.delete_all_frames"]    = "Delete All Frames",
            ["context_menu.frame_rename"]         = "Rename",
            ["context_menu.frame_edit_comments"]  = "Edit Comments",
            ["context_menu.frame_color"]          = "Color",
            ["context_menu.frame_zlayer"]         = "Z-Layer",
            ["context_menu.frame_zlayer_top"]     = "Move To Top",
            ["context_menu.frame_zlayer_up"]      = "Move Up",
            ["context_menu.frame_zlayer_down"]    = "Move Down",
            ["context_menu.frame_zlayer_bottom"]  = "Move To Bottom",
            ["context_menu.frame_fit_selected"]   = "Fit to Selected",
            ["context_menu.frame_move_nodes"]     = "Move Contents",
            ["context_menu.frame_lock"]           = "Lock",
            ["context_menu.frame_unlock"]         = "Unlock",
            ["context_menu.frame_delete"]         = "Delete",
            ["context_menu.frame_delete_multi"]   = "Delete ({0} frames)",
            ["context_menu.looptime"]             = "Looptime",
            ["context_menu.tag"]                  = "Tag",
            ["context_menu.remove_tags"]          = "Remove Tags",

            // ── Parameter right-click menu ────────────────────────────────────────
            ["params_menu.add_below"]      = "Add Parameter below",
            ["params_menu.convert_to"]             = "Convert to",
            ["params_menu.convert_controller"]     = "Controller",
            ["params_menu.convert_vrc_params"]     = "VRC Params",
            ["params_menu.sync_vrc_asset"]      = "Sync VRC Parameters Asset",
            ["params_menu.sync_vrc_asset_title"] = "Sync VRC Parameters Asset",
            ["params_menu.sync_vrc_asset_body"]  = "Add:\n{0}\n\nRemove:\n{1}",
            ["params_menu.sync_vrc_asset_none"]  = "(none)",
            ["params_menu.sync_vrc_asset_ok"]     = "Sync",
            ["params_menu.sync_vrc_asset_cancel"] = "Cancel",
            ["params_menu.find_uses"]      = "Find Parameter Uses",
            ["params_menu.create_aap"]     = "Create AAP",
            ["params_menu.remove_aap"]     = "Remove AAP",
            ["params_menu.remap_to"]          = "Remap to Parameter",
            ["params_menu.delete_and_clean"]   = "Delete and Clean",
            ["params_menu.remove_unused"]  = "Remove Unused Parameters",
            ["params_menu.rename_sibling_title"]  = "Rename Sibling Parameters",
            ["params_menu.rename_sibling_body"]   = "Renaming '{0}' → '{1}' affects\n{2} other {3} {4}:\n\n{5}\n\nRename these too?",
            ["params_menu.rename_sibling_param"]  = "parameter",
            ["params_menu.rename_sibling_params"] = "parameters",
            ["params_menu.rename_sibling_ok"]     = "Rename All",
            ["params_menu.rename_sibling_cancel"] = "Cancel",
            ["params_menu.rename_sibling_skip"]   = "Skip",

            // ── Layer template ────────────────────────────────────────────────────
            ["layer_template.new_layer"]       = "New Layer",
            ["layer_template.no_templates"]    = "(no templates)",
            ["layer_template.delete_template"]     = "Delete Template",
            ["layer_template.delete_confirm_title"] = "Delete Template",
            ["layer_template.delete_confirm_body"]   = "Delete '{0}' and all its clips? This cannot be undone.",
            ["layer_template.delete_confirm_ok"]     = "Delete",
            ["layer_template.delete_confirm_cancel"] = "Cancel",
            ["layer_template.create_template"] = "Create Template",
            ["layer_template.import_template"] = "Import Template",
            ["layer_template.create_blendtree"] = "Create Blend Tree Template",
            ["layer_template.import_blendtree"] = "Import Blend Tree Template",
            ["layer_template.parameter"]       = "Parameter",
            ["layer_template.export_as"]       = "Export As",
            ["layer_template.import_as"]       = "Import As",
            ["layer_template.no_params"]       = "No parameters in template.",
            ["layer_template.template_name"]   = "Template Name",
            ["layer_template.blend_tree_name"] = "Blend Tree Name",
            ["layer_template.layer_name"]      = "Layer Name",
            ["layer_template.confirm"]         = "Confirm",

            // ── Blend tree context menu ───────────────────────────────────────────
            ["blend_tree.copy"]            = "Copy",
            ["blend_tree.paste_as_child"]  = "Paste as Child",
            ["blend_tree.save_template"]   = "Save as Template",
            ["blend_tree.no_templates"]    = "(no templates)",
            ["blend_tree.delete_template_title"] = "Delete Blend Tree Template",
            ["blend_tree.remap_parameter"]        = "Remap Parameter",
            ["blend_tree.remap_parameter_to"]     = "Remap To",
            ["blend_tree.no_used_parameters"]     = "(no parameters used)",
            ["blend_tree.no_float_parameters"]    = "(no float parameters)",

            // ── Footer ────────────────────────────────────────────────────────────
            ["footer.links"] = "Links ▾",
            ["footer.docs"]  = "Docs",

            // ── Bottom bar ────────────────────────────────────────────────────────
            ["bottom_bar.selection"]         = "{0} Nodes / {1} Transitions Selected",
            ["bottom_bar.fan_seeded"]         = "Fan Mode : Seeded",
            ["bottom_bar.fan_with_paste"]     = "Fan Mode  [{0} to seed]",
            ["bottom_bar.fan"]                = "Fan Mode",
            ["bottom_bar.chain"]              = "Chain Mode",
            ["bottom_bar.paste_transition"]   = "Paste {0} Transition",
            ["bottom_bar.paste_transitions"]  = "Paste {0} Transitions",
            ["bottom_bar.multi_with_paste"]   = "Multi Transition — click destination  [{0} to seed]",
            ["bottom_bar.multi"]              = "Multi Transition — click destination",
            ["bottom_bar.redirect"]           = "Redirect Transitions — click destination",
            ["bottom_bar.replicate"]          = "Replicate Transitions — click sources",

            // ── Settings ──────────────────────────────────────────────────────────
            ["settings.language"] = "Language",
            // Section headers
            ["settings.section.interface"]           = "Interface",
            ["settings.section.graph_background"]    = "Graph Background",
            ["settings.section.node_icons"]          = "Node Icons",
            ["settings.section.transition_overlay"]  = "Transition Overlay",
            ["settings.section.node_colors"]         = "Node Colors",
            ["settings.section.transition_defaults"] = "Transition Defaults",
            ["settings.section.state_defaults"]      = "State",
            ["settings.section.keybindings"]         = "Keybindings",
            ["settings.section.miscellaneous"]       = "Miscellaneous",
            // Shared controls
            ["settings.enable"]          = "Enable",
            ["settings.apply_on_create"] = "Apply on Create",
            // Interface
            ["settings.layer_indicators"]              = "Layer Indicators",
            ["settings.type_icons"]                    = "Type Icons",
            ["settings.vrc_icons"]                     = "VRC Icons",
            ["settings.aap_icons"]                     = "AAP Icons",
            ["settings.graph_footer"]                  = "Graph Footer",
            ["settings.vrc_comp_icons"]                = "VRC Comp Icons",
            ["settings.param_budget"]                  = "Param Budget",
            ["settings.empty_params"]                  = "Empty Params",
            ["settings.palette.primary"]               = "Primary",
            ["settings.palette.secondary"]             = "Secondary",
            ["settings.palette.accent"]                = "Accent",
            ["settings.palette.param_type_vrc_colors"] = "Parameter Type / VRC Icon Colors",
            ["settings.palette.vrc_label"]             = "VRC Label",
            ["settings.palette.graph_analysis"]        = "Graph Analysis",
            ["settings.palette.analysis_highlight"]    = "Analysis Highlight",
            ["settings.localization_label"]            = "Localization",
            // Graph background
            ["settings.bg.background"]      = "Background",
            ["settings.bg.color"]           = "Color",
            ["settings.bg.image"]           = "Image",
            ["settings.bg.grid"]            = "Grid",
            ["settings.bg.major_grid"]      = "Major Grid",
            ["settings.bg.minor_grid"]      = "Minor Grid",
            ["settings.bg.grid_scale"]      = "Grid Scale",
            ["settings.bg.minor_divisions"] = "Minor Divisions",
            // Node icons overlay
            ["settings.overlay.loop_empty"] = "!/↻ Loop",
            ["settings.overlay.clip_time"] = "Clip Time",
            ["settings.overlay.wd"]        = "WD",
            ["settings.overlay.behaviors"] = "Behaviors",
            ["settings.overlay.speed"]     = "Speed",
            ["settings.overlay.motion"]    = "Motion",
            ["settings.overlay.clip_name"] = "Clip Name",
            ["settings.overlay.coords"]    = "Coords",
            ["settings.overlay.active"]    = "Active",
            ["settings.overlay.inactive"]  = "Inactive",
            // Transition overlay
            ["settings.trans_overlay.labels"]             = "Labels",
            ["settings.trans_overlay.selection_colors"]   = "Selection Colors",
            ["settings.trans_overlay.indicator_arrows"]   = "Indicator Arrows",
            ["settings.trans_overlay.animate"]            = "Animate",
            ["settings.trans_overlay.gradient"]           = "Gradient",
            ["settings.trans_overlay.gradient_speed"]     = "Gradient Speed",
            ["settings.trans_overlay.transition_line"]    = "Transition Line",
            ["settings.trans_overlay.selection_in"]       = "Selection In",
            ["settings.trans_overlay.selection_out"]      = "Selection Out",
            ["settings.trans_overlay.default_arrow"]      = "Default ▶",
            ["settings.trans_overlay.no_condition_arrow"] = "No Condition ▶",
            ["settings.trans_overlay.instant_arrow"]      = "Instant ▶",
            // Node colors
            ["settings.node_colors.visual_style"]        = "Visual Style",
            ["settings.node_colors.flat_3d"]             = "Flat / 3D",
            ["settings.node_colors.selection_highlight"] = "Selection Highlight",
            ["settings.node_colors.state_nodes"]         = "State Nodes",
            ["settings.node_colors.default_state"]       = "Default State",
            ["settings.node_colors.sub_state_machine"]   = "Sub State Machine",
            ["settings.node_colors.entry_node"]          = "Entry Node",
            ["settings.node_colors.exit_node"]           = "Exit Node",
            ["settings.node_colors.any_state"]           = "Any State",
            ["settings.node_colors.blend_tree_direct"]   = "Blend Tree Direct",
            ["settings.node_colors.blend_tree_1d"]       = "Blend Tree 1D",
            ["settings.node_colors.blend_tree_2d"]       = "Blend Tree 2D",
            // Keybindings
            ["settings.kb.select_incoming"]        = "Select Incoming",
            ["settings.kb.select_outgoing"]        = "Select Outgoing",
            ["settings.kb.select_both"]            = "Select Both",
            ["settings.kb.select_all_nodes"]       = "Select All Nodes",
            ["settings.kb.select_all_transitions"] = "Select All Transitions",
            ["settings.kb.copy"]                   = "Copy",
            ["settings.kb.paste"]                  = "Paste",
            ["settings.kb.duplicate"]              = "Duplicate",
            ["settings.kb.chain_mode"]             = "Chain Mode",
            ["settings.kb.fan_mode"]               = "Fan Mode",
            ["settings.kb.multi_transition"]       = "Multi Transition",
            ["settings.kb.reverse_transitions"]    = "Reverse Transitions",
            ["settings.kb.replicate"]              = "Replicate",
            ["settings.kb.redirect"]               = "Redirect",
                        ["settings.kb.press_key"]              = "[ Press a key... ]",
            // Miscellaneous
            ["settings.misc.wd_blend_trees"]       = "WD Blend Trees",
            ["settings.misc.prevent_layer_scroll"] = "Prevent Layer Scroll",
            ["settings.misc.prevent_param_scroll"] = "Prevent Param Scroll",
            ["settings.misc.layer_weight_1"]       = "Layer Weight 1",
            ["settings.misc.clip_menu_nesting"]    = "Clip Menu Nesting",
            ["settings.misc.layer_templates"]      = "Layer Templates",
            ["settings.misc.param_add_menu"]       = "Param Add Menu",
            ["settings.misc.frames"]               = "Frames",
            ["settings.misc.inspector_mode"]       = "Inspector Mode",
            ["settings.misc.compatibility"]        = "Compatibility",
            ["settings.misc.compatibility_desc"]   = "Turn off features that clash with other tools. Changes apply immediately.",
            ["settings.misc.context_menus"]        = "Context Menus",
            ["settings.misc.node_overlay"]         = "Node Overlay",
            ["settings.misc.node_colors_feat"]     = "Node Colors",
            ["settings.misc.transition_overlay"]   = "Transition Overlay",
            ["settings.misc.graph_interaction"]    = "Graph Interaction",
            ["settings.misc.grid_background"]      = "Grid Background",
            ["settings.misc.layer_view"]           = "Layer View",
            ["settings.misc.parameter_view"]       = "Parameter View",
            ["settings.misc.blend_tree_feat"]      = "Blend Tree",
            ["settings.misc.bottom_bar"]           = "Bottom Bar",
            // Miscellaneous — feature toggle tooltips
            ["settings.misc.tt.context_menus"]     = "Turn off if node right-click menus crash or conflict with another tool.",
            ["settings.misc.tt.node_overlay"]      = "Turn off if state nodes crash or conflict with another tool.",
            ["settings.misc.tt.node_colors"]       = "Turn off if node colors crash or conflict with another tool.",
            ["settings.misc.tt.transition_overlay"]= "Turn off if transitions crash or conflict with another tool.",
            ["settings.misc.tt.graph_interaction"] = "Turn off if the graph crashes or stops working with another tool.",
            ["settings.misc.tt.grid_background"]   = "Turn off if the grid crashes or conflicts with another tool.",
            ["settings.misc.tt.layer_view"]        = "Turn off if the layer panel crashes or conflicts with another tool.",
            ["settings.misc.tt.parameter_view"]    = "Turn off if the parameter panel crashes or conflicts with another tool.",
            ["settings.misc.tt.blend_tree"]        = "Turn off if blend trees crash or conflict with another tool.",
            ["settings.misc.tt.bottom_bar"]        = "Turn off if the bottom bar crashes or conflicts with another tool.",
            ["settings.misc.palettes"]             = "Color Palettes",
            ["settings.misc.save_palette"]         = "Save Current Palette",
            ["settings.misc.apply_palette"]        = "Apply Palette",
            ["settings.misc.copy_palette"]         = "Copy",
            ["settings.misc.palette_import_hint"]  = "Paste palette code…",
            ["settings.misc.color_tags"]           = "Color Tags",
            ["settings.misc.add_color_tag"]        = "+ Add Tag",

            // ── Layer context menu ────────────────────────────────────────────────
            ["layer_menu.copy"]            = "Copy Layer",
            ["layer_menu.paste"]           = "Paste Layer",
            ["layer_menu.paste_settings"]  = "Paste Layer Settings",
            ["layer_menu.delete"]          = "Delete Layer",
            ["layer_menu.create_template"] = "Create Template",

            // ── Transition overlay labels ─────────────────────────────────────────
            ["transition_overlay.invalid"]      = "Invalid",
            ["transition_overlay.n_conditions"] = "{n} Conditions",

            // ── Section header counts ─────────────────────────────────────────────
            ["header.n_selected"]               = "{n} Selected",
            ["controller.count.layers"]         = "{n} Layers",
            ["controller.count.state_machines"] = "{n} State Machines",
            ["controller.count.states"]         = "{n} States",
            ["controller.count.blend_trees"]    = "{n} Blend Trees",
            ["controller.count.clips"]          = "{n} Clips",
            ["controller.clean"]                = "Clean ({n})",

            // ── Toggle layer creator ──────────────────────────────────────────────
            ["toggle.menu_item"]       = "Create Toggle",
            ["toggle.title"]           = "Toggle Setup",
            ["toggle.header"]          = "Create Toggle Layer",
            ["toggle.object"]          = "object",
            ["toggle.objects"]         = "objects",
            ["toggle.form.parameter"]  = "Parameter",
            ["toggle.form.layer_name"] = "Layer Name",
            ["toggle.empty_hint"]      = "Drop GameObjects from the Hierarchy",
            ["toggle.bind.object"]     = "Object",
            ["toggle.bind.renderer"]   = "Renderer",
            ["toggle.bind.particle"]   = "Particle",
            ["toggle.bind.audio"]      = "Audio",
            ["toggle.bind.light"]      = "Light",
            ["toggle.bind.physbone"]   = "PhysBone",
            ["toggle.bind.blendshape"] = "Blendshape",
            ["toggle.create"]          = "Create",

            // ── Find Usage window ─────────────────────────────────────────────────
            ["find_usage.title"]                = "Find Uses",
            ["find_usage.tab.transitions"] = "Transitions ({n})",
            ["find_usage.tab.behaviors"]   = "Behaviors ({n})",
            ["find_usage.tab.aap_clips"]   = "AAP Clips ({n})",
            ["find_usage.tab.objects"]     = "Objects ({n})",
            ["find_usage.col.transition"]       = "Transition",
            ["find_usage.col.state_node"]       = "State Node",
            ["find_usage.col.condition"]        = "Condition",
            ["find_usage.col.animation_clip"]   = "Animation Clip",
            ["find_usage.col.behavior"]         = "Behavior",
            ["find_usage.col.effecting_object"] = "Effecting Object",
            ["find_usage.empty.no_objects"]     = "No effecting objects found in scene.",
            ["find_usage.empty.no_transitions"] = "No transitions use this parameter.",
            ["find_usage.empty.no_behaviors"]   = "No behavior uses found.",
            ["find_usage.empty.no_clips"]       = "No clips animate this parameter as AAP.",
            ["find_usage.empty.no_references"]  = "No clips reference this object.",
            ["find_usage.count.nodes_clips"]    = "{n} Nodes · {m} Clips",
            ["find_usage.behavior.parameter_driver"] = "Parameter Driver",
            ["find_usage.behavior.play_audio"]       = "Play Audio",
            ["find_usage.behavior.clip_select"]      = "clip select",
            ["find_usage.behavior.blend_x"]            = "Blend X",
            ["find_usage.behavior.blend_y"]            = "Blend Y",
        };

        [System.Serializable]
        class LocalizationData
        {
            public List<LocalizationEntry> entries;
        }

        [System.Serializable]
        class LocalizationEntry
        {
            public string key;
            public string value;
        }
    }
}
#endif
