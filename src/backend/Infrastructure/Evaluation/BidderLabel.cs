// The pseudonym an anonymised bid is known by while scoring is open.
//
// Letters rather than numbers, deliberately. A number reads as a rank, and the whole point is that the
// evaluator does not yet know which bid is better.
//
// The Arabic labels use the abjad letter sequence rather than the modern alphabetical one, which is what an
// Arabic reader expects for enumeration.
//
// Past the end of either alphabet it falls back to a number rather than throwing. An evaluation with
// twenty-seven bids is unlikely and would still have to render.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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

internal static class BidderLabel
{
    private const string EnglishLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly string[] ArabicAbjad =
        ["أ", "ب", "ج", "د", "هـ", "و", "ز", "ح", "ط", "ي", "ك", "ل", "م", "ن", "س", "ع", "ف", "ص", "ق", "ر", "ش", "ت", "ث", "خ", "ذ", "ض", "ظ", "غ"];

    public static string English(int index) =>
        index < EnglishLetters.Length
            ? $"Bidder {EnglishLetters[index]}"
            : $"Bidder {index + 1}";

    public static string Arabic(int index) =>
        index < ArabicAbjad.Length
            ? $"مورّد {ArabicAbjad[index]}"
            : $"مورّد {index + 1}";
}
