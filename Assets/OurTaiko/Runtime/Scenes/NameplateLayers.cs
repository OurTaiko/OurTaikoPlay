using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The layers a NameplateView wears: the band, effect and icon of a title (TitleParts) or the
    // player's own pick (PlayerInfo's items, NameplateItems' files). A player's loose band and
    // effect are cut to the set's plain band (BandShape). The set's own know their shape: a band
    // draws on the full plate canvas over its source backing, and an effect is cut to the band only
    // if the catalog says it stays inside (otherwise it is a burst that runs outside it). Without
    // a set there is no shape to cut to, and the plate wears no layers. The title's text goes
    // between the band and the effect (PlaceBand, then the text, then PlaceEffect): it is printed on
    // the band and the effect sparkles over it, so the band and the effect each have their own cut.
    public sealed class NameplateLayers
    {
        // The band whose shape the mask is: the plain one, which every band covers.
        public const string BandShape = "band/lumen-title-wood";

        readonly RectTransform plate;
        RectTransform bandCut, effectCut;
        Transform iconAnchor;
        readonly double[] started = new double[NameplateSlots.All.Length];
        bool foregroundIcon;
        RectTransform bandMask, effectMask;
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
        // The band's shape a loose band and an effect that stays inside the band are cut to.
        public RectTransform BandMask => bandMask;
        public RectTransform EffectMask => effectMask;

        public NameplateLayers(RectTransform plate) => this.plate = plate;

        // Wears `worn`; `catalog` (the set's) says which items are the set's own and which effects
        // stay inside the band. Null: no set, no layers.
        public void Apply(PlateComposition.Catalog catalog, TitleParts worn)
        {
            if (catalog == null) worn = default;
            var band = NameplateItems.Load(NameplateSlot.Band, worn.Band);
            var effect = NameplateItems.Load(NameplateSlot.Effect, worn.Effect);
            var icon = NameplateItems.Load(NameplateSlot.Icon, worn.Icon);
            if (band == null && effect == null && icon == null && bandMask == null) return;
            if (!Make()) return;
            bool loose = band != null && !catalog.Contains(NameplateSlot.Band, worn.Band);
            bool clipped = effect != null && catalog.ClipsEffect(worn.Effect);
            Parent(bandView, loose ? bandMask : plate);
            Parent(effectView, clipped ? effectMask : plate);
            Use(bandView, band, NameplateSlot.Band);
            Use(effectView, effect, NameplateSlot.Effect);
            Use(iconView, icon, NameplateSlot.Icon);
            bandCut.gameObject.SetActive(loose);
            effectCut.gameObject.SetActive(clipped);
        }

        // The layers `seconds` into their clips.
        public void Show(double seconds)
        {
            if (bandMask == null) return;
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

        // False when the set has no band shape to make the mask of.
        bool Make()
        {
            if (bandMask != null) return true;
            var shape = NameplateItems.Load(NameplateSlot.Band, BandShape);
            if (shape == null) return false;
            bandMask = Cut("Band mask", shape, out bandCut);
            effectMask = Cut("Effect mask", shape, out effectCut);
            bandView = Layer("BandLayer", bandMask);
            effectView = Layer("EffectLayer", effectMask);
            iconView = Layer("IconLayer", plate);
            FillPlate(bandView);
            FillPlate(effectView);
            FillPlate(iconView);
            return true;
        }

        // Use the neutral band geometry itself as the stencil. Its idle frame is a single UI graphic.
        RectTransform Cut(string name, OtanimAsset shape, out RectTransform canvas)
        {
            var stencil = PlateComposition.CreatePicture(name, plate);
            stencil.Use(shape, "idle");
            stencil.Show(0);
            canvas = (RectTransform)stencil.transform;
            var cut = stencil.GetComponentInChildren<MaskableGraphic>().rectTransform;
            cut.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            return cut;
        }

        // The band, on top of what the plate has drawn so far; the title's text goes next.
        public void PlaceBand()
        {
            if (bandMask == null) return;
            if (bandView.transform.parent == plate) bandView.transform.SetAsLastSibling();
            bandCut.SetAsLastSibling();
        }

        // The effect, over the band and the title's text.
        public void PlaceEffect()
        {
            if (effectMask == null) return;
            effectCut.SetAsLastSibling();
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
