// One partial edit that replaced five per-field edit routes on a bid.
//
//
// EVERY RULE THE OLD ROUTES ENFORCED IS ENFORCED HERE
//
// They are not re-implemented. The same aggregate methods are called, so draft-only editing, the
// positive-quantity and positive-price guards, the currency requirement, the validity ordering and the
// requiredness of answers all come from the bid exactly as before.
//
// What changes is the number of round trips, and that the whole edit is one transaction against one row
// version. Two colleagues editing different sections of one bid now collide visibly instead of overwriting
// each other invisibly, which is why this came after the concurrency work rather than before.
//
// The audit vocabulary is the old routes' own, unchanged. Renaming these actions would silently break every
// audit query and report written against them: the route moved, the history did not.
//
// Mentioning nothing is not an error, because an empty merge patch is a no-op by the standard, but it must not
// write an audit row claiming an edit happened.
//
// The response is built from the same mapper the old routes used, after the save, so the totals and the new
// version the endpoint turns into a header are the persisted ones rather than in-memory guesses.
//
//
// THE PRICED LINES ARE A LIST, AND A LIST IS REPLACED WHOLESALE
//
// That is what the merge-patch standard says, and it is applied that way deliberately: an entry prices its
// line, and a line the array omits has its pricing REMOVED. That is what replacing the array means, and it is
// what makes deleting a line possible at all now that there is no delete route for one.
//
// Only a line the aggregate has just CREATED is announced to the change tracker. Pricing upserts: re-pricing a
// line already on the bid mutates an entity that is already tracked, and adding it again marks a persisted row
// as new, so the save issues an insert carrying its existing key and the request fails with a duplicate-key
// error rather than a refusal anybody could act on.
//
// That defect survived because it only fires from the SECOND line onwards. The array is replaced wholesale, so
// the caller resends every line it wants kept, and the first edit of a one-line bid has nothing persisted to
// re-add. Every walkthrough of this product had priced exactly one line.
//
//
// COMMERCIAL TERMS CANNOT BE HALF-CLEARED
//
// Setting them requires a currency, so "delete my commercial terms" has no representation the aggregate
// accepts. It is refused explicitly rather than silently ignored.
//
// Members the patch does not mention keep their current values, which is the whole point of a merge patch and
// the reason a supplier editing only their payment terms does not lose a warranty they entered last week.
//
// Dates are parsed as the explicit calendar form under the invariant culture. The ordinary parse uses the
// current culture, so the same value a JSON body always carries would parse on one host and throw on another,
// which it did, as a server error, before this was pinned.
//
// The delivery term has to name a row in the reference table. That is checked BEFORE the aggregate is touched,
// so a bad code leaves the bid exactly as it was rather than half-applied.
//
//
// THE REQUIREMENT ANSWERS ARE AN INVENTION, AND ARE MARKED AS ONE
//
// The written contract's worked body for this route does not include them, and it names no route for them, so
// they are carried here rather than left with no home once the per-answer route was retired.
//
// Each new answer is added to the tracked set explicitly, exactly as the retired handler did. A child created
// on a tracked aggregate's collection is not necessarily discovered, and a silently unsaved answer surfaces
// much later as "all mandatory requirements must be answered" at submission, which is precisely how this was
// caught.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class PatchProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IPatchProposalHandler
{
    public async Task<ProposalPatchResult> HandleAsync(string proposalReferenceCode, ProposalMergePatch patch, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalPatchResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

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
                var outcome = await ApplyCommercialTermsAsync(proposal!, patch.Member("commercialTerms"), ct);
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

        foreach (var action in touched.Distinct())
        {
            await auditLogger.LogAsync("Proposal", proposal!.Id, action, scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        }

        await db.SaveChangesAsync(ct);

        return new ProposalPatchResult.Success(ProposalDtoMapper.ToDto(proposal!, rfq.ReferenceCode));
    }

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

            var entity = proposal.Items.First(i => i.RfqItemId == rfqItemId);
            if (db.Entry(entity).State == EntityState.Detached) db.ProposalItems.Add(entity);
            keep.Add(rfqItemId);
        }

        foreach (var dropped in proposal.Items.Where(i => !keep.Contains(i.RfqItemId)).Select(i => i.RfqItemId).ToList())
        {
            proposal.RemoveItemPricing(dropped);
        }

        return null;
    }

    private async Task<ProposalPatchResult?> ApplyCommercialTermsAsync(Proposal proposal, JsonNode? node, CancellationToken ct)
    {
        if (node is null)
        {
            return new ProposalPatchResult.Invalid("commercialTerms", "TERMS_NOT_CLEARABLE",
                "Commercial terms cannot be deleted; send the terms you want instead.");
        }

        if (node is not JsonObject terms) return new ProposalPatchResult.Invalid("commercialTerms", "TERMS_NOT_AN_OBJECT", "commercialTerms must be an object.");

        string? Current(string member, string? currentValue) =>
            terms.ContainsKey(member) ? terms[member]?.GetValue<string>() : currentValue;

        DateOnly? CurrentDate(string member, DateOnly? currentValue)
        {
            if (!terms.ContainsKey(member)) return currentValue;
            if (terms[member] is null) return null;

            var raw = terms[member]!.GetValue<string>();
            return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new DomainException($"'{member}' must be a date in yyyy-MM-dd form.");
        }

        var submittedIncoterm = Current("incotermCode", proposal.IncotermCode);
        var (knownIncoterm, resolvedIncoterm) = await IncotermRule.ResolveAsync(db, submittedIncoterm, ct);
        if (!knownIncoterm)
        {
            return new ProposalPatchResult.Invalid("commercialTerms.incotermCode", "UNKNOWN_INCOTERM",
                await IncotermRule.RefusalDetailAsync(db, submittedIncoterm, ct));
        }

        proposal.SetCommercialTerms(
            Current("currencyCode", proposal.CurrencyCode) ?? string.Empty,
            Current("paymentTerms", proposal.PaymentTerms),
            resolvedIncoterm,
            Current("deliveryTermsAr", proposal.DeliveryTermsAr),
            Current("deliveryTermsEn", proposal.DeliveryTermsEn),
            Current("warranty", proposal.Warranty),
            CurrentDate("validityStart", proposal.ValidityStart),
            CurrentDate("validityEnd", proposal.ValidityEnd));

        return null;
    }

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

            db.RequirementAnswers.Add(proposal.RequirementAnswers.First(a => a.RequirementId == requirementId));
            touched.Add("proposal_requirement_answered");
        }

        return null;
    }
}
