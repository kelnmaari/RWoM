using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TorannMagic
{
    // Ф5 spike: grants vanilla bridge abilities from the knowledge store.
    // Spike scope covers the Heal pilot family only. A learned knowledge entry
    // whose bridge def exists gets the bridge ability in the vanilla tracker
    // (gizmo grid, AI lookups); sleeping knowledge (original def gone) stays
    // visible in the grimoire only. Idempotent per pawn.
    public static class VanillaBridgeSync
    {
        private static readonly string[] BridgeSpellIds = { "rwom.tm_heal" };

        private static readonly Dictionary<string, TMBridgeAbilityDef> BridgeDefs =
            new Dictionary<string, TMBridgeAbilityDef>();

        public static void Sync(Pawn pawn)
        {
            if (pawn?.abilities == null) return;
            AbilityKnowledgeComp knowledge = pawn.TryGetComp<AbilityKnowledgeComp>();
            if (knowledge == null) return;
            foreach (string spellId in BridgeSpellIds)
            {
                SpellKnowledge entry = knowledge.Find(spellId);
                if (entry == null || !entry.learned) continue;
                TMBridgeAbilityDef bridge = BridgeDef(spellId);
                if (bridge == null) continue;
                if (pawn.abilities.GetAbility(bridge) != null) continue;
                pawn.abilities.GainAbility(bridge);
            }
        }

        private static TMBridgeAbilityDef BridgeDef(string spellId)
        {
            if (BridgeDefs.TryGetValue(spellId, out TMBridgeAbilityDef cached))
            {
                return cached;
            }
            // Bridge defName convention: rwom.tm_heal -> TM_Bridge_Heal.
            string name = GrimoireKnowledge.DisplayName(spellId)?.Replace(" ", "") ?? null;
            TMBridgeAbilityDef def = name == null
                ? null
                : DefDatabase<AbilityDef>.GetNamedSilentFail("TM_Bridge_" + name) as TMBridgeAbilityDef;
            if (def != null)
            {
                BridgeDefs[spellId] = def; // do not cache misses: defs may register later
            }
            return def;
        }
    }
}
