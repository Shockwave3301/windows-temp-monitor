using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace TempMonitor
{
    /// <summary>
    /// PawnIO is the signed kernel driver LibreHardwareMonitor uses to read CPU and motherboard
    /// sensors. It is not bundled; when it's missing the app offers to download the official
    /// installer, pinned to an exact version and checksum, and run it.
    /// </summary>
    internal static class PawnIo
    {
        public const string Version = "2.2.0";
        public const string Website = "https://pawnio.eu";
        private const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        private const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

        public static bool IsInstalled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
                        return key != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Downloads the installer, verifies its SHA-256, runs it and waits for it to exit.
        /// Returns the installer's exit code. Throws if the download or the checksum fails.
        /// </summary>
        public static int DownloadAndInstall()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            byte[] data;
            using (var web = new WebClient())
            {
                web.Headers.Add(HttpRequestHeader.UserAgent, "TempMonitor");
                data = web.DownloadData(InstallerUrl);
            }

            string dir = Path.Combine(Path.GetTempPath(), "TempMonitor");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "PawnIO_setup_" + Version + ".exe");
            File.WriteAllBytes(file, data);

            try
            {
                // Re-open the file so no other process can modify it, then verify what is on disk
                // (not what was downloaded) and run it while the handle still blocks writes.
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    string hash;
                    using (SHA256 sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(locked)).Replace("-", "");

                    if (!hash.Equals(InstallerSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded PawnIO installer did not match the expected checksum, so it was not run.");

                    using (Process p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }))
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }
    }
}
