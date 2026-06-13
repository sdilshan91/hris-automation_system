using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Queries;

/// <summary>
/// Gets a single custom field definition by ID.
/// </summary>
public sealed record GetCustomFieldByIdQuery(Guid CustomFieldId)
    : IRequest<Result<CustomFieldDefinitionDto>>;
