using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Queries;

public sealed class GetSalaryGradesQueryHandler
    : IRequestHandler<GetSalaryGradesQuery, Result<IReadOnlyList<SalaryGradeDto>>>
{
    private readonly ISalaryGradeService _salaryGradeService;

    public GetSalaryGradesQueryHandler(ISalaryGradeService salaryGradeService)
    {
        _salaryGradeService = salaryGradeService;
    }

    public Task<Result<IReadOnlyList<SalaryGradeDto>>> Handle(
        GetSalaryGradesQuery request, CancellationToken cancellationToken)
    {
        return _salaryGradeService.GetAllAsync(request.IncludeInactive, cancellationToken);
    }
}
