using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>Builds a custom eyedropper cursor (hotspot at the centre precision crosshair).</summary>
internal static class CursorFactory
{
    private static Cursor? _eyedropper;

    public static Cursor Eyedropper => _eyedropper ??= CreateEyedropper();

    private static Cursor CreateEyedropper()
    {
        try
        {
            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Barrel from the sampling point (centre 16,16) up to the bulb (white halo for
                // visibility on any background, then the dark barrel on top).
                using (var halo = new Pen(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(halo, 16, 16, 25, 7);
                using (var barrel = new Pen(Color.Black, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(barrel, 16, 16, 25, 7);

                using (var bulb = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    g.FillEllipse(bulb, 21, 2, 9, 9);
                using (var ring = new Pen(Color.White, 1.5f))
                    g.DrawEllipse(ring, 21, 2, 9, 9);

                // Precision crosshair centred on the hotspot.
                using (var cw = new Pen(Color.White, 3))
                {
                    g.DrawLine(cw, 9, 16, 23, 16);
                    g.DrawLine(cw, 16, 9, 16, 23);
                }
                using (var ck = new Pen(Color.Black, 1.2f))
                {
                    g.DrawLine(ck, 9, 16, 23, 16);
                    g.DrawLine(ck, 16, 9, 16, 23);
                }
            }

            // GetHicon gives an icon whose hotspot is the centre (16,16) — exactly our crosshair.
            // HICON and HCURSOR are interchangeable, so this becomes a cursor with a centre hotspot.
            return new Cursor(bmp.GetHicon());
        }
        catch
        {
            return Cursors.Cross;
        }
    }
}
