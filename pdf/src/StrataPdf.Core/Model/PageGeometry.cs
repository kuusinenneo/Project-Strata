namespace StrataPdf.Core.Model;

/// <summary>
/// Maps between a page as shown (after the rotation added in Strata PDF) and its base space, where
/// annotations are stored. Same convention as the saver: clockwise rotation, y down.
/// </summary>
public static class PageGeometry
{
    /// <summary>Shown point → base point. (bw, bh) is the base (unrotated) page size.</summary>
    public static PagePoint ToBase(PageItem page, double x, double y) =>
        (((page.Rotation % 360) + 360) % 360) switch
        {
            90 => new PagePoint(y, page.BaseHeight - x),
            180 => new PagePoint(page.BaseWidth - x, page.BaseHeight - y),
            270 => new PagePoint(page.BaseWidth - y, x),
            _ => new PagePoint(x, y),
        };

    /// <summary>Base point → shown point.</summary>
    public static PagePoint ToShown(PageItem page, double x, double y) =>
        (((page.Rotation % 360) + 360) % 360) switch
        {
            90 => new PagePoint(page.BaseHeight - y, x),
            180 => new PagePoint(page.BaseWidth - x, page.BaseHeight - y),
            270 => new PagePoint(y, page.BaseWidth - x),
            _ => new PagePoint(x, y),
        };
}
