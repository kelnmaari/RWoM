using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using AbilityUser;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using Verse.AI;

namespace TorannMagic
{
    public class Verb_SniperShot : Verb_UseAbility
    {
        internal static Verb WeaponVerb(Pawn body)
        {
            var weapon = body?.equipment?.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb;
            return weapon?.GetProjectile()?.projectile != null && TM_Calc.IsUsingRanged(body) ? weapon : null;
        }

        internal static bool CanFire(Pawn body, MightAbility power, Pawn target)
        {
            if (power?.mightDef?.MainVerb?.verbClass != typeof(Verb_SniperShot) || target?.Spawned != true || target.Dead ||
                body?.Spawned != true || target.Map != body.Map || !target.HostileTo(body)) return false;
            var shot = ForHost(body,power);
            return body.Position.InHorDistOf(target.Position,shot.verbProps.range) &&
                shot.TryFindShootLineFromTo(body.Position,target,out _);
        }

        private static Verb_SniperShot ForHost(Pawn body,MightAbility power) =>
            new Verb_SniperShot { caster=body, Ability=power, verbProps=power.mightDef.MainVerb };

        internal static bool FireFromHost(Pawn body,MightAbility power,Pawn target)
        {
            if (!CanFire(body,power,target)) return false;
            var shot = ForHost(body,power);
            shot.currentTarget = target;
            if (!shot.FireRound()) return false;
            return true;
        }

        private bool FireRound()
        {
            var weapon = WeaponVerb(CasterPawn);
            if (weapon == null || UseAbilityProps.defaultProjectile?.thingClass == null ||
                !typeof(Projectile_AbilityBase).IsAssignableFrom(UseAbilityProps.defaultProjectile.thingClass)) return false;
            weapon.verbProps.soundCast?.PlayOneShot(SoundInfo.InMap(CasterPawn));
            if (!TryLaunchProjectileCheck(weapon.GetProjectile(), currentTarget)) return false;
            if (Ability is MightAbility power) Odyssey.ManualTravelSafety.Settle(power);
            return true;
        }

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            var body = CasterPawn;
            if (body?.stances?.curStance is Stance_Cooldown stance && ReferenceEquals(stance.verb,this))
            {
                // The paid projectile is already in flight. A saved transient verb cannot resume this job.
                stance.verb = null;
                if (body.jobs.curDriver is TMJobDriver_CastAbilityVerb && ReferenceEquals(body.CurJob?.verbToUse,this))
                {
                    body.jobs.EndCurrentJob(JobCondition.Succeeded);
                    if (body.stances.curStance is Stance_Mobile && stance.ticksLeft > 0) body.stances.SetStance(stance);
                }
            }
        }

        protected override bool TryCastShot()
        {
            if (CasterPawn == null) return false;
            if (WeaponVerb(CasterPawn) == null)
            {
                Messages.Message("MustHaveRangedWeapon".Translate(CasterPawn.LabelCap),MessageTypeDefOf.RejectInput);
                return false;
            }
            try
            {
                PostCastShot(FireRound(), out bool success);
                if (!success) Ability.Notify_AbilityFailed(UseAbilityProps.refundsPointsAfterFailing);
                return success;
            }
            finally { burstShotsLeft = 0; }
        }

        internal Thing MakeRound(ThingDef round)
        {
            // Same initialization as ThingMaker for a non-stuff projectile. The instance
            // keeps the weapon's registered definition, including its speed and damage.
            var shot = (Projectile_AbilityBase)Activator.CreateInstance(UseAbilityProps.defaultProjectile.thingClass);
            shot.def = round;
            shot.PostMake();
            shot.PostPostMake();
            if (shot is Projectile_SniperRound sniperRound && Ability is MightAbility power)
                sniperRound.BindShot(CasterPawn,power,round);
            return shot;
        }
    }

    [HarmonyPatch(typeof(Verb_UseAbility), nameof(Verb_UseAbility.TryLaunchProjectileCheck))]
    internal static class SniperRoundSpawn
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn),
                new[] { typeof(ThingDef), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
            var code = instructions.ToList();
            int index = code.FindIndex(c => c.Calls(original));
            if (index < 0 || code.Skip(index + 1).Any(c => c.Calls(original)))
                throw new InvalidOperationException("Could not identify the ability projectile spawn in TryLaunchProjectileCheck.");
            code[index].operand = AccessTools.Method(typeof(SniperRoundSpawn), nameof(Spawn));
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.MoveLabelsFrom(code[index]);
            code.Insert(index, load);
            return code;
        }

        private static Thing Spawn(ThingDef def, IntVec3 cell, Map map, WipeMode mode, Verb_UseAbility verb)
            => verb is Verb_SniperShot sniper ? GenSpawn.Spawn(sniper.MakeRound(def), cell, map, mode)
                : GenSpawn.Spawn(def, cell, map, mode);
    }
}
