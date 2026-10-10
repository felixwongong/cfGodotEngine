using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using cfEngine.Rx;

namespace cfGodotEngine.Debug
{
    public class RxGraphBuilder
    {
        public class GraphNode
        {
            public string Id;
            public string OwnerType;
            public string Label;
            public readonly List<GraphEdge> Outgoing = new();
        }

        public class GraphEdge
        {
            public GraphNode From;
            public GraphNode To;
            public string Label;
            public string ListenerTargetType;
        }

        public List<GraphNode> Build(RxSubscriptionRegistry registry)
        {
            var nodes = new Dictionary<object, GraphNode>();
            var records = registry.LiveRecords;

            // First pass: create a node for every owner that is still alive.
            foreach (var record in records)
            {
                if (record.OwnerRef == null) continue;
                if (!record.OwnerRef.TryGetTarget(out var owner)) continue;
                if (!nodes.TryGetValue(owner, out _))
                {
                    nodes[owner] = new GraphNode
                    {
                        Id = RuntimeHelpers.GetHashCode(owner).ToString(),
                        OwnerType = record.OwnerType,
                        Label = record.OwnerTag
                    };
                }
            }

            // Second pass: add edges when the listener target is itself a known owner.
            foreach (var record in records)
            {
                if (record.OwnerRef == null || !record.OwnerRef.TryGetTarget(out var fromOwner)) continue;
                if (!record.ListenerTargetRef.TryGetTarget(out var target)) continue;
                if (!nodes.ContainsKey(target)) continue;

                var fromNode = nodes[fromOwner];
                var toNode = nodes[target];
                fromNode.Outgoing.Add(new GraphEdge
                {
                    From = fromNode,
                    To = toNode,
                    Label = record.ListenerMethod,
                    ListenerTargetType = record.ListenerTargetType
                });
            }

            return new List<GraphNode>(nodes.Values);
        }
    }
}
