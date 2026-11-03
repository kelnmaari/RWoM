using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorannMagic.ClassProgression
{
    internal static class SpiritHostedMartial
    {
        internal const string Maneuver = "TM_SpiritCastManeuver", SelfAid = "TM_SpiritCastFirstAid", Pommel = "TM_SpiritCastPommelStrike";
        internal const string Headshot = "TM_SpiritCastHeadshot";
        internal const string DisablingShot = "TM_SpiritCastDisablingShot", AntiArmor = "TM_SpiritCastAntiArmor";
        private const string Aimed = "TM_SpiritCastAimedShot", Fracture = "TM_SpiritCastArmorFracture";
        private const string ShadowSlayer = "TM_SpiritCastShadowSlayer";
        internal const string Sweep = "TM_SpiritCastGladiatorSweep";
        internal const string Grapple = "TM_SpiritCastGrapple";
        internal static bool GrappleRank(TMAbilityDef def) => def != null && (def == TorannMagicDefOf.TM_Grapple ||
            def == TorannMagicDefOf.TM_Grapple_I || def == TorannMagicDefOf.TM_Grapple_II || def == TorannMagicDefOf.TM_Grapple_III);
        internal static bool HasNativeOrder(string job) => job == Sweep || job == Grapple;
        internal static bool IsSniperShot(string job) => job==Headshot || job==DisablingShot || job==AntiArmor || job==Aimed || job==Fracture;
        internal static bool CanFire(Pawn body, MightAbility owner, Pawn target) =>
            Verb_SniperBallisticShot.FromHost(body, owner) is Verb_SniperBallisticShot ballistic
                ? target != null && ballistic.CanHitTargetFrom(body.Position, target) : Verb_SniperShot.CanFire(body, owner, target);
        internal static bool Fire(Pawn body, MightAbility owner, Pawn target) =>
            Verb_SniperBallisticShot.FromHost(body, owner) is Verb_SniperBallisticShot ballistic
                ? ballistic.FireFromHost(target) : Verb_SniperShot.FireFromHost(body, owner, target);
        private const string ManeuverEffect = "TM_GladiatorManeuverHD";

        internal static MightAbility Find(Pawn body, Pawn soul, string job)
        {
            bool native = body == soul && HasNativeOrder(job);
            if (native)
            {
                if (SpiritRebirthActionPolicy.Marked(body)) return null;
            }
            else if (!SpiritRebirthState.TryGetPossessingSpirit(body, out var held) || held != soul ||
                !SpiritRebirthState.TryGet(soul, out var state) || state.DepletionPending) return null;
            bool sprint = job == SpiritHostedStance.Job;
            TMAbilityDef def;
            if (job == Headshot) def = TorannMagicDefOf.TM_Headshot;
            else if (job == DisablingShot) def = TorannMagicDefOf.TM_DisablingShot;
            else if (job == AntiArmor) def = TorannMagicDefOf.TM_AntiArmor;
            else if (job == ShadowSlayer) def = TorannMagicDefOf.TM_ShadowSlayer;
            else if (job == Sweep) def = DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_GladiatorSweep");
            else if (job == Grapple) def = TorannMagicDefOf.TM_Grapple;
            else if (job == SniperOpeningAbility.JobName) def = DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_SniperSingleOpening");
            else if (job == Aimed || job == Fracture) def = DefDatabase<TMAbilityDef>.GetNamedSilentFail(job == Aimed ? "TM_SniperAimedShot" : "TM_SniperArmorFracture");
            else if (job == SelfAid) def = TorannMagicDefOf.TM_FirstAid;
            else if (job == Pommel) def = TorannMagicDefOf.TM_PommelStrike;
            else if (job == Maneuver && ClassIdentity.TryGetState(soul, out var path) &&
                path.Find(JobDriver_GladiatorInitiation.ClassId)?.GladiatorManeuver == true)
                def = DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_GladiatorManeuver");
            else if (SpiritHostedStance.BaseAbility(job) is TMAbilityDef stance) def = stance;
            else return null;

            bool Match(PawnAbility power) => power != null && (sprint ? SpiritHostedStance.SprintFamily(power.Def as TMAbilityDef) : power.Def == def || job==DisablingShot &&
                (power.Def==TorannMagicDefOf.TM_DisablingShot_I || power.Def==TorannMagicDefOf.TM_DisablingShot_II || power.Def==TorannMagicDefOf.TM_DisablingShot_III) || job == Grapple && GrappleRank(power.Def as TMAbilityDef));
            bool Local(CompAbilityUser comp) => comp?.AbilityData.AllPowers.Any(Match) == true;
            if (def == null || !native && (Local(body.GetCompAbilityUserMagic()) || Local(body.GetCompAbilityUserMight()))) return null;
            var source = soul.GetCompAbilityUserMight();
            var powers = source?.AbilityData.Powers.OfType<MightAbility>()
                .Where(a => Match(a) && ReferenceEquals(a.AbilityUser, source)).Take(2).ToList();
            return powers?.Count == 1 ? powers[0] : null;
        }

        internal static bool Ready(Pawn body, MightAbility ability, string job)
        {
            if (ability == null || !body.Spawned || body.Dead || body.Downed || body.InMentalState || body.IsPrisoner ||
                (ability.Pawn != body && body.Faction != Faction.OfPlayer) || ability.CooldownTicksLeft > 0) return false;
            var def = ability.mightDef;
            var might = ability.MightUser;
            if (job == SniperOpeningAbility.JobName) return ability is SniperOpeningAbility opening &&
                JobDriver_SniperSingleOpening.Ready(body, opening.Pawn) && opening.VerbFor(body).HasCompatibleWeapon;
            if (SpiritHostedStance.BaseAbility(job) != null) return SpiritHostedStance.Ready(body,ability);
            if (!def.consumeEnergy || def.staminaCost <= 0 || def.chiCost != 0 || def.bloodCost != 0 || def.manaCost != 0 ||
                def.requiredNeed != null || def.requiredHediff != null || might.ExistingMightData == null ||
                might.Stamina == null || might.Stamina.CurLevel < might.ActualStaminaCost(def)) return false;
            if (IsSniperShot(job)) return !body.WorkTagIsDisabled(WorkTags.Violent) &&
                (Verb_SniperBallisticShot.FromHost(body, ability)?.HasCompatibleWeapon ?? (Verb_SniperShot.WeaponVerb(body) != null));
            if (job == ShadowSlayer) return Verb_ShadowSlayerCloak.CanProject(body);
            if (job == Sweep) return body.equipment?.Primary?.def.IsMeleeWeapon == true &&
                !body.WorkTagIsDisabled(WorkTags.Violent) && JobDriver_GladiatorDefense.Path(ability.Pawn)?.Attack == GladiatorAttack.Sweep;
            if (job == Grapple) return !body.WorkTagIsDisabled(WorkTags.Violent) && ClassIdentity.Component(body)?.Circle == null;
            if (job == Pommel) return !body.WorkTagIsDisabled(WorkTags.Violent) && body.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
            if (job == SelfAid) return Verb_FirstAid.EligibleWounds(body).Any();
            var effect = DefDatabase<HediffDef>.GetNamedSilentFail(ManeuverEffect);
            return job == Maneuver && ClassIdentity.Component(body)?.Circle == null &&
                effect?.comps?.Any(c => c.compClass == typeof(HediffComp_SpiritCastOrigin)) == true &&
                !body.health.hediffSet.HasHediff(effect);
        }

        internal static bool CanStrike(Pawn body, Pawn target, MightAbility ability) =>
            target?.Spawned == true && !target.Dead && target != body && target.Map == body.Map && target.HostileTo(body) &&
            body.Position.InHorDistOf(target.Position, ability.Def.MainVerb.range) &&
            body.CanReachImmediate(target, PathEndMode.Touch);

        internal static bool CanMelee(Pawn body, Pawn target, MightAbility ability,string job)
        {
            if (target?.Spawned != true || target.Map != body.Map) return false;
            if (job == Grapple) return Verb_Grapple.ForHost(body,ability).CanHitTargetFrom(body.Position,target);
            if (job == Sweep) return Verb_GladiatorSweep.ForHost(body,ability).CanHitTargetFrom(body.Position,target);
            return CanStrike(body,target,ability);
        }
        internal static bool UsesCombatTarget(string job) => job == Sweep || job == Grapple || job == Pommel;

        internal static bool Strike(Pawn body, Pawn soul, Pawn target,string job = Pommel)
        {
            var ability = Find(body, soul, job);
            if (!Ready(body, ability, job) || !CanMelee(body, target, ability,job)) return false;
            if (job == Sweep)
            {
                if (!Verb_GladiatorSweep.ForHost(body,ability).Strike(target)) return false;
            }
            else if (job == Grapple)
            {
                if (!Verb_Grapple.ForHost(body,ability).FireFromHost(target)) return false;
            }
            else Verb_PommelStrike.Strike(body, target, ability.MightUser);
            ability.PostAbilityAttempt();
            return true;
        }

        internal static bool Apply(Pawn body, Pawn soul, string job)
        {
            var ability = Find(body, soul, job);
            if (job == Pommel || !Ready(body, ability, job)) return false;
            if (SpiritHostedStance.BaseAbility(job) != null) return SpiritHostedStance.Toggle(body,ability);
            if (job == ShadowSlayer)
            {
                if (!Verb_ShadowSlayerCloak.Activate(body,soul)) return false;
            }
            else if (job == SelfAid)
            {
                var skills = ability.MightUser.MightData.MightPowerSkill_FirstAid;
                int Rank(string label) => skills.FirstOrDefault(s => s.label == label)?.level ?? 0;
                if (!Verb_FirstAid.Tend(body, Rank("TM_FirstAid_pwr"), Rank("TM_FirstAid_ver"))) return false;
            }
            else
            {
                var effect = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(ManeuverEffect), body);
                effect.TryGetComp<HediffComp_SpiritCastOrigin>().Bind(soul);
                effect.Severity = 1f;
                body.health.AddHediff(effect);
                if (!body.health.hediffSet.hediffs.Contains(effect)) return false;
            }
            ability.PostAbilityAttempt();
            return true;
        }

        private static void FindTarget(Pawn body, Pawn soul, JobDef job, System.Func<Pawn, bool> valid)
        {
            Verse.Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = false,
                canTargetItems = false, validator = t => valid(t.Thing as Pawn) }, target => {
                if (body.CurJobDef != job && valid(target.Pawn))
                {
                    Job order;
                    if (job.defName == SniperOpeningAbility.JobName)
                    {
                        if (!(Find(body,soul,job.defName) is SniperOpeningAbility opening)) return;
                        order = opening.MakeJobFor(body,target);
                    }
                    else order = JobMaker.MakeJob(job, soul, target);
                    body.jobs.TryTakeOrderedJob(order, JobTag.Misc);
                }
            }, highlightAction: target => {
                if (IsSniperShot(job.defName) && Find(body,soul,job.defName) is MightAbility shot)
                    GenDraw.DrawRadiusRing(body.Position,Verb_SniperBallisticShot.FromHost(body,shot)?.EffectiveRange ?? shot.mightDef.MainVerb.range);
                else if(job.defName == SniperOpeningAbility.JobName && Find(body,soul,job.defName) is SniperOpeningAbility opening)
                    GenDraw.DrawRadiusRing(body.Position, opening.VerbFor(body).EffectiveRange);
                else if(job.defName == Grapple && Find(body,soul,job.defName) is MightAbility hook)
                    GenDraw.DrawRadiusRing(body.Position,hook.mightDef.MainVerb.range);
            }, targetValidator: target => valid(target.Pawn), caster: body);
        }

        internal static IEnumerable<Gizmo> Commands(IEnumerable<Gizmo> existing, Pawn body)
        {
            foreach (var command in existing) yield return command;
            if (!SpiritRebirthState.TryGetPossessingSpirit(body, out var soul)) yield break;
            var cleave = GladiatorMastery.AutomaticSource(body,body.GetCompAbilityUserMight());
            if (cleave?.Pawn == soul && body.Faction == Faction.OfPlayer)
                yield return cleave.MakeCleaveCommand(() => {
                    if (body.Spawned && !body.Dead && body.Faction == Faction.OfPlayer &&
                        GladiatorMastery.AutomaticSource(body,body.GetCompAbilityUserMight()) == cleave)
                        cleave.useCleaveToggle = !cleave.useCleaveToggle;
                });
            foreach (var name in new[] { Maneuver, SelfAid, Pommel, Sweep, Grapple, Headshot, DisablingShot, AntiArmor, Aimed, Fracture, ShadowSlayer, SniperOpeningAbility.JobName, SpiritHostedStance.Job, SpiritHostedStance.StrongBack, SpiritHostedStance.HeavyBlow,
                SpiritHostedStance.ThickSkin, SpiritHostedStance.FightersFocus, SpiritHostedStance.GearRepair, SpiritHostedStance.InnerHealing })
            {
                var owner = Find(body, soul, name);
                var job = DefDatabase<JobDef>.GetNamedSilentFail(name);
                if (owner == null || job == null) continue;
                var cmd = new Command_Action { defaultLabel = owner.Def.LabelCap, defaultDesc = owner.Def.description,
                    icon = owner.Def.uiIcon, action = () => {
                        if (body.CurJobDef == job || !Ready(body, Find(body, soul, name), name)) return;
                        if (name == SniperOpeningAbility.JobName && owner is SniperOpeningAbility opening)
                            FindTarget(body,soul,job,target => target!=null && Ready(body,Find(body,soul,name),name) &&
                                opening.VerbFor(body).CanHitTargetFrom(body.Position,target));
                        else if (IsSniperShot(name))
                        {
                            bool Valid(Pawn target) => Ready(body, Find(body,soul,name),name) && CanFire(body,owner,target);
                            FindTarget(body,soul,job,Valid);
                        }
                        else if (UsesCombatTarget(name))
                        {
                            bool Valid(Pawn target) => Ready(body, Find(body, soul, name), name) && CanMelee(body, target, owner,name);
                            FindTarget(body, soul, job, Valid);
                        }
                        else
                            body.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job, soul), JobTag.Misc);
                    } };
                if (body.CurJobDef == job || !Ready(body, owner, name))
                    cmd.Disable("TM_SpiritRebirthCastUnavailable".Translate());
                yield return cmd;
            }
        }
    }

    public sealed class JobDriver_SpiritHostedMartial : JobDriver
    {
        private bool attempted;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref attempted, "attempted");
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var soul = job.targetA.Thing as Pawn;
            string name = job.def.defName;
            if (!SpiritHostedMartial.HasNativeOrder(name) || pawn != soul)
                this.FailOn(() => !SpiritRebirthState.TryGetPossessingSpirit(pawn, out var current) || current != soul);
            var warmup = Toils_General.Wait(1);
            warmup.initAction = () => {
                var owner = SpiritHostedMartial.Find(pawn, soul, name);
                if (!SpiritHostedMartial.Ready(pawn, owner, name) ||
                    SpiritHostedMartial.IsSniperShot(name) && !SpiritHostedMartial.CanFire(pawn,owner,job.targetB.Pawn) ||
                    SpiritHostedMartial.UsesCombatTarget(name) &&
                    !SpiritHostedMartial.CanMelee(pawn, job.targetB.Pawn, owner,name)) { EndJobWith(JobCondition.Incompletable); return; }
                ticksLeftThisToil = UnityEngine.Mathf.Max(1, GenTicks.SecondsToTicks(
                    owner.Def.MainVerb.warmupTime * Compatibility.AbilityTiming.WarmupMultiplier));
            };
            yield return warmup;
            yield return Toils_General.Do(() => {
                if (attempted || pawn.CurJob != job || pawn.jobs.curDriver != this) { EndJobWith(JobCondition.Incompletable); return; }
                attempted = true;
                if (SpiritHostedMartial.IsSniperShot(name))
                {
                    var owner = SpiritHostedMartial.Find(pawn,soul,name);
                    if (!SpiritHostedMartial.Ready(pawn,owner,name) || !SpiritHostedMartial.Fire(pawn,owner,job.targetB.Pawn))
                        EndJobWith(JobCondition.Incompletable);
                    return;
                }
                bool success = SpiritHostedMartial.UsesCombatTarget(name) ? SpiritHostedMartial.Strike(pawn, soul, job.targetB.Pawn,name)
                    : SpiritHostedMartial.Apply(pawn, soul, name);
                if (!success) EndJobWith(JobCondition.Incompletable);
            });
        }
    }
}
