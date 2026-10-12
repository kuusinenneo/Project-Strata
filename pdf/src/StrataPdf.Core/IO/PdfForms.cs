using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace StrataPdf.Core.IO;

public enum FieldKind { Text, CheckBox, Choice, Radio, Other }

/// <summary>A fillable form field. <see cref="Value"/> is text, "true"/"false" for check boxes, or an option.</summary>
public sealed record FormField(string Name, FieldKind Kind, string Value, IReadOnlyList<string> Options, bool ReadOnly);

/// <summary>Reading and filling AcroForm fields with PDFsharp.</summary>
public static class PdfForms
{
    public static IReadOnlyList<FormField> Read(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var form = doc.AcroForm;
        if (form == null) return Array.Empty<FormField>();
        var list = new List<FormField>();
        Collect(form.Fields, list, "");
        return list;
    }

    private static void Collect(PdfAcroField.PdfAcroFieldCollection fields, List<FormField> list, string prefix)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            string name = string.IsNullOrEmpty(prefix) ? f.Name : $"{prefix}.{f.Name}";
            if (f.HasKids)
            {
                Collect(f.Fields, list, name);
                if (f is not PdfRadioButtonField) continue;
            }
            switch (f)
            {
                case PdfTextField t:
                    list.Add(new FormField(name, FieldKind.Text, t.Text ?? "", Array.Empty<string>(), t.ReadOnly));
                    break;
                case PdfCheckBoxField c:
                    list.Add(new FormField(name, FieldKind.CheckBox, c.Checked ? "true" : "false", Array.Empty<string>(), c.ReadOnly));
                    break;
                case PdfComboBoxField cb:
                    list.Add(new FormField(name, FieldKind.Choice, cb.Value?.ToString()?.Trim('(', ')') ?? "", Options(cb), cb.ReadOnly));
                    break;
                case PdfListBoxField lb:
                    list.Add(new FormField(name, FieldKind.Choice, lb.Value?.ToString()?.Trim('(', ')') ?? "", Options(lb), lb.ReadOnly));
                    break;
                default:
                    if (list.All(x => x.Name != name))
                        list.Add(new FormField(name, f is PdfRadioButtonField ? FieldKind.Radio : FieldKind.Other,
                            f.Value?.ToString() ?? "", Array.Empty<string>(), true));
                    break;
            }
        }
    }

    private static IReadOnlyList<string> Options(PdfChoiceField f)
    {
        var opts = f.Elements.GetArray("/Opt");
        if (opts == null) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in opts.Elements)
        {
            string? s = item switch
            {
                PdfString str => str.Value,
                PdfArray pair when pair.Elements.Count > 1 => (pair.Elements[1] as PdfString)?.Value,
                _ => null,
            };
            if (s != null) list.Add(s);
        }
        return list;
    }

    /// <summary>Writes a copy of <paramref name="path"/> with the given field values to <paramref name="output"/>.</summary>
    public static void Fill(string path, string output, IReadOnlyDictionary<string, string> values)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        var form = doc.AcroForm ?? throw new InvalidOperationException("This PDF has no form fields.");
        foreach (var (name, value) in values)
        {
            var f = Find(form.Fields, name, "");
            if (f == null || f.ReadOnly) continue;
            switch (f)
            {
                case PdfTextField t:
                    t.Text = value;
                    break;
                case PdfCheckBoxField c:
                    c.Checked = value is "true" or "True" or "1" or "yes";
                    break;
                case PdfComboBoxField cb:
                    cb.Value = new PdfString(value);
                    break;
                case PdfListBoxField lb:
                    lb.Value = new PdfString(value);
                    break;
            }
        }
        // Ask viewers to regenerate field appearances from the new values.
        form.Elements["/NeedAppearances"] = new PdfBoolean(true);
        doc.Save(output);
    }

    private static PdfAcroField? Find(PdfAcroField.PdfAcroFieldCollection fields, string name, string prefix)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            string full = string.IsNullOrEmpty(prefix) ? f.Name : $"{prefix}.{f.Name}";
            if (full == name) return f;
            if (f.HasKids && Find(f.Fields, name, full) is { } kid) return kid;
        }
        return null;
    }
}
