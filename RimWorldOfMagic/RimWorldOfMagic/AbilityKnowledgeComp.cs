using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TorannMagic
{
    // Unified knowledge store for all spells a pawn knows. Versioned schema with
    // stepwise migrations. Replaces 26+125 legacy lists and 53 bool flags.
    // Consumers access through TryGet/TrySet — no direct def or label derefs.
    public class AbilityKnowledgeComp : ThingComp, IExposable
    {
        public const int CurrentSchemaVersion = 1;
        private int schemaVersion;
        private List<SpellKnowledge> spells = new List<SpellKnowledge>();

        public IReadOnlyList<SpellKnowledge> Spells => spells.AsReadOnly();
        public int SchemaVersion => schemaVersion;

        public SpellKnowledge Find(string spellId)
            => spells.FirstOrDefault(s => s != null && s.spellId == spellId);

        public SpellKnowledge GetOrCreate(string spellId, int tier)
        {
            var existing = Find(spellId);
            if (existing == null)
            {
                existing = new SpellKnowledge { spellId = spellId, tier = tier };
                spells.Add(existing);
            }
            return existing;
        }

        public bool Remove(string spellId)
            => spells.RemoveAll(s => s != null && s.spellId == spellId) > 0;

        // Skill rank by role: 0 = Power, 1 = Efficiency, 2 = Versatility.
        // Missing entries return 0 — never NRE.
        public int GetSkillRank(string spellId, int role)
        {
            var s = Find(spellId);
            if (s == null) return 0;
            return role switch { 0 => s.pwr, 1 => s.eff, 2 => s.ver, _ => 0 };
        }

        public void SetSkillRank(string spellId, int role, int value)
        {
            var s = GetOrCreate(spellId, 0);
            if (role == 0) s.pwr = value;
            else if (role == 1) s.eff = value;
            else if (role == 2) s.ver = value;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", CurrentSchemaVersion);
            Scribe_Collections.Look(ref spells, "spells", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                spells?.RemoveAll(s => s == null || string.IsNullOrEmpty(s.spellId));
                schemaVersion = CurrentSchemaVersion; // migrations go here when version bumps
            }
        }
    }
}
