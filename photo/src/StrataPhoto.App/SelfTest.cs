using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using StrataPhoto.App.ViewModels;
using StrataPhoto.App.Views;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;
using StrataPhoto.Core.Tools;

namespace StrataPhoto.App;

/// <summary>
/// <c>StrataPhoto.exe --self-test [report.png]</c>: exercises the real UI end to end (documents,
/// tools, layers, adjustments, filters, files), measures brush speed on a 4000×4000 image,
/// saves a screenshot plus a JSON report next to it and exits (code 0 = all steps passed).
/// </summary>
internal static class SelfTest
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm)
    {
        var report = new Dictionary<string, object?>();
        var steps = new Dictionary<string, object?>();
        report["steps"] = steps;
        string png = StartupArgs.SelfTestReport ?? Path.Combine(Path.GetTempPath(), "strataphoto-selftest.png");
        bool ok = true;

        async Task Step(string name, Func<Task<object?>> action)
        {
            try
            {
                steps[name] = await action() ?? "ok";
            }
            catch (Exception ex)
            {
                ok = false;
                steps[name] = "FAILED: " + ex.Message;
                CrashLog.Write(ex);
            }
        }

        static Task Frame() => Task.Delay(120);

        await Step("theme", () => Task.FromResult<object?>(Strata.Ui.SharedTheme.SharedFolder));

        DocumentViewModel? d = null;
        await Step("new_document", async () =>
        {
            var doc = Document.CreateBlank(1600, 1000, "Self-test", SKColors.White);
            d = vm.AddDocument(doc);
            await Frame();
            return $"{doc.Width}x{doc.Height}";
        });
        if (d == null) { Finish(); return; }
        var docm = d.Document;
        var tools = vm.Tools;

        void Drag(ToolKind kind, SKPoint a, SKPoint b, bool shift = false, int steps = 12)
        {
            tools.Select(kind);
            tools.PointerDown(d, new PointerInput(a, Shift: shift));
            for (int i = 1; i <= steps; i++)
            {
                var p = new SKPoint(a.X + (b.X - a.X) * i / steps, a.Y + (b.Y - a.Y) * i / steps);
                float pressure = 0.3f + 0.7f * i / steps;
                tools.PointerMove(d, new PointerInput(p, pressure, Shift: shift));
            }
            tools.PointerUp(d, new PointerInput(b, Shift: shift));
        }

        await Step("gradient", async () =>
        {
            tools.Settings.Foreground = new SKColor(0x1e, 0x3a, 0x5f);
            tools.Settings.Background = new SKColor(0x40, 0x9c, 0xff);
            Drag(ToolKind.Gradient, new SKPoint(0, 0), new SKPoint(1600, 1000));
            await Frame();
            return docm.History.Entries[^1].Name;
        });

        await Step("brush_layer", async () =>
        {
            vm.NewLayerCommand.Execute(null);
            tools.Settings.Foreground = new SKColor(0xff, 0x8a, 0x3d);
            tools.Settings.Brush.Size = 40;
            for (int i = 0; i < 4; i++)
                Drag(ToolKind.Brush, new SKPoint(200, 300 + i * 90), new SKPoint(900, 200 + i * 110), steps: 40);
            await Frame();
            return docm.ActiveLayer?.Name;
        });

        await Step("shapes", async () =>
        {
            tools.Settings.Foreground = SKColors.White;
            tools.Settings.ShapeFill = false;
            tools.Settings.ShapeStrokeWidth = 6;
            Drag(ToolKind.Ellipse, new SKPoint(1000, 200), new SKPoint(1400, 600));
            tools.Settings.ShapeFill = true;
            tools.Settings.Foreground = new SKColor(0x96, 0x5a, 0xb4);
            Drag(ToolKind.Rectangle, new SKPoint(1100, 650), new SKPoint(1450, 900));
            await Frame();
            return docm.AllLayers.Count();
        });

        await Step("text", async () =>
        {
            tools.Select(ToolKind.Text);
            tools.Settings.Foreground = SKColors.White;
            tools.Settings.FontSize = 90;
            tools.Settings.Bold = true;
            vm.BeginTextEdit(d, new SKPoint(140, 860));
            vm.TextEdit!.Text = "Strata Photo";
            await Frame();
            vm.EndTextEdit();
            var t = docm.AllLayers.OfType<TextLayer>().Single();
            LayerOps.EditText(docm, t, t.Text with { Text = "Strata Photo ✓" }); // editable after creation
            return t.Text.Text;
        });

        await Step("selection_and_mask", async () =>
        {
            docm.ActiveLayer = docm.AllLayers.First(l => l.Name == "Layer 1");
            Drag(ToolKind.EllipseMarquee, new SKPoint(250, 180), new SKPoint(850, 700));
            if (docm.Selection.IsEmpty) throw new Exception("no selection");
            vm.AddMaskRevealCommand.Execute(null);
            vm.DeselectCommand.Execute(null);
            await Frame();
            return docm.ActiveLayer?.HasMask;
        });

        await Step("group_and_blend", async () =>
        {
            docm.ActiveLayer = docm.AllLayers.First(l => l.Name.StartsWith("Ellipse"));
            vm.GroupLayersCommand.Execute(null);
            LayerOps.SetBlendMode(docm, docm.ActiveLayer!, BlendMode.Screen);
            await Frame();
            return docm.ActiveLayer?.Name;
        });

        await Step("adjustment_preview", async () =>
        {
            docm.ActiveLayer = docm.Root.Children[0];
            vm.Panels.Adjustments.Begin("hue-saturation");
            var s = vm.Panels.Adjustments.Session ?? throw new Exception("no session");
            s.Params.First(p => p.Def.Key == "hue").Value = 25;
            await Task.Delay(400); // background preview
            vm.Panels.Adjustments.ApplyCommand.Execute(null);
            return docm.History.Entries[^1].Name;
        });

        await Step("filter", async () =>
        {
            docm.ActiveLayer = docm.AllLayers.First(l => l.Name.StartsWith("Rectangle"));
            var op = new Core.Operations.AddNoise();
            if (!PixelOps.Apply(docm, op.Name, (s, dd, r) => op.Apply(s, dd, r, op.Defaults()))) throw new Exception("filter refused");
            await Frame();
            return docm.History.Entries[^1].Name;
        });

        await Step("undo_redo", async () =>
        {
            int pos = docm.History.Position;
            vm.UndoCommand.Execute(null);
            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            await Frame();
            if (docm.History.Position != pos) throw new Exception("history position mismatch");
            return $"{docm.History.Entries.Count} steps";
        });

        await Step("free_transform", async () =>
        {
            docm.ActiveLayer = docm.AllLayers.First(l => l.Name.StartsWith("Rectangle"));
            vm.FreeTransformCommand.Execute(null);
            if (!tools.TransformActive) throw new Exception("transform did not start");
            tools.PointerDown(d, new PointerInput(new SKPoint(1300, 780), HandleRadius: 8));
            tools.PointerMove(d, new PointerInput(new SKPoint(1250, 760), HandleRadius: 8));
            tools.PointerUp(d, new PointerInput(new SKPoint(1250, 760)));
            tools.CommitActive();
            await Frame();
            return docm.History.Entries[^1].Name;
        });

        await Step("save_and_reopen", async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "strataphoto-selftest" + ProjectFile.Extension);
            ProjectFile.Save(docm, path);
            var loaded = ProjectFile.Load(path);
            int n = loaded.AllLayers.Count();
            loaded.Dispose();
            await Frame();
            return $"{n} layers";
        });

        await Step("export", async () =>
        {
            var results = new List<string>();
            foreach (var (fmt, ext) in new[] { (ExportFormat.Png, ".png"), (ExportFormat.Jpeg, ".jpg"), (ExportFormat.Webp, ".webp") })
            {
                var path = Path.Combine(Path.GetTempPath(), "strataphoto-selftest-export" + ext);
                ImageIO.Export(docm, path, new ExportOptions(fmt, 85, 800, 500));
                results.Add($"{ext} {new FileInfo(path).Length / 1024} KB");
            }
            await Frame();
            return string.Join(", ", results);
        });

        await Step("brush_4000", async () =>
        {
            var big = Document.CreateBlank(4000, 4000, "Brush 4000", SKColors.White);
            var bd = vm.AddDocument(big);
            await Frame();
            LayerOps.AddLayer(big);
            LayerOps.AddLayer(big);
            tools.Select(ToolKind.Brush);
            tools.Settings.Brush.Size = 80;
            var sw = Stopwatch.StartNew();
            int events = 300;
            tools.PointerDown(bd, new PointerInput(new SKPoint(200, 200)));
            for (int i = 1; i <= events; i++)
            {
                tools.PointerMove(bd, new PointerInput(new SKPoint(200 + i * 12, 200 + (float)Math.Sin(i / 10.0) * 600 + i * 6)));
                if (i % 30 == 0) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            tools.PointerUp(bd, new PointerInput(new SKPoint(3800, 2000)));
            sw.Stop();
            await Frame();
            double ms = sw.Elapsed.TotalMilliseconds / events;
            report["brush_ms_per_event_4000"] = Math.Round(ms, 3);
            report["tile_uploads"] = bd.Tiles.Uploads;
            if (ms > 16) throw new Exception($"brush too slow: {ms:F2} ms/event");
            vm.ActiveDocument = d;
            await Frame();
            return $"{ms:F2} ms per pointer event";
        });

        vm.Tools.Select(ToolKind.Brush);
        d.Fit();
        await Task.Delay(600);

        await Step("canvas_pointer_events", async () =>
        {
            // Real Avalonia pointer events through CanvasView (view -> document mapping, capture, brush).
            var canvas = d.Canvas ?? throw new Exception("no canvas");
            docm.ActiveLayer = docm.AllLayers.First(l => l.Name == "Layer 1");
            vm.DeselectCommand.Execute(null);
            tools.Select(ToolKind.Brush);
            int before = docm.History.Entries.Count;
            var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
            var props = new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed);
            Point P(double fx, double fy) => canvas.TranslatePoint(new Point(canvas.Bounds.Width * fx, canvas.Bounds.Height * fy), window) ?? default;
            canvas.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(canvas, pointer, window, P(0.3, 0.5), 0, props, Avalonia.Input.KeyModifiers.None));
            for (int i = 1; i <= 10; i++)
                canvas.RaiseEvent(new Avalonia.Input.PointerEventArgs(Avalonia.Input.InputElement.PointerMovedEvent, canvas, pointer, window,
                    P(0.3 + i * 0.03, 0.5), (ulong)i, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                        Avalonia.Input.PointerUpdateKind.Other), Avalonia.Input.KeyModifiers.None));
            canvas.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(canvas, pointer, window, P(0.6, 0.5), 20,
                new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PointerUpdateKind.LeftButtonReleased),
                Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left));
            await Frame();
            if (docm.History.Entries.Count != before + 1 || docm.History.Entries[^1].Name != "Brush")
                throw new Exception("no brush stroke recorded from pointer events");
            var mid = canvas.ToDoc(new Point(canvas.Bounds.Width * 0.45, canvas.Bounds.Height * 0.5));
            var c = Core.Imaging.Bitmaps.GetPixel(((PixelLayer)docm.ActiveLayer!).Pixels, (int)mid.X, (int)mid.Y);
            if (c.Alpha == 0) throw new Exception("stroke not under the pointer");
            return $"stroke at doc ({mid.X:F0}, {mid.Y:F0})";
        });

        report["gpu_tiles"] = Canvas.TileCache.LastDrawUsedGpu; // real window render (before the software screenshot)

        await Step("dialogs", async () =>
        {
            var opened = new List<string>();
            async Task Probe(Func<Task> show)
            {
                var task = show();
                await Task.Delay(250);
                var dlg = window.OwnedWindows.LastOrDefault() ?? throw new Exception("dialog did not open");
                opened.Add(dlg.Title ?? "?");
                dlg.Close();
                await task;
            }
            await Probe(() => window.NewDocumentAsync(SKColors.White));
            await Probe(() => window.ImageSizeAsync(docm));
            await Probe(() => window.CanvasSizeAsync(docm));
            await Probe(() => window.AskNumberAsync("Feather Selection", "Feather radius", 5, 0, 250, "px"));
            await Probe(() => window.AskSaveChangesAsync("x"));
            await Probe(() => window.ShowShortcutsAsync());
            await Probe(() => window.ShowAboutAsync());
            await Probe(async () => { await window.ExportAsync(docm); });
            docm.ActiveLayer = docm.Root.Children[0];
            var blur = OperationPreview.Start(d, new Core.Operations.GaussianBlur(), out _)!;
            await Probe(async () => { if (!await window.RunFilterAsync(blur)) blur.Cancel(); });
            return string.Join(", ", opened);
        });

        await Step("reset_layout", async () =>
        {
            vm.ResetLayoutCommand.Execute(null);
            await Task.Delay(300);
            vm.ShowPanel(vm.Panels.History);
            await Frame();
            return vm.Factory.Documents?.VisibleDockables?.Count + " documents after reset";
        });

        await Step("panel_views", () =>
        {
            // Every panel view must be bound to its own view model.
            var views = window.GetVisualDescendants().OfType<Avalonia.Controls.UserControl>()
                .Where(c => c.GetType().Namespace == "StrataPhoto.App.Views.Panels")
                .Select(c => $"{c.GetType().Name}<-{c.DataContext?.GetType().Name ?? "null"}")
                .ToList();
            var bad = views.Where(v => !v.Split("<-")[1].StartsWith(v.Split("<-")[0].Replace("Panel", ""))).ToList();
            if (bad.Count > 0) throw new Exception("mismatched: " + string.Join(", ", bad));
            return Task.FromResult<object?>(string.Join(", ", views));
        });

        await Step("screenshot", () =>
        {
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var rtb = new RenderTargetBitmap(size);
            rtb.Render(window);
            rtb.Save(png);
            return Task.FromResult<object?>(png);
        });

        Finish();

        void Finish()
        {
            report["ok"] = ok;
            try
            {
                File.WriteAllText(Path.ChangeExtension(png, ".json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
            StartupArgs.ExitCode = ok ? 0 : 1;
            foreach (var doc in vm.Documents.ToList()) doc.Document.MarkSaved();
            window.Close();
        }
    }
}
