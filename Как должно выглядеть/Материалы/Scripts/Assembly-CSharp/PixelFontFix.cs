using UnityEngine;

public class PixelFontFix : MonoBehaviour
{
	[SerializeField]
	private Font[] _fonts;

	private void Start()
	{
		Font[] fonts = _fonts;
		for (int i = 0; i < fonts.Length; i++)
		{
			fonts[i].material.mainTexture.filterMode = FilterMode.Point;
		}
	}
}
