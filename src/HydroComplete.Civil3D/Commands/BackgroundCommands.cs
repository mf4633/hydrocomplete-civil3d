using System;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HydroComplete.Civil3D.Writing;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_BACKGROUND — attach georeferenced image on HC-BACKGROUND layer.</summary>
    public sealed class BackgroundCommands
    {
        [CommandMethod("HC_BACKGROUND")]
        public void AttachBackground()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            string defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            var pathOpts = new PromptStringOptions("\nBackground image file path")
            {
                DefaultValue = defaultFolder,
                UseDefaultValue = false,
                AllowSpaces = true,
            };
            PromptResult pathRes = ed.GetString(pathOpts);
            if (pathRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pathRes.StringResult))
            {
                ed.WriteMessage("\nBackground attach cancelled.\n");
                return;
            }

            string imagePath = pathRes.StringResult.Trim();
            if (!File.Exists(imagePath))
            {
                ed.WriteMessage("\nFile not found: " + imagePath + "\n");
                return;
            }

            var ptOpts = new PromptPointOptions("\nInsertion point (lower-left)")
            {
                AllowNone = false,
            };
            PromptPointResult ptRes = ed.GetPoint(ptOpts);
            if (ptRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nBackground attach cancelled.\n");
                return;
            }

            double width = PromptDouble(ed, "\nImage width in drawing units (ft)", 1000.0);
            BackgroundImageWriter.AttachResult attach = BackgroundImageWriter.AttachImage(
                doc.Database, imagePath, ptRes.Value, width);

            if (!attach.Success)
            {
                ed.WriteMessage("\nBackground attach failed: " + attach.Error + "\n");
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: background image ---\n  File: {0}\n  Layer: {1}\n  Width: {2:0.##} drawing units\n",
                imagePath,
                BackgroundImageWriter.LayerName,
                width));
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

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}