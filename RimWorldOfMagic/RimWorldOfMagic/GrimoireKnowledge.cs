using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TorannMagic
{
    // Ф4 step 4: grimoire adapter over the knowledge store. Sleeping knowledge —
    // learned entries whose def no longer resolves — is listed for the grey
    // section; convention spellIds prettify into readable names. Card layout
    // itself keeps reading legacy owners while the migration runs.
    public static class GrimoireKnowledge
    {
        public static string DisplayName(string spellId)
        {
            if (string.IsNullOrEmpty(spellId)) return "???";
            if (!spellId.StartsWith("rwom.")) return spellId;
            string name = spellId.Substring("rwom.".Length);
            if (name.StartsWith("tm_")) name = name.Substring(3);
            string[] words = name.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                words[i] = IsRomanNumeral(words[i])
                    ? words[i].ToUpperInvariant()
                    : char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        private static bool IsRomanNumeral(string word)
        {
            foreach (char c in word)
                if (c != 'i' && c != 'v' && c != 'x') return false;
            return true;
        }

        // Learned entries whose def fails to resolve (removed or renamed spell).
        // Never throws; the list is recomputed per frame (knowledge lists are small).
        public static List<SpellKnowledge> SleepingSpells(Pawn pawn)
            => SleepingSpells(pawn?.TryGetComp<AbilityKnowledgeComp>());

        public static List<SpellKnowledge> SleepingSpells(AbilityKnowledgeComp knowledge)
        {
            var sleeping = new List<SpellKnowledge>();
            if (knowledge == null) return sleeping;
            foreach (SpellKnowledge spell in knowledge.Spells)
            {
                if (spell == null || !spell.learned || string.IsNullOrEmpty(spell.spellId)) continue;
                if (!CoreBridge.TryResolve(spell.spellId, out _)) sleeping.Add(spell);
            }
            return sleeping;
        }
    }
}
