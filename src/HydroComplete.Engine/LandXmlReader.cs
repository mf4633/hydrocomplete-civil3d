using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace HydroComplete.Engine
{
    /// <summary>Result of parsing a LandXML 1.2 storm pipe network file.</summary>
    public sealed class LandXmlImportResult
    {
        public string ProjectName { get; set; } = "";

        public List<LandXmlPipeRecord> Pipes { get; set; } = new List<LandXmlPipeRecord>();

        public List<LandXmlStructureRecord> Structures { get; set; } = new List<LandXmlStructureRecord>();

        public List<string> Errors { get; set; } = new List<string>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Reads LandXML 1.2 storm sewer pipe networks (reverse of <see cref="LandXmlWriter"/>).
    /// Pure engine — no Autodesk references.
    /// </summary>
    public static class LandXmlReader
    {
        private const string LandXmlNamespace = "http://www.landxml.org/schema/LandXML-1.2";

        public static LandXmlImportResult Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            var result = new LandXmlImportResult();
            if (!File.Exists(filePath))
            {
                result.Errors.Add("File not found: " + filePath);
                return result;
            }

            using (var stream = File.OpenRead(filePath))
                ParseDocument(stream, result);

            return result;
        }

        public static LandXmlImportResult Parse(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var result = new LandXmlImportResult();
            ParseDocument(stream, result);
            return result;
        }

        private static void ParseDocument(Stream stream, LandXmlImportResult result)
        {
            var document = new XmlDocument { PreserveWhitespace = false };

            try
            {
                string xml;
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    xml = reader.ReadToEnd();

                document.LoadXml(xml);
            }
            catch (XmlException ex)
            {
                result.Errors.Add("Invalid XML: " + ex.Message);
                return;
            }
            catch (IOException ex)
            {
                result.Errors.Add("Could not read file: " + ex.Message);
                return;
            }

            XmlElement? root = document.DocumentElement;
            if (root == null)
            {
                result.Errors.Add("Empty XML document.");
                return;
            }

            XmlNamespaceManager nsm = CreateNamespaceManager(document);
            bool hasExpectedNamespace = string.Equals(
                root.NamespaceURI,
                LandXmlNamespace,
                StringComparison.Ordinal);

            if (!hasExpectedNamespace && !string.IsNullOrEmpty(root.NamespaceURI))
                result.Warnings.Add("Root namespace is not LandXML 1.2; attempting tolerant parse.");

            XmlNode? projectNode = SelectNode(root, nsm, "Project");
            if (projectNode?.Attributes?["name"] != null)
                result.ProjectName = projectNode.Attributes["name"]!.Value.Trim();

            XmlNodeList networkNodes = SelectNodes(root, nsm, "//PipeNetwork");
            if (networkNodes.Count == 0)
            {
                result.Warnings.Add("No PipeNetwork elements found.");
                return;
            }

            var invertByPipe = new Dictionary<string, (double? Start, double? End)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (XmlNode networkNode in networkNodes)
            {
                string networkName = ReadAttribute(networkNode, "name");
                if (string.IsNullOrWhiteSpace(networkName))
                    networkName = "Network";

                ParseStructures(networkNode, nsm, networkName, result, invertByPipe);
                ParsePipes(networkNode, nsm, networkName, result);
            }

            ApplyInvertElevations(result.Pipes, invertByPipe);
        }

        private static void ParseStructures(
            XmlNode networkNode,
            XmlNamespaceManager nsm,
            string networkName,
            LandXmlImportResult result,
            Dictionary<string, (double? Start, double? End)> invertByPipe)
        {
            XmlNode? structsNode = SelectChild(networkNode, nsm, "Structs");
            if (structsNode == null) return;

            foreach (XmlNode structNode in structsNode.ChildNodes)
            {
                if (!IsElement(structNode, "Struct")) continue;

                string name = ReadAttribute(structNode, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    result.Warnings.Add("Skipping structure with no name in network '" + networkName + "'.");
                    continue;
                }

                var record = new LandXmlStructureRecord
                {
                    Name = name,
                    NetworkName = networkName,
                    RimFt = TryReadDouble(structNode, "elevRim"),
                    InvertFt = TryReadDouble(structNode, "elevSump"),
                };

                XmlNode? centerNode = SelectChild(structNode, nsm, "Center");
                if (centerNode != null && TryParseCenter(centerNode.InnerText, out double northing, out double easting))
                {
                    record.NorthingFt = northing;
                    record.EastingFt = easting;
                }

                XmlNode? circStruct = SelectChild(structNode, nsm, "CircStruct");
                if (circStruct != null)
                    record.DiameterFt = TryReadDouble(circStruct, "diameter");

                CollectInverts(structNode, nsm, invertByPipe);
                result.Structures.Add(record);
            }
        }

        private static void CollectInverts(
            XmlNode structNode,
            XmlNamespaceManager nsm,
            Dictionary<string, (double? Start, double? End)> invertByPipe)
        {
            foreach (XmlNode child in structNode.ChildNodes)
            {
                if (!IsElement(child, "Invert")) continue;

                string refPipe = ReadAttribute(child, "refPipe");
                if (string.IsNullOrWhiteSpace(refPipe)) continue;

                double? elev = TryReadDouble(child, "elev");
                if (!elev.HasValue) continue;

                string flowDir = ReadAttribute(child, "flowDir");
                if (!invertByPipe.TryGetValue(refPipe, out (double? Start, double? End) pair))
                    pair = (null, null);

                if (string.Equals(flowDir, "out", StringComparison.OrdinalIgnoreCase))
                    pair.Start = elev;
                else if (string.Equals(flowDir, "in", StringComparison.OrdinalIgnoreCase))
                    pair.End = elev;

                invertByPipe[refPipe] = pair;
            }
        }

        private static void ParsePipes(
            XmlNode networkNode,
            XmlNamespaceManager nsm,
            string networkName,
            LandXmlImportResult result)
        {
            XmlNode? pipesNode = SelectChild(networkNode, nsm, "Pipes");
            if (pipesNode == null) return;

            foreach (XmlNode pipeNode in pipesNode.ChildNodes)
            {
                if (!IsElement(pipeNode, "Pipe")) continue;

                string name = ReadAttribute(pipeNode, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    result.Warnings.Add("Skipping pipe with no name in network '" + networkName + "'.");
                    continue;
                }

                var record = new LandXmlPipeRecord
                {
                    Name = name,
                    NetworkName = networkName,
                    StartStructureName = ReadAttribute(pipeNode, "refStart"),
                    EndStructureName = ReadAttribute(pipeNode, "refEnd"),
                    Slope = TryReadDouble(pipeNode, "slope").GetValueOrDefault(),
                    DesignFlowCfs = TryReadDouble(pipeNode, "flow"),
                };

                XmlNode? circPipe = SelectChild(pipeNode, nsm, "CircPipe");
                XmlNode? boxPipe = SelectChild(pipeNode, nsm, "BoxPipe");

                if (circPipe != null)
                {
                    record.DiameterFt = TryReadDouble(circPipe, "diameter").GetValueOrDefault();
                    record.ManningN = TryReadDouble(circPipe, "manningsN").GetValueOrDefault(0.013);
                    record.LengthFt = TryReadDouble(circPipe, "length").GetValueOrDefault();
                }
                else if (boxPipe != null)
                {
                    double? width = TryReadDouble(boxPipe, "width");
                    double? height = TryReadDouble(boxPipe, "height");
                    record.DiameterFt = EquivalentDiameter(width, height);
                    record.ManningN = TryReadDouble(boxPipe, "manningsN").GetValueOrDefault(0.013);
                    record.LengthFt = TryReadDouble(boxPipe, "length").GetValueOrDefault();

                    if (!width.HasValue || !height.HasValue)
                        result.Warnings.Add("BoxPipe '" + name + "' missing width or height; diameter set to 0.");
                }
                else
                {
                    result.Warnings.Add("Pipe '" + name + "' has no CircPipe or BoxPipe child.");
                }

                result.Pipes.Add(record);
            }
        }

        private static void ApplyInvertElevations(
            List<LandXmlPipeRecord> pipes,
            Dictionary<string, (double? Start, double? End)> invertByPipe)
        {
            foreach (LandXmlPipeRecord pipe in pipes)
            {
                if (!invertByPipe.TryGetValue(pipe.Name, out (double? Start, double? End) pair))
                    continue;

                if (pair.Start.HasValue)
                    pipe.StartInvertFt = pair.Start.Value;
                if (pair.End.HasValue)
                    pipe.EndInvertFt = pair.End.Value;
            }
        }

        private static double EquivalentDiameter(double? width, double? height)
        {
            if (!width.HasValue || !height.HasValue || width.Value <= 0 || height.Value <= 0)
                return 0;

            return 2.0 * width.Value * height.Value / (width.Value + height.Value);
        }

        private static bool TryParseCenter(string text, out double northing, out double easting)
        {
            northing = 0;
            easting = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            return TryParseDouble(parts[0], out northing)
                && TryParseDouble(parts[1], out easting);
        }

        private static XmlNamespaceManager CreateNamespaceManager(XmlDocument document)
        {
            var nsm = new XmlNamespaceManager(document.NameTable);
            nsm.AddNamespace("lx", LandXmlNamespace);
            return nsm;
        }

        private static XmlNode? SelectNode(XmlNode root, XmlNamespaceManager nsm, string localName)
        {
            return root.SelectSingleNode("lx:" + localName, nsm)
                ?? root.SelectSingleNode(".//lx:" + localName, nsm)
                ?? FindDescendantByLocalName(root, localName);
        }

        private static XmlNodeList SelectNodes(XmlNode root, XmlNamespaceManager nsm, string xpath)
        {
            XmlNodeList? nodes = root.SelectNodes(xpath, nsm);
            if (nodes != null && nodes.Count > 0)
                return nodes;

            return FindDescendantsByLocalName(root, "PipeNetwork");
        }

        private static XmlNode? SelectChild(XmlNode parent, XmlNamespaceManager nsm, string localName)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (IsElement(child, localName))
                    return child;
            }

            return parent.SelectSingleNode("lx:" + localName, nsm);
        }

        private static XmlNodeList FindDescendantsByLocalName(XmlNode root, string localName)
        {
            var matches = new List<XmlNode>();
            CollectByLocalName(root, localName, matches);
            return new XmlNodeArray(matches);
        }

        private static XmlNode? FindDescendantByLocalName(XmlNode root, string localName)
        {
            if (IsElement(root, localName))
                return root;

            foreach (XmlNode child in root.ChildNodes)
            {
                XmlNode? found = FindDescendantByLocalName(child, localName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void CollectByLocalName(XmlNode node, string localName, List<XmlNode> matches)
        {
            if (IsElement(node, localName))
                matches.Add(node);

            foreach (XmlNode child in node.ChildNodes)
                CollectByLocalName(child, localName, matches);
        }

        private static bool IsElement(XmlNode? node, string localName)
        {
            return node != null
                && node.NodeType == XmlNodeType.Element
                && string.Equals(node.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadAttribute(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value?.Trim() ?? "";
        }

        private static double? TryReadDouble(XmlNode node, string attributeName)
        {
            string raw = ReadAttribute(node, attributeName);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return TryParseDouble(raw, out double value) ? value : (double?)null;
        }

        private static bool TryParseDouble(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private sealed class XmlNodeArray : XmlNodeList
        {
            private readonly IReadOnlyList<XmlNode> _nodes;

            public XmlNodeArray(IReadOnlyList<XmlNode> nodes)
            {
                _nodes = nodes;
            }

            public override int Count => _nodes.Count;

            public override XmlNode? Item(int index) => _nodes[index];

            public override System.Collections.IEnumerator GetEnumerator() => _nodes.GetEnumerator();
        }
    }
}