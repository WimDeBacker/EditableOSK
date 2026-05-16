// SvgIconLoader.cs — loads SVG files from the icons directory, tints them,
// and caches the result as GDI+ Bitmaps for use in toolbar buttons.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Svg;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Loads SVG icon files from the <c>icons</c> subfolder next to the executable,
    /// replaces the near-black primary stroke colour with a requested tint colour,
    /// renders the result to a <see cref="Bitmap"/>, and caches it so each
    /// unique (filename, tint, size) triple is only processed once per session.
    ///
    /// <para>
    /// All SVG icons in the icons directory use <c>#1a1a1c</c> as their primary
    /// stroke colour (near-black, for light backgrounds).  This loader swaps that
    /// colour before rendering so the same icon files work on both light and dark
    /// backgrounds — pass <see cref="Color.White"/> for the dark toolbar, or
    /// <see cref="Fluent.TextPrimary"/> for a light dialog.
    /// </para>
    /// <para>
    /// Accent colours (<c>#2b7ec9</c> blue for "insert" indicators,
    /// <c>#bf534e</c> red for "delete" indicators) are intentionally left
    /// unchanged so the visual meaning of each icon is preserved.
    /// </para>
    /// </summary>
    internal static class SvgIconLoader
    {
        // Cache key: filename + tint ARGB + pixel size → rendered Bitmap.
        private static readonly Dictionary<(string, int, int), Bitmap> _cache = new();

        // Absolute path to the icons directory (next to the executable).
        private static readonly string _dir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");

        /// <summary>
        /// Returns a <paramref name="size"/>×<paramref name="size"/> pixel
        /// <see cref="Bitmap"/> for the named SVG file, with the primary stroke
        /// colour replaced by <paramref name="tint"/>.
        /// </summary>
        /// <param name="filename">
        /// Filename inside the icons directory, e.g. <c>"save.svg"</c>.
        /// </param>
        /// <param name="tint">
        /// Colour to substitute for the SVG's primary near-black stroke (<c>#1a1a1c</c>).
        /// Pass <see cref="Color.White"/> for dark toolbar buttons.
        /// </param>
        /// <param name="size">Width and height in pixels of the returned bitmap.</param>
        /// <returns>
        /// A cached <see cref="Bitmap"/>, or <c>null</c> if the file is missing
        /// or the SVG cannot be parsed.
        /// </returns>
        internal static Bitmap Load(string filename, Color tint, int size = 24)
        {
            var key = (filename, tint.ToArgb(), size);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            string path = Path.Combine(_dir, filename);
            if (!File.Exists(path)) return null;

            try
            {
                // Replace the primary near-black stroke with the requested tint colour.
                // A simple string replace works reliably here because every icon in the
                // directory uses #1a1a1c literally as an attribute value.  Accent colours
                // (#2b7ec9 blue, #bf534e red) are left as-is.
                string hex = $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}";
                string svg = File.ReadAllText(path, Encoding.UTF8)
                                 .Replace("#1a1a1c", hex, StringComparison.OrdinalIgnoreCase);

                // Parse the modified SVG XML from memory.
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
                var doc = SvgDocument.Open<SvgDocument>(stream);

                // Render to a bitmap at the requested pixel dimensions.
                // Svg.NET respects the viewBox attribute and scales the content to fit.
                Bitmap bmp = doc.Draw(size, size);

                _cache[key] = bmp;
                return bmp;
            }
            catch
            {
                // If SVG parsing or rendering fails (e.g. unsupported feature),
                // return null so the caller can fall back to a glyph or text label.
                return null;
            }
        }
    }
}
