using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace QuickOneNote;

/// <summary>
/// Applies a downloaded update. A running exe can't overwrite its own file, so the swap is done by
/// a DETACHED helper launched from a COPY of the new build (the staged folder, which is a complete
/// self-contained copy with all its DLLs). Flow:
///   running app: download zip -> extract to %LOCALAPPDATA%\QuickOneNote\update\staged
///               -> spawn staged\QuickOneNote.exe apply-update ... -> EXIT
///   helper (from staged): wait for the old exe to unlock -> copy staged\* over the install dir
///               -> relaunch the installed exe.
/// </summary>
public static class SelfUpdate
{
    public const string ExeName = "QuickOneNote.exe";

    private static string InstallDir =>
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!;

    /// <summary>
    /// Downloads and stages the update, then launches the detached helper. The caller must then
    /// EXIT the app so the helper can swap the files. Returns false (with a message) if nothing was
    /// applied.
    /// </summary>
    public static async Task<bool> TryUpdateAsync(ReleaseInfo rel, string token,
        Action<string, double>? onProgress = null, Action<string>? onError = null, CancellationToken ct = default)
    {
        void Report(string phase, double pct) { try { onProgress?.Invoke(phase, pct); } catch { } }
        try
        {
            var updRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "QuickOneNote", "update");
            var zip = Path.Combine(updRoot, rel.AssetName);
            var staged = Path.Combine(updRoot, "staged");
            Directory.CreateDirectory(updRoot);
            if (Directory.Exists(staged)) Directory.Delete(staged, true);

            double last = -1;
            Report("downloading", 0);
            await AppUpdater.DownloadAssetAsync(rel.AssetUrl, token, zip, (done, total) =>
            {
                if (total <= 0) return;
                var pct = Math.Round(100.0 * done / total);
                if (pct != last) { last = pct; Report("downloading", pct); }
            }, ct).ConfigureAwait(false);

            Report("staging", 100);
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            ZipFile.ExtractToDirectory(zip, staged, overwriteFiles: true);

            var stagedExe = Path.Combine(staged, ExeName);
            if (!File.Exists(stagedExe)) { onError?.Invoke($"Update package has no {ExeName}."); return false; }

            // Run the helper FROM the staged folder: it's a complete self-contained copy (its DLLs
            // are alongside it) and lives OUTSIDE the install dir, so the install exe/DLLs are free
            // to be overwritten once this app exits. A lone exe copy cannot start in a multi-file
            // deployment.
            var installExe = Path.Combine(InstallDir, ExeName);
            var psi = new ProcessStartInfo(stagedExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = staged,
            };
            psi.ArgumentList.Add("apply-update");
            psi.ArgumentList.Add("--dir"); psi.ArgumentList.Add(InstallDir);
            psi.ArgumentList.Add("--staged"); psi.ArgumentList.Add(staged);
            psi.ArgumentList.Add("--relaunch"); psi.ArgumentList.Add(installExe);
            Process.Start(psi);

            Report("applying", 100);
            return true;   // caller shuts the app down now
        }
        catch (Exception ex) { onError?.Invoke(ex.Message); Report("error", 0); return false; }
    }

    /// <summary>
    /// Detached-helper entry (verb <c>apply-update</c>): copy staged files over the install dir and
    /// relaunch. Runs from the staged copy, so the install exe is free once the old app exits.
    /// </summary>
    public static int ApplyUpdate(string[] args)
    {
        string dir = GetOpt(args, "--dir") ?? "";
        string staged = GetOpt(args, "--staged") ?? "";
        string relaunch = GetOpt(args, "--relaunch") ?? "";
        string logPath = Path.Combine(dir, "update.log");
        void Log(string m) { try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} {m}{Environment.NewLine}"); } catch { } }

        try
        {
            var targetExe = Path.Combine(dir, ExeName);
            if (!WaitUnlocked(targetExe, TimeSpan.FromSeconds(30)))
                Log("warning: target exe still locked after 30s — copying anyway");

            CopyOver(staged, dir, Log);

            if (!string.IsNullOrEmpty(relaunch) && File.Exists(relaunch))
            {
                Log($"relaunching {relaunch}");
                Process.Start(new ProcessStartInfo(relaunch) { UseShellExecute = true, WorkingDirectory = dir });
            }
            Log("apply-update: done");
            return 0;
        }
        catch (Exception ex) { Log("apply-update FAILED: " + ex); return 1; }
    }

    private static bool WaitUnlocked(string file, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            try
            {
                if (!File.Exists(file)) return true;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;                       // got an exclusive handle → old app has exited
            }
            catch { System.Threading.Thread.Sleep(500); }
        }
        return false;
    }

    private static void CopyOver(string stagedDir, string installDir, Action<string> log)
    {
        foreach (var src in Directory.EnumerateFiles(stagedDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(stagedDir, src);
            var dst = Path.Combine(installDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(src, dst, overwrite: true); break; }
                catch when (attempt < 20) { System.Threading.Thread.Sleep(500); }   // retry if a file is briefly held
            }
        }
        log("files copied");
    }

    private static string? GetOpt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
