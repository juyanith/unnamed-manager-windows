using SkiaSharp;
using Svg.Skia;

// IconTool <AppIcon.svg> <assets-dir> [review.png]
//   writes <assets-dir>\AppIcon.ico (16, 20, 24, 32, 40, 48, 64, 256 px) and AppIcon.png (256 px),
//   and a review sheet: every size on the light and dark taskbar and Start colors.
//   A hand-tuned drawing for one size sits next to the SVG as AppIcon-<size>.svg (AppIcon-16.svg)
//   and replaces the scaled-down main drawing at that size.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: IconTool <AppIcon.svg> <assets-dir> [review.png]");
    return 1;
}

var svgs = new List<SKSvg>();
SKPicture? Load(string path)
{
    var svg = new SKSvg();
    svgs.Add(svg);
    return svg.Load(path);
}

if (Load(args[0]) is not { } picture)
{
    Console.Error.WriteLine($"could not read {args[0]}");
    return 1;
}

// Windows asks for these sizes: 16 to 48 for title bars, the taskbar, and Explorer at 100 to 300 percent, 256 for Start and large icons.
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
var pictures = new Dictionary<int, SKPicture>();
foreach (var size in sizes)
{
    var own = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, $"{Path.GetFileNameWithoutExtension(args[0])}-{size}.svg");
    if (!File.Exists(own)) { pictures[size] = picture; continue; }
    if (Load(own) is not { } tuned)
    {
        Console.Error.WriteLine($"could not read {own}");
        return 1;
    }
    pictures[size] = tuned;
    Console.WriteLine($"{size} px from {Path.GetFileName(own)}");
}
var pngs = sizes.Select(size => Render(pictures[size], size)).ToList();

Directory.CreateDirectory(args[1]);
WriteIco(Path.Combine(args[1], "AppIcon.ico"), sizes, pngs);
File.WriteAllBytes(Path.Combine(args[1], "AppIcon.png"), pngs[^1]);
Console.WriteLine($"wrote {Path.Combine(args[1], "AppIcon.ico")} and AppIcon.png");

// The tray icon (S-059): a one-color glyph drawn by hand at each size the tray asks for
// (Icon\Tray-<size>.svg), tinted near-black for a light taskbar and white for a dark one,
// in three states: ready, off (40 %), and warning (an amber dot with a dark rim).
int[] traySizes = [16, 20, 24, 32];
string[] states = ["Ready", "Off", "Warning"];
(string Name, SKColor Glyph)[] taskbars = [("Light", new SKColor(0x1A, 0x1A, 0x1A)), ("Dark", SKColors.White)];
var trayPictures = new Dictionary<int, SKPicture>();
foreach (var size in traySizes)
{
    var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, $"Tray-{size}.svg");
    if (Load(file) is not { } glyph)
    {
        Console.Error.WriteLine($"could not read {file}");
        return 1;
    }
    trayPictures[size] = glyph;
}
foreach (var old in new[] { "TrayReady.ico", "TrayOff.ico", "TrayWarning.ico" }) File.Delete(Path.Combine(args[1], old));
foreach (var state in states)
    foreach (var (taskbar, color) in taskbars)
        WriteIco(Path.Combine(args[1], $"Tray{state}-{taskbar}.ico"), traySizes, traySizes.Select(size => RenderTray(trayPictures[size], size, state, color)).ToList());
Console.WriteLine("wrote Tray<Ready|Off|Warning>-<Light|Dark>.ico");

if (args.Length > 2)
{
    WriteReview(args[2], pictures, sizes);
    Console.WriteLine($"wrote {args[2]}");
    var trayReview = Path.Combine(Path.GetDirectoryName(args[2])!, Path.GetFileNameWithoutExtension(args[2]) + "-tray.png");
    WriteTrayReview(trayReview, trayPictures, traySizes, states, taskbars);
    Console.WriteLine($"wrote {trayReview}");
}
foreach (var svg in svgs) svg.Dispose();
return 0;

static byte[] Render(SKPicture picture, int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        var bounds = picture.CullRect;
        canvas.Scale(size / bounds.Width, size / bounds.Height);
        canvas.DrawPicture(picture);
    }
    using var image = SKImage.FromBitmap(bitmap);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

static byte[] RenderTray(SKPicture picture, int size, string state, SKColor color)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        var bounds = picture.CullRect;
        canvas.Save();
        canvas.Scale(size / bounds.Width, size / bounds.Height);
        // The glyph's black, in the taskbar's color; off at 40 %.
        using var tint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(color.WithAlpha(state == "Off" ? (byte)102 : (byte)255), SKBlendMode.SrcIn) };
        canvas.DrawPicture(picture, tint);
        canvas.Restore();
        if (state == "Warning")
        {
            // An amber dot at the bottom right, in a ring cut out of the glyph so the
            // taskbar shows round it: it reads on a light and a dark taskbar alike.
            var dot = size * 0.17f;
            var ring = Math.Max(1, size / 16f);
            var c = size - dot - 0.5f;
            using var cut = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = true };
            using var amber = new SKPaint { Color = new SKColor(0xFC, 0xB9, 0x00), IsAntialias = true };
            canvas.DrawCircle(c, c, dot + ring, cut);
            canvas.DrawCircle(c, c, dot, amber);
        }
    }
    using var image = SKImage.FromBitmap(bitmap);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

