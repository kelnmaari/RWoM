using System.Runtime.CompilerServices;
using Verse;

namespace TorannMagic
{
    public static class SpellIds
    {
        private static readonly ConditionalWeakTable<Def, string> Ids = new();

        public static string Of(Def def)
            => def?.defName == null ? null : Ids.GetValue(def, Resolve);

        private static string Resolve(Def def)
            => CoreBridge.DeclaredId(def) ?? Convention(def);

        internal static string Convention(Def def) => "rwom." + def.defName.ToLowerInvariant();
    }
}
