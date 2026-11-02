using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using UnityEngine;
using RimWorld;
using Verse;

namespace TorannMagic
{
    // Ф1 (Waylith адаптация): purge stale spell versions left in saved pawns by
    // older mod builds. Removed/renamed spell defs come back from the save as
    // null entries (LookMode.Def) and as runtime wrappers with a dead Def — the
    // source of the "red bugs autocasting healing" NRE family. Learned flags,
    // levels and points of surviving spells are never touched.
    public static class SpellSanitizer
    {
        public static void Sanitize(MagicPower power)
        {
            if (power?.TMabilityDefs == null) return;
            power.TMabilityDefs.RemoveAll(d => d == null);
            if (power.TMabilityDefs.Count == 0)
            {
                power.learned = false;
                power.autocast = false;
                power.level = 0;
            }
            else
            {
                power.level = Mathf.Clamp(power.level, 0, power.TMabilityDefs.Count - 1);
                power.maxLevel = power.TMabilityDefs.Count - 1;
            }
        }

        public static void Sanitize(MightPower power)
        {
            if (power?.TMabilityDefs == null) return;
            power.TMabilityDefs.RemoveAll(d => d == null);
            if (power.TMabilityDefs.Count == 0)
            {
                power.learned = false;
                power.autocast = false;
                power.level = 0;
            }
            else
            {
                power.level = Mathf.Clamp(power.level, 0, power.TMabilityDefs.Count - 1);
                power.maxLevel = power.TMabilityDefs.Count - 1;
            }
        }

        // Runtime wrappers whose spell def died with an old version: dropped so
        // the autocast dispatch and UI never dereference them.
        public static void Sanitize(List<PawnAbility> powers)
        {
            if (powers == null) return;
            powers.RemoveAll(p => p == null || p.Def == null);
        }

        // Missing skill-label entries resolve to a default level instead of the
        // unguarded FirstOrDefault(...).level NRE pattern.
        public static int SkillLevelOrDefault(List<MagicPowerSkill> skills, string label, int defaultLevel = 0)
        {
            var skill = skills?.FirstOrDefault(x => x != null && x.label == label);
            return skill?.level ?? defaultLevel;
        }
    }
}
