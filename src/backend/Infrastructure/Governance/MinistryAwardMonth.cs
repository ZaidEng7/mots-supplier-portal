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

namespace MotsSupplierPortal.Infrastructure.Governance;

/// <summary>
/// Shared by the four SCR-601/602/603/606 handlers: is the Ministry permitted to see money, and what is a
/// bid worth.
///
/// <para><b>The flag is read on every request rather than cached.</b> It is a policy switch, and the point of
/// a policy switch is that turning it off takes effect now - a cached "yes" would keep disclosing for the
/// lifetime of a process after somebody decided to stop.</para>
/// </summary>
/// <summary>
/// The month an award belongs to, as the analytics bucket it.
///
/// <para>Public and named so it can be tested, because the defect it closes cannot be seen from the
/// outside without award data. <c>ToString("yyyy-MM")</c> formats in the CURRENT culture's calendar, and
/// this application runs with Arabic cultures available - so the buckets came back "1448-01", Hijri
/// years, on an axis a reader takes for the Gregorian months every other date on the screen uses. With
/// no awards in a database there are no buckets and an empty chart formats nothing, so the bug shipped
/// and stayed invisible until the demonstration data had its first award.</para>
/// </summary>
public static class MinistryAwardMonth
{
    public static string Of(DateTimeOffset when) => when.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
