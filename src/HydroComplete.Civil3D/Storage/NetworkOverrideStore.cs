using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HydroComplete.Civil3D.Storage
{
    /// <summary>Persists per-drawing pipe design overrides (Q, Manning n) for the network editor.</summary>
    internal static class NetworkOverrideStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        internal sealed class PipeOverride
        {
            public string PipeKey { get; set; } = "";
            public string PipeName { get; set; } = "";
            public string NetworkName { get; set; } = "";
            public double? DesignFlowCfs { get; set; }
            public double? ManningN { get; set; }
            public string Notes { get; set; } = "";
        }

        internal sealed class OverrideFile
        {
            public List<PipeOverride> Pipes { get; set; } = new List<PipeOverride>();
        }

        public static string StoreFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HydroComplete",
                "overrides");

        public static List<PipeOverride> Load(string drawingPath)
        {
            string path = FilePathForDrawing(drawingPath);
            if (!File.Exists(path)) return new List<PipeOverride>();

            try
            {
                string json = File.ReadAllText(path);
                OverrideFile? file = JsonSerializer.Deserialize<OverrideFile>(json, JsonOptions);
                return file?.Pipes ?? new List<PipeOverride>();
            }
            catch
            {
                return new List<PipeOverride>();
            }
        }

        public static void Save(string drawingPath, IReadOnlyList<PipeOverride> pipes)
        {
            Directory.CreateDirectory(StoreFolder);
            var file = new OverrideFile();
            file.Pipes.AddRange(pipes);
            string json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(FilePathForDrawing(drawingPath), json, Encoding.UTF8);
        }

        public static string FilePathForDrawing(string drawingPath)
        {
            string key = string.IsNullOrWhiteSpace(drawingPath) ? "untitled" : drawingPath.Trim();
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
            string id = Convert.ToHexString(hash).Substring(0, 16);
            return Path.Combine(StoreFolder, $"overrides-{id}.json");
        }
    }
}