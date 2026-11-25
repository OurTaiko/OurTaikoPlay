using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The layers a NameplateView wears: the band, effect and icon of a title (TitleParts) or the
    // player's own pick (PlayerInfo's items, NameplateItems' files). A player's loose band and
    // effect are cut to the built-in band shape. The built-in ones know their shape: a band draws
    // on the full plate canvas over its source backing, and an effect is cut to the band only if
    // the catalog says it stays inside (otherwise it is a burst that runs outside it).
    public sealed class NameplateLayers
    {
        readonly RectTransform plate;
        RectTransform maskCanvas;
        Transform iconAnchor;
        readonly double[] started = new double[NameplateSlots.All.Length];
        bool foregroundIcon;
        RectTransform mask;
        OtanimView bandView, effectView, iconView;

        public OtanimView BandView => bandView;
        public OtanimView EffectView => effectView;
        public OtanimView IconView => iconView;
        // The view a slot is drawn by; null until the plate first wears a layer.
        public OtanimView View(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Band => bandView,
            NameplateSlot.Effect => effectView,
            _ => iconView,
        };
        public RectTransform Mask => mask;

        public NameplateLayers(RectTransform plate) => this.plate = plate;

        public void Apply(TitleParts worn)
        {
            var catalog = PlateComposition.ReadCatalog();
            var band = NameplateItems.Load(NameplateSlot.Band, worn.Band);
            var effect = NameplateItems.Load(NameplateSlot.Effect, worn.Effect);
            var icon = NameplateItems.Load(NameplateSlot.Icon, worn.Icon);
            if (band == null && effect == null && icon == null && mask == null) return;
            Make();
            bool loose = band != null && !catalog.Contains(NameplateSlot.Band, worn.Band);
            bool clipped = effect != null && catalog.ClipsEffect(worn.Effect);
            Parent(bandView, loose ? mask : plate);
            if (loose) bandView.transform.SetAsFirstSibling();
            Parent(effectView, clipped ? mask : plate);
            Use(bandView, band, NameplateSlot.Band);
            Use(effectView, effect, NameplateSlot.Effect);
            Use(iconView, icon, NameplateSlot.Icon);
            maskCanvas.gameObject.SetActive(loose || clipped);
        }

        // The layers `seconds` into their clips.
        public void Show(double seconds)
        {
            if (mask == null) return;
            Show(bandView, seconds - started[(int)NameplateSlot.Band]);
            Show(effectView, seconds - started[(int)NameplateSlot.Effect]);
            Show(iconView, seconds - started[(int)NameplateSlot.Icon]);
        }

        void Use(OtanimView view, OtanimAsset item, NameplateSlot slot)
        {
            if (view.asset == item && view.gameObject.activeSelf == (item != null)) return;
            started[(int)slot] = 0;
            if (slot == NameplateSlot.Icon) foregroundIcon = false;
            view.Use(item, item != null ? NameplateItems.ClipOf(item.File)?.Name : null);
            view.gameObject.SetActive(item != null);
        }

        static void Show(OtanimView view, double seconds)
        {
            // An item read again after its file changed is gone until the plate is shown again.
            if (!view.gameObject.activeSelf || view.asset == null) return;
            view.Show(seconds);
        }

        // Restart a named clip on one worn item at the caller's plate time.
        public bool Play(NameplateSlot slot, string clip, double seconds, bool foreground = false)
        {
            var view = View(slot);
            if (view == null || view.File?.Find(clip) == null) return false;
            view.Use(view.asset, clip);
            started[(int)slot] = seconds;
            if (slot == NameplateSlot.Icon)
            {
                foregroundIcon = foreground;
                if (foreground) iconView.transform.SetAsLastSibling();
                else if (iconAnchor != null) iconView.transform.SetSiblingIndex(iconAnchor.GetSiblingIndex() + 1);
            }
            view.Show(0);
            return true;
        }

        void Make()
        {
            if (mask != null) return;
            // Use the neutral band geometry itself as the stencil. Its idle frame is a single UI graphic.
            var stencil = PlateComposition.CreatePicture("Layer mask", plate);
            stencil.Use(Resources.Load<OtanimAsset>("Nameplate/band/lumen-title-wood"), "idle");
            stencil.Show(0);
            maskCanvas = (RectTransform)stencil.transform;
            mask = stencil.GetComponentInChildren<MaskableGraphic>().rectTransform;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            bandView = Layer("BandLayer", mask);
            effectView = Layer("EffectLayer", mask);
            iconView = Layer("IconLayer", plate);
            FillPlate(bandView);
            FillPlate(effectView);
            FillPlate(iconView);
        }

        public void PlaceBand()
        {
            if (mask == null) return;
            if (bandView.transform.parent == plate) bandView.transform.SetAsLastSibling();
            maskCanvas.SetAsLastSibling();
            if (effectView.transform.parent == plate) effectView.transform.SetAsLastSibling();
        }

        public void PlaceIcon()
        {
            if (iconView == null) return;
            iconAnchor = plate.GetChild(plate.childCount - 1);
            iconView.transform.SetAsLastSibling();
        }

        public void FinishOrder()
        {
            if (foregroundIcon && iconView != null) iconView.transform.SetAsLastSibling();
        }

        static void Parent(OtanimView view, Transform parent)
        {
            if (view.transform.parent == parent) return;
            view.transform.SetParent(parent, false);
            FillPlate(view);
        }

        static void FillPlate(OtanimView view)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static OtanimView Layer(string name, Transform parent) => Child(name, parent).gameObject.AddComponent<OtanimView>();
    }
}
