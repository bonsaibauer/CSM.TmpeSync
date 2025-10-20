using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace CSM.TmpeSync.Util
{
    internal static class DebugGuiManager
    {
        private static readonly object Sync = new object();
        private static bool _initialised;
        private static bool _subscribed;
        private static Process _viewerProcess;
        private static string _viewerScriptPath;

        internal static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialised)
                    return;

                MultiplayerStateObserver.RoleChanged += OnRoleChanged;
                _subscribed = true;
                _initialised = true;

                if (Log.IsDebugEnabled && CsmCompat.IsServerInstance())
                    EnsureViewerStarted();
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (_subscribed)
                {
                    MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
                    _subscribed = false;
                }

                StopViewer();
                _initialised = false;
            }
        }

        private static void OnRoleChanged(string role)
        {
            if (!Log.IsDebugEnabled)
            {
                StopViewer();
                return;
            }

            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
            {
                EnsureViewerStarted();
            }
            else
            {
                StopViewer();
            }
        }

        private static void EnsureViewerStarted()
        {
            lock (Sync)
            {
                if (!Log.IsDebugEnabled)
                {
                    StopViewer();
                    return;
                }

                if (_viewerProcess != null && !_viewerProcess.HasExited)
                    return;

                var scriptPath = EnsureViewerScript();
                if (string.IsNullOrEmpty(scriptPath))
                {
                    Log.Warn(LogCategory.Diagnostics, "Debug log viewer unavailable | reason=script_initialisation_failed");
                    return;
                }

                var logPath = Log.GetCurrentLogFilePath();
                if (string.IsNullOrEmpty(logPath))
                {
                    Log.Warn(LogCategory.Diagnostics, "Debug log viewer unavailable | reason=log_file_missing path={0}", logPath ?? "<null>");
                    return;
                }

                foreach (var candidate in new[] { "pythonw", "python", "python3" })
                {
                    try
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = candidate,
                            Arguments = string.Format(CultureInfo.InvariantCulture, "{0} {1}", Quote(scriptPath), Quote(logPath)),
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        _viewerProcess = Process.Start(startInfo);
                        if (_viewerProcess != null && !_viewerProcess.HasExited)
                        {
                            Log.Info(LogCategory.Diagnostics, "Debug log viewer started | python={0} logPath={1}", candidate, logPath);
                            return;
                        }
                    }
                    catch (Win32Exception)
                    {
                        // try next candidate
                    }
                    catch (FileNotFoundException)
                    {
                        // try next candidate
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Diagnostics, "Debug log viewer launch failed | python={0} error={1}", candidate, ex);
                    }
                }

                Log.Warn(LogCategory.Diagnostics, "Debug log viewer could not be started | reason=python_executable_missing");
                _viewerProcess = null;
            }
        }

        private static string EnsureViewerScript()
        {
            if (!string.IsNullOrEmpty(_viewerScriptPath) && File.Exists(_viewerScriptPath))
                return _viewerScriptPath;

            var directory = Log.GetDataDirectory();
            if (string.IsNullOrEmpty(directory))
                return null;

            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "tmpe_log_viewer.py");
                File.WriteAllText(path, ViewerScript, Encoding.UTF8);
                _viewerScriptPath = path;
                return path;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to prepare debug log viewer script | error={0}", ex);
                return null;
            }
        }

        private static void StopViewer()
        {
            if (_viewerProcess == null)
                return;

            try
            {
                if (!_viewerProcess.HasExited)
                {
                    if (_viewerProcess.CloseMainWindow())
                    {
                        if (!_viewerProcess.WaitForExit(2000))
                            _viewerProcess.Kill();
                    }
                    else
                    {
                        _viewerProcess.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, "Failed to stop debug log viewer | error={0}", ex);
            }
            finally
            {
                try
                {
                    _viewerProcess.Dispose();
                }
                catch
                {
                    // ignore disposal errors
                }

                _viewerProcess = null;
            }
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private const string ViewerScript = @"import io
import os
import sys
import tkinter as tk
from tkinter import ttk

REFRESH_MS = 500

class LogViewer(tk.Tk):
    def __init__(self, log_path):
        super().__init__()
        self.title('CSM TM:PE Sync Log Viewer')
        self.geometry('900x600')
        self.log_path = log_path

        self.columnconfigure(0, weight=1)
        self.rowconfigure(0, weight=1)

        frame = ttk.Frame(self)
        frame.grid(row=0, column=0, sticky='nsew')
        frame.columnconfigure(0, weight=1)
        frame.rowconfigure(0, weight=1)

        self.text = tk.Text(frame, wrap='none')
        self.text.configure(state='disabled')
        vsb = ttk.Scrollbar(frame, orient='vertical', command=self.text.yview)
        hsb = ttk.Scrollbar(frame, orient='horizontal', command=self.text.xview)
        self.text.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        self.text.grid(row=0, column=0, sticky='nsew')
        vsb.grid(row=0, column=1, sticky='ns')
        hsb.grid(row=1, column=0, sticky='ew')

        self.status = ttk.Label(frame, anchor='w')
        self.status.grid(row=2, column=0, columnspan=2, sticky='ew')

        self.after(0, self.refresh)

    def refresh(self):
        try:
            with io.open(self.log_path, 'r', encoding='utf-8', errors='replace') as handle:
                contents = handle.read()
            status_text = os.path.abspath(self.log_path)
        except OSError as exc:
            contents = f'Unable to read log file: {exc}'
            status_text = 'Waiting for log file...'

        self.text.configure(state='normal')
        self.text.delete('1.0', tk.END)
        self.text.insert(tk.END, contents)
        self.text.configure(state='disabled')
        self.text.see(tk.END)
        self.status.configure(text=status_text)
        self.after(REFRESH_MS, self.refresh)


def main():
    if len(sys.argv) < 2:
        print('Usage: python tmpe_log_viewer.py <log_file>')
        return 1

    path = sys.argv[1]
    app = LogViewer(path)
    app.mainloop()
    return 0


if __name__ == '__main__':
    sys.exit(main())
";
    }
}
