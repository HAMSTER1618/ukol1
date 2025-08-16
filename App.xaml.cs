using System.Configuration;
using System.Data;
using System.Windows;
using System;
using System.IO;
using System.Diagnostics;

namespace ukol1
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.SetData("DataDirectory", baseDir);

            var scriptDir = Path.Combine(baseDir, "Scripts");
            var dbFile = Path.Combine(scriptDir, "KNIHOVNA2.fdb");
            var batFile = Path.Combine(scriptDir, "run_init_db.bat");
            var sqlFile = Path.Combine(scriptDir, "init_db.sql");

            //kontrola
            if (!Directory.Exists(scriptDir))
            {
                MessageBox.Show($"Scripts folder not found:\n{scriptDir}",
                    "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }
            else if (!File.Exists(batFile))
            {
                MessageBox.Show($"Setup script missing (run_init_db.bat):\n{batFile}",
                    "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }
            else if (!File.Exists(sqlFile))
            {
                MessageBox.Show($"Schema file missing (init_db.sql):\n{sqlFile}",
                    "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            if (!File.Exists(dbFile))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batFile}\"\"",
                    WorkingDirectory = scriptDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    MessageBox.Show("Failed to start run_init_db.bat.",
                        "Database init", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return;
                }

                proc.WaitForExit();

                //kontrola udeleni BD
                if (proc.ExitCode != 0 || !File.Exists(dbFile))
                {
                    MessageBox.Show($"Database initialization failed (exit code {proc.ExitCode}).",
                        "Database init", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return;
                }
            }

            new MainWindow().Show();
        }
    }
}