#if RW17
using AbilityUser;
using HarmonyLib;
using Verse;

namespace TorannMagic.Compatibility
{
    internal static class AbilityCastSafety
    {
        public static bool AllowsSelfTarget(Thing caster, TMAbilityDef ability, LocalTargetInfo target)
        {
            if (!target.HasThing || target.Thing != caster) return true;
            // Older explicit self abilities sometimes retain canTargetSelf=false.
            // Their self category is intentional; isViolent is not a targeting rule.
            return ability.MainVerb.AbilityTargetCategory == AbilityTargetCategory.TargetSelf
                || ability.MainVerb.targetParams?.canTargetSelf == true;
        }

        public static bool IsAutomaticCast(Verb verb)
        {
            var job = verb.CasterPawn?.CurJob;
            // Jecs marks AI jobs playerForced too. Use its actual context marker
            // and executing verb, not colonist/faction status or playerForced.
            return job != null && job.def != null && job.verbToUse == verb && job.count == 0
                && (job.def == TorannMagicDefOf.TMCastAbilityVerb || job.def == TorannMagicDefOf.TMCastAbilitySelf);
        }

        private static bool ValidMapTarget(Verb verb, LocalTargetInfo target)
        {
            Thing caster = verb.caster;
            return caster != null && caster.Spawned && target.IsValid && target.Cell.InBounds(caster.Map)
                && (!target.HasThing || (target.Thing.Spawned && !target.Thing.Destroyed && target.Thing.Map == caster.Map));
        }

        public static bool CanAutocast(Verb verb, LocalTargetInfo target)
        {
            if (!(verb is Verb_UseAbility abilityVerb) || !(abilityVerb.Ability?.Def is TMAbilityDef ability)) return true;
            if (!AllowsSelfTarget(verb.caster, ability, target) || !ValidMapTarget(verb, target)
                || !verb.CanHitTarget(target)) return false;
            // A custom CanHitTargetFrom override may omit LoS. Use the actual
            // engine shoot line (including leaning), only when this spell needs it.
            return !verb.verbProps.requireLineOfSight
                || verb.TryFindShootLineFromTo(verb.caster.Position, target, out _);
        }

        public static bool AllowsStart(Verb verb, LocalTargetInfo target)
        {
            if (!(verb is Verb_UseAbility abilityVerb) || !(abilityVerb.Ability?.Def is TMAbilityDef ability)) return true;
            return AllowsSelfTarget(verb.caster, ability, target)
                && (!IsAutomaticCast(verb) || CanAutocast(verb, target));
        }

        public static bool CanBeginJobCast(Verb_UseAbility verb, LocalTargetInfo target)
        {
            // The job deducts some special resources before TryStartCastOn. Check
            // its native start prerequisites first, preserving deduction timing
            // for zero-warmup effects that read the remaining resource.
            return AllowsStart(verb, target) && verb.caster != null && verb.caster.Spawned
                && verb.state != VerbState.Bursting && verb.CanHitTarget(target)
                && (!verb.CasterIsPawn || verb.WarmupTime <= 0f
                    || verb.TryFindShootLineFromTo(verb.caster.Position, target, out _));
        }

        public static bool AllowsProjectileLaunch(Verb_UseAbility verb, LocalTargetInfo target)
        {
            if (!(verb.Ability?.Def is TMAbilityDef ability)) return true;
            if (!AllowsSelfTarget(verb.caster, ability, target)) return false;
            if (!IsAutomaticCast(verb)) return true;
            if (!ValidMapTarget(verb, target)) return false;
            // Preserve the original continuation policy for bursts that explicitly
            // allow losing sight. Reject before JecsLite's player-facing message.
            return !verb.verbProps.requireLineOfSight || !verb.verbProps.stopBurstWithoutLos
                || verb.TryFindShootLineFromTo(verb.caster.Position, target, out _);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] {
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class AbilityCastStartPatch
    {
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            if (AbilityCastSafety.AllowsStart(__instance, castTarg)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb_UseAbility), nameof(Verb_UseAbility.PreCastShotCheck))]
    public static class AbilityPreCastStartPatch
    {
        public static bool Prefix(Verb_UseAbility __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            if (AbilityCastSafety.AllowsStart(__instance, castTarg)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb_UseAbility), nameof(Verb_UseAbility.TryLaunchProjectileCheck))]
    public static class AbilityProjectileLaunchSafetyPatch
    {
        public static bool Prefix(Verb_UseAbility __instance, LocalTargetInfo launchTarget, ref bool __result)
        {
            if (AbilityCastSafety.AllowsProjectileLaunch(__instance, launchTarget)) return true;
            __result = false;
            return false;
        }
    }
}
#endif
