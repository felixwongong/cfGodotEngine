using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace cfGodotEngine.Debug
{
    public static class RxGraphExporter
    {
        private static readonly string[] Colors =
        {
            "#dae8fc",
            "#d5e8d4",
            "#fff2cc",
            "#f8cecc",
            "#e1d5e7",
            "#ffe6cc"
        };

        public static void ExportDrawIo(string path)
        {
            var graph = new RxGraphBuilder().Build(RxRecorder.Instance.Registry);
            var doc = BuildXml(graph);
            doc.Save(path);
        }

        private static XDocument BuildXml(List<RxGraphBuilder.GraphNode> graph)
        {
            var root = new XElement("mxGraphModel",
                new XAttribute("dx", "0"),
                new XAttribute("dy", "0"),
                new XAttribute("grid", "1"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "1"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", "850"),
                new XAttribute("pageHeight", "1100"),
                new XAttribute("math", "0"),
                new XAttribute("shadow", "0")
            );

            var xmlRoot = new XElement("root");
            xmlRoot.Add(new XElement("mxCell", new XAttribute("id", "0")));
            xmlRoot.Add(new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

            var groups = graph.GroupBy(n => n.OwnerType).ToList();
            var nodeIds = new Dictionary<RxGraphBuilder.GraphNode, int>();
            int id = 2;

            const int nodeWidth = 160;
            const int nodeHeight = 60;
            const int colWidth = 320;
            const int rowHeight = 80;

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var color = Colors[g % Colors.Length];
                var nodes = group.ToList();
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    nodeIds[node] = id++;
                    int x = 40 + g * colWidth;
                    int y = 40 + i * rowHeight;
                    xmlRoot.Add(CreateNode(nodeIds[node], node.Label, x, y, nodeWidth, nodeHeight, color));
                }
            }

            foreach (var node in graph)
            {
                if (!nodeIds.TryGetValue(node, out var sourceId)) continue;
                foreach (var edge in node.Outgoing)
                {
                    if (!nodeIds.TryGetValue(edge.To, out var targetId)) continue;
                    var edgeId = id++;
                    var edgeEl = new XElement("mxCell",
                        new XAttribute("id", edgeId),
                        new XAttribute("value", edge.Label),
                        new XAttribute("style", "edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;"),
                        new XAttribute("edge", "1"),
                        new XAttribute("source", sourceId),
                        new XAttribute("target", targetId),
                        new XAttribute("parent", "1")
                    );
                    edgeEl.Add(new XElement("mxGeometry", new XAttribute("relative", "1")));
                    xmlRoot.Add(edgeEl);
                }
            }

            root.Add(xmlRoot);
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XElement CreateNode(int id, string label, int x, int y, int width, int height, string fill)
        {
            var cell = new XElement("mxCell",
                new XAttribute("id", id),
                new XAttribute("value", label),
                new XAttribute("style", $"rounded=1;whiteSpace=wrap;html=1;fillColor={fill};"),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1")
            );
            cell.Add(new XElement("mxGeometry",
                new XAttribute("x", x),
                new XAttribute("y", y),
                new XAttribute("width", width),
                new XAttribute("height", height),
                new XAttribute("as", "geometry")
            ));
            return cell;
        }
    }
}
