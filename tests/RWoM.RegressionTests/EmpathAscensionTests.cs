using System.Collections.Generic;
internal static class EmpathAscensionTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("EmpathAscension.ActualWorkSaveInterruptionAndGemPayment", f => ClassAscensionJobTests.Work(f, empath: true));
    }
}
