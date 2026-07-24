using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRM.Tests.Unit;

// DF-30: LocalFileStorage must reject a relativePath that escapes the tenant's base directory
// (defense-in-depth — relativePath is server-derived today, but the guard must exist).
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _base;
    private readonly LocalFileStorage _storage;
    private readonly Guid _tenant = Guid.NewGuid();

    public LocalFileStorageTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "hrm-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
        _storage = new LocalFileStorage(_base, NullLogger<LocalFileStorage>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); } catch { /* best-effort */ }
    }

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    [Trait("TC", "TC-CHR-334")]
    public async Task UploadAsync_LegitimatePath_WritesAndReadsBack()
    {
        var url = await _storage.UploadAsync(_tenant, "branding/logo.png", Bytes("PNGDATA"), "image/png");
        url.Should().Be($"/{_tenant}/branding/logo.png");

        await using var read = await _storage.OpenReadAsync(_tenant, "branding/logo.png");
        read.Should().NotBeNull();
        using var r = new StreamReader(read!);
        (await r.ReadToEndAsync()).Should().Be("PNGDATA");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../" + "other-tenant/secret.png")]
    [InlineData("branding/../../escape.txt")]
    [Trait("TC", "TC-CHR-334")]
    public async Task UploadAsync_TraversalRelativePath_IsRejected(string evil)
    {
        var act = async () => await _storage.UploadAsync(_tenant, evil, Bytes("x"), "text/plain");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a relativePath resolving outside the tenant's base directory must be refused");
    }

    [Fact]
    [Trait("TC", "TC-CHR-334")]
    public async Task OpenReadAsync_TraversalRelativePath_IsRejected()
    {
        var act = async () => await _storage.OpenReadAsync(_tenant, "../../escape.txt");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    [Trait("TC", "TC-CHR-334")]
    public async Task DeleteAsync_TraversalRelativePath_IsRejected()
    {
        var act = async () => await _storage.DeleteAsync(_tenant, "../../escape.txt");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    [Trait("TC", "TC-CHR-334")]
    public async Task UploadAsync_SiblingTenantPrefix_IsNotTreatedAsInside()
    {
        // A tenant dir whose name is a PREFIX of another must not let "{tenant}X/.." style tricks in;
        // the trailing-separator check guards `/base/tenantX` vs `/base/tenantXY`.
        // NOTE: use "../" (forward slash) — on Linux "\" is a literal filename char, NOT a path
        // separator, so a "..\\" payload never escapes and this arm silently passed nothing there.
        // Use the guard's own id format (ToString(), with hyphens) so "{tenant}-sibling" is a REAL
        // prefix-extension of the tenant dir, actually exercising the trailing-separator boundary.
        var act = async () => await _storage.UploadAsync(_tenant, "../" + _tenant.ToString() + "-sibling/x", Bytes("x"), "text/plain");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
