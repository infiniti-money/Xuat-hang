namespace Hisa.App;

public sealed partial class WarehouseService
{
    private static void ValidateMotorSerialsForExport(Slip slip,IEnumerable<Slip> history)
    {
        if(slip.Status=="delivered")MotorSerials.Validate(slip,history);
    }
}
