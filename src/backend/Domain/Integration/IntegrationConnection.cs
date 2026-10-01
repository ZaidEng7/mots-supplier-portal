// Where another system lives, and what opens it.
//
// WHY THIS IS A ROW RATHER THAN CONFIGURATION. The address of somebody else's server is not a property of our
// deployment - it changes when THEY move, on their schedule, and asking for a redeploy every time is how an
// integration stays broken for a week over a URL. The credential moves with it, because a new server issues a new
// key.
//
// THE SECRET IS STORED ENCRYPTED AND NEVER READ BACK OUT. Nothing in this product returns it: the screen shows
// whether one is set and when it changed, and offers to replace it. A credential that can be read out of a screen
// is one that ends up in a screenshot, a support ticket, or on a projector - and nobody needs to read it, they
// need to know it is there and be able to change it.
//
// THE KEY IS STABLE AND THE DISPLAY NAME IS NOT. Code looks the connection up by Key; people read DisplayName.
// Renaming "Seven Gates ERP" to something else must not detach it from the code that uses it.
//
// A BLANK BASE URL MEANS "NOT CONFIGURED HERE", and that is the whole precedence rule. A deployment that has
// never opened the screen keeps working from its own settings; the moment somebody saves an address, this row
// takes over completely. Field-by-field precedence was the alternative and it is the kind of rule nobody can hold
// in their head: an address from one place and a credential from another, with no screen able to say which.
//
// THE LAST TEST RESULT IS STORED rather than only shown. Somebody changes an address on Friday, tests it, and
// leaves; the next person needs to know it was tested and what happened, not just that the fields look filled in.
//
// WRITING SUPPLIERS TO THE ERP IS OFF UNTIL SOMEBODY TURNS IT ON. CreateSuppliersInErp is the one switch for every
// write the portal makes to the ERP's supplier records - creating an approved supplier there, with its address,
// contact and website user - and it starts off, on the seeded row and on any new one. A write to somebody else's
// system cannot be taken back from here: the ERP makes a second supplier for a second request, and removing one is
// the ERP team's work. Reading does not depend on it; the import and the connection test run while it is off.
//
// DefaultSupplierGroup is the ERP supplier group that every supplier created from here is filed under: one group, set
// once on this screen, and not one per currency, as the owner decided. The ERP requires a group on every supplier and
// the portal's categories do not map to the ERP's groups, so the switch cannot be turned on without one.

namespace MotsSupplierPortal.Domain.Integration;

using MotsSupplierPortal.Domain.Suppliers;

public sealed class IntegrationConnection
{
    public const string ErpKey = "erp";

    // An ERP supplier group is referred to by its record name, which the ERP stores in at most 140 characters.
    public const int SupplierGroupMaxLength = 140;

    public Guid Id { get; private init; }
    public string Key { get; private init; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string BaseUrl { get; private set; } = string.Empty;
    public string ApiKey { get; private set; } = string.Empty;
    public string? SecretCipher { get; private set; }
    public DateTimeOffset? SecretSetAt { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? LastTestedAt { get; private set; }
    public bool? LastTestSucceeded { get; private set; }
    public string? LastTestDetail { get; private set; }
    public DateTimeOffset? LastSyncAt { get; private set; }
    public IntegrationSyncOutcome? LastSyncOutcome { get; private set; }
    public string? LastSyncSummary { get; private set; }
    public bool CreateSuppliersInErp { get; private set; }
    public string? DefaultSupplierGroup { get; private set; }

    public bool IsConfiguredHere => !string.IsNullOrWhiteSpace(BaseUrl);

    public bool HasSecret => SecretCipher is not null;

    public static IntegrationConnection Create(string key, string displayName) => new()
    {
        Id = Guid.CreateVersion7(),
        Key = key,
        DisplayName = displayName,
    };

    // A null secret means "leave the one that is there", not "clear it".
    //
    // The screen cannot show the stored secret, so its field arrives empty on every edit. Treating empty as
    // "clear it" would wipe the credential every time somebody corrected a typo in the URL - and the failure
    // would not appear until the next run.
    public void Update(
        string baseUrl,
        string apiKey,
        string? secretCipher,
        bool isEnabled,
        Guid updatedByUserId)
    {
        BaseUrl = baseUrl.Trim();
        ApiKey = apiKey.Trim();
        IsEnabled = isEnabled;

        if (secretCipher is not null)
        {
            SecretCipher = secretCipher;
            SecretSetAt = DateTimeOffset.UtcNow;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    // Switching the supplier writes on or off, with the group they file suppliers under.
    //
    // TURNING IT ON NEEDS A GROUP, because the ERP refuses a supplier without one and every create would fail on it.
    // While the switch is off the group may be set or cleared, so it can be chosen before anything is written. A blank
    // group is stored as none, and a name longer than the ERP could hold is refused rather than saved to fail later.
    //
    // It is recorded as a change to the connection, with who made it, because turning on writes to another system is
    // the change somebody will later ask about.
    public void SetSupplierCreation(bool createSuppliersInErp, string? defaultSupplierGroup, Guid updatedByUserId)
    {
        var group = string.IsNullOrWhiteSpace(defaultSupplierGroup) ? null : defaultSupplierGroup.Trim();

        if (createSuppliersInErp && group is null)
        {
            throw new DomainException("Creating suppliers in the ERP needs a default ERP supplier group.");
        }

        if (group is { Length: > SupplierGroupMaxLength })
        {
            throw new DomainException(
                $"An ERP supplier group's name is at most {SupplierGroupMaxLength} characters.");
        }

        CreateSuppliersInErp = createSuppliersInErp;
        DefaultSupplierGroup = group;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    // The last import is recorded on the connection, whoever or whatever ran it.
    //
    // A scheduled job that fails is silent by nature: nobody is watching when it runs, and a run that did
    // nothing looks exactly like a run that found nothing to do. Putting the outcome where the connection is managed
    // means the person who would fix it sees it the next time they look, without knowing a job exists.
    //
    // THREE OUTCOMES, NOT TWO. A run that could not finish and a run that finished but left something for a person -
    // suspensions held back over a suspicious list, a probable rename, a supplier that failed - are different mornings
    // for whoever reads the card. With only succeeded-or-not, both showed the same warning and the outright failure
    // looked no more urgent than a rename waiting to be checked.
    //
    // The summary is truncated rather than rejected, because losing the record of a failure over the length of its
    // message would defeat the point of keeping it.
    public void RecordSync(IntegrationSyncOutcome outcome, string summary)
    {
        LastSyncAt = DateTimeOffset.UtcNow;
        LastSyncOutcome = outcome;
        LastSyncSummary = summary.Length <= 1000 ? summary : summary[..999] + "…";
    }

    // The detail is cut to the column's thousand characters rather than refused: it quotes an address and, when the ERP
    // cannot be reached, the network's own error text, and a test that failed to save its own result would leave the
    // screen showing the previous one.
    public void RecordTest(bool succeeded, string? detail)
    {
        LastTestedAt = DateTimeOffset.UtcNow;
        LastTestSucceeded = succeeded;
        LastTestDetail = detail is { Length: > 1000 } ? detail[..1000] : detail;
    }
}

public enum IntegrationSyncOutcome
{
    Succeeded,
    NeedsAttention,
    Failed,
}
