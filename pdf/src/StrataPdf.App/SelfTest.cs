using System.Text.Json;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using StrataPdf.App.ViewModels;
using StrataPdf.App.Views;
using StrataPdf.Core.IO;
using StrataPdf.Core.Model;
using StrataPdf.Core.Rendering;

namespace StrataPdf.App;

/// <summary>
/// <c>StrataPDF.exe --self-test [report.png]</c>: builds a sample PDF, drives the real window
/// (tools via pointer events, page operations, stamps, forms, saving, splitting, compression),
/// saves a screenshot and a JSON report, and exits with 0 when every step passed.
/// </summary>
internal static class SelfTest
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm)
    {
        var steps = new Dictionary<string, object?>();
        bool ok = true;
        string png = StartupArgs.SelfTestReport ?? Path.Combine(Path.GetTempPath(), "strata-pdf-selftest.png");
        string dir = Path.Combine(Path.GetTempPath(), "strata-pdf-selftest");
        Directory.CreateDirectory(dir);

        async Task Step(string name, Func<Task<object?>> f)
        {
            try { steps[name] = await f() ?? "ok"; }
            catch (Exception ex)
            {
                ok = false;
                steps[name] = "FAILED: " + ex.Message;
                CrashLog.Write(ex);
            }
        }
        static Task Frame() => Task.Delay(150);

        await Step("open", async () =>
        {
            string src = Path.Combine(dir, "sample.pdf");
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < 3; i++)
                {
                    var page = doc.AddPage();
                    using var g = XGraphics.FromPdfPage(page);
                    g.DrawString($"Strata PDF sample page {i + 1}", new XFont("Arial", 20), XBrushes.Black, 50, 80);
                    g.DrawString("Account number 0000-SECRET-42", new XFont("Arial", 12), XBrushes.DarkRed, 50, 120);
                    g.DrawRectangle(new XPen(XColors.SteelBlue, 2), 50, 160, 300, 200);
                }
                doc.Save(src);
            }
            await vm.OpenPathsAsync(new[] { src });
            await Frame();
            if (vm.Workspace?.Pages.Count != 3) throw new Exception("pages not loaded");
            return $"{vm.Workspace.Pages.Count} pages";
        });
        var ws = vm.Workspace!;
        var canvas = window.DocumentCanvas;

        async Task Drag(PdfTool tool, int page, PagePoint a, PagePoint b)
        {
            vm.SelectToolCommand.Execute(tool);
            vm.GoToPage(page);
            await Frame();
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            Point P(PagePoint p) => canvas.TranslatePoint(canvas.ViewPoint(page, p), window) ?? default;
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, pointer, window, P(a), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            for (int i = 1; i <= 6; i++)
            {
                var p = new PagePoint(a.X + (b.X - a.X) * i / 6, a.Y + (b.Y - a.Y) * i / 6 + (tool == PdfTool.Pen ? Math.Sin(i) * 8 : 0));
                canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, pointer, window, P(p), (ulong)i,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            }
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, pointer, window, P(b), 10,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            await Frame();
        }

        await Step("annotate_with_pointer", async () =>
        {
            await Drag(PdfTool.Highlight, 0, new(48, 60), new(330, 88));
            await Drag(PdfTool.Pen, 0, new(60, 420), new(300, 440));
            vm.ColorHex = "#409cff";
            await Drag(PdfTool.Rectangle, 0, new(60, 480), new(200, 560));
            await Drag(PdfTool.Ellipse, 0, new(220, 480), new(360, 560));
            await Drag(PdfTool.Arrow, 0, new(380, 560), new(470, 470));
            await Drag(PdfTool.Redact, 1, new(48, 108), new(260, 124));
            int n = ws.Pages[0].Annotations.Count;
            if (n != 5 || ws.Pages[1].Annotations.Count != 1) throw new Exception($"expected 5+1 annotations, got {n}+{ws.Pages[1].Annotations.Count}");
            return $"{n} on page 1, 1 redaction on page 2";
        });

        await Step("text_note_signature", async () =>
        {
            ws.AddAnnotation(ws.Pages[0].Id, new TextBoxAnnotation(new PageRect(60, 600, 300, 40), "Approved — Strata PDF self-test", 14, Rgba.Red));
            ws.AddAnnotation(ws.Pages[0].Id, new NoteAnnotation(new PagePoint(500, 60), "Please review"));
            var pad = new SignaturePad { Width = 300, Height = 120 };
            pad.AddStroke(new[] { new Point(10, 80), new Point(40, 30), new Point(70, 90), new Point(110, 40), new Point(150, 85), new Point(200, 50) });
            vm.SignaturePng = pad.ToPng();
            await Drag(PdfTool.Signature, 0, new(380, 640), new(380, 640));
            await Frame();
            return $"{ws.Pages[0].Annotations.Count} annotations on page 1";
        });

        await Step("select_move_delete", async () =>
        {
            vm.SelectToolCommand.Execute(PdfTool.Select);
            var rect = ws.Pages[0].Annotations.OfType<ShapeAnnotation>().First(s => s.Kind == ShapeKind.Rectangle);
            await Drag(PdfTool.Select, 0, new(rect.From.X + 1, rect.From.Y + 1), new(rect.From.X + 31, rect.From.Y + 21));
            var moved = ws.Pages[0].Annotations.OfType<ShapeAnnotation>().First(s => s.Kind == ShapeKind.Rectangle);
            if (Math.Abs(moved.From.X - rect.From.X - 30) > 1) throw new Exception("move failed");
            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);
            return "moved by (30, 20)";
        });

        await Step("pages", async () =>
        {
            vm.SelectedThumbs.Clear();
            vm.GoToPage(2);
            vm.RotateRightCommand.Execute(null);
            vm.MovePagesUpCommand.Execute(null);
            await Frame();
            if (ws.Pages[1].Rotation != 90) throw new Exception("rotate/move failed");
            vm.ApplyStampsCommand.Execute(null);
            vm.PnEnabled = true;
            vm.WmEnabled = true;
            vm.WmText = "SELF-TEST";
            vm.ApplyStampsCommand.Execute(null);
            await Frame();
            return "rotated page 3, moved it up, page numbers + watermark";
        });

        string saved = Path.Combine(dir, "saved.pdf");
        await Step("save", async () =>
        {
            await Task.Run(() => PdfSaver.Save(ws, saved));
            int n = PdfRenderer.PageCount(saved);
            string t0 = PdfRenderer.PageText(saved, 0), t2 = PdfRenderer.PageText(saved, 2);
            if (n != 3) throw new Exception("page count");
            if (!t0.Contains("Approved") || !t0.Contains("Page 1 of 3")) throw new Exception("text box / page numbers missing");
            if (t2.Contains("SECRET")) throw new Exception("redaction did not remove text");
            return $"{MainViewModel.FormatSize(new FileInfo(saved).Length)}, redaction removed text";
        });

        await Step("split_extract_compress", async () =>
        {
            string part = Path.Combine(dir, "part.pdf");
            await Task.Run(() => PdfSaver.Save(ws, part, new SaveOptions(OnlyPages: new[] { ws.Pages[0].Id })));
            string small = Path.Combine(dir, "small.pdf");
            await Task.Run(() => PdfSaver.Save(ws, small, new SaveOptions(CompressionLevel.Strong, 72, 60)));
            return $"extract: {PdfRenderer.PageCount(part)} page, strong: {MainViewModel.FormatSize(new FileInfo(small).Length)}";
        });

        await Step("merge_saved_file", async () =>
        {
            ws.InsertPdf(saved);
            await Frame();
            int n = ws.Pages.Count;
            vm.UndoCommand.Execute(null);
            return $"{n} pages after merge, undone";
        });

        await Step("thumbnails", async () =>
        {
            for (int i = 0; i < 40 && vm.Thumbs.Any(t => t.Image == null); i++) await Task.Delay(100);
            return $"{vm.Thumbs.Count(t => t.Image != null)} of {vm.Thumbs.Count} rendered";
        });

        await Step("extract_images_to_photo", async () =>
        {
            // the source pages have no images; the saved file holds the signature image
            var images = PdfImages.Extract(saved, new[] { 1 }, Path.Combine(dir, "images"), out _);
            if (images.Count == 0) throw new Exception("signature image not found in the saved PDF");
            await window.ExtractImagesToPhotoAsync(vm, launch: false, quiet: true);
            return $"{images.Count} image(s) from the saved PDF; Strata Photo found: {Strata.Ui.SuiteApps.IsAvailable(Strata.Ui.SuiteApp.Photo)}";
        });

        vm.SelectToolCommand.Execute(PdfTool.Select);
        vm.GoToPage(0);
        vm.FitWidthCommand.Execute(null);
        await Task.Delay(800);
        await Step("screenshot", () =>
        {
            using var rtb = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            rtb.Render(window);
            rtb.Save(png);
            return Task.FromResult<object?>(png);
        });

        try
        {
            File.WriteAllText(Path.ChangeExtension(png, ".json"),
                JsonSerializer.Serialize(new { ok, steps }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
        StartupArgs.ExitCode = ok ? 0 : 1;
        ws.MarkSaved();
        window.Close();
    }
}
