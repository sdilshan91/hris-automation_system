using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

/// <summary>
/// Returns the department hierarchy as a tree structure (FR-8).
/// Root departments (ParentDepartmentId == null) are top-level nodes.
/// </summary>
public sealed record GetDepartmentTreeQuery : IRequest<Result<IReadOnlyList<DepartmentTreeNodeDto>>>;
