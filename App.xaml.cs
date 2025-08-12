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
            var dataDir = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.SetData("DataDirectory", AppDomain.CurrentDomain.BaseDirectory);

            var scriptDir = Path.Combine(dataDir, "Scripts");
            var dbFile = Path.Combine(scriptDir, "KNIHOVNA2.FDB");
            var batFile = Path.Combine(scriptDir, "run_init_db.bat");
            var sqlFile = Path.Combine(scriptDir, "init_db.sql");

            MessageBox.Show(
                $"Script folder exists: {Directory.Exists(scriptDir)}\n" +
                $"BAT exists: {File.Exists(batFile)}\n+" +
                $"SQL exists:{File.Exists(dbFile)}", "Debug Paths"
                );

            if (!File.Exists(dbFile) && File.Exists(batFile))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = batFile,
                    WorkingDirectory = scriptDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();  // чекаємо, поки батник відпрацює
                    MessageBox.Show($"BAT ExitCode = {proc.ExitCode}", "Debug BAT");
                }
                else
                {
                    MessageBox.Show("Не вдалося запустити setup_db.bat", "Debug BAT");
                }
            }
            var main = new MainWindow();
            main.Show();
        }
    }
}