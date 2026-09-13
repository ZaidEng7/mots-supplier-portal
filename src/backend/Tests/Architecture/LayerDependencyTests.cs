// The dependency direction between the four layers, enforced rather than described.
//
// The domain depends on nothing outward. The application layer depends only on the domain. Only the
// infrastructure and the web layer may depend on the database mapper or the web framework.
//
//
// ONE RULE HERE FILTERS, SO ITS DENOMINATOR IS ASSERTED FIRST
//
// The rule that domain exceptions must be sealed is the only one with a filter, and a rule of this kind whose
// filter matches nothing PASSES, vacuously, and indistinguishably from passing on real types.
//
// Rename the base exception, move it to another assembly, or change what it inherits, and the rule would keep
// reporting success over an empty set.
//
// Five instruments in this repository have already been found reporting on an absent or empty denominator. The
// non-empty assertion before the rule is the cheapest possible defence against being the sixth.

namespace MotsSupplierPortal.Tests.Architecture;

using FluentAssertions;
using NetArchTest.Rules;

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
