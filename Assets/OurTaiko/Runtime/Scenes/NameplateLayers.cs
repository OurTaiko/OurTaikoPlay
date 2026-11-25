using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The layers a NameplateView wears (PlayerInfo's band, effect and icon items, NameplateItems'
    // files). Loose bands and effects use the built-in band mask. Catalog bands contain
    // their own clipping and draw over their source backing on the full plate canvas.
    public sealed class NameplateLayers
    {
        readonly RectTransform plate;
        readonly Image band;
        readonly Transform outline;
        readonly double[] started = new double[NameplateSlots.All.Length];
        bool foregroundIcon;
        RectTransform mask;
        Image maskImage;
        OtanimView bandView, effectView, iconView, backingView;

        // A band item is worn, so the plate's own band is not drawn.
        public bool CoversBand { get; private set; }
        public OtanimView BandView => bandView;
        public OtanimView EffectView => effectView;
        public OtanimView IconView => iconView;
        public RectTransform Mask => mask;

        public NameplateLayers(RectTransform plate, Image band, Transform outline)
        {
            this.plate = plate;
            this.band = band;
            this.outline = outline;
        }

        // Wears `info`'s items; `shown` false (no art, or a plate without a band) wears none.
        public void Apply(PlayerInfo info, bool shown)
        {
            var bandItem = shown ? NameplateItems.Load(NameplateSlot.Band, info.bandItem) : null;
            var effect = shown ? NameplateItems.Load(NameplateSlot.Effect, info.effectItem) : null;
            var icon = shown ? NameplateItems.Load(NameplateSlot.Icon, info.iconItem) : null;
            CoversBand = bandItem != null;
            if (bandItem == null && effect == null && icon == null && mask == null) return;
            Make();
            maskImage.sprite = NameplateArt.Bands.Length > 0 ? NameplateArt.Bands[0] : null;
            var catalog = bandItem != null ? PlateComposition.ReadCatalog() : null;
            bool authoredBand = catalog?.ContainsBand(info.bandItem) == true;
            var parent = authoredBand ? plate : mask;
            if (bandView.transform.parent != parent) bandView.transform.SetParent(parent, false);
            if (!authoredBand) bandView.transform.SetAsFirstSibling();
            if (authoredBand)
            {
                backingView ??= Layer("BandBacking", plate);
                var plates = NameplateItems.CompositionPart("plates");
                bool hasBacking = plates?.File.Find(catalog.BandBackingClip) != null;
                backingView.gameObject.SetActive(hasBacking);
                if (hasBacking)
                {
                    backingView.Use(plates, catalog.BandBackingClip);
                }
            }
            else if (backingView != null) backingView.gameObject.SetActive(false);
            Use(bandView, bandItem, NameplateSlot.Band);
            Use(effectView, effect, NameplateSlot.Effect);
            Use(iconView, icon, NameplateSlot.Icon);
            Place(authoredBand);
            mask.gameObject.SetActive(bandItem != null || effect != null);
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
            var view = slot switch { NameplateSlot.Band => bandView, NameplateSlot.Effect => effectView, NameplateSlot.Icon => iconView, _ => null };
            if (view == null || view.File?.Find(clip) == null) return false;
            view.Use(view.asset, clip);
            started[(int)slot] = seconds;
            if (slot == NameplateSlot.Icon)
            {
                foregroundIcon = foreground;
                PlaceIcon();
            }
            view.Show(0);
            return true;
        }

        void Make()
        {
            if (mask != null) return;
            mask = Child("LayerMask", plate);
            maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            bandView = Layer("BandLayer", mask);
            effectView = Layer("EffectLayer", mask);
            iconView = Layer("IconLayer", plate);
        }

        // The mask where the band is, the layers over the whole plate, in the plate's drawing order.
        void Place(bool authoredBand)
        {
            var bandRect = band.rectTransform;
            mask.anchorMin = bandRect.anchorMin;
            mask.anchorMax = bandRect.anchorMax;
            mask.pivot = bandRect.pivot;
            mask.anchoredPosition = bandRect.anchoredPosition;
            mask.sizeDelta = bandRect.sizeDelta;
            mask.SetSiblingIndex(band.transform.GetSiblingIndex() + 1);
            // The plate's top left corner from the band's, both in the plate's space.
            var plateBox = plate.rect;
            var bandBox = bandRect.rect;
            var bandTopLeft = (Vector2)bandRect.localPosition + new Vector2(bandBox.xMin, bandBox.yMax);
            var offset = new Vector2(plateBox.xMin, plateBox.yMax) - bandTopLeft;
            foreach (var view in new[] { bandView, effectView })
            {
                var rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = offset;
                rect.sizeDelta = plateBox.size;
            }
            if (authoredBand)
            {
                FillPlate(backingView);
                if (backingView.gameObject.activeSelf) backingView.Show(0);
                FillPlate(bandView);
                backingView.transform.SetAsLastSibling();
                bandView.transform.SetAsLastSibling();
                mask.SetAsLastSibling();
                // Above the standard outline, below the badge, rank and text.
                int position = outline.GetSiblingIndex() + 1;
                backingView.transform.SetSiblingIndex(position);
                bandView.transform.SetSiblingIndex(position + 1);
                mask.SetSiblingIndex(position + 2);
            }
            FillPlate(iconView);
            PlaceIcon();
        }

        static void FillPlate(OtanimView view)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        void PlaceIcon()
        {
            if (foregroundIcon) iconView.transform.SetAsLastSibling();
            else
            {
                var anchor = bandView.transform.parent == plate ? (Transform)mask : outline;
                iconView.transform.SetAsLastSibling();
                iconView.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            }
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
