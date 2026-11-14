using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace StellarReversi
{
    /// <summary>
    /// 画面の文字・ボタン・スライダーを、TextMeshPro(NotoSansJP の SDF フォント)で描く小さな仕組み。
    /// 毎フレーム Begin() → Fill/Label/Button/Slider … → End() の順に呼ぶ(IMGUI と同じ使い方)。
    /// 座標は「高さ 1080 の仮想画面」で、左上が (0, 0)。横幅は Width(画面の縦横比で変わる)。
    /// uGUI の Canvas を使うが、EventSystem は使わず、クリックは Input System のポインターを直接調べる。
    ///
    /// フォント: Resources/Fonts/NotoSansJP-Regular SDF(TMP_FontAsset)を読み込む。
    ///   Web ビルドでも日本語が出るよう、OS のフォントは一切使わない。
    ///   エディターでは、まだ Resources に移していなくても Assets/Fonts/ の同名アセットを見つけて使う
    ///   (移すのは Assets/Editor/StellarFontSetup.cs が自動でやる)。
    /// </summary>
    public sealed class StellarUi
    {
        public const string FontResource = "Fonts/NotoSansJP-Regular SDF";
        public const float Height = 1080f;

        static readonly Color ButtonColor = new Color(0.07f, 0.07f, 0.22f, 1f);
        static readonly Color ButtonHoverColor = new Color(0.13f, 0.13f, 0.33f, 1f);

        sealed class Item
        {
            public GameObject go;
            public RectTransform rt;
            public Image img;
            public TextMeshProUGUI text;
        }

        readonly List<Item> items = new List<Item>();
        Transform canvasTransform;
        Canvas canvas;
        static float offX, offY;   // 16:9 の表示領域が、画面の左上からどれだけずれているか(仮想画面の単位)
        readonly TMP_FontAsset font;
        readonly Sprite dot;
        int used;
        int activeSlider = -1;
        bool allowPress;

        /// <summary>仮想画面の横幅(高さは 1080 固定)。</summary>
        public float Width { get; private set; } = 1920f;

        /// <summary>ポインター(マウス/タッチ)の位置。仮想画面の座標。</summary>
        public Vector2 PointerPos { get; private set; }

        /// <summary>このフレームに押された。</summary>
        public bool Pressed { get; private set; }

        /// <summary>いま押している。</summary>
        public bool Down { get; private set; }

        /// <summary>このフレームに、スライダーから指を離した。</summary>
        public bool SliderReleased { get; private set; }

        public StellarUi(Transform parent, Sprite dotSprite)
        {
            dot = dotSprite;
            font = EnsureWorking(LoadFont());

            var go = new GameObject("StellarUi", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            canvas = go.AddComponent<Canvas>();
            // Canvas を付けると Transform が RectTransform に置き換わるので、参照は付けたあとに取り直す
            // (付ける前に持っていた Transform は無効になり、子がどこにも属さず描画されなくなる)
            canvasTransform = go.GetComponent<RectTransform>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            // 大きさは、表示領域(16:9)の高さに合わせて、Begin のたびに決める(CanvasScaler は使わない)
        }

        static TMP_FontAsset LoadFont()
        {
            TMP_FontAsset f = Resources.Load<TMP_FontAsset>(FontResource);
#if UNITY_EDITOR
            if (f == null)
            {
                f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansJP-Regular SDF.asset");
            }
#endif
            if (f == null)
            {
                Debug.LogWarning("[星空リバーシ] フォント Resources/" + FontResource + " が見つかりません。" +
                                 "Assets/Fonts/NotoSansJP-Regular SDF.asset を Assets/Resources/Fonts/ に移してください。" +
                                 "(いまは日本語が表示されません)");
                f = TMP_Settings.defaultFontAsset;
            }
            return f;
        }

        // フォントが実際に日本語を描けるか確かめる。描けなければ、同じ TTF から実行時にフォントアセットを作り直して使う
        // (WebGL などで、アセットの動的アトラスがうまく働かなかったときの保険。理由は Console / ブラウザーのコンソールに出る)
        static TMP_FontAsset EnsureWorking(TMP_FontAsset f)
        {
            if (f == null) return null;

            // 1 枚のアトラス(1024x1024)には 100 字ほどしか入らない。足りなくなったら、アトラスを増やして続けられるようにする
            f.isMultiAtlasTexturesEnabled = true;

            // 注意: TryAddCharacters は「全部すでに入っている」ときも false を返す(足りない文字として全部を返す)。
            // 一度でも入ったアセットは再生のたびにそうなるので、HasCharacter(足りなければ追加する) で確かめる。
            bool ok = true;
            string missing = "";
            try
            {
                foreach (char c in "星空リバーシ")
                {
                    if (!f.HasCharacter(c, false, true))
                    {
                        ok = false;
                        missing += c;
                    }
                }
            }
            catch (System.Exception e)
            {
                ok = false;
                Debug.LogWarning("[星空リバーシ] フォントに文字を追加できませんでした: " + e.Message);
            }

            if (ok)
            {
                Debug.Log("[星空リバーシ] フォント " + f.name + " で日本語を描けます(モード: " + f.atlasPopulationMode +
                          ", アトラス: " + f.atlasWidth + "x" + f.atlasHeight + ", 登録済みの文字: " + f.characterTable.Count + ")");
                return f;
            }

            Debug.LogWarning("[星空リバーシ] フォント " + f.name + " では日本語を描けませんでした(足りない文字: " + missing +
                             ", モード: " + f.atlasPopulationMode + ", 元の TTF: " + (f.sourceFontFile != null ? f.sourceFontFile.name : "なし") +
                             ")。アセットの動的アトラスが使えない状態のため、同じ TTF・同じ設定(90pt / 余白9 / SDF)から実行時にフォントを作り直して使います。");
            if (f.sourceFontFile != null)
            {
                try
                {
                    TMP_FontAsset rt = TMP_FontAsset.CreateFontAsset(f.sourceFontFile, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048,
                                                                    AtlasPopulationMode.Dynamic, true);
                    if (rt != null)
                    {
                        rt.name = f.name + " (runtime)";
                        string miss2;
                        bool ok2 = rt.TryAddCharacters("星空リバーシ", out miss2);
                        Debug.Log("[星空リバーシ] 作り直したフォント: 日本語を描ける = " + ok2);
                        if (ok2) return rt;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[星空リバーシ] フォントの作り直しに失敗しました: " + e.Message);
                }
            }
            return f;
        }

        // 使う文字を先に入れておく(動的アトラスなので、初めて出る文字は描くときに追加される。先に入れると、その待ちが無くなる)
        public void Prewarm(string characters)
        {
            if (font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                // 1 枚のアトラスに入る数には限りがある(1024x1024 で 100 字ほど。2048x2048 なら 400 字ほど)ので、重複を除いて入る分だけにする
                int limit = font.isMultiAtlasTexturesEnabled || font.atlasWidth >= 2048 ? 600 : 70;
                var sb = new System.Text.StringBuilder();
                for (char a = ' '; a <= '~'; a++) sb.Append(a);   // 英数字と記号(Console など)
                foreach (char c in characters)
                {
                    if (sb.Length >= limit) break;
                    if (sb.ToString().IndexOf(c) < 0) sb.Append(c);
                }
                string missing;
                font.TryAddCharacters(sb.ToString(), out missing);
            }
        }

        // ------------------------------------------------------------------

        /// <param name="allowPressThisFrame">false のフレームは、ボタンやスライダーが押されたことにしない(画面が切り替わった直後など)。</param>
        public void Begin(bool allowPressThisFrame = true)
        {
            used = 0;
            SliderReleased = false;
            allowPress = allowPressThisFrame;
            Rect view = StellarView.Pixels;
            float scale = Mathf.Max(0.01f, view.height / Height);
            canvas.scaleFactor = scale;
            Width = view.width / scale;
            offX = view.x / scale;
            offY = (Screen.height - view.yMax) / scale;

            Pressed = false;
            Down = false;
            Pointer pointer = Pointer.current;
            if (pointer != null)
            {
                Vector2 p = pointer.position.ReadValue();
                PointerPos = new Vector2((p.x - view.x) / scale, (view.yMax - p.y) / scale);
                Pressed = allowPress && pointer.press.wasPressedThisFrame;
                Down = pointer.press.isPressed;
            }
        }

        public void End()
        {
            for (int i = used; i < items.Count; i++)
            {
                if (items[i].go.activeSelf) items[i].go.SetActive(false);
            }
        }

        Item Next()
        {
            if (used == items.Count)
            {
                var go = new GameObject("Item" + items.Count, typeof(RectTransform));
                go.transform.SetParent(canvasTransform, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var tr = textGo.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = Vector2.zero;
                tr.offsetMax = Vector2.zero;
                var text = textGo.AddComponent<TextMeshProUGUI>();
                text.raycastTarget = false;
                if (font != null) text.font = font;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.richText = false;

                items.Add(new Item { go = go, rt = go.GetComponent<RectTransform>(), img = img, text = text });
            }

            Item it = items[used++];
            if (!it.go.activeSelf) it.go.SetActive(true);
            return it;
        }

        static void Place(Item it, Rect r)
        {
            RectTransform rt = it.rt;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(r.x + offX, -(r.y + offY));
            rt.sizeDelta = new Vector2(r.width, r.height);
        }

        // ------------------------------------------------------------------

        public void Fill(Rect r, Color c)
        {
            Item it = Next();
            Place(it, r);
            it.img.enabled = true;
            it.img.sprite = null;
            it.img.color = c;
            it.text.enabled = false;
        }

        public void Label(Rect r, string text, float size, TextAlignmentOptions align, Color color)
        {
            Item it = Next();
            Place(it, r);
            it.img.enabled = false;
            SetText(it, text, size, align, color);
        }

        // <color=#...> などのタグが使える文字(カットインで、一部だけ色を変えるため)
        public void LabelRich(Rect r, string text, float size, TextAlignmentOptions align, Color color)
        {
            Item it = Next();
            Place(it, r);
            it.img.enabled = false;
            SetText(it, text, size, align, color);
            it.text.richText = true;
        }

        public bool Button(Rect r, string text, float size)
        {
            bool hover = r.Contains(PointerPos);
            Item it = Next();
            Place(it, r);
            it.img.enabled = true;
            it.img.sprite = null;
            it.img.color = hover ? ButtonHoverColor : ButtonColor;
            SetText(it, text, size, TextAlignmentOptions.Center, hover ? Color.white : new Color(246f / 255f, 238f / 255f, 186f / 255f, 1f));
            return hover && Pressed;
        }

        static void SetText(Item it, string text, float size, TextAlignmentOptions align, Color color)
        {
            TextMeshProUGUI t = it.text;
            t.enabled = true;
            t.richText = false;
            if (t.text != text) t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
        }

        void Knob(Rect r, Color c)
        {
            Item it = Next();
            Place(it, r);
            it.img.enabled = true;
            it.img.sprite = dot;
            it.img.color = c;
            it.text.enabled = false;
        }

        /// <summary>スライダー(ゆっくり光る点がつまみ)。0..1。</summary>
        public float Slider(Rect r, float value, Color track, Color knob)
        {
            int id = used;
            var hit = new Rect(r.x - 20f, r.y - 24f, r.width + 40f, r.height + 48f);
            if (Pressed && hit.Contains(PointerPos)) activeSlider = id;
            if (activeSlider == id)
            {
                if (Down)
                {
                    value = Mathf.Clamp01((PointerPos.x - r.x) / r.width);
                }
                else
                {
                    activeSlider = -1;
                    SliderReleased = true;
                }
            }

            float cy = r.y + r.height * 0.5f;
            Fill(new Rect(r.x, cy - 3f, r.width, 6f), new Color(track.r, track.g, track.b, 0.25f));
            Fill(new Rect(r.x, cy - 3f, r.width * value, 6f), new Color(track.r, track.g, track.b, 0.9f));
            float cx = r.x + r.width * value;
            Knob(new Rect(cx - 30f, cy - 30f, 60f, 60f), knob);
            return value;
        }
    }

    /// <summary>
    /// 画面の縦横比が 16:9 でないときに、16:9 の領域を真ん中に置いて、余りを黒くする
    /// (ブラウザの窓の形や解像度が変わっても、Game ビューと同じ見た目にするため)。スマホ(縦長)では、画面いっぱいを使う。
    /// </summary>
    public static class StellarView
    {
        public const float Target = 16f / 9f;

        /// <summary>画面に対する割合(0〜1)で表した、表示領域。</summary>
        public static Rect Normalized
        {
            get
            {
                float sa = (float)Screen.width / Mathf.Max(1, Screen.height);
                if (Application.isMobilePlatform || Mathf.Abs(sa - Target) < 0.002f) return new Rect(0f, 0f, 1f, 1f);
                if (sa > Target)
                {
                    float w = Target / sa;
                    return new Rect((1f - w) * 0.5f, 0f, w, 1f);   // 横に広い窓: 左右が黒
                }
                float h = sa / Target;
                return new Rect(0f, (1f - h) * 0.5f, 1f, h);        // 縦に広い窓: 上下が黒
            }
        }

        /// <summary>ピクセルで表した、表示領域(左下が原点)。</summary>
        public static Rect Pixels
        {
            get
            {
                Rect n = Normalized;
                return new Rect(n.x * Screen.width, n.y * Screen.height, n.width * Screen.width, n.height * Screen.height);
            }
        }
    }
}
