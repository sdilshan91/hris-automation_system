using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

public sealed class CreateSalaryGradeCommandHandler
    : IRequestHandler<CreateSalaryGradeCommand, Result<SalaryGradeDto>>
{
    private readonly ISalaryGradeService _salaryGradeService;

    public CreateSalaryGradeCommandHandler(ISalaryGradeService salaryGradeService)
    {
        _salaryGradeService = salaryGradeService;
    }

    public Task<Result<SalaryGradeDto>> Handle(
        CreateSalaryGradeCommand request, CancellationToken cancellationToken)
    {
        var createRequest = new CreateSalaryGradeRequest
        {
            Code = request.Code,
            Name = request.Name,
            MinAmount = request.MinAmount,
            MidAmount = request.MidAmount,
            MaxAmount = request.MaxAmount,
            Currency = request.Currency,
            Description = request.Description,
        };

        return _salaryGradeService.CreateAsync(createRequest, cancellationToken);
    }
}
