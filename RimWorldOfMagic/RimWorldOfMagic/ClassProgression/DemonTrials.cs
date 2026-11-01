using System;
using System.Linq;
using System.Runtime.CompilerServices;
using AbilityUser;
using HarmonyLib;
using Verse;
using RimWorld;

namespace TorannMagic.ClassProgression
{
    public static class DemonTrials
    {
        public const string WarlockFoundationTrialId = "rwom.warlock.foundation", WarlockMasteryTrialId = "rwom.warlock.mastery";
        public const string SuccubusFoundationTrialId = "rwom.succubus.foundation", SuccubusMasteryTrialId = "rwom.succubus.mastery";
        static CompAbilityUserMagic Magic(Pawn p) => p?.AllComps.OfType<CompAbilityUserMagic>().FirstOrDefault();
        static string Trial(string profile, bool mastery) => profile + (mastery ? ".mastery" : ".foundation");
        static TMAbilityDef Spell(string profile, bool mastery) => !mastery ? TorannMagicDefOf.TM_SoulBond : profile == "rwom.warlock" ? TorannMagicDefOf.TM_Repulsion : TorannMagicDefOf.TM_Attraction;
        static DisciplineProgress Owned(Pawn caster, string profile, bool mastery, AbilityUser.AbilityDef ability)
        {
            if (caster == null || caster.Dead || !caster.Spawned || caster.Faction == null || caster.story?.traits?.HasTrait(TorannMagicDefOf.Faceless) == true
                || !ClassIdentity.TryGetState(caster, out var state) || (state.Vocation & Vocation.Mage) == 0 || state.Disciplines.Count != 1) return null;
            if (ClassIdentity.Profile(profile)?.domain != DisciplineDomain.Magic) return null;
            var path = state.Find(profile);
            if (path?.Domain != DisciplineDomain.Magic || path.Stage != (mastery ? InitiationStage.Adept : InitiationStage.Foundation) || path.CompletedTrials.Contains(Trial(profile, mastery))) return null;
            var magic = Magic(caster);
            if (!mastery) TM_ClassState.RestoreSharedMagicProgression(magic);
            var powers = profile == "rwom.warlock" ? magic?.MagicData.MagicPowersWD : magic?.MagicData.MagicPowersSD;
            var owner = powers?.FirstOrDefault(p => p.TMabilityDefs.Contains(Spell(profile, mastery)));
            return owner?.learned == true && owner.TMabilityDefs.Contains(ability) && ReferenceEquals(TM_ClassState.PreferredMagicOwner(magic, ability), owner) ? path : null;
        }
        static bool Runtime(Verb_UseAbility verb) => verb?.Ability != null && verb.Ability.AbilityUser == Magic(verb.CasterPawn)
            && Magic(verb.CasterPawn)?.AbilityData.Powers.Any(a => ReferenceEquals(a, verb.Ability)) == true;
        static bool Ally(Pawn caster, Pawn target) => target != null && target != caster && !target.Dead && target.Spawned && target.Map == caster.Map
            && target.RaceProps?.Humanlike == true && target.Faction == caster.Faction && !target.IsPrisoner && !target.IsSlave && !TM_Calc.IsUndead(target);
        static bool Hostile(Pawn caster, Pawn target) => target != null && !target.Dead && target.Spawned && target.Map == caster.Map && !target.IsPrisoner && !target.IsSlave
            && target.HostileTo(caster) && (target.Faction == null ? target.InAggroMentalState : target.Faction != caster.Faction && target.Faction.HostileTo(caster.Faction));
        static Hediff Hediff(Pawn pawn, string name) => pawn?.health?.hediffSet?.hediffs.FirstOrDefault(h => h.def?.defName == name);
        static string CasterBond(string profile) => profile == "rwom.warlock" ? "TM_WDSoulBondMentalHD" : "TM_SDSoulBondPhysicalHD";
        static string HostBond(string profile) => profile == "rwom.warlock" ? "TM_SoulBondMentalHD" : "TM_SoulBondPhysicalHD";
        static bool Bonded(Pawn caster, Pawn target, string profile) => target != null && Magic(caster)?.soulBondPawn == target && Hediff(caster, CasterBond(profile)) != null
            && Hediff(target, HostBond(profile))?.TryGetComp<HediffComp_SoulBondHost>()?.BonderPawn == caster;
        public sealed class BondSnapshot
        {
            internal Pawn caster, target; internal string profile; internal DisciplineProgress path; internal AbilityUser.AbilityDef ability; internal bool used;
        }
        public static BondSnapshot BeforeBond(Verb_UseAbility verb, Pawn target)
        {
            if (!Runtime(verb) || !Ally(verb.CasterPawn, target)) return null;
            foreach (string profile in new[] { "rwom.warlock", "rwom.succubus" })
            {
                var path = Owned(verb.CasterPawn, profile, false, verb.Ability.Def);
                if (path != null && !Bonded(verb.CasterPawn, Magic(verb.CasterPawn)?.soulBondPawn, "rwom.warlock") && !Bonded(verb.CasterPawn, Magic(verb.CasterPawn)?.soulBondPawn, "rwom.succubus")
                    && Hediff(verb.CasterPawn, CasterBond(profile)) == null && Hediff(target, HostBond(profile)) == null)
                    return new BondSnapshot { caster = verb.CasterPawn, target = target, profile = profile, path = path, ability = verb.Ability.Def };
            }
            return null;
        }
        public static bool AfterBond(BondSnapshot snapshot)
        {
            if (snapshot == null || snapshot.used) return false; snapshot.used = true;
            return Ally(snapshot.caster, snapshot.target) && Owned(snapshot.caster, snapshot.profile, false, snapshot.ability) == snapshot.path
                && Bonded(snapshot.caster, snapshot.target, snapshot.profile) && snapshot.path.RecordCompletedTrial(Trial(snapshot.profile, false));
        }
        public sealed class Tag : IExposable
        {
            public int casterId; public string profile; public AbilityUser.AbilityDef ability;
            public void ExposeData() { Scribe_Values.Look(ref casterId, "casterId", 0); Scribe_Values.Look(ref profile, "profile"); Scribe_Defs.Look(ref ability, "ability"); }
        }
        static readonly ConditionalWeakTable<Projectile_AbilityBase, Tag> sources = new ConditionalWeakTable<Projectile_AbilityBase, Tag>();
        [ThreadStatic] static Tag launching;
        static string Projectile(string profile) => profile == "rwom.warlock" ? "Projectile_Repulsion" : profile == "rwom.succubus" ? "Projectile_Attraction" : null;
        public static Tag BeginLaunch(Verb_UseAbility verb, ThingDef def)
        {
            var previous = launching; launching = null;
            string profile = def?.defName == "Projectile_Repulsion" ? "rwom.warlock" : def?.defName == "Projectile_Attraction" ? "rwom.succubus" : null;
            if (profile != null && Runtime(verb) && Owned(verb.CasterPawn, profile, true, verb.Ability.Def) != null)
                launching = new Tag { casterId = verb.CasterPawn.thingIDNumber, profile = profile, ability = verb.Ability.Def };
            return previous;
        }
        public static void EndLaunch(Tag previous) => launching = previous;
        public static void Launched(Projectile_AbilityBase projectile, Thing caster, AbilityUser.AbilityDef ability)
        {
            var tag = launching; launching = null; sources.Remove(projectile);
            if (tag != null && tag.casterId == caster?.thingIDNumber && projectile.def?.defName == Projectile(tag.profile) && ability == tag.ability) sources.Add(projectile, tag);
        }
        public static void ExposeProjectile(Projectile_AbilityBase projectile)
        {
            sources.TryGetValue(projectile, out var tag); if (tag == null && Scribe.mode == LoadSaveMode.Saving) return;
            Scribe_Deep.Look(ref tag, "TM_demonTrialSource");
            if (Scribe.mode != LoadSaveMode.Saving) { sources.Remove(projectile); if (tag?.casterId > 0 && Projectile(tag.profile) != null && tag.ability != null) sources.Add(projectile, tag); }
        }
        public sealed class EffectSnapshot
        {
            internal Pawn caster, target; internal string profile; internal DisciplineProgress path; internal AbilityUser.AbilityDef ability; internal float before; internal bool used;
        }
        static float Slow(Pawn target) => target.health.hediffSet.hediffs.Where(h => h.def == TorannMagicDefOf.TM_GravitySlowHD).Sum(h => h.Severity);
        public static EffectSnapshot BeforeEffect(Projectile_AbilityBase projectile, Pawn target, bool repulsion)
        {
            // ConditionalWeakTable throws on a null key; a missing projectile simply
            // cannot be an owned trial source.
            if (projectile == null) return null;
            if (!sources.TryGetValue(projectile, out var tag) || tag.profile != (repulsion ? "rwom.warlock" : "rwom.succubus") || projectile.Caster?.thingIDNumber != tag.casterId
                || projectile.def?.defName != Projectile(tag.profile) || projectile.localAbilityDef != tag.ability || !Hostile(projectile.Caster, target)) return null;
            var path = Owned(projectile.Caster, tag.profile, true, tag.ability);
            return path == null ? null : new EffectSnapshot { caster = projectile.Caster, target = target, profile = tag.profile, ability = tag.ability, path = path, before = repulsion ? 0 : Slow(target) };
        }
        public static bool AfterEffect(EffectSnapshot snapshot, Thing launched = null)
        {
            if (snapshot == null || snapshot.used) return false; snapshot.used = true;
            if (snapshot.target.Dead || Owned(snapshot.caster, snapshot.profile, true, snapshot.ability) != snapshot.path) return false;
            bool actual = snapshot.profile == "rwom.warlock" ? launched is FlyingObject_Spinning && launched.Spawned && launched.Map == snapshot.caster.Map && !snapshot.target.Spawned && !snapshot.target.IsPrisoner && !snapshot.target.IsSlave
                    && snapshot.target.HostileTo(snapshot.caster) && (snapshot.target.Faction == null ? snapshot.target.InAggroMentalState : snapshot.target.Faction != snapshot.caster.Faction && snapshot.target.Faction.HostileTo(snapshot.caster.Faction))
                    && ReferenceEquals(AccessTools.Field(typeof(FlyingObject_Spinning), "flyingThing").GetValue(launched), snapshot.target)
                : Hostile(snapshot.caster, snapshot.target) && Slow(snapshot.target) > snapshot.before;
            return actual && snapshot.path.RecordCompletedTrial(Trial(snapshot.profile, true));
        }
        public static void AdjustSlow(Projectile_AbilityBase projectile, Pawn target, float amount)
        {
            var snapshot = BeforeEffect(projectile, target, false);
            HealthUtility.AdjustSeverity(target, TorannMagicDefOf.TM_GravitySlowHD, amount);
            AfterEffect(snapshot);
        }
    }
}
