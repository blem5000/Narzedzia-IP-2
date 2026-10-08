using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    // Boot (silent) update flow, like _startup_update_check() in app.py:
    // before the DHCP prompt, check GitHub Releases. When a newer release
    // exists (and was not skipped), download it with a small cancellable
    // progress window and hand over to the GUI updater. Returns true when the
    // updater was launched - the caller must shut down immediately.
    //
    // IMPORTANT: TrySilentUpdate() is called on the UI thread and blocks it,
    // so the network check runs on a threadpool thread with
    // ConfigureAwait(false) all the way. Blocking the UI thread on an async
    // method that resumes on the UI context deadlocks the app (hang with no
    // window). The download dialog itself runs on the UI thread via
    // ShowDialog(), which pumps messages and is safe.
    public enum BootAction
    {
        StartApp,
        ShowDownloadDialog,
        DeferToMainDialog
    }

    public static class BootUpdate
    {
        public static bool SessionChecked { get; private set; }
        public static ReleaseInfo Pending { get; private set; }

        public static void ClearPending()
        {
            Pending = null;
        }

        // Czysta decyzja bez UI i bez sieci - w 100% testowalna.
        // info == null oznacza: brak odpowiedzi API / błąd / brak nowszej wersji.
        public static BootAction EvaluateBootAction(bool auto, ReleaseInfo info, string currentVersion, string skippedTag, bool updaterPresent)
        {
            if (!auto)
                return BootAction.StartApp;
            if (info == null)
                return BootAction.StartApp;
            bool newer;
            try { newer = Updater.IsNewer(info.Tag, currentVersion); }
            catch { newer = false; }
            if (!newer)
                return BootAction.StartApp;
            if (string.Equals(info.Tag, skippedTag, StringComparison.OrdinalIgnoreCase))
                return BootAction.StartApp; // user skipped this version
            if (!updaterPresent)
                return BootAction.DeferToMainDialog;
            return BootAction.ShowDownloadDialog;
        }

        public static bool TrySilentUpdate()
        {
            SessionChecked = true;
            Pending = null;

            bool auto;
            string skipped;
            try
            {
                auto = global::Narzedzia_IP_2.Properties.Settings.Default.UpdateAuto;
                skipped = global::Narzedzia_IP_2.Properties.Settings.Default.UpdateSkipped;
            }
            catch
            {
                auto = false;
                skipped = null;
            }

            ReleaseInfo info = null;
            try
            {
                info = Task.Run(() => FetchPendingUpdateAsync()).GetAwaiter().GetResult();
            }
            catch
            {
                info = null;
            }

            bool updaterPresent = File.Exists(Path.Combine(Updater.AppDir(), Updater.UpdaterExeName));

            switch (EvaluateBootAction(auto, info, Updater.CurrentVersion(), skipped, updaterPresent))
            {
                case BootAction.ShowDownloadDialog:
                    var dlg = new BootUpdateWindow(info);
                    bool? result = dlg.ShowDialog();
                    if (result == true)
                        return true; // installer launched
                    if (dlg.DownloadFailed)
                        Pending = info; // let the user retry from the main window
                    return false;

                case BootAction.DeferToMainDialog:
                    // Old install without the updater program: leave it to the
                    // post-startup dialog flow (which falls back to .bat).
                    Pending = info;
                    return false;

                default:
                    return false;
            }
        }

        // Pure network + compare. No UI touches, ConfigureAwait(false), so it
        // is safe to block on from the UI thread via Task.Run(...).GetResult().
        private static async Task<ReleaseInfo> FetchPendingUpdateAsync()
        {
            ReleaseInfo info;
            try
            {
                info = await Updater.FetchLatestReleaseAsync(10).ConfigureAwait(false);
            }
            catch
            {
                return null; // offline / API error -> start normally
            }

            bool newer;
            try { newer = Updater.IsNewer(info.Tag, null); }
            catch { newer = false; }
            return newer ? info : null;
        }
    }

    // Shared "downloaded -> install and exit" step, like _launch_installer().
    public static class UpdateFlow
    {
        public static void LaunchInstaller(string zipPath, string tag)
        {
            string exeName = Updater.ExeName();
            string bundled = Path.Combine(Updater.AppDir(), Updater.UpdaterExeName);
            if (File.Exists(bundled))
            {
                string staged = Updater.StageGuiUpdater(bundled);
                Updater.LaunchGuiUpdater(staged, zipPath, Updater.AppDir(), exeName,
                    System.Diagnostics.Process.GetCurrentProcess().Id, Updater.OwnStartUnix(), tag);
                return;
            }
            string bat = Updater.WriteApplyScript(zipPath, Updater.AppDir(), System.Diagnostics.Process.GetCurrentProcess().Id);
            Updater.LaunchApplyAndExit(bat);
        }

        public static void ExitForUpdate()
        {
            try { Application.Current.Shutdown(); }
            catch { }
            Environment.Exit(0);
        }
    }
}
