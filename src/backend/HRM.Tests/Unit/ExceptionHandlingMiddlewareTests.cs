using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using HRM.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-376. The global exception handler had NO tests at all — which is how a client-abort came to be
/// reported as an unhandled 500 for months without anyone noticing.
///
/// <para>The distinction these arms pin: <see cref="OperationCanceledException"/> reaches this middleware
/// from two completely different causes. A CLIENT abort (user navigated away, closed the tab, cancelled an
/// XHR) is not a fault. A SERVER-side deadline firing a linked token is. They arrive as the same exception
/// type, so only <c>HttpContext.RequestAborted.IsCancellationRequested</c> tells them apart — and an
/// unfiltered catch would silently convert every server timeout into a non-error.</para>
/// </summary>
public sealed class ExceptionHandlingMiddlewareTests
{
    /// <summary>
    /// `DefaultHttpContext` reports `HasStarted == false` forever — its response feature simply does not
    /// track it. A test using the bare default therefore CANNOT exercise the has-started guard, and would
    /// pass whether or not the guard existed. This feature makes the flag real by flipping it on first
    /// write, so the arm below tests the branch it claims to.
    /// </summary>
    private sealed class StartTrackingResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        internal void MarkStarted() => HasStarted = true;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    /// <summary>Flips the feature's HasStarted on the first byte written, the way a real server does.</summary>
    private sealed class StartTrackingStream : Stream
    {
        private readonly Stream _inner;
        private readonly StartTrackingResponseFeature _owner;
        public StartTrackingStream(Stream inner, StartTrackingResponseFeature owner)
            => (_inner, _owner) = (inner, owner);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _owner.MarkStarted();
            _inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _owner.MarkStarted();
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            _owner.MarkStarted();
            return _inner.WriteAsync(buffer, offset, count, ct);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] b, int o, int c) => _inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin so) => _inner.Seek(o, so);
        public override void SetLength(long v) => _inner.SetLength(v);
    }

    private static async Task<HttpContext> RunAsync(
        RequestDelegate next, bool clientAborted = false, bool trackResponseStart = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/employees";
        context.Request.Method = "GET";

        if (trackResponseStart)
        {
            // Both features are required. HasStarted lives on IHttpResponseFeature, but since ASP.NET Core
            // 3.0 the response STREAM comes from IHttpResponseBodyFeature — so a tracking stream installed
            // only on the former is never written to, and the flag never flips.
            var responseFeature = new StartTrackingResponseFeature();
            var body = new StartTrackingStream(new MemoryStream(), responseFeature);
            context.Features.Set<IHttpResponseFeature>(responseFeature);
            context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        }
        else
        {
            context.Response.Body = new MemoryStream();
        }

        if (clientAborted)
        {
            var aborted = new CancellationTokenSource();
            await aborted.CancelAsync();
            context.RequestAborted = aborted.Token;
        }

        var middleware = new ExceptionHandlingMiddleware(
            next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task A_client_abort_is_499_and_not_a_500()
    {
        var context = await RunAsync(
            _ => throw new OperationCanceledException("The operation was canceled."),
            clientAborted: true);

        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest,
            "a request that ended because the USER LEFT is not a server fault, and reporting it as 500 "
            + "puts non-faults into the bucket the on-call error signal reads");

        context.Response.Body.Length.Should().Be(0,
            "the connection is already closed — writing a body attempts a socket write that throws out "
            + "of the exception handler itself");
    }

    [Fact]
    public async Task A_TaskCanceledException_from_a_client_abort_is_also_499()
    {
        // TaskCanceledException derives from OperationCanceledException and is what HttpClient and several
        // EF paths actually throw, so the filter must cover it too. The original stack reported this type.
        var context = await RunAsync(
            _ => throw new TaskCanceledException("A task was canceled."),
            clientAborted: true);

        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task A_cancellation_the_CLIENT_did_not_cause_is_still_a_500()
    {
        // The arm that makes the filter matter. A server-side deadline (a linked CTS firing on OUR timeout)
        // throws the identical exception type while RequestAborted is NOT cancelled. That is a real fault.
        // An unfiltered catch would report it as 499 and hide every server timeout in the system.
        var context = await RunAsync(
            _ => throw new OperationCanceledException("Server-side timeout."),
            clientAborted: false);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "only the client aborting justifies 499; a server-side timeout is a genuine fault and must "
            + "stay in the error signal");
    }

    [Fact]
    public async Task An_ordinary_exception_is_still_a_500_with_a_body()
    {
        var context = await RunAsync(_ => throw new InvalidOperationException("boom"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Body.Length.Should().BeGreaterThan(0,
            "a live connection must still receive the generic error envelope");
    }

    [Fact]
    public async Task A_validation_exception_is_still_a_400()
    {
        var context = await RunAsync(_ => throw new ValidationException(
            [new ValidationFailure("Name", "Name is required.")]));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_failure_after_the_response_started_does_not_throw_a_second_exception()
    {
        // Setting StatusCode after HasStarted throws InvalidOperationException, which would escape the
        // handler and replace the original error with a confusing one. Guarding is what keeps the first
        // failure diagnosable.
        var context = await RunAsync(
            async ctx =>
            {
                await ctx.Response.WriteAsync("partial");
                throw new InvalidOperationException("failed midway through streaming");
            },
            trackResponseStart: true);

        context.Response.HasStarted.Should().BeTrue("the harness must actually report a started response, "
            + "or this arm passes whether or not the guard exists");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "the status was already committed with the first byte and cannot be rewritten — setting it "
            + "again throws InvalidOperationException OUT of the exception handler, replacing the original "
            + "failure with a confusing second one");
    }
}
