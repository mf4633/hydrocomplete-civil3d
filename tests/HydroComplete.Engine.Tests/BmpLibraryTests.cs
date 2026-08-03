using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class BmpLibraryTests
    {
        // HC_WQ_DIAGRAM builds its keyword list by resolving every BmpType const
        // eagerly; a const without a matching library entry crashes the command.
        [Fact]
        public void GetBmp_ResolvesEveryBmpTypeConst()
        {
            var consts = typeof(BmpType)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            Assert.NotEmpty(consts);
            foreach (string key in consts)
            {
                BmpDefinition bmp = BmpLibrary.GetBmp(key);
                Assert.Equal(key, bmp.Key);
                Assert.False(string.IsNullOrWhiteSpace(bmp.Name));
                foreach (string pollutant in Pollutant.Core)
                {
                    Assert.True(bmp.TrappingEfficiency.ContainsKey(pollutant),
                        $"{key} is missing a {pollutant} trapping efficiency.");
                }
            }
        }

        // AutoCAD keyword lists split on '/' and reject non-alphanumeric globals;
        // HC_WQ_DIAGRAM derives keywords from Name stripped of spaces/hyphens/parens.
        [Fact]
        public void BmpNames_YieldValidAutoCadKeywords()
        {
            foreach (BmpDefinition bmp in BmpLibrary.AllBmps.Values)
            {
                string keyword = bmp.Name
                    .Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                Assert.True(keyword.All(char.IsLetterOrDigit),
                    $"BMP '{bmp.Key}' name '{bmp.Name}' produces invalid keyword '{keyword}'.");
            }
        }

        [Fact]
        public void ApplyTreatmentTrain_NewBmpTypes_RemoveExpectedTssFraction()
        {
            var loads = new Dictionary<string, double>
            {
                [Pollutant.Tss] = 100.0,
                [Pollutant.Tn] = 10.0,
                [Pollutant.Tp] = 2.0,
            };

            var train = WaterQualityEngine.ApplyTreatmentTrain(
                loads, new[] { BmpType.ConstructedWetland });

            Assert.Equal(0.85 * 100.0, train.TotalRemovedLbs[Pollutant.Tss], 6);
            Assert.Equal(0.35 * 10.0, train.TotalRemovedLbs[Pollutant.Tn], 6);
            Assert.Equal(0.45 * 2.0, train.TotalRemovedLbs[Pollutant.Tp], 6);
        }

        [Fact]
        public void ApplyTreatmentTrain_CisternProvidesNoTreatmentCredit()
        {
            var loads = new Dictionary<string, double>
            {
                [Pollutant.Tss] = 100.0,
                [Pollutant.Tn] = 10.0,
                [Pollutant.Tp] = 2.0,
            };

            var train = WaterQualityEngine.ApplyTreatmentTrain(
                loads, new[] { BmpType.Cistern });

            Assert.Equal(0.0, train.TotalRemovedLbs[Pollutant.Tss], 6);
        }
    }
}
