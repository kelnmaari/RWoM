using System.Linq;
using AbilityUser;
using RimWorld;
using Verse;

namespace TorannMagic.ClassProgression
{
    // The launcher remains the physical pawn for the native projectile and targeting rules.
    internal abstract class Projectile_DemonField : Projectile_AbilityBase
    {
        protected bool initialized;
        protected int age;
        protected int duration;
        private bool resolvingImpact;

        protected abstract void TickField();

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (initialized) return;
            resolvingImpact = !blockedByShield;
            try { base.Impact(hitThing,blockedByShield); }
            finally { resolvingImpact = false; }
            if (Destroyed || !Spawned || blockedByShield) return;
            // Only time spent at the impact site counts toward the life of the field.
            age = 0;
            landed = true;
            TickField();
        }

        protected override void TickInterval(int delta)
        {
            if (!initialized) base.TickInterval(delta);
        }

        protected override void Tick()
        {
            if (!initialized) { base.Tick(); return; }
            if (++age >= duration) Destroy();
            else TickField();
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!resolvingImpact) base.Destroy(mode);
        }

        protected int pwrVal, verVal;
        protected Pawn pawn;
        private Pawn projectedOwner;
        protected float casterSensitivity = 1f;
        internal Pawn SkillOwner => projectedOwner ?? pawn ?? launcher as Pawn;

        internal void PrepareHosted(MagicAbility power, Pawn body)
        {
            projectedOwner = power.Pawn;
            pawn = body;
            ReadRanks(projectedOwner,this is Projectile_Repulsion ? TorannMagicDefOf.TM_Repulsion : TorannMagicDefOf.TM_Attraction);
        }

        protected void InitializeRanks(TMAbilityDef spell)
        {
            pawn = launcher as Pawn;
            if (projectedOwner == null && pawn != null) ReadRanks(pawn,spell);
        }

        private void ReadRanks(Pawn owner, TMAbilityDef spell)
        {
            var magic = owner.GetCompAbilityUserMagic();
            pwrVal = magic?.MagicData.GetSkill_Power(spell)?.level ?? 0;
            verVal = magic?.MagicData.GetSkill_Versatility(spell)?.level ?? 0;
            if (owner.story?.traits?.HasTrait(TorannMagicDefOf.Faceless) == true)
            {
                var copied = owner.GetCompAbilityUserMight()?.MightData.MightPowerSkill_Mimic;
                pwrVal = copied?.FirstOrDefault(s => s.label == "TM_Mimic_pwr")?.level ?? 0;
                verVal = copied?.FirstOrDefault(s => s.label == "TM_Mimic_ver")?.level ?? 0;
            }
            if (ModOptions.Settings.Instance.AIHardMode && !owner.IsColonist) pwrVal = verVal = 3;
            if (spell == TorannMagicDefOf.TM_Repulsion)
                casterSensitivity = owner.GetStatValue(StatDefOf.PsychicSensitivity,false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pwrVal,"pwrVal");
            Scribe_Values.Look(ref verVal,"verVal");
            Scribe_References.Look(ref pawn,"pawn");
            Scribe_References.Look(ref projectedOwner,"TM_demonFieldOwner");
            Scribe_Values.Look(ref casterSensitivity,"TM_demonFieldSensitivity",1f);
        }
    }
}
