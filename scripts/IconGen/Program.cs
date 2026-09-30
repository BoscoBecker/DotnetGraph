using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

var outputDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "DotnetGraph.Extension", "Resources", "Icons"));

Directory.CreateDirectory(outputDir);

SaveGraphIconPng(128, Path.Combine(outputDir, "PackageIcon.png"), detailed: true);
WriteCommandIconStrip(Path.Combine(outputDir, "CommandGraphIcons.png"));
SaveGraphIconPng(16, Path.Combine(outputDir, "CommandGraph16.png"), detailed: false);

static void WriteCommandIconStrip(string path)
{
    const int stripWidth = 32;
    const int frameHeight = 32;
    const int frameCount = 2;

    using var bitmap = new Bitmap(stripWidth, frameHeight * frameCount, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Transparent);

    for (var index = 0; index < frameCount; index++)
    {
        var size = index == 0 ? 16 : 32;
        using var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var frameGraphics = Graphics.FromImage(frame))
        {
            RenderGraphIcon(frameGraphics, size, detailed: false);
        }

        var offsetX = (stripWidth - size) / 2;
        var offsetY = index * frameHeight + (frameHeight - size) / 2;
        graphics.DrawImage(frame, offsetX, offsetY, size, size);
    }

    bitmap.Save(path, ImageFormat.Png);
}

static void SaveGraphIconPng(int size, string path, bool detailed)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    RenderGraphIcon(graphics, size, detailed);
    bitmap.Save(path, ImageFormat.Png);
}

static void RenderGraphIcon(Graphics graphics, int size, bool detailed)
{
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.Clear(Color.Transparent);

    var accent = Color.FromArgb(255, 0, 120, 212);
    var dotnet = Color.FromArgb(255, 81, 43, 212);
    var edge = Color.FromArgb(255, 105, 103, 100);
    var edgeLight = Color.FromArgb(255, 160, 158, 155);

    var nodeRadius = Math.Max(2.2f, size * 0.105f);
    var edgeWidth = Math.Max(1f, size / 18f);

    (float x, float y)[] nodes =
    [
        (0.50f, 0.22f),
        (0.22f, 0.58f),
        (0.78f, 0.58f),
        (0.50f, 0.82f)
    ];

    var points = nodes.Select(n => new PointF(n.x * size, n.y * size)).ToArray();
    var edges = new (int from, int to)[]
    {
        (0, 1), (0, 2), (1, 3), (2, 3), (1, 2)
    };

    using var edgePen = new Pen(edge, edgeWidth);
    edgePen.LineJoin = LineJoin.Round;
    edgePen.StartCap = LineCap.Round;
    edgePen.EndCap = LineCap.Round;

    foreach (var (from, to) in edges)
    {
        graphics.DrawLine(edgePen, points[from], points[to]);
        if (detailed && size >= 64)
        {
            DrawArrowHead(graphics, points[from], points[to], edgeLight, edgeWidth * 1.4f);
        }
    }

    for (var i = 0; i < points.Length; i++)
    {
        var fill = i == 0 ? dotnet : accent;
        DrawNode(graphics, points[i], nodeRadius, fill, accent, size, i == 0 && detailed);
    }
}

static void DrawNode(Graphics graphics, PointF center, float radius, Color fill, Color stroke, int size, bool hub)
{
    var diameter = radius * 2f;
    var x = center.X - radius;
    var y = center.Y - radius;

    if (hub && size >= 48)
    {
        using var halo = new SolidBrush(Color.FromArgb(40, fill.R, fill.G, fill.B));
        graphics.FillEllipse(halo, x - radius * 0.35f, y - radius * 0.35f, diameter + radius * 0.7f, diameter + radius * 0.7f);
    }

    using var fillBrush = new SolidBrush(fill);
    using var strokePen = new Pen(stroke, Math.Max(1f, size / 32f));
    graphics.FillEllipse(fillBrush, x, y, diameter, diameter);
    graphics.DrawEllipse(strokePen, x, y, diameter, diameter);

    if (size >= 96)
    {
        using var inner = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        var innerR = radius * 0.35f;
        graphics.FillEllipse(inner, center.X - innerR, center.Y - innerR, innerR * 2f, innerR * 2f);
    }
}

static void DrawArrowHead(Graphics graphics, PointF from, PointF to, Color color, float headLength)
{
    var dx = to.X - from.X;
    var dy = to.Y - from.Y;
    var len = MathF.Sqrt(dx * dx + dy * dy);
    if (len < 0.001f)
    {
        return;
    }

    var ux = dx / len;
    var uy = dy / len;
    var tipX = to.X - ux * headLength * 1.8f;
    var tipY = to.Y - uy * headLength * 1.8f;
    var wing = headLength * 0.9f;

    using var brush = new SolidBrush(color);
    graphics.FillPolygon(
        brush,
        [
            new PointF(to.X, to.Y),
            new PointF(tipX - uy * wing, tipY + ux * wing),
            new PointF(tipX + uy * wing, tipY - ux * wing)
        ]);
}
