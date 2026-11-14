#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 画面の文字に使う TextMeshPro フォントを、Resources に置く(Web ビルドでも読み込めるように)。
    /// Assets/Fonts/NotoSansJP-Regular SDF.asset があって、Assets/Resources/Fonts/ にまだ無いときだけ、
    /// 一度だけ移す(AssetDatabase.MoveAsset なので GUID は変わらず、他からの参照も切れない)。
    /// </summary>
    [InitializeOnLoad]
    static class StellarFontSetup
    {
        const string From = "Assets/Fonts/NotoSansJP-Regular SDF.asset";
        const string ToFolder = "Assets/Resources/Fonts";
        const string To = ToFolder + "/NotoSansJP-Regular SDF.asset";

        static StellarFontSetup()
        {
            EditorApplication.delayCall += Move;
        }

        [MenuItem("Tools/Stellar Reversi/フォントを Resources に移す")]
        static void Move()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(To) != null) return;
            if (AssetDatabase.LoadAssetAtPath<Object>(From) == null) return;

            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(ToFolder)) AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

            string error = AssetDatabase.MoveAsset(From, To);
            if (string.IsNullOrEmpty(error))
            {
                Debug.Log("[星空リバーシ] フォントを " + To + " に移しました(Web ビルドでも読み込めます)。");
            }
            else
            {
                Debug.LogWarning("[星空リバーシ] フォントを移せませんでした: " + error);
            }
        }
    }
}
#endif
