using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>US-ADM-007 BR-6: delete a workflow (only when there are no in-flight instances).</summary>
public sealed record DeleteWorkflowCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteWorkflowCommandHandler
    : IRequestHandler<DeleteWorkflowCommand, Result>
{
    private readonly IWorkflowService _service;

    public DeleteWorkflowCommandHandler(IWorkflowService service) => _service = service;

    public Task<Result> Handle(DeleteWorkflowCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.Id, cancellationToken);
}
