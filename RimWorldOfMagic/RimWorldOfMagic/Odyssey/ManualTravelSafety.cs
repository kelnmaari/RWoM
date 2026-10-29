using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AbilityUser;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorannMagic.Odyssey
{
    // Consent belongs to one concrete job and endpoint, never to a pawn globally.
    public sealed class TravelConsent : IExposable
    {
        public Pawn Pawn;
        public Map Map;
        public IntVec3 Cell;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref Cell, "cell");
        }
    }

    public static class ManualTravelSafety
    {
        private static readonly ConditionalWeakTable<Job, TravelConsent> Consents = new ConditionalWeakTable<Job, TravelConsent>();
        private sealed class Payment { public Payment() { } public bool Due; }
        private static readonly ConditionalWeakTable<PawnAbility, Payment> Payments = new ConditionalWeakTable<PawnAbility, Payment>();
        public static void BeginAttempt(PawnAbility ability)
        {
            if (ability != null) Payments.GetOrCreateValue(ability).Due = false;
        }
        public static void Settle(PawnAbility ability)
        {
            if (ability == null) return;
            Payments.GetOrCreateValue(ability).Due = true;
            ability.PostAbilityAttempt();
        }
        public static bool TakePayment(PawnAbility ability)
        {
            if (!Payments.TryGetValue(ability, out var payment)) return !IsTravel(ability);
            bool due = payment.Due; payment.Due = false; return due;
        }

        public static bool IsTravel(PawnAbility ability)
        {
            var name = ability?.Def?.defName;
            // Add-on combat jumps use the same Flight verb and despawn/payment lifecycle.
            // Resolve from the definition, without constructing a verb or touching graphics.
            return IsFlight(ability) || name == "TM_Blink" || name?.StartsWith("TM_Blink_") == true
                || name == "TM_PhaseStrike" || name?.StartsWith("TM_PhaseStrike_") == true;
        }

        private static bool IsFlight(PawnAbility ability)
            => ability?.Def is TMAbilityDef def && (def.defName == "TM_Flight"
                || def.MainVerb?.verbClass == typeof(Effect_Flight));

        public static bool CanPay(PawnAbility ability)
            => ability != null && (!IsTravel(ability) || ability.CanCastPowerCheck(
                ability.Pawn?.CurJob?.count == 0 ? AbilityContext.AI : AbilityContext.Player, out _));

        public static bool HasConsent(Pawn pawn, Job job, Map map, IntVec3 cell)
            => job != null && Consents.TryGetValue(job, out var consent)
                && consent.Pawn == pawn && consent.Map == map && consent.Cell == cell;

        public static bool CanComplete(Pawn pawn, Map map, IntVec3 cell, int horizonTicks = 0)
        {
            var result = TravelEnvironmentSafety.Evaluate(pawn, map, cell, horizonTicks);
            return result.IsSafe || ((result.Hazards & TravelEnvironmentHazard.InvalidDestination) == 0
                && HasConsent(pawn, pawn?.CurJob, map, cell));
        }

        public static string Warning(Pawn pawn, TravelEnvironmentResult result)
        {
            var reasons = new List<string>();
            if ((result.Hazards & TravelEnvironmentHazard.Vacuum) != 0) reasons.Add("TM_TravelSafety_Vacuum".Translate());
            if ((result.Hazards & TravelEnvironmentHazard.Cold) != 0) reasons.Add("TM_TravelSafety_Cold".Translate());
            if ((result.Hazards & TravelEnvironmentHazard.Heat) != 0) reasons.Add("TM_TravelSafety_Heat".Translate());
            if (result.ShellExpiring) reasons.Add("TM_TravelSafety_ShellExpiring".Translate());
            return "TM_TravelSafety_Warning".Translate(pawn.LabelShort, string.Join(", ", reasons));
        }

        // UI boundary kept separate from admission for headless/native contract checks.
        public static void Confirm(string warning, Action continuation)
            => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(warning, continuation));

        public static bool Submit(Pawn pawn, Job job, Map map, IntVec3 cell, bool player,
            Func<bool> validate, Action submit, int horizonTicks = 0)
        {
            if (pawn == null || job == null || !validate()) return false;
            var result = TravelEnvironmentSafety.Evaluate(pawn, map, cell, horizonTicks);
            if ((result.Hazards & TravelEnvironmentHazard.InvalidDestination) != 0) return false;
            if (result.IsSafe) { submit(); return true; }
            if (!player || pawn.Faction?.IsPlayer != true) return false;
            bool used = false;
            Confirm(Warning(pawn, result), () =>
            {
                if (used) return;
                used = true;
                if (!validate() || (TravelEnvironmentSafety.Evaluate(pawn, map, cell, horizonTicks).Hazards
                    & TravelEnvironmentHazard.InvalidDestination) != 0) return;
                Consents.Remove(job);
                Consents.Add(job, new TravelConsent { Pawn = pawn, Map = map, Cell = cell });
                submit();
            });
            return false;
        }

        public static Job UseAbility(PawnAbility ability, AbilityContext context, LocalTargetInfo target)
        {
            var pawn = ability.Pawn;
            if (!target.IsValid || !ability.CanCastPowerCheck(context, out _)) return null;
            if (target.HasThing && !ability.CanOverpowerTarget(context, target, out _)) return null;
            if (context == AbilityContext.AI && !Compatibility.AbilityCastSafety.CanAutocast(ability.Verb, target)) return null;
            var map = pawn.Map;
            var job = ability.GetJob(context, target);
            int horizon = IsFlight(ability) ? (int)Math.Ceiling((pawn.Position - target.Cell).LengthHorizontal * 5f) : 0;
            bool submitted = Submit(pawn, job, map, target.Cell, context == AbilityContext.Player,
                () => pawn.Spawned && pawn.Map == map && !pawn.Dead && ability.CanCastPowerCheck(context, out _)
                    && ability.Verb.CanHitTargetFrom(pawn.Position, target),
                () => { if (context == AbilityContext.Player) pawn.jobs.TryTakeOrderedJob(job); }, horizon);
            return submitted ? job : null;
        }

        public static void ExposeConsent(Job job)
        {
            Consents.TryGetValue(job, out var consent);
            Scribe_Deep.Look(ref consent, "TM_travelConsent");
            if (Scribe.mode == LoadSaveMode.LoadingVars && consent != null)
            {
                Consents.Remove(job);
                Consents.Add(job, consent);
            }
        }

        public static void ExposePayment(PawnAbility ability)
        {
            bool tracked = Payments.TryGetValue(ability, out var payment);
            Scribe_Values.Look(ref tracked, "TM_travelPaymentTracked");
            if (Scribe.mode == LoadSaveMode.LoadingVars && tracked) BeginAttempt(ability);
        }
    }

    [HarmonyPatch(typeof(Job), nameof(Job.ExposeData))]
    internal static class TravelConsentSavePatch
    {
        static void Postfix(Job __instance) => ManualTravelSafety.ExposeConsent(__instance);
    }
    [HarmonyPatch(typeof(PawnAbility), nameof(PawnAbility.ExposeData))]
    internal static class TravelPaymentSavePatch
    {
        static void Postfix(PawnAbility __instance) => ManualTravelSafety.ExposePayment(__instance);
    }
}
