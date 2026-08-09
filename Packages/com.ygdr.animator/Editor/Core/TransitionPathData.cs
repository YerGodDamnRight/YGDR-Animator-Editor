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
    // A "special" node (AnyState/Entry/Exit) has no AnimatorState asset, so fromState/toState are null for it —
    // fromSpecial/toSpecial disambiguate which special node (if any) sits on that side of the edge.
    public enum SpecialNode { None, AnyState, Entry, Exit }

    [Serializable]
    public class TransitionPathEntry
    {
        public AnimatorState fromState;  // null when fromSpecial != None — edge identity, not a single transition
        public AnimatorState toState;    // null when toSpecial != None — one edge can carry multiple AnimatorStateTransitions
        public SpecialNode fromSpecial;
        public SpecialNode toSpecial;
        public List<Vector2> points = new();
        public Vector2 sourceOffset;     // delta from the source node's native anchor — lets the line leave horizontally
        public Vector2 destOffset;       // delta from the destination node's native anchor
    }

    public class TransitionPathData : ScriptableObject
    {
        public List<TransitionPathEntry> entries = new();

        [NonSerialized] Dictionary<(int fromId, SpecialNode fromSpecial, int toId, SpecialNode toSpecial), TransitionPathEntry> _lookup;
        [NonSerialized] int _lookupVersion = -1;
        int _dataVersion;

        static int IdOf(AnimatorState state) => state == null ? 0 : state.GetInstanceID();

        void EnsureLookup()
        {
            if (_lookup != null && _lookupVersion == _dataVersion) return;
            _lookup = new(entries.Count);
            foreach (var entry in entries)
                _lookup[(IdOf(entry.fromState), entry.fromSpecial, IdOf(entry.toState), entry.toSpecial)] = entry;
            _lookupVersion = _dataVersion;
        }

        public TransitionPathEntry TryGetEntry(AnimatorState fromState, AnimatorState toState,
            SpecialNode fromSpecial = SpecialNode.None, SpecialNode toSpecial = SpecialNode.None)
        {
            EnsureLookup();
            return _lookup.TryGetValue((IdOf(fromState), fromSpecial, IdOf(toState), toSpecial), out var entry) ? entry : null;
        }

        public void SetEnabled(AnimatorState fromState, AnimatorState toState, bool enabled,
            SpecialNode fromSpecial = SpecialNode.None, SpecialNode toSpecial = SpecialNode.None)
        {
            var existing = TryGetEntry(fromState, toState, fromSpecial, toSpecial);
            if (enabled)
            {
                if (existing != null) return;
                entries.Add(new TransitionPathEntry { fromState = fromState, toState = toState, fromSpecial = fromSpecial, toSpecial = toSpecial });
            }
            else
            {
                if (existing == null) return;
                entries.Remove(existing);
            }
            _dataVersion++;
            EditorUtility.SetDirty(this);
        }

        // Call after mutating a TransitionPathEntry's points list directly (add/delete/drag).
        public void BumpVersion() => _dataVersion++;

        public void PruneOrphaned()
        {
            bool changed = entries.RemoveAll(entry =>
                (entry.fromState != null && entry.fromState.Equals(null)) ||
                (entry.toState != null && entry.toState.Equals(null))) > 0;
            if (changed)
            {
                _dataVersion++;
                EditorUtility.SetDirty(this);
            }
        }

        public static TransitionPathData Get(AnimatorController controller)
        {
            if (controller == null) return null;
            var path = AssetDatabase.GetAssetPath(controller);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<TransitionPathData>().FirstOrDefault();
        }

        public static TransitionPathData GetOrCreate(AnimatorController controller, out bool created)
        {
            var existing = Get(controller);
            if (existing != null) { created = false; return existing; }

            var data = CreateInstance<TransitionPathData>();
            data.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(data, controller);
            created = true;
            return data;
        }

        public static void RemoveIfEmpty(AnimatorController controller)
        {
            if (controller == null) return;
            var existing = Get(controller);
            if (existing == null || existing.entries.Count > 0) return;
            AssetDatabase.RemoveObjectFromAsset(existing);
            DestroyImmediate(existing, true);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }
    }
}
#endif
