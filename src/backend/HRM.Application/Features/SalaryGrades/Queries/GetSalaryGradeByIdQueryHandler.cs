using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Queries;

public sealed class GetSalaryGradeByIdQueryHandler
    : IRequestHandler<GetSalaryGradeByIdQuery, Result<SalaryGradeDto>>
{
    private readonly ISalaryGradeService _salaryGradeService;

    public GetSalaryGradeByIdQueryHandler(ISalaryGradeService salaryGradeService)
    {
        _salaryGradeService = salaryGradeService;
    }

    public Task<Result<SalaryGradeDto>> Handle(
        GetSalaryGradeByIdQuery request, CancellationToken cancellationToken)
    {
        return _salaryGradeService.GetByIdAsync(request.SalaryGradeId, cancellationToken);
    }
}
