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
    /// <summary>HC_MULTIRP — per-pipe peak Q and d/D for 2/10/25/100-yr storms.</summary>
    public sealed class MultiRpCommands
    {
        [CommandMethod("HC_MULTIRP")]
        public void MultiReturnPeriodAnalysis()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var readPipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (readPipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc, readPipes);
            ApplyDefaultTcFallback(catchments, readPipes);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found — HC_MULTIRP requires catchments for Rational routing.\n");
                return;
            }

            Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, doc.Database);
            IReadOnlyDictionary<int, IdfCurve> idfByRp = ResolveMultiRpIdfCurves(resolution);
            string idfLabel = DescribeIdfSource(resolution, idfByRp);

            var analysisPipes = BuildAnalysisPipes(readPipes);
            var structureNames = NetworkPipeLinkMapper.StructureNamesFromPipes(readPipes);

            MultiRpAnalysis.MultiRpResult result;
            try
            {
                result = MultiRpAnalysis.Analyze(
                    catchments, analysisPipes, idfByRp, structureNames);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Multi-RP analysis failed: {ex.Message}\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: multi-return-period capacity ({0} catchments, {1} pipes) ---",
                catchments.Count, result.Pipes.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  IDF: {0}  |  return periods: {1}",
                idfLabel,
                string.Join(", ", result.ReturnPeriods.Select(rp => $"{rp}-yr"))));
            ed.WriteMessage(
                "\nNetwork / Pipe            Q2    d/D2   Q10   d/D10   Q25   d/D25   Q100  d/D100");

            foreach (MultiRpAnalysis.PipeMultiRpRow row in result.Pipes)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-24} {1} {2}",
                    Trim(row.NetworkName + "/" + row.PipeName, 24),
                    FormatRpColumns(row, 2, 10),
                    FormatRpColumns(row, 25, 100)));
            }

            int overloaded100 = result.Pipes.Count(row =>
                row.ByReturnPeriod.TryGetValue(100, out MultiRpAnalysis.ReturnPeriodPipeResult? rp)
                && rp.Surcharged);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0} pipe(s) surcharged at 100-yr design Q.\n", overloaded100));
        }

        private static List<MultiRpAnalysis.AnalysisPipe> BuildAnalysisPipes(IReadOnlyList<ReadPipe> readPipes)
        {
            var links = NetworkPipeLinkMapper.FromReadPipes(readPipes);
            var linksByKey = links.ToDictionary(l => l.PipeKey, StringComparer.OrdinalIgnoreCase);

            var pipes = new List<MultiRpAnalysis.AnalysisPipe>(readPipes.Count);
            foreach (ReadPipe rp in readPipes)
            {
                string key = rp.PipeId.Handle.ToString();
                if (!linksByKey.TryGetValue(key, out NetworkPipeLink? link))
                    continue;

                pipes.Add(new MultiRpAnalysis.AnalysisPipe
                {
                    PipeKey = key,
                    NetworkName = rp.NetworkName,
                    PipeName = rp.PipeName,
                    Link = link,
                    Segment = rp.Segment,
                });
            }

            return pipes;
        }

        private static IReadOnlyDictionary<int, IdfCurve> ResolveMultiRpIdfCurves(Atlas14Resolution? resolution)
        {
            if (resolution != null && resolution.Source != Atlas14Source.Embedded)
            {
                var liveCurves = new Dictionary<int, IdfCurve>(MultiRpAnalysis.StandardReturnPeriods.Length);
                foreach (int rp in MultiRpAnalysis.StandardReturnPeriods)
                {
                    try
                    {
                        Atlas14Resolution rpResolution = Atlas14Service.Resolve(
                            resolution.Lat, resolution.Lon, rp);
                        liveCurves[rp] = rpResolution.ToCurve();
                    }
                    catch
                    {
                        // Fall through to embedded multi-RP when a live fetch fails.
                    }
                }

                if (liveCurves.Count == MultiRpAnalysis.StandardReturnPeriods.Length)
                    return liveCurves;
            }

            return MultiRpAnalysis.ResolveIdfCurves(resolution);
        }

        private static string DescribeIdfSource(
            Atlas14Resolution? resolution,
            IReadOnlyDictionary<int, IdfCurve> idfByRp)
        {
            if (resolution != null)
                return $"{resolution.DisplayLabel} [{resolution.SourceLabel}]";

            int rp = idfByRp.Keys.OrderBy(k => k).FirstOrDefault();
            Atlas14Presets.Preset? preset = Atlas14Presets.Find("charlotte-nc");
            return preset != null
                ? $"{preset.DisplayName} [embedded, {rp}-yr]"
                : "embedded Atlas 14";
        }

        private static string FormatRpColumns(MultiRpAnalysis.PipeMultiRpRow row, params int[] returnPeriods)
        {
            var parts = new List<string>(returnPeriods.Length * 2);
            foreach (int rp in returnPeriods)
            {
                if (!row.ByReturnPeriod.TryGetValue(rp, out MultiRpAnalysis.ReturnPeriodPipeResult? result))
                {
                    parts.Add("   — ");
                    parts.Add("  — ");
                    continue;
                }

                string dOverD = result.Surcharged
                    ? "SURCH"
                    : result.RelativeDepth.ToString("0.00", CultureInfo.InvariantCulture);
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0,5:0.0}", result.PeakFlowCfs));
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0,5}", dOverD));
            }

            return string.Join(" ", parts);
        }

        private static void ApplyDefaultTcFallback(
            IList<Catchment> catchments,
            IReadOnlyList<ReadPipe> pipes)
        {
            if (catchments.Count == 0 || pipes.Count == 0) return;

            bool anyDefault = catchments.Any(cm =>
                Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01);
            if (!anyDefault) return;

            double networkTc = Reading.NetworkTcEstimator.EstimateSystemTc(pipes);
            if (networkTc <= 0) return;

            foreach (Catchment cm in catchments)
            {
                if (Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01)
                    cm.TcMinutes = networkTc;
            }
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}