using System.Net;
using LinuxInstallHelper.Core.Download;

namespace LinuxInstallHelper.Core.Tests.Download;

public class ResumePolicyTests
{
    private static readonly Uri Url = new("https://mirror.example/distro.iso");

    private static PartialDownloadState State(long? total = 1000, string? etag = "\"abc\"", string? url = "https://mirror.example/distro.iso") =>
        new() { TotalSize = total, ETag = etag, Url = url, LastModified = "Sat, 03 Oct 2026 10:00:00 GMT" };

    [Fact]
    public void Nothing_on_disk_starts_fresh()
    {
        var decision = ResumePolicy.Decide(0, null, 1000, Url);

        Assert.Equal(ResumeAction.StartFresh, decision.Action);
        Assert.Equal(0, decision.Offset);
    }

    [Fact]
    public void Partial_file_without_metadata_is_never_trusted()
    {
        Assert.Equal(ResumeAction.Restart, ResumePolicy.Decide(500, null, 1000, Url).Action);
    }

    [Fact]
    public void Partial_file_resumes_with_its_etag()
    {
        var decision = ResumePolicy.Decide(400, State(), 1000, Url);

        Assert.Equal(ResumeAction.Resume, decision.Action);
        Assert.Equal(400, decision.Offset);
        Assert.Equal("\"abc\"", decision.Validator);
    }

    [Fact]
    public void Last_modified_is_used_when_the_etag_is_weak()
    {
        var decision = ResumePolicy.Decide(400, State(etag: "W/\"weak\""), 1000, Url);

        Assert.Equal("Sat, 03 Oct 2026 10:00:00 GMT", decision.Validator);
    }

    [Fact]
    public void Another_mirror_resumes_without_validator()
    {
        var decision = ResumePolicy.Decide(400, State(), 1000, new Uri("https://other.example/distro.iso"));

        Assert.Equal(ResumeAction.Resume, decision.Action);
        Assert.Null(decision.Validator);
    }

    [Fact]
    public void Different_total_size_restarts()
    {
        Assert.Equal(ResumeAction.Restart, ResumePolicy.Decide(400, State(total: 900), 1000, Url).Action);
    }

    [Fact]
    public void Partial_file_larger_than_the_total_restarts()
    {
        Assert.Equal(ResumeAction.Restart, ResumePolicy.Decide(1200, State(), null, Url).Action);
    }

    [Fact]
    public void Complete_partial_file_needs_no_request()
    {
        var decision = ResumePolicy.Decide(1000, State(), 1000, Url);

        Assert.Equal(ResumeAction.Complete, decision.Action);
        Assert.Equal(1000, decision.Offset);
    }

    [Theory]
    [InlineData(HttpStatusCode.PartialContent, 400L, 400L, 1000L, RangeOutcome.Append)]
    [InlineData(HttpStatusCode.PartialContent, 400L, 0L, 1000L, RangeOutcome.Restart)]
    [InlineData(HttpStatusCode.OK, 400L, null, null, RangeOutcome.Restart)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, 1000L, null, 1000L, RangeOutcome.Complete)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, 1200L, null, 1000L, RangeOutcome.Restart)]
    [InlineData(HttpStatusCode.InternalServerError, 400L, null, null, RangeOutcome.Error)]
    public void Server_answers_are_interpreted(HttpStatusCode status, long offset, long? from, long? total, RangeOutcome expected)
    {
        Assert.Equal(expected, ResumePolicy.Interpret(status, offset, from, total));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Gone, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(null, false)]
    public void Permanent_failures_skip_to_the_next_mirror(HttpStatusCode? status, bool permanent)
    {
        Assert.Equal(permanent, ResumePolicy.IsPermanentFailure(status));
    }
}
