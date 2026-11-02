using AbilityUser;
using HarmonyLib;
using Verse;

namespace TorannMagic
{
    // Ф5 phase Б: when the vanilla tracker provides the bridge gizmo for a
    // spell (family enabled + bridge def exists), the classic Command_Pawn
    // Ability gizmo hides — one gizmo per spell, cast path unchanged (the
    // bridge delegates into the same JecsLite owner).
    [HarmonyPatch(typeof(PawnAbility), nameof(PawnAbility.ShouldShowGizmo))]
    internal static class VanillaBridgeGizmoVisibilityPatch
    {
        static void Postfix(PawnAbility __instance, ref bool __result)
        {
            if (!__result) return;
            if (VanillaBridgeSync.ShouldHideLegacyGizmo(__instance.Def as TMAbilityDef))
            {
                __result = false;
            }
        }
    }
}
