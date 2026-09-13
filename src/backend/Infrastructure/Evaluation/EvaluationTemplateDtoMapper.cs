using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

internal static class EvaluationTemplateDtoMapper
{
    public static EvaluationTemplateDto ToDto(EvaluationTemplate t) => new(
        t.Id, t.FamilyId, t.Version, t.NameAr, t.NameEn, t.Status, t.IsReferenced,
        [.. t.Criteria.OrderBy(c => c.SortOrder).Select(c => new CriterionDto(
            c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType,
            c.GuidanceAr, c.GuidanceEn, c.SortOrder, c.RequiresJustification))],
        t.RowVersion);
}
