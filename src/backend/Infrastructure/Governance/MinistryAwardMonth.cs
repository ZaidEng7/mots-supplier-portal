// The month an award belongs to, as the analytics bucket it.
//
// Public and named so it can be tested, because the defect it closes cannot be seen from outside without award
// data.
//
// The ordinary short-date format uses the CURRENT culture's calendar, and this application runs with Arabic
// cultures available, so the buckets came back as Hijri years on an axis a reader takes for the Gregorian months
// every other date on the screen uses.
//
// With no awards in a database there are no buckets and an empty chart formats nothing, so the defect shipped and
// stayed invisible until the demonstration data had its first award.

namespace MotsSupplierPortal.Infrastructure.Governance;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Suppliers;

public static class MinistryAwardMonth
{
    public static string Of(DateTimeOffset when) => when.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
