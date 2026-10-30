namespace Hisa.App;

/// <summary>Fixture data only. Production never invents a serial number.</summary>
internal static class SerialTestData
{
    internal static void Fill(Slip slip,string prefix)
    {
        slip.MotorSerials=MotorSerials.BuildRows(slip);
        for(int i=0;i<slip.MotorSerials.Count;i++)slip.MotorSerials[i].SerialNumber=prefix+"-"+(i+1).ToString("0000");
    }
}
