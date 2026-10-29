using RimWorld;
using Verse;

namespace TorannMagic.ClassProgression
{
    // A master tome is never a legacy class-granting book.
    public sealed class CompUseEffect_MasterTome : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
            => ClassInitiation.CanUseMaster(pawn, parent, out _)
                ? (AcceptanceReport)true : (AcceptanceReport)"TM_ProgressionBookUnavailable".Translate();

        public override void DoEffect(Pawn pawn)
        {
            if (!ClassInitiation.CanUseMaster(pawn, parent, out _) || !ClassInitiation.CompleteBook(pawn, parent))
                Messages.Message("TM_ProgressionBookUnavailable".Translate(), pawn, MessageTypeDefOf.RejectInput, false);
        }
    }
}
