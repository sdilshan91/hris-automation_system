# MediatR → Mediator Migration Guide (reference / decision-support)

> **Provenance:** vendored from [`codewithmukesh/dotnet-claude-kit`](https://github.com/codewithmukesh/dotnet-claude-kit)
> (`knowledge/mediatr-to-mediator-migration.md`), MIT © 2025 Mukesh Murugan.
>
> **Status for HRM: decision-support only — NOT an executed or scheduled migration.**

## Why this is in our repo

This platform runs **MediatR** as the CQRS dispatcher across every feature handler, plus two
pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`) in `HRM.Application`. MediatR v13+
moved to a **dual RPL / commercial license** (Jimmy Bogard / LuckyPenny Software), so continuing to
take new MediatR versions may eventually carry a commercial-licensing obligation depending on
company size and usage.

**Watch-item, not an emergency.** Before acting on this, verify the exact risk for *our* pinned
version and org:

1. Check the MediatR version pinned in `src/backend` (`dotnet list package | grep -i mediatr`, or the
   `.csproj` / `Directory.Packages.props`). Older versions remain under their original permissive terms.
2. Read the current MediatR license terms for that version and the free-use threshold.
3. Decide: **stay pinned** (no cost, no code change), or **migrate** to the MIT, source-generated
   [`Mediator`](https://github.com/martinothamar/Mediator) using the guide below.

The API is intentionally close, so a migration is mechanical (mainly `Task→ValueTask` and the
pipeline `next` signature). *Confidence the licensing change is real and applies to recent MediatR:
~90% — confirm against our pinned version before any decision.*

---

## Why Migrate (if we choose to)

- **MIT license** — Mediator is free for all use cases. MediatR introduced commercial licensing.
- **Source-generated** — Zero runtime reflection. Faster startup, AOT compatible, compile-time handler resolution.
- **API similarity** — Mediator's API is intentionally close to MediatR's, making migration straightforward.

## API Comparison

| Concept | MediatR | Mediator |
|---------|---------|----------|
| Request interface | `IRequest<TResponse>` | `IRequest<TResponse>` |
| Handler interface | `IRequestHandler<TRequest, TResponse>` | `IRequestHandler<TRequest, TResponse>` |
| Notification | `INotification` | `INotification` |
| Notification handler | `INotificationHandler<T>` | `INotificationHandler<T>` |
| Pipeline behavior | `IPipelineBehavior<TRequest, TResponse>` | `IPipelineBehavior<TRequest, TResponse>` |
| Send dispatch | `ISender.Send(request, ct)` | `ISender.Send(request, ct)` |
| Publish dispatch | `IPublisher.Publish(notification, ct)` | `IPublisher.Publish(notification, ct)` |
| DI registration | `services.AddMediatR(cfg => ...)` | `services.AddMediator()` |
| Handler return type | `Task<TResponse>` | `ValueTask<TResponse>` |
| Pipeline `next()` call | `next()` | `next(request, ct)` |

## Key Differences

### 1. `Task<T>` to `ValueTask<T>`

All handler methods return `ValueTask<T>` instead of `Task<T>`.

```csharp
// MediatR
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public async Task<OrderResponse> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // ...
    }
}

// Mediator
public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public async ValueTask<OrderResponse> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // ...
    }
}
```

### 2. Pipeline Behavior Delegate Signature

The `next` delegate in pipeline behaviors requires `request` and `ct` parameters.
(In our repo this affects `ValidationBehavior` and `LoggingBehavior`.)

```csharp
// MediatR
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Before
        var response = await next(); // <-- no args
        // After
        return response;
    }
}

// Mediator
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken ct)
    {
        // Before
        var response = await next(request, ct); // <-- pass request + ct
        // After
        return response;
    }
}
```

### 3. Namespace Changes

```csharp
// MediatR
using MediatR;

// Mediator
using Mediator;
```

### 4. Registration

```csharp
// MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Mediator — source-generated, registers all handlers at compile time
builder.Services.AddMediator();
```

## Migration Checklist

1. **Swap NuGet packages**
   ```bash
   dotnet remove package MediatR
   dotnet remove package MediatR.Extensions.Microsoft.DependencyInjection
   dotnet add package Mediator.Abstractions
   dotnet add package Mediator.SourceGenerator
   ```

2. **Find-and-replace namespaces**
   - `using MediatR;` → `using Mediator;`

3. **Update handler return types**
   - `Task<TResponse>` → `ValueTask<TResponse>` on all `Handle` methods

4. **Update pipeline behaviors**
   - `RequestHandlerDelegate<TResponse>` → `MessageHandlerDelegate<TRequest, TResponse>`
   - `await next()` → `await next(request, ct)`

5. **Update DI registration**
   - `services.AddMediatR(...)` → `services.AddMediator()`

6. **Seal handler classes** — Mediator source generator works with `sealed` classes; prefer them for performance.

7. **Build and fix** — The source generator will report compile-time errors for any handlers with incorrect signatures.

8. **Run tests** — Verify all handler and behavior tests pass.

## Common Gotchas

- **`ValueTask` cannot be awaited multiple times** — If you were caching or branching on `Task<T>` in behaviors, use `.AsTask()` to convert.
- **Generic constraints** — Mediator may require `IRequest<TResponse>` constraints on pipeline behaviors where MediatR did not.
- **Stream requests** — Mediator uses `IStreamRequest<T>` (same name as MediatR). Verify stream handlers compile.
- **No `IMediator` facade** — Use `ISender` for commands/queries and `IPublisher` for notifications directly.
