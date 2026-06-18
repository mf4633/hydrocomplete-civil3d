using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace HydroComplete.Engine
{
    /// <summary>Pipe geometry and hydraulics for LandXML 1.2 storm-network export.</summary>
    public sealed class LandXmlPipeRecord
    {
        public string Name { get; set; } = "";

        public string NetworkName { get; set; } = "";

        public double LengthFt { get; set; }

        public double DiameterFt { get; set; }

        public double Slope { get; set; }

        public double StartInvertFt { get; set; }

        public double EndInvertFt { get; set; }

        public double ManningN { get; set; } = 0.013;

        /// <summary>Optional design discharge, cfs.</summary>
        public double? DesignFlowCfs { get; set; }

        public string StartStructureName { get; set; } = "";

        public string EndStructureName { get; set; } = "";

        /// <summary>LandXML cross-section (CircPipe, BoxPipe, etc.).</summary>
        public LandXmlPipeShape Shape { get; set; } = LandXmlPipeShape.Circular;

        /// <summary>Box/arch inside width or span, ft.</summary>
        public double WidthFt { get; set; }

        /// <summary>Box/arch inside height or rise, ft.</summary>
        public double HeightFt { get; set; }
    }

    /// <summary>Junction structure for LandXML export (rim/invert when known).</summary>
    public sealed class LandXmlStructureRecord
    {
        public string Name { get; set; } = "";

        public string NetworkName { get; set; } = "";

        public double? RimFt { get; set; }

        public double? InvertFt { get; set; }

        public double? NorthingFt { get; set; }

        public double? EastingFt { get; set; }

        public double? DiameterFt { get; set; }
    }

    /// <summary>
    /// Writes LandXML 1.2 storm sewer pipe networks (Hydraflow-compatible topology).
    /// Pure engine — no Autodesk references.
    /// </summary>
    public static class LandXmlWriter
    {
        private const string LandXmlNamespace = "http://www.landxml.org/schema/LandXML-1.2";

        public static string WriteToString(
            IReadOnlyList<LandXmlPipeRecord> pipes,
            IReadOnlyList<LandXmlStructureRecord>? structures = null,
            string? projectName = null)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false,
            };

            using (var writer = XmlWriter.Create(sb, settings))
                WriteDocument(writer, pipes, structures, projectName);

            return sb.ToString();
        }

        public static void Write(
            string filePath,
            IReadOnlyList<LandXmlPipeRecord> pipes,
            IReadOnlyList<LandXmlStructureRecord>? structures = null,
            string? projectName = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8,
            };

            using (var writer = XmlWriter.Create(filePath, settings))
                WriteDocument(writer, pipes, structures, projectName);
        }

        private static void WriteDocument(
            XmlWriter writer,
            IReadOnlyList<LandXmlPipeRecord> pipes,
            IReadOnlyList<LandXmlStructureRecord>? structures,
            string? projectName)
        {
            DateTime now = DateTime.Now;
            string project = string.IsNullOrWhiteSpace(projectName) ? "HydroComplete" : projectName!.Trim();

            writer.WriteStartDocument();
            writer.WriteStartElement("LandXML", LandXmlNamespace);
            writer.WriteAttributeString("version", "1.2");
            writer.WriteAttributeString("date", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("time", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            writer.WriteStartElement("Units");
            writer.WriteStartElement("Imperial");
            writer.WriteAttributeString("areaUnit", "squareFoot");
            writer.WriteAttributeString("linearUnit", "foot");
            writer.WriteAttributeString("volumeUnit", "cubicYard");
            writer.WriteAttributeString("temperatureUnit", "fahrenheit");
            writer.WriteAttributeString("pressureUnit", "PSI");
            writer.WriteAttributeString("diameterUnit", "foot");
            writer.WriteAttributeString("angularUnit", "decimal degrees");
            writer.WriteAttributeString("directionUnit", "decimal degrees");
            writer.WriteEndElement(); // Imperial
            writer.WriteEndElement(); // Units

            writer.WriteStartElement("Project");
            writer.WriteAttributeString("name", project);

            writer.WriteStartElement("PipeNetworks");
            writer.WriteAttributeString("name", project + " Networks");

            foreach (IGrouping<string, LandXmlPipeRecord> networkGroup in GroupByNetwork(pipes))
            {
                string networkName = networkGroup.Key;
                List<LandXmlPipeRecord> networkPipes = networkGroup.ToList();
                List<LandXmlStructureRecord> networkStructures = FilterStructuresForNetwork(
                    structures, networkName, networkPipes);

                WritePipeNetwork(writer, networkName, networkPipes, networkStructures);
            }

            writer.WriteEndElement(); // PipeNetworks
            writer.WriteEndElement(); // Project
            writer.WriteEndElement(); // LandXML
            writer.WriteEndDocument();
        }

        private static void WritePipeNetwork(
            XmlWriter writer,
            string networkName,
            IReadOnlyList<LandXmlPipeRecord> pipes,
            IReadOnlyList<LandXmlStructureRecord> structures)
        {
            writer.WriteStartElement("PipeNetwork");
            writer.WriteAttributeString("name", networkName);
            writer.WriteAttributeString("pipeNetType", "storm");

            writer.WriteStartElement("Structs");
            foreach (LandXmlStructureRecord structure in structures)
                WriteStructure(writer, structure, pipes);
            writer.WriteEndElement(); // Structs

            writer.WriteStartElement("Pipes");
            foreach (LandXmlPipeRecord pipe in pipes)
                WritePipe(writer, pipe);
            writer.WriteEndElement(); // Pipes

            writer.WriteEndElement(); // PipeNetwork
        }

        private static void WriteStructure(
            XmlWriter writer,
            LandXmlStructureRecord structure,
            IReadOnlyList<LandXmlPipeRecord> pipes)
        {
            string structName = ResolveStructureName(structure.Name, structure.NetworkName);
            double? sump = structure.InvertFt ?? LowestConnectedInvert(structName, pipes);

            writer.WriteStartElement("Struct");
            writer.WriteAttributeString("name", structName);
            if (structure.RimFt.HasValue)
                writer.WriteAttributeString("elevRim", FormatDouble(structure.RimFt.Value));
            if (sump.HasValue)
                writer.WriteAttributeString("elevSump", FormatDouble(sump.Value));

            if (structure.NorthingFt.HasValue && structure.EastingFt.HasValue)
            {
                writer.WriteStartElement("Center");
                writer.WriteString(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}",
                    FormatDouble(structure.NorthingFt.Value),
                    FormatDouble(structure.EastingFt.Value)));
                writer.WriteEndElement();
            }

            double diameter = structure.DiameterFt.GetValueOrDefault(4.0);
            if (diameter <= 0) diameter = 4.0;

            writer.WriteStartElement("CircStruct");
            writer.WriteAttributeString("diameter", FormatDouble(diameter));
            writer.WriteEndElement();

            foreach (LandXmlPipeRecord pipe in pipes)
            {
                string start = ResolveStructureName(pipe.StartStructureName, pipe.NetworkName);
                string end = ResolveStructureName(pipe.EndStructureName, pipe.NetworkName);
                string pipeName = ResolvePipeName(pipe.Name, pipe.NetworkName);

                if (string.Equals(start, structName, StringComparison.OrdinalIgnoreCase))
                {
                    WriteInvert(writer, pipe.StartInvertFt, "out", pipeName);
                }

                if (string.Equals(end, structName, StringComparison.OrdinalIgnoreCase))
                {
                    WriteInvert(writer, pipe.EndInvertFt, "in", pipeName);
                }
            }

            writer.WriteEndElement(); // Struct
        }

        private static void WriteInvert(XmlWriter writer, double elevationFt, string flowDir, string pipeName)
        {
            writer.WriteStartElement("Invert");
            writer.WriteAttributeString("elev", FormatDouble(elevationFt));
            writer.WriteAttributeString("flowDir", flowDir);
            writer.WriteAttributeString("refPipe", pipeName);
            writer.WriteEndElement();
        }

        private static void WritePipe(XmlWriter writer, LandXmlPipeRecord pipe)
        {
            string pipeName = ResolvePipeName(pipe.Name, pipe.NetworkName);
            string start = ResolveStructureName(pipe.StartStructureName, pipe.NetworkName);
            string end = ResolveStructureName(pipe.EndStructureName, pipe.NetworkName);

            writer.WriteStartElement("Pipe");
            writer.WriteAttributeString("name", pipeName);
            if (!string.IsNullOrEmpty(start))
                writer.WriteAttributeString("refStart", start);
            if (!string.IsNullOrEmpty(end))
                writer.WriteAttributeString("refEnd", end);
            writer.WriteAttributeString("slope", FormatDouble(pipe.Slope));
            if (pipe.DesignFlowCfs.HasValue && pipe.DesignFlowCfs.Value > 0)
                writer.WriteAttributeString("flow", FormatDouble(pipe.DesignFlowCfs.Value));

            if (pipe.Shape == LandXmlPipeShape.Box && pipe.WidthFt > 0 && pipe.HeightFt > 0)
            {
                writer.WriteStartElement("BoxPipe");
                writer.WriteAttributeString("width", FormatDouble(pipe.WidthFt));
                writer.WriteAttributeString("height", FormatDouble(pipe.HeightFt));
                writer.WriteAttributeString("manningsN", FormatDouble(pipe.ManningN));
                if (pipe.LengthFt > 0)
                    writer.WriteAttributeString("length", FormatDouble(pipe.LengthFt));
                writer.WriteEndElement();
            }
            else
            {
                writer.WriteStartElement("CircPipe");
                writer.WriteAttributeString("diameter", FormatDouble(pipe.DiameterFt));
                writer.WriteAttributeString("manningsN", FormatDouble(pipe.ManningN));
                if (pipe.LengthFt > 0)
                    writer.WriteAttributeString("length", FormatDouble(pipe.LengthFt));
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // Pipe
        }

        private static IEnumerable<IGrouping<string, LandXmlPipeRecord>> GroupByNetwork(
            IReadOnlyList<LandXmlPipeRecord> pipes)
        {
            return pipes
                .GroupBy(p => string.IsNullOrWhiteSpace(p.NetworkName) ? "Network" : p.NetworkName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }

        private static List<LandXmlStructureRecord> FilterStructuresForNetwork(
            IReadOnlyList<LandXmlStructureRecord>? structures,
            string networkName,
            IReadOnlyList<LandXmlPipeRecord> networkPipes)
        {
            var structureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LandXmlPipeRecord pipe in networkPipes)
            {
                if (!string.IsNullOrWhiteSpace(pipe.StartStructureName))
                    structureNames.Add(ResolveStructureName(pipe.StartStructureName, pipe.NetworkName));
                if (!string.IsNullOrWhiteSpace(pipe.EndStructureName))
                    structureNames.Add(ResolveStructureName(pipe.EndStructureName, pipe.NetworkName));
            }

            var results = new List<LandXmlStructureRecord>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (structures != null)
            {
                foreach (LandXmlStructureRecord structure in structures)
                {
                    if (!string.IsNullOrWhiteSpace(structure.NetworkName)
                        && !string.Equals(structure.NetworkName, networkName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = ResolveStructureName(structure.Name, networkName);
                    if (!structureNames.Contains(name) || !seen.Add(name))
                        continue;

                    results.Add(new LandXmlStructureRecord
                    {
                        Name = name,
                        NetworkName = networkName,
                        RimFt = structure.RimFt,
                        InvertFt = structure.InvertFt,
                        NorthingFt = structure.NorthingFt,
                        EastingFt = structure.EastingFt,
                        DiameterFt = structure.DiameterFt,
                    });
                }
            }

            foreach (string name in structureNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(name))
                {
                    results.Add(new LandXmlStructureRecord
                    {
                        Name = name,
                        NetworkName = networkName,
                    });
                }
            }

            return results
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double? LowestConnectedInvert(string structureName, IReadOnlyList<LandXmlPipeRecord> pipes)
        {
            double? lowest = null;
            foreach (LandXmlPipeRecord pipe in pipes)
            {
                string start = ResolveStructureName(pipe.StartStructureName, pipe.NetworkName);
                string end = ResolveStructureName(pipe.EndStructureName, pipe.NetworkName);

                if (string.Equals(start, structureName, StringComparison.OrdinalIgnoreCase))
                    lowest = Min(lowest, pipe.StartInvertFt);
                if (string.Equals(end, structureName, StringComparison.OrdinalIgnoreCase))
                    lowest = Min(lowest, pipe.EndInvertFt);
            }

            return lowest;
        }

        private static double? Min(double? current, double value)
        {
            if (!current.HasValue) return value;
            return Math.Min(current.Value, value);
        }

        private static string ResolvePipeName(string name, string networkName)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            return string.IsNullOrWhiteSpace(networkName) ? "Pipe" : networkName.Trim() + "-Pipe";
        }

        private static string ResolveStructureName(string name, string networkName)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            return string.IsNullOrWhiteSpace(networkName) ? "Structure" : networkName.Trim() + "-Struct";
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}