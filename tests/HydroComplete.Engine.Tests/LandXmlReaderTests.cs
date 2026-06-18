using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class LandXmlReaderTests
    {
        [Fact]
        public void Parse_RoundTripFromWriter_PreservesPipeAndStructureData()
        {
            string xml = LandXmlWriter.WriteToString(SamplePipes(), SampleStructures(), "RoundTripProject");
            LandXmlImportResult result = ParseString(xml);

            Assert.Empty(result.Errors);
            Assert.Equal("RoundTripProject", result.ProjectName);
            Assert.Single(result.Pipes);

            LandXmlPipeRecord pipe = result.Pipes[0];
            Assert.Equal("Pipe-12", pipe.Name);
            Assert.Equal("Storm", pipe.NetworkName);
            Assert.Equal(120.0, pipe.LengthFt, 3);
            Assert.Equal(2.5, pipe.DiameterFt, 3);
            Assert.Equal(0.004, pipe.Slope, 6);
            Assert.Equal(0.013, pipe.ManningN, 6);
            Assert.Equal("MH-1", pipe.StartStructureName);
            Assert.Equal("MH-2", pipe.EndStructureName);
            Assert.Equal(900.0, pipe.StartInvertFt, 3);
            Assert.Equal(899.5, pipe.EndInvertFt, 3);

            Assert.Equal(2, result.Structures.Count);
            LandXmlStructureRecord mh1 = result.Structures.Single(s => s.Name == "MH-1");
            Assert.Equal(905.5, mh1.RimFt!.Value, 3);
            Assert.Equal(900.0, mh1.InvertFt!.Value, 3);
            Assert.Equal(1000.0, mh1.NorthingFt!.Value, 3);
            Assert.Equal(2000.0, mh1.EastingFt!.Value, 3);
        }

        [Fact]
        public void Parse_FilePath_ReadsExistingExport()
        {
            string path = Path.Combine(Path.GetTempPath(), "hc-landxml-read-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                LandXmlWriter.Write(path, SamplePipes(), SampleStructures(), "FileProject");

                LandXmlImportResult result = LandXmlReader.Parse(path);

                Assert.Empty(result.Errors);
                Assert.Equal("FileProject", result.ProjectName);
                Assert.Single(result.Pipes);
                Assert.Equal(2, result.Structures.Count);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void Parse_BoxPipe_UsesEquivalentDiameter()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<LandXML xmlns=""http://www.landxml.org/schema/LandXML-1.2"" version=""1.2"">
  <Project name=""BoxProject"">
    <PipeNetworks>
      <PipeNetwork name=""Storm"" pipeNetType=""storm"">
        <Structs />
        <Pipes>
          <Pipe name=""Box-1"" refStart=""MH-1"" refEnd=""MH-2"" slope=""0.005"">
            <BoxPipe width=""4"" height=""2"" manningsN=""0.014"" length=""80"" />
          </Pipe>
        </Pipes>
      </PipeNetwork>
    </PipeNetworks>
  </Project>
</LandXML>";

            LandXmlImportResult result = ParseString(xml);

            Assert.Empty(result.Errors);
            Assert.Single(result.Pipes);
            Assert.Equal(2.667, result.Pipes[0].DiameterFt, 3);
            Assert.Equal(80.0, result.Pipes[0].LengthFt, 3);
            Assert.Equal(0.014, result.Pipes[0].ManningN, 6);
        }

        [Fact]
        public void Parse_ToleratesMissingOptionalFields()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<LandXML xmlns=""http://www.landxml.org/schema/LandXML-1.2"" version=""1.2"">
  <Project>
    <PipeNetworks>
      <PipeNetwork name=""Sparse"" pipeNetType=""storm"">
        <Structs>
          <Struct name=""MH-A"" />
        </Structs>
        <Pipes>
          <Pipe name=""P1"" refStart=""MH-A"" refEnd=""MH-B"">
            <CircPipe diameter=""3"" />
          </Pipe>
        </Pipes>
      </PipeNetwork>
    </PipeNetworks>
  </Project>
</LandXML>";

            LandXmlImportResult result = ParseString(xml);

            Assert.Empty(result.Errors);
            Assert.Equal("", result.ProjectName);
            Assert.Single(result.Pipes);
            Assert.Equal(3.0, result.Pipes[0].DiameterFt, 3);
            Assert.Equal(0.0, result.Pipes[0].Slope);
            Assert.Equal(0.0, result.Pipes[0].LengthFt);
            Assert.Equal(0.013, result.Pipes[0].ManningN, 6);

            Assert.Single(result.Structures);
            Assert.Null(result.Structures[0].RimFt);
            Assert.Null(result.Structures[0].NorthingFt);
        }

        [Fact]
        public void Parse_MissingFile_AddsError()
        {
            string path = Path.Combine(Path.GetTempPath(), "hc-missing-" + Guid.NewGuid().ToString("N") + ".xml");

            LandXmlImportResult result = LandXmlReader.Parse(path);

            Assert.Single(result.Errors);
            Assert.Contains("File not found", result.Errors[0], StringComparison.Ordinal);
            Assert.Empty(result.Pipes);
        }

        [Fact]
        public void Parse_InvalidXml_AddsError()
        {
            LandXmlImportResult result = ParseString("<LandXML><unclosed>");

            Assert.Single(result.Errors);
            Assert.Contains("Invalid XML", result.Errors[0], StringComparison.Ordinal);
        }

        private static LandXmlImportResult ParseString(string xml)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return LandXmlReader.Parse(stream);
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