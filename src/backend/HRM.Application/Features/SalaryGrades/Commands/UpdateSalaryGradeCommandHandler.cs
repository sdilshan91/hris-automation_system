using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

public sealed class UpdateSalaryGradeCommandHandler
    : IRequestHandler<UpdateSalaryGradeCommand, Result<SalaryGradeDto>>
{
    private readonly ISalaryGradeService _salaryGradeService;

    public UpdateSalaryGradeCommandHandler(ISalaryGradeService salaryGradeService)
    {
        _salaryGradeService = salaryGradeService;
    }

    public Task<Result<SalaryGradeDto>> Handle(
        UpdateSalaryGradeCommand request, CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateSalaryGradeRequest
        {
            Code = request.Code,
            Name = request.Name,
            MinAmount = request.MinAmount,
            MidAmount = request.MidAmount,
            MaxAmount = request.MaxAmount,
            Currency = request.Currency,
            Description = request.Description,
        };

        return _salaryGradeService.UpdateAsync(request.SalaryGradeId, updateRequest, cancellationToken);
    }
}
