using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>
/// Shared base for everything under <c>/board</c>: the <c>Board</c> policy plus the small
/// bits of glue every board page needs (current user id, mapping, toasts).
/// </summary>
[Authorize(Policy = "Board")]
public abstract class BoardControllerBase : Controller
{
    /// <summary>The signed-in board user. The policy guarantees the claim exists.</summary>
    protected Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new InvalidOperationException("The board policy ran without a user id claim.");
        }
    }

    protected void Toast(Result result) =>
        TempData[result.Succeeded ? "Toast.Success" : "Toast.Error"] = result.Message;

    protected static ApprovalRowViewModel ToApprovalRow(MemberSummary member, ITimeZoneService timeZone) => new()
    {
        Id = member.Id,
        DisplayName = member.DisplayName,
        Email = member.Email,
        RequestedOnLabel = timeZone.FormatLocal(member.JoinedAtUtc, "d MMM yyyy"),
        Initials = UserStatusView.Initials(member.DisplayName),
        LeetCodeHandle = member.LeetCodeHandle,
        CodeforcesHandle = member.CodeforcesHandle
    };

    protected MemberRowViewModel ToMemberRow(MemberSummary member, ITimeZoneService timeZone, int? points = null)
    {
        var isSelf = member.Id == CurrentUserId;

        return new MemberRowViewModel
        {
            Id = member.Id,
            DisplayName = member.DisplayName,
            Email = member.Email,
            Status = member.Status,
            Initials = UserStatusView.Initials(member.DisplayName),
            LeetCodeHandle = member.LeetCodeHandle,
            CodeforcesHandle = member.CodeforcesHandle,
            IsBoard = member.IsBoard,
            JoinedLabel = timeZone.FormatLocal(member.JoinedAtUtc, "d MMM yyyy"),
            Points = points,
            CanDeactivate = !isSelf && member.Status == UserStatus.Active,
            CanRestore = member.Status == UserStatus.Deactivated,
            IsSelf = isSelf
        };
    }


}
