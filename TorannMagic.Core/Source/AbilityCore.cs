using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TorannMagic.Core
{
    // Central resolver: stable spellId → current Def. Rebuilt whenever the def
    // database changes; lookups never throw (missing = null = "dormant").
    public static class AbilityCore
    {
        private static Dictionary<string, Def> _byId;

        public static void Invalidate() => _byId = null;

        // Host-mod registration for convention ids (and any other late id).
        // Extension ids take precedence: the registry is always rebuilt (and
        // extension ids indexed) before the first registration lands, and an
        // existing key is never overwritten.
        public static void Register(string spellId, Def def)
        {
            if (spellId == null || def == null) return;
            if (_byId == null) Rebuild();
            if (!_byId.ContainsKey(spellId)) _byId[spellId] = def;
        }

        public static Def Resolve(string spellId)
        {
            var reg = Registry;
            return reg != null && spellId != null && reg.TryGetValue(spellId, out var def) ? def : null;
        }

        public static bool TryResolve(string spellId, out Def def)
        {
            var reg = Registry;
            if (reg != null && spellId != null && reg.TryGetValue(spellId, out def)) return true;
            def = null; return false;
        }

        public static IReadOnlyDictionary<string, Def> Registry
        {
            get
            {
                if (_byId == null) Rebuild();
                return _byId;
            }
        }

        private static void Rebuild()
        {
            _byId = new Dictionary<string, Def>();
            foreach (var defType in GenDefDatabase.AllDefTypesWithDatabases())
            {
                foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(defType))
                {
                    var ext = def.GetModExtension<SpellIdentityExtension>();
                    if (ext?.spellId != null && !_byId.ContainsKey(ext.spellId))
                        _byId[ext.spellId] = def;
                }
            }
        }
    }
}
