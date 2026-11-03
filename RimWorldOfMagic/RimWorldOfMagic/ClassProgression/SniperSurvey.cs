using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TorannMagic.ClassProgression
{
    public enum SniperShotChoice { None, Headshot, Aimed }

    public sealed class SniperSurvey : IExposable
    {
        private List<int> enemies = new List<int>();
        private List<string> sectors = new List<string>();
        public int Enemies => enemies.Count;
        public int Sectors => sectors.Count;
        internal const string Trial = "rwom.sniper.recon";
        internal static DisciplineProgress Path(Pawn pawn) => ClassIdentity.TryGetState(pawn,out var state) ? state.Find(SniperFoundation.Id) : null;
        internal static string Sector(Map map, IntVec3 cell) => map.uniqueID + "/" + cell.x / 12 + "/" + cell.z / 12;
        internal bool Known(Pawn enemy, string sector) => enemy != null ? enemies.Contains(enemy.thingIDNumber) : sectors.Contains(sector);

        internal static bool Observe(Pawn pawn, LocalTargetInfo target)
        {
            var path = Path(pawn);
            if (path?.Stage != InitiationStage.Foundation || path.CompletedTrials.Contains(Trial) || !JobDriver_SniperObservation.Visible(pawn,target)) return false;
            var progress = path.EnsureSniperSurvey();
            var sector = Sector(pawn.Map,target.Cell);
            if (progress.Known(target.Pawn,sector)) return false;
            if (target.Pawn != null) progress.enemies.Add(target.Pawn.thingIDNumber);
            else progress.sectors.Add(sector);
            if (progress.Enemies >= 10 || progress.Sectors >= 10) path.RecordCompletedTrial(Trial);
            return true;
        }

        internal static bool Choose(Pawn pawn, SniperShotChoice choice)
        {
            var path = Path(pawn);
            if (path?.Stage != InitiationStage.Foundation || path.ShotChoice != SniperShotChoice.None ||
                (choice != SniperShotChoice.Headshot && choice != SniperShotChoice.Aimed)) return false;
            var comp = pawn.GetCompAbilityUserMight();
            var owner = comp?.ExistingMightData?.MightPowersS.FirstOrDefault(p => p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Headshot));
            var ability = choice == SniperShotChoice.Headshot ? TorannMagicDefOf.TM_Headshot : DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_SniperAimedShot");
            if (owner == null || ability == null || !ClassIdentity.Component(pawn).State.TryAdvanceAfterTrial(path.ClassId,InitiationStage.Foundation,Trial)) return false;
            path.ShotChoice = choice;
            if (choice == SniperShotChoice.Headshot) owner.learned = true;
            Restore(comp);
            return true;
        }

        internal static void Restore(CompAbilityUserMight comp)
        {
            var path = Path(comp.Pawn);
            if (path != null && TorannMagicDefOf.TM_AntiArmor != null)
            {
                var existing = comp.ExistingMightData?.MightPowersS.FirstOrDefault(p =>
                    p?.learned == true && p.TMabilityDefs?.Contains(TorannMagicDefOf.TM_AntiArmor) == true);
                if (existing != null) TM_ClassState.AddRuntimeAbility(comp,existing.abilityDef);
            }
            if (path?.ShotChoice == SniperShotChoice.Headshot)
                TM_ClassState.AddRuntimeAbility(comp,comp.ExistingMightData?.MightPowersS.FirstOrDefault(p => p.learned && p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Headshot))?.abilityDef);
            else if (path?.ShotChoice == SniperShotChoice.Aimed)
                TM_ClassState.AddRuntimeAbility(comp,DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_SniperAimedShot"));
            if (HasCombatShot(path))
                TM_ClassState.AddRuntimeAbility(comp, DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_SniperArmorFracture"));
            SniperMastery.Restore(comp, path);
            SniperAscension.Restore(comp, path);
        }

        internal static bool HasCombatShot(DisciplineProgress path)
        {
            return path != null && path.Stage >= InitiationStage.Adept &&
                (path.ShotChoice == SniperShotChoice.Aimed || path.ShotChoice == SniperShotChoice.Headshot);
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref enemies,"enemies",LookMode.Value);
            Scribe_Collections.Look(ref sectors,"sectors",LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { enemies ??= new List<int>(); sectors ??= new List<string>(); }
        }
    }

    public sealed class JobDriver_SniperObservation : JobDriver
    {
        internal static bool Visible(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || !target.IsValid || pawn.Map == null) return false;
            var cell = target.Cell;
            int distance = (cell-pawn.Position).LengthHorizontalSquared;
            if (distance < 144 || distance > 1225 || !cell.InBounds(pawn.Map) || cell.Fogged(pawn.Map)) return false;
            if (target.HasThing && (target.Pawn is not Pawn enemy || !enemy.Spawned || enemy.Map != pawn.Map
                || enemy.Dead || enemy.Downed || enemy.IsPrisoner || !enemy.HostileTo(pawn))) return false;
            return GenSight.LineOfSight(pawn.Position,cell,pawn.Map);
        }
        private static bool Valid(Pawn pawn, LocalTargetInfo target)
        {
            var path = SniperSurvey.Path(pawn);
            return path?.Stage == InitiationStage.Foundation && !path.CompletedTrials.Contains(SniperSurvey.Trial) && Visible(pawn,target)
                && path.Survey?.Known(target.Pawn,SniperSurvey.Sector(pawn.Map,target.Cell)) != true;
        }
        internal static void Target(Pawn pawn)
        {
            var map = pawn.Map;
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns=true, canTargetBuildings=false,
                canTargetItems=false, canTargetLocations=true, mapObjectTargetsMustBeAutoAttackable=false,
                validator=t => pawn.Map == map && Find.CurrentMap == map && Valid(pawn,t.HasThing ? new LocalTargetInfo(t.Thing) : new LocalTargetInfo(t.Cell)) },
                t => { if (pawn.Map == map && Find.CurrentMap == map && Valid(pawn,t))
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("TM_SniperObservation"),t,pawn.Position),JobTag.Misc); });
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => Valid(pawn,job.targetA);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Position != job.targetB.Cell || !Valid(pawn, job.targetA));
            yield return Toils_General.Wait(180,TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => { if (!SniperSurvey.Observe(pawn,job.targetA)) EndJobWith(JobCondition.Incompletable); });
        }
    }

    // Only a single ordinary ballistic shot is borrowed from the weapon. Launchers
    // and custom firing verbs retain their own ammunition and burst rules.
    public abstract class Verb_SniperBallisticShot : Verb_UseAbility
    {
        protected abstract bool OwnsTechnique(DisciplineProgress path);
        protected abstract string RoundName { get; }
        private Verb_Shoot Weapon => CasterPawn?.equipment?.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb as Verb_Shoot;
        public override float EffectiveRange => Weapon?.EffectiveRange ?? 0f;
        public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target)
        {
            var pawn = CasterPawn;
            var gun = Weapon;
            var bullet = gun?.Projectile;
            return pawn?.Spawned == true && !pawn.Downed && OwnsTechnique(SniperSurvey.Path(pawn))
                && gun != null && gun.GetType() == typeof(Verb_Shoot) && bullet?.thingClass == typeof(Bullet) && bullet.projectile?.explosionRadius <= 0
                && target.Pawn is Pawn victim && victim.Spawned && victim.Map == pawn.Map && !victim.Dead && !victim.Downed
                && !victim.IsPrisoner && victim.HostileTo(pawn) && gun.CanHitTargetFrom(root,target);
        }
        protected override bool TryCastShot() => FireWeapon(currentTarget);

        protected bool FireWeapon(LocalTargetInfo target)
        {
            if (CasterPawn == null || !CanHitTargetFrom(CasterPawn.Position, target)) return false;
            var gun = Weapon;
            return new AimedWeaponVerb { roundName=RoundName, caster=CasterPawn, verbProps=gun.verbProps, verbTracker=gun.verbTracker }.Fire(target, gun.Projectile);
        }
        private sealed class AimedWeaponVerb : Verb_Shoot
        {
            public string roundName;
            public override ThingDef Projectile => DefDatabase<ThingDef>.GetNamed(roundName);
            internal bool Fire(LocalTargetInfo target,ThingDef round)
            {
                currentTarget=target;
                var before=SniperWeaponBullet.Source;
                SniperWeaponBullet.Source=round;
                try { return TryCastShot(); }
                finally { SniperWeaponBullet.Source=before; }
            }
        }
    }

    public sealed class Verb_SniperAimedShot : Verb_SniperBallisticShot
    {
        protected override bool OwnsTechnique(DisciplineProgress path) => path?.ShotChoice == SniperShotChoice.Aimed;
        protected override string RoundName => "TM_SniperAimedRound";
    }

    public abstract class SniperWeaponBullet : Bullet
    {
        [System.ThreadStatic] internal static ThingDef Source;
        public override void PostMake()
        {
            // Map listers must register the real ammunition definition from the start.
            if (Source != null) def=Source;
            base.PostMake();
        }
    }

    public sealed class SniperAimedBullet : SniperWeaponBullet
    {
        private int shotDamage;
        public override int DamageAmount => shotDamage > 0 ? shotDamage : base.DamageAmount;
        public override void Launch(Thing launcher,Vector3 origin,LocalTargetInfo usedTarget,LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,bool preventFriendlyFire=false,Thing equipment=null,ThingDef targetCoverDef=null)
        {
            var percent = SniperTraining.DamagePercent(SniperTraining.Rank(launcher as Pawn, breach: false));
            shotDamage = Mathf.Max(1, Mathf.RoundToInt(def.projectile.GetDamageAmount(equipment) * percent / 100f));
            base.Launch(launcher,origin,usedTarget,intendedTarget,hitFlags,preventFriendlyFire,equipment,targetCoverDef);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shotDamage,"sniperAimedDamage");
        }
    }
}
