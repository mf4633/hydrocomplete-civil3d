using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HydroComplete.Engine;
using Xunit;
using Xunit.Abstractions;

namespace HydroComplete.Engine.Tests
{
    /// <summary>Run manually to regenerate embedded preset coefficients from live PFDS.</summary>
    public class Atlas14PresetGenerator
    {
        private readonly ITestOutputHelper _output;

        public Atlas14PresetGenerator(ITestOutputHelper output) => _output = output;

        [Fact(Skip = "Manual preset regeneration from live NOAA PFDS")]
        public async Task EmitPresetSource()
        {
            var cities = new (string key, string name, string state, double lat, double lon)[]
            {
                ("charlotte-nc", "Charlotte, NC", "NC", 35.23, -80.84),
                ("raleigh-nc", "Raleigh, NC", "NC", 35.78, -78.64),
                ("asheville-nc", "Asheville, NC", "NC", 35.60, -82.55),
                ("atlanta-ga", "Atlanta, GA", "GA", 33.75, -84.39),
                ("washington-dc", "Washington, DC", "DC", 38.91, -77.04),
                ("philadelphia-pa", "Philadelphia, PA", "PA", 39.95, -75.17),
                ("new-york-ny", "New York, NY", "NY", 40.71, -74.01),
                ("boston-ma", "Boston, MA", "MA", 42.36, -71.06),
                ("chicago-il", "Chicago, IL", "IL", 41.88, -87.63),
                ("detroit-mi", "Detroit, MI", "MI", 42.33, -83.05),
                ("minneapolis-mn", "Minneapolis, MN", "MN", 44.98, -93.27),
                ("denver-co", "Denver, CO", "CO", 39.74, -104.99),
                ("dallas-tx", "Dallas, TX", "TX", 32.78, -96.80),
                ("houston-tx", "Houston, TX", "TX", 29.76, -95.37),
                ("miami-fl", "Miami, FL", "FL", 25.76, -80.19),
                ("phoenix-az", "Phoenix, AZ", "AZ", 33.45, -112.07),
                ("los-angeles-ca", "Los Angeles, CA", "CA", 34.05, -118.24),
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var sb = new StringBuilder();

            foreach (var city in cities)
            {
                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}?lat={1:0.####}&lon={2:0.####}&data=intensity&units=english&series=pds",
                    Atlas14Fetcher.DefaultPfdsIntensityUrl,
                    city.lat,
                    city.lon);
                string csv = await client.GetStringAsync(url).ConfigureAwait(false);
                var i10 = Atlas14Fetcher.ParseIntensitiesAtDuration(csv, 10.0);
                var fits = Atlas14Fetcher.ParseAndFitAll(csv, city.lat, city.lon);

                sb.Append("            new Preset(\"")
                    .Append(city.key).Append("\", \"")
                    .Append(city.name).Append("\", \"")
                    .Append(city.state).Append("\", ")
                    .Append(city.lat.ToString("0.##", CultureInfo.InvariantCulture)).Append(", ")
                    .Append(city.lon.ToString("0.##", CultureInfo.InvariantCulture)).Append(", ");

                foreach (int rp in Atlas14Fetcher.StandardReturnPeriods)
                {
                    Atlas14CacheEntry fit = fits[rp];
                    sb.Append(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.##}, {1:0.##}, {2:0.###}, ",
                        fit.A, fit.B, fit.C));
                }

                foreach (int rp in Atlas14Fetcher.StandardReturnPeriods)
                {
                    sb.Append(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.##}, ",
                        i10[rp]));
                }

                sb.Length -= 2;
                sb.AppendLine("),");
                await Task.Delay(300).ConfigureAwait(false);
            }

            _output.WriteLine(sb.ToString());
        }
    }
}