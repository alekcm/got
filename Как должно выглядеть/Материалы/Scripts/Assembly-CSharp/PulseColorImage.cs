using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class PulseColorImage : MonoBehaviour
{
	[SerializeField]
	private Image _image;

	[SerializeField]
	private Color _fromColor;

	[SerializeField]
	private Color _toColor;

	private bool isActive;

	private void OnEnable()
	{
		if (!isActive)
		{
			Sequence s = DOTween.Sequence();
			s.Append(_image.DOColor(_fromColor, 0.5f));
			s.Append(_image.DOColor(_toColor, 0.5f)).SetLoops(-1, LoopType.Yoyo);
			isActive = true;
		}
	}
}
