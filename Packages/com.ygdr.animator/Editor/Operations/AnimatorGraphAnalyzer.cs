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
    internal static class AnimatorGraphAnalyzer
    {
        internal static HashSet<AnimatorState> HighlightedStates = new();
        internal static HashSet<AnimatorStateMachine> HighlightedSubStateMachines = new();
        internal static HashSet<AnimatorStateTransition> HighlightedTransitions = new();
        internal static Color HighlightColor => AnimatorDefaultSettings.Load().analysisHighlightColor;
        internal static bool SuppressNextSelectionClear;
        internal static bool HasResults =>
            HighlightedStates.Count > 0 || HighlightedTransitions.Count > 0 || HighlightedSubStateMachines.Count > 0;

        internal static void ClearResults()
        {
            HighlightedStates.Clear();
            HighlightedSubStateMachines.Clear();
            HighlightedTransitions.Clear();
        }

        static void ApplySelectionHighlight()
        {
            SuppressNextSelectionClear = true;
            var objects = HighlightedStates.Cast<UnityEngine.Object>()
                .Concat(HighlightedSubStateMachines.Cast<UnityEngine.Object>())
                .Concat(HighlightedTransitions.Cast<UnityEngine.Object>())
                .Distinct()
                .ToArray();
            Selection.objects = objects;
        }

        // ── Graph data ────────────────────────────────────────────────────────

        sealed class GraphData
        {
            public Dictionary<AnimatorState, HashSet<AnimatorState>> adjacencyMap = new();
            public Dictionary<AnimatorState, HashSet<AnimatorState>> reverseAdjacencyMap = new();
            public Dictionary<(AnimatorState source, AnimatorState destination), List<AnimatorStateTransition>> transitionMap = new();
            public HashSet<AnimatorState> allStates = new();
            public Dictionary<AnimatorState, AnimatorStateMachine> stateToOwnerSM = new();
            public Dictionary<AnimatorStateMachine, AnimatorStateMachine> subSMToParentSM = new();
            public List<AnimatorState> entryDestinations = new();
            public HashSet<AnimatorState> anyStateDestinations = new();
        }

        static bool IsEffectiveTransition(AnimatorStateTransition transition) =>
            transition.hasExitTime || transition.conditions.Length > 0;

        static GraphData BuildGraph(AnimatorStateMachine rootSM, bool filterDeadTransitions = false)
        {
            var graph = new GraphData();
            graph.entryDestinations = ResolveEntryDestinations(rootSM);
            WalkStateMachine(rootSM, null, graph, filterDeadTransitions);

            foreach (var anyStateTransition in rootSM.anyStateTransitions)
            {
                if (anyStateTransition.mute) continue;
                if (filterDeadTransitions && !IsEffectiveTransition(anyStateTransition)) continue;
                if (anyStateTransition.destinationState != null)
                    graph.anyStateDestinations.Add(anyStateTransition.destinationState);
                else if (anyStateTransition.destinationStateMachine != null)
                    foreach (var resolved in ResolveEntryDestinations(anyStateTransition.destinationStateMachine))
                        graph.anyStateDestinations.Add(resolved);
            }

            return graph;
        }

        static void WalkStateMachine(AnimatorStateMachine stateMachine, AnimatorStateMachine parentSM, GraphData graph, bool filterDeadTransitions = false)
        {
            if (parentSM != null)
                graph.subSMToParentSM[stateMachine] = parentSM;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                graph.allStates.Add(state);
                graph.stateToOwnerSM[state] = stateMachine;
                graph.adjacencyMap.TryAdd(state, new HashSet<AnimatorState>());
                graph.reverseAdjacencyMap.TryAdd(state, new HashSet<AnimatorState>());

                foreach (var transition in state.transitions)
                {
                    if (transition.isExit || transition.mute) continue;
                    if (filterDeadTransitions && !IsEffectiveTransition(transition)) continue;

                    IEnumerable<AnimatorState> destinations = transition.destinationState != null
                        ? (IEnumerable<AnimatorState>)new[] { transition.destinationState }
                        : transition.destinationStateMachine != null
                            ? ResolveEntryDestinations(transition.destinationStateMachine)
                            : Array.Empty<AnimatorState>();

                    foreach (var destination in destinations)
                    {
                        graph.adjacencyMap[state].Add(destination);

                        if (!graph.reverseAdjacencyMap.TryGetValue(destination, out var reverseNeighbors))
                            graph.reverseAdjacencyMap[destination] = reverseNeighbors = new HashSet<AnimatorState>();
                        reverseNeighbors.Add(state);

                        var key = (state, destination);
                        if (!graph.transitionMap.TryGetValue(key, out var transitionList))
                            graph.transitionMap[key] = transitionList = new List<AnimatorStateTransition>();
                        transitionList.Add(transition);
                    }
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
                WalkStateMachine(childStateMachine.stateMachine, stateMachine, graph, filterDeadTransitions);
        }

        static List<AnimatorState> ResolveEntryDestinations(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return new List<AnimatorState>();
            var destinations = new HashSet<AnimatorState>();
            if (stateMachine.defaultState != null)
                destinations.Add(stateMachine.defaultState);
            foreach (var entryTransition in stateMachine.entryTransitions)
            {
                if (entryTransition.mute) continue;
                if (entryTransition.destinationState != null)
                    destinations.Add(entryTransition.destinationState);
                else if (entryTransition.destinationStateMachine != null)
                    foreach (var resolved in ResolveEntryDestinations(entryTransition.destinationStateMachine))
                        destinations.Add(resolved);
            }
            return destinations.ToList();
        }

        static void PopulateHighlightedSubStateMachines(GraphData graph, AnimatorStateMachine rootSM)
        {
            HighlightedSubStateMachines.Clear();
            foreach (var state in HighlightedStates)
            {
                if (!graph.stateToOwnerSM.TryGetValue(state, out var ownerSM)) continue;
                var currentSM = ownerSM;
                while (currentSM != null && currentSM != rootSM)
                {
                    HighlightedSubStateMachines.Add(currentSM);
                    if (!graph.subSMToParentSM.TryGetValue(currentSM, out currentSM))
                        break;
                }
            }
        }

        // ── Analysis methods ──────────────────────────────────────────────────

        internal static void FindUnreachableStates(AnimatorStateMachine rootSM)
        {
            ClearResults();
            var graph = BuildGraph(rootSM, filterDeadTransitions: true);

            var seeds = new HashSet<AnimatorState>(graph.entryDestinations);
            foreach (var destination in graph.anyStateDestinations)
                seeds.Add(destination);

            var reachableStates = BreadthFirstSearch(seeds, graph.adjacencyMap);

            foreach (var state in graph.allStates)
                if (!reachableStates.Contains(state))
                    HighlightedStates.Add(state);

            PopulateHighlightedSubStateMachines(graph, rootSM);
            ApplySelectionHighlight();
        }

        internal static void FindTerminalStates(AnimatorStateMachine rootSM)
        {
            ClearResults();
            var graph = BuildGraph(rootSM, filterDeadTransitions: true);

            // Pass 1: pure terminals — no effective non-self outgoing edges, no effective isExit transitions
            foreach (var state in graph.allStates)
            {
                bool hasNonSelfOutgoing = graph.adjacencyMap.TryGetValue(state, out var neighbors)
                    && neighbors.Any(destination => destination != state);
                bool hasEffectiveExitTransition = state.transitions.Any(t => !t.mute && t.isExit && IsEffectiveTransition(t));
                if (!hasNonSelfOutgoing && !hasEffectiveExitTransition)
                    HighlightedStates.Add(state);
            }

            // Pass 2: trapped SCCs — cycles with no exit path
            foreach (var member in FindTrapSCCMembers(graph))
                HighlightedStates.Add(member);

            PopulateHighlightedSubStateMachines(graph, rootSM);
            ApplySelectionHighlight();
        }

        // Iterative Tarjan SCC — returns all states trapped in cycles with no exit
        static HashSet<AnimatorState> FindTrapSCCMembers(GraphData graph)
        {
            int indexCounter = 0;
            var indices  = new Dictionary<AnimatorState, int>();
            var lowLinks = new Dictionary<AnimatorState, int>();
            var sccStack = new Stack<AnimatorState>();
            var onStack  = new HashSet<AnimatorState>();
            var trapped  = new HashSet<AnimatorState>();

            IEnumerator<AnimatorState> GetSuccessors(AnimatorState state) =>
                (graph.adjacencyMap.TryGetValue(state, out var neighbors)
                    ? (IEnumerable<AnimatorState>)neighbors
                    : Array.Empty<AnimatorState>()).GetEnumerator();

            foreach (var startState in graph.allStates)
            {
                if (indices.ContainsKey(startState)) continue;

                indices[startState] = lowLinks[startState] = indexCounter++;
                sccStack.Push(startState);
                onStack.Add(startState);

                var workStack = new Stack<(AnimatorState state, IEnumerator<AnimatorState> enumerator)>();
                workStack.Push((startState, GetSuccessors(startState)));

                while (workStack.Count > 0)
                {
                    var (currentState, enumerator) = workStack.Peek();
                    if (enumerator.MoveNext())
                    {
                        var successorState = enumerator.Current;
                        if (!indices.ContainsKey(successorState))
                        {
                            indices[successorState] = lowLinks[successorState] = indexCounter++;
                            sccStack.Push(successorState);
                            onStack.Add(successorState);
                            workStack.Push((successorState, GetSuccessors(successorState)));
                        }
                        else if (onStack.Contains(successorState))
                        {
                            lowLinks[currentState] = Math.Min(lowLinks[currentState], indices[successorState]);
                        }
                    }
                    else
                    {
                        workStack.Pop();
                        if (workStack.Count > 0)
                        {
                            var (parentState, _) = workStack.Peek();
                            lowLinks[parentState] = Math.Min(lowLinks[parentState], lowLinks[currentState]);
                        }

                        if (lowLinks[currentState] != indices[currentState]) continue;

                        var scc = new List<AnimatorState>();
                        AnimatorState popped;
                        do
                        {
                            popped = sccStack.Pop();
                            onStack.Remove(popped);
                            scc.Add(popped);
                        } while (popped != currentState);

                        if (scc.Count < 2) continue;
                        var sccSet = new HashSet<AnimatorState>(scc);
                        bool hasExitEdge = scc.Any(member =>
                            member.transitions.Any(t => !t.mute && t.isExit && IsEffectiveTransition(t)) ||
                            (graph.adjacencyMap.TryGetValue(member, out var neighbors) &&
                             neighbors.Any(successor => !sccSet.Contains(successor))));
                        if (!hasExitEdge)
                            foreach (var member in scc)
                                trapped.Add(member);
                    }
                }
            }

            return trapped;
        }

        static HashSet<AnimatorState> BreadthFirstSearch(
            HashSet<AnimatorState> seeds,
            Dictionary<AnimatorState, HashSet<AnimatorState>> adjacencyMap)
        {
            var visited = new HashSet<AnimatorState>(seeds);
            var queue   = new Queue<AnimatorState>(seeds);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!adjacencyMap.TryGetValue(current, out var neighbors)) continue;
                foreach (var neighbor in neighbors)
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
            }
            return visited;
        }
    }
}
#endif
