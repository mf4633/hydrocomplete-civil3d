using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>TR-55 segmented time-of-concentration worksheet (HC_TC).</summary>
    public sealed class TcCommands
    {
        private const int MaxSegments = 6;

        private sealed class TypeNameCounters
        {
            public int Sheet;
            public int Shallow;
            public int Channel;

            public string NextDefault(string type)
            {
                string t = type.Trim().ToLowerInvariant();
                if (t == "shallow") return $"Shallow{++Shallow}";
                if (t == "channel") return $"Channel{++Channel}";
                return $"Sheet{++Sheet}";
            }
        }

        [CommandMethod("HC_TC")]
        public void Tr55TimeOfConcentration()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            ed.WriteMessage("\n=== HydroComplete: TR-55 segmented Tc worksheet ===");
            ed.WriteMessage("\n  Build 1-6 flow-path segments (sheet / shallow / channel).");
            ed.WriteMessage("\n  Enter segment data; type Done when finished.\n");

            var segments = new List<TimeOfConcentration.TcSegment>();
            var nameCounters = new TypeNameCounters();

            while (segments.Count < MaxSegments)
            {
                if (segments.Count > 0 && PromptDone(ed))
                    break;

                segments.Add(PromptSegment(ed, segments.Count + 1, nameCounters));
            }

            if (segments.Count == 0)
            {
                ed.WriteMessage("\n  No segments entered.\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- TR-55 travel times ({0} segment(s)) ---", segments.Count));

            foreach (TimeOfConcentration.TcSegment seg in segments)
            {
                TimeOfConcentration.TcResult segResult = ComputeSegmentResult(seg);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0} ({1})  Tt = {2:0.##} min",
                    seg.Name, seg.Type, segResult.TcMinutes));
                WriteCalcSteps(ed, segResult.Steps, indent: "    ");
            }

            TimeOfConcentration.TcResult composite =
                TimeOfConcentration.FromTr55Segments(segments);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  TOTAL Tc = {0:0.##} min", composite.TcMinutes));
            foreach (CalcStep step in composite.Steps)
            {
                if (string.Equals(step.Label, "Tc", StringComparison.OrdinalIgnoreCase))
                    ed.WriteMessage("\n    " + step);
            }

            OfferCatchmentTcPreview(ed, doc, composite.TcMinutes);
            ed.WriteMessage("\n");
        }

        private static TimeOfConcentration.TcSegment PromptSegment(
            Editor ed,
            int segmentNumber,
            TypeNameCounters nameCounters)
        {
            string neutralDefault = $"Segment{segmentNumber}";
            string name = PromptString(ed,
                $"\nSegment {segmentNumber} name [{neutralDefault}]",
                neutralDefault);

            string type = PromptSegmentType(ed);
            if (string.Equals(name, neutralDefault, StringComparison.OrdinalIgnoreCase))
                name = nameCounters.NextDefault(type);

            double lengthFt = PromptDouble(ed, "Length L (ft)", DefaultLengthFt(type));
            double slope = PromptDouble(ed, "Slope S (ft/ft)", 0.05);

            var seg = new TimeOfConcentration.TcSegment
            {
                Name = name,
                Type = type,
                LengthFt = lengthFt,
                Slope = slope,
            };

            string typeKey = type.Trim().ToLowerInvariant();
            if (typeKey == "sheet")
            {
                seg.ManningN = PromptDouble(ed, "Manning n", 0.40);
                seg.Rainfall2YearIn = PromptDouble(ed, "2-yr 24-hr rainfall P2 (in)", 3.0);
            }
            else if (typeKey == "shallow")
            {
                seg.SurfaceType = PromptShallowSurface(ed);
            }
            else
            {
                seg.BottomWidthFt = PromptDouble(ed, "Channel bottom width (ft)", 4.0);
                seg.SideSlopeZ = PromptDouble(ed, "Side slope z (z:1)", 3.0);
                seg.DepthFt = PromptDouble(ed, "Flow depth y (ft)", 1.0);
                seg.ManningN = PromptDouble(ed, "Manning n", 0.013);
            }

            return seg;
        }

        private static double DefaultLengthFt(string type)
        {
            string t = type.Trim().ToLowerInvariant();
            if (t == "shallow") return 500.0;
            if (t == "channel") return 200.0;
            return 100.0;
        }

        private static TimeOfConcentration.TcResult ComputeSegmentResult(TimeOfConcentration.TcSegment seg)
        {
            string type = (seg.Type ?? "sheet").Trim().ToLowerInvariant();

            if (type == "sheet")
            {
                return TimeOfConcentration.SheetFlow(
                    seg.ManningN, seg.LengthFt, seg.Rainfall2YearIn, seg.Slope);
            }

            if (type == "shallow")
            {
                return TimeOfConcentration.ShallowConcentrated(
                    seg.LengthFt, seg.Slope, seg.SurfaceType);
            }

            if (type == "channel")
            {
                ChannelHydraulics.FlowResult flow = ChannelHydraulics.FlowAtDepth(
                    seg.BottomWidthFt,
                    seg.SideSlopeZ,
                    seg.DepthFt,
                    seg.ManningN,
                    seg.Slope);

                if (flow.VelocityFps <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(seg),
                        $"Zero velocity in channel segment '{seg.Name}'.");
                }

                double minutes = seg.LengthFt / flow.VelocityFps / 60.0;
                var result = new TimeOfConcentration.TcResult { TcMinutes = minutes };
                result.Steps.Add(new CalcStep(
                    $"Tt[{seg.Name}]",
                    minutes,
                    "min",
                    string.Format(CultureInfo.InvariantCulture,
                        "L/V/60  (V={0:0.##} ft/s)", flow.VelocityFps)));
                return result;
            }

            throw new ArgumentOutOfRangeException(nameof(seg), $"Unknown segment type '{seg.Type}'.");
        }

        private static void OfferCatchmentTcPreview(Editor ed, Document doc, double worksheetTcMin)
        {
            CivilDocument civilDoc = CivilApplication.ActiveDocument;
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count == 0)
                return;

            if (!PromptYesNo(ed, "\nPreview worksheet Tc on a catchment for Rational", defaultYes: false))
                return;

            string names = string.Join(", ",
                catchments.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(8));
            if (catchments.Count > 8)
                names += ", …";

            string defaultName = catchments[0].Name ?? "";
            string picked = PromptString(ed,
                $"\nCatchment name [{defaultName}]  ({names})",
                defaultName);

            Catchment? match = catchments.FirstOrDefault(c =>
                string.Equals(c.Name, picked, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                ed.WriteMessage($"\n  Catchment '{picked}' not found — no preview.\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Catchment '{0}': drawing Tc = {1:0.##} min",
                match.Name, match.TcMinutes));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Worksheet Tc = {0:0.##} min → Rational would use {0:0.##} min if applied.",
                worksheetTcMin));
            ed.WriteMessage("\n  (Drawing not modified — preview only.)");
        }

        private static string PromptSegmentType(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nSegment type [Sheet/Shallow/Channel]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Sheet");
            opts.Keywords.Add("Shallow");
            opts.Keywords.Add("Channel");
            opts.Keywords.Default = "Sheet";

            PromptResult res = ed.GetKeywords(opts);
            return res.Status == PromptStatus.OK ? res.StringResult : "Sheet";
        }

        private static TimeOfConcentration.ShallowSurfaceType PromptShallowSurface(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nShallow surface [Paved/Unpaved]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Paved");
            opts.Keywords.Add("Unpaved");
            opts.Keywords.Default = "Unpaved";

            PromptResult res = ed.GetKeywords(opts);
            return res.Status == PromptStatus.OK
                && string.Equals(res.StringResult, "Paved", StringComparison.OrdinalIgnoreCase)
                ? TimeOfConcentration.ShallowSurfaceType.Paved
                : TimeOfConcentration.ShallowSurfaceType.Unpaved;
        }

        private static bool PromptDone(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nAnother segment or Done [Next/Done]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Next");
            opts.Keywords.Add("Done");
            opts.Keywords.Default = "Next";

            PromptResult res = ed.GetKeywords(opts);
            return res.Status == PromptStatus.OK
                && string.Equals(res.StringResult, "Done", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message)
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string PromptString(Editor ed, string message, string dflt)
        {
            var opts = new PromptStringOptions(message)
            {
                AllowSpaces = false,
                UseDefaultValue = true,
                DefaultValue = dflt,
            };
            PromptResult res = ed.GetString(opts);
            return res.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(res.StringResult)
                ? res.StringResult.Trim()
                : dflt;
        }

        private static double PromptDouble(Editor ed, string message, double dflt)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : dflt;
        }

        private static void WriteCalcSteps(Editor ed, IEnumerable<CalcStep> steps, string indent)
        {
            foreach (CalcStep step in steps)
                ed.WriteMessage("\n" + indent + step);
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}