namespace OscCommittee.Web.Domain.Enums;

/// <summary>Where a C# submission sits in the grading pipeline (PLAN §11).</summary>
public enum SubmissionStatus
{
    Uploaded = 0,
    Grading = 1,
    Graded = 2,
    Failed = 3,

    /// <summary>The AI produced a draft that a board member still has to accept.</summary>
    AwaitingReview = 4,

    /// <summary>
    /// The AI could not compile the submission. The attempt is not kept as history; the member
    /// fixes the error and uploads again, and the marker row is deleted when they do.
    /// </summary>
    TryAgain = 5
}