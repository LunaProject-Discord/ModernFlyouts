using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>
    /// "Run at startup": the package's StartupTask when installed as MSIX,
    /// otherwise the per-user Run registry key (unpackaged development builds).
    /// </summary>
    internal static class StartupRegistration
    {
        private const string TaskId = "ModernFlyoutsStartupId";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ModernFlyouts.WinUI";

        public static bool IsPackaged { get; } = DetectPackaged();

        private static bool DetectPackaged()
        {
            try
            {
                _ = Package.Current.Id;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static async Task<bool> IsEnabledAsync()
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) != null;
        }

        /// <summary>Applies the setting and returns the resulting state (the user or policy may refuse).</summary>
        public static async Task<bool> SetEnabledAsync(bool enabled)
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (enabled)
                {
                    var state = await task.RequestEnableAsync();
                    return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                }

                task.Disable();
                return task.State is StartupTaskState.EnabledByPolicy;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            return enabled;
        }
    }
}
