using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using Verse;
using Verse.AI;

namespace RWoM.RegressionTests;

// Player report: the monk's Meditate autocast re-proc'd its activation every
// cooldown even mid-task with nothing to gain. The utility gate must fire only
// when meditation would actually do something (heal, purge, break-risk relief,
// or chi/rest building while idle) and never interrupt real work for a trickle.
internal static class MonkMeditateTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("MonkMeditate.HealthyIdleBuildsChi", HealthyIdle);
        yield return new("MonkMeditate.WorkingPawnNotInterruptedForTrickle", WorkingTrickle);
        yield return new("MonkMeditate.HighChiSkips", HighChi);
        yield return new("MonkMeditate.TiredBuildsRest", Tired);
        yield return new("MonkMeditate.InjuryAlwaysWorthIt", Injured);
    }

    static Pawn Pawn(bool working, float rest = 1f)
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "MonkMeditatePawn"; def.category = ThingCategory.Pawn;
        def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
        var pawn = new Pawn { def = def };
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.needs = new Pawn_NeedsTracker(pawn);
        pawn.needs.rest = new Need_Rest(pawn) { def = new NeedDef { defName = "MonkMeditateRest" } };
        pawn.needs.rest.CurLevel = rest;
        pawn.jobs = new Pawn_JobTracker(pawn);
        if (working)
        {
            // Any work-giver job registers as "busy with real work".
            var job = new Job { def = new JobDef { defName = "MonkMeditateFakeWork" } };
            job.workGiverDef = new WorkGiverDef { defName = "MonkMeditateFakeWg" };
            FleshStatTests.Set(pawn.jobs, "curJob", job);
        }
        return pawn;
    }

    private static readonly HediffDef ChiDef = new HediffDef { defName = "TM_ChiHD" };

    static void SetChi(Pawn pawn, float severity)
    {
        var chi = new Hediff { def = ChiDef, pawn = pawn };
        typeof(Hediff).GetField("severityInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chi, severity);
        pawn.health.hediffSet.hediffs.Add(chi);
    }

    static bool Worth(Pawn pawn)
    {
        HediffDef old = TorannMagicDefOf.TM_ChiHD;
        TorannMagicDefOf.TM_ChiHD = ChiDef;
        try { return TM_Calc.MonkMeditateWorthAutocast(pawn); }
        finally { TorannMagicDefOf.TM_ChiHD = old; }
    }

    static void HealthyIdle(RuntimeFixture _)
    {
        var pawn = Pawn(working: false);
        Check.Equal(true, Worth(pawn), "an idle healthy monk may build chi");
    }

    static void WorkingTrickle(RuntimeFixture _)
    {
        var pawn = Pawn(working: true);
        Check.Equal(false, Worth(pawn), "a working pawn is never interrupted for a chi trickle");
    }

    static void HighChi(RuntimeFixture _)
    {
        var pawn = Pawn(working: false);
        SetChi(pawn, 90f);
        Check.Equal(false, Worth(pawn), "high chi skips the cast even when idle");
    }

    static void Tired(RuntimeFixture _)
    {
        var pawn = Pawn(working: true, rest: .2f);
        Check.Equal(true, Worth(pawn), "a tired working monk still benefits from the rest trickle");
    }

    static void Injured(RuntimeFixture _)
    {
        var pawn = Pawn(working: true);
        var injury = new Hediff_Injury
        {
            def = new HediffDef { defName = "MonkMeditateScratch", hediffClass = typeof(Hediff_Injury) },
            pawn = pawn, Severity = 5f
        };
        var part = new BodyPartRecord { def = new BodyPartDef { defName = "MonkMeditateTorso" } };
        FleshStatTests.Set(injury, "part", part);
        pawn.health.hediffSet.hediffs.Add(injury);
        Check.Equal(true, Worth(pawn), "wounds always justify the cast, even mid-task");
    }
}
