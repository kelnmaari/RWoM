using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class SpiritRebirthCasting
    {
        public static bool Allows(Pawn pawn, AbilityDef ability)
        {
            // A trait, race, or native possession alone never opts a legacy pawn in.
            if (ClassIdentity.Component(pawn)?.SpiritRebirth == null) return true;
            if (!SpiritRebirthEnergy.CanCast(pawn)) return false;
            return IsSpiritAbility(ability);
        }

        public static bool IsSpiritAbility(AbilityDef ability)
        {
            if (ability == null || TorannMagicDefOf.TM_Possessor == null) return false;
            var classes = DefDatabase<TM_CustomClassDef>.GetNamedSilentFail("TM_CustomClasses")?.customClasses;
            var matches = classes?.Where(c => c != null && c.classTrait == TorannMagicDefOf.TM_Possessor).ToList();
            if (matches?.Count != 1 || !matches[0].isMage || matches[0].classMageAbilities == null) return false;
            // Native rank variants are explicit class entries. Follow only their
            // actual child definitions, with cycle protection; names grant nothing.
            var pending = new Stack<TMAbilityDef>(matches[0].classMageAbilities.Where(a => a != null));
            var seen = new HashSet<TMAbilityDef>();
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!seen.Add(current)) continue;
                if (ReferenceEquals(current, ability)) return true;
                foreach (var child in current.childAbilities ?? new List<TMAbilityDef>())
                    if (child != null) pending.Push(child);
            }
            return false;
        }
    }
}
