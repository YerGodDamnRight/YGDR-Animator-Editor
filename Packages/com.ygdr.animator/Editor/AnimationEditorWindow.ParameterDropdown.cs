#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        void ShowParameterDropdown(Rect rect, Action<string> onSelected)
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            new ParameterDropdown(_controller.parameters, onSelected).Show(rect);
        }

        class ParameterDropdown : AdvancedDropdown
        {
            readonly AnimatorControllerParameter[] _parameters;
            readonly Action<string> _onSelected;

            internal ParameterDropdown(AnimatorControllerParameter[] parameters, Action<string> onSelected)
                : base(new AdvancedDropdownState())
            {
                _parameters = parameters;
                _onSelected = onSelected;
                minimumSize = new Vector2(200, 250);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Parameters");
                foreach (var param in _parameters)
                    root.AddChild(new AdvancedDropdownItem(param.name));
                return root;

                /* grouped by type — keep for future toggle mode
                foreach (var group in _parameters.GroupBy(p => p.type))
                {
                    var category = new AdvancedDropdownItem(group.Key.ToString());
                    foreach (var param in group)
                        category.AddChild(new AdvancedDropdownItem(param.name));
                    root.AddChild(category);
                }
                */
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
                => _onSelected?.Invoke(item.name);
        }
    }
}
#endif
