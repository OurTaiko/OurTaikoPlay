using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The layers a NameplateView wears (PlayerInfo's band, effect and icon items, NameplateItems'
    // files), made under the plate the first time it wears any: the band and the effect inside a
    // mask of the band's shape, just above the plate's own band, and the icon above the outline.
    // Every layer is an OtanimView over the whole plate canvas, so the files' common view puts
    // them where they belong. The mask is the art's first title band, whose shape every band has.
    public sealed class NameplateLayers
    {
        readonly RectTransform plate;
        readonly Image band;
        readonly Transform outline;
        readonly double[] started = new double[NameplateSlots.All.Length];
        bool foregroundIcon;
        RectTransform mask;
        Image maskImage;
        OtanimView bandView, effectView, iconView;

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
            Use(bandView, bandItem, NameplateSlot.Band);
            Use(effectView, effect, NameplateSlot.Effect);
            Use(iconView, icon, NameplateSlot.Icon);
            Place();
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
        void Place()
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
            var iconRect = (RectTransform)iconView.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            PlaceIcon();
        }

        void PlaceIcon()
        {
            if (foregroundIcon) iconView.transform.SetAsLastSibling();
            else iconView.transform.SetSiblingIndex(outline.GetSiblingIndex() + 1);
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
