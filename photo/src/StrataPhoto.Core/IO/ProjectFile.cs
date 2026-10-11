using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.IO;

/// <summary>
/// The layered project format (<c>.sphoto</c>): a zip archive with
/// <c>document.json</c> (layer tree and properties), one PNG per layer (<c>layers/&lt;n&gt;.png</c>),
/// one per mask (<c>masks/&lt;n&gt;.png</c>) and a flattened <c>preview.png</c>.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".sphoto";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class DocDto
    {
        public string App { get; set; } = "Strata Photo";
        public int Version { get; set; } = FormatVersion;
        public int Width { get; set; }
        public int Height { get; set; }
        public int? ActiveLayer { get; set; }
        public List<LayerDto> Layers { get; set; } = new();
    }

    private sealed class LayerDto
    {
        public string Type { get; set; } = "pixel"; // pixel | text | group
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public bool Locked { get; set; }
        public float Opacity { get; set; } = 1;
        public BlendMode BlendMode { get; set; }
        public string? Pixels { get; set; }
        public string? Mask { get; set; }
        public bool MaskEnabled { get; set; } = true;
        public bool? Expanded { get; set; }
        public TextDto? Text { get; set; }
        public List<LayerDto>? Children { get; set; }
    }

    private sealed class TextDto
    {
        public string Text { get; set; } = "";
        public string FontFamily { get; set; } = "Segoe UI";
        public float FontSize { get; set; }
        public string Color { get; set; } = "#000000";
        public TextAlign Align { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float LineSpacing { get; set; } = 1.2f;
    }

    public static void Save(Document doc, string path)
    {
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            int counter = 0;
            var dto = new DocDto { Width = doc.Width, Height = doc.Height };
            var indexOf = new Dictionary<Layer, int>();
            LayerDto ToDto(Layer layer)
            {
                int n = counter++;
                indexOf[layer] = n;
                var d = new LayerDto
                {
                    Index = n,
                    Name = layer.Name,
                    Visible = layer.Visible,
                    Locked = layer.Locked,
                    Opacity = layer.Opacity,
                    BlendMode = layer.BlendMode,
                    MaskEnabled = layer.MaskEnabled,
                };
                if (layer.Mask != null)
                {
                    d.Mask = $"masks/{n}.png";
                    WritePng(zip, d.Mask, layer.Mask);
                }
                switch (layer)
                {
                    case GroupLayer g:
                        d.Type = "group";
                        d.Expanded = g.Expanded;
                        d.Children = g.Children.Select(ToDto).ToList();
                        break;
                    case TextLayer t:
                        d.Type = "text";
                        d.Text = new TextDto
                        {
                            Text = t.Text.Text, FontFamily = t.Text.FontFamily, FontSize = t.Text.FontSize,
                            Color = t.Text.Color.ToString(), Align = t.Text.Align, Bold = t.Text.Bold,
                            Italic = t.Text.Italic, X = t.Text.X, Y = t.Text.Y, LineSpacing = t.Text.LineSpacing,
                        };
                        d.Pixels = $"layers/{n}.png";
                        WritePng(zip, d.Pixels, t.Pixels);
                        break;
                    case PixelLayer p:
                        d.Pixels = $"layers/{n}.png";
                        WritePng(zip, d.Pixels, p.Pixels);
                        break;
                }
                return d;
            }
            dto.Layers = doc.Root.Children.Select(ToDto).ToList();
            if (doc.ActiveLayer != null && indexOf.TryGetValue(doc.ActiveLayer, out var ai)) dto.ActiveLayer = ai;

            var entry = zip.CreateEntry("document.json", CompressionLevel.Optimal);
            using (var s = entry.Open())
                JsonSerializer.Serialize(s, dto, Json);

            using var flat = Compositor.Flatten(doc.Root, doc.Width, doc.Height);
            WritePng(zip, "preview.png", flat);
        }
        File.Move(tmp, path, overwrite: true);
        doc.FilePath = path;
        doc.Name = Path.GetFileName(path);
        doc.MarkSaved();
    }

    public static Document Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var docEntry = zip.GetEntry("document.json") ?? throw new InvalidDataException("Not a Strata Photo project (document.json missing).");
        DocDto dto;
        using (var s = docEntry.Open())
            dto = JsonSerializer.Deserialize<DocDto>(s, Json) ?? throw new InvalidDataException("Empty project file.");
        if (dto.Version > FormatVersion)
            throw new InvalidDataException($"This project was saved by a newer Strata Photo (format {dto.Version}).");
        if (dto.Width < 1 || dto.Height < 1) throw new InvalidDataException("Invalid document size.");

        var doc = new Document(dto.Width, dto.Height, Path.GetFileName(path), "Open");
        var byIndex = new Dictionary<int, Layer>();

        SKBitmap ReadBitmap(string? name)
        {
            var e = name == null ? null : zip.GetEntry(name);
            if (e == null) return Bitmaps.Create(dto.Width, dto.Height);
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            var bmp = ImageIO.Load(ms) ?? Bitmaps.Create(dto.Width, dto.Height);
            if (bmp.Width != dto.Width || bmp.Height != dto.Height)
            {
                var fixedBmp = Bitmaps.Create(dto.Width, dto.Height);
                using (var c = new SKCanvas(fixedBmp)) c.DrawPixels(bmp, 0, 0);
                bmp.Dispose();
                bmp = fixedBmp;
            }
            return bmp;
        }

        Layer FromDto(LayerDto d)
        {
            Layer layer = d.Type switch
            {
                "group" => new GroupLayer(d.Name) { Expanded = d.Expanded ?? true },
                "text" when d.Text != null => new TextLayer(d.Name, new TextProps(
                    d.Text.Text, d.Text.FontFamily, d.Text.FontSize,
                    SKColor.TryParse(d.Text.Color, out var c) ? c : SKColors.Black,
                    d.Text.Align, d.Text.Bold, d.Text.Italic, d.Text.X, d.Text.Y, d.Text.LineSpacing), ReadBitmap(d.Pixels)),
                _ => new PixelLayer(d.Name, ReadBitmap(d.Pixels)),
            };
            layer.Visible = d.Visible;
            layer.Locked = d.Locked;
            layer.Opacity = d.Opacity;
            layer.BlendMode = d.BlendMode;
            layer.MaskEnabled = d.MaskEnabled;
            if (d.Mask != null) layer.Mask = ReadBitmap(d.Mask);
            if (layer is GroupLayer g && d.Children != null)
                foreach (var child in d.Children)
                    g.Add(FromDto(child));
            byIndex[d.Index] = layer;
            return layer;
        }

        foreach (var l in dto.Layers) doc.Root.Add(FromDto(l));
        doc.FilePath = path;
        doc.ActiveLayer = dto.ActiveLayer.HasValue && byIndex.TryGetValue(dto.ActiveLayer.Value, out var active)
            ? active
            : doc.AllLayers.LastOrDefault();
        doc.InvalidateAll();
        doc.MarkSaved();
        return doc;
    }

    private static void WritePng(ZipArchive zip, string name, SKBitmap bmp)
    {
        // PNG is already compressed; storing avoids a second, slow deflate pass.
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        using var s = entry.Open();
        using var image = bmp.AsImage();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(s);
    }
}
