using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace TempMonitor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // TempMonitor.exe --dump <file>  : write one snapshot of all temperatures to a text file and exit.
            if (args.Length >= 2 && args[0] == "--dump")
                return Dump(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return 1;
            }
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show(
                "Temp Monitor hit an unexpected error. Press Ctrl+C to copy this message if you want to report it.\n\n" + ex,
                "Temp Monitor " + MainForm.AppVersion, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static int Dump(string path)
        {
            var sb = new StringBuilder();
            int exitCode = 0;
            try
            {
                sb.AppendLine("Temp Monitor " + MainForm.AppVersion + " - " + DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
                sb.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " x64" : ""));
                sb.AppendLine("PawnIO installed: " + (PawnIo.IsInstalled ? "yes" : "no"));
                sb.AppendLine();

                Computer computer = Sensors.CreateComputer();
                computer.Open();
                List<Reading> readings = null;
                for (int i = 0; i < 2; i++) // second pass fills sensors that need two samples
                {
                    readings = Sensors.ReadAll(computer);
                    Thread.Sleep(500);
                }
                computer.Close();

                string lastGroup = null;
                foreach (Reading r in readings)
                {
                    if (r.GroupName != lastGroup) { sb.AppendLine("[" + r.HwType + "] " + r.GroupName); lastGroup = r.GroupName; }
                    sb.AppendLine("    " + r.Name + " = " + (r.Value.HasValue ? r.Value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " C" : "n/a"));
                }
                sb.AppendLine(readings.Count + " temperature sensors");
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex);
                exitCode = 1;
            }
            File.WriteAllText(path, sb.ToString());
            return exitCode;
        }
    }
}
