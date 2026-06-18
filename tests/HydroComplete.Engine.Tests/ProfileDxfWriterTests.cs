using System;
using System.IO;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ProfileDxfWriterTests
    {
        [Fact]
        public void WriteToString_IncludesLayersPolylinesAndStationLabels()
        {
            var data = SampleData();
            var options = new ProfileDxfWriter.ProfileDxfOptions
            {
                DatumElevationFt = 890.0,
                HorizontalScale = 10.0,
                VerticalScale = 10.0,
                IncludeHgl = true,
            };

            string dxf = ProfileDxfWriter.WriteToString(data, options);

            Assert.Contains("LWPOLYLINE", dxf, StringComparison.Ordinal);
            Assert.Contains(ProfileDxfWriter.InvertLayer, dxf, StringComparison.Ordinal);
            Assert.Contains(ProfileDxfWriter.CrownLayer, dxf, StringComparison.Ordinal);
            Assert.Contains(ProfileDxfWriter.HglLayer, dxf, StringComparison.Ordinal);
            Assert.Contains("MH-1", dxf, StringComparison.Ordinal);
            Assert.Contains("STA 0.0", dxf, StringComparison.Ordinal);
        }

        [Fact]
        public void WriteToString_ScalesChainageAndElevation()
        {
            var data = new ProfileDxfWriter.ProfileDxfData();
            data.InvertPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 100.0, ElevationFt = 900.0 });
            data.InvertPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 200.0, ElevationFt = 899.0 });

            var options = new ProfileDxfWriter.ProfileDxfOptions
            {
                OriginX = 5.0,
                OriginY = 10.0,
                DatumElevationFt = 890.0,
                HorizontalScale = 20.0,
                VerticalScale = 10.0,
            };

            string dxf = ProfileDxfWriter.WriteToString(data, options);

            // x = 5 + 100/20 = 10, y = 10 + (900-890)/10 = 11 (DXF group codes 10/20)
            Assert.Contains("10\n10\n20\n11\n", dxf, StringComparison.Ordinal);
        }

        [Fact]
        public void Write_FileRoundTrip_WritesAsciiDxf()
        {
            string path = Path.Combine(Path.GetTempPath(), "hc-profile-" + Guid.NewGuid().ToString("N") + ".dxf");
            try
            {
                ProfileDxfWriter.Write(path, SampleData(), new ProfileDxfWriter.ProfileDxfOptions
                {
                    DatumElevationFt = 890.0,
                });

                Assert.True(File.Exists(path));
                string text = File.ReadAllText(path);
                Assert.StartsWith("0\nSECTION", text, StringComparison.Ordinal);
                Assert.Contains("0\nEOF\n", text, StringComparison.Ordinal);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static ProfileDxfWriter.ProfileDxfData SampleData()
        {
            var data = new ProfileDxfWriter.ProfileDxfData { NetworkName = "Storm" };
            data.InvertPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 0, ElevationFt = 900 });
            data.InvertPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 120, ElevationFt = 899.5 });
            data.CrownPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 0, ElevationFt = 902.5 });
            data.CrownPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 120, ElevationFt = 902 });
            data.HglPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 0, ElevationFt = 901 });
            data.HglPoints.Add(new ProfileDxfWriter.ProfilePoint { ChainageFt = 120, ElevationFt = 900.2 });
            data.Stations.Add(new ProfileDxfWriter.ProfileStation
            {
                ChainageFt = 0,
                StructureName = "MH-1",
                InvertFt = 900,
                CrownFt = 902.5,
                HglFt = 901,
            });
            return data;
        }
    }
}