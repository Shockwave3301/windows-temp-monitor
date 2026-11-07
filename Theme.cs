using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TempMonitor
{
    internal enum Status { Unknown, Normal, Warm, Hot }

    /// <summary>Colours for the whole UI. Follows the Windows light/dark app mode and High Contrast.</summary>
    internal sealed class Theme
    {
        public bool Dark;
        public Color Background, Card, CardBorder, Text, SubText, Track, Hover, Accent, AccentText;
        public Color NoticeBack, NoticeBorder;
        public Color Normal, Warm, Hot, Unknown;

        /// <param name="forceDark">Overrides the Windows light/dark setting (not High Contrast) when set.</param>
        public static Theme Current(bool? forceDark)
        {
            if (SystemInformation.HighContrast) return HighContrast();
            bool dark = forceDark.HasValue ? forceDark.Value : IsDarkMode();
            return dark ? DarkTheme() : LightTheme();
        }

        public Color For(Status s)
        {
            switch (s)
            {
                case Status.Normal: return Normal;
                case Status.Warm: return Warm;
                case Status.Hot: return Hot;
                default: return Unknown;
            }
        }

        /// <summary>A soft background tint of a colour, for icon tiles and pills.</summary>
        public Color Tint(Color c)
        {
            return Blend(c, Card, Dark ? 0.18f : 0.12f);
        }

        public static Color Blend(Color fg, Color bg, float amount)
        {
            return Color.FromArgb(
                (int)(bg.R + (fg.R - bg.R) * amount),
                (int)(bg.G + (fg.G - bg.G) * amount),
                (int)(bg.B + (fg.B - bg.B) * amount));
        }

        private static Theme LightTheme()
        {
            return new Theme
            {
                Dark = false,
                Background = Hex(0xF3F3F3), Card = Hex(0xFFFFFF), CardBorder = Hex(0xE5E5E5),
                Text = Hex(0x1B1B1B), SubText = Hex(0x616161), Track = Hex(0xEBEBEB), Hover = Hex(0xF0F0F0),
                Accent = Hex(0x005FB8), AccentText = Color.White,
                NoticeBack = Hex(0xFFF4CE), NoticeBorder = Hex(0xF1DCA3),
                Normal = Hex(0x0F7B0F), Warm = Hex(0xB65B00), Hot = Hex(0xC42B1C), Unknown = Hex(0x8A8A8A),
            };
        }

        private static Theme DarkTheme()
        {
            return new Theme
            {
                Dark = true,
                Background = Hex(0x202020), Card = Hex(0x2B2B2B), CardBorder = Hex(0x3A3A3A),
                Text = Hex(0xFFFFFF), SubText = Hex(0xA8A8A8), Track = Hex(0x3D3D3D), Hover = Hex(0x353535),
                Accent = Hex(0x60CDFF), AccentText = Hex(0x002A3D),
                NoticeBack = Hex(0x433519), NoticeBorder = Hex(0x5C4A22),
                Normal = Hex(0x6CCB5F), Warm = Hex(0xFFB547), Hot = Hex(0xFF6B5E), Unknown = Hex(0x8A8A8A),
            };
        }

        private static Theme HighContrast()
        {
            return new Theme
            {
                Dark = SystemColors.Window.GetBrightness() < 0.5f,
                Background = SystemColors.Window, Card = SystemColors.Window, CardBorder = SystemColors.WindowText,
                Text = SystemColors.WindowText, SubText = SystemColors.WindowText, Track = SystemColors.GrayText,
                Hover = SystemColors.Highlight, Accent = SystemColors.Highlight, AccentText = SystemColors.HighlightText,
                NoticeBack = SystemColors.Window, NoticeBorder = SystemColors.WindowText,
                Normal = SystemColors.WindowText, Warm = SystemColors.WindowText, Hot = SystemColors.WindowText,
                Unknown = SystemColors.GrayText,
            };
        }

        private static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        private static bool IsDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch
            {
                return false;
            }
        }

        // ---- native window theming ----

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        /// <summary>Dark or light title bar (Windows 10 1809+ and Windows 11).</summary>
        public static void ApplyTitleBar(IntPtr hwnd, bool dark)
        {
            int value = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)     // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));          // same, pre-20H1 builds
            }
            catch { }
        }

        /// <summary>Dark or light scrollbars for a control.</summary>
        public static void ApplyScrollbars(IntPtr hwnd, bool dark)
        {
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }
}
