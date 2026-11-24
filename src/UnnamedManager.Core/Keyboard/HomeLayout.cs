namespace UnnamedManager.Core.Keyboard;

/// Which groups share a column of a big Home (S-098), for both apps' Guides.
public static class HomeLayout
{
    /// Up to this many rows make one column (docs/DESIGN.md, The Guide).
    public const int RowsPerColumn = 12;

    /// With more than 12 entries: the current group, the first, has the first column. The
    /// others are packed in order into columns of up to 12 rows, a group's header counting
    /// as one. A group is never split, so one bigger than a column has one of its own.
    /// `entries` is each group's count; the result is each column's groups, by index.
    public static IReadOnlyList<IReadOnlyList<int>> Pack(IReadOnlyList<int> entries)
    {
        var columns = new List<IReadOnlyList<int>>();
        if (entries.Count == 0) return columns;
        columns.Add([0]);
        var column = new List<int>();
        var rows = 0;
        for (var i = 1; i < entries.Count; i++)
        {
            var height = entries[i] + 1;
            if (column.Count > 0 && rows + height > RowsPerColumn)
            {
                columns.Add(column);
                column = [];
                rows = 0;
            }
            column.Add(i);
            rows += height;
        }
        if (column.Count > 0) columns.Add(column);
        return columns;
    }
}
