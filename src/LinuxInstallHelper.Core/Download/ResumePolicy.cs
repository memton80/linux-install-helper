using System.Net;

namespace LinuxInstallHelper.Core.Download;

/// <summary>What is known about a partially downloaded file (stored next to it as <c>.part.json</c>).</summary>
public sealed record PartialDownloadState
{
    public string? Url { get; init; }

    public string? ETag { get; init; }

    public string? LastModified { get; init; }

    public long? TotalSize { get; init; }
}

public enum ResumeAction
{
    /// <summary>Nothing usable on disk: download from the first byte.</summary>
    StartFresh,

    /// <summary>The partial file is unusable (size or version changed): delete it and start again.</summary>
    Restart,

    /// <summary>Ask the server for the missing bytes only.</summary>
    Resume,

    /// <summary>The partial file already holds every byte.</summary>
    Complete,
}

public sealed record ResumeDecision(ResumeAction Action, long Offset, string? Validator);

public enum RangeOutcome
{
    /// <summary>206 for the requested offset: append to the partial file.</summary>
    Append,

    /// <summary>The server sends the whole file (range ignored or file changed): truncate and write from 0.</summary>
    Restart,

    /// <summary>416 because the partial file is already complete.</summary>
    Complete,

    /// <summary>Unexpected answer.</summary>
    Error,
}

/// <summary>Pure decision logic of the resumable downloader (unit tested).</summary>
public static class ResumePolicy
{
    /// <summary>Decides how to continue from what is on disk.</summary>
    /// <param name="existingLength">Length of the <c>.part</c> file, 0 when it does not exist.</param>
    /// <param name="state">Metadata saved with the partial file, if any.</param>
    /// <param name="expectedSize">Size announced by the catalog or resolver, if known.</param>
    /// <param name="url">URL that is about to be used.</param>
    public static ResumeDecision Decide(long existingLength, PartialDownloadState? state, long? expectedSize, Uri url)
    {
        if (existingLength <= 0)
        {
            return new ResumeDecision(ResumeAction.StartFresh, 0, null);
        }

        if (state is null)
        {
            // Bytes of unknown origin: never trust them.
            return new ResumeDecision(ResumeAction.Restart, 0, null);
        }

        var total = state.TotalSize;
        if (expectedSize is not null && total is not null && total != expectedSize)
        {
            return new ResumeDecision(ResumeAction.Restart, 0, null);
        }

        total ??= expectedSize;
        if (total is not null && existingLength > total)
        {
            return new ResumeDecision(ResumeAction.Restart, 0, null);
        }

        if (total is not null && existingLength == total)
        {
            return new ResumeDecision(ResumeAction.Complete, existingLength, null);
        }

        // A validator only makes sense for the server that produced it. With another mirror the bytes are
        // still reused (same total size); the final SHA-256 check catches any inconsistency.
        var sameUrl = string.Equals(state.Url, url.ToString(), StringComparison.Ordinal);
        var validator = sameUrl ? StrongETag(state.ETag) ?? state.LastModified : null;
        return new ResumeDecision(ResumeAction.Resume, existingLength, validator);
    }

    /// <summary>Interprets the answer to a (possibly ranged) GET.</summary>
    public static RangeOutcome Interpret(HttpStatusCode status, long requestedOffset, long? rangeFrom, long? rangeTotal)
    {
        switch (status)
        {
            case HttpStatusCode.PartialContent:
                return rangeFrom == requestedOffset ? RangeOutcome.Append : RangeOutcome.Restart;

            case HttpStatusCode.OK:
                return RangeOutcome.Restart;

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                return requestedOffset > 0 && rangeTotal == requestedOffset ? RangeOutcome.Complete : RangeOutcome.Restart;

            default:
                return RangeOutcome.Error;
        }
    }

    /// <summary>Weak ETags (<c>W/"..."</c>) cannot be used with <c>If-Range</c>.</summary>
    internal static string? StrongETag(string? etag) =>
        string.IsNullOrEmpty(etag) || etag.StartsWith("W/", StringComparison.Ordinal) ? null : etag;

    /// <summary>HTTP statuses for which retrying the same URL is pointless.</summary>
    public static bool IsPermanentFailure(HttpStatusCode? status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
}
