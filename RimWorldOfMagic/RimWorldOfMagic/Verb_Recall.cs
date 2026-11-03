using AbilityUser;

namespace TorannMagic
{
    class Verb_Recall : Verb_UseAbility
    {
        protected override bool TryCastShot()
        {
            burstShotsLeft = 0;
            var pawn = CasterPawn;
            return pawn != null && !pawn.Downed && TM_Action.TryRecall(pawn, pawn.GetCompAbilityUserMagic(), false);
        }
    }
}
