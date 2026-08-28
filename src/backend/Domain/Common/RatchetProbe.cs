namespace MotsSupplierPortal.Domain.Common;

/// TEMPORARY - deliberately uncovered code to prove the coverage ratchet fails. Removed in the
/// next commit.
public static class RatchetProbe
{
    public static int Doubled(int value) => value * 2;
    public static int Tripled(int value) => value * 3;
    public static int Quadrupled(int value) => value * 4;
    public static string Describe(int value) => value > 0 ? "positive" : "non-positive";
    public static bool IsEven(int value) => value % 2 == 0;
}
