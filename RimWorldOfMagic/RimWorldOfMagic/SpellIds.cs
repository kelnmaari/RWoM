namespace TorannMagic
{
    // Single source of the stable spellId convention: "rwom." + lower-case
    // defName. The legacy import, the autocast gate and the registry
    // registrations all use this form until defs declare explicit
    // SpellIdentityExtension spellIds in XML (Ф2 item 2).
    public static class SpellIds
    {
        public static string Of(Verse.Def def)
            => def?.defName == null ? null : "rwom." + def.defName.ToLower();
    }
}
