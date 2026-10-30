using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AbilityUser;
using TorannMagic.TMDefs;
using Verse;

namespace TorannMagic.ClassProgression
{
    internal sealed class SpiritRuntimeClock : IExposable
    {
        private int lastTick = -1;
        public SpiritRuntimeClock() { }
        internal bool Observe(int now)
        {
            if (now < 0 || now <= lastTick || Find.TickManager.Paused) return false;
            lastTick = now;
            return true;
        }
        public void ExposeData() => Scribe_Values.Look(ref lastTick, "lastTick", -1);
    }

    internal static class SpiritRebirthRuntime
    {
        // Cache type metadata only. Saved owners and lists are always read fresh.
        private static readonly FieldInfo[] skillFields = typeof(MagicData).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(List<MagicPowerSkill>)).ToArray();
        private static readonly FieldInfo[] powerFields = typeof(MagicData).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(List<MagicPower>)).ToArray();
        internal static int SavedRank(MagicData data, string label)
        {
            if (data == null) return 0;
            foreach (var field in skillFields)
                if (field.GetValue(data) is List<MagicPowerSkill> skills)
                    foreach (var skill in skills)
                        if (skill != null && TM_CustomSkillLabels.Canonical(skill.label) == label) return skill.level;
            return 0;
        }
        internal static int SavedRank(MagicData data, TMAbilityDef ability, string suffix)
        {
            if (ability == null) return 0;
            string label = ability.defName.TrimEnd('_', 'I', 'V', 'X') + suffix;
            foreach (var field in skillFields)
                if (field.GetValue(data) is List<MagicPowerSkill> skills)
                    foreach (var skill in skills)
                        if (skill != null && TM_CustomSkillLabels.Canonical(skill.label) == label) return skill.level;
            foreach (var definition in DefDatabase<TM_CustomPowerDef>.AllDefsListForReading)
            {
                var power = definition.customPower;
                if (power?.abilityDefs?.Contains(ability) != true || power.skills == null) continue;
                foreach (var skill in power.skills)
                    if (skill?.label?.EndsWith(suffix) == true) return SavedRank(data, TM_CustomSkillLabels.Canonical(skill.label));
            }
            return 0;
        }
        internal static TMAbilityDef SavedHediffAbility(MagicData data, Hediff hediff)
        {
            foreach (var field in powerFields)
                if (field.GetValue(data) is List<MagicPower> powers)
                    foreach (var power in powers)
                    {
                        var defs = power?.TMabilityDefs;
                        if (defs == null || defs.Count == 0) continue;
                        var ability = defs[Math.Max(0, Math.Min(power.level, defs.Count - 1))] as TMAbilityDef;
                        if (ability?.abilityHediff == hediff?.def) return ability;
                    }
            return null;
        }
        internal static bool TickMagic(CompAbilityUserMagic comp, SpiritRuntimeClock clock)
        {
            var pawn = comp.Pawn;
            if (!SpiritRebirthEnergy.Marked(pawn)) return false;
            int now = Find.TickManager.TicksGame;
            if (!SpiritRebirthBody.RetainsNeeds(pawn) || !clock.Observe(now)) return true;
            SpiritRebirthHealthForm.TryRefreshPending(pawn);
            TickAbilities(comp, true);
            if (!SpiritRebirthBody.RetainsNeeds(pawn)) return true;
            for (int i = comp.chainedAbilitiesList.Count - 1; i >= 0; i--)
            {
                var chain = comp.chainedAbilitiesList[i];
                if (chain?.abilityDef == null) { comp.chainedAbilitiesList.RemoveAt(i); continue; }
                chain.expirationTicks--;
                if (!chain.expires || chain.expirationTicks > 0) continue;
                comp.RemovePawnAbility(chain.abilityDef);
                comp.chainedAbilitiesList.RemoveAt(i);
            }
            if (now % 60 == 0 && comp.ExistingMagicData != null) comp.ResolveOwnedMagicEconomy();
            // The native need tracker can also observe this tick. Its saved
            // interval receipt makes the two legitimate paths charge only once.
            if (pawn.IsHashIntervalTick(150)) comp.Mana?.NeedInterval();
            return true;
        }

        internal static bool TickMight(CompAbilityUserMight comp, SpiritRuntimeClock clock)
        {
            if (!SpiritRebirthEnergy.Marked(comp.Pawn)) return false;
            if (!SpiritRebirthBody.RetainsNeeds(comp.Pawn) || !clock.Observe(Find.TickManager.TicksGame)) return true;
            TickAbilities(comp, false);
            for (int i = comp.chainedAbilitiesList.Count - 1; i >= 0; i--)
            {
                var chain = comp.chainedAbilitiesList[i];
                if (chain?.abilityDef == null) { comp.chainedAbilitiesList.RemoveAt(i); continue; }
                chain.expirationTicks--;
                if (!chain.expires || chain.expirationTicks > 0) continue;
                comp.RemovePawnAbility(chain.abilityDef);
                comp.chainedAbilitiesList.RemoveAt(i);
            }
            return true;
        }

        private static void TickAbilities(CompAbilityUser comp, bool magic)
        {
            bool active = magic && SpiritRebirthFormPolicy.IsSpawnedEthereal(comp.Pawn) && SpiritRebirthEnergy.CanCast(comp.Pawn);
            var family = active ? SpiritRebirthCasting.NativeFamily() : null;
            // One provenance walk per dispatch, not one graph traversal per power.
            var seen = new HashSet<PawnAbility>();
            foreach (var ability in comp.AbilityData.AllPowers.ToArray())
            {
                if (ability == null || !seen.Add(ability) || !ReferenceEquals(ability.AbilityUser, comp)) continue;
                if (active && family.Contains(ability.Def) && SpiritRebirthFormPolicy.IsSpawnedEthereal(comp.Pawn)
                    && SpiritRebirthEnergy.CanCast(comp.Pawn)) ability.Tick();
                else if (ability.CooldownTicksLeft > -1) ability.CooldownTicksLeft--;
            }
        }

        internal static void TickHeld(Thing_SoulStone stone)
        {
            var pawn = stone?.ContainedSpirit;
            if (!SpiritRebirthState.TryGet(pawn, out var state) || !state.HasValidStone(pawn)
                || !ReferenceEquals(state.Stone, stone)) return;
            SpiritRebirthHealthForm.TryRefreshPending(pawn);
            pawn.GetCompAbilityUserMagic()?.CompTick();
            pawn.GetCompAbilityUserMight()?.CompTick();
        }
    }
}