// An .ico with PNG entries: a 6 byte header, a 16 byte entry per size, then the PNGs.
static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(pngs[i].Length);
        w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var png in pngs) w.Write(png);
}

// Rows: each state on the light and the dark taskbar; columns: each size at 4x with no smoothing.
static void WriteTrayReview(string path, Dictionary<int, SKPicture> pictures, int[] sizes, string[] states, (string Name, SKColor Glyph)[] taskbars)
{
    (string Name, SKColor Color)[] backgrounds = [("Light", new SKColor(0xEE, 0xF1, 0xF6)), ("Dark", new SKColor(0x1C, 0x1C, 0x1C))];
    const int cell = 160, label = 28;
    using var bitmap = new SKBitmap(cell * sizes.Length, (cell + label) * states.Length * 2);
    using var canvas = new SKCanvas(bitmap);
    using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 16);
    var row = 0;
    foreach (var state in states)
        foreach (var (taskbar, background) in backgrounds)
        {
            var top = row++ * (cell + label);
            using var fill = new SKPaint { Color = background };
            canvas.DrawRect(0, top, bitmap.Width, cell + label, fill);
            using var text = new SKPaint { Color = taskbar == "Light" ? SKColors.Black : SKColors.White, IsAntialias = true };
            canvas.DrawText($"{state} on a {taskbar.ToLowerInvariant()} taskbar", 8, top + 20, SKTextAlign.Left, font, text);
            var glyph = taskbars.First(t => t.Name == taskbar).Glyph;
            for (var col = 0; col < sizes.Length; col++)
            {
                using var image = SKImage.FromEncodedData(RenderTray(pictures[sizes[col]], sizes[col], state, glyph));
                var shown = sizes[col] * 4;
                var x = col * cell + (cell - shown) / 2f;
                var y = top + label + (cell - shown) / 2f;
                canvas.DrawImage(image, new SKRect(x, y, x + shown, y + shown), new SKSamplingOptions(SKFilterMode.Nearest));
            }
        }
    using var output = SKImage.FromBitmap(bitmap);
    File.WriteAllBytes(path, output.Encode(SKEncodedImageFormat.Png, 100).ToArray());
}

static void WriteReview(string path, Dictionary<int, SKPicture> pictures, int[] sizes)
{
    // Rows: Windows light taskbar, dark taskbar, light Start, dark Start.
    (string Name, SKColor Color)[] backgrounds =
    [
        ("light taskbar", new SKColor(0xEE, 0xF1, 0xF6)),
        ("dark taskbar", new SKColor(0x1C, 0x1C, 0x1C)),
        ("light Start", new SKColor(0xF3, 0xF3, 0xF3)),
        ("dark Start", new SKColor(0x2B, 0x2B, 0x2B)),
    ];
    const int cell = 280, label = 28;
    using var bitmap = new SKBitmap(cell * sizes.Length, (cell + label) * backgrounds.Length);
    using var canvas = new SKCanvas(bitmap);
    using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 16);
    for (var row = 0; row < backgrounds.Length; row++)
    {
        var top = row * (cell + label);
        using var fill = new SKPaint { Color = backgrounds[row].Color };
        canvas.DrawRect(0, top, bitmap.Width, cell + label, fill);
        using var text = new SKPaint { Color = row % 2 == 0 ? SKColors.Black : SKColors.White, IsAntialias = true };
        canvas.DrawText(backgrounds[row].Name, 8, top + 20, SKTextAlign.Left, font, text);
        for (var col = 0; col < sizes.Length; col++)
        {
            using var image = SKImage.FromEncodedData(Render(pictures[sizes[col]], sizes[col]));
            // Small sizes are drawn at 4x with no smoothing, so you see their real pixels.
            var shown = sizes[col] <= 64 ? sizes[col] * 4 : sizes[col];
            var x = col * cell + (cell - shown) / 2f;
            var y = top + label + (cell - shown) / 2f;
            canvas.DrawImage(image, new SKRect(x, y, x + shown, y + shown), new SKSamplingOptions(SKFilterMode.Nearest));
        }
    }
    using var output = SKImage.FromBitmap(bitmap);
    File.WriteAllBytes(path, output.Encode(SKEncodedImageFormat.Png, 100).ToArray());
}
