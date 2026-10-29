using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AbilityUser;
using HarmonyLib;
using RimWorld;
using TorannMagic.Compatibility;
using TorannMagic.Utils;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class ClassEnrollment
    {
        private static readonly HashSet<Pawn> inFlight = new HashSet<Pawn>();
        private static readonly FieldInfo magicDataField = AccessTools.Field(typeof(CompAbilityUserMagic), "magicData");
        private static readonly FieldInfo mightDataField = AccessTools.Field(typeof(CompAbilityUserMight), "mightData");
        private static readonly FieldInfo runtimeDataField = AccessTools.Field(typeof(CompAbilityUser), "abilityData");
        private static readonly FieldInfo[] magicFlags = OwnershipFlags(typeof(CompAbilityUserMagic));
        private static readonly FieldInfo[] mightFlags = OwnershipFlags(typeof(CompAbilityUserMight));
        private static FieldInfo[] OwnershipFlags(System.Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(bool) && (f.Name.StartsWith("spell_") || f.Name.StartsWith("skill_") || f.Name == "recallSpell")).ToArray();

        public static bool CanEnrollMage(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.RaceProps?.Humanlike != true
                || pawn.story?.traits == null || !pawn.IsColonistPlayerControlled
                || pawn.IsShambler || pawn.IsGhoul || pawn.health == null || pawn.needs == null
                || ClassIdentity.Component(pawn) == null || ClassIdentity.Component(pawn).State != null
                || DefDatabase<TraitDef>.GetNamedSilentFail("TM_VocationMage") == null) return false;
            var traits = pawn.story.traits.allTraits;
            if (!traits.Any(t => t.def == TorannMagicDefOf.TM_Gifted && !t.Suppressed)
                || traits.Any(t => t.def == TorannMagicDefOf.PhysicalProdigy || t.def?.defName?.StartsWith("TM_Vocation") == true)) return false;
            // Match declarations regardless of degree, suppression or enabled settings:
            // this is enrollment, never a migration of a partially recognized class.
            if (traits.Any(t => TM_ClassGroups.MagicEntriesField.Any(e => e.trait == t.def?.defName)
                || TM_ClassGroups.MightEntriesField.Any(e => e.trait == t.def?.defName)
                || (TM_ClassUtility.CustomClasses?.Any(c => c?.classTrait == t.def) ?? false))) return false;
            foreach (var comp in pawn.AllComps.Where(c => c is CompAbilityUserMagic || c is CompAbilityUserMight))
            {
                var field = comp is CompAbilityUserMagic ? magicDataField : mightDataField;
                if (field == null || runtimeDataField == null || field.GetValue(comp) != null) return false;
                var runtime = runtimeDataField.GetValue(comp) as AbilityData;
                if (runtime?.Count > 0) return false;
                if ((comp is CompAbilityUserMagic ? magicFlags : mightFlags).Any(f => (bool)f.GetValue(comp))) return false;
            }
            return true;
        }

        public static bool TryEnrollMage(Pawn pawn)
        {
            if (!CanEnrollMage(pawn) || !inFlight.Add(pawn)) return false;
            try
            {
                if (!pawn.AllComps.OfType<CompAbilityUserMagic>().Any()) TM_PawnTracker.EnsureClassComps(pawn);
                if (!CanEnrollMage(pawn)) return false;
                var magic = pawn.AllComps.OfType<CompAbilityUserMagic>().FirstOrDefault();
                if (magic == null) return false;
                var vocation = DefDatabase<TraitDef>.GetNamedSilentFail("TM_VocationMage");
                var gifted = pawn.story.traits.allTraits.First(t => t.def == TorannMagicDefOf.TM_Gifted && !t.Suppressed);
                // Commit identity before trait hooks; they must see a new empty Mage,
                // not infer a lost legacy class and seed its default spells.
                if (!ClassIdentity.Component(pawn).BeginNewProgression(Vocation.Mage)) return false;
                pawn.story.traits.RemoveTrait(gifted);
                pawn.story.traits.GainTrait(new Trait(vocation));
                TM_PawnTracker.ResolveComps(pawn);
                // Missing data initialization is allowed only after the read-only
                // preflight proved there was no previous progression to migrate.
                TM_LegacyRecovery.Repair(pawn);
                pawn.Notify_DisabledWorkTypesChanged();
                pawn.skills?.Notify_SkillDisablesChanged();
                return true;
            }
            finally { inFlight.Remove(pawn); }
        }
    }
}
