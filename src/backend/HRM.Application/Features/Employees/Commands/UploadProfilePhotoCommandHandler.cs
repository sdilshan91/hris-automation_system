using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class UploadProfilePhotoCommandHandler
    : IRequestHandler<UploadProfilePhotoCommand, Result<string>>
{
    private readonly IEmployeeService _employeeService;

    public UploadProfilePhotoCommandHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<string>> Handle(
        UploadProfilePhotoCommand request, CancellationToken cancellationToken)
    {
        return _employeeService.UploadProfilePhotoAsync(
            request.EmployeeId,
            request.FileStream,
            request.FileName,
            request.ContentType,
            request.FileSize,
            cancellationToken);
    }
}
