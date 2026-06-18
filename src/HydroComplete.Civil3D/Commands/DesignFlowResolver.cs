using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Storage;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>Resolved design flows for capacity / HGL commands.</summary>
    internal sealed class DesignFlowContext
    {
        public bool IsRouted { get; set; }

        /// <summary>Uniform Q when not routed, or system total when routed.</summary>
        public double DesignFlowCfs { get; set; }

        public Dictionary<string, double>? PipeFlowCfs { get; set; }

        public CatchmentFlowRouterResult? RouteResult { get; set; }
    }

    /// <summary>Prompts and resolves uniform or per-catchment routed design Q.</summary>
    internal static class DesignFlowResolver
    {
        public static DesignFlowContext Prompt(
            Editor ed,
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> pipes,
            string? drawingName = null)
        {
            NetworkOverrideApplier.ApplyToPipes(
                pipes,
                NetworkOverrideStore.Load(drawingName ?? ""));

            var catchments = CatchmentReader.ReadAll(db, civilDoc, pipes);
            ApplyDefaultTcFallback(catchments, pipes);
            if (catchments.Count == 0)
            {
                double uniform = PromptDouble(ed, "\nUniform design flow Q (cfs)", 10.0);
                return new DesignFlowContext { DesignFlowCfs = uniform };
            }

            bool routeFlows = PromptYesNo(ed, "\nRoute catchment flows", defaultYes: true);
            if (!routeFlows)
            {
                double uniform = PromptUniformFromCatchments(ed, db, catchments);
                return new DesignFlowContext { DesignFlowCfs = uniform };
            }

            Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, db);
            IdfCurve idf;
            string idfLabel;

            if (resolution != null)
            {
                idf = resolution.ToCurve();
                idfLabel = $"{resolution.DisplayLabel} [{resolution.SourceLabel}]";
            }
            else
            {
                idf = IdfPrompts.PromptCustomIdfCurve(ed);
                idfLabel = "custom IDF";
            }

            var links = NetworkPipeLinkMapper.FromReadPipes(pipes);
            var structureNames = NetworkPipeLinkMapper.StructureNamesFromPipes(pipes);
            var route = CatchmentFlowRouter.Route(catchments, links, idf, structureNames);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Routed catchment flows ({0}, {1} catchments, total Q={2:0.00} cfs)\n",
                DescribeAssignment(route.AssignmentMethod),
                catchments.Count,
                route.TotalPeakCfs));

            foreach (RoutedCatchmentFlow routed in route.CatchmentFlows)
            {
                string assign = string.IsNullOrEmpty(routed.AssignedStructureId)
                    ? "(unassigned)"
                    : routed.AssignedStructureId;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n    {0,-16} Q={1,6:0.00} cfs  -> struct {2}",
                    Trim(routed.Catchment.Name, 16),
                    routed.PeakFlowCfs,
                    assign));
            }

            if (route.AssignmentMethod == CatchmentAssignmentMethod.AreaWeightedHeadwater)
            {
                ed.WriteMessage(
                    "\n  Note: no catchment outlet links — flows area-weighted to headwater structures.");
            }

            int nearestCount = catchments.Count(cm =>
                !string.IsNullOrEmpty(cm.OutfallStructureId));
            if (nearestCount > 0 && route.AssignmentMethod != CatchmentAssignmentMethod.OutletStructure)
            {
                ed.WriteMessage(
                    "\n  Note: verify outlet assignments; some used proximity heuristic.");
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  IDF: {0}\n", idfLabel));

            return new DesignFlowContext
            {
                IsRouted = true,
                DesignFlowCfs = route.TotalPeakCfs,
                PipeFlowCfs = route.PipeFlowCfs,
                RouteResult = route,
            };
        }

        private static double PromptUniformFromCatchments(
            Editor ed,
            Database db,
            IReadOnlyList<Catchment> catchments)
        {
            if (PromptYesNo(ed, "\nUse Rational Q from catchments + Atlas 14 IDF", defaultYes: true))
            {
                Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, db);
                if (resolution == null)
                {
                    IdfCurve idf = IdfPrompts.PromptCustomIdfCurve(ed);
                    double systemTc = 0.0;
                    foreach (Catchment cm in catchments) systemTc = Math.Max(systemTc, cm.TcMinutes);
                    var intensity = idf.Intensity(systemTc);
                    var q = Rational.Peak(catchments, intensity.IntensityInHr);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  Rational design Q = {0:0.00} cfs ({1} catchments, Tc={2:0.0} min)\n",
                        q.PeakFlowCfs, catchments.Count, systemTc));
                    return q.PeakFlowCfs;
                }

                var peak = resolution.PeakFromCatchments(catchments);
                double tc = 0.0;
                foreach (Catchment cm in catchments) tc = Math.Max(tc, cm.TcMinutes);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Rational design Q = {0:0.00} cfs ({1} [{2}], {3} catchments, Tc={4:0.0} min)\n",
                    peak.PeakFlowCfs, resolution.DisplayLabel, resolution.SourceLabel,
                    catchments.Count, tc));
                return peak.PeakFlowCfs;
            }

            return PromptDouble(ed, "\nUniform design flow Q (cfs)", 10.0);
        }

        internal static void ApplyDefaultTcFallback(
            IList<Catchment> catchments,
            IReadOnlyList<ReadPipe> pipes)
        {
            if (catchments.Count == 0 || pipes.Count == 0) return;

            bool anyDefault = false;
            foreach (Catchment cm in catchments)
            {
                if (Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01)
                {
                    anyDefault = true;
                    break;
                }
            }

            if (!anyDefault) return;

            double networkTc = Reading.NetworkTcEstimator.EstimateSystemTc(pipes);
            if (networkTc <= 0) return;

            foreach (Catchment cm in catchments)
            {
                if (Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01)
                    cm.TcMinutes = networkTc;
            }
        }

        private static string DescribeAssignment(CatchmentAssignmentMethod method)
        {
            switch (method)
            {
                case CatchmentAssignmentMethod.OutletStructure:
                    return "outlet structures";
                case CatchmentAssignmentMethod.AreaWeightedHeadwater:
                    return "area-weighted headwaters";
                default:
                    return "uniform fallback";
            }
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

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}