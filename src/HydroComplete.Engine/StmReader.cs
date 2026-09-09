using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Project-level settings carried in a Hydraflow / Civil 3D Storm Sewers
    /// <c>.stm</c> file, alongside the pipe and structure records.
    /// </summary>
    public sealed class StmProject
    {
        public string ProjectName { get; set; } = "";

        /// <summary>Design storm, years.</summary>
        public int ReturnPeriodYears { get; set; } = 10;

        /// <summary>IDF coefficients for the design storm: i = a / (t + b)^c.</summary>
        public double IdfA { get; set; }

        public double IdfB { get; set; }

        public double IdfC { get; set; }

        /// <summary>All populated IDF curves, keyed by return period in years.</summary>
        public Dictionary<int, (double A, double B, double C)> IdfCurves { get; } =
            new Dictionary<int, (double, double, double)>();

        /// <summary>Minimum time of concentration used for intensity, minutes.</summary>
        public double MinimumTcMinutes { get; set; } = 5.0;

        /// <summary>
        /// Hydraflow's "Starting HGL": the tailwater at the outfall, ft.
        /// Null when the file carries 0, which means "not set".
        /// </summary>
        public double? TailwaterFt { get; set; }

        /// <summary>Most common per-line junction loss coefficient K.</summary>
        public double JunctionK { get; set; } = 0.5;

        public bool SiUnits { get; set; }

        /// <summary>
        /// True for files written by the Civil 3D "Storm Sewers" extension, where
        /// Rise/Span are in feet and "Return Period Index" is the period itself.
        /// </summary>
        public bool Civil3DFormat { get; set; }

        public List<LandXmlPipeRecord> Pipes { get; } = new List<LandXmlPipeRecord>();

        public List<LandXmlStructureRecord> Structures { get; } = new List<LandXmlStructureRecord>();

        /// <summary>Per-structure inlet data, keyed by structure name.</summary>
        public Dictionary<string, StmInlet> Inlets { get; } = new Dictionary<string, StmInlet>();

        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Inlet geometry and hydrology carried on a Storm Sewers line.</summary>
    public sealed class StmInlet
    {
        public string StructureName { get; set; } = "";

        public double DrainageAreaAcres { get; set; }

        public double RunoffCoefficient { get; set; }

        public double InletTimeMinutes { get; set; }

        /// <summary>Curb-opening length, ft (0 when the inlet is a grate).</summary>
        public double CurbOpeningLengthFt { get; set; }

        public double GrateLengthFt { get; set; }

        public double GrateWidthFt { get; set; }

        public double GutterSlope { get; set; }

        public double CrossSlopeSx { get; set; }

        /// <summary>True when the inlet is in a sag rather than on grade.</summary>
        public bool InSag { get; set; }
    }

    /// <summary>
    /// Reads Hydraflow Storm Sewers <c>.stm</c> project files — both the
    /// standalone Hydraflow format and the Civil 3D "Storm Sewers" extension
    /// format — into the same records the LandXML importer produces, so the
    /// whole downstream path (drawing creation, analysis, reporting) is shared.
    ///
    /// Civil 3D cannot open these files. A firm with a decade of Hydraflow
    /// projects has no route into a modern drawing except retyping them, which
    /// is the reason this reader exists.
    ///
    /// The format's traps, each of which silently corrupts a run (all found by
    /// checking a real 2015 and 2012 export against the report Hydraflow
    /// printed for it):
    ///
    ///   * Two different signature lines. Civil 3D writes "Storm Sewers for
    ///     AutoCAD Civil 3D &lt;year&gt;", not "Hydraflow Storm Sewers".
    ///   * "Gutter N-Value" contains the substring "N-Value", and follows the
    ///     pipe n in every block, so a loose match reads the gutter roughness
    ///     as the pipe roughness.
    ///   * Rise/Span are FEET in the Civil 3D format and inches in the
    ///     standalone one.
    ///   * "Return Period Index" is the period in years in the Civil 3D format
    ///     and a column index into the IDF table in the standalone one.
    ///   * Every line carries its own end inverts. A pipe entering a structure
    ///     above that structure's outlet invert is a real drop, and flattening
    ///     it to the structure invert changes the slope.
    ///   * An outfall carries "Ground / Rim Elev = 0", which is not a rim.
    /// </summary>
    public static class StmReader
    {
        /// <summary>IDF table column (0-based) to return period, years.</summary>
        private static readonly (int Slot, int Years)[] IdfSlots =
        {
            (0, 1), (1, 2), (2, 3), (3, 5), (4, 10), (5, 25), (6, 50), (7, 100),
        };

        private const double CoordinateTolerance = 0.5;

        public static StmProject Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("file path required", nameof(filePath));
            }

            return ParseText(File.ReadAllText(filePath), Path.GetFileNameWithoutExtension(filePath));
        }

        public static StmProject ParseText(string text, string fallbackName = "STM Import")
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            // Accept both producers. The Civil 3D extension quotes its banner
            // from 2015 on and leaves it bare in 2012, so match on the phrase.
            bool looksLikeStm =
                (text.IndexOf("Hydraflow Storm Sewers", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 text.IndexOf("Storm Sewers for AutoCAD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 text.IndexOf("Storm Sewers", StringComparison.OrdinalIgnoreCase) >= 0) &&
                text.IndexOf("LINE DATA", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeStm)
            {
                throw new InvalidDataException(
                    "not a Hydraflow Storm Sewers .stm file (no recognised header or LINE DATA section)");
            }

            var project = new StmProject
            {
                ProjectName = fallbackName,
                Civil3DFormat = text.IndexOf("Storm Sewers for AutoCAD", StringComparison.OrdinalIgnoreCase) >= 0,
            };

            var lines = text.Replace("\r\n", "\n").Split('\n');
            ReadHeader(lines, project, out double startingHgl, out var junctionKs);
            var stmLines = ReadLineBlocks(lines, project);
            ReadIdfTable(lines, project);
            BuildRecords(stmLines, project, startingHgl, junctionKs);
            return project;
        }

        // ---------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------

        private static void ReadHeader(
            string[] lines, StmProject project, out double startingHgl, out List<double> junctionKs)
        {
            startingHgl = 0.0;
            junctionKs = new List<double>();
            int returnPeriodField = 10;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.IndexOf("Project Name = ", StringComparison.Ordinal) >= 0)
                {
                    var name = ReadStringField(line, "Project Name = ");
                    if (!string.IsNullOrWhiteSpace(name) && name != ",")
                    {
                        project.ProjectName = name;
                    }
                }
                else if (StartsWithKey(line, "SI Units?"))
                {
                    project.SiUnits = line.IndexOf("#TRUE#", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else if (TryReadNumber(line, "Minimum Tc used to calc Intensity", out double minTc))
                {
                    project.MinimumTcMinutes = minTc;
                }
                else if (TryReadNumber(line, "Return Period Index = ", out double rp))
                {
                    returnPeriodField = (int)Math.Round(rp);
                }
                else if (TryReadNumber(line, "Starting HGL = ", out double hgl))
                {
                    startingHgl = hgl;
                }
                else if (StartsWithKey(line, "Junction Loss Coeff = ") &&
                         TryReadNumber(line, "Junction Loss Coeff = ", out double k))
                {
                    junctionKs.Add(k);
                }
            }

            // Civil 3D writes the period itself; standalone writes a table slot.
            project.ReturnPeriodYears = project.Civil3DFormat
                ? Math.Max(1, returnPeriodField)
                : IdfSlots.FirstOrDefault(s => s.Slot == returnPeriodField).Years is int y && y > 0 ? y : 10;

            // K = 0 means "auto compute" in Hydraflow, not a lossless structure.
            var real = junctionKs.Where(v => v > 0.0).ToList();
            project.JunctionK = real.Count == 0
                ? 0.5
                : real.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
        }

        // ---------------------------------------------------------------
        // LINE DATA blocks
        // ---------------------------------------------------------------

        private sealed class StmLine
        {
            public int LineNo;
            public string LineId = "";
            public int Downstream;
            public double XUp, YUp, XDn, YDn;
            public double AreaAcres, C, InletTime;
            public double Length;
            public double InvertUp, InvertDn, RimUp, RimDn;
            public double Rise, Span;
            public double N = 0.013;
            public string LineType = "Cir";
            public string InletId = "";
            public double InletLength, GrateLength, GrateWidth, GutterSlope, CrossSlopeSx;
            public bool Sag;
        }

        private static List<StmLine> ReadLineBlocks(string[] lines, StmProject project)
        {
            int start = Array.FindIndex(lines, l => l.IndexOf("LINE DATA", StringComparison.OrdinalIgnoreCase) >= 0);
            if (start < 0)
            {
                throw new InvalidDataException("STM file has no LINE DATA section");
            }

            var result = new List<StmLine>();
            StmLine? current = null;
            double defaultN = 0.013;
            foreach (var raw in lines)
            {
                if (TryReadNumber(raw.Trim(), "Default Pipe n-value = ", out double dn) && dn > 0)
                {
                    defaultN = dn;
                    break;
                }
            }

            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("\"Line No. = \"", StringComparison.Ordinal))
                {
                    if (current != null && current.LineNo > 0)
                    {
                        result.Add(current);
                    }

                    current = new StmLine { N = defaultN };
                    if (TryReadNumber(line, "Line No. = ", out double no))
                    {
                        current.LineNo = (int)Math.Round(no);
                    }

                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (TryReadNumber(line, "Downstream Line No. = ", out double dsn))
                {
                    current.Downstream = (int)Math.Round(dsn);
                }
                else if (line.IndexOf("Line ID = ", StringComparison.Ordinal) >= 0)
                {
                    current.LineId = ReadStringField(line, "Line ID = ");
                }
                else if (line.IndexOf("X,Y Coord Up = ", StringComparison.Ordinal) >= 0)
                {
                    ReadXy(line, "X,Y Coord Up = ", out current.XUp, out current.YUp);
                }
                else if (line.IndexOf("X,Y Coord Dn = ", StringComparison.Ordinal) >= 0)
                {
                    ReadXy(line, "X,Y Coord Dn = ", out current.XDn, out current.YDn);
                }
                else if (TryReadNumber(line, "Drainage Area = ", out double area))
                {
                    current.AreaAcres = area;
                }
                else if (TryReadNumber(line, "Runoff Coeff. = ", out double c))
                {
                    current.C = c;
                }
                else if (TryReadNumber(line, "Inlet Time = ", out double it))
                {
                    current.InletTime = it;
                }
                else if (TryReadNumber(line, "Line Length = ", out double len))
                {
                    current.Length = len;
                }
                else if (TryReadNumber(line, "Invert Elev Up = ", out double iu))
                {
                    current.InvertUp = iu;
                }
                else if (TryReadNumber(line, "Invert Elev Dn = ", out double idn))
                {
                    current.InvertDn = idn;
                }
                else if (TryReadNumber(line, "Ground / Rim Elev Up = ", out double ru))
                {
                    current.RimUp = ru;
                }
                else if (TryReadNumber(line, "Ground / Rim Elev Dn = ", out double rd))
                {
                    current.RimDn = rd;
                }
                else if (TryReadNumber(line, "Rise = ", out double rise))
                {
                    current.Rise = rise;
                }
                else if (TryReadNumber(line, "Span = ", out double span))
                {
                    current.Span = span;
                }
                else if (line.StartsWith("\"N-Value = \"", StringComparison.Ordinal))
                {
                    // Exact key only. "Gutter N-Value = " also contains
                    // "N-Value = " and appears later in the same block.
                    if (TryReadNumber(line, "N-Value = ", out double n))
                    {
                        current.N = n;
                    }
                }
                else if (line.IndexOf("Line Type = ", StringComparison.Ordinal) >= 0)
                {
                    current.LineType = ReadStringField(line, "Line Type = ");
                }
                else if (line.IndexOf("Inlet ID = ", StringComparison.Ordinal) >= 0)
                {
                    current.InletId = ReadStringField(line, "Inlet ID = ");
                }
                else if (TryReadNumber(line, "Inlet Length = ", out double il))
                {
                    current.InletLength = il;
                }
                else if (line.StartsWith("\"Grate Length = \"", StringComparison.Ordinal) &&
                         TryReadNumber(line, "Grate Length = ", out double gl))
                {
                    current.GrateLength = gl;
                }
                else if (line.StartsWith("\"Grate Width = \"", StringComparison.Ordinal) &&
                         TryReadNumber(line, "Grate Width = ", out double gw))
                {
                    current.GrateWidth = gw;
                }
                else if (TryReadNumber(line, "Gutter Slope = ", out double gs))
                {
                    current.GutterSlope = gs;
                }
                else if (TryReadNumber(line, "Inlet Cross Slope Sx = ", out double sx))
                {
                    current.CrossSlopeSx = sx;
                }
                else if (TryReadNumber(line, "Inlet Sag = ", out double sag))
                {
                    current.Sag = Math.Abs(sag) > 0.5;
                }
            }

            if (current != null && current.LineNo > 0)
            {
                result.Add(current);
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException("STM file contains no line data");
            }

            return result;
        }

        // ---------------------------------------------------------------
        // IDF table
        // ---------------------------------------------------------------

        private static void ReadIdfTable(string[] lines, StmProject project)
        {
            int pos = Array.FindIndex(lines, l => l.IndexOf("IDF Curves", StringComparison.OrdinalIgnoreCase) >= 0);
            if (pos < 0)
            {
                project.Warnings.Add("no IDF table in file; design storm coefficients not set");
                return;
            }

            var rows = new List<double[]>();
            for (int i = pos + 1; i < lines.Length && rows.Count < 3; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var values = line.Split(',')
                    .Select(v => double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN)
                    .ToArray();
                if (values.Length >= 6 && values.All(v => !double.IsNaN(v)))
                {
                    rows.Add(values);
                }
            }

            if (rows.Count < 3)
            {
                project.Warnings.Add("IDF table incomplete; design storm coefficients not set");
                return;
            }

            foreach (var (slot, years) in IdfSlots)
            {
                if (slot >= rows[0].Length || slot >= rows[1].Length || slot >= rows[2].Length)
                {
                    continue;
                }

                double a = rows[0][slot], b = rows[1][slot], c = rows[2][slot];
                if (a > 0 && b > 0 && c > 0)
                {
                    project.IdfCurves[years] = (a, b, c);
                }
            }

            if (project.IdfCurves.TryGetValue(project.ReturnPeriodYears, out var design))
            {
                project.IdfA = design.A;
                project.IdfB = design.B;
                project.IdfC = design.C;
            }
            else if (project.IdfCurves.Count > 0)
            {
                var first = project.IdfCurves.OrderBy(kv => kv.Key).First();
                project.IdfA = first.Value.A;
                project.IdfB = first.Value.B;
                project.IdfC = first.Value.C;
                project.Warnings.Add(
                    $"no IDF curve for the {project.ReturnPeriodYears}-yr design storm; using the {first.Key}-yr curve");
            }
        }

        // ---------------------------------------------------------------
        // Records
        // ---------------------------------------------------------------

        private static void BuildRecords(
            List<StmLine> stmLines, StmProject project, double startingHgl, List<double> junctionKs)
        {
            var nameAt = new Dictionary<(long, long), string>();
            int nextId = 1;

            string EnsureStructure(
                double x, double y, double invert, double rim, bool isOutfall, string label, StmInlet? inlet)
            {
                var key = ((long)Math.Round(x / CoordinateTolerance), (long)Math.Round(y / CoordinateTolerance));
                if (nameAt.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                string name = !string.IsNullOrWhiteSpace(label) ? SanitizeId(label) : $"N{nextId}";
                nextId++;
                nameAt[key] = name;

                // Hydraflow writes rim 0 at an outfall: there is no structure
                // there, so a rim of 0 would read as hundreds of feet of
                // negative freeboard downstream.
                double resolvedRim = rim <= invert ? invert : rim;

                project.Structures.Add(new LandXmlStructureRecord
                {
                    Name = name,
                    NetworkName = project.ProjectName,
                    RimFt = resolvedRim,
                    InvertFt = invert,
                    EastingFt = x,
                    NorthingFt = y,
                });

                if (inlet != null && !isOutfall)
                {
                    inlet.StructureName = name;
                    project.Inlets[name] = inlet;
                }

                return name;
            }

            foreach (var line in stmLines.OrderBy(l => l.LineNo))
            {
                bool downstreamIsOutfall = line.Downstream == 0;
                var inlet = new StmInlet
                {
                    DrainageAreaAcres = line.AreaAcres,
                    RunoffCoefficient = line.C,
                    // An inlet time of 0 means "use the minimum Tc".
                    InletTimeMinutes = line.InletTime > 0 ? line.InletTime : project.MinimumTcMinutes,
                    CurbOpeningLengthFt = line.InletLength,
                    GrateLengthFt = line.GrateLength,
                    GrateWidthFt = line.GrateWidth,
                    GutterSlope = line.GutterSlope,
                    CrossSlopeSx = line.CrossSlopeSx,
                    InSag = line.Sag,
                };

                string up = EnsureStructure(
                    line.XUp, line.YUp, line.InvertUp, line.RimUp, false, line.InletId, inlet);
                string dn = EnsureStructure(
                    line.XDn, line.YDn, line.InvertDn, line.RimDn, downstreamIsOutfall, "", null);

                var (shape, riseFt, spanFt, diameterFt) =
                    ResolveSection(line.LineType, line.Rise, line.Span, project.SiUnits, project.Civil3DFormat);

                double length = Math.Max(line.Length, 1.0);
                project.Pipes.Add(new LandXmlPipeRecord
                {
                    Name = string.IsNullOrWhiteSpace(line.LineId) ? $"P{line.LineNo}" : SanitizeId(line.LineId),
                    NetworkName = project.ProjectName,
                    LengthFt = length,
                    DiameterFt = diameterFt,
                    Slope = length > 0 ? (line.InvertUp - line.InvertDn) / length : 0.0,
                    StartInvertFt = line.InvertUp,
                    EndInvertFt = line.InvertDn,
                    ManningN = line.N,
                    StartStructureName = up,
                    EndStructureName = dn,
                    Shape = shape,
                });
            }

            double lowestOutfall = project.Structures.Count == 0
                ? 0.0
                : project.Structures.Min(s => s.InvertFt ?? 0.0);
            project.TailwaterFt = startingHgl > 0.0 && startingHgl > lowestOutfall ? startingHgl : (double?)null;
        }

        /// <summary>
        /// Resolve the conduit section. Rise/Span are FEET in the Civil 3D
        /// format (an 18-inch pipe is <c>Rise = 1.5</c>) and inches in the
        /// standalone Hydraflow format; SI files use millimetres.
        /// </summary>
        private static (LandXmlPipeShape Shape, double RiseFt, double SpanFt, double DiameterFt) ResolveSection(
            string lineType, double rise, double span, bool si, bool civil3d)
        {
            double divisor = civil3d ? 1.0 : si ? 1000.0 : 12.0;
            double minimum = si ? 0.15 : 0.5;
            string type = (lineType ?? "").Trim().ToLowerInvariant();

            if (type.StartsWith("box", StringComparison.Ordinal))
            {
                double r = rise / divisor, s = span / divisor;
                return (LandXmlPipeShape.Box, r, s, Math.Max(Math.Max(r, s), minimum));
            }

            if (type.StartsWith("arc", StringComparison.Ordinal) || type.StartsWith("ell", StringComparison.Ordinal))
            {
                double r = rise / divisor, s = span / divisor;
                return (LandXmlPipeShape.Arch, r, s, Math.Max(Math.Max(r, s), minimum));
            }

            double raw = rise > 0 ? rise : span;
            double diameter;
            if (civil3d || si)
            {
                diameter = Math.Max(raw / divisor, minimum);
            }
            else
            {
                // A standalone US file occasionally stores a small value that is
                // already in feet; anything above 3 is inches.
                diameter = raw > 3.0 ? raw / divisor : Math.Max(raw, minimum);
            }

            return (LandXmlPipeShape.Circular, 0.0, 0.0, diameter);
        }

        // ---------------------------------------------------------------
        // Field helpers. Hydraflow writes `"Key = ",value`, quoting the key.
        // ---------------------------------------------------------------

        private static bool StartsWithKey(string line, string key) =>
            line.StartsWith("\"" + key, StringComparison.Ordinal);

        private static string ReadStringField(string line, string key)
        {
            string rest = Remainder(line, key);
            if (rest == null)
            {
                return "";
            }

            rest = rest.Trim();
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                rest = rest.Substring(1);
            }

            rest = rest.TrimStart().TrimStart(',').Trim();
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                string inner = rest.Substring(1);
                int end = inner.IndexOf('"');
                return end >= 0 ? inner.Substring(0, end) : inner;
            }

            return rest.Trim('"');
        }

        private static bool TryReadNumber(string line, string key, out double value)
        {
            value = 0.0;
            string rest = Remainder(line, key);
            if (rest == null)
            {
                return false;
            }

            // What is left starts at the key's CLOSING quote, and the key may
            // or may not have carried its own " = " (the 2012 writer includes
            // it, the 2015 one does not). Peel those off before the value.
            rest = rest.Trim();
            for (int guard = 0; guard < 3; guard++)
            {
                if (rest.StartsWith("=", StringComparison.Ordinal))
                {
                    rest = rest.Substring(1).Trim();
                }
                else if (rest.StartsWith("\"", StringComparison.Ordinal) && !IsQuotedValue(rest))
                {
                    rest = rest.Substring(1).Trim();
                }
                else
                {
                    break;
                }
            }

            rest = rest.TrimStart(',').Trim();

            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                string inner = rest.Substring(1);
                int end = inner.IndexOf('"');
                rest = (end >= 0 ? inner.Substring(0, end) : inner).Trim();
            }

            int comma = rest.IndexOf(',');
            if (comma >= 0)
            {
                rest = rest.Substring(0, comma).Trim();
            }

            return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// True when a leading quote opens a quoted <em>value</em> ("12.5")
        /// rather than closing the quoted key.
        /// </summary>
        private static bool IsQuotedValue(string rest)
        {
            int end = rest.IndexOf('"', 1);
            if (end <= 1)
            {
                return false;
            }

            string inner = rest.Substring(1, end - 1).Trim();
            return inner.Length > 0 &&
                   double.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static string? Remainder(string line, string key)
        {
            int quoted = line.IndexOf("\"" + key, StringComparison.Ordinal);
            if (quoted >= 0)
            {
                return line.Substring(quoted + key.Length + 1);
            }

            int plain = line.IndexOf(key, StringComparison.Ordinal);
            return plain >= 0 ? line.Substring(plain + key.Length) : null;
        }

        private static void ReadXy(string line, string key, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            string rest = Remainder(line, key);
            if (rest == null)
            {
                return;
            }

            var nums = rest.Split(',')
                .Select(v => v.Trim().Trim('"'))
                .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    ? (double?)d
                    : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToArray();

            if (nums.Length >= 2)
            {
                x = nums[0];
                y = nums[1];
            }
        }

        /// <summary>
        /// Names come through as Hydraflow wrote them. Civil 3D writes the same
        /// labels into LandXML ("Pipe - (5)", "AI-4"), and
        /// <see cref="LandXmlReader"/> keeps those verbatim, so a network
        /// imported from a .stm and the same network imported from a LandXML
        /// export have to end up with matching part names. Rewriting the
        /// punctuation here would silently make them two different networks.
        /// Only control characters, which no CAD label may carry, are removed.
        /// </summary>
        private static string SanitizeId(string raw)
        {
            string trimmed = (raw ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "N1";
            }

            if (!trimmed.Any(char.IsControl))
            {
                return trimmed;
            }

            return new string(trimmed.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        }
    }
}
