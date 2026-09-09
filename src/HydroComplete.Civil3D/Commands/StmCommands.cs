using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// HC_STM_IMPORT — read a Hydraflow Storm Sewers <c>.stm</c> project.
    ///
    /// Civil 3D cannot open these files, and Autodesk retired the Storm Sewers
    /// extension, so a firm with a decade of Hydraflow projects has no route
    /// into a modern drawing except retyping them. This command reads the file,
    /// reports what it found, and writes a LandXML 1.2 file that Civil 3D's own
    /// pipe network import will build a network from.
    ///
    /// It reads and writes files. It does not touch the drawing.
    ///
    /// The reader is checked against Hydraflow's own printed reports for two
    /// real projects; see HydraflowReferenceTests and VALIDATION-HYDRAFLOW.md
    /// for what matches, what does not, and why.
    /// </summary>
    public sealed class StmCommands
    {
        [CommandMethod("HC_STM_IMPORT")]
        public void ImportStm()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
            string drawingName = ReportWriterCommon.SanitizeFileName(
                Path.GetFileNameWithoutExtension(doc.Name));
            string defaultPath = Path.Combine(
                ReportWriterCommon.OutputFolder, drawingName + ".stm");

            string inputPath = PromptInputPath(ed, defaultPath);
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                ed.WriteMessage("\nStorm Sewers import cancelled.\n");
                return;
            }

            if (!File.Exists(inputPath))
            {
                ed.WriteMessage("\nNo such file: " + inputPath + "\n");
                return;
            }

            StmProject project;
            try
            {
                project = StmReader.Parse(inputPath);
            }
            catch (InvalidDataException ex)
            {
                // The usual cause is a .stm from a different Hydraflow product
                // (Express, Hydrographs) rather than Storm Sewers.
                ed.WriteMessage("\nNot a Storm Sewers project file: " + ex.Message + "\n");
                return;
            }
            catch (IOException ex)
            {
                ed.WriteMessage("\nCould not read " + inputPath + ": " + ex.Message + "\n");
                return;
            }

            ReportProject(ed, project, inputPath);
            ReportPipes(ed, project);
            ReportInlets(ed, project);
            CompareToDrawing(ed, doc, project);

            foreach (string warning in project.Warnings)
            {
                ed.WriteMessage("\n  ! " + warning);
            }

            if (project.Warnings.Count > 0)
            {
                ed.WriteMessage("\n");
            }

            WriteLandXml(ed, project, inputPath);
        }

        // ─────────────────────────────── report ───────────────────────────────

        private static void ReportProject(Editor ed, StmProject project, string inputPath)
        {
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: Hydraflow Storm Sewers import ---" +
                "\n  File:      {0}" +
                "\n  Project:   {1}" +
                "\n  Written by:{2}" +
                "\n  Units:     {3}",
                Path.GetFileName(inputPath),
                string.IsNullOrWhiteSpace(project.ProjectName) ? "(unnamed)" : project.ProjectName,
                project.Civil3DFormat
                    ? " Storm Sewers for AutoCAD Civil 3D"
                    : " Hydraflow Storm Sewers (standalone)",
                project.SiUnits ? "SI" : "US survey feet"));

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Storm:     {0}-year, i = {1:0.####} / (t + {2:0.####})^{3:0.######}" +
                "\n  Minimum Tc:{4,6:0.0} min" +
                "\n  Junction K:{5,6:0.00}",
                project.ReturnPeriodYears,
                project.IdfA,
                project.IdfB,
                project.IdfC,
                project.MinimumTcMinutes,
                project.JunctionK));

            // A tailwater of zero in the file means "not set", which is not the
            // same as a tailwater at elevation zero. Say which one this is.
            ed.WriteMessage(project.TailwaterFt.HasValue
                ? string.Format(CultureInfo.InvariantCulture,
                    "\n  Tailwater: {0:0.###} ft (starting HGL)", project.TailwaterFt.Value)
                : "\n  Tailwater: not set in the file");

            if (project.IdfCurves.Count > 1)
            {
                ed.WriteMessage("\n  Other storms in the file: " + string.Join(
                    ", ",
                    project.IdfCurves.Keys
                        .Where(y => y != project.ReturnPeriodYears)
                        .OrderBy(y => y)
                        .Select(y => y.ToString(CultureInfo.InvariantCulture) + "-yr")));
            }

            ed.WriteMessage("\n");
        }

        private static void ReportPipes(Editor ed, StmProject project)
        {
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Lines: {0}   Structures: {1}   Total length: {2:0.0} ft\n",
                project.Pipes.Count,
                project.Structures.Count,
                project.Pipes.Sum(p => p.LengthFt)));

            ed.WriteMessage("\n  Line              Size(ft)  Slope     Length(ft)  Inv up    Inv dn    Up         Dn");

            foreach (LandXmlPipeRecord pipe in project.Pipes)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-16} {1,8:0.00}  {2,8:0.#####}  {3,10:0.0}  {4,8:0.00}  {5,8:0.00}  {6,-10} {7,-10}",
                    Trim(pipe.Name, 16),
                    pipe.DiameterFt,
                    pipe.Slope,
                    pipe.LengthFt,
                    pipe.StartInvertFt,
                    pipe.EndInvertFt,
                    Trim(pipe.StartStructureName, 10),
                    Trim(pipe.EndStructureName, 10)));
            }

            ed.WriteMessage("\n");
        }

        private static void ReportInlets(Editor ed, StmProject project)
        {
            if (project.Inlets.Count == 0)
            {
                ed.WriteMessage("\n  No inlet data in this file (laid out but not assigned).\n");
                return;
            }

            ed.WriteMessage("\n  Inlet       Area(ac)   C     C*A    Inlet t(min)  Grate LxW(ft)  Sag");

            foreach (KeyValuePair<string, StmInlet> entry in project.Inlets.OrderBy(
                e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                StmInlet inlet = entry.Value;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-10} {1,8:0.00}  {2,5:0.00}  {3,5:0.00}  {4,12:0.0}  {5,6:0.0} x {6,-4:0.0}  {7}",
                    Trim(entry.Key, 10),
                    inlet.DrainageAreaAcres,
                    inlet.RunoffCoefficient,
                    inlet.DrainageAreaAcres * inlet.RunoffCoefficient,
                    inlet.InletTimeMinutes,
                    inlet.GrateLengthFt,
                    inlet.GrateWidthFt,
                    inlet.InSag ? "sag" : "on grade"));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Total C*A: {0:0.00} acres\n",
                project.Inlets.Values.Sum(i => i.DrainageAreaAcres * i.RunoffCoefficient)));
        }

        private static void CompareToDrawing(Editor ed, Document doc, StmProject project)
        {
            List<ReadPipe> drawingPipes;
            try
            {
                drawingPipes = PipeNetworkReader.ReadAll(doc.Database, CivilApplication.ActiveDocument)
                    .ToList();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // No Civil 3D document, or no networks to read. The import is
                // still useful; skip the comparison rather than fail the command.
                return;
            }

            if (drawingPipes.Count == 0)
            {
                ed.WriteMessage(
                    "\n  This drawing has no pipe networks yet. Import the LandXML " +
                    "written below to build one.\n");
                return;
            }

            var inFile = new HashSet<string>(
                project.Pipes.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var inDrawing = new HashSet<string>(
                drawingPipes.Select(p => p.PipeName), StringComparer.OrdinalIgnoreCase);

            int both = inFile.Count(inDrawing.Contains);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Drawing comparison: {0} lines in the file, {1} pipes in the drawing, " +
                "{2} names in both.\n",
                inFile.Count,
                inDrawing.Count,
                both));

            var fileOnly = inFile.Where(n => !inDrawing.Contains(n)).OrderBy(n => n).Take(10).ToList();
            if (fileOnly.Count > 0)
            {
                ed.WriteMessage("\n  In the file, not the drawing: " + string.Join(", ", fileOnly) + "\n");
            }
        }

        // ──────────────────────────── LandXML out ────────────────────────────

        private static void WriteLandXml(Editor ed, StmProject project, string inputPath)
        {
            if (project.Pipes.Count == 0)
            {
                ed.WriteMessage("\nNothing to write: the file carried no line data.\n");
                return;
            }

            string defaultOut = Path.Combine(
                ReportWriterCommon.OutputFolder,
                ReportWriterCommon.SanitizeFileName(
                    Path.GetFileNameWithoutExtension(inputPath)) + "_from_stm.xml");

            if (!PromptYesNo(ed, "\nWrite a LandXML for Civil 3D to import?", true))
            {
                ed.WriteMessage("\nNo LandXML written.\n");
                return;
            }

            string outputPath = PromptOutputPath(ed, defaultOut);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                ed.WriteMessage("\nNo LandXML written.\n");
                return;
            }

            try
            {
                LandXmlWriter.Write(
                    outputPath,
                    project.Pipes,
                    project.Structures,
                    string.IsNullOrWhiteSpace(project.ProjectName) ? "Storm Sewers import" : project.ProjectName);
            }
            catch (IOException ex)
            {
                ed.WriteMessage("\nCould not write " + outputPath + ": " + ex.Message + "\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Wrote {0} pipes and {1} structures to:\n    {2}" +
                "\n  Bring it in with Civil 3D's Insert tab > Import > LandXML." +
                "\n  Hydrology and inlet data do not travel in LandXML; the numbers" +
                "\n  printed above are the ones to re-enter or to run through HC_ commands.\n",
                project.Pipes.Count,
                project.Structures.Count,
                outputPath));
        }

        // ────────────────────────────── prompts ──────────────────────────────

        private static string PromptInputPath(Editor ed, string defaultPath)
        {
            var opts = new PromptStringOptions("\nHydraflow Storm Sewers .stm file path")
            {
                DefaultValue = defaultPath,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(res.StringResult) ? defaultPath : res.StringResult.Trim();
        }

        private static string PromptOutputPath(Editor ed, string defaultPath)
        {
            var opts = new PromptStringOptions("\nLandXML output file path")
            {
                DefaultValue = defaultPath,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(res.StringResult) ? defaultPath : res.StringResult.Trim();
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message) { AllowNone = true };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
            {
                return defaultYes;
            }

            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Length <= max ? value : value.Substring(0, max - 1) + "~";
        }

        private static Document Active()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                throw new InvalidOperationException("No active drawing.");
            }

            return doc;
        }
    }
}
