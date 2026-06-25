using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HydroComplete.Civil3D.DagHost
{
    /// <summary>
    /// AutoCAD dockable PaletteSet hosting the HydroComplete DAG editor (Rust WASM)
    /// via a WinForms WebView2 control.
    /// </summary>
    public sealed class DagPaletteSet : PaletteSet
    {
        private WebView2? _webView;
        private bool _webViewReady;

        public event Func<string, string, Task>? OnRunRequested;

        public DagPaletteSet()
            : base("HydroComplete Model Builder", "HC_DAG_PANEL",
                   new Guid("8b3f2a1e-4c5d-4e6f-b7a8-9c0d1e2f3a4b"))
        {
            MinimumSize = new System.Drawing.Size(800, 480);
            Dock      = DockSides.Left | DockSides.Right | DockSides.Bottom | DockSides.None;
            DockEnabled = DockSides.Left | DockSides.Right | DockSides.Bottom | DockSides.None;
            Style = PaletteSetStyles.ShowAutoHideButton
                  | PaletteSetStyles.ShowCloseButton
                  | PaletteSetStyles.Snappable;

            var wv = new WebView2 { Dock = DockStyle.Fill };
            _webView = wv;

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(wv);

            wv.HandleCreated += async (_, _) =>
            {
                await Task.Delay(50);
                await InitWebViewAsync(wv);
            };

            Add("Model Builder", panel);
        }

        private async Task InitWebViewAsync(WebView2 wv)
        {
            try
            {
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HydroComplete", "WebView2");
                Directory.CreateDirectory(dataDir);

                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
                await wv.EnsureCoreWebView2Async(env);

                wv.CoreWebView2.WebMessageReceived += OnWebMessage;
                wv.CoreWebView2.Settings.IsWebMessageEnabled = true;

                string dagFolder = ResolveDagFolder();
                string indexPath = Path.Combine(dagFolder, "index.html");
                if (Directory.Exists(dagFolder) && File.Exists(indexPath))
                {
                    // file:// blocks ES-module + WASM loads in WebView2; virtual host serves correct MIME types.
                    const string vhost = "hydrocomplete.app";
                    wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        vhost,
                        dagFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                    wv.CoreWebView2.Navigate($"https://{vhost}/index.html");
                }
                else
                {
                    wv.CoreWebView2.Navigate(
                        $"data:text/html,{Uri.EscapeDataString(FallbackHtml(indexPath))}");
                }

                _webViewReady = true;
            }
            catch (Exception ex)
            {
                try
                {
                    wv.CoreWebView2.NavigateToString(
                        $"<pre style='color:red'>WebView2 init failed:\n{ex}</pre>");
                }
                catch { }
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;
                using JsonDocument doc = JsonDocument.Parse(raw);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("type", out JsonElement typeEl)) return;
                if (typeEl.GetString() != "HC_RUN") return;
                string dagJson = root.TryGetProperty("dag",   out JsonElement d) ? d.GetRawText() : "{}";
                string ordJson = root.TryGetProperty("order", out JsonElement o) ? o.GetRawText() : "[]";
                OnRunRequested?.Invoke(dagJson, ordJson);
            }
            catch { }
        }

        /// Retrieves the current DAG JSON from the editor.
        public async Task<string> GetDagJsonAsync()
        {
            if (_webView == null || !_webViewReady) return "{}";
            try
            {
                // ExecuteScriptAsync returns the JS value JSON-encoded; a string gets double-quoted.
                string raw = await _webView.ExecuteScriptAsync("editor.to_json()");
                // Unescape: raw is a JSON-encoded string → deserialize it
                return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "{}";
            }
            catch { return "{}"; }
        }

        /// Send a seed DAG to the editor (e.g. auto-populated from drawing data).
        public async Task SeedDagAsync(string dagJson)
        {
            if (_webView == null || !_webViewReady) return;
            // Escape for JS string — use JSON.stringify round-trip via script
            string escaped = dagJson.Replace("\\", "\\\\").Replace("'", "\\'");
            string script  = $"if(typeof editor!=='undefined')editor.from_json('{escaped}'),editor.zoom_fit()";
            await _webView.ExecuteScriptAsync(script);
        }

        public async Task SendResultAsync(string resultDagJson)
        {
            if (_webView == null || !_webViewReady) return;
            string script = $"window.postMessage({{type:'HC_RESULT',dag:{resultDagJson}}}, '*')";
            await _webView.ExecuteScriptAsync(script);
        }

        private static string ResolveDagFolder()
        {
            string dll = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string bundled = Path.Combine(dll, "dag");
            if (Directory.Exists(bundled) && File.Exists(Path.Combine(bundled, "index.html")))
                return bundled;

            string dev = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "dev", "hydrocomplete-dag", "www");
            if (Directory.Exists(dev) && File.Exists(Path.Combine(dev, "index.html")))
                return dev;

            return bundled;
        }

        private static string FallbackHtml(string path) =>
            $"<html><body style='font-family:Segoe UI;padding:20px;color:#c00'>" +
            $"<h3>DAG editor not found</h3><p>Expected: {path}</p>" +
            $"<p>Run: <code>wasm-pack build --target web --out-dir www/pkg</code> " +
            $"in <code>~/dev/hydrocomplete-dag/</code></p></body></html>";
    }
}
