using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The CourseSelect.Enso scene owns the global chrome: the QR chip and the 2P invite. The arcade's
    // timer is not shown (user decision: the simulator does not limit the player's time).
    public sealed class SongSelectOverlayView : MonoBehaviour
    {
        public Image qrChip;
        public Image inviteBubble;
        public TextMeshProUGUI invitePlayer;
        public TextMeshProUGUI inviteMessage;
    }
}
