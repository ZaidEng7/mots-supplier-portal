// The ERP writer as the running application registers it.
//
// THE 30-SECOND LIMIT LIVES IN STARTUP, NOT IN THE WRITER, because a typed client's timeout is set where the client is
// registered. No unit test can see that line, and without it the writer would wait HttpClient's default of 100 seconds
// on every create - long enough for a run of the push to hang on one supplier. So it is read back from the host's own
// client factory, under the name a typed client is registered by: its interface's.
//
// THE WRITE SWITCH REACHES THE WRITER THROUGH THE CONNECTION, read from the connection's row by ErpConnectionProvider,
// and the default supplier group reaches the push job the same way. The writer's and the job's own tests hand them a
// connection; these read one from the database, so a provider that dropped the switch or the group, or invented them,
// fails here. The row is shared with every other integration class, so each test puts it back as the
// seed left it - no address and writes off - whatever happens.
//
// Nothing here calls the ERP. Resolving the writer builds it; only a call would send anything.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErpSupplierRegistrarRegistrationTests(PostgresApiFixture fixture)
{
    [Fact]
    public void The_writer_is_the_registrar_the_application_resolves()
    {
        using var scope = fixture.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IErpSupplierRegistrar>().Should().BeOfType<ErpSupplierRegistrar>();
    }

    [Fact]
    public void The_writers_client_gives_each_call_thirty_seconds()
    {
        var client = fixture.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IErpSupplierRegistrar));

        client.Timeout.Should().Be(TimeSpan.FromSeconds(30),
            "the integration architecture sets 30 seconds per call, and HttpClient's own default is 100");
    }

    [Fact]
    public async Task The_connection_carries_the_write_switch_from_the_row()
    {
        try
        {
            await SetRowAsync(createSuppliersInErp: true, "Local Suppliers - SYP");

            var connection = (await CurrentConnectionAsync())!;
            connection.CreateSuppliersInErp.Should().BeTrue();
            connection.DefaultSupplierGroup.Should().Be(
                "Local Suppliers - SYP", "the push reads the group from the connection, together with the switch");
        }
        finally
        {
            await SetRowAsync(createSuppliersInErp: false, null, address: string.Empty);
        }
    }

    [Fact]
    public async Task Writes_stay_off_on_a_row_that_was_never_switched_on()
    {
        try
        {
            await SetRowAsync(createSuppliersInErp: false, null);

            var connection = (await CurrentConnectionAsync())!;
            connection.CreateSuppliersInErp.Should().BeFalse();
            connection.DefaultSupplierGroup.Should().BeNull();
        }
        finally
        {
            await SetRowAsync(createSuppliersInErp: false, null, address: string.Empty);
        }
    }

    private async Task SetRowAsync(bool createSuppliersInErp, string? group, string address = "http://erp.example:8001")
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.IntegrationConnections.SingleAsync(c => c.Key == IntegrationConnection.ErpKey);
        row.Update(address, address.Length == 0 ? string.Empty : "the-key", null, isEnabled: false, Guid.Empty);
        row.SetSupplierCreation(createSuppliersInErp, group, Guid.Empty);
        await db.SaveChangesAsync();
    }

    private async Task<ErpConnection?> CurrentConnectionAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IErpConnectionProvider>().CurrentAsync(CancellationToken.None);
    }
}
