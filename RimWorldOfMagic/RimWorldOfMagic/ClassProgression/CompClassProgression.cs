using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace TorannMagic.ClassProgression
{
    public sealed class CompClassProgression : ThingComp
    {
        private ClassProgressionState state;
        private SpiritRebirthState spiritRebirth;
        public SpiritRebirthState SpiritRebirth => spiritRebirth;
        private SpiritRebirthRitualTransaction spiritRitual;
        internal SpiritRebirthRitualTransaction SpiritRitual => spiritRitual;
        internal bool TryBeginSpiritRitual(SpiritRebirthRitualTransaction receipt)
        {
            if (spiritRitual != null || receipt == null || !ReferenceEquals(receipt.Original, parent)) return false;
            spiritRitual = receipt;
            return true;
        }
        internal bool EnsureSpiritRitualPending(SpiritRebirthRitualTransaction receipt)
        {
            var pawn = parent as Pawn;
            if (receipt == null || !ReferenceEquals(spiritRitual, receipt) || !ReferenceEquals(receipt.Original, pawn)
                || pawn == null || pawn.Dead || pawn.Destroyed) return false;
            if (spiritRebirth == null) spiritRebirth = SpiritRebirthState.CreateRitualPending(pawn);
            return SpiritRebirthState.TryGet(pawn, out var state) && state.Form == SpiritRebirthForm.Dormant;
        }
        internal bool ClearRefundedSpiritRitual(SpiritRebirthRitualTransaction receipt)
        {
            if (!ReferenceEquals(spiritRitual, receipt) || receipt == null || !receipt.FullyRefunded) return false;
            spiritRitual = null;
            return true;
        }

        // Reserved for a future explicit corpse conversion transaction. No spawn,
        // loading, trait, or legacy migration path calls this entry point.
        internal bool BeginSpiritRebirth()
        {
            var pawn = parent as Pawn;
            if (spiritRebirth != null || pawn == null || pawn.Dead || pawn.Destroyed
                || !ReferenceEquals(ClassIdentity.Component(pawn), this)) return false;
            spiritRebirth = new SpiritRebirthState(pawn);
            return true;
        }
        private int tomeQuestAvailableAtTick;
        public int TomeQuestAvailableAtTick => tomeQuestAvailableAtTick;
        public void DelayNextTomeQuest(int now)
            => tomeQuestAvailableAtTick = System.Math.Max(tomeQuestAvailableAtTick, now + 60000);
        public ClassProgressionState State => state;
        public bool UsesClassicProgression => state == null;

        // Explicit entry point only. Existing saves, spawn and opening the grimoire
        // do not call it. Migration requires its own preservation-checked transaction.
        public bool BeginNewProgression(Vocation vocation)
        {
            if (state != null || vocation == Vocation.None || (vocation & ~Vocation.WarriorMage) != 0) return false;
            state = new ClassProgressionState(vocation);
            return true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            var pawn = parent as Pawn;
            if (pawn?.IsColonistPlayerControlled != true) yield break;
            if (ClassIdentity.TryGetState(pawn, out _))
            {
                yield return new Command_Action {
                    defaultLabel = "TM_ProgressionOpenGrimoire".Translate(),
                    defaultDesc = "TM_ProgressionOpenGrimoireDesc".Translate(),
                    icon = Verse.TexButton.IconBook, action = () => Window_TMGrimoire.OpenFor(pawn)
                };
            }
            else if (ClassEnrollment.CanEnrollMage(pawn))
            {
                yield return new Command_Action {
                    defaultLabel = "TM_ProgressionEnrollMage".Translate(),
                    defaultDesc = "TM_ProgressionEnrollMageDesc".Translate(),
                    icon = Verse.TexButton.IconBook,
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "TM_ProgressionEnrollMageDesc".Translate(), () => {
                            if (ClassEnrollment.TryEnrollMage(pawn)) Window_TMGrimoire.OpenFor(pawn);
                        }))
                };
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (state?.Find("rwom.summoner")?.Stage == InitiationStage.Foundation && Find.TickManager.TicksGame % 30 == 0)
                SummonerTrials.Observe(parent as Pawn, Find.TickManager.TicksGame);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            var path = state?.Find("rwom.summoner");
            if (path != null) path.SummonerTrials.LastActive = false;
            base.PostDeSpawn(map, mode);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            var summoner = state?.Find("rwom.summoner");
            if (summoner != null) summoner.SummonerTrials.LastActive = false;
            NecromancerControlTrial.PauseOwner(parent as Pawn);
            LichTransformation.RestoreHealth(parent as Pawn);
            SynchronizeHealthDisplay();
        }

        public void SynchronizeHealthDisplay()
        {
            var pawn = parent as Pawn;
            if (pawn?.health?.hediffSet == null || !ClassIdentity.TryGetState(pawn, out var current)) return;
            var markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("TM_ClassDiscipline");
            if (markerDef == null) return;
            var wanted = new HashSet<string>(current.Disciplines.Where(p => p != null
                && !string.IsNullOrWhiteSpace(p.ClassId)).Select(p => p.ClassId));
            var seen = new HashSet<string>();
            foreach (var marker in pawn.health.hediffSet.hediffs.OfType<Hediff_ClassDiscipline>().ToList())
                if (!wanted.Contains(marker.profileId) || !seen.Add(marker.profileId)) pawn.health.RemoveHediff(marker);
            foreach (string id in wanted)
                if (!seen.Contains(id))
                {
                    var marker = HediffMaker.MakeHediff(markerDef, pawn) as Hediff_ClassDiscipline;
                    if (marker == null) continue;
                    marker.profileId = id;
                    pawn.health.AddHediff(marker);
                }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && state == null && spiritRebirth == null && spiritRitual == null) return;
            Scribe_Deep.Look(ref state, "TM_classProgression");
            Scribe_Deep.Look(ref spiritRebirth, "TM_spiritRebirth");
            Scribe_Deep.Look(ref spiritRitual, "TM_spiritRitual");
            Scribe_Values.Look(ref tomeQuestAvailableAtTick, "TM_tomeQuestAvailableAtTick", 0);
        }
    }

    [StaticConstructorOnStartup]
    public static class ClassProgressionBootstrap
    {
        static ClassProgressionBootstrap()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading) EnsureComp(def);
        }

        public static void EnsureComp(ThingDef def)
        {
            if (def?.race == null || !def.race.Humanlike) return;
            if (def.comps == null) def.comps = new List<CompProperties>();
            if (!def.comps.Any(p => p?.compClass == typeof(CompClassProgression)))
                def.comps.Add(new CompProperties { compClass = typeof(CompClassProgression) });
        }
    }
}

