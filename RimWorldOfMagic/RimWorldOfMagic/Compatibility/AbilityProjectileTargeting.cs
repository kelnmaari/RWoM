#if RW17
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AbilityUser;
using HarmonyLib;
using Verse;

namespace TorannMagic.Compatibility
{
    // 1.7 returns HitFlags.None during overhead flight. JecsLite's generic ability
    // impact then rejects even its intended pawn and falls back to caster effects.
    // Repair only terminal ability impacts; never enable interception in flight.
    internal static class AbilityProjectileTargeting
    {
        internal static readonly MethodInfo NativeCanHit = AccessTools.Method(typeof(Projectile), "CanHit");
        private static readonly Func<Projectile, Thing, bool> CanHitNative =
            AccessTools.MethodDelegate<Func<Projectile, Thing, bool>>(NativeCanHit);
        private static readonly AccessTools.FieldRef<Projectile, ProjectileHitFlags> RequestedHitFlags =
            AccessTools.FieldRefAccess<Projectile, ProjectileHitFlags>("desiredHitFlags");

        public static bool CanHitAtDestination(Projectile projectile, Thing target)
        {
            if (CanHitNative(projectile, target)) return true;
            if (!(projectile is Projectile_AbilityBase ability) || !(ability.localAbilityDef is TMAbilityDef)
                || !projectile.def.projectile.flyOverhead || projectile.HitFlags != ProjectileHitFlags.None
                || (RequestedHitFlags(projectile) & ProjectileHitFlags.IntendedTarget) == 0)
                return false;

            // Match the native target/map/cover restrictions. The enclosing original
            // impact method still owns thick roofs and its downed-target miss roll.
            return target is Pawn && target != ability.Caster && target.Spawned && !target.Destroyed
                && target.Map == projectile.Map && target == projectile.usedTarget.Thing
                && target == projectile.intendedTarget.Thing
                && !CoverUtility.ThingCovered(target, projectile.Map);
        }

        public static void ApplyImpactEffects(Projectile_AbilityBase projectile, Pawn victim, Pawn caster,
            List<ApplyMentalStates> mentalStates, AbilityUser.AbilityDef abilityDef)
        {
            // A missed/lost external target is not an instruction to debuff the
            // caster. Keep actual self-target casts and unrelated frameworks intact.
            // This wrapper is used only by the base impact, not intentional custom
            // calls such as a derived projectile healing its caster on a hit.
            if (projectile.localAbilityDef is TMAbilityDef && victim == caster
                && projectile.intendedTarget.HasThing && projectile.intendedTarget.Thing != caster)
                return;
            projectile.ApplyHediffsAndMentalStates(victim, caster, mentalStates, abilityDef);
        }

        internal static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions,
            MethodInfo original, MethodInfo replacement)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && Equals(instruction.operand, original))
                {
                    // Retain the original labels and exception-block boundaries.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count == 0)
                throw new InvalidOperationException("Ability projectile compatibility target changed: " + original);
        }
    }

    [HarmonyPatch(typeof(Projectile_AbilityBase), "ImpactSomething")]
    public static class AbilityProjectileImpactPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return AbilityProjectileTargeting.ReplaceCalls(instructions, AbilityProjectileTargeting.NativeCanHit,
                AccessTools.Method(typeof(AbilityProjectileTargeting), nameof(AbilityProjectileTargeting.CanHitAtDestination)));
        }
    }

    [HarmonyPatch(typeof(Projectile_AbilityBase), nameof(Projectile_AbilityBase.Impact_Override))]
    public static class AbilityProjectileRecipientPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return AbilityProjectileTargeting.ReplaceCalls(instructions,
                AccessTools.Method(typeof(Projectile_AbilityBase), nameof(Projectile_AbilityBase.ApplyHediffsAndMentalStates)),
                AccessTools.Method(typeof(AbilityProjectileTargeting), nameof(AbilityProjectileTargeting.ApplyImpactEffects)));
        }
    }
}
#endif
