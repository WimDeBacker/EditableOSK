// MakeIco.cs  — run with: dotnet script MakeIco.cs
// Combines PNG files at 16 / 32 / 48 / 256 px into a single multi-resolution ICO.
//
// ICO format used: PNG-in-ICO (Windows Vista+).
// Each image frame stores raw PNG bytes verbatim; Windows decodes them at display time.
// The 256 px slot uses width/height = 0 in the directory entry, per the ICO spec.

using System;
using System.IO;

string root  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
int[]  sizes = { 16, 32, 48, 256 };

// Load each PNG into memory
var pngs = new byte[sizes.Length][];
for (int i = 0; i < sizes.Length; i++)
{
    string path = Path.Combine(root, $"icons/osk_tmp_{sizes[i]}.png");
    pngs[i] = File.ReadAllBytes(path);
    Console.WriteLine($"  Loaded {sizes[i]}px  ({pngs[i].Length} bytes)");
}

// Build the ICO binary
//   Header       :  6 bytes  (reserved, type=1, count)
//   Directory    : 16 bytes × count
//   Image data   : PNG bytes, contiguous
using var ms = new MemoryStream();
using var bw = new BinaryWriter(ms);

// ── ICONDIR header ────────────────────────────────────────────────────────
bw.Write((ushort)0);             // reserved — must be 0
bw.Write((ushort)1);             // type     — 1 = ICO
bw.Write((ushort)sizes.Length);  // number of images

// ── ICONDIRENTRY for each size ────────────────────────────────────────────
// Payload starts immediately after header + all directory entries.
int payloadOffset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int sz = sizes[i];
    bw.Write((byte)(sz == 256 ? 0 : sz));  // width  (0 encodes 256 per spec)
    bw.Write((byte)(sz == 256 ? 0 : sz));  // height (0 encodes 256 per spec)
    bw.Write((byte)0);                      // color count  (0 = no palette)
    bw.Write((byte)0);                      // reserved     (must be 0)
    bw.Write((ushort)1);                    // color planes
    bw.Write((ushort)32);                   // bits per pixel (32-bit RGBA)
    bw.Write((uint)pngs[i].Length);         // size of image data in bytes
    bw.Write((uint)payloadOffset);          // offset of image data from file start
    payloadOffset += pngs[i].Length;
}

// ── Image data ────────────────────────────────────────────────────────────
foreach (var png in pngs)
    bw.Write(png);

// Write ICO file
string icoPath = Path.Combine(root, "icons/onscreenkeyboard.ico");
File.WriteAllBytes(icoPath, ms.ToArray());
Console.WriteLine($"\nWrote {ms.Length} bytes → {icoPath}");

// Remove temp PNGs
foreach (int sz in sizes)
{
    string tmp = Path.Combine(root, $"icons/osk_tmp_{sz}.png");
    if (File.Exists(tmp)) { File.Delete(tmp); Console.WriteLine($"  Deleted temp {sz}px PNG"); }
}
