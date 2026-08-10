using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace QuickOneNote;

/// <summary>One drawable annotation on a snip (stroke, shape, text, blur, step badge).</summary>
internal abstract class Annotation
{
    /// <summary>Draw in image coordinates. <paramref name="baseImage"/> is the un-annotated snip
    /// (only the blur tool samples it).</summary>
    public abstract void Draw(Graphics g, Bitmap baseImage);

    public virtual RectangleF Bounds => RectangleF.Empty;
    public virtual bool HitTest(PointF p) => Bounds.Contains(p);
    public virtual void Move(float dx, float dy) { }

    /// <summary>Deep copy — used for the undo/redo snapshots.</summary>
    public abstract Annotation Clone();

    protected static RectangleF RectOf(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

internal sealed class StrokeAnnotation : Annotation
{
    public Color Color;
    public float Width;
    public List<PointF> Points = new();

    public override void Draw(Graphics g, Bitmap baseImage)
    {
        using var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (Points.Count == 1)
        {
            float r = Width / 2f;
            using var br = new SolidBrush(Color);
            g.FillEllipse(br, Points[0].X - r, Points[0].Y - r, Width, Width);
        }
        else if (Points.Count >= 2)
        {
            g.DrawLines(pen, Points.ToArray());
        }
    }

    public override RectangleF Bounds
    {
        get
        {
            if (Points.Count == 0) return RectangleF.Empty;
            float minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
            float maxX = Points.Max(p => p.X), maxY = Points.Max(p => p.Y);
            return RectangleF.Inflate(RectangleF.FromLTRB(minX, minY, maxX, maxY), Width, Width);
        }
    }

    public override void Move(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
    }

    public override Annotation Clone() => new StrokeAnnotation { Color = Color, Width = Width, Points = new List<PointF>(Points) };
}

internal enum ShapeKind { Rectangle, Ellipse, Line, Arrow }

internal sealed class ShapeAnnotation : Annotation
{
    public ShapeKind Kind;
    public Color Color;
    public float Width;
    public PointF Start;
    public PointF End;

    public override void Draw(Graphics g, Bitmap baseImage)
    {
        using var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (Kind)
        {
            case ShapeKind.Rectangle: g.DrawRectangle(pen, Rectangle.Round(RectOf(Start, End))); break;
            case ShapeKind.Ellipse: g.DrawEllipse(pen, RectOf(Start, End)); break;
            case ShapeKind.Line: g.DrawLine(pen, Start, End); break;
            case ShapeKind.Arrow: DrawArrow(g, pen); break;
        }
    }

    private void DrawArrow(Graphics g, Pen pen)
    {
        g.DrawLine(pen, Start, End);
        double ang = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        float head = Math.Max(12f, Width * 3.5f);
        const double spread = 0.45;
        var p1 = new PointF((float)(End.X - head * Math.Cos(ang - spread)), (float)(End.Y - head * Math.Sin(ang - spread)));
        var p2 = new PointF((float)(End.X - head * Math.Cos(ang + spread)), (float)(End.Y - head * Math.Sin(ang + spread)));
        using var br = new SolidBrush(Color);
        g.FillPolygon(br, new[] { End, p1, p2 });
    }

    public override RectangleF Bounds => RectangleF.Inflate(RectOf(Start, End), Width + 10, Width + 10);
    public override void Move(float dx, float dy) { Start = new PointF(Start.X + dx, Start.Y + dy); End = new PointF(End.X + dx, End.Y + dy); }
    public override bool HitTest(PointF p) => Bounds.Contains(p);
    public override Annotation Clone() => new ShapeAnnotation { Kind = Kind, Color = Color, Width = Width, Start = Start, End = End };
}

internal sealed class TextAnnotation : Annotation
{
    public PointF Location;
    public string Text = "";
    public float FontSize = 18f;
    public Color Color;
    public SizeF Measured;

    public override void Draw(Graphics g, Bitmap baseImage)
    {
        if (string.IsNullOrEmpty(Text)) return;
        using var f = new Font("Segoe UI", FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var br = new SolidBrush(Color);
        g.DrawString(Text, f, br, Location);
    }

    public override RectangleF Bounds => new(Location, Measured == SizeF.Empty ? new SizeF(FontSize * 4, FontSize * 1.4f) : Measured);
    public override void Move(float dx, float dy) => Location = new PointF(Location.X + dx, Location.Y + dy);
    public override Annotation Clone() => new TextAnnotation { Location = Location, Text = Text, FontSize = FontSize, Color = Color, Measured = Measured };
}

internal sealed class BlurAnnotation : Annotation
{
    public PointF Start;
    public PointF End;

    public override void Draw(Graphics g, Bitmap baseImage)
    {
        var r = Rectangle.Round(RectOf(Start, End));
        r.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));
        if (r.Width < 2 || r.Height < 2) return;

        using var region = baseImage.Clone(r, baseImage.PixelFormat);
        int block = Math.Max(6, Math.Min(r.Width, r.Height) / 12);
        int sw = Math.Max(1, r.Width / block), sh = Math.Max(1, r.Height / block);
        using var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(region, new Rectangle(0, 0, sw, sh));
        }
        var old = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, r);
        g.InterpolationMode = old;
    }

    public override RectangleF Bounds => RectOf(Start, End);
    public override void Move(float dx, float dy) { Start = new PointF(Start.X + dx, Start.Y + dy); End = new PointF(End.X + dx, End.Y + dy); }
    public override Annotation Clone() => new BlurAnnotation { Start = Start, End = End };
}

/// <summary>The badge outline used by a numbered step.</summary>
internal enum StepShape { Circle, Square, RoundedSquare }

internal sealed class StepAnnotation : Annotation
{
    public PointF Center;
    public int Number;
    public Color Color;
    public float Radius = 16f;
    public StepShape Shape = StepShape.Circle;

    public override void Draw(Graphics g, Bitmap baseImage)
    {
        var rect = new RectangleF(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
        float ringW = Math.Max(2f, Radius * 0.12f);
        using (var br = new SolidBrush(Color))
        using (var ring = new Pen(Color.White, ringW))
        {
            switch (Shape)
            {
                case StepShape.Square:
                    g.FillRectangle(br, rect);
                    g.DrawRectangle(ring, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
                case StepShape.RoundedSquare:
                    using (var path = RoundedRect(rect, Radius * 0.5f))
                    {
                        g.FillPath(br, path);
                        g.DrawPath(ring, path);
                    }
                    break;
                default:
                    g.FillEllipse(br, rect);
                    g.DrawEllipse(ring, rect);
                    break;
            }
        }

        string s = Number.ToString();
        using var f = new Font("Segoe UI", Radius * 1.05f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tb = new SolidBrush(Color.White);
        var sz = g.MeasureString(s, f);
        g.DrawString(s, f, tb, Center.X - sz.Width / 2, Center.Y - sz.Height / 2);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public override RectangleF Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override void Move(float dx, float dy) => Center = new PointF(Center.X + dx, Center.Y + dy);
    public override bool HitTest(PointF p)
    {
        // Square/rounded use the bounding box; circle uses the inscribed radius.
        if (Shape != StepShape.Circle) return Bounds.Contains(p);
        float dx = p.X - Center.X, dy = p.Y - Center.Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
    public override Annotation Clone() =>
        new StepAnnotation { Center = Center, Number = Number, Color = Color, Radius = Radius, Shape = Shape };
}
