using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class LandXmlWriterTests
    {
        [Fact]
        public void WriteToString_ContainsLandXml12Namespace()
        {
            string xml = LandXmlWriter.WriteToString(SamplePipes(), SampleStructures());

            Assert.Contains("http://www.landxml.org/schema/LandXML-1.2", xml);
            Assert.Contains("LandXML", xml);
        }

        [Fact]
        public void WriteToString_ContainsPipeNameAndDiameter()
        {
            string xml = LandXmlWriter.WriteToString(SamplePipes(), SampleStructures());

            Assert.Contains("Pipe-12", xml);
            Assert.Contains("diameter=\"2.5\"", xml);
        }

        [Fact]
        public void WriteToString_ProducesValidXml()
        {
            string xml = LandXmlWriter.WriteToString(SamplePipes(), SampleStructures());

            var document = new XmlDocument();
            Exception? parseError = Record.Exception(() => document.LoadXml(xml));
            Assert.Null(parseError);

            XmlNamespaceManager nsm = new XmlNamespaceManager(document.NameTable);
            nsm.AddNamespace("lx", "http://www.landxml.org/schema/LandXML-1.2");

            XmlNode? pipe = document.SelectSingleNode("//lx:Pipe[@name='Pipe-12']", nsm);
            Assert.NotNull(pipe);

            XmlNode? circPipe = pipe!.SelectSingleNode("lx:CircPipe", nsm);
            Assert.NotNull(circPipe);
            Assert.Equal("2.5", circPipe!.Attributes?["diameter"]?.Value);
        }

        [Fact]
        public void Write_WritesFileWithStructures()
        {
            string path = Path.Combine(Path.GetTempPath(), "hc-landxml-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                LandXmlWriter.Write(path, SamplePipes(), SampleStructures(), "TestProject");

                Assert.True(File.Exists(path));
                string xml = File.ReadAllText(path);
                Assert.Contains("elevRim=\"905.5\"", xml);
                Assert.Contains("name=\"MH-1\"", xml);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static List<LandXmlPipeRecord> SamplePipes()
        {
            return new List<LandXmlPipeRecord>
            {
                new LandXmlPipeRecord
                {
                    Name = "Pipe-12",
                    NetworkName = "Storm",
                    LengthFt = 120.0,
                    DiameterFt = 2.5,
                    Slope = 0.004,
                    StartInvertFt = 900.0,
                    EndInvertFt = 899.5,
                    ManningN = 0.013,
                    DesignFlowCfs = 12.5,
                    StartStructureName = "MH-1",
                    EndStructureName = "MH-2",
                },
            };
        }

        private static List<LandXmlStructureRecord> SampleStructures()
        {
            return new List<LandXmlStructureRecord>
            {
                new LandXmlStructureRecord
                {
                    Name = "MH-1",
                    NetworkName = "Storm",
                    RimFt = 905.5,
                    InvertFt = 900.0,
                    NorthingFt = 1000.0,
                    EastingFt = 2000.0,
                },
                new LandXmlStructureRecord
                {
                    Name = "MH-2",
                    NetworkName = "Storm",
                    RimFt = 904.8,
                    InvertFt = 899.5,
                    NorthingFt = 1120.0,
                    EastingFt = 2000.0,
                },
            };
        }
    }
}