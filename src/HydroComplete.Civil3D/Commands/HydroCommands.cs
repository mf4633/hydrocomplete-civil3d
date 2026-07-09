using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Auth;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// The HC_* command set. v0 reads geometry from the active Civil 3D drawing,
    /// runs the engine, and prints a formula-transparent report to the command
    /// line. Writing results back to the drawing as labels/profiles is the next
    /// increment.
    /// </summary>
    public sealed class HydroCommands
    {
        [CommandMethod("HC_ABOUT")]
        public void About()
        {
            Editor ed = Active().Editor;
            ed.WriteMessage("\n=== HydroComplete for Civil 3D 1.7.2 ===");
            ed.WriteMessage("\n  HC_NETWORK       Per-network summary (pipes, length, inverts, diameters, structures)");
            ed.WriteMessage("\n  HC_PIPES         Manning capacity of every pipe-network pipe (circular, box, arch)");
            ed.WriteMessage("\n  HC_PIPES_WRITE   Label Qfull/Vfull on layer HC-CAPACITY");
            ed.WriteMessage("\n  HC_CAPACITY      Design Q vs Q_full check (d/D, surcharge flag)");
            ed.WriteMessage("\n  HC_CAPACITY_WRITE Label overloaded pipes on layer HC-CAPACITY");
            ed.WriteMessage("\n  HC_SIZE          Standard catalog pipe sizing (velocity, % full)");
            ed.WriteMessage("\n  HC_VALIDATE      Design-criteria review (slope, capacity, velocity, cover, HGL)");
            ed.WriteMessage("\n  HC_ANALYZE       Full-network analysis (hydrology, routing, HGL, sediment, compliance)");
            ed.WriteMessage("\n  HC_REVIEW        Design review + state regulatory compliance check");
            ed.WriteMessage("\n  HC_SCS           SCS CN runoff from catchments");
            ed.WriteMessage("\n  HC_UNIT_HYDRO    SCS unit hydrograph table output");
            ed.WriteMessage("\n  HC_SEDIMENT      RUSLE/MUSLE soil loss from catchments");
            ed.WriteMessage("\n  HC_WQV           Water quality volume calculation");
            ed.WriteMessage("\n  HC_DETENTION     Detention pond routing (Modified Puls, SCS UH inflow, orifice/weir outlets)");
            ed.WriteMessage("\n  HC_BMP_SIZE      WQV-based BMP sizing (bioretention, wet pond, sand filter, swale)");
            ed.WriteMessage("\n  HC_WQ_TRAIN      BMP treatment train with EMC pollutant loads from catchments");
            ed.WriteMessage("\n  HC_SEDIMENT_BASIN Sediment basin design from peak Q (NCDEQ surface-area method)");
            ed.WriteMessage("\n  HC_HGL           Steady HGL (normal depth) + HEC-22/momentum/bend losses + HC-HGL labels + plan profile");
            ed.WriteMessage("\n  HC_PREPOST       Pre/post-development peak comparison (SCS UH, multi-storm state depths)");
            ed.WriteMessage("\n  HC_OPTIMIZE      BMP treatment-train cost optimizer (top 3 chains)");
            ed.WriteMessage("\n  HC_CULVERT       Culvert headwater (FHWA HDS-5 inlet/outlet control)");
            ed.WriteMessage("\n  HC_GVF           Gradually varied flow profile (Standard Step, trapezoidal channel)");
            ed.WriteMessage("\n  HC_PROFILE       Chainage profile plot (invert, crown, optional HGL) â€” modal options dialog");
            ed.WriteMessage("\n  HC_PROFILE_DXF   Export chainage profile to DXF (invert, crown, optional HGL)");
            ed.WriteMessage("\n  HC_REPORT      Export formula-transparent HTML Manning + HGL report (free)");
            ed.WriteMessage("\n  HC_REPORT_PDF  Export formula-transparent PDF Manning + HGL report (Pro)");
            ed.WriteMessage("\n  HC_RATIONAL    Rational Q from catchments + NOAA Atlas 14 IDF presets");
            ed.WriteMessage("\n  HC_MULTIRP     Per-pipe Q and d/D for 2/10/25/100-yr return periods");
            ed.WriteMessage("\n  HC_TC          TR-55 segmented time-of-concentration worksheet");
            ed.WriteMessage("\n  HC_INLETS      HEC-22 inlet check (grate / sag / curb opening) â€” modal options dialog");
            ed.WriteMessage("\n  HC_NETWORK_EDIT  Edit pipe Q and Manning n overrides (saved per drawing)");
            ed.WriteMessage("\n  HC_NETWORK_DIAGRAM Export HTML/SVG pipe network schematic from plan topology");
            ed.WriteMessage("\n  HC_PUMP          Pump station duty-point check (curve vs system head)");
            ed.WriteMessage("\n  HC_COST          Pipe cost roll-up from diameter catalog ($/LF)");
            ed.WriteMessage("\n  HC_BACKGROUND    Attach georeferenced raster on HC-BACKGROUND layer");
            ed.WriteMessage("\n  HC_HYDROGRAPH    Synthetic hydrograph ordinates (SCS, Clark, Snyder)");
            ed.WriteMessage("\n  HC_ROUTE_HYDRO   Route catchment hydrographs through pipe network (lag + junction superposition)");
            ed.WriteMessage("\n  HC_BIORETENTION  Bioretention routing with underdrain/outlet");
            ed.WriteMessage("\n  HC_WETLAND       Wetland detention routing");
            ed.WriteMessage("\n  HC_SOIL          Live SSURGO soil lookup (drawing geo or map unit name) + BMP suitability");
            ed.WriteMessage("\n  HC_LANDXML       Export pipe network to LandXML 1.2");
            ed.WriteMessage("\n  HC_LANDXML_IMPORT Import LandXML 1.2 (preview + optional write to drawing)");
            ed.WriteMessage("\n  HC_ATLAS14     List Atlas 14 IDF presets + live PFDS fetch info");
            ed.WriteMessage("\n  HC_LOSS        Green-Ampt / Horton / CN incremental loss method on SCS Type II storm");
            ed.WriteMessage("\n  HC_CONTINUOUS  Multi-year continuous simulation (17 cities, Hargreaves ET, pollutant loads)");
            ed.WriteMessage("\n  HC_WQ_DIAGRAM  HTML/SVG treatment-train node diagram from catchment land use + BMP chain");
            ed.WriteMessage("\n  HC_DAG         Visual model builder — drag-and-drop node DAG editor (WebView2 panel, net8 only)");
            ed.WriteMessage("\n  HC_DAG_SAVE    Save the current DAG to a .hcdag file alongside the drawing (net8 only)");
            ed.WriteMessage("\n  HC_DAG_LOAD    Load a .hcdag file and open it in the DAG editor (net8 only)");
            ed.WriteMessage("\n  HC_ACTIVATE    Activate Pro with email + beta token (hc_live_*)");
            ed.WriteMessage("\n  HC_LICENSE     Show Free/Pro license status and activation info");
            ed.WriteMessage("\n  HC_ABOUT       This list");
            ed.WriteMessage("\n  Pro features (PDF export) require a license â€” visit https://hydrocomplete.com/civil3d");
            ed.WriteMessage("\n  Engine: Rational, TR-55 Tc, Manning (circular/box/arch), GVF, detention, BMP/WQV, LandXML, HEC-22 â€” fully shown.\n");
        }

        [CommandMethod("HC_ACTIVATE")]
        public void Activate()
        {
            Editor ed = Active().Editor;
            ed.WriteMessage("\n=== HydroComplete Pro Activation ===");
            ed.WriteMessage("\n  Enter your beta email and token (format: hc_live_â€¦).");
            ed.WriteMessage("\n  You may paste both on one line: email@domain.com hc_live_â€¦\n");

            string email;
            string token;

            var emailOpts = new PromptStringOptions("\nEmail (or paste 'email token')")
            {
                AllowSpaces = true,
            };
            PromptResult emailRes = ed.GetString(emailOpts);
            if (emailRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(emailRes.StringResult))
            {
                ed.WriteMessage("\n  Activation cancelled.\n");
                return;
            }

            if (LicenseActivator.TryParseCombinedInput(emailRes.StringResult, out email, out token))
            {
                ed.WriteMessage($"\n  Parsed email: {email}");
            }
            else
            {
                email = emailRes.StringResult.Trim();
                var tokenOpts = new PromptStringOptions("\nActivation token")
                {
                    AllowSpaces = false,
                };
                PromptResult tokenRes = ed.GetString(tokenOpts);
                if (tokenRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(tokenRes.StringResult))
                {
                    ed.WriteMessage("\n  Activation cancelled.\n");
                    return;
                }

                token = tokenRes.StringResult.Trim();
            }

            try
            {
                var result = LicenseGate.ActivateAsync(email, token, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!result.Success)
                {
                    ed.WriteMessage($"\n  Activation failed: {result.Message}\n");
                    return;
                }

                ed.WriteMessage($"\n  {result.Message}");
                ed.WriteMessage($"\n  Mode: {result.Mode}");
                if (!string.IsNullOrWhiteSpace(result.Expires)
                    && DateTimeOffset.TryParse(result.Expires, out var expires))
                {
                    ed.WriteMessage($"\n  Expires: {expires:yyyy-MM-dd}");
                }

                ed.WriteMessage($"\n  Status: {LicenseGate.GetStatusLabel()}");
                ed.WriteMessage("\n  Pro unlocks HC_REPORT_PDF. Run HC_LICENSE for details.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Activation error: {ex.Message}\n");
            }
        }

        [CommandMethod("HC_LICENSE")]
        public void License()
        {
            Editor ed = Active().Editor;
            ed.WriteMessage("\n=== HydroComplete License ===");
            ed.WriteMessage($"\n  Status: {LicenseGate.GetStatusLabel()}");
            ed.WriteMessage($"\n  Validation mode: {LicenseGate.GetValidationModeLabel()}");
            ed.WriteMessage($"\n  Last validated: {LicenseGate.GetLastValidatedLabel()}");
            ed.WriteMessage($"\n  Network: {LicenseGate.GetOnlineOfflineLabel()}");
            ed.WriteMessage($"\n  License file: {LicenseGate.GetLicenseFilePath()}");
            ed.WriteMessage("\n  Activate: HC_ACTIVATE  |  https://hydrocomplete.com/civil3d");
            ed.WriteMessage("\n  Pro unlocks PDF export (HC_REPORT_PDF). HTML reports (HC_REPORT) stay free.\n");
        }

        [CommandMethod("HC_ATLAS14")]
        public void Atlas14List()
        {
            IdfPrompts.WriteAtlas14List(Active().Editor);
        }

        [CommandMethod("HC_NETWORK")]
        public void NetworkSummary()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var summaries = PipeNetworkReader.ReadNetworkSummaries(doc.Database, civilDoc);
            if (summaries.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: pipe network summary ({0} network(s)) ---",
                summaries.Count));
            ed.WriteMessage("\nNetwork                 Pipes  Structs  Length(ft)   Invert(ft) min-max      Dia(ft) min-max");

            foreach (var summary in summaries.OrderBy(s => s.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                string invertRange = summary.HasPipes
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0,8:0.00} - {1,8:0.00}", summary.MinInvertFt, summary.MaxInvertFt)
                    : "       â€”";
                string diaRange = summary.HasPipes
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0,5:0.00} - {1,5:0.00}", summary.MinDiameterFt, summary.MaxDiameterFt)
                    : "    â€”";

                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-22} {1,5}  {2,7}  {3,10:0.0}  {4}  {5}",
                    Trim(summary.NetworkName, 22),
                    summary.PipeCount,
                    summary.StructureCount,
                    summary.TotalLengthFt,
                    invertRange,
                    diaRange));
            }

            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_PIPES")]
        public void Pipes()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            ed.WriteMessage($"\n--- HydroComplete: Manning capacity ({pipes.Count} pipes) ---");
            ed.WriteMessage("\nNetwork / Pipe            Dia(ft)  Slope    Q_full(cfs)  V_full(fps)");
            foreach (var rp in pipes)
            {
                try
                {
                    var cap = Manning.Capacity(rp.Segment);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n{0,-24} {1,6:0.00}  {2,6:0.0000}  {3,10:0.00}  {4,10:0.00}",
                        Trim(rp.NetworkName + "/" + rp.PipeName, 24),
                        rp.Segment.DiameterFt, rp.Segment.Slope,
                        cap.FullFlowCfs, cap.FullVelocityFps));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n{Trim(rp.PipeName, 24),-24} (skipped: {ex.Message})");
                }
            }
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_CAPACITY")]
        public void CapacityCheck()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            var report = CapacityReportBuilder.Build(
                pipes, flow.DesignFlowCfs, flow.PipeFlowCfs);

            string qHeader = report.IsRouted
                ? string.Format(CultureInfo.InvariantCulture,
                    "routed Q, system total={0:0.0} cfs", flow.DesignFlowCfs)
                : string.Format(CultureInfo.InvariantCulture, "Q={0:0.0} cfs", flow.DesignFlowCfs);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: design capacity check ({0}, {1} pipes) ---",
                qHeader, report.Rows.Count));
            ed.WriteMessage("\nNetwork / Pipe            Q_full   Q_des   Q_des/Q   d/D   SURCH");

            foreach (CapacityPipeRow row in report.Rows)
            {
                ReadPipe rp = row.Pipe;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-24} {1,6:0.0}  {2,6:0.0}  {3,7:0.00}  {4,5:0.00}  {5,5}",
                    Trim(rp.NetworkName + "/" + rp.PipeName, 24),
                    row.QFullCfs, row.DesignFlowCfs, row.FlowRatio, row.RelativeDepth,
                    row.Surcharged ? "*" : ""));
            }

            int overloaded = report.Rows.Count(r => r.Surcharged);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0} pipe(s) surcharged (Q > peak open-channel capacity).\n", overloaded));
        }

        [CommandMethod("HC_CAPACITY_WRITE")]
        public void CapacityWrite()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            bool overloadOnly = PromptYesNo(ed,
                "\nLabel overloaded pipes only (No = label all pipes, dim OK)",
                defaultYes: true);

            var report = CapacityReportBuilder.Build(
                pipes, flow.DesignFlowCfs, flow.PipeFlowCfs);
            var write = PipeNetworkWriter.WriteDesignCapacity(doc.Database, report.Rows, overloadOnly);

            string mode = overloadOnly ? "overloaded" : "all";
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: wrote {0} capacity label(s) ({1} mode) ---",
                write.Updated, mode));
            if (write.Skipped > 0)
                ed.WriteMessage($"\n  Skipped {write.Skipped} pipe(s).");
            foreach (string err in write.Errors)
                ed.WriteMessage($"\n  {err}");
            ed.WriteMessage("\n  Labels on layer HC-CAPACITY at each pipe midpoint.\n");
        }

        [CommandMethod("HC_PIPES_WRITE")]
        public void PipesWrite()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            var capacities = new Dictionary<ObjectId, Manning.CapacityResult>();
            foreach (var rp in pipes)
            {
                try
                {
                    capacities[rp.PipeId] = Manning.Capacity(rp.Segment);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nSkipping {rp.PipeName}: {ex.Message}");
                }
            }

            var write = PipeNetworkWriter.WriteCapacities(doc.Database, pipes, capacities);
            ed.WriteMessage($"\n--- HydroComplete: wrote capacity to {write.Updated} pipe(s) ---");
            if (write.Skipped > 0)
                ed.WriteMessage($"\n  Skipped {write.Skipped} pipe(s).");
            foreach (string err in write.Errors)
                ed.WriteMessage($"\n  {err}");
            ed.WriteMessage("\n  Labels placed on layer HC-CAPACITY at each pipe midpoint.\n");
        }

        [CommandMethod("HC_HGL")]
        public void HglProfile()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            bool useMinorLosses = PromptYesNo(ed, "\nInclude HEC-22 junction/exit losses", defaultYes: true);
            bool useMomentumJunction = PromptYesNo(ed, "\nUse momentum junction losses? [Yes/No]", defaultYes: false);

            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = useMinorLosses,
                IncludeExitLoss = useMinorLosses,
                UseMomentumJunction = useMomentumJunction,
                UseBendLoss = useMinorLosses,
            };

            var networks = NetworkTopology.BuildOrderedNetworks(pipes);
            var allMidHgl = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var pipeHglEnds = new Dictionary<string, HglProfileWriter.HglPipeEnds>(StringComparer.OrdinalIgnoreCase);
            var surchargedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string lossNote = useMinorLosses ? " + HEC-22" : "";
            if (useMomentumJunction)
                lossNote += " + momentum junction";
            string qNote = flow.IsRouted
                ? $"routed Q, system total={flow.DesignFlowCfs:0.0} cfs"
                : $"Q={flow.DesignFlowCfs:0.0} cfs";
            ed.WriteMessage($"\n--- HydroComplete: steady HGL{lossNote} ({qNote}, normal depth) ---");
            if (flow.IsRouted)
                ed.WriteMessage("\nNetwork                 Pipe              Q(cfs)  HGL_US(ft)  HGL_DS(ft)  HGL_mid(ft)  h_m(ft)  d/D    SURCH");
            else
                ed.WriteMessage("\nNetwork                 Pipe              HGL_US(ft)  HGL_DS(ft)  HGL_mid(ft)  h_m(ft)  d/D    SURCH");

            foreach (var net in networks)
            {
                if (net.OrderedPipes.Count == 0) continue;

                List<NetworkReach> reaches = flow.IsRouted && flow.PipeFlowCfs != null
                    ? NetworkTopology.BuildReaches(net.OrderedPipes, flow.PipeFlowCfs, useMinorLosses)
                    : NetworkTopology.BuildReaches(net.OrderedPipes, flow.DesignFlowCfs, useMinorLosses);

                double tailwater = PromptTailwater(ed, net);
                var profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwater, hglOptions);

                for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
                {
                    ReadPipe rp = net.OrderedPipes[i];
                    HglProfilePoint point = profile[i];
                    string reachName = reaches[i].Name;

                    double hglUs = point.HglUpstreamFt;
                    double hglDs = point.HglFt;
                    double hglMid = 0.5 * (hglUs + hglDs);
                    allMidHgl[reachName] = hglMid;
                    pipeHglEnds[reachName] = new HglProfileWriter.HglPipeEnds
                    {
                        HglUsFt = hglUs,
                        HglDsFt = hglDs,
                    };

                    bool surcharged = Hgl.IsSurcharged(
                        hglUs, hglDs,
                        rp.UpstreamInvertFt, rp.DownstreamInvertFt, rp.Segment.DiameterFt);
                    if (surcharged)
                        surchargedKeys.Add(reachName);

                    string dOverD = reaches[i].FlowSurcharged
                        ? "SURCH"
                        : reaches[i].RelativeDepth.ToString("0.00", CultureInfo.InvariantCulture);

                    if (flow.IsRouted)
                    {
                        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                            "\n{0,-22} {1,-16} {2,6:0.0}  {3,10:0.00}  {4,10:0.00}  {5,10:0.00}  {6,8:0.00}  {7,5}  {8,5}",
                            Trim(net.NetworkName, 22),
                            Trim(rp.PipeName, 16),
                            reaches[i].FlowCfs,
                            hglUs, hglDs, hglMid, point.HmFt,
                            dOverD,
                            surcharged ? "*" : ""));
                    }
                    else
                    {
                        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                            "\n{0,-22} {1,-16} {2,10:0.00}  {3,10:0.00}  {4,10:0.00}  {5,8:0.00}  {6,5}  {7,5}",
                            Trim(net.NetworkName, 22),
                            Trim(rp.PipeName, 16),
                            hglUs, hglDs, hglMid, point.HmFt,
                            dOverD,
                            surcharged ? "*" : ""));
                    }
                }
            }

            var write = HglLabelWriter.WriteHglLabels(doc.Database, pipes, allMidHgl, surchargedKeys);
            ed.WriteMessage($"\n--- Wrote HGL labels to {write.Updated} pipe(s) on layer HC-HGL ---");
            if (write.Skipped > 0)
                ed.WriteMessage($"\n  Skipped {write.Skipped} pipe(s).");
            foreach (string err in write.Errors)
                ed.WriteMessage($"\n  {err}");

            bool drawProfile = PromptYesNo(ed, "\nDraw HGL profile polyline", defaultYes: true);
            if (drawProfile)
            {
                var profileWrite = HglProfileWriter.WriteHglProfiles(doc.Database, networks, pipeHglEnds);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n--- Wrote HGL profile polyline(s) for {0} network(s) ({1} vertices) on layer HC-HGL-PROFILE ---",
                    profileWrite.NetworksDrawn, profileWrite.VerticesDrawn));
                if (profileWrite.Skipped > 0)
                    ed.WriteMessage($"\n  Skipped {profileWrite.Skipped} network(s)/pipe(s).");
                foreach (string err in profileWrite.Errors)
                    ed.WriteMessage($"\n  {err}");
            }

            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_REPORT")]
        public void Report()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            if (!TryBuildHydraulicReport(doc, ed, out var pipes, out var capacities, out var hglData, out var capacityData, out string drawingName))
                return;

            string reportPath;
            try
            {
                reportPath = HtmlReportWriter.Write(drawingName, pipes, capacities, hglData, capacityData);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n  Could not write report: " + ex.Message + "\n");
                return;
            }
            ed.WriteMessage($"\n--- HydroComplete: HTML report written ---");
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Manning capacity + steady HGL (Q={0:0.0} cfs) -> {1}", hglData.DesignFlowCfs, reportPath));
            OfferOpenReport(ed, reportPath);
        }

        [CommandMethod("HC_REPORT_PDF")]
        public void ReportPdf()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            if (!LicenseGate.IsProEnabled())
            {
                ed.WriteMessage("\n--- HydroComplete: PDF export is a Pro feature ---");
                ed.WriteMessage("\n  Activate at https://hydrocomplete.com/civil3d");
                ed.WriteMessage("\n  Free alternative: HC_REPORT exports the same Manning + HGL report as HTML.\n");
                return;
            }

            if (!TryBuildHydraulicReport(doc, ed, out var pipes, out var capacities, out var hglData, out var capacityData, out string drawingName))
                return;

            string reportPath;
            try
            {
                reportPath = PdfReportWriter.Write(drawingName, pipes, capacities, hglData, capacityData);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n  Could not write report: " + ex.Message + "\n");
                return;
            }
            ed.WriteMessage($"\n--- HydroComplete: PDF report written ---");
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Manning capacity + steady HGL (Q={0:0.0} cfs) -> {1}", hglData.DesignFlowCfs, reportPath));
            OfferOpenReport(ed, reportPath);
        }

        private static bool TryBuildHydraulicReport(
            Document doc,
            Editor ed,
            out List<ReadPipe> pipes,
            out Dictionary<ObjectId, Manning.CapacityResult> capacities,
            out HglReportData hglData,
            out CapacityReportData capacityData,
            out string drawingName)
        {
            CivilDocument civilDoc = CivilApplication.ActiveDocument;
            pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            capacities = new Dictionary<ObjectId, Manning.CapacityResult>();

            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                hglData = new HglReportData();
                capacityData = new CapacityReportData();
                drawingName = "";
                return false;
            }

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            bool useMinorLosses = PromptYesNo(ed, "\nInclude HEC-22 junction/exit losses in HGL section", defaultYes: true);

            foreach (var rp in pipes)
            {
                try
                {
                    capacities[rp.PipeId] = Manning.Capacity(rp.Segment);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nSkipping {rp.PipeName}: {ex.Message}");
                }
            }

            hglData = BuildHglReportData(pipes, flow, useMinorLosses);
            capacityData = CapacityReportBuilder.Build(
                pipes, flow.DesignFlowCfs, flow.PipeFlowCfs);

            drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            if (string.IsNullOrWhiteSpace(drawingName))
                drawingName = "untitled";
            return true;
        }

        private static HglReportData BuildHglReportData(
            IReadOnlyList<ReadPipe> pipes, DesignFlowContext flow, bool useMinorLosses)
        {
            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = useMinorLosses,
                IncludeExitLoss = useMinorLosses,
                UseBendLoss = useMinorLosses,
            };

            var report = new HglReportData
            {
                DesignFlowCfs = flow.DesignFlowCfs,
                IsRouted = flow.IsRouted,
                IncludeMinorLosses = useMinorLosses,
            };

            foreach (var net in NetworkTopology.BuildOrderedNetworks(pipes))
            {
                if (net.OrderedPipes.Count == 0) continue;

                List<NetworkReach> reaches = flow.IsRouted && flow.PipeFlowCfs != null
                    ? NetworkTopology.BuildReaches(net.OrderedPipes, flow.PipeFlowCfs, useMinorLosses)
                    : NetworkTopology.BuildReaches(net.OrderedPipes, flow.DesignFlowCfs, useMinorLosses);

                double tailwater = OutfallTailwaterFt(net);
                var profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwater, hglOptions);

                var netReport = new HglNetworkReport
                {
                    NetworkName = net.NetworkName,
                    StartHglFt = tailwater,
                };

                for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
                {
                    ReadPipe rp = net.OrderedPipes[i];
                    HglProfilePoint point = profile[i];
                    double hglUs = point.HglUpstreamFt;

                    netReport.Rows.Add(new HglPipeReportRow
                    {
                        PipeName = rp.PipeName,
                        DesignFlowCfs = reaches[i].FlowCfs,
                        HglUsFt = hglUs,
                        HglDsFt = point.HglFt,
                        Point = point,
                        IsSurcharged = Hgl.IsSurcharged(
                            hglUs, point.HglFt,
                            rp.UpstreamInvertFt, rp.DownstreamInvertFt, rp.Segment.DiameterFt),
                        RelativeDepth = reaches[i].RelativeDepth,
                        FlowSurcharged = reaches[i].FlowSurcharged,
                    });
                }

                report.Networks.Add(netReport);
            }

            return report;
        }

        [CommandMethod("HC_RATIONAL")]
        public void RationalMethod()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found in this drawing.\n");
                return;
            }

            Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, doc.Database);
            Rational.PeakFlowResult q;
            double systemTc = 0.0;
            foreach (var cm in catchments) systemTc = Math.Max(systemTc, cm.TcMinutes);

            if (resolution != null)
            {
                q = resolution.PeakFromCatchments(catchments);
                string keySuffix = resolution.PresetKey != null ? $" ({resolution.PresetKey})" : "";
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  IDF: {0}{1} [{2}, {3}-yr]\n",
                    resolution.DisplayLabel, keySuffix, resolution.SourceLabel, resolution.ReturnPeriodYears));
            }
            else
            {
                IdfCurve idf = IdfPrompts.PromptCustomIdfCurve(ed);
                var intensity = idf.Intensity(systemTc);
                q = Rational.Peak(catchments, intensity.IntensityInHr);
            }

            ed.WriteMessage($"\n--- HydroComplete: Rational method ({catchments.Count} catchments) ---");
            foreach (var cm in catchments)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-20} A={1,7:0.000} ac  C={2,4:0.00}  Tc={3,5:0.0} min",
                    Trim(cm.Name, 20), cm.AreaAcres, cm.RunoffC, cm.TcMinutes));

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  System Tc = {0:0.0} min  ->  i = {1:0.000} in/hr", systemTc, q.IntensityInHr));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Composite C = {0:0.000}   Total area = {1:0.000} ac", q.CompositeC, q.TotalAreaAcres));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  PEAK FLOW Q = {0:0.00} cfs   (Q = C*i*A)\n", q.PeakFlowCfs));
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }

        private static void OfferOpenReport(Editor ed, string reportPath)
        {
            if (!PromptYesNo(ed, "\nOpen report now?", defaultYes: true))
            {
                ed.WriteMessage("\n");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Could not open report: {ex.Message}");
            }

            ed.WriteMessage("\n");
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

        /// <summary>
        /// Default outfall tailwater = the downstream invert of the most-downstream
        /// (outfall) pipe â€” i.e. a free outfall at the pipe invert. The HGL profile
        /// is anchored here and stepped upstream.
        /// </summary>
        private static double OutfallTailwaterFt(NetworkTopology.OrderedNetwork net)
        {
            return net.OrderedPipes.Count > 0
                ? net.OrderedPipes[net.OrderedPipes.Count - 1].DownstreamInvertFt
                : 0.0;
        }

        private static double PromptTailwater(Editor ed, NetworkTopology.OrderedNetwork net)
        {
            double outfallInvert = OutfallTailwaterFt(net);
            var opts = new PromptDoubleOptions(
                $"\nOutfall tailwater HGL elevation for '{net.NetworkName}'")
            {
                DefaultValue = outfallInvert,
                UseDefaultValue = true,
                AllowNegative = true,
                AllowZero = true,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : outfallInvert;
        }
    }
}
