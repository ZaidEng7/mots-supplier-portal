using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// §12.5: one PATCH replaces the five per-field edit sub-routes, applied as RFC 7396 merge patch.
///
/// <para><b>Every rule the sub-routes enforced is enforced here.</b> They are not re-implemented -
/// the same aggregate methods are called, so Draft-only editing, the positive-quantity and
/// positive-price guards, the currency requirement, the validity ordering and the answer
/// requiredness all come from <see cref="Proposal"/> exactly as before. What changes is the number
/// of round-trips, and that the whole edit is one transaction against one row version: two team
/// members editing different sections of one proposal now collide visibly instead of overwriting
/// each other invisibly, which is the point of doing this after §8.1 rather than before.</para>
/// </summary>
public sealed class PatchProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IPatchProposalHandler
{
    public async Task<ProposalPatchResult> HandleAsync(string proposalReferenceCode, ProposalMergePatch patch, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalPatchResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        // The audit vocabulary is the sub-routes' own, unchanged. A PATCH that renamed these would
        // silently break every audit query and report written against the old actions - the route
        // moved, the history did not.
        var touched = new List<string>();

        try
        {
            if (patch.Mentions("items"))
            {
                var outcome = ApplyItems(proposal!, patch.Member("items"));
                if (outcome is not null) return outcome;
                touched.Add("proposal_item_priced");
            }

            if (patch.Mentions("commercialTerms"))
            {
                var outcome = ApplyCommercialTerms(proposal!, patch.Member("commercialTerms"));
                if (outcome is not null) return outcome;
                touched.Add("proposal_terms_updated");
            }

            if (patch.Mentions("technicalResponse"))
            {
                var outcome = ApplyTechnicalResponse(proposal!, patch.Member("technicalResponse"), touched);
                if (outcome is not null) return outcome;
            }
        }
        catch (DomainException ex)
        {
            return new ProposalPatchResult.InvalidState(ex.Message);
        }

        // Nothing mentioned is not an error - an empty merge patch is a no-op by RFC 7396 - but it
        // must not write an audit row claiming an edit happened.
        foreach (var action in touched.Distinct())
        {
            await auditLogger.LogAsync("Proposal", proposal!.Id, action, scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        }

        await db.SaveChangesAsync(ct);

        // Reloaded through the same mapper the sub-routes used, so totals and the new RowVersion the
        // endpoint turns into an ETag are the persisted ones rather than the in-memory guesses.
        return new ProposalPatchResult.Success(ProposalDtoMapper.ToDto(proposal!, rfq.ReferenceCode));
    }

    /// <summary>
    /// <c>items</c> is a list, and RFC 7396 replaces a list wholesale rather than merging into it.
    /// Applied that way deliberately: an entry present prices that line, and a line the array omits
    /// has its pricing REMOVED - which is what "replace the array" means, and what makes deleting a
    /// line possible at all now that DELETE /items/{id} is gone.
    /// </summary>
    private ProposalPatchResult? ApplyItems(Proposal proposal, JsonNode? node)
    {
        if (node is null)
        {
            foreach (var existing in proposal.Items.Select(i => i.RfqItemId).ToList())
            {
                proposal.RemoveItemPricing(existing);
            }
            return null;
        }

        if (node is not JsonArray array) return new ProposalPatchResult.Invalid("items", "ITEMS_NOT_AN_ARRAY", "items must be an array.");

        var keep = new List<Guid>();
        foreach (var entry in array)
        {
            if (entry is not JsonObject item) return new ProposalPatchResult.Invalid("items", "ITEM_NOT_AN_OBJECT", "Each item must be an object.");

            if (!Guid.TryParse(item["rfqItemId"]?.GetValue<string>(), out var rfqItemId))
            {
                return new ProposalPatchResult.Invalid("items[].rfqItemId", "RFQ_ITEM_ID_REQUIRED", "Each item must carry the rfqItemId it prices.");
            }

            var quantity = item["quantity"]?.GetValue<decimal>() ?? 0m;
            var unitPrice = item["unitPrice"]?.GetValue<decimal>() ?? 0m;

            proposal.SetItemPricing(rfqItemId, quantity, unitPrice,
                item["discount"]?.GetValue<decimal>(), item["leadTimeDays"]?.GetValue<int>(),
                item["notesAr"]?.GetValue<string>(), item["notesEn"]?.GetValue<string>());

            db.ProposalItems.Add(proposal.Items.First(i => i.RfqItemId == rfqItemId));
            keep.Add(rfqItemId);
        }

        foreach (var dropped in proposal.Items.Where(i => !keep.Contains(i.RfqItemId)).Select(i => i.RfqItemId).ToList())
        {
            proposal.RemoveItemPricing(dropped);
        }

        return null;
    }

    private static ProposalPatchResult? ApplyCommercialTerms(Proposal proposal, JsonNode? node)
    {
        if (node is null)
        {
            // Terms cannot be half-cleared: SetCommercialTerms requires a currency, so "delete my
            // commercial terms" has no representation the aggregate accepts. Refused explicitly
            // rather than silently ignored.
            return new ProposalPatchResult.Invalid("commercialTerms", "TERMS_NOT_CLEARABLE",
                "Commercial terms cannot be deleted; send the terms you want instead.");
        }

        if (node is not JsonObject terms) return new ProposalPatchResult.Invalid("commercialTerms", "TERMS_NOT_AN_OBJECT", "commercialTerms must be an object.");

        // Members the patch does not mention keep their current values - the whole point of merge
        // patch, and the reason a supplier editing only their payment terms does not lose a warranty
        // they entered last week.
        string? Current(string member, string? currentValue) =>
            terms.ContainsKey(member) ? terms[member]?.GetValue<string>() : currentValue;

        // ISO-8601 and invariant, explicitly. DateOnly.Parse uses the CURRENT culture, so the same
        // "2026-09-03" that a JSON body always carries would parse on one host and throw on another
        // - which it did, as a 500, before this was pinned.
        DateOnly? CurrentDate(string member, DateOnly? currentValue)
        {
            if (!terms.ContainsKey(member)) return currentValue;
            if (terms[member] is null) return null;

            var raw = terms[member]!.GetValue<string>();
            return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new DomainException($"'{member}' must be a date in yyyy-MM-dd form.");
        }

        proposal.SetCommercialTerms(
            Current("currencyCode", proposal.CurrencyCode) ?? string.Empty,
            Current("paymentTerms", proposal.PaymentTerms),
            Current("incotermCode", proposal.IncotermCode),
            Current("deliveryTermsAr", proposal.DeliveryTermsAr),
            Current("deliveryTermsEn", proposal.DeliveryTermsEn),
            Current("warranty", proposal.Warranty),
            CurrentDate("validityStart", proposal.ValidityStart),
            CurrentDate("validityEnd", proposal.ValidityEnd));

        return null;
    }

    /// <summary>
    /// §12.5's <c>technicalResponse</c>. The narrative maps onto the field the old PUT set; the
    /// requirement answers are an <b>invention</b> - §12.5's worked body does not include them and
    /// §12 names no route for them, so they are carried here as <c>answers[]</c> rather than left
    /// with no home once POST /requirements/{id}/answer is retired.
    /// </summary>
    private ProposalPatchResult? ApplyTechnicalResponse(Proposal proposal, JsonNode? node, List<string> touched)
    {
        if (node is null)
        {
            proposal.SetNarrative(null, null);
            touched.Add("proposal_narrative_updated");
            return null;
        }

        if (node is not JsonObject response) return new ProposalPatchResult.Invalid("technicalResponse", "TECHNICAL_RESPONSE_NOT_AN_OBJECT", "technicalResponse must be an object.");

        if (response.ContainsKey("narrativeAr") || response.ContainsKey("narrativeEn"))
        {
            proposal.SetNarrative(
                response.ContainsKey("narrativeAr") ? response["narrativeAr"]?.GetValue<string>() : proposal.NarrativeAr,
                response.ContainsKey("narrativeEn") ? response["narrativeEn"]?.GetValue<string>() : proposal.NarrativeEn);
            touched.Add("proposal_narrative_updated");
        }

        if (!response.ContainsKey("answers")) return null;

        if (response["answers"] is not JsonArray answers)
        {
            return new ProposalPatchResult.Invalid("technicalResponse.answers", "ANSWERS_NOT_AN_ARRAY", "answers must be an array.");
        }

        foreach (var entry in answers)
        {
            if (entry is not JsonObject answer ||
                !Guid.TryParse(answer["requirementId"]?.GetValue<string>(), out var requirementId))
            {
                return new ProposalPatchResult.Invalid("technicalResponse.answers[].requirementId", "REQUIREMENT_ID_REQUIRED",
                    "Each answer must carry the requirementId it answers.");
            }

            proposal.AnswerRequirement(requirementId,
                answer["answerAr"]?.GetValue<string>() ?? string.Empty,
                answer["answerEn"]?.GetValue<string>() ?? string.Empty);

            // Explicitly added, exactly as the retired handler did: a child created on a tracked
            // aggregate's collection is not necessarily discovered by the change tracker, and a
            // silently unsaved answer would surface much later as "all mandatory requirements must
            // be answered" on submit - which is precisely how this was caught.
            db.RequirementAnswers.Add(proposal.RequirementAnswers.First(a => a.RequirementId == requirementId));
            touched.Add("proposal_requirement_answered");
        }

        return null;
    }
}
