using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(HydroComplete.TestSeed.SeedCommands))]

namespace HydroComplete.TestSeed
{
    /// <summary>
    /// HC_TEST_SEED_CATCHMENTS: creates a catchment group with three catchments
    /// attached to distinct structures of the first pipe network, with explicit
    /// runoff C, Tc, and reference-structure outlets. Test harness only.
    /// </summary>
    public sealed class SeedCommands
    {
        private sealed class SeedSpec
        {
            public string Name = "";
            public double SideFt;
            public double RunoffC;
            public double TcMinutes;
        }

        [CommandMethod("HC_TEST_SEED_CATCHMENTS")]
        public void SeedCatchments()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("No active drawing.");
            Editor ed = doc.Editor;
            try
            {
                SeedCatchmentsCore(doc, ed);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nHC_TEST_SEED_CATCHMENTS FAILED: {0}\n", ex);
            }
        }

        private static void SeedCatchmentsCore(Document doc, Editor ed)
        {
            Database db = doc.Database;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;
            ed.WriteMessage("\n  [seed] civilDoc ok");

            // Distinct areas/C/Tc so routed flows differ per catchment.
            var specs = new[]
            {
                new SeedSpec { Name = "HC-Test-A", SideFt = 360.0, RunoffC = 0.65, TcMinutes = 12.0 },
                new SeedSpec { Name = "HC-Test-B", SideFt = 300.0, RunoffC = 0.45, TcMinutes = 18.0 },
                new SeedSpec { Name = "HC-Test-C", SideFt = 420.0, RunoffC = 0.80, TcMinutes = 8.0 },
            };

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var structures = ReadNetworkStructures(civilDoc, tr);
                ed.WriteMessage("\n  [seed] structures: {0}", structures.Count);
                if (structures.Count < specs.Length)
                {
                    ed.WriteMessage(
                        "\nHC_TEST_SEED_CATCHMENTS: need at least {0} structures, found {1}.\n",
                        specs.Length, structures.Count);
                    return;
                }

                ObjectId groupId = CatchmentGroup.Create(db, "HC-Test");
                ed.WriteMessage("\n  [seed] group created");
                ObjectId styleId = ResolveCatchmentStyle(civilDoc);
                ed.WriteMessage("\n  [seed] style resolved");

                // Spread outlets across the network: first, middle, last structure.
                int[] pick = { 0, structures.Count / 2, structures.Count - 1 };

                for (int i = 0; i < specs.Length; i++)
                {
                    SeedSpec spec = specs[i];
                    (ObjectId structId, ObjectId networkId, Point3d pos) = structures[pick[i]];

                    // Square boundary offset from the structure so centroids stay
                    // nearest their own outlet even if the reference link is ignored.
                    double half = spec.SideFt / 2.0;
                    double cx = pos.X + 600.0 * (i + 1);
                    double cy = pos.Y + 600.0;
                    // Catchment.Create requires an explicitly closed polygon.
                    var boundary = new Point3dCollection
                    {
                        new Point3d(cx - half, cy - half, 0),
                        new Point3d(cx + half, cy - half, 0),
                        new Point3d(cx + half, cy + half, 0),
                        new Point3d(cx - half, cy + half, 0),
                        new Point3d(cx - half, cy - half, 0),
                    };

                    ed.WriteMessage("\n  [seed] creating {0}...", spec.Name);
                    ObjectId catchId = Catchment.Create(
                        spec.Name, styleId, groupId, ObjectId.Null, boundary);
                    ed.WriteMessage(" created");
                    var catchment = (Catchment)tr.GetObject(catchId, OpenMode.ForWrite);
                    catchment.RunoffCoefficient = spec.RunoffC;
                    catchment.TimeOfConcentrationCalculationMethod =
                        TimeOfConcentrationCalculationMethod.CalculationMethodUserDefined;
                    catchment.TimeOfConcentration = spec.TcMinutes;
                    try
                    {
                        // ReferencePipeNetworkId is read-only in 2026; the
                        // structure id implies its network.
                        catchment.ReferencePipeNetworkStructureId = structId;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\n  {0}: reference-structure link failed ({1}) - centroid heuristic will apply.",
                            spec.Name, ex.Message);
                    }

                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  Seeded {0}: {1:0.00} ac, C={2:0.00}, Tc={3:0.#} min",
                        spec.Name, catchment.Area2d / 43560.0, spec.RunoffC, spec.TcMinutes));
                }

                tr.Commit();
            }

            ed.WriteMessage("\n--- HC_TEST_SEED_CATCHMENTS: seeded 3 catchments in group HC-Test ---\n");
        }

        private static List<(ObjectId StructId, ObjectId NetworkId, Point3d Pos)> ReadNetworkStructures(
            CivilDocument civilDoc, Transaction tr)
        {
            var result = new List<(ObjectId, ObjectId, Point3d)>();
            foreach (ObjectId netId in civilDoc.GetPipeNetworkIds())
            {
                if (!(tr.GetObject(netId, OpenMode.ForRead) is Network network)) continue;
                foreach (ObjectId sid in network.GetStructureIds())
                {
                    if (!(tr.GetObject(sid, OpenMode.ForRead) is Structure structure)) continue;
                    result.Add((sid, netId, structure.Position));
                }
                if (result.Count > 0) break;
            }
            return result;
        }

        private static ObjectId ResolveCatchmentStyle(CivilDocument civilDoc)
        {
            var styles = civilDoc.Styles.CatchmentStyles;
            foreach (ObjectId sid in styles)
                return sid;
            return styles.Add("HC-Test");
        }
    }
}
