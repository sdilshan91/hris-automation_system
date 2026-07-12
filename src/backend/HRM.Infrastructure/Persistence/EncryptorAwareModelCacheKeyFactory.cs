using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// EF Core model-cache key factory that incorporates the <c>AppDbContext</c>'s field-encryptor identity (P3-4).
/// The Pip/Recommendation encryption value converters close over a specific <see cref="Application.Common.Interfaces.IFieldEncryptor"/>,
/// so a context built with the NO-OP encryptor and one built with the real AES-GCM encryptor must NOT share a
/// compiled model (EF caches the model per context CLR type by default, which would otherwise let whichever
/// context is built first bake its converters in for the whole process). Keying additionally on the encryptor
/// TYPE keeps a plaintext-round-trip model and an encrypting model separate, while all real-encryptor instances
/// (same key across a run) safely share one model.
/// </summary>
public sealed class EncryptorAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is AppDbContext app
            ? (context.GetType(), app.FieldEncryptorDiscriminator, designTime)
            : (object)(context.GetType(), designTime);
}
