using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

public sealed class DeactivateSalaryGradeCommandHandler
    : IRequestHandler<DeactivateSalaryGradeCommand, Result>
{
    private readonly ISalaryGradeService _salaryGradeService;

    public DeactivateSalaryGradeCommandHandler(ISalaryGradeService salaryGradeService)
    {
        _salaryGradeService = salaryGradeService;
    }

    public Task<Result> Handle(
        DeactivateSalaryGradeCommand request, CancellationToken cancellationToken)
    {
        return _salaryGradeService.DeactivateAsync(request.SalaryGradeId, cancellationToken);
    }
}
