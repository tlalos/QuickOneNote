using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace QuickOneNote;

internal static class Program
{
    private static void RunWgcTest(string[] args)
    {
        string outPath = Path.Combine(Path.GetTempPath(), "quickonenote_wgctest.png");
        int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
        if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];

        var log = new System.Text.StringBuilder();
        try
        {
            var bounds = Screen.PrimaryScreen!.Bounds;
            using var bmp = GraphicsCapture.CaptureArea(bounds);
            bmp.Save(outPath, ImageFormat.Png);

            // Sample a grid of pixels to see whether the image is all-black (capture failed).
            long nonBlack = 0, sampled = 0;
            for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 40))
                for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 40))
                {
                    var c = bmp.GetPixel(x, y);
                    sampled++;
                    if (c.R > 8 || c.G > 8 || c.B > 8) nonBlack++;
                }

            log.AppendLine($"Saved {bmp.Width}x{bmp.Height} to {outPath}");
            log.AppendLine($"Non-black samples: {nonBlack}/{sampled}");
            log.AppendLine(nonBlack > 0 ? "RESULT: OK (real image)" : "RESULT: BLACK (capture failed)");
        }
        catch (Exception ex)
        {
            log.AppendLine("RESULT: ERROR " + ex);
        }

        File.WriteAllText(Path.ChangeExtension(outPath, ".txt"), log.ToString());
    }

    private static void RunUpdateTest(string[] args)
    {
        string outPath = Path.Combine(Path.GetTempPath(), "quickonenote_updatetest.txt");
        int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
        if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];

        var log = new System.Text.StringBuilder();
        try
        {
            var s = AppSettings.Load();
            string repo = string.IsNullOrWhiteSpace(s.UpdateRepo) ? "tlalos/QuickOneNote" : s.UpdateRepo!;
            log.AppendLine($"repo = {repo}; current = v{TrayApplicationContext.AppVersion}");
            var rel = AppUpdater.CheckLatestAsync(repo, s.UpdateToken ?? "", includePrerelease: false,
                assetPrefix: "quickonenote-update", currentVersion: TrayApplicationContext.AppVersion)
                .GetAwaiter().GetResult();
            log.AppendLine($"Available = {rel.Available}");
            log.AppendLine($"Version   = {rel.Version}");
            log.AppendLine($"Asset     = {rel.AssetName}");
            log.AppendLine($"AssetUrl  = {rel.AssetUrl}");
        }
        catch (Exception ex)
        {
            log.AppendLine("ERROR " + ex);
        }
        File.WriteAllText(outPath, log.ToString());
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // Update helper: when relaunched as the detached helper, swap the installed files and exit
        // BEFORE any WinForms/UI or the single-instance mutex. (See SelfUpdate.)
        if (args.Length > 0 && string.Equals(args[0], "apply-update", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(SelfUpdate.ApplyUpdate(args));
            return;
        }

        ApplicationConfiguration.Initialize();


        // Hidden live self-test: enumerate sections and append a test note (text + image),
        // writing the outcome to a file. Used for verification; no GUI.
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest(args);
            return;
        }

        // Hidden copy self-test: verify the simulated Ctrl+C pipeline against a real window.
        if (args.Any(a => string.Equals(a, "--copytest", StringComparison.OrdinalIgnoreCase)))
        {
            RunCopyTest(args);
            return;
        }

        // Hidden target window used by --copytest (a separate process with its own message loop).
        if (args.Any(a => string.Equals(a, "--copytarget", StringComparison.OrdinalIgnoreCase)))
        {
            RunCopyTarget(args);
            return;
        }

        // Hidden cloud test: prove the Graph backend is used (decode token appid, list sections, append).
        if (args.Any(a => string.Equals(a, "--cloudtest", StringComparison.OrdinalIgnoreCase)))
        {
            RunCloudTest(args);
            return;
        }

        // Hidden capture test: grab the primary screen via Windows.Graphics.Capture, save a PNG,
        // and report whether it is a real (non-black) image.
        if (args.Any(a => string.Equals(a, "--wgctest", StringComparison.OrdinalIgnoreCase)))
        {
            RunWgcTest(args);
            return;
        }

        // Hidden update test: query GitHub Releases and report whether a newer version is available.
        if (args.Any(a => string.Equals(a, "--updatetest", StringComparison.OrdinalIgnoreCase)))
        {
            RunUpdateTest(args);
            return;
        }

        // Hidden series test: build 2 captioned images and AppendSeries via the configured backend.
        if (args.Any(a => string.Equals(a, "--seriestest", StringComparison.OrdinalIgnoreCase)))
        {
            string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickonenote_seriestest.txt");
            int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
            if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];
            var log = new System.Text.StringBuilder();
            try
            {
                var settings = AppSettings.Load();
                log.AppendLine($"Backend = {settings.Backend}; Section = {settings.SectionName}");
                var items = new List<SeriesItem>
                {
                    new("First screenshot caption", MakeTestImage()),
                    new("Second screenshot caption", MakeTestImage()),
                };
                string title = "Series self-test " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                log.AppendLine($"Appending series '{title}' with {items.Count} items…");
                OneNoteBackends.For(settings).AppendSeries(settings, title, items);
                log.AppendLine("RESULT: SUCCESS — titled series appended.");
            }
            catch (Exception ex) { log.AppendLine("RESULT: FAIL — " + ex.GetType().Name + ": " + ex.Message); }
            finally { try { File.WriteAllText(outPath, log.ToString()); } catch { } }
            return;
        }

        // Hidden OCR test: render known text, run local OCR, report the recognised text.
        if (args.Any(a => string.Equals(a, "--ocrtest", StringComparison.OrdinalIgnoreCase)))
        {
            string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickonenote_ocrtest.txt");
            int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
            if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];
            try
            {
                const string sample = "The quick brown fox 12345";
                using var bmp = new Bitmap(640, 120);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    using var f = new Font("Segoe UI", 30, FontStyle.Regular);
                    g.DrawString(sample, f, Brushes.Black, 12, 30);
                }
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                string recognized = Task.Run(() => OcrService.RecognizeAsync(ms.ToArray())).GetAwaiter().GetResult();
                File.WriteAllText(outPath, $"Expected: {sample}\nRecognised: {recognized}\n");
            }
            catch (Exception ex) { File.WriteAllText(outPath, "OCR FAIL: " + ex.Message); }
            return;
        }

        // Hidden read test: report what the clipboard-only path (Ctrl+Shift+I) would capture.
        if (args.Any(a => string.Equals(a, "--readtest", StringComparison.OrdinalIgnoreCase)))
        {
            string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickonenote_readtest.txt");
            int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
            if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];
            var c = ClipboardCapture.CaptureClipboard();
            File.WriteAllText(outPath, $"text={c.Text ?? "(null)"}\nimageBytes={(c.PngImage?.Length ?? 0)}\n");
            return;
        }

        // One-shot mode: invoked with file paths (e.g. from the optional Explorer right-click
        // entry). Send each file to OneNote and exit without showing the tray icon.
        var files = args.Where(a => !string.IsNullOrWhiteSpace(a) && File.Exists(a)).ToArray();
        if (files.Length > 0)
        {
            RunFileCapture(files);
            return;
        }

        // Single instance: if QuickOneNote is already running, exit silently so the hotkeys and
        // tray icon aren't duplicated.
        using var mutex = new System.Threading.Mutex(true, @"Local\QuickOneNote_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        Application.Run(new TrayApplicationContext());
    }

    private static void RunSelfTest(string[] args)
    {
        // Optional output path: --out <path>
        string outPath = Path.Combine(Path.GetTempPath(), "quickonenote_selftest.txt");
        int outIdx = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
        if (outIdx >= 0 && outIdx + 1 < args.Length) outPath = args[outIdx + 1];

        var log = new System.Text.StringBuilder();
        void Line(string s) => log.AppendLine(s);

        try
        {
            var client = new OneNoteClient();

            Line("== QuickOneNote self-test ==");
            Line("Enumerating sections…");
            var sections = client.GetSections();
            Line($"Sections found: {sections.Count}");
            foreach (var s in sections.Take(10))
                Line("  - " + s.DisplayPath);

            if (sections.Count == 0)
            {
                Line("RESULT: FAIL — no sections. Is a notebook open in desktop OneNote?");
                return;
            }

            // Prefer the user's configured section; otherwise the first one found.
            var saved = AppSettings.Load();
            var target = sections.FirstOrDefault(s => s.Id == saved.SectionId) ?? sections[0];
            Line($"Target section: {target.DisplayPath}");

            // Build a test payload: text + a small generated PNG.
            byte[] png = MakeTestImage();
            var content = new CapturedContent(
                $"QuickOneNote self-test at {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                png);

            var testSettings = new AppSettings
            {
                SectionId = target.Id,
                SectionName = target.DisplayPath,
                Mode = CaptureMode.TodaysPage,
                ShowNotifications = false,
            };

            Line("Appending test note (text + image)…");
            client.Append(testSettings, content);
            Line("RESULT: SUCCESS — note appended to today's page in the target section.");
        }
        catch (Exception ex)
        {
            Line("RESULT: FAIL — " + ex.GetType().Name + ": " + ex.Message);
            Line(ex.ToString());
        }
        finally
        {
            try { File.WriteAllText(outPath, log.ToString()); } catch { /* ignore */ }
        }
    }

    private static void RunCopyTarget(string[] args)
    {
        int idx = Array.FindIndex(args, a => string.Equals(a, "--copytarget", StringComparison.OrdinalIgnoreCase));
        string marker = (idx >= 0 && idx + 1 < args.Length) ? args[idx + 1] : "MARKER";

        var form = new Form
        {
            Text = "QuickOneNote copy target",
            Width = 460,
            Height = 220,
            TopMost = true,
            StartPosition = FormStartPosition.CenterScreen,
        };
        var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = marker };
        form.Controls.Add(tb);
        form.Shown += (_, _) => { form.Activate(); tb.Focus(); tb.SelectAll(); };
        Application.Run(form);
    }

    private static void RunCopyTest(string[] args)
    {
        string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickonenote_copytest.txt");
        int outIdx = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
        if (outIdx >= 0 && outIdx + 1 < args.Length) outPath = args[outIdx + 1];

        var log = new System.Text.StringBuilder();
        void Line(string s) => log.AppendLine(s);
        System.Diagnostics.Process? target = null;
        try
        {
            string marker = "QON_COPYTEST_" + DateTime.Now.Ticks;
            try { Clipboard.SetText("SENTINEL_BEFORE"); } catch { }

            Line("== QuickOneNote copy self-test ==");
            Line("Launching target window (separate process)…");
            string exe = Environment.ProcessPath!;
            target = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--copytarget {marker}") { UseShellExecute = false });

            // Wait for the target window to appear and become foreground.
            IntPtr handle = IntPtr.Zero;
            for (int i = 0; i < 30 && handle == IntPtr.Zero; i++)
            {
                System.Threading.Thread.Sleep(100);
                target!.Refresh();
                handle = target.MainWindowHandle;
            }
            Line("Target window handle: " + (handle == IntPtr.Zero ? "NOT FOUND" : handle.ToString()));
            if (handle != IntPtr.Zero) NativeMethods.SetForegroundWindow(handle);
            System.Threading.Thread.Sleep(500);

            Line("Selecting all (Ctrl+A) and capturing (simulated Ctrl+C)…");
            NativeMethods.SendSelectAll();
            System.Threading.Thread.Sleep(250);

            // CaptureSelection blocks this thread, but the target is a separate process that
            // pumps its own messages — exactly like the real app talking to another app.
            var content = ClipboardCapture.CaptureSelection();
            string got = content.Text ?? "(null)";
            Line("Captured text: " + (got.Length > 60 ? got[..60] + "…" : got));

            bool ok = content.Text != null && content.Text.Contains(marker);
            Line(ok ? "RESULT: SUCCESS — simulated Ctrl+C copied the selection."
                    : "RESULT: FAIL — the copy did not capture the target text.");

            string after = "";
            try { after = Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)"; } catch { }
            Line("Clipboard after (should be restored to SENTINEL_BEFORE): " + after);
        }
        catch (Exception ex)
        {
            Line("RESULT: FAIL — " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { if (target is { HasExited: false }) target.Kill(); } catch { }
            try { File.WriteAllText(outPath, log.ToString()); } catch { }
        }
    }

    private static void RunCloudTest(string[] args)
    {
        string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickonenote_cloudtest.txt");
        int oi = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
        if (oi >= 0 && oi + 1 < args.Length) outPath = args[oi + 1];

        var log = new System.Text.StringBuilder();
        void Line(string s) => log.AppendLine(s);
        try
        {
            var settings = AppSettings.Load();
            Line("== QuickOneNote CLOUD (Graph) verification ==");
            Line($"Saved backend        : {settings.Backend} ({(settings.Backend == BackendKind.Cloud ? "Cloud/Graph" : "Local/COM")})");
            Line($"Client ID (your app) : {settings.GraphClientId}");
            Line($"Target section       : {settings.SectionName}");
            Line($"Section ID           : {settings.SectionId}");
            Line("");

            if (settings.Backend != BackendKind.Cloud)
                Line("NOTE: saved backend is not Cloud, but running the Graph path anyway to verify.");

            var auth = new GraphAuth(settings.GraphClientId ?? "");

            // 1) Get a real access token and decode it — proves it came from YOUR registration.
            string token = Task.Run(auth.GetAccessTokenAsync).GetAwaiter().GetResult();
            Line("--- Access token claims (decoded from the JWT Microsoft issued) ---");
            Line($"aud (audience)   : {JwtClaim(token, "aud")}   <- should be Microsoft Graph");
            Line($"appid (this app) : {JwtClaim(token, "appid")}   <- should equal your Client ID");
            Line($"scp (permissions): {JwtClaim(token, "scp")}   <- should include Notes.ReadWrite");
            Line("");

            // 2) List sections via graph.microsoft.com.
            var backend = new GraphOneNoteBackend(auth);
            var sections = backend.GetSections();
            Line($"GET https://graph.microsoft.com/v1.0/me/onenote/sections -> {sections.Count} section(s):");
            foreach (var s in sections.Take(10)) Line("   - " + s.DisplayPath);
            Line("");

            // 3) Append a real note (text + image) via Graph.
            var content = new CapturedContent($"QuickOneNote CLOUD self-test at {DateTime.Now:yyyy-MM-dd HH:mm:ss}", MakeTestImage());
            backend.Append(settings, content);
            Line("POST/PATCH https://graph.microsoft.com/... -> appended a test note to your cloud OneNote.");
            Line("");
            Line("RESULT: SUCCESS — QuickOneNote is talking to the Microsoft Graph cloud API using your app registration.");
        }
        catch (Exception ex)
        {
            Line("RESULT: FAIL — " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { File.WriteAllText(outPath, log.ToString()); } catch { }
        }
    }

    /// <summary>Decode a single claim from a JWT's payload (no signature validation — display only).</summary>
    private static string JwtClaim(string jwt, string claim)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return "(not a JWT)";
            string p = parts[1].Replace('-', '+').Replace('_', '/');
            switch (p.Length % 4) { case 2: p += "=="; break; case 3: p += "="; break; }
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p));
            var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
            return root.TryGetProperty(claim, out var v) ? (v.GetString() ?? v.ToString()) : "(absent)";
        }
        catch { return "(could not decode)"; }
    }

    private static byte[] MakeTestImage()
    {
        using var bmp = new Bitmap(260, 70);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, 250, 205)); // light yellow
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            g.DrawString("QuickOneNote test image", font, Brushes.Black, 8, 8);
            using var font2 = new Font("Segoe UI", 9);
            g.DrawString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), font2, Brushes.DimGray, 8, 36);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void RunFileCapture(string[] files)
    {
        var settings = AppSettings.Load();
        if (!settings.IsConfigured)
        {
            MessageBox.Show(
                "Open QuickOneNote from the tray and choose a OneNote section first.",
                "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var client = OneNoteBackends.For(settings);
        int ok = 0;
        try
        {
            foreach (var file in files)
            {
                var content = ClipboardCapture.CaptureFromFile(file);
                if (!content.IsEmpty)
                {
                    client.Append(settings, content);
                    ok++;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't add to OneNote: " + ex.Message,
                "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ok > 0 && settings.ShowNotifications)
        {
            MessageBox.Show($"Added {ok} item(s) to OneNote.",
                "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
