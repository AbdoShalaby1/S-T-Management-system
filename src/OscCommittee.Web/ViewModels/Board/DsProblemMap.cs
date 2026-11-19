using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;

namespace OscCommittee.Web.ViewModels.Board;

/// <summary>One mapping shared by the sheet editor panel and the problem pages.</summary>
public static class DsProblemMap
{
    public static List<DsProblemRowViewModel> ToRows(
        IReadOnlyList<DsProblemRow> rows,
        ITimeZoneService timeZone)
    {
        var mapped = new List<DsProblemRowViewModel>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            mapped.Add(new DsProblemRowViewModel
            {
                Id = row.Id,
                DsSheetId = row.DsSheetId,
                SheetTitle = row.SheetTitle,
                Title = row.Title,
                Platform = row.Platform,
                ExternalId = row.ExternalId,
                Url = row.Url,
                Difficulty = row.Difficulty,
                PointsOverride = row.PointsOverride,
                Points = row.Points,
                Rating = row.Rating,
                IsPaidOnly = row.IsPaidOnly,
                AssignedLabel = timeZone.FormatLocal(row.AssignedAtUtc, "d MMM yyyy · HH:mm"),
                SortOrder = row.SortOrder,
                SolveCount = row.SolveCount,
                IsFirst = index == 0,
                IsLast = index == rows.Count - 1
            });
        }

        return mapped;
    }
}