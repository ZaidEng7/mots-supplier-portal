using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

/// <summary>The loaded technical envelope of one bid. Internal to Infrastructure - the GUID stays on
/// this side of the boundary and never reaches EvaluatorProposalDto.</summary>
internal sealed record EvaluatorBid(
    Guid ProposalId, string ProposalCode,
    string SupplierReferenceCode, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    string? NarrativeAr, string? NarrativeEn,
    IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    IReadOnlyList<EvaluatorProposalDocumentDto> Documents);
