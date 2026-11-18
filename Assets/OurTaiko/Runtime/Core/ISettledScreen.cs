namespace OurTaiko
{
    // A screen with animations of its own says when they are over and nothing of its own is
    // asking the player anything: a result once its scores, crown and messages have all come in,
    // a menu while it is not moving. Whatever comes up over a screen (RewardOverlay) waits for it;
    // a screen that does not implement this is taken as settled.
    public interface ISettledScreen
    {
        bool IsSettled { get; }
    }
}
