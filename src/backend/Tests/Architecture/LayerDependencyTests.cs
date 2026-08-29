using FluentAssertions;
using NetArchTest.Rules;

namespace MotsSupplierPortal.Tests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency direction described in
/// docs/architecture/00-foundational-decisions.md: Domain has no outward dependencies,
/// Application depends only on Domain, and only Infrastructure/Api may depend on
/// EF Core / ASP.NET Core.
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "MotsSupplierPortal.Domain";
    private const string Application = "MotsSupplierPortal.Application";
    private const string Infrastructure = "MotsSupplierPortal.Infrastructure";
    private const string Api = "MotsSupplierPortal.Api";

    [Fact]
    public void Domain_should_not_depend_on_Application()
    {
        var result = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .Should()
            .NotHaveDependencyOn(Application)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .Should()
            .NotHaveDependencyOn(Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_Api()
    {
        var result = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .Should()
            .NotHaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_EntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.Auth.LoginCommand).Assembly)
            .Should()
            .NotHaveDependencyOn(Infrastructure)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_should_not_depend_on_Api()
    {
        var result = Types.InAssembly(typeof(Application.Auth.LoginCommand).Assembly)
            .Should()
            .NotHaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_should_not_depend_on_EntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(Application.Auth.LoginCommand).Assembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_Api()
    {
        var result = Types.InAssembly(typeof(Infrastructure.Persistence.AppDbContext).Assembly)
            .Should()
            .NotHaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_exceptions_should_be_sealed_or_abstract()
    {
        // The DENOMINATOR, asserted before the rule (Phase 4 sweep, MSP-83).
        //
        // This is the only rule in this file with a `.That()` filter, and a NetArchTest rule whose
        // filter matches nothing passes - vacuously, and indistinguishably from passing on real
        // types. Rename DomainException, move it to another assembly, or change what it inherits,
        // and this test keeps reporting success over an empty set.
        //
        // Five instruments in this repository have already been found reporting on an empty or
        // absent denominator. This is the cheapest possible defence against being the sixth.
        var domainExceptions = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .That()
            .Inherit(typeof(Exception))
            .GetTypes()
            .ToList();

        domainExceptions.Should().NotBeEmpty(
            "this rule is about domain exception types - if the filter matches none, the rule is " +
            "passing over nothing rather than passing");

        var result = Types.InAssembly(typeof(Domain.Suppliers.Supplier).Assembly)
            .That()
            .Inherit(typeof(Exception))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    private static string FailureMessage(TestResult result) =>
        result.FailingTypes is null
            ? "no detail available"
            : string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
