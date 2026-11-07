using System;
using System.Drawing;
using System.Globalization;
using Microsoft.Win32;

namespace TempMonitor
{
    /// <summary>User preferences, stored in HKEY_CURRENT_USER\Software\TempMonitor.</summary>
    internal sealed class Settings
    {
        private const string KeyPath = @"Software\TempMonitor";

        public bool Fahrenheit;
        public bool TopMost;
        public Rectangle? WindowBounds;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null) return s;
                    s.Fahrenheit = Convert.ToInt32(key.GetValue("Fahrenheit", 0)) != 0;
                    s.TopMost = Convert.ToInt32(key.GetValue("TopMost", 0)) != 0;
                    s.WindowBounds = ParseRect(key.GetValue("WindowBounds") as string);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key.SetValue("Fahrenheit", Fahrenheit ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TopMost", TopMost ? 1 : 0, RegistryValueKind.DWord);
                    if (WindowBounds.HasValue)
                    {
                        Rectangle r = WindowBounds.Value;
                        key.SetValue("WindowBounds", string.Join(",", r.X, r.Y, r.Width, r.Height));
                    }
                }
            }
            catch { }
        }

        private static Rectangle? ParseRect(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] p = text.Split(',');
            int x, y, w, h;
            if (p.Length == 4
                && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out w)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)
                && w > 0 && h > 0)
                return new Rectangle(x, y, w, h);
            return null;
        }
    }
}
